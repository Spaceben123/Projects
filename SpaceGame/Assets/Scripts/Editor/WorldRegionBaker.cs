using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

// Assets > SpaceGame > Bake Region Map from Shapefile
//
// Download: naturalearthdata.com → Cultural → 1:10m → Shapefile
//   Admin 0 – Countries (without boundary lakes)  → ne_10m_admin_0_countries_lakes.shp
//   Admin 1 – States, Provinces                   → ne_10m_admin_1_states_provinces.shp
// Unzip both, then select the admin-0 .shp when prompted, followed by the
// admin-1 .shp. The second prompt may be CANCELLED — that produces a
// country-granular bake (one district per country), a valid degraded mode.
//
// Territory is DISTRICT-level: a national border is not authored geometry, it is
// the set of district edges whose two sides have different owners. Nothing is
// ever clipped at runtime — conquest is a single integer write and the border
// redraws itself on the next recolour pass.
//
// Saves to Assets/Resources/WorldPolygons/:
//   district_map.bytes       (~16 MB) — per-pixel DISTRICT index raster, 16-bit
//                                       little-endian, 65535 = ocean. Used for
//                                       hit-testing (click-to-select) and the
//                                       alliance-colour fill.
//   district_polygons.bytes           — simplified multi-LOD vector rings +
//                                       per-segment neighbour-DISTRICT tags,
//                                       used by CountryBorderRenderer.
//   district_table.json               — district identity in district-index
//                                       order (code, name, parent country).
//   district_adjacency.bytes          — district neighbour graph, a free
//                                       byproduct of the proximity pass and the
//                                       input a later front-line system needs.
//
// A district's OWNER is a byte country index; only district IDENTITY is 16-bit,
// so WorldRegionMapper / NationDataRegistry / RegionRegistry stay untouched.
//
// Rebake required whenever WorldRegionMapper or WorldDistrictPolicy entries
// change — all four files together, never individually, because district
// indices are a pure function of the sorted (parentCountryIdx, adm1_code) key.
public static class WorldRegionBaker
{
    const int W = 4096;
    const int H = 2048;

    /// <summary>Ocean / no-district sentinel. Matches WorldDistricts.None.</summary>
    const ushort kNoDistrict = 65535;

    /// <summary>Highest assignable district index (65535 is the sentinel).</summary>
    const int kMaxDistricts = 65535;

    // Reference resolution the island-size threshold below was tuned against.
    // Scaled to whatever resolution Rasterize() is actually called at, so the
    // physical (km²) cutoff stays constant even if W/H change above.
    const int kRefW = 2048;
    const int kRefH = 1024;
    // Historically, rings smaller than this many reference-resolution pixels
    // were dropped entirely so remote islands/atolls didn't render as an
    // isolated, disconnected, single-pixel "dot" of colour. Now that
    // Rasterize() supersamples each candidate pixel (kSupersamplePerAxis
    // below) instead of testing only the pixel center, any ring with nonzero
    // area overlapping at least one pixel registers a hit on its own, so a
    // separate area-based rescue is no longer needed — shrunk to 1 (i.e. a
    // no-op filter) rather than removed outright, to keep the scaling-safety
    // net for W/H changes.
    const int kMinIslandRefPixelArea = 1;

    // Supersampling factor per axis used by Rasterize()'s ALL_TOUCHED-style
    // point-in-polygon test: each candidate pixel is tested at a
    // kSupersamplePerAxis x kSupersamplePerAxis grid of subsample points
    // rather than only its center, and counts as a hit for a polygon if ANY
    // subsample point falls inside the ring. This is the standard GDAL
    // ALL_TOUCHED technique for not losing thin slivers/small islands when
    // rasterizing vector polygons to a coarser grid — a single pixel-center
    // test can miss a polygon entirely if the polygon covers part of the
    // pixel but not its exact center. Increases bake time by roughly
    // kSupersamplePerAxis^2 (9x at the default of 3) — acceptable for a
    // one-time offline editor operation.
    const int kSupersamplePerAxis = 3;

    // Coastline/border vector export. Real 1:50m rings can carry tens of
    // thousands of vertices (1:10m rings ~10x that), so every ring is exported
    // at several Douglas-Peucker simplification tolerances — one LOD level per
    // entry below, FINEST FIRST. CountryBorderRenderer builds a mesh set per
    // level and swaps by camera distance, so a zoomed-in view gets true
    // coastline detail while a whole-globe view pays for only a fraction of the
    // vertices.
    static readonly float[] kLodEpsilonDeg =
    {
        0.004f, // ~0.45 km — close zoom, full detail
        0.02f,  // ~2.2 km  — mid zoom
        0.08f,  // ~8.9 km  — whole-globe view
    };

    // Two different districts' ring segments closer than this are treated as
    // the same shared border. Rings tracing the same physical boundary are
    // coincident lines (distance ~0) even when each side subdivides it into a
    // different number of vertices, so this can stay tight — it only needs to
    // absorb coordinate rounding, not vertex-placement differences.
    const float kAdjacencyProximityDeg = 0.02f; // ~2.2 km at the equator
    const float kAdjacencyCellDeg      = 0.25f; // spatial-hash cell size for the proximity search

    // Number of spatial-hash columns spanning the full 360° of longitude. Cell
    // columns are taken modulo this so a query at +179.9° also reaches cells at
    // -180°: without the wrap, every district border sitting against the
    // antimeridian (Russia's Chukotka) is mis-tagged as coastline.
    static readonly int kAdjacencyCellColumns = Mathf.RoundToInt(360f / kAdjacencyCellDeg);

    // DBF attribute candidates, tried per record in priority order so a "-99"
    // placeholder falls through to the next field (France has ISO_A3 = "-99"
    // but ISO_A3_EH = "FRA").
    static readonly string[] kAdmin0IsoFields  = { "ISO_A3", "ISO_A3_EH", "ADM0_A3", "GU_A3", "SOV_A3" };
    static readonly string[] kAdmin0NameFields = { "NAME_EN", "NAME", "ADMIN", "SOVEREIGNT" };
    static readonly string[] kAdmin1ParentFields = { "adm0_a3", "sov_a3", "gu_a3" };
    static readonly string[] kAdmin1CodeFields   = { "adm1_code", "diss_me", "fips" };
    static readonly string[] kAdmin1NameFields   = { "name", "name_en", "gn_name", "woe_name" };
    static readonly string[] kAdmin1Iso2Fields   = { "iso_3166_2", "code_hasc" };

    [MenuItem("Assets/SpaceGame/Bake Region Map from Shapefile")]
    static void Run()
    {
        string admin0Path = EditorUtility.OpenFilePanel(
            "Select Natural Earth ADMIN 0 (countries) .shp", "", "shp");
        if (string.IsNullOrEmpty(admin0Path)) return;

        // Cancelling here is legal: the bake falls back to one district per
        // country, which is useful for A/B testing the district pipeline
        // against the previous country-granular behaviour.
        string admin1Path = EditorUtility.OpenFilePanel(
            "Select Natural Earth ADMIN 1 (states/provinces) .shp — Cancel for country-granular bake", "", "shp");

        EditorUtility.DisplayProgressBar("Baking District Map", "Reading admin-0 shapefile…", 0f);
        try
        {
            List<RawRing> admin0 = ParseAdmin0(admin0Path);

            List<RawRing> admin1 = new List<RawRing>();
            if (!string.IsNullOrEmpty(admin1Path))
            {
                EditorUtility.DisplayProgressBar("Baking District Map", "Reading admin-1 shapefile…", 0.04f);
                admin1 = ParseAdmin1(admin1Path);
            }
            else
            {
                Debug.LogWarning("[RegionBaker] No admin-1 shapefile selected — baking country-granular " +
                                 "districts (one district per country).");
            }

            EditorUtility.DisplayProgressBar("Baking District Map", "Building district table…", 0.07f);
            List<PolyEntry> polys = BuildDistrictTable(admin0, admin1, out DistrictInfo[] districts);

            LogDiagnostics(polys, districts, admin1.Count > 0);

            EditorUtility.DisplayProgressBar("Baking District Map", "Rasterising district fill…", 0.10f);
            ushort[] map = Rasterize(polys, W, H);

            EditorUtility.DisplayProgressBar("Baking District Map", "Detecting shared borders…", 0.55f);
            ushort[][] originalNeighbours = ComputeAdjacency(polys);

            EditorUtility.DisplayProgressBar("Baking District Map", "Simplifying + exporting border vectors…", 0.75f);
            byte[] polygonData = BuildPolygonExport(polys, originalNeighbours);

            EditorUtility.DisplayProgressBar("Baking District Map", "Exporting district table + adjacency…", 0.95f);
            string tableJson    = BuildDistrictTableExport(districts);
            byte[] adjacencyData = BuildAdjacencyExport(polys, originalNeighbours, districts.Length);

            EditorUtility.DisplayProgressBar("Baking District Map", "Saving…", 0.98f);
            Save(ToLittleEndianBytes(map), "district_map.bytes");
            Save(polygonData,              "district_polygons.bytes");
            Save(Encoding.UTF8.GetBytes(tableJson), "district_table.json");
            Save(adjacencyData,            "district_adjacency.bytes");
            EditorUtility.ClearProgressBar();

            EditorUtility.DisplayDialog("District Map Baked",
                $"{districts.Length} districts across {polys.Count} rings.\n\n" +
                $"Saved to Resources/WorldPolygons/:\n" +
                $"  district_map.bytes ({W}x{H}, 16-bit)\n" +
                $"  district_polygons.bytes ({kLodEpsilonDeg.Length} LOD levels, finest " +
                $"{kLodEpsilonDeg[0]}° / coarsest {kLodEpsilonDeg[kLodEpsilonDeg.Length - 1]}°)\n" +
                $"  district_table.json\n" +
                $"  district_adjacency.bytes\n\n" +
                $"Hit Play — WorldDistricts, FactionTextureRenderer (fill) and\n" +
                $"CountryBorderRenderer (borders) load them automatically.", "OK");
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError("[RegionBaker] " + e.Message + "\n" + e.StackTrace);
            EditorUtility.DisplayDialog("Error", e.Message, "OK");
        }
    }

    // -------------------------------------------------------------------------
    // Data
    // -------------------------------------------------------------------------

    // One polygon ring straight out of a shapefile, with the attributes needed
    // to group it into a district. Intermediate only — BuildDistrictTable turns
    // these into PolyEntry + DistrictInfo.
    struct RawRing
    {
        public string  parentIso3; // admin-0 ISO-3 (the join key to country indices)
        public string  code;       // adm1_code, or the ISO-3 for a whole-country district
        public string  name;
        public string  iso3166_2;
        public float[] ring;       // flat [lat0, lon0, lat1, lon1, …]
    }

    struct PolyEntry
    {
        public ushort  districtIdx; // stable district index (see BuildDistrictTable)
        public byte    countryIdx;  // parent country (WorldRegionMapper alphabetical index)
        public float[] ring;        // flat [lat0, lon0, lat1, lon1, …]
    }

    struct DistrictInfo
    {
        public string code;
        public string name;
        public string iso3166_2;
        public string parentIso3;
        public byte   parentCountryIdx;
    }

    // -------------------------------------------------------------------------
    // Shapefile parsing  (.shp geometry + .dbf attributes)
    // -------------------------------------------------------------------------

    static List<RawRing> ParseAdmin0(string shpPath)
    {
        string dbfPath = RequireDbf(shpPath);
        string[][] attrs = ReadDbfFields(dbfPath,
            new[] { kAdmin0IsoFields, kAdmin0NameFields }, "admin-0");

        string[] iso   = attrs[0];
        string[] names = attrs[1];

        var keep = new bool[iso.Length];
        for (int i = 0; i < iso.Length; i++)
            keep[i] = WorldRegionMapper.TryGetCountryIndex(iso[i], out _);

        var rings  = ReadShpRings(shpPath, keep);
        var result = new List<RawRing>(rings.Count);
        foreach (var (record, ring) in rings)
        {
            result.Add(new RawRing
            {
                parentIso3 = iso[record],
                code       = iso[record],
                name       = record < names.Length && !string.IsNullOrEmpty(names[record]) ? names[record] : iso[record],
                iso3166_2  = "",
                ring       = ring,
            });
        }

        Debug.Log($"[RegionBaker] Admin-0: {result.Count} rings kept from {iso.Length} records.");
        return result;
    }

    static List<RawRing> ParseAdmin1(string shpPath)
    {
        string dbfPath = RequireDbf(shpPath);
        string[][] attrs = ReadDbfFields(dbfPath,
            new[] { kAdmin1ParentFields, kAdmin1CodeFields, kAdmin1NameFields, kAdmin1Iso2Fields }, "admin-1");

        string[] parent = attrs[0];
        string[] codes  = attrs[1];
        string[] names  = attrs[2];
        string[] iso2   = attrs[3];

        // Only records whose parent country is both known AND curated for
        // subdivision are worth reading geometry for — everything else collapses
        // to a whole-country district from the admin-0 rings instead.
        var keep = new bool[parent.Length];
        for (int i = 0; i < parent.Length; i++)
            keep[i] = WorldRegionMapper.TryGetCountryIndex(parent[i], out _)
                   && WorldDistrictPolicy.ShouldSubdivide(parent[i])
                   && !string.IsNullOrEmpty(codes[i]);

        var rings  = ReadShpRings(shpPath, keep);
        var result = new List<RawRing>(rings.Count);
        foreach (var (record, ring) in rings)
        {
            result.Add(new RawRing
            {
                parentIso3 = parent[record],
                code       = codes[record],
                name       = record < names.Length && !string.IsNullOrEmpty(names[record]) ? names[record] : codes[record],
                iso3166_2  = record < iso2.Length ? iso2[record] : "",
                ring       = ring,
            });
        }

        Debug.Log($"[RegionBaker] Admin-1: {result.Count} rings kept from {parent.Length} records.");
        return result;
    }

    static string RequireDbf(string shpPath)
    {
        string dbfPath = Path.ChangeExtension(shpPath, ".dbf");
        if (!File.Exists(dbfPath))
            throw new FileNotFoundException(
                "Expected a .dbf file alongside the .shp:\n" + dbfPath);
        return dbfPath;
    }

    // Reads several logical attributes in ONE pass over the DBF. Each entry in
    // candidateGroups is a priority-ordered list of field names for one logical
    // attribute; the returned array holds one string[numRecords] per group.
    // Per-record fallback within a group skips "-99"/"-1" placeholders, which is
    // what makes France resolve from ISO_A3_EH.
    static string[][] ReadDbfFields(string dbfPath, string[][] candidateGroups, string label)
    {
        using FileStream   fs = File.OpenRead(dbfPath);
        using BinaryReader br = new BinaryReader(fs, Encoding.ASCII);

        // ---- header ----
        br.ReadByte();
        br.ReadBytes(3);
        int numRecords = br.ReadInt32();
        int headerSize = br.ReadInt16();
        int recordSize = br.ReadInt16();
        br.ReadBytes(20);

        // ---- field descriptors — trim BOTH null bytes and spaces to handle any DBF variant ----
        var fields      = new List<(string name, int offset, int length)>();
        int fieldOffset = 1;
        while (true)
        {
            byte b = br.ReadByte();
            if (b == 0x0D) break;

            byte[] rest = br.ReadBytes(10);
            byte[] full = new byte[11];
            full[0] = b;
            Buffer.BlockCopy(rest, 0, full, 1, 10);
            string name = Encoding.ASCII.GetString(full).Trim('\0', ' ');

            br.ReadByte();
            br.ReadBytes(4);
            int len = br.ReadByte();
            br.ReadBytes(15);

            fields.Add((name, fieldOffset, len));
            fieldOffset += len;
        }

        string allFields = string.Join(", ", fields.ConvertAll(f => f.name));

        // ---- resolve each group's candidate fields, in priority order ----
        var resolved = new List<(string name, int offset, int length)>[candidateGroups.Length];
        for (int g = 0; g < candidateGroups.Length; g++)
        {
            resolved[g] = new List<(string name, int offset, int length)>();
            foreach (string candidate in candidateGroups[g])
            {
                foreach (var f in fields)
                {
                    if (string.Equals(f.name, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        resolved[g].Add(f);
                        break;
                    }
                }
            }

            // The FIRST group of every call is the country join key and the
            // second (for admin-1) the district code — without either the bake
            // cannot produce stable indices, so fail loudly with the field list.
            if (resolved[g].Count == 0 && g <= 1)
                throw new Exception(
                    $"No field matching [{string.Join(", ", candidateGroups[g])}] found in the {label} DBF.\n" +
                    $"Available fields: {allFields}");
        }

        var summary = new StringBuilder($"[RegionBaker] {label} DBF fields resolved:");
        for (int g = 0; g < resolved.Length; g++)
            summary.Append($" [{g}] {(resolved[g].Count > 0 ? string.Join("/", resolved[g].ConvertAll(f => f.name)) : "(none)")};");
        summary.AppendLine();
        summary.Append("All fields: " + allFields);
        Debug.Log(summary.ToString());

        // ---- read records — per record pick the first non-placeholder value in each group ----
        fs.Seek(headerSize, SeekOrigin.Begin);
        var    values = new string[candidateGroups.Length][];
        for (int g = 0; g < values.Length; g++) values[g] = new string[numRecords];

        byte[] rec = new byte[recordSize];
        for (int i = 0; i < numRecords; i++)
        {
            int read = 0;
            while (read < recordSize)
            {
                int got = fs.Read(rec, read, recordSize - read);
                if (got <= 0) break;
                read += got;
            }

            bool deleted = rec[0] == 0x2A;
            for (int g = 0; g < values.Length; g++)
            {
                string best = "";
                if (!deleted)
                {
                    foreach (var (_, off, len) in resolved[g])
                    {
                        if (off + len > recordSize) continue;
                        string val = Encoding.UTF8.GetString(rec, off, len).Trim().Trim('\0');
                        if (!string.IsNullOrEmpty(val) && val != "-99" && val != "-1")
                        {
                            best = val;
                            break;
                        }
                    }
                }
                values[g][i] = best;
            }
        }

        return values;
    }

    // Reads every polygon ring of the records flagged in keepRecord, returning
    // (record index, flat lat/lon ring) pairs. Geometry parsing is identical for
    // admin-0 and admin-1 — only the DBF field names differ.
    static List<(int record, float[] ring)> ReadShpRings(string shpPath, bool[] keepRecord)
    {
        var result = new List<(int, float[])>();

        using FileStream   fs = File.OpenRead(shpPath);
        using BinaryReader br = new BinaryReader(fs);

        fs.Seek(100, SeekOrigin.Begin); // skip 100-byte file header

        while (fs.Position + 8 <= fs.Length)
        {
            int recNum       = ReadInt32BE(br) - 1;     // 0-based
            int contentBytes = ReadInt32BE(br) * 2;     // 16-bit words → bytes
            long contentEnd  = fs.Position + contentBytes;

            int shapeType = br.ReadInt32();             // LE

            bool keep = recNum >= 0 && recNum < keepRecord.Length && keepRecord[recNum];

            // Shape types: 5=Polygon, 15=PolygonZ, 25=PolygonM
            if (keep && (shapeType == 5 || shapeType == 15 || shapeType == 25))
            {
                br.ReadBytes(32);               // bounding box (4 doubles)
                int numParts  = br.ReadInt32();
                int numPoints = br.ReadInt32();

                int[] parts = new int[numParts];
                for (int p = 0; p < numParts; p++) parts[p] = br.ReadInt32();

                double[] xs = new double[numPoints];
                double[] ys = new double[numPoints];
                for (int p = 0; p < numPoints; p++)
                {
                    xs[p] = br.ReadDouble(); // longitude
                    ys[p] = br.ReadDouble(); // latitude
                }

                for (int p = 0; p < numParts; p++)
                {
                    int start = parts[p];
                    int end   = p + 1 < numParts ? parts[p + 1] : numPoints;
                    int count = end - start;
                    if (count < 3) continue;

                    float[] ring = new float[count * 2];
                    for (int v = 0; v < count; v++)
                    {
                        ring[v * 2]     = (float)ys[start + v]; // lat
                        ring[v * 2 + 1] = (float)xs[start + v]; // lon
                    }

                    result.Add((recNum, ring));
                }
            }

            fs.Seek(contentEnd, SeekOrigin.Begin);
        }

        return result;
    }

    static int ReadInt32BE(BinaryReader br)
    {
        byte[] b = br.ReadBytes(4);
        Array.Reverse(b);
        return BitConverter.ToInt32(b, 0);
    }

    // -------------------------------------------------------------------------
    // District table — merges admin-0 and admin-1 features into the final set.
    //
    // Curated countries (WorldDistrictPolicy) are subdivided into their admin-1
    // features; EVERY other country — and any curated country whose admin-1
    // coverage turns out to be empty — becomes exactly one indivisible district
    // built from its admin-0 rings. Natural Earth's admin-1 coverage is uneven
    // by design, so that fallback is a correctness requirement.
    //
    // Index assignment is a pure function of the (parentCountryIdx, code)
    // ordinal sort, mirroring WorldRegionMapper's alphabetical-stability
    // rationale: a reordering silently invalidates every baked file.
    // -------------------------------------------------------------------------

    static List<PolyEntry> BuildDistrictTable(List<RawRing> admin0, List<RawRing> admin1,
                                              out DistrictInfo[] districts)
    {
        // key → (info, rings)
        var groups          = new Dictionary<string, (DistrictInfo info, List<float[]> rings)>(1024);
        var subdividedCount = new Dictionary<string, int>();

        foreach (var raw in admin1)
        {
            if (!WorldRegionMapper.TryGetCountryIndex(raw.parentIso3, out byte countryIdx)) continue;
            if (!WorldDistrictPolicy.ShouldSubdivide(raw.parentIso3)) continue;

            string key = "A1:" + raw.code;
            AddRingToGroup(groups, key, raw, countryIdx);

            subdividedCount.TryGetValue(raw.parentIso3, out int c);
            subdividedCount[raw.parentIso3] = c + 1;
        }

        foreach (var raw in admin0)
        {
            if (!WorldRegionMapper.TryGetCountryIndex(raw.parentIso3, out byte countryIdx)) continue;
            // Country already covered by surviving admin-1 features.
            if (subdividedCount.ContainsKey(raw.parentIso3)) continue;

            AddRingToGroup(groups, "A0:" + raw.parentIso3, raw, countryIdx);
        }

        // Warn about curated codes that produced nothing — catches typos in
        // WorldDistrictPolicy and Natural Earth attribute drift.
        var missing = new List<string>();
        foreach (string iso3 in WorldDistrictPolicy.SubdividedCodes)
            if (!subdividedCount.ContainsKey(iso3)) missing.Add(iso3);
        if (missing.Count > 0 && admin1.Count > 0)
        {
            missing.Sort(StringComparer.Ordinal);
            Debug.LogWarning($"[RegionBaker] {missing.Count} curated ISO-3 code(s) produced ZERO admin-1 " +
                             $"features and fell back to a single whole-country district: " +
                             string.Join(", ", missing) +
                             "\nCheck WorldDistrictPolicy for typos or Natural Earth adm0_a3 drift.");
        }

        // Stable ordering: parent country index, then district code (ordinal).
        var keys = new List<string>(groups.Keys);
        keys.Sort((a, b) =>
        {
            var ia = groups[a].info;
            var ib = groups[b].info;
            int byCountry = ia.parentCountryIdx.CompareTo(ib.parentCountryIdx);
            return byCountry != 0 ? byCountry : string.CompareOrdinal(ia.code, ib.code);
        });

        if (keys.Count > kMaxDistricts)
            throw new Exception($"{keys.Count} districts exceeds the 16-bit ceiling of {kMaxDistricts}. " +
                                "Trim WorldDistrictPolicy or widen the district index to 32-bit.");

        districts = new DistrictInfo[keys.Count];
        var polys = new List<PolyEntry>(4096);
        for (int d = 0; d < keys.Count; d++)
        {
            var (info, rings) = groups[keys[d]];
            districts[d] = info;
            foreach (float[] ring in rings)
                polys.Add(new PolyEntry
                {
                    districtIdx = (ushort)d,
                    countryIdx  = info.parentCountryIdx,
                    ring        = ring,
                });
        }

        return polys;
    }

    static void AddRingToGroup(Dictionary<string, (DistrictInfo info, List<float[]> rings)> groups,
                               string key, RawRing raw, byte countryIdx)
    {
        if (!groups.TryGetValue(key, out var group))
        {
            group = (new DistrictInfo
            {
                code             = raw.code,
                name             = raw.name,
                iso3166_2        = raw.iso3166_2,
                parentIso3       = raw.parentIso3,
                parentCountryIdx = countryIdx,
            }, new List<float[]>(4));
            groups[key] = group;
        }
        group.rings.Add(raw.ring);
    }

    static void LogDiagnostics(List<PolyEntry> polys, DistrictInfo[] districts, bool hasAdmin1)
    {
        // Rings per macro-region (unchanged diagnostic, still country-keyed).
        int[] regionCounts = new int[14];
        string[] regionNames = { "north_america","c_america","s_america","w_europe","e_europe",
                                 "russia","middle_east","n_africa","s_africa","e_asia",
                                 "s_asia","se_asia","c_asia","oceania" };
        foreach (var p in polys)
        {
            byte r = WorldRegionMapper.GetRegionForCountry(p.countryIdx);
            if (r < 14) regionCounts[r]++;
        }

        var sb = new StringBuilder(
            $"[RegionBaker] {districts.Length} districts / {polys.Count} rings " +
            $"({(hasAdmin1 ? "hybrid admin-0 + admin-1" : "country-granular, no admin-1")}):\n");
        for (int i = 0; i < 14; i++)
            sb.AppendLine($"  [{i}] {regionNames[i]}: {regionCounts[i]} rings");
        Debug.Log(sb.ToString());

        // Districts per country, subdivided nations first and descending.
        var perCountry = new Dictionary<string, int>();
        foreach (var d in districts)
        {
            perCountry.TryGetValue(d.parentIso3, out int c);
            perCountry[d.parentIso3] = c + 1;
        }

        var multi = new List<KeyValuePair<string, int>>();
        int singles = 0;
        foreach (var kv in perCountry)
        {
            if (kv.Value > 1) multi.Add(kv);
            else singles++;
        }
        multi.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value)
                                               : string.CompareOrdinal(a.Key, b.Key));

        var breakdown = new StringBuilder("[RegionBaker] District breakdown per country:\n");
        foreach (var kv in multi) breakdown.AppendLine($"  {kv.Key}: {kv.Value} districts");
        breakdown.AppendLine($"  + {singles} countries with a single whole-country district.");
        Debug.Log(breakdown.ToString());
    }

    // -------------------------------------------------------------------------
    // Rasteriser — paints each polygon into a width×height DISTRICT index array
    // (16-bit, 65535 = ocean). Rings smaller than kMinIslandRefPixelArea
    // (scaled to this resolution) are skipped entirely — see field comment.
    // -------------------------------------------------------------------------

    static ushort[] Rasterize(List<PolyEntry> polys, int width, int height)
    {
        int W = width, H = height;
        ushort[] map = new ushort[W * H];
        for (int i = 0; i < map.Length; i++) map[i] = kNoDistrict;

        int minRingArea = Mathf.Max(1, Mathf.RoundToInt(
            kMinIslandRefPixelArea * ((long)W * H) / (float)(kRefW * kRefH)));

        // Unwrap longitudes for rings that cross the ±180° antimeridian (e.g.
        // Russia's Far East / Chukotka) into a continuous longitude space
        // before doing any bbox or point-in-ring math below — otherwise the
        // raw min/max longitude of such a ring spans nearly the whole globe
        // and PointInRing's crossing-number test misclassifies unrelated
        // pixels as inside the ring.
        var rings = new float[polys.Count][];
        for (int p = 0; p < polys.Count; p++)
        {
            float[] ring = polys[p].ring;
            rings[p] = NeedsLongitudeUnwrap(ring) ? UnwrapRingLongitudes(ring) : ring;
        }

        // Pre-compute pixel bounding boxes (in the same, possibly-unwrapped,
        // longitude space as `rings` — may fall outside [0, W-1] for
        // antimeridian-crossing rings; see WrapPixelX for how columns are
        // brought back into range when writing to `map`).
        var boxes = new (int x0, int y0, int x1, int y1)[polys.Count];
        for (int p = 0; p < polys.Count; p++)
            boxes[p] = PixelBBox(rings[p], W, H);

        // Sort smallest-bbox first so enclaves (Kaliningrad, etc.) fill before surrounding nation
        int[] order = new int[polys.Count];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            long aA = (long)(boxes[a].x1 - boxes[a].x0) * (boxes[a].y1 - boxes[a].y0);
            long aB = (long)(boxes[b].x1 - boxes[b].x0) * (boxes[b].y1 - boxes[b].y0);
            return aA.CompareTo(aB);
        });

        var fillIdx = new List<int>(256);
        for (int step = 0; step < order.Length; step++)
        {
            if (step % 30 == 0)
                EditorUtility.DisplayProgressBar("Baking District Map",
                    $"Rasterising polygon {step + 1} / {order.Length} ({W}x{H})…",
                    0.15f + 0.35f * step / order.Length);

            int     p           = order[step];
            ushort  districtIdx = polys[p].districtIdx;
            float[] ring        = rings[p];
            var (x0, y0, x1, y1) = boxes[p];

            fillIdx.Clear();
            for (int py = y0; py <= y1; py++)
            {
                for (int px = x0; px <= x1; px++)
                {
                    int wpx = WrapPixelX(px, W);
                    int i = py * W + wpx;
                    if (map[i] != kNoDistrict) continue;      // already assigned
                    if (PixelTouchesRing(px, py, W, H, ring))
                        fillIdx.Add(i);
                }
            }

            // Too small to render as anything but a stray pixel — leave unassigned
            // so it either shows nothing (open ocean) or lets the surrounding
            // landmass's own ring claim it instead of a disconnected dot.
            if (fillIdx.Count < minRingArea) continue;

            for (int k = 0; k < fillIdx.Count; k++)
                map[fillIdx[k]] = districtIdx;
        }

        return map;
    }

    // True if a ring's raw longitude span exceeds 180°, meaning it likely
    // crosses the ±180° antimeridian (e.g. Russia's Far East) rather than
    // simply being a wide ring — the majority of rings never trigger this
    // and are left completely untouched by the unwrap step below.
    static bool NeedsLongitudeUnwrap(float[] ring)
    {
        float minLon = 180, maxLon = -180;
        for (int i = 0; i < ring.Length; i += 2)
        {
            float lon = ring[i + 1];
            if (lon < minLon) minLon = lon;
            if (lon > maxLon) maxLon = lon;
        }
        return (maxLon - minLon) > 180f;
    }

    // Standard longitude angle-unwrapping: walks the ring and adds/subtracts
    // 360° to each point as needed so consecutive points never differ by
    // more than 180°, producing a continuous (non-wrapping) longitude
    // sequence for antimeridian-crossing rings. The resulting longitudes may
    // fall outside the normal ±180° range, so callers must use wrap-aware
    // pixel indexing (WrapPixelX) rather than clamping back into [0, W-1].
    static float[] UnwrapRingLongitudes(float[] ring)
    {
        float[] unwrapped = (float[])ring.Clone();
        for (int i = 2; i < unwrapped.Length; i += 2)
        {
            float prevLon = unwrapped[i - 1];
            float lon     = unwrapped[i + 1];
            while (lon - prevLon >  180f) lon -= 360f;
            while (lon - prevLon < -180f) lon += 360f;
            unwrapped[i + 1] = lon;
        }
        return unwrapped;
    }

    // Wraps a pixel column that may fall outside [0, W-1] (produced by an
    // unwrapped ring's bounding box) back into valid texture range.
    static int WrapPixelX(int px, int W) => ((px % W) + W) % W;

    static (int x0, int y0, int x1, int y1) PixelBBox(float[] ring, int W, int H)
    {
        float minLat =  90, maxLat = -90;
        float minLon = float.MaxValue, maxLon = float.MinValue;
        for (int i = 0; i < ring.Length; i += 2)
        {
            float lat = ring[i], lon = ring[i + 1];
            if (lat < minLat) minLat = lat; if (lat > maxLat) maxLat = lat;
            if (lon < minLon) minLon = lon; if (lon > maxLon) maxLon = lon;
        }
        // Longitude columns are intentionally NOT clamped to [0, W-1]: an
        // unwrapped, antimeridian-crossing ring's min/max longitude may fall
        // outside ±180°, and the resulting out-of-range columns are wrapped
        // back into range with WrapPixelX at the point of use instead.
        return (
                         Mathf.FloorToInt((minLon + 180f) / 360f * W),
            Mathf.Max(0, Mathf.FloorToInt((minLat +  90f) / 180f * H)),
                         Mathf.CeilToInt ((maxLon + 180f) / 360f * W),
            Mathf.Min(H - 1, Mathf.CeilToInt ((maxLat +  90f) / 180f * H))
        );
    }

    // ALL_TOUCHED-style supersampled hit test: marks pixel (px, py) as
    // touching `ring` if ANY of its kSupersamplePerAxis x kSupersamplePerAxis
    // subsample points fall inside the ring, not just the pixel center. See
    // kSupersamplePerAxis's field comment for why this matters.
    static bool PixelTouchesRing(int px, int py, int W, int H, float[] ring)
    {
        for (int sy = 0; sy < kSupersamplePerAxis; sy++)
        {
            float fy  = (sy + 0.5f) / kSupersamplePerAxis;
            float lat = -90f + (py + fy) / H * 180f;
            for (int sx = 0; sx < kSupersamplePerAxis; sx++)
            {
                float fx  = (sx + 0.5f) / kSupersamplePerAxis;
                float lon = -180f + (px + fx) / W * 360f;
                if (PointInRing(lat, lon, ring))
                    return true;
            }
        }
        return false;
    }

    static bool PointInRing(float lat, float lon, float[] ring)
    {
        int  n      = ring.Length / 2;
        bool inside = false;
        int  j      = n - 1;
        for (int i = 0; i < n; i++)
        {
            float yi = ring[i * 2], xi = ring[i * 2 + 1];
            float yj = ring[j * 2], xj = ring[j * 2 + 1];
            if ((yi > lat) != (yj > lat) &&
                lon < (xj - xi) * (lat - yi) / (yj - yi) + xi)
                inside = !inside;
            j = i;
        }
        return inside;
    }

    // -------------------------------------------------------------------------
    // Adjacency — detects which segments of two DIFFERENT districts' rings are
    // the same shared border. Keyed on district identity, not country: an
    // internal district edge inside one nation must still be tagged, because it
    // becomes a visible national border the moment the two sides' owners differ.
    //
    // This used to match edges by exact vertex-snap equality (both endpoints of
    // an edge snapping to the same grid cells in both rings), which silently
    // dropped most LONG STRAIGHT borders: where two sides meet along a geodetic
    // straight line (the US/Canada 49th parallel, most Saharan borders), Natural
    // Earth stores that same line with DIFFERENT vertex counts on each side —
    // e.g. USA has one edge spanning 28° of longitude while Canada splits it
    // into three. No edge key matched, so both sides were tagged "coastline" and
    // never drawn.
    //
    // The replacement is geometric instead of combinatorial: a segment is an
    // internal border if sample points along it lie within
    // kAdjacencyProximityDeg of ANY segment belonging to a different district.
    // Two rings tracing the same physical border are coincident lines, so this
    // matches at ~0 distance regardless of how each side chose to subdivide it.
    // A uniform lat/lon spatial hash keeps it near-linear.
    //
    // Returns, per input ring, a ushort[] of length (pointCount) where index i
    // is the neighbour DISTRICT index for the segment from point i to point
    // (i+1)%pointCount, or 65535 if that segment is a coastline (no neighbour).
    // -------------------------------------------------------------------------

    static ushort[][] ComputeAdjacency(List<PolyEntry> polys)
    {
        var result = new ushort[polys.Count][];
        for (int i = 0; i < polys.Count; i++)
        {
            int n = polys[i].ring.Length / 2;
            result[i] = new ushort[n];
            for (int s = 0; s < n; s++) result[i][s] = kNoDistrict;
        }

        var grid = new Dictionary<long, List<(int ring, int seg)>>(1 << 18);
        for (int p = 0; p < polys.Count; p++)
        {
            float[] ring = polys[p].ring;
            int n = ring.Length / 2;
            for (int s = 0; s < n; s++)
            {
                int b = (s + 1) % n;
                InsertSegmentIntoGrid(grid, ring[s * 2], ring[s * 2 + 1], ring[b * 2], ring[b * 2 + 1], p, s);
            }
        }

        var neighborCells = new List<long>(9);
        for (int p = 0; p < polys.Count; p++)
        {
            if (p % 30 == 0)
                EditorUtility.DisplayProgressBar("Baking District Map",
                    $"Detecting shared borders {p + 1} / {polys.Count}…",
                    0.55f + 0.18f * p / polys.Count);

            float[] ring        = polys[p].ring;
            ushort  districtIdx = polys[p].districtIdx;
            int     n           = ring.Length / 2;

            for (int s = 0; s < n; s++)
            {
                int b = (s + 1) % n;
                float aLat = ring[s * 2], aLon = ring[s * 2 + 1];
                float bLat = ring[b * 2], bLon = ring[b * 2 + 1];

                // Three sample points rather than just the midpoint, so a segment
                // that is only partly coincident with the neighbour's line (mixed
                // coast/border runs) still registers.
                ushort found = kNoDistrict;
                for (int t = 1; t <= 3 && found == kNoDistrict; t++)
                {
                    float f    = t * 0.25f;
                    float pLat = aLat + (bLat - aLat) * f;
                    float pLon = aLon + WrapLongitudeDelta(bLon - aLon) * f;
                    found = NearestOtherDistrict(grid, polys, districtIdx, pLat, pLon, neighborCells);
                }

                result[p][s] = found;
            }
        }

        return result;
    }

    // Rasterises a segment's path into the spatial hash so a proximity query
    // anywhere along it finds the segment, however long the segment is. The
    // longitude delta is wrapped first so a segment straddling ±180° is walked
    // the short way around instead of across the whole globe.
    static void InsertSegmentIntoGrid(Dictionary<long, List<(int ring, int seg)>> grid,
                                      float lat0, float lon0, float lat1, float lon1,
                                      int ringIdx, int segIdx)
    {
        float dLat  = lat1 - lat0, dLon = WrapLongitudeDelta(lon1 - lon0);
        float span  = Mathf.Max(Mathf.Abs(dLat), Mathf.Abs(dLon));
        int   steps = Mathf.Max(1, Mathf.CeilToInt(span / (kAdjacencyCellDeg * 0.5f)));

        long lastKey = long.MinValue;
        for (int i = 0; i <= steps; i++)
        {
            float f   = i / (float)steps;
            long  key = CellKey(lat0 + dLat * f, lon0 + dLon * f);
            if (key == lastKey) continue;
            lastKey = key;

            if (!grid.TryGetValue(key, out var list))
            {
                list = new List<(int ring, int seg)>(4);
                grid[key] = list;
            }
            list.Add((ringIdx, segIdx));
        }
    }

    // Returns the district index of the closest segment belonging to a DIFFERENT
    // district within kAdjacencyProximityDeg of (lat, lon), or 65535 if none.
    static ushort NearestOtherDistrict(Dictionary<long, List<(int ring, int seg)>> grid,
                                       List<PolyEntry> polys, ushort selfDistrict,
                                       float lat, float lon, List<long> scratchCells)
    {
        scratchCells.Clear();
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                scratchCells.Add(CellKey(lat + dy * kAdjacencyCellDeg, lon + dx * kAdjacencyCellDeg));

        ushort best     = kNoDistrict;
        float  bestDist = kAdjacencyProximityDeg;

        for (int c = 0; c < scratchCells.Count; c++)
        {
            if (!grid.TryGetValue(scratchCells[c], out var list)) continue;
            for (int k = 0; k < list.Count; k++)
            {
                var (ringIdx, segIdx) = list[k];
                ushort other = polys[ringIdx].districtIdx;
                if (other == selfDistrict) continue;

                float[] ring = polys[ringIdx].ring;
                int n = ring.Length / 2;
                int b = (segIdx + 1) % n;
                float dist = WrappedPerpendicularDistance(lat, lon,
                    ring[segIdx * 2], ring[segIdx * 2 + 1], ring[b * 2], ring[b * 2 + 1]);

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best     = other;
                }
            }
        }

        return best;
    }

    // Cell columns wrap modulo the full 360° span, so a query at +179.9° also
    // reaches segments stored at -180°. Without this, every district border
    // sitting against the antimeridian is mis-tagged as coastline.
    static long CellKey(float lat, float lon)
    {
        long cy = (long)Mathf.FloorToInt((lat + 90f) / kAdjacencyCellDeg);
        long cx = Mathf.FloorToInt((lon + 180f) / kAdjacencyCellDeg);
        cx = ((cx % kAdjacencyCellColumns) + kAdjacencyCellColumns) % kAdjacencyCellColumns;
        return cy * 1000003L + cx;
    }

    /// <summary>Normalises a longitude difference into [-180, 180], so the ±180° seam is not a chasm.</summary>
    static float WrapLongitudeDelta(float dLon)
    {
        while (dLon >  180f) dLon -= 360f;
        while (dLon < -180f) dLon += 360f;
        return dLon;
    }

    // Point-to-segment distance in degrees with wrap-aware longitudes: both
    // endpoints are re-expressed relative to the query point's longitude so a
    // segment on the far side of the antimeridian measures as adjacent rather
    // than 360° away.
    static float WrappedPerpendicularDistance(float lat, float lon,
                                              float aLat, float aLon, float bLat, float bLon)
    {
        float aRel = WrapLongitudeDelta(aLon - lon);
        float bRel = aRel + WrapLongitudeDelta(bLon - aLon);
        return PerpendicularDistance(lat, 0f, aLat, aRel, bLat, bRel);
    }

    // -------------------------------------------------------------------------
    // Douglas-Peucker simplification — reduces a closed ring's vertex count
    // while preserving its visual shape, so CountryBorderRenderer's combined
    // ribbon meshes stay within a reasonable triangle budget even though
    // 1:10m coastline data can carry hundreds of thousands of points. Returns
    // the indices (into the original ring) of the vertices to keep, in order.
    // -------------------------------------------------------------------------

    static List<int> SimplifyRing(float[] ring, float epsilonDeg)
    {
        int n = ring.Length / 2;
        var kept = new List<int>();
        if (n <= 3)
        {
            for (int i = 0; i < n; i++) kept.Add(i);
            return kept;
        }

        // Close the ring explicitly (point n == point 0) so standard
        // open-polyline Douglas-Peucker can run between two fixed anchors.
        var keep = new bool[n + 1];
        keep[0] = true;
        keep[n] = true;
        DouglasPeucker(ring, n, 0, n, epsilonDeg, keep);

        for (int i = 0; i < n; i++) // exclude the duplicated closing point (index n)
            if (keep[i]) kept.Add(i);
        return kept;
    }

    static void DouglasPeucker(float[] ring, int n, int startIdx, int endIdx, float epsilonDeg, bool[] keep)
    {
        if (endIdx <= startIdx + 1) return;

        float startLat = ring[(startIdx % n) * 2], startLon = ring[(startIdx % n) * 2 + 1];
        float endLat   = ring[(endIdx   % n) * 2], endLon   = ring[(endIdx   % n) * 2 + 1];

        float maxDist = 0f;
        int   maxIdx  = -1;
        for (int i = startIdx + 1; i < endIdx; i++)
        {
            float lat = ring[(i % n) * 2], lon = ring[(i % n) * 2 + 1];
            float dist = PerpendicularDistance(lat, lon, startLat, startLon, endLat, endLon);
            if (dist > maxDist) { maxDist = dist; maxIdx = i; }
        }

        if (maxIdx >= 0 && maxDist > epsilonDeg)
        {
            keep[maxIdx % n] = true;
            DouglasPeucker(ring, n, startIdx, maxIdx, epsilonDeg, keep);
            DouglasPeucker(ring, n, maxIdx, endIdx, epsilonDeg, keep);
        }
    }

    static float PerpendicularDistance(float lat, float lon, float aLat, float aLon, float bLat, float bLon)
    {
        float dx = bLon - aLon, dy = bLat - aLat;
        float lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-12f)
        {
            float ddx0 = lon - aLon, ddy0 = lat - aLat;
            return Mathf.Sqrt(ddx0 * ddx0 + ddy0 * ddy0);
        }

        float t = ((lon - aLon) * dx + (lat - aLat) * dy) / lenSq;
        t = Mathf.Clamp01(t);
        float projLon = aLon + t * dx;
        float projLat = aLat + t * dy;
        float ddx = lon - projLon, ddy = lat - projLat;
        return Mathf.Sqrt(ddx * ddx + ddy * ddy);
    }

    // -------------------------------------------------------------------------
    // Vector export — writes every ring at each LOD tolerance in
    // kLodEpsilonDeg, re-deriving each simplified segment's neighbour-DISTRICT
    // tag from the original segments it replaces.
    //
    // Format ("WPL3"):
    //   int   magic
    //   int   lodCount
    //   per LOD:  int ringCount
    //             per ring: ushort districtIdx, int pointCount,
    //                       float[pointCount*2] lat/lon,
    //                       ushort[pointCount]  neighbour district per segment
    //
    // Rings with no internal border at all (pure coastline islands) are skipped
    // entirely: CountryBorderRenderer only draws district-to-district segments,
    // so exporting them would just bloat the file. Re-add them here if a future
    // system needs full coastline geometry (e.g. a vector district fill).
    // -------------------------------------------------------------------------

    const int kPolygonFormatMagic = 0x334C5057; // 'WPL3'

    static byte[] BuildPolygonExport(List<PolyEntry> polys, ushort[][] originalNeighbours)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(kPolygonFormatMagic);
        bw.Write(kLodEpsilonDeg.Length);

        for (int lod = 0; lod < kLodEpsilonDeg.Length; lod++)
        {
            EditorUtility.DisplayProgressBar("Baking District Map",
                $"Simplifying border vectors — LOD {lod} ({kLodEpsilonDeg[lod]}°)…",
                0.75f + 0.2f * lod / kLodEpsilonDeg.Length);

            var lodRings = new List<(ushort districtIdx, float[] pts, ushort[] neighbours)>();

            for (int p = 0; p < polys.Count; p++)
            {
                float[]  ring     = polys[p].ring;
                ushort[] neighbor = originalNeighbours[p];
                int      n        = ring.Length / 2;

                List<int> kept = SimplifyRing(ring, kLodEpsilonDeg[lod]);
                if (kept.Count < 3) continue;

                int  simCount    = kept.Count;
                var  simNeighbor = new ushort[simCount];
                bool anyInternal = false;
                for (int i = 0; i < simCount; i++)
                {
                    int a = kept[i];
                    int b = kept[(i + 1) % simCount];
                    simNeighbor[i] = DominantNeighbour(neighbor, n, a, b);
                    if (simNeighbor[i] != kNoDistrict) anyInternal = true;
                }

                if (!anyInternal) continue;

                var pts = new float[simCount * 2];
                for (int i = 0; i < simCount; i++)
                {
                    pts[i * 2]     = ring[kept[i] * 2];
                    pts[i * 2 + 1] = ring[kept[i] * 2 + 1];
                }

                lodRings.Add((polys[p].districtIdx, pts, simNeighbor));
            }

            bw.Write(lodRings.Count);
            foreach (var (districtIdx, pts, neighbours) in lodRings)
            {
                bw.Write(districtIdx);
                bw.Write(neighbours.Length);
                for (int i = 0; i < pts.Length; i++) bw.Write(pts[i]);
                for (int i = 0; i < neighbours.Length; i++) bw.Write(neighbours[i]);
            }

            Debug.Log($"[RegionBaker] LOD {lod} ({kLodEpsilonDeg[lod]}°): {lodRings.Count} rings with internal borders.");
        }

        bw.Flush();
        return ms.ToArray();
    }

    // Walks the ORIGINAL segments spanned by one simplified segment (from
    // kept-index a to kept-index b, wrapping around the ring) and returns the
    // first non-coastline neighbour tag found among them — i.e. the simplified
    // segment still counts as an internal border if any original sub-segment it
    // replaces bordered another district.
    static ushort DominantNeighbour(ushort[] neighbor, int n, int a, int b)
    {
        int i = a;
        while (i != b)
        {
            if (neighbor[i] != kNoDistrict) return neighbor[i];
            i = (i + 1) % n;
        }
        return kNoDistrict;
    }

    // -------------------------------------------------------------------------
    // District table export — district_table.json, in district-index order.
    // JSON rather than binary because it is small (~800 entries), human
    // inspectable, and reuses the JsonUtility pattern NationDataRegistry and
    // RegionDefinitionList already rely on.
    // -------------------------------------------------------------------------

    [Serializable]
    class DistrictJson
    {
        public string code;
        public string name;
        public string iso3166_2;
        public string parentIso3;
        public int    parentCountryIdx;
    }

    [Serializable]
    class DistrictJsonList
    {
        public DistrictJson[] items;
    }

    static string BuildDistrictTableExport(DistrictInfo[] districts)
    {
        var list = new DistrictJsonList { items = new DistrictJson[districts.Length] };
        for (int d = 0; d < districts.Length; d++)
        {
            list.items[d] = new DistrictJson
            {
                code             = districts[d].code,
                name             = districts[d].name,
                iso3166_2        = districts[d].iso3166_2,
                parentIso3       = districts[d].parentIso3,
                parentCountryIdx = districts[d].parentCountryIdx,
            };
        }
        return JsonUtility.ToJson(list, true);
    }

    // -------------------------------------------------------------------------
    // Adjacency export — district_adjacency.bytes, a free byproduct of the
    // proximity pass and the foundation a later front-line/invasion system
    // consumes.
    //
    // Format:
    //   int districtCount
    //   per district: ushort neighbourCount, ushort[neighbourCount] neighbours
    // -------------------------------------------------------------------------

    static byte[] BuildAdjacencyExport(List<PolyEntry> polys, ushort[][] originalNeighbours, int districtCount)
    {
        var sets = new HashSet<ushort>[districtCount];
        for (int d = 0; d < districtCount; d++) sets[d] = new HashSet<ushort>();

        for (int p = 0; p < polys.Count; p++)
        {
            ushort self = polys[p].districtIdx;
            ushort[] tags = originalNeighbours[p];
            for (int s = 0; s < tags.Length; s++)
            {
                ushort other = tags[s];
                if (other == kNoDistrict || other == self) continue;
                if (self >= districtCount || other >= districtCount) continue;
                // Symmetric by construction, but tagged from one side only when
                // the two rings subdivide the shared line differently.
                sets[self].Add(other);
                sets[other].Add(self);
            }
        }

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(districtCount);

        long totalEdges = 0;
        for (int d = 0; d < districtCount; d++)
        {
            var neighbours = new List<ushort>(sets[d]);
            neighbours.Sort();
            bw.Write((ushort)neighbours.Count);
            for (int i = 0; i < neighbours.Count; i++) bw.Write(neighbours[i]);
            totalEdges += neighbours.Count;
        }

        bw.Flush();
        Debug.Log($"[RegionBaker] District adjacency: {totalEdges / 2} undirected edges across {districtCount} districts.");
        return ms.ToArray();
    }

    // -------------------------------------------------------------------------
    // Save
    // -------------------------------------------------------------------------

    // Explicit little-endian pair writing rather than Buffer.BlockCopy, so the
    // on-disk layout is independent of the baking machine's endianness.
    static byte[] ToLittleEndianBytes(ushort[] data)
    {
        var bytes = new byte[data.Length * 2];
        for (int i = 0; i < data.Length; i++)
        {
            bytes[i * 2]     = (byte)(data[i] & 0xFF);
            bytes[i * 2 + 1] = (byte)(data[i] >> 8);
        }
        return bytes;
    }

    static void Save(byte[] data, string fileName)
    {
        const string dir  = "Assets/Resources/WorldPolygons";
        string file = dir + "/" + fileName;

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllBytes(file, data);
        AssetDatabase.Refresh();
        Debug.Log($"[RegionBaker] Saved {data.Length / 1024} KB → {file}");
    }
}
