using System;
using System.Collections.Generic;
using UnityEngine;

// Singleton owning all active SurfaceSites and advancing their build state
// each simulated tick. Ticked the same way NationEconomySystem.Tick is, from
// WorldSimulation.cs. Pattern follows RegionRegistry/NationDataRegistry:
// a singleton Instance with the owned collection exposed read-only.
//
// The progression rule below is an intentionally simple placeholder — what a
// site actually costs, produces, or how it escalates contact/military stages
// is a game-design decision left for a later pass (see plan Implementation
// Notes); this class only owns the data/tick plumbing.
public class SurfaceSiteRegistry : MonoBehaviour
{
    public static SurfaceSiteRegistry Instance { get; private set; }

    const float kProgressPerSimSecond = 0.02f; // placeholder build-speed constant

    readonly List<SurfaceSite> _sites = new();

    public IReadOnlyList<SurfaceSite> Sites => _sites;

    /// <summary>Fired whenever a site is created, removed, or completes a build step.</summary>
    public event Action OnSitesChanged;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Creates and registers a new site for ownerCountryIdx at the given lat/lon.</summary>
    public SurfaceSite CreateSite(byte ownerCountryIdx, float lat, float lon, SurfaceSiteType type)
    {
        var site = new SurfaceSite
        {
            ownerCountryIdx = ownerCountryIdx,
            lat             = lat,
            lon             = lon,
            type            = type,
            progress01      = 0f,
            level           = 0,
        };
        _sites.Add(site);
        OnSitesChanged?.Invoke();
        return site;
    }

    public void RemoveSite(SurfaceSite site)
    {
        if (_sites.Remove(site))
            OnSitesChanged?.Invoke();
    }

    /// <summary>Advances every active site's build progress by one simulated tick.</summary>
    public void Tick(float deltaTime, float timeWarpFactor)
    {
        if (_sites.Count == 0) return;

        float simDt   = deltaTime * timeWarpFactor;
        bool  changed = false;
        foreach (var site in _sites)
        {
            site.progress01 = Mathf.Clamp01(site.progress01 + kProgressPerSimSecond * simDt);
            if (site.progress01 >= 1f)
            {
                site.level++;
                site.progress01 = 0f;
                changed = true;
            }
        }

        if (changed) OnSitesChanged?.Invoke();
    }
}
