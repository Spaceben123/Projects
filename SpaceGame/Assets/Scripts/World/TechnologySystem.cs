using UnityEngine;
using System.Collections.Generic;

public class TechnologySystem : MonoBehaviour
{
    public static TechnologySystem Instance { get; private set; }

    RegionRegistry      _registry;
    ContactStageManager _stages;
    EconomySystem       _economy;

    public event System.Action<string, float> OnMilestoneReached;

    static readonly float[] s_milestones = { 0.3f, 0.6f, 0.8f, 1.0f };
    readonly HashSet<string> _firedMilestones = new HashSet<string>();

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        _registry = RegionRegistry.Instance;
        _stages   = ContactStageManager.Instance;
        _economy  = GetComponent<EconomySystem>();
    }

    public void Tick(float simYearsElapsed)
    {
        if (_registry == null) return;
        int stageInt = (int)_stages.CurrentStage;

        foreach (var r in _registry.Regions)
        {
            if (r == null) continue;
            float researchBudget = _economy?.GetResearchBudget(r) ?? 0f;
            float rate = SimulationMath.TechAdvanceRate(0.03f, researchBudget, stageInt, r.DamageLevel);
            r.TechLevel = Mathf.Clamp01(r.TechLevel + rate * simYearsElapsed);
            CheckMilestones(r);
        }
    }

    void CheckMilestones(RegionRuntime r)
    {
        foreach (float m in s_milestones)
        {
            string key = r.Def.regionId + "_" + m;
            if (!_firedMilestones.Contains(key) && r.TechLevel >= m)
            {
                _firedMilestones.Add(key);
                OnMilestoneReached?.Invoke(r.Def.regionId, m);
                Debug.Log($"[Tech] {r.Def.displayName} reached milestone {m:F1}");
            }
        }
    }
}
