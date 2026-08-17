using UnityEngine;
using UnityEngine.InputSystem;

// Debug/authoring hook for the district territory system.
//
// UNBOUND BY DEFAULT: no input is consumed unless _mouseBindingsEnabled is
// ticked in the inspector. The conquest entry points are public, so gameplay
// systems (a future war resolver, a scripted scenario, a cheat console) can call
// them directly without this component ever touching the mouse.
//
// Note the binding conflict that motivated leaving it off: middle mouse also
// toggles CameraController's cursor lock, and left mouse is nation selection
// plus — double-clicked — camera focus. Re-enable the bindings only for a
// deliberate verification session.
//
// What a conquest looks like on the globe: the target district alone changes to
// the conqueror's alliance colour, and a black outline appears completely around
// it, including along the edges it shared with its own countrymen, which were
// invisible a frame earlier.
public class DistrictDebugHooks : MonoBehaviour
{
    [SerializeField] string _conquerorIso3 = "USA";

    [Header("Debug input — off by default")]
    [Tooltip("When on, middle-click conquers the district under the cursor and right-click restores it.")]
    [SerializeField] bool _mouseBindingsEnabled;

    [Header("Auto-resolved in Start if left empty")]
    [SerializeField] Transform              _earthTransform;
    [SerializeField] FactionTextureRenderer _factionRenderer;

    Camera _cam;

    /// <summary>ISO-3 code of the country that ConquerDistrictUnderCursor hands territory to.</summary>
    public string ConquerorIso3
    {
        get => _conquerorIso3;
        set => _conquerorIso3 = value;
    }

    void Start()
    {
        _cam = Camera.main;
        if (_earthTransform == null)
            _earthTransform = GameObject.Find("Earth")?.transform;
        if (_factionRenderer == null && _earthTransform != null)
            _factionRenderer = _earthTransform.GetComponentInChildren<FactionTextureRenderer>();

        if (_mouseBindingsEnabled && _factionRenderer == null)
            Debug.LogWarning("[DistrictDebug] FactionTextureRenderer not found — middle-click conquest will do nothing.");
    }

    void Update()
    {
        if (!_mouseBindingsEnabled) return;

        var mouse = Mouse.current;
        if (mouse == null) return;
        if (mouse.middleButton.wasPressedThisFrame) ApplyOwnershipUnderCursor(true);
        if (mouse.rightButton.wasPressedThisFrame)  ApplyOwnershipUnderCursor(false);
    }

    /// <summary>Hands the district under the cursor to ConquerorIso3. False when the cursor is not over land.</summary>
    public bool ConquerDistrictUnderCursor() => ApplyOwnershipUnderCursor(true);

    /// <summary>Restores the district under the cursor to its original parent country. False when the cursor is not over land.</summary>
    public bool RestoreDistrictUnderCursor() => ApplyOwnershipUnderCursor(false);

    /// <summary>Resolves the district under the cursor, or 65535 when the cursor is over ocean or off the globe.</summary>
    public ushort GetDistrictUnderCursor()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null || _factionRenderer == null || _earthTransform == null) return WorldDistricts.None;
        if (Mouse.current == null) return WorldDistricts.None;

        Vector2 screenPos = Mouse.current.position.ReadValue();
        Ray ray = _cam.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));

        if (!Physics.Raycast(ray, out RaycastHit hit)) return WorldDistricts.None;
        if (hit.collider.transform != _earthTransform) return WorldDistricts.None;

        // Same GeoUtils convention NationSelectionSystem uses:
        // x = -cos(lat)*cos(lon), y = sin(lat), z = -cos(lat)*sin(lon)
        Vector3 local = _earthTransform.InverseTransformPoint(hit.point).normalized;
        float lat = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
        float lon = Mathf.Atan2(-local.z, -local.x) * Mathf.Rad2Deg;

        // Pixel dimensions come from the renderer's actual baked raster, never
        // hardcoded — the bake resolution is the single source of truth.
        int mapW = _factionRenderer.MapWidth;
        int mapH = _factionRenderer.MapHeight;
        if (mapW <= 0 || mapH <= 0) return WorldDistricts.None;

        int px = Mathf.Clamp(Mathf.FloorToInt((lon + 180f) / 360f * mapW), 0, mapW - 1);
        int py = Mathf.Clamp(Mathf.FloorToInt((lat +  90f) / 180f * mapH), 0, mapH - 1);

        return _factionRenderer.GetDistrictAtPixel(px, py);
    }

    /// <summary>Resolves the district under the cursor and either conquers it or restores it.</summary>
    bool ApplyOwnershipUnderCursor(bool conquer)
    {
        ushort districtIdx = GetDistrictUnderCursor();
        if (districtIdx == WorldDistricts.None)
        {
            Debug.Log("[DistrictDebug] Ocean / no district under the cursor.");
            return false;
        }

        var territory = TerritoryController.Instance;
        if (territory == null)
        {
            Debug.LogWarning("[DistrictDebug] No TerritoryController in the scene.");
            return false;
        }

        if (conquer)
        {
            if (!WorldRegionMapper.TryGetCountryIndex(_conquerorIso3, out byte conqueror))
            {
                Debug.LogWarning($"[DistrictDebug] Unknown conqueror ISO-3 '{_conquerorIso3}' — " +
                                 "must be an entry in WorldRegionMapper.");
                return false;
            }
            territory.SetDistrictOwner(districtIdx, conqueror);
        }
        else
        {
            territory.ResetDistrictOwner(districtIdx);
        }

        Debug.Log($"[DistrictDebug] {(conquer ? "Conquered" : "Restored")} " +
                  $"[{districtIdx}] {WorldDistricts.GetName(districtIdx)} " +
                  $"({WorldDistricts.GetCode(districtIdx)}) — " +
                  $"parent={WorldDistricts.GetParentCountry(districtIdx)}, " +
                  $"owner={territory.GetDistrictOwner(districtIdx)}, " +
                  $"neighbours={WorldDistricts.Neighbours(districtIdx).Count}");
        return true;
    }
}
