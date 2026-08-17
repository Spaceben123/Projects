using System.Collections.Generic;
using UnityEngine;

public class SatelliteFlareController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Camera _observerCamera;
    [SerializeField] Transform _sunTransform;
    [SerializeField] Transform _dotTransform;
    [SerializeField] Renderer _dotRenderer;

    [Header("Distant Reflection")]
    [SerializeField, Min(0f)] float _baseIntensity = 0.45f;
    [SerializeField, Min(0f)] float _peakIntensity = 1.8f;
    [SerializeField, Range(0.25f, 1f)] float _glintStartAlignment = 0.72f;
    [SerializeField, Range(0.75f, 1f)] float _glintPeakAlignment = 0.985f;
    [SerializeField, Min(1f)] float _glintSharpness = 2.2f;
    [SerializeField, Min(0f)] float _nearDistance = 4f;
    [SerializeField, Min(1f)] float _farDistance = 120f;
    [SerializeField, Min(0.0001f)] float _angularSize = 0.0007f;
    [SerializeField, Min(0.0001f)] float _minimumWorldSize = 0.025f;
    [SerializeField, Min(0.0001f)] float _maximumWorldSize = 0.07f;
    [SerializeField] Color _reflectionColor = new(0.72f, 0.86f, 1f, 1f);
    [SerializeField] bool _sunIsDirectional = true;

    [Header("Tracking / UI")]
    [Tooltip("Which right-dock Information panel category this craft is listed under.")]
    [SerializeField] SpaceObjectCategory _category = SpaceObjectCategory.UnmannedCraft;
    [Tooltip("Name shown in the Information panel's craft list. Falls back to the GameObject name when left blank.")]
    [SerializeField] string _displayName;

    /// <summary>Which right-dock Information panel category this craft is listed under.</summary>
    public SpaceObjectCategory Category => _category;

    /// <summary>Name shown in the Information panel's craft list; falls back to the GameObject name.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? name : _displayName;

    Material _dotMaterial;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    // Every glare-only space object (ships, stations, ...) is represented on screen by a
    // billboarded dot whose visible size has nothing to do with its near-invisible physical
    // collider. Registering here lets CameraController do forgiving screen-space "click the
    // glare" hit-testing instead of relying on a 3D physics raycast against that tiny collider.
    static readonly List<SatelliteFlareController> s_all = new List<SatelliteFlareController>();
    public static IReadOnlyList<SatelliteFlareController> All => s_all;

    void Awake()
    {
        ResolveReferences();
    }

    void OnEnable()
    {
        if (!s_all.Contains(this))
            s_all.Add(this);
    }

    void LateUpdate()
    {
        if (!ResolveReferences())
            return;

        Vector3 toCamera = _observerCamera.transform.position - transform.position;
        float distance = toCamera.magnitude;
        if (distance <= 0.0001f)
            return;

        Vector3 toObserver = toCamera / distance;
        Vector3 toSun = _sunIsDirectional
            ? -_sunTransform.forward
            : (_sunTransform.position - transform.position).normalized;
        float alignment = Mathf.Clamp01(Vector3.Dot(toObserver, toSun));
        float glint = CalculateGlint(alignment);
        float distanceFactor = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_nearDistance, _farDistance, distance));
        float intensity = Mathf.Lerp(_baseIntensity * 0.35f, _baseIntensity, distanceFactor) + _peakIntensity * glint;

        _dotTransform.rotation = Quaternion.LookRotation(toObserver, _observerCamera.transform.up);
        float worldSize = Mathf.Clamp(distance * _angularSize, _minimumWorldSize, _maximumWorldSize);
        Vector3 lossyScale = transform.lossyScale;
        _dotTransform.localScale = new Vector3(
            worldSize / Mathf.Max(0.0001f, lossyScale.x),
            worldSize / Mathf.Max(0.0001f, lossyScale.y),
            1f);

        Color color = _reflectionColor * intensity;
        color.a = _reflectionColor.a;
        _dotMaterial.SetColor(BaseColorId, color);
        _dotMaterial.SetColor(ColorId, color);
    }

    bool ResolveReferences()
    {
        if (_observerCamera == null)
            _observerCamera = Camera.main;
        if (_sunTransform == null)
            _sunTransform = GameObject.Find("Sun")?.transform;
        if (_dotTransform == null && transform.childCount > 0)
            _dotTransform = transform.GetChild(0);
        if (_dotRenderer == null && _dotTransform != null)
            _dotRenderer = _dotTransform.GetComponent<Renderer>();
        if (_dotMaterial == null && _dotRenderer != null)
            _dotMaterial = _dotRenderer.material;

        return _observerCamera != null && _sunTransform != null && _dotTransform != null && _dotMaterial != null;
    }

    float CalculateGlint(float observerSunAlignment)
    {
        if (observerSunAlignment <= _glintStartAlignment)
            return 0f;

        float alignmentRange = Mathf.Max(0.0001f, _glintPeakAlignment - _glintStartAlignment);
        float normalizedAlignment = Mathf.Clamp01((observerSunAlignment - _glintStartAlignment) / alignmentRange);
        float shapedGlint = Mathf.SmoothStep(0f, 1f, normalizedAlignment);
        return Mathf.Pow(shapedGlint, _glintSharpness);
    }

    void OnDisable()
    {
        s_all.Remove(this);

        if (_dotMaterial != null)
        {
            Color color = _reflectionColor * 0f;
            color.a = 0f;
            _dotMaterial.SetColor(BaseColorId, color);
            _dotMaterial.SetColor(ColorId, color);
        }
    }
}
