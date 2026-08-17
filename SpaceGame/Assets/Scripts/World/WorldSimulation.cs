using UnityEngine;
using UnityEngine.InputSystem;

public class WorldSimulation : MonoBehaviour
{
    [Header("Time")]
    [SerializeField] float _timeWarpFactor = 1f;
    [SerializeField] float _simulatedYear  = 2026f;

    public float SimulatedYear   => _simulatedYear;
    public float TimeWarpFactor  => _timeWarpFactor;

    PopulationSystem      _pop;
    EconomySystem         _econ;
    TechnologySystem      _tech;
    NationEconomySystem   _nationEcon;
    SurfaceSiteRegistry   _surfaceSites;

    float _dayAccum;
    float _monthAccum;
    float _yearAccum;

    const float SecsPerSimDay   = 1f;
    const float SecsPerSimMonth = 60f;
    const float SecsPerSimYear  = 365f;

    static readonly float[] s_warpOptions = { 1f, 10f, 60f, 365f, 3650f };
    int _warpIndex;

    void Start()
    {
        _pop          = GetComponent<PopulationSystem>();
        _econ         = GetComponent<EconomySystem>();
        _tech         = GetComponent<TechnologySystem>();
        _nationEcon   = GetComponent<NationEconomySystem>();
        _surfaceSites = GetComponent<SurfaceSiteRegistry>();
        _warpIndex    = FindWarpIndex(_timeWarpFactor);
    }

    void Update()
    {
        HandleTimeWarpInput();

        float simDt = Time.deltaTime * _timeWarpFactor;

        _dayAccum   += simDt;
        _monthAccum += simDt;
        _yearAccum  += simDt;
        _simulatedYear += simDt / SecsPerSimYear;

        if (_dayAccum >= SecsPerSimDay)
        {
            _pop?.Tick(_dayAccum / SecsPerSimDay);
            _dayAccum = 0f;
        }

        if (_monthAccum >= SecsPerSimMonth)
        {
            _econ?.Tick(_monthAccum / SecsPerSimMonth);
            _monthAccum = 0f;
        }

        if (_yearAccum >= SecsPerSimYear)
        {
            _tech?.Tick(_yearAccum / SecsPerSimYear);
            _yearAccum = 0f;
        }

        _nationEcon?.Tick(Time.deltaTime, _timeWarpFactor);
        _surfaceSites?.Tick(Time.deltaTime, _timeWarpFactor);
    }

    void HandleTimeWarpInput()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.commaKey.wasPressedThisFrame)
            ChangeTimeWarp(-1);
        else if (Keyboard.current.periodKey.wasPressedThisFrame)
            ChangeTimeWarp(1);
    }

    /// <summary>Moves the simulation one step up or down the available time-warp scale.</summary>
    public void ChangeTimeWarp(int direction)
    {
        if (direction == 0) return;

        int step = direction > 0 ? 1 : -1;
        _warpIndex = Mathf.Clamp(_warpIndex + step, 0, s_warpOptions.Length - 1);
        _timeWarpFactor = s_warpOptions[_warpIndex];
        Debug.Log($"[WorldSim] Time warp: {_timeWarpFactor}x");
    }

    /// <summary>Advances the simulation to the next available time-warp level.</summary>
    public void CycleTimeWarp()
    {
        ChangeTimeWarp(1);
    }

    static int FindWarpIndex(float warpFactor)
    {
        int closestIndex = 0;
        float closestDistance = Mathf.Abs(warpFactor - s_warpOptions[0]);

        for (int i = 1; i < s_warpOptions.Length; i++)
        {
            float distance = Mathf.Abs(warpFactor - s_warpOptions[i]);
            if (distance < closestDistance)
            {
                closestIndex = i;
                closestDistance = distance;
            }
        }

        return closestIndex;
    }

    // HUD presentation is handled by ContactStageHud.
}
