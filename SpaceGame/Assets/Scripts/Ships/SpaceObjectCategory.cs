/// <summary>
/// Which right-dock Information panel category a trackable craft or satellite belongs to.
/// Assigned per-object on its SatelliteFlareController; drives both the categorized craft list
/// and the ground-track map's marker color.
/// </summary>
public enum SpaceObjectCategory
{
    InformationSatellite,
    UnmannedCraft,
    MannedCraft,
    Weapon
}
