using UnityEngine;

public static class GeoUtils
{
    public const float EarthRadiusUnits  = 10.0f;
    public const float MoonRadiusUnits   = 2.727f;
    public const float MoonDistanceUnits = 301.7f;

    public static Vector3 LatLonToWorld(float latDeg, float lonDeg, float radius)
    {
        float lat = latDeg * Mathf.Deg2Rad;
        float lon = lonDeg * Mathf.Deg2Rad;
        // UVSphere: u=0(left edge)=lon-180, prime meridian(u=0.5) sits on -X axis.
        // x=-cos(lat)*cos(lon), z=-cos(lat)*sin(lon) matches that convention.
        return new Vector3(
            -Mathf.Cos(lat) * Mathf.Cos(lon),
             Mathf.Sin(lat),
            -Mathf.Cos(lat) * Mathf.Sin(lon)
        ) * radius;
    }

    public static (float lat, float lon) WorldToLatLon(Vector3 worldPos, float radius)
    {
        Vector3 n = worldPos / radius;
        float lat = Mathf.Asin(Mathf.Clamp(n.y, -1f, 1f)) * Mathf.Rad2Deg;
        float lon = Mathf.Atan2(-n.z, -n.x) * Mathf.Rad2Deg;
        return (lat, lon);
    }

    public static Vector2 LatLonToUV(float latDeg, float lonDeg)
    {
        float u = (lonDeg + 180f) / 360f;
        float v = 1f - (latDeg + 90f) / 180f;
        return new Vector2(u, v);
    }

    public static float GreatCircleAngle(float lat1, float lon1, float lat2, float lon2)
    {
        float dLat = (lat2 - lat1) * Mathf.Deg2Rad;
        float dLon = (lon2 - lon1) * Mathf.Deg2Rad;
        float a = Mathf.Sin(dLat * 0.5f) * Mathf.Sin(dLat * 0.5f)
                + Mathf.Cos(lat1 * Mathf.Deg2Rad) * Mathf.Cos(lat2 * Mathf.Deg2Rad)
                  * Mathf.Sin(dLon * 0.5f) * Mathf.Sin(dLon * 0.5f);
        return 2f * Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(1f - a));
    }
}
