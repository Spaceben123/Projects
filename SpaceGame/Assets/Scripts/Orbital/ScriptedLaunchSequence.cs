using UnityEngine;

/// <summary>
/// Scripted (non-physical) ascent-to-orbit sequence: animates a craft from a surface launch pad
/// to orbital insertion altitude over a fixed duration, then hands off to OrbitingBody's real
/// Keplerian coasting. Per this project's "some things can be scripted movement" allowance, the
/// ascent visuals themselves are not physically simulated - only the resulting circular
/// insertion orbit is physically derived.
/// </summary>
public class ScriptedLaunchSequence : MonoBehaviour
{
    [Header("References")]
    [SerializeField] OrbitingBody _orbitingBody;
    [SerializeField] GravitySource _launchPlanet;
    [SerializeField] EngineExhaustTrail _exhaustTrail;

    [Header("Launch Site (lat/lon, degrees)")]
    [SerializeField] float _launchLatitudeDeg;
    [SerializeField] float _launchLongitudeDeg;

    [Header("Target Orbit")]
    [SerializeField, Min(0f)] float _targetAltitudeKm = 400f;
    [SerializeField, Range(0f, 180f)] float _targetInclinationDeg = 51.6f;
    [SerializeField, Min(1f)] float _ascentDurationSeconds = 8f;

    float _ascentElapsedSeconds;
    Vector3 _ascentStartWorldPos;
    bool _isAscending;

    /// <summary>Begins the scripted ascent from the current transform position toward the configured target orbit.</summary>
    public void BeginAscent()
    {
        _ascentStartWorldPos = transform.position;
        _ascentElapsedSeconds = 0f;
        _isAscending = true;
        _orbitingBody?.EnterSuborbitalMode();
        _exhaustTrail?.SetForcedActive(true);
    }

    void Update()
    {
        if (!_isAscending || _launchPlanet == null)
            return;

        _ascentElapsedSeconds += Time.deltaTime;
        float progress01 = Mathf.Clamp01(_ascentElapsedSeconds / _ascentDurationSeconds);

        float targetRadiusUnits = (float)((_launchPlanet.RadiusKm + _targetAltitudeKm) / OrbitalConstants.KmPerUnit);
        Vector3 surfaceDirection = GeoUtils.LatLonToWorld(_launchLatitudeDeg, _launchLongitudeDeg, 1f).normalized;
        Vector3 targetWorldPos = _launchPlanet.transform.position + surfaceDirection * targetRadiusUnits;
        transform.position = Vector3.Lerp(_ascentStartWorldPos, targetWorldPos, Mathf.SmoothStep(0f, 1f, progress01));

        if (progress01 >= 1f)
            CompleteAscent();
    }

    void CompleteAscent()
    {
        _isAscending = false;
        _exhaustTrail?.SetForcedActive(false);

        if (_launchPlanet == null || _orbitingBody == null)
            return;

        double radiusKm = _launchPlanet.RadiusKm + _targetAltitudeKm;

        var circularOrbit = new OrbitalElements
        {
            SemiMajorAxisKm = radiusKm,
            Eccentricity = 0.0,
            InclinationRad = _targetInclinationDeg * Mathf.Deg2Rad,
            RaanRad = _launchLongitudeDeg * Mathf.Deg2Rad,
            ArgOfPeriapsisRad = 0.0,
            MeanAnomalyAtEpochRad = 0.0,
            EpochSeconds = 0.0
        };

        _orbitingBody.SetOrbit(circularOrbit);
    }
}
