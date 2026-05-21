using UnityEngine;

public class PopulationSystem : MonoBehaviour
{
    RegionRegistry      _registry;
    ContactStageManager _stages;

    void Start()
    {
        _registry = RegionRegistry.Instance;
        _stages   = ContactStageManager.Instance;
    }

    public void Tick(float simDaysElapsed)
    {
        if (_registry == null) return;
        int stageInt = (int)_stages.CurrentStage;

        foreach (var r in _registry.Regions)
        {
            if (r == null) continue;
            float growthPerYear = SimulationMath.PopGrowthRatePerYear(r.TechLevel, stageInt, r.DamageLevel);
            float growthPerDay  = growthPerYear / 365f;
            r.PopulationM = Mathf.Max(0f, r.PopulationM * (1f + growthPerDay * simDaysElapsed));
        }
    }
}
