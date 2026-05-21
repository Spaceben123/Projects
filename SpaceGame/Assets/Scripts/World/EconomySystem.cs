using UnityEngine;

public class EconomySystem : MonoBehaviour
{
    RegionRegistry      _registry;
    ContactStageManager _stages;

    void Start()
    {
        _registry = RegionRegistry.Instance;
        _stages   = ContactStageManager.Instance;
    }

    public float GetGlobalMilitaryBudget()
    {
        if (_registry == null) return 0f;
        float total = 0f;
        foreach (var r in _registry.Regions)
            if (r != null) total += r.GdpTrillion;
        float militaryFraction = Mathf.Lerp(0.02f, 0.12f, _stages?.FearLevel ?? 0f);
        return total * militaryFraction;
    }

    public float GetRegionLaunchCapacity(string regionId)
    {
        var r = _registry?.GetRegion(regionId);
        if (r == null) return 0f;
        float stageMult = Mathf.Lerp(1f, 4f, _stages?.FearLevel ?? 0f);
        return r.GdpTrillion * 0.5f * stageMult;
    }

    public float GetResearchBudget(RegionRuntime region)
    {
        return region.GdpTrillion * 0.03f;
    }

    public void Tick(float simMonthsElapsed)
    {
        if (_registry == null) return;
        int stageInt = (int)_stages.CurrentStage;

        foreach (var r in _registry.Regions)
        {
            if (r == null) continue;
            float targetGdp = SimulationMath.CalcGDP(r.PopulationM, r.Def.baseWealth,
                                                      r.TechLevel, stageInt, r.DamageLevel);
            r.GdpTrillion = Mathf.Lerp(r.GdpTrillion, targetGdp, 0.1f * simMonthsElapsed);
        }
    }
}
