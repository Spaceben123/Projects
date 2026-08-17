using UnityEngine;

/// <summary>
/// Shared display styling (marker/dot color and list section header label) for each
/// SpaceObjectCategory. Kept in one place so the Information panel's craft list and its
/// ground-track map always agree on how a category looks.
/// </summary>
public static class SpaceObjectCategoryStyle
{
    static readonly Color InformationSatelliteColor = new Color(0.35f, 0.95f, 0.65f, 1f);
    static readonly Color UnmannedCraftColor = new Color(0.75f, 0.78f, 0.82f, 1f);
    static readonly Color MannedCraftColor = new Color(0.4f, 0.72f, 1f, 1f);
    static readonly Color WeaponColor = new Color(1f, 0.4f, 0.35f, 1f);

    /// <summary>The color used for this category's list dot and ground-track map marker.</summary>
    public static Color GetColor(SpaceObjectCategory category) => category switch
    {
        SpaceObjectCategory.InformationSatellite => InformationSatelliteColor,
        SpaceObjectCategory.UnmannedCraft => UnmannedCraftColor,
        SpaceObjectCategory.MannedCraft => MannedCraftColor,
        SpaceObjectCategory.Weapon => WeaponColor,
        _ => Color.white
    };

    /// <summary>The section header label shown above this category's rows in the craft list.</summary>
    public static string GetHeaderLabel(SpaceObjectCategory category) => category switch
    {
        SpaceObjectCategory.InformationSatellite => "INFORMATION SATELLITES",
        SpaceObjectCategory.UnmannedCraft => "UNMANNED CRAFT",
        SpaceObjectCategory.MannedCraft => "MANNED CRAFT",
        SpaceObjectCategory.Weapon => "WEAPONS",
        _ => "UNKNOWN"
    };
}
