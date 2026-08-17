using UnityEngine;
using UnityEngine.InputSystem;

public class GlobeOverlayToggle : MonoBehaviour
{
    // Overlay is a hard on/off toggle, not a blend — when on it must fully replace the
    // lit terrain colour so it reads at constant strength regardless of lighting/atmosphere.
    [Header("Default strengths when ON")]
    [SerializeField] float _factionStrength = 1f;

    [Header("Borders")]
    [SerializeField] CountryBorderRenderer _borderRenderer;

    bool _factionOn = true;
    bool _bordersOn = true;

    MeshRenderer          _earthRenderer;
    MaterialPropertyBlock _propBlock;

    void Start()
    {
        _earthRenderer = transform.parent?.GetComponent<MeshRenderer>();
        _propBlock     = new MaterialPropertyBlock();
        if (_earthRenderer == null)
            Debug.LogWarning("[GlobeToggle] Earth MeshRenderer not found on parent.");

        if (_borderRenderer == null)
            _borderRenderer = GetComponent<CountryBorderRenderer>();
        if (_borderRenderer == null)
            Debug.LogWarning("[GlobeToggle] CountryBorderRenderer not found — border toggle (F2) will do nothing.");

        ApplyAll();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.f1Key.wasPressedThisFrame) SetFactionVisible(!_factionOn);
        if (kb.f2Key.wasPressedThisFrame) SetBordersVisible(!_bordersOn);
    }

    public void SetFactionVisible(bool v)
    {
        _factionOn = v;
        SetPropFloat("_FactionStrength", v ? _factionStrength : 0f);
    }

    public void SetBordersVisible(bool v)
    {
        _bordersOn = v;
        _borderRenderer?.SetVisible(v);
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
    }
}
