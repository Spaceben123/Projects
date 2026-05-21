using UnityEngine;

public class WorldSimulation : MonoBehaviour
{
    [Header("Time")]
    [SerializeField] float _timeWarpFactor = 1f;
    [SerializeField] float _simulatedYear  = 2026f;

    public float SimulatedYear   => _simulatedYear;
    public float TimeWarpFactor  => _timeWarpFactor;

    PopulationSystem    _pop;
    EconomySystem       _econ;
    TechnologySystem    _tech;
    ContactStageManager _stages;
    NationEconomySystem _nationEcon;

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
        _pop       = GetComponent<PopulationSystem>();
        _econ      = GetComponent<EconomySystem>();
        _tech      = GetComponent<TechnologySystem>();
        _stages    = ContactStageManager.Instance;
        _nationEcon = GetComponent<NationEconomySystem>();
    }

    void Update()
    {
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
    }

    public void CycleTimeWarp()
    {
        _warpIndex      = (_warpIndex + 1) % s_warpOptions.Length;
        _timeWarpFactor = s_warpOptions[_warpIndex];
        Debug.Log($"[WorldSim] Time warp: {_timeWarpFactor}x");
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 200, 20), $"Year: {_simulatedYear:F1}  Warp: {_timeWarpFactor}x");
        GUI.Label(new Rect(10, 28, 200, 20), $"Stage: {_stages?.CurrentStage}  Fear: {_stages?.FearLevel:F3}");
        if (GUI.Button(new Rect(10, 48, 100, 22), "Cycle Warp")) CycleTimeWarp();
    }
}
