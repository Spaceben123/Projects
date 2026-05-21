using UnityEngine;
using UnityEngine.InputSystem;

public class GlobeOverlayToggle : MonoBehaviour
{
    [Header("Default strengths when ON")]
    [SerializeField] float _factionStrength = 0.4f;
    [SerializeField] float _borderStrength  = 0.6f;

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
        SetPropFloat("_BorderStrength", v ? _borderStrength : 0f);
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
