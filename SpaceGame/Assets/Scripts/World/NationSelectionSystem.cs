using UnityEngine;
using UnityEngine.InputSystem;

// Attach to a persistent GameObject (e.g. WorldSimulation).
// Raycasts against Earth on left-click → reads the district index from
// district_map → resolves that district's CURRENT owner → selects that nation.
// Clicking conquered land therefore selects the conqueror, which is the correct
// behaviour and comes free from district-level ownership.
public class NationSelectionSystem : MonoBehaviour
{
    [SerializeField] Transform              _earthTransform;
    [SerializeField] FactionTextureRenderer _factionRenderer;

    public event System.Action<NationRuntime> OnNationSelected;
    public event System.Action               OnNationDeselected;

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

        if (_factionRenderer == null) return;

        // Pixel dimensions come from the renderer's actual baked raster rather than
        // hardcoded constants: the bake resolution is a single source of truth, and
        // hardcoding 2048x1024 against a 4096x2048 bake halved hit-test precision.
        int mapW = _factionRenderer.MapWidth;
        int mapH = _factionRenderer.MapHeight;
        if (mapW <= 0 || mapH <= 0) return;

        int px = Mathf.Clamp(Mathf.FloorToInt((lon + 180f) / 360f * mapW), 0, mapW - 1);
        int py = Mathf.Clamp(Mathf.FloorToInt((lat +  90f) / 180f * mapH), 0, mapH - 1);

        ushort districtIdx = _factionRenderer.GetDistrictAtPixel(px, py);
        if (districtIdx == WorldDistricts.None)
        {
            Deselect();
            return;
        }

        // Resolve through the district's current owner, not its original parent
        // country, so conquered territory selects the conqueror.
        byte ownerIdx = TerritoryController.Instance != null
            ? TerritoryController.Instance.GetDistrictOwner(districtIdx)
            : WorldDistricts.GetParentCountry(districtIdx);

        if (ownerIdx == 255)
        {
            Deselect();
            return;
        }

        _factionRenderer.SelectCountry(ownerIdx);

        var nation = NationDataRegistry.Instance?.GetByCountryIndex(ownerIdx);
        if (nation != null) OnNationSelected?.Invoke(nation);
        else Deselect();
    }

    void Deselect()
    {
        _factionRenderer?.ClearSelection();
        OnNationDeselected?.Invoke();
    }
}
