using System;
using System.Collections.Generic;
using UnityEngine;

// Runtime registry of DISTRICT identity and adjacency, loaded from the files
// baked by WorldRegionBaker (district_table.json + district_adjacency.bytes).
//
// A district is the atomic unit of territory: conquest is a single integer write
// (TerritoryController.SetDistrictOwner) and a national border is simply the set
// of district edges whose two sides have different owners. No vector geometry is
// ever mutated at runtime.
//
// District identity is a ushort (0-65534, 65535 = none) because ~800 districts
// do not fit in a byte. A district's OWNER is still a byte country index, so
// WorldRegionMapper / NationDataRegistry / RegionRegistry are untouched.
//
// Pattern follows NationDataRegistry: Resources.Load<TextAsset> → JsonUtility
// into a [Serializable] list wrapper → parallel arrays + dictionary lookups,
// with a graceful failure path. Every accessor is safe when IsLoaded is false,
// so an unbaked project still runs.
public static class WorldDistricts
{
    /// <summary>Ocean / no-district sentinel. Matches the baker's 16-bit raster fill value.</summary>
    public const ushort None = 65535;

    const string kTableResource     = "WorldPolygons/district_table";
    const string kAdjacencyResource = "WorldPolygons/district_adjacency";
    const string kBakeMenuPath      = "Assets > SpaceGame > Bake Region Map from Shapefile";
    const int    kCountryCount      = 256;

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

    static bool s_loadAttempted;
    static bool s_loaded;

    static string[] s_codes       = Array.Empty<string>();
    static string[] s_names       = Array.Empty<string>();
    static string[] s_iso3166_2   = Array.Empty<string>();
    static byte[]   s_parent      = Array.Empty<byte>();

    static readonly Dictionary<string, ushort> s_byCode = new();

    // byte country index → its districts. Always kCountryCount long once loaded.
    static ushort[][] s_districtsOfCountry = Array.Empty<ushort[]>();

    // Adjacency in CSR (compressed sparse row) layout: neighbours of district d
    // are s_adjacency[s_adjacencyOffsets[d] .. s_adjacencyOffsets[d + 1]).
    // A flat pair of arrays instead of ushort[][] avoids ~800 heap allocations.
    static ushort[] s_adjacency        = Array.Empty<ushort>();
    static int[]    s_adjacencyOffsets = Array.Empty<int>();
    // Lazily materialised per-district views, so the IReadOnlyList API costs at
    // most one allocation per district ever queried and none on repeat calls.
    static IReadOnlyList<ushort>[] s_neighbourViews = Array.Empty<IReadOnlyList<ushort>>();

    static readonly ushort[]              s_emptyIndices = Array.Empty<ushort>();
    static readonly IReadOnlyList<ushort> s_emptyView    = Array.Empty<ushort>();

    /// <summary>True when both baked district resources loaded and agree with each other.</summary>
    public static bool IsLoaded
    {
        get { EnsureLoaded(); return s_loaded; }
    }

    /// <summary>Total number of districts. 0 when nothing is loaded.</summary>
    public static int Count
    {
        get { EnsureLoaded(); return s_codes.Length; }
    }

    /// <summary>Parent country index of a district, or 255 when unknown/unloaded.</summary>
    public static byte GetParentCountry(ushort districtIdx)
    {
        EnsureLoaded();
        return districtIdx < s_parent.Length ? s_parent[districtIdx] : (byte)255;
    }

    /// <summary>Human-readable district name (Natural Earth admin-1 name, or the country name). Empty when unknown.</summary>
    public static string GetName(ushort districtIdx)
    {
        EnsureLoaded();
        return districtIdx < s_names.Length ? s_names[districtIdx] : string.Empty;
    }

    /// <summary>Stable Natural Earth adm1_code (or ISO-3 for whole-country districts). Empty when unknown.</summary>
    public static string GetCode(ushort districtIdx)
    {
        EnsureLoaded();
        return districtIdx < s_codes.Length ? s_codes[districtIdx] : string.Empty;
    }

    /// <summary>ISO 3166-2 subdivision code, when Natural Earth supplies one. Empty otherwise.</summary>
    public static string GetIso3166_2(ushort districtIdx)
    {
        EnsureLoaded();
        return districtIdx < s_iso3166_2.Length ? s_iso3166_2[districtIdx] : string.Empty;
    }

    /// <summary>Resolves a baked district code to its index. False (and None) when unknown.</summary>
    public static bool TryGetIndexByCode(string adm1Code, out ushort districtIdx)
    {
        EnsureLoaded();
        if (!string.IsNullOrEmpty(adm1Code) && s_byCode.TryGetValue(adm1Code, out districtIdx))
            return true;
        districtIdx = None;
        return false;
    }

    /// <summary>Every district originally belonging to a country. Empty for an unknown country.</summary>
    public static IReadOnlyList<ushort> DistrictsOfCountry(byte countryIdx)
    {
        EnsureLoaded();
        return countryIdx < s_districtsOfCountry.Length ? s_districtsOfCountry[countryIdx] : s_emptyIndices;
    }

    /// <summary>Districts sharing a border with the given district. Empty when unknown or unloaded.</summary>
    public static IReadOnlyList<ushort> Neighbours(ushort districtIdx)
    {
        EnsureLoaded();
        if (districtIdx >= s_neighbourViews.Length) return s_emptyView;

        IReadOnlyList<ushort> view = s_neighbourViews[districtIdx];
        if (view == null)
        {
            int start = s_adjacencyOffsets[districtIdx];
            int end   = s_adjacencyOffsets[districtIdx + 1];
            var copy  = new ushort[end - start];
            Array.Copy(s_adjacency, start, copy, 0, copy.Length);
            view = copy;
            s_neighbourViews[districtIdx] = view;
        }
        return view;
    }

    /// <summary>Allocation-free neighbour count, for hot gameplay loops.</summary>
    public static int NeighbourCount(ushort districtIdx)
    {
        EnsureLoaded();
        return districtIdx + 1 < s_adjacencyOffsets.Length
            ? s_adjacencyOffsets[districtIdx + 1] - s_adjacencyOffsets[districtIdx]
            : 0;
    }

    /// <summary>Allocation-free neighbour accessor, paired with NeighbourCount. Returns None when out of range.</summary>
    public static ushort GetNeighbour(ushort districtIdx, int neighbourSlot)
    {
        EnsureLoaded();
        if (districtIdx + 1 >= s_adjacencyOffsets.Length) return None;
        int start = s_adjacencyOffsets[districtIdx];
        int end   = s_adjacencyOffsets[districtIdx + 1];
        int at    = start + neighbourSlot;
        return neighbourSlot >= 0 && at < end ? s_adjacency[at] : None;
    }

    /// <summary>Loads the baked district resources once. Safe to call repeatedly and from any thread-free context.</summary>
    public static bool TryLoad()
    {
        EnsureLoaded();
        return s_loaded;
    }

    static void EnsureLoaded()
    {
        if (s_loadAttempted) return;
        s_loadAttempted = true;

        TextAsset table     = Resources.Load<TextAsset>(kTableResource);
        TextAsset adjacency = Resources.Load<TextAsset>(kAdjacencyResource);

        if (table == null || adjacency == null)
        {
            Debug.LogError("[WorldDistricts] Missing baked district data (" +
                           (table == null ? "district_table.json" : "district_adjacency.bytes") +
                           $") — run {kBakeMenuPath}. Territory will be inert.");
            return;
        }

        try
        {
            if (!ParseTable(table.text)) return;
            if (!ParseAdjacency(adjacency.bytes)) return;
        }
        catch (Exception e)
        {
            Debug.LogError($"[WorldDistricts] Failed to parse baked district data: {e.Message} — re-run {kBakeMenuPath}.");
            Reset();
            return;
        }

        s_loaded = true;
        Debug.Log($"[WorldDistricts] Loaded {s_codes.Length} districts, {s_adjacency.Length / 2} undirected adjacency edges.");
    }

    static bool ParseTable(string json)
    {
        var list = JsonUtility.FromJson<DistrictJsonList>(json);
        if (list?.items == null || list.items.Length == 0)
        {
            Debug.LogError($"[WorldDistricts] district_table.json is empty or malformed — re-run {kBakeMenuPath}.");
            return false;
        }

        int n = list.items.Length;
        s_codes     = new string[n];
        s_names     = new string[n];
        s_iso3166_2 = new string[n];
        s_parent    = new byte[n];
        s_byCode.Clear();

        var perCountry = new List<ushort>[kCountryCount];
        for (int d = 0; d < n; d++)
        {
            DistrictJson e = list.items[d];
            s_codes[d]     = e.code     ?? string.Empty;
            s_names[d]     = e.name     ?? string.Empty;
            s_iso3166_2[d] = e.iso3166_2 ?? string.Empty;
            s_parent[d]    = (byte)Mathf.Clamp(e.parentCountryIdx, 0, 255);

            if (!string.IsNullOrEmpty(s_codes[d])) s_byCode[s_codes[d]] = (ushort)d;

            // A byte parent index always fits the 256-entry rollup.
            byte country = s_parent[d];
            (perCountry[country] ??= new List<ushort>(8)).Add((ushort)d);
        }

        s_districtsOfCountry = new ushort[kCountryCount][];
        for (int c = 0; c < kCountryCount; c++)
            s_districtsOfCountry[c] = perCountry[c] != null ? perCountry[c].ToArray() : s_emptyIndices;

        return true;
    }

    static bool ParseAdjacency(byte[] bytes)
    {
        using var ms = new System.IO.MemoryStream(bytes);
        using var br = new System.IO.BinaryReader(ms);

        int districtCount = br.ReadInt32();
        if (districtCount != s_codes.Length)
        {
            Debug.LogError($"[WorldDistricts] district_adjacency.bytes declares {districtCount} districts but " +
                           $"district_table.json has {s_codes.Length} — the two files are from different bakes. " +
                           $"Re-run {kBakeMenuPath} to regenerate all district files together.");
            Reset();
            return false;
        }

        var flat = new List<ushort>(districtCount * 6);
        s_adjacencyOffsets = new int[districtCount + 1];
        for (int d = 0; d < districtCount; d++)
        {
            s_adjacencyOffsets[d] = flat.Count;
            int count = br.ReadUInt16();
            for (int i = 0; i < count; i++) flat.Add(br.ReadUInt16());
        }
        s_adjacencyOffsets[districtCount] = flat.Count;

        s_adjacency      = flat.ToArray();
        s_neighbourViews = new IReadOnlyList<ushort>[districtCount];
        return true;
    }

    static void Reset()
    {
        s_loaded             = false;
        s_codes              = Array.Empty<string>();
        s_names              = Array.Empty<string>();
        s_iso3166_2          = Array.Empty<string>();
        s_parent             = Array.Empty<byte>();
        s_districtsOfCountry = Array.Empty<ushort[]>();
        s_adjacency          = Array.Empty<ushort>();
        s_adjacencyOffsets   = Array.Empty<int>();
        s_neighbourViews     = Array.Empty<IReadOnlyList<ushort>>();
        s_byCode.Clear();
    }
}
