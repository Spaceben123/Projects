using UnityEngine;

public class RegionBorderRenderer : MonoBehaviour
{
    [Header("Visuals")]
    [SerializeField] Material _lineMaterial;
    [SerializeField] float    _lineWidth = 0.04f;
    [SerializeField] bool     _visible   = true;

    static readonly Color s_natoColor        = new Color(0.2f, 0.5f, 1.0f, 0.8f);
    static readonly Color s_bricsColor       = new Color(1.0f, 0.25f, 0.2f, 0.8f);
    static readonly Color s_nonAlignedColor  = new Color(0.6f, 0.6f, 0.6f, 0.8f);
    static readonly Color s_superNationColor = new Color(0.8f, 0.9f, 0.2f, 0.9f);
    static readonly Color s_collapsedColor   = new Color(0.3f, 0.2f, 0.2f, 0.5f);

    LineRenderer[] _lines;

    void Start()
    {
        if (_lineMaterial == null)
            _lineMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));

        var registry = RegionRegistry.Instance;
        if (registry == null) { Debug.LogWarning("[BorderRenderer] No RegionRegistry"); return; }

        _lines = new LineRenderer[registry.Regions.Length];

        for (int i = 0; i < registry.Regions.Length; i++)
        {
            var region = registry.Regions[i];
            if (region == null) continue;

            GameObject go = new GameObject("Border_" + region.Def.regionId);
            go.transform.SetParent(transform);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material          = _lineMaterial;
            lr.startWidth        = _lineWidth;
            lr.endWidth          = _lineWidth;
            lr.useWorldSpace     = true;
            lr.loop              = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            SetBorderPoints(lr, region.Def.boundary);
            _lines[i] = lr;
        }
    }

    void Update()
    {
        var registry = RegionRegistry.Instance;
        if (registry == null || _lines == null) return;

        for (int i = 0; i < registry.Regions.Length; i++)
        {
            var r = registry.Regions[i];
            if (r == null || _lines[i] == null) continue;

            _lines[i].enabled    = _visible;
            _lines[i].startColor = AlignmentColor(r.Alignment);
            _lines[i].endColor   = AlignmentColor(r.Alignment);
        }
    }

    void SetBorderPoints(LineRenderer lr, float[] boundary)
    {
        if (boundary == null || boundary.Length < 4) return;
        int count = boundary.Length / 2;
        lr.positionCount = count;

        for (int i = 0; i < count; i++)
        {
            float lat = boundary[i * 2];
            float lon = boundary[i * 2 + 1];
            lr.SetPosition(i, GeoUtils.LatLonToWorld(lat, lon, GeoUtils.EarthRadiusUnits * 1.002f));
        }
    }

    static Color AlignmentColor(FactionAlignment a)
    {
        return a switch
        {
            FactionAlignment.NATO        => s_natoColor,
            FactionAlignment.BRICS       => s_bricsColor,
            FactionAlignment.SuperNation => s_superNationColor,
            FactionAlignment.Collapsed   => s_collapsedColor,
            _                            => s_nonAlignedColor
        };
    }

    public void SetVisible(bool v) => _visible = v;
}
