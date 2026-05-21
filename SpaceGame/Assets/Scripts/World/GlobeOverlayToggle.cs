using UnityEngine;
using UnityEngine.InputSystem;

public class GlobeOverlayToggle : MonoBehaviour
{
    [Header("Default strengths when ON")]
    [SerializeField] float _factionStrength = 0.4f;
    [SerializeField] float _borderStrength  = 0.6f;
    [SerializeField] float _heatmapStrength = 0.5f;

    bool _factionOn = true;
    bool _bordersOn = true;
    bool _citiesOn  = true;
    bool _heatmapOn = false;

    MeshRenderer _earthRenderer;
    MaterialPropertyBlock _propBlock;
    CityDotRenderer _cityDots;

    void Start()
    {
        _earthRenderer = transform.parent?.GetComponent<MeshRenderer>();
        _propBlock     = new MaterialPropertyBlock();
        _cityDots      = GetComponent<CityDotRenderer>();

        if (_earthRenderer == null)
            Debug.LogWarning("[GlobeToggle] Earth MeshRenderer not found on parent.");

        ApplyAll();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.f1Key.wasPressedThisFrame) ToggleFaction();
        if (kb.f2Key.wasPressedThisFrame) ToggleBorders();
        if (kb.f3Key.wasPressedThisFrame) ToggleCities();
        if (kb.f4Key.wasPressedThisFrame) ToggleHeatmap();
    }

    public void ToggleFaction()  => SetFactionVisible(!_factionOn);
    public void ToggleBorders()  => SetBordersVisible(!_bordersOn);
    public void ToggleCities()   => SetCitiesVisible(!_citiesOn);
    public void ToggleHeatmap()  => SetHeatmapVisible(!_heatmapOn);

    public void SetFactionVisible(bool v)
    {
        _factionOn = v;
        SetPropFloat("_FactionStrength", v ? _factionStrength : 0f);
    }

    public void SetBordersVisible(bool v)
    {
        _bordersOn = v;
        SetPropFloat("_BorderStrength", v ? _borderStrength : 0f);
    }

    public void SetCitiesVisible(bool v)
    {
        _citiesOn = v;
        _cityDots?.SetVisible(v);
    }

    public void SetHeatmapVisible(bool v)
    {
        _heatmapOn = v;
        SetPropFloat("_HeatmapStrength", v ? _heatmapStrength : 0f);
    }

    void SetPropFloat(string name, float value)
    {
        if (_earthRenderer == null) return;
        _earthRenderer.GetPropertyBlock(_propBlock);
        _propBlock.SetFloat(name, value);
        _earthRenderer.SetPropertyBlock(_propBlock);
    }

    void ApplyAll()
    {
        SetFactionVisible(_factionOn);
        SetBordersVisible(_bordersOn);
        SetCitiesVisible(_citiesOn);
        SetHeatmapVisible(_heatmapOn);
    }
}
