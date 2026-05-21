using UnityEngine;
using UnityEngine.InputSystem;

// Attach to a persistent GameObject (e.g. WorldSimulation).
// Raycasts against Earth on left-click → reads country index from region_map → selects nation.
public class NationSelectionSystem : MonoBehaviour
{
    [SerializeField] Transform              _earthTransform;
    [SerializeField] FactionTextureRenderer _factionRenderer;

    public event System.Action<NationRuntime> OnNationSelected;
    public event System.Action               OnNationDeselected;

    const int MapW = 2048;
    const int MapH = 1024;

    Camera _cam;

    void Start()
    {
        _cam = Camera.main;
        if (_earthTransform == null)
            _earthTransform = GameObject.Find("Earth")?.transform;
        if (_factionRenderer == null && _earthTransform != null)
            _factionRenderer = _earthTransform.GetComponentInChildren<FactionTextureRenderer>();
    }

    void Update()
    {
        if (Mouse.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
            HandleClick();

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            Deselect();
    }

    void HandleClick()
    {
        if (_cam == null) return;
        Vector2 screenPos = Mouse.current.position.ReadValue();
        Ray ray = _cam.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));

        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        // Check hit is on Earth (not atmosphere or Moon)
        if (_earthTransform != null && hit.collider.transform != _earthTransform) return;

        // Convert world hit point → lat/lon using GeoUtils convention:
        // x = -cos(lat)*cos(lon), y = sin(lat), z = -cos(lat)*sin(lon)
        Vector3 local = _earthTransform.InverseTransformPoint(hit.point).normalized;
        float lat = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
        float lon = Mathf.Atan2(-local.z, -local.x) * Mathf.Rad2Deg;

        int px = Mathf.Clamp(Mathf.FloorToInt((lon + 180f) / 360f * MapW), 0, MapW - 1);
        int py = Mathf.Clamp(Mathf.FloorToInt((lat +  90f) / 180f * MapH), 0, MapH - 1);

        if (_factionRenderer == null) return;
        byte countryIdx = _factionRenderer.GetCountryAtPixel(px, py);

        if (countryIdx == 255)
        {
            Deselect();
            return;
        }

        _factionRenderer.SelectCountry(countryIdx);

        var nation = NationDataRegistry.Instance?.GetByCountryIndex(countryIdx);
        if (nation != null) OnNationSelected?.Invoke(nation);
        else Deselect();
    }

    void Deselect()
    {
        _factionRenderer?.ClearSelection();
        OnNationDeselected?.Invoke();
    }
}
