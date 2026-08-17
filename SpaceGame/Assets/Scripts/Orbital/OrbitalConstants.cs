using UnityEngine;

/// <summary>
/// Pure-static conversion constants and helpers bridging real-world orbital units
/// (kilometers, seconds) and this project's Unity world-space scale. Pattern:
/// SimulationMath.cs - a static math class with no MonoBehaviour.
/// </summary>
public static class OrbitalConstants
{
    /// <summary>Real-world kilometers represented by one Unity unit, derived from Earth's scale-10 radius of 6371 km.</summary>
    public const double KmPerUnit = 637.1;

    public const double EarthMuKm3PerSec2 = 398600.4418;
    public const double MoonMuKm3PerSec2 = 4902.8;
    public const double EarthRadiusKm = 6371.0;
    public const double MoonRadiusKm = 1737.4;

    /// <summary>Altitude (km) of the Karman line, used to decide when reentry effects should trigger.</summary>
    public const double KarmanLineAltitudeKm = 100.0;

    /// <summary>Standard gravity (m/s^2), used by the Tsiolkovsky rocket equation.</summary>
    public const double StandardGravityMPerSec2 = 9.80665;

    /// <summary>Converts a position expressed in kilometers into Unity world-space units. The only place this factor should be applied.</summary>
    public static Vector3 KmToWorld(double xKm, double yKm, double zKm)
    {
        return new Vector3((float)(xKm / KmPerUnit), (float)(yKm / KmPerUnit), (float)(zKm / KmPerUnit));
    }

    /// <summary>Converts a Unity world-space offset vector into kilometers. The only place this factor should be applied.</summary>
    public static Vector3 WorldToKmVector(Vector3 world)
    {
        return new Vector3((float)(world.x * KmPerUnit), (float)(world.y * KmPerUnit), (float)(world.z * KmPerUnit));
    }
}
