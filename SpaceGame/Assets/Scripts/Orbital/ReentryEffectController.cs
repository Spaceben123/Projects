using UnityEngine;

/// <summary>
/// Watches an OrbitingBody's altitude and plays a scripted deorbit/burn-up sequence once it
/// descends below the Karman line while falling - a visual flourish, not simulated atmospheric
/// drag, per this project's "reentry effects can be scripted" allowance.
/// </summary>
public class ReentryEffectController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] OrbitingBody _orbitingBody;
    [SerializeField] Renderer _flareRenderer;
    [SerializeField] EngineExhaustTrail _exhaustTrail;

    [Header("Deorbit Sequence")]
    [SerializeField] Color _heatingGlowColor = new Color(1f, 0.55f, 0.2f, 1f);
    [SerializeField, Min(0f)] float _heatingIntensity = 4f;
    [SerializeField, Min(0.1f)] float _burnUpDurationSeconds = 3f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    Material _flareMaterial;
    bool _isDeorbiting;
    float _deorbitElapsedSeconds;
    double _previousAltitudeKm;

    void Start()
    {
        if (_flareRenderer != null)
            _flareMaterial = _flareRenderer.material;
        if (_orbitingBody != null)
            _previousAltitudeKm = _orbitingBody.AltitudeKm;
    }

    void Update()
    {
        if (_orbitingBody == null)
            return;

        if (_isDeorbiting)
        {
            AdvanceDeorbitSequence();
            return;
        }

        double altitudeKm = _orbitingBody.AltitudeKm;
        bool isDescending = altitudeKm < _previousAltitudeKm;
        if (isDescending && altitudeKm < OrbitalConstants.KarmanLineAltitudeKm)
            BeginDeorbitSequence();

        _previousAltitudeKm = altitudeKm;
    }

    void BeginDeorbitSequence()
    {
        _isDeorbiting = true;
        _deorbitElapsedSeconds = 0f;
        _exhaustTrail?.SetForcedActive(true);

        if (_flareMaterial != null)
        {
            Color glow = _heatingGlowColor * _heatingIntensity;
            glow.a = 1f;
            _flareMaterial.SetColor(BaseColorId, glow);
            _flareMaterial.SetColor(ColorId, glow);
        }
    }

    void AdvanceDeorbitSequence()
    {
        _deorbitElapsedSeconds += Time.deltaTime;
        if (_deorbitElapsedSeconds < _burnUpDurationSeconds)
            return;

        _exhaustTrail?.SetForcedActive(false);
        gameObject.SetActive(false);
    }
}
