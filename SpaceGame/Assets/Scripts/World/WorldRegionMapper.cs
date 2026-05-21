using System.Collections.Generic;
using System.Linq;

// Maps ISO 3166-1 alpha-3 codes to country indices (unique, alphabetically stable)
// and to region indices (0-13 macro-regions for faction gameplay).
// Country indices are assigned by sorting ISO3 codes alphabetically, so baked
// region_map.bytes and runtime lookups always agree.
public static class WorldRegionMapper
{
    static readonly (string iso3, byte region)[] s_raw =
    {
        // 0 — north_america
        ("USA",0),("CAN",0),("GRL",0),
        // 1 — c_america
        ("MEX",1),("GTM",1),("BLZ",1),("HND",1),("SLV",1),
        ("NIC",1),("CRI",1),("PAN",1),
        ("CUB",1),("JAM",1),("HTI",1),("DOM",1),
        ("PRI",1),("TTO",1),("BHS",1),("BRB",1),
        ("LCA",1),("VCT",1),("GRD",1),("ATG",1),("KNA",1),("DMA",1),
        ("GLP",1),("MTQ",1),("MAF",1),("BLM",1),
        ("ABW",1),("CUW",1),("SXM",1),("MSR",1),
        ("AIA",1),("VGB",1),("VIR",1),("TCA",1),("CYM",1),("BMU",1),
        // 2 — s_america
        ("COL",2),("VEN",2),("GUY",2),("SUR",2),("GUF",2),
        ("BRA",2),("ECU",2),("PER",2),("BOL",2),
        ("CHL",2),("ARG",2),("URY",2),("PRY",2),("FLK",2),
        // 3 — w_europe
        ("GBR",3),("IRL",3),("ISL",3),
        ("NOR",3),("SWE",3),("FIN",3),("DNK",3),("FRO",3),
        ("NLD",3),("BEL",3),("LUX",3),
        ("FRA",3),("MCO",3),("AND",3),("ESP",3),("PRT",3),
        ("DEU",3),("CHE",3),("AUT",3),("LIE",3),
        ("ITA",3),("SMR",3),("VAT",3),
        ("GRC",3),("CYP",3),("MLT",3),
        // 4 — e_europe
        ("POL",4),("CZE",4),("SVK",4),
        ("HUN",4),("ROU",4),("BGR",4),
        ("MDA",4),("UKR",4),("BLR",4),
        ("LTU",4),("LVA",4),("EST",4),
        ("SVN",4),("HRV",4),("BIH",4),
        ("SRB",4),("MNE",4),("ALB",4),("MKD",4),("XKX",4),
        // 5 — russia
        ("RUS",5),
        // 6 — middle_east
        ("TUR",6),("SYR",6),("LBN",6),
        ("ISR",6),("PSE",6),("JOR",6),
        ("SAU",6),("YEM",6),("OMN",6),
        ("ARE",6),("QAT",6),("BHR",6),("KWT",6),
        ("IRQ",6),("IRN",6),("AFG",6),
        // 7 — n_africa
        ("MAR",7),("DZA",7),("TUN",7),("LBY",7),("EGY",7),
        ("SDN",7),("SSD",7),("ETH",7),("ERI",7),("DJI",7),("SOM",7),
        ("MRT",7),("MLI",7),("NER",7),("TCD",7),("ESH",7),
        // 8 — s_africa
        ("SEN",8),("GMB",8),("GNB",8),("GIN",8),("SLE",8),("LBR",8),
        ("CIV",8),("GHA",8),("TGO",8),("BEN",8),("NGA",8),("CMR",8),
        ("GNQ",8),("GAB",8),("COG",8),("COD",8),("CAF",8),
        ("UGA",8),("KEN",8),("RWA",8),("BDI",8),
        ("TZA",8),("MOZ",8),("MWI",8),
        ("ZMB",8),("ZWE",8),("BWA",8),
        ("NAM",8),("ZAF",8),("LSO",8),("SWZ",8),("AGO",8),
        ("MDG",8),("COM",8),("MUS",8),("SYC",8),("CPV",8),("STP",8),
        // 9 — e_asia
        ("CHN",9),("JPN",9),("KOR",9),("PRK",9),("MNG",9),
        ("TWN",9),("HKG",9),("MAC",9),
        // 10 — s_asia
        ("IND",10),("PAK",10),("BGD",10),
        ("LKA",10),("NPL",10),("BTN",10),("MDV",10),
        // 11 — se_asia
        ("MMR",11),("THA",11),("LAO",11),("VNM",11),("KHM",11),
        ("MYS",11),("SGP",11),("IDN",11),("BRN",11),("PHL",11),("TLS",11),
        // 12 — c_asia
        ("KAZ",12),("UZB",12),("TKM",12),("KGZ",12),("TJK",12),
        ("AZE",12),("ARM",12),("GEO",12),
        // 13 — oceania
        ("AUS",13),("NZL",13),("PNG",13),
        ("SLB",13),("VUT",13),("FJI",13),
        ("WSM",13),("TON",13),("KIR",13),
        ("MHL",13),("FSM",13),("PLW",13),("NRU",13),("TUV",13),
        ("NCL",13),("PYF",13),("COK",13),("NIU",13),
        ("WLF",13),("ASM",13),("GUM",13),("MNP",13),
    };

    static readonly Dictionary<string, byte> s_countryMap = new();
    static readonly Dictionary<string, byte> s_regionMap  = new();
    static readonly byte[] s_regionForCountry;

    static WorldRegionMapper()
    {
        var sorted = s_raw.OrderBy(x => x.iso3).ToArray();
        s_regionForCountry = new byte[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
        {
            s_countryMap[sorted[i].iso3] = (byte)i;
            s_regionMap[sorted[i].iso3]  = sorted[i].region;
            s_regionForCountry[i]        = sorted[i].region;
        }
    }

    // Unique country index (0-N, alphabetically stable). 255 = unknown.
    public static bool TryGetCountryIndex(string iso3, out byte countryIdx)
    {
        if (!string.IsNullOrEmpty(iso3) && s_countryMap.TryGetValue(iso3, out countryIdx))
            return true;
        countryIdx = 255;
        return false;
    }

    // Region index (0-13) for a country index. 255 = unknown.
    public static byte GetRegionForCountry(byte countryIdx)
        => countryIdx < s_regionForCountry.Length ? s_regionForCountry[countryIdx] : (byte)255;

    // Legacy: ISO3 → region index 0-13 (used by tests and other callers).
    public static bool TryGetIndex(string iso3, out byte index)
    {
        if (!string.IsNullOrEmpty(iso3) && s_regionMap.TryGetValue(iso3, out index))
            return true;
        index = 255;
        return false;
    }
}
