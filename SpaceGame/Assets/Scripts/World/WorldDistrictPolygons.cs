using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Runtime loader/parser for Resources/WorldPolygons/district_polygons.bytes, the
// simplified vector border data baked offline by WorldRegionBaker ("WPL3"). The
// file holds several LOD levels of the same rings (finest first);
// CountryBorderRenderer builds one mesh set per level and swaps by camera
// distance.
//
// Each segment carries the DISTRICT index on the other side of the line, not a
// country: a segment draws only when the two districts' current owners differ,
// which is what makes internal district lines invisible at peace and makes a
// conquered district's border appear instantly.
//
// Pattern follows WorldRegionMapper: a static class backed by a Resources.Load
// call, same as RegionRegistry/NationDataRegistry/FactionTextureRenderer.
public static class WorldDistrictPolygons
{
    const int    kExpectedMagic = 0x334C5057; // 'WPL3' — written by WorldRegionBaker
    const int    kLegacyMagicV2 = 0x324C5057; // 'WPL2' — country-indexed, superseded
    const int    kLegacyMagicV1 = 0x314C5057; // 'WPL1'
    const string kBakeMenuPath  = "Assets > SpaceGame > Bake Region Map from Shapefile";

    public struct DistrictRing
    {
        public ushort   DistrictIdx;
        public float[]  LatLonFlat;                // flat [lat0, lon0, lat1, lon1, ...]
        public ushort[] SegmentNeighbourDistrict;  // segment i = point i -> point (i+1)%count; 65535 = coastline/no neighbour
    }

    static List<DistrictRing>[] s_ringsByLod;
    static bool s_loaded;
    static bool s_loadSucceeded;

    /// <summary>Number of available detail levels, finest (0) first. 0 when nothing is loaded.</summary>
    public static int LodCount => s_ringsByLod?.Length ?? 0;

    /// <summary>Loads district_polygons.bytes from Resources (once). Safe to call repeatedly.</summary>
    public static bool TryLoad()
    {
        if (s_loaded) return s_loadSucceeded;
        s_loaded = true;

        TextAsset baked = Resources.Load<TextAsset>("WorldPolygons/district_polygons");
        if (baked == null)
        {
            Debug.LogWarning("[WorldDistrictPolygons] district_polygons.bytes not found — " +
                $"run {kBakeMenuPath}. No borders will render.");
            s_ringsByLod = Array.Empty<List<DistrictRing>>();
            return false;
        }

        try
        {
            Parse(baked.bytes);
            s_loadSucceeded = true;
        }
        catch (Exception e)
        {
            Debug.LogError("[WorldDistrictPolygons] Failed to parse district_polygons.bytes: " + e.Message);
            s_ringsByLod    = Array.Empty<List<DistrictRing>>();
            s_loadSucceeded = false;
        }

        return s_loadSucceeded;
    }

    static void Parse(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var br = new BinaryReader(ms);

        int magic = br.ReadInt32();
        if (magic != kExpectedMagic)
            throw new InvalidDataException(
                magic == kLegacyMagicV2 || magic == kLegacyMagicV1
                    ? "district_polygons.bytes is in the old COUNTRY-indexed format " +
                      $"({(magic == kLegacyMagicV2 ? "WPL2" : "WPL1")}). Re-run {kBakeMenuPath} " +
                      "with both the admin-0 and admin-1 shapefiles to regenerate it."
                    : $"district_polygons.bytes has an unrecognised header. Re-run {kBakeMenuPath}.");

        int lodCount = br.ReadInt32();
        s_ringsByLod = new List<DistrictRing>[lodCount];

        var summary = new System.Text.StringBuilder("[WorldDistrictPolygons] Loaded border LODs:");
        for (int lod = 0; lod < lodCount; lod++)
        {
            int ringCount = br.ReadInt32();
            var rings     = new List<DistrictRing>(ringCount);
            int points    = 0;

            for (int r = 0; r < ringCount; r++)
            {
                ushort districtIdx = br.ReadUInt16();
                int    pointCount  = br.ReadInt32();

                float[] flat = new float[pointCount * 2];
                for (int i = 0; i < flat.Length; i++) flat[i] = br.ReadSingle();

                ushort[] neighbour = new ushort[pointCount];
                for (int i = 0; i < pointCount; i++) neighbour[i] = br.ReadUInt16();

                rings.Add(new DistrictRing
                {
                    DistrictIdx              = districtIdx,
                    LatLonFlat               = flat,
                    SegmentNeighbourDistrict = neighbour,
                });
                points += pointCount;
            }

            s_ringsByLod[lod] = rings;
            summary.Append($" [{lod}] {ringCount} rings / {points} pts;");
        }

        Debug.Log(summary.ToString());
    }

    /// <summary>Every ring at one detail level (0 = finest). Empty for an out-of-range level.</summary>
    public static IReadOnlyList<DistrictRing> RingsAtLod(int lod)
        => s_ringsByLod != null && lod >= 0 && lod < s_ringsByLod.Length
            ? s_ringsByLod[lod]
            : Array.Empty<DistrictRing>();
}
