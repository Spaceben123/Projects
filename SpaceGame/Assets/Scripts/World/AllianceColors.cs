using UnityEngine;

/// <summary>
/// Single shared color palette for <see cref="FactionAlignment"/>, used by both
/// <see cref="CountryBorderRenderer"/> and <see cref="FactionTextureRenderer"/> so border
/// and fill colors can never drift out of sync. Future palette tuning (e.g. this round's
/// NonAligned brightening) only ever needs to touch this one file.
/// </summary>
public static class AllianceColors
{
    static readonly Color32 s_natoColor        = new Color32(64,  128, 242, 255);
    static readonly Color32 s_bricsColor       = new Color32(230, 64,  51,  255);
    // Brightened from (128,128,128) this round: the old mid-gray read as too dark
    // against lit terrain, since NonAligned is the default alignment for most of the
    // world's countries.
    static readonly Color32 s_nonAlignedColor  = new Color32(190, 190, 190, 255);
    static readonly Color32 s_superNationColor = new Color32(217, 230, 38,  255);
    static readonly Color32 s_collapsedColor   = new Color32(31,  15,  15,  255);

    /// <summary>Shared transparent sentinel — used for ocean pixels and hidden (internal) border segments.</summary>
    public static readonly Color32 Clear = new Color32(0, 0, 0, 0);

    /// <summary>Returns the shared palette color for a given faction alignment.</summary>
    public static Color32 ColorFor(FactionAlignment a) => a switch
    {
        FactionAlignment.NATO        => s_natoColor,
        FactionAlignment.BRICS       => s_bricsColor,
        FactionAlignment.NonAligned  => s_nonAlignedColor,
        FactionAlignment.SuperNation => s_superNationColor,
        FactionAlignment.Collapsed   => s_collapsedColor,
        _                            => s_nonAlignedColor,
    };
}
