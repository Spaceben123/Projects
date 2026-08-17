using UnityEngine;

/// <summary>
/// Core per-object component driving Newtonian orbital motion: analytic Keplerian coasting
/// (cheap, closed-form, throttleable), numeric-integrated maneuvering burns, and the
/// delta-v/continuous-thrust API that launch sequences and later missile/maneuver-command
/// plans call into.
/// </summary>
public class OrbitingBody : MonoBehaviour
{
    public enum OrbitalState { Coasting, Maneuvering, Suborbital }

    [Header("Gravity & Orbit")]
    [SerializeField] GravitySource _primaryBody;
    [SerializeField] OrbitalElements _elements;
    [SerializeField] SpacecraftPropellant _propellant = new SpacecraftPropellant();
    [SerializeField] WorldSimulation _worldSimulation;

    [Header("Update Throttling")]
    [Tooltip("When true, a parked coasting orbit only re-solves its position every _staticIdleUpdateIntervalSeconds of simulated time instead of every frame.")]
    [SerializeField] bool _isStaticIdle = true;
    [SerializeField, Min(0.1f)] float _staticIdleUpdateIntervalSeconds = 0.75f;

    OrbitalState _state = OrbitalState.Coasting;
    double _epochAccumulatorSeconds;
    double _idleUpdateAccumulatorSeconds;
    double _burnMassFlowRateKgPerSec;
    Vector3 _maneuverPosKm;
    Vector3 _maneuverVelKmPerSec;
    Vector3 _thrustAccelKmPerSec2;

    // Static-idle coasting interpolates smoothly across each throttle interval instead of
    // snapping the Transform once per solve, which is what previously caused visible stutter.
    bool _idleSegmentInitialized;
    Vector3 _idleSegmentStartPosKm;
    Vector3 _idleSegmentEndPosKm;

    public OrbitalState CurrentState => _state;
    public GravitySource PrimaryBody => _primaryBody;
    public SpacecraftPropellant Propellant => _propellant;

    /// <summary>Height above the primary body's surface, in kilometers.</summary>
    public double AltitudeKm
    {
        get
        {
            if (_primaryBody == null)
                return 0.0;

            Vector3 offsetKm = OrbitalConstants.WorldToKmVector(transform.position - _primaryBody.transform.position);
            return offsetKm.magnitude - _primaryBody.RadiusKm;
        }
    }

    public bool IsAboveKarmanLine => AltitudeKm > OrbitalConstants.KarmanLineAltitudeKm;

    void FixedUpdate()
    {
        if (_primaryBody == null)
            return;

        double dt = Time.fixedDeltaTime * (_worldSimulation != null ? _worldSimulation.TimeWarpFactor : 1f);
        _epochAccumulatorSeconds += dt;

        switch (_state)
        {
            case OrbitalState.Maneuvering:
                StepManeuver(dt);
                break;

            case OrbitalState.Coasting:
                StepCoast(dt);
                break;

            case OrbitalState.Suborbital:
                // Position is driven externally by ScriptedLaunchSequence / ReentryEffectController while in this state.
                break;
        }
    }

    void StepManeuver(double dt)
    {
        _propellant.ConsumeMass(_burnMassFlowRateKgPerSec * dt);
        OrbitalNumericIntegrator.RK4Step(ref _maneuverPosKm, ref _maneuverVelKmPerSec, _thrustAccelKmPerSec2, dt);
        WriteWorldPosition(_maneuverPosKm);

        if (_propellant.PropellantMassKg <= 0.0)
            EndBurn();
    }

    void StepCoast(double dt)
    {
        if (!_isStaticIdle)
        {
            (Vector3 posKm, _) = _elements.ToStateVectors(_epochAccumulatorSeconds, _primaryBody.MuKm3PerSec2);
            WriteWorldPosition(posKm);
            return;
        }

        // Only the two endpoint solves per interval are throttled (cheap win for many parked
        // objects); the Transform itself is written every FixedUpdate by interpolating across
        // the interval so the visible motion stays smooth instead of snapping every solve.
        if (!_idleSegmentInitialized || _idleUpdateAccumulatorSeconds >= _staticIdleUpdateIntervalSeconds)
            BeginIdleSegment();

        _idleUpdateAccumulatorSeconds += dt;
        float segmentAlpha = Mathf.Clamp01((float)(_idleUpdateAccumulatorSeconds / _staticIdleUpdateIntervalSeconds));
        Vector3 interpolatedPosKm = Vector3.Lerp(_idleSegmentStartPosKm, _idleSegmentEndPosKm, segmentAlpha);
        WriteWorldPosition(interpolatedPosKm);
    }

    /// <summary>Solves the analytic position at the start and end epoch of the next idle throttle interval.</summary>
    void BeginIdleSegment()
    {
        _idleSegmentStartPosKm = SolveCoastPositionKm(_epochAccumulatorSeconds);
        _idleSegmentEndPosKm = SolveCoastPositionKm(_epochAccumulatorSeconds + _staticIdleUpdateIntervalSeconds);
        _idleUpdateAccumulatorSeconds = 0.0;
        _idleSegmentInitialized = true;
    }

    Vector3 SolveCoastPositionKm(double epochSeconds)
    {
        (Vector3 posKm, _) = _elements.ToStateVectors(epochSeconds, _primaryBody.MuKm3PerSec2);
        return posKm;
    }

    void WriteWorldPosition(Vector3 posKm)
    {
        transform.position = _primaryBody.transform.position + OrbitalConstants.KmToWorld(posKm.x, posKm.y, posKm.z);
    }

    /// <summary>Replaces the current orbit with a fresh set of Keplerian elements and switches to Coasting.</summary>
    public void SetOrbit(OrbitalElements elements)
    {
        _elements = elements;
        _epochAccumulatorSeconds = elements.EpochSeconds;
        _idleUpdateAccumulatorSeconds = 0.0;
        _idleSegmentInitialized = false;
        _state = OrbitalState.Coasting;
    }

    /// <summary>
    /// Applies an instantaneous delta-v kick (patched-conic approximation, same as real mission
    /// planning tools use): consumes propellant, adds the velocity change to the current state
    /// vector, and re-derives orbital elements from the result.
    /// </summary>
    public void ApplyImpulsiveDeltaV(Vector3 deltaVDirectionWorld, float deltaVKmPerSec)
    {
        if (_primaryBody == null)
            return;

        if (!_propellant.TryConsumeImpulsiveDeltaV(deltaVKmPerSec))
        {
            Debug.LogWarning($"[OrbitingBody] {name}: insufficient propellant for a {deltaVKmPerSec} km/s burn.");
            return;
        }

        (Vector3 posKm, Vector3 velKmPerSec) = _elements.ToStateVectors(_epochAccumulatorSeconds, _primaryBody.MuKm3PerSec2);
        velKmPerSec += deltaVDirectionWorld.normalized * deltaVKmPerSec;

        _elements = OrbitalElements.FromStateVectors(posKm, velKmPerSec, _primaryBody.MuKm3PerSec2, _epochAccumulatorSeconds);
        _isStaticIdle = false;
        _state = OrbitalState.Coasting;
    }

    /// <summary>Begins a sustained-thrust maneuver, switching from analytic coasting to numeric integration.</summary>
    public void BeginContinuousBurn(Vector3 thrustDirectionWorld, float thrustNewtons)
    {
        if (_primaryBody == null)
            return;

        (_maneuverPosKm, _maneuverVelKmPerSec) = _elements.ToStateVectors(_epochAccumulatorSeconds, _primaryBody.MuKm3PerSec2);

        double accelKmPerSec2 = thrustNewtons / _propellant.CurrentMassKg / 1000.0;
        _thrustAccelKmPerSec2 = thrustDirectionWorld.normalized * (float)accelKmPerSec2;
        _burnMassFlowRateKgPerSec = _propellant.MassFlowRateKgPerSec(thrustNewtons);

        _isStaticIdle = false;
        _state = OrbitalState.Maneuvering;
    }

    /// <summary>Ends the current continuous burn, re-deriving Keplerian elements from the final state vector and returning to Coasting.</summary>
    public void EndBurn()
    {
        if (_primaryBody == null || _state != OrbitalState.Maneuvering)
            return;

        _elements = OrbitalElements.FromStateVectors(_maneuverPosKm, _maneuverVelKmPerSec, _primaryBody.MuKm3PerSec2, _epochAccumulatorSeconds);
        _idleSegmentInitialized = false;
        _state = OrbitalState.Coasting;
    }

    /// <summary>
    /// Switches into the Suborbital state, suspending this component's own position updates so a
    /// ScriptedLaunchSequence or ReentryEffectController can drive the transform directly.
    /// </summary>
    public void EnterSuborbitalMode()
    {
        _state = OrbitalState.Suborbital;
    }

    /// <summary>Toggles the static-idle throttle. Never changes the underlying analytic result, only how often it's written to the Transform.</summary>
    public void SetStaticIdle(bool idle)
    {
        _isStaticIdle = idle;
        _idleUpdateAccumulatorSeconds = 0.0;
        _idleSegmentInitialized = false;
    }
}
