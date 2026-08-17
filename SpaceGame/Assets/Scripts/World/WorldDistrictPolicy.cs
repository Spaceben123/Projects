using System.Collections.Generic;

// Curated list of ISO 3166-1 alpha-3 codes that get subdivided into Natural Earth
// admin-1 districts (states/provinces) by WorldRegionBaker. Every country NOT in
// this list collapses to exactly one indivisible district built from its admin-0
// rings, which keeps the total district count around ~800 instead of the full
// 4,596 admin-1 features.
//
// Grouped in the same macro-region order as WorldRegionMapper.s_raw for
// reviewability. Deliberately standalone (no dependencies) so both the Editor
// baker and the runtime can reference it without an assembly cycle.
//
// WARNING: editing this list renumbers every district index. All four baked
// files (district_map.bytes, district_polygons.bytes, district_table.json,
// district_adjacency.bytes) must be re-baked together afterwards.
public static class WorldDistrictPolicy
{
    static readonly string[] s_subdivided =
    {
        // north_america
        "USA", "CAN",
        // c_america
        "MEX",
        // s_america
        "BRA", "ARG", "COL", "PER", "BOL", "VEN", "CHL",
        // w_europe
        "FRA", "DEU", "GBR", "ESP", "ITA",
        // e_europe
        "POL", "UKR",
        // russia
        "RUS",
        // middle_east
        "TUR", "SAU", "IRN", "AFG",
        // n_africa
        "DZA", "LBY", "EGY", "SDN", "SSD", "TCD", "NER", "MLI", "MRT", "ETH",
        // s_africa
        "COD", "AGO", "ZAF", "TZA", "NGA", "ZMB",
        // e_asia
        "CHN", "JPN", "MNG",
        // s_asia
        "IND", "PAK",
        // se_asia
        "MMR", "IDN",
        // c_asia
        "KAZ",
        // oceania
        "AUS",
    };

    static readonly HashSet<string> s_lookup;

    static WorldDistrictPolicy()
    {
        s_lookup = new HashSet<string>(s_subdivided);
    }

    /// <summary>True when the given ISO-3 country code should be split into admin-1 districts.</summary>
    public static bool ShouldSubdivide(string iso3)
        => !string.IsNullOrEmpty(iso3) && s_lookup.Contains(iso3);

    /// <summary>Every curated ISO-3 code, for bake-time diagnostics.</summary>
    public static IReadOnlyCollection<string> SubdividedCodes => s_lookup;
}
