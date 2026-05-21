using UnityEngine;
using System.Collections.Generic;

public enum ContactStage
{
    Undetected       = 1,
    PassiveDetect    = 2,
    NonAggressive    = 3,
    ReactiveUnmanned = 4,
    ReactiveCrewed   = 5,
    CounterAttack    = 6,
    Indiscriminate   = 7,
    Collapse         = 8
}

public class ContactStageManager : MonoBehaviour
{
    public static ContactStageManager Instance { get; private set; }

    [Header("State")]
    [SerializeField] ContactStage _stage = ContactStage.Undetected;
    [SerializeField] float        _fearLevel;
    [SerializeField] bool         _hibernating;

    public ContactStage CurrentStage  => _stage;
    public float        FearLevel     => _fearLevel;
    public bool         IsHibernating => _hibernating;

    ContactStage _permanentMinStage = ContactStage.Undetected;

    readonly Dictionary<string, int> _repetitionCounts = new Dictionary<string, int>();

    public event System.Action<ContactStage, ContactStage> OnStageChanged;
    public event System.Action<RegionRuntime>              OnFactionAttack;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        if (_hibernating)
            _fearLevel = Mathf.MoveTowards(_fearLevel, 0f, SimulationMath.FearDecayPerSec * Time.deltaTime);

        TryAutoTransition();
        RollSpontaneousAttacks();
    }

    public void RegisterShipAction(string actionType, float baseFear,
                                   bool witnessedByMillions, float populationDensity01)
    {
        if (!_repetitionCounts.ContainsKey(actionType))
            _repetitionCounts[actionType] = 0;
        _repetitionCounts[actionType]++;

        float delta = SimulationMath.FearDelta(baseFear, witnessedByMillions,
                                               populationDensity01,
                                               _repetitionCounts[actionType]);
        _fearLevel = Mathf.Clamp01(_fearLevel + delta);
    }

    public void RegisterDestruction(bool crewed)
    {
        ContactStage next = crewed ? ContactStage.ReactiveCrewed : ContactStage.ReactiveUnmanned;
        if ((int)next > (int)_stage)
            SetStage(next);
    }

    public void SetHibernating(bool hibernate) => _hibernating = hibernate;

    public void TriggerCollapse()
    {
        SetStage(ContactStage.Collapse);
        RegionRegistry.Instance?.CollapseAll();
    }

    void TryAutoTransition()
    {
        int nextInt = (int)_stage + 1;
        if (nextInt >= SimulationMath.StageThresholds.Length) return;

        float threshold = SimulationMath.StageThresholds[nextInt];
        if (threshold > 0f && _fearLevel >= threshold)
        {
            ContactStage next = (ContactStage)nextInt;
            if (next == ContactStage.Collapse) return;
            SetStage(next);
        }

        if (_hibernating && (int)_stage > (int)_permanentMinStage)
        {
            float lower = SimulationMath.StageThresholds[(int)_stage];
            if (_fearLevel < lower * 0.7f)
                SetStage((ContactStage)((int)_stage - 1));
        }

        if (_stage == ContactStage.Indiscriminate)
            RegionRegistry.Instance?.MergeAllToSuperNation();
    }

    void RollSpontaneousAttacks()
    {
        if (_stage < ContactStage.NonAggressive) return;
        if (RegionRegistry.Instance == null) return;

        foreach (var region in RegionRegistry.Instance.Regions)
        {
            if (region == null) continue;
            float p = SimulationMath.SpontaneousAttackProbPerSec(
                region.Def.hawkishness, _fearLevel, 1) * Time.deltaTime;
            if (Random.value < p)
                OnFactionAttack?.Invoke(region);
        }
    }

    void SetStage(ContactStage newStage)
    {
        if (newStage == _stage) return;
        ContactStage old = _stage;
        _stage = newStage;
        if ((int)newStage > (int)_permanentMinStage && newStage != ContactStage.Collapse)
            _permanentMinStage = newStage;
        OnStageChanged?.Invoke(old, newStage);
        Debug.Log($"[ContactStage] {old} -> {newStage}  (fear={_fearLevel:F3})");
    }
}
