using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

// Assets > SpaceGame > Bake Region Map from Shapefile
//
// Download: naturalearthdata.com
//   Cultural → Admin 0 – Countries → 1:50m → without boundary lakes → Shapefile
//   Unzip the .zip, then select the .shp file when prompted.
//
// Saves Assets/Resources/WorldPolygons/region_map.bytes (~2 MB).
// Each pixel stores a COUNTRY index (unique per country, not region index).
// FactionTextureRenderer maps country → region → faction color at runtime.
// Rebake required whenever WorldRegionMapper entries change.
public static class WorldRegionBaker
{
    const int W = 2048;
    const int H = 1024;

    [MenuItem("Assets/SpaceGame/Bake Region Map from Shapefile")]
    static void Run()
    {
        string shpPath = EditorUtility.OpenFilePanel(
            "Select Natural Earth Countries .shp", "", "shp");
        if (string.IsNullOrEmpty(shpPath)) return;

        EditorUtility.DisplayProgressBar("Baking Region Map", "Reading Shapefile…", 0f);
        try
        {
            List<PolyEntry> polys = ParseShapefile(shpPath);

            // Diagnostic: count polygons per region
            int[] regionCounts = new int[14];
            string[] regionNames = { "north_america","c_america","s_america","w_europe","e_europe",
                                     "russia","middle_east","n_africa","s_africa","e_asia",
                                     "s_asia","se_asia","c_asia","oceania" };
            foreach (var p in polys)
            {
                byte r = WorldRegionMapper.GetRegionForCountry(p.countryIdx);
                if (r < 14) regionCounts[r]++;
            }
            var sb = new StringBuilder($"[RegionBaker] {polys.Count} rings total:\n");
            for (int i = 0; i < 14; i++)
                sb.AppendLine($"  [{i}] {regionNames[i]}: {regionCounts[i]} rings");
            Debug.Log(sb.ToString());

            byte[] map = Rasterize(polys);

            EditorUtility.DisplayProgressBar("Baking Region Map", "Saving…", 0.98f);
            Save(map);
            EditorUtility.ClearProgressBar();

            EditorUtility.DisplayDialog("Region Map Baked",
                $"Saved to Resources/WorldPolygons/region_map.bytes\n\n" +
                $"Hit Play — FactionTextureRenderer loads it automatically.", "OK");
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

    struct PolyEntry
    {
        public byte    countryIdx; // unique per country (WorldRegionMapper alphabetical index)
        public float[] ring;       // flat [lat0, lon0, lat1, lon1, …]
    }

    // -------------------------------------------------------------------------
    // Shapefile parser  (.shp geometry + .dbf attributes)
    // -------------------------------------------------------------------------

    static List<PolyEntry> ParseShapefile(string shpPath)
    {
        string dbfPath = Path.ChangeExtension(shpPath, ".dbf");
        if (!File.Exists(dbfPath))
            throw new FileNotFoundException(
                "Expected a .dbf file alongside the .shp:\n" + dbfPath);

        string[] isoCodes = ReadDbfIsoField(dbfPath,
            new[] { "ISO_A3", "ISO_A3_EH", "ADM0_A3", "GU_A3", "SOV_A3" });

        return ReadShpPolygons(shpPath, isoCodes);
    }

    // Returns the best ISO-3 code per record, trying candidate fields in priority order.
    // Per-record fallback handles "-99" values (e.g. France has ISO_A3="-99", ISO_A3_EH="FRA").
    static string[] ReadDbfIsoField(string dbfPath, string[] candidateFields)
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

        // ---- find all candidate fields in priority order (case-insensitive) ----
        var found = new List<(string name, int offset, int length)>();
        foreach (string candidate in candidateFields)
        {
            foreach (var f in fields)
            {
                if (string.Equals(f.name, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(f);
                    break;
                }
            }
        }

        if (found.Count == 0)
            throw new Exception(
                "No ISO field found in DBF. Available: " +
                string.Join(", ", fields.ConvertAll(f => f.name)));

        Debug.Log("[RegionBaker] DBF ISO fields found (priority order): " +
                  string.Join(", ", found.ConvertAll(f => f.name)) +
                  $"\nAll DBF fields: " + string.Join(", ", fields.ConvertAll(f => f.name)));

        // ---- read records — per record pick first non-"-99" candidate value ----
        fs.Seek(headerSize, SeekOrigin.Begin);
        string[] codes = new string[numRecords];
        byte[]   rec   = new byte[recordSize];
        for (int i = 0; i < numRecords; i++)
        {
            int read = 0;
            while (read < recordSize)
                read += fs.Read(rec, read, recordSize - read);

            if (rec[0] == 0x2A) { codes[i] = ""; continue; }

            string best = "";
            foreach (var (_, off, len) in found)
            {
                string val = Encoding.ASCII.GetString(rec, off, len).Trim();
                if (!string.IsNullOrEmpty(val) && val != "-99" && val != "-1")
                {
                    best = val;
                    break;
                }
            }
            codes[i] = best;
        }
        return codes;
    }

    static List<PolyEntry> ReadShpPolygons(string shpPath, string[] isoCodes)
    {
        var result = new List<PolyEntry>();

        using FileStream   fs = File.OpenRead(shpPath);
        using BinaryReader br = new BinaryReader(fs);

        fs.Seek(100, SeekOrigin.Begin); // skip 100-byte file header

        while (fs.Position + 8 <= fs.Length)
        {
            int recNum       = ReadInt32BE(br) - 1;     // 0-based
            int contentBytes = ReadInt32BE(br) * 2;     // 16-bit words → bytes
            long contentEnd  = fs.Position + contentBytes;

            int shapeType = br.ReadInt32();             // LE

            string iso3 = recNum < isoCodes.Length ? isoCodes[recNum] : "";
            WorldRegionMapper.TryGetCountryIndex(iso3, out byte countryIdx);

            // Shape types: 5=Polygon, 15=PolygonZ, 25=PolygonM
            if (countryIdx != 255 &&
                (shapeType == 5 || shapeType == 15 || shapeType == 25))
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

                    result.Add(new PolyEntry { countryIdx = countryIdx, ring = ring });
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
    // Rasteriser — paints each polygon into a 2048×1024 byte array
    // -------------------------------------------------------------------------

    static byte[] Rasterize(List<PolyEntry> polys)
    {
        byte[] map = new byte[W * H];
        for (int i = 0; i < map.Length; i++) map[i] = 255; // 255 = unassigned / ocean

        // Pre-compute pixel bounding boxes
        var boxes = new (int x0, int y0, int x1, int y1)[polys.Count];
        for (int p = 0; p < polys.Count; p++)
            boxes[p] = PixelBBox(polys[p].ring);

        // Sort smallest-bbox first so enclaves (Kaliningrad, etc.) fill before surrounding nation
        int[] order = new int[polys.Count];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            long aA = (long)(boxes[a].x1 - boxes[a].x0) * (boxes[a].y1 - boxes[a].y0);
            long aB = (long)(boxes[b].x1 - boxes[b].x0) * (boxes[b].y1 - boxes[b].y0);
            return aA.CompareTo(aB);
        });

        for (int step = 0; step < order.Length; step++)
        {
            if (step % 30 == 0)
                EditorUtility.DisplayProgressBar("Baking Region Map",
                    $"Rasterising polygon {step + 1} / {order.Length}…",
                    0.15f + 0.80f * step / order.Length);

            int     p          = order[step];
            byte    countryIdx = polys[p].countryIdx;
            float[] ring       = polys[p].ring;
            var (x0, y0, x1, y1) = boxes[p];

            for (int py = y0; py <= y1; py++)
            {
                float lat = -90f + (py + 0.5f) / H * 180f;
                for (int px = x0; px <= x1; px++)
                {
                    int i = py * W + px;
                    if (map[i] != 255) continue;            // already assigned
                    float lon = -180f + (px + 0.5f) / W * 360f;
                    if (PointInRing(lat, lon, ring))
                        map[i] = countryIdx;
                }
            }
        }

        return map;
    }

    static (int x0, int y0, int x1, int y1) PixelBBox(float[] ring)
    {
        float minLat =  90, maxLat = -90;
        float minLon = 180, maxLon = -180;
        for (int i = 0; i < ring.Length; i += 2)
        {
            float lat = ring[i], lon = ring[i + 1];
            if (lat < minLat) minLat = lat; if (lat > maxLat) maxLat = lat;
            if (lon < minLon) minLon = lon; if (lon > maxLon) maxLon = lon;
        }
        return (
            Mathf.Max(0,     Mathf.FloorToInt((minLon + 180f) / 360f * W)),
            Mathf.Max(0,     Mathf.FloorToInt((minLat +  90f) / 180f * H)),
            Mathf.Min(W - 1, Mathf.CeilToInt ((maxLon + 180f) / 360f * W)),
            Mathf.Min(H - 1, Mathf.CeilToInt ((maxLat +  90f) / 180f * H))
        );
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
    // Save
    // -------------------------------------------------------------------------

    static void Save(byte[] data)
    {
        const string dir  = "Assets/Resources/WorldPolygons";
        const string file = dir + "/region_map.bytes";

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllBytes(file, data);
        AssetDatabase.Refresh();
        Debug.Log($"[RegionBaker] Saved {data.Length / 1024} KB → {file}");
    }
}
