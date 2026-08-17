using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a body (Earth, Moon, ...) that exerts Newtonian gravity on orbiting objects.
/// Every enabled instance is tracked in the static <see cref="All"/> registry, which the
/// numeric integrator sums over - adding a future third body (e.g. the Sun) needs no
/// integrator changes.
/// </summary>
public class GravitySource : MonoBehaviour
{
    [Header("Physical Properties")]
    [SerializeField] double _muKm3PerSec2 = OrbitalConstants.EarthMuKm3PerSec2;
    [SerializeField] double _radiusKm = OrbitalConstants.EarthRadiusKm;

    static readonly List<GravitySource> s_all = new List<GravitySource>();

    /// <summary>All currently enabled gravity sources in the scene.</summary>
    public static IReadOnlyList<GravitySource> All => s_all;

    /// <summary>Standard gravitational parameter (mu = G*M), in km^3/s^2.</summary>
    public double MuKm3PerSec2 => _muKm3PerSec2;

    /// <summary>Physical radius of this body, in kilometers.</summary>
    public double RadiusKm => _radiusKm;

    /// <summary>This body's current position, converted from world space into kilometers.</summary>
    public Vector3 PositionKm()
    {
        return OrbitalConstants.WorldToKmVector(transform.position);
    }

    void OnEnable()
    {
        if (!s_all.Contains(this))
            s_all.Add(this);
    }

    void OnDisable()
    {
        s_all.Remove(this);
    }
}
