using System.Collections.Generic;
using UnityEngine;

// Renders each SurfaceSite as a small camera-facing marker pinned to the
// globe surface, colored by its owner's current alliance. Follows
// SatelliteFlareController's camera-facing billboard convention, and uses
// GeoUtils.LatLonToWorld for placement — the project's one authoritative
// lat/lon -> world-space conversion, same as CountryBorderRenderer.
public class SurfaceSiteMarkerRenderer : MonoBehaviour
{
    [SerializeField] Camera _observerCamera;
    [SerializeField] float  _markerWorldSize = 0.12f;
    [SerializeField] float  _surfaceOffset   = 0.01f; // above GeoUtils.EarthRadiusUnits, matches CountryBorderRenderer's convention

    static readonly Color s_natoColor        = new Color(0.25f, 0.5f,  0.95f, 1f);
    static readonly Color s_bricsColor       = new Color(0.9f,  0.25f, 0.2f,  1f);
    static readonly Color s_neutralColor     = new Color(0.6f,  0.6f,  0.6f,  1f);
    static readonly Color s_superNationColor = new Color(0.85f, 0.9f,  0.2f,  1f);
    static readonly Color s_collapsedColor   = new Color(0.3f,  0.2f,  0.2f,  1f);

    static Mesh s_markerMesh;
    Material    _markerMaterial;

    // Pooled marker GameObjects — reused and repositioned/recolored on every
    // OnSitesChanged rather than instantiated/destroyed per change.
    readonly List<GameObject>            _pool       = new();
    readonly List<MaterialPropertyBlock> _propBlocks = new();

    void Start()
    {
        if (_observerCamera == null)
            _observerCamera = Camera.main;

        BuildMaterial();

        if (SurfaceSiteRegistry.Instance != null)
        {
            SurfaceSiteRegistry.Instance.OnSitesChanged += Refresh;
            Refresh();
        }
    }

    void OnDestroy()
    {
        if (SurfaceSiteRegistry.Instance != null)
            SurfaceSiteRegistry.Instance.OnSitesChanged -= Refresh;
    }

    void LateUpdate()
    {
        if (_observerCamera == null)
            _observerCamera = Camera.main;
        if (_observerCamera == null) return;

        for (int i = 0; i < _pool.Count; i++)
        {
            GameObject marker = _pool[i];
            if (!marker.activeSelf) continue;

            Vector3 toCamera = (_observerCamera.transform.position - marker.transform.position).normalized;
            marker.transform.rotation = Quaternion.LookRotation(toCamera, _observerCamera.transform.up);
        }
    }

    void Refresh()
    {
        var sites = SurfaceSiteRegistry.Instance?.Sites;
        if (sites == null) return;

        EnsurePoolSize(sites.Count);

        var territory = TerritoryController.Instance;
        for (int i = 0; i < _pool.Count; i++)
        {
            if (i >= sites.Count)
            {
                _pool[i].SetActive(false);
                continue;
            }

            SurfaceSite site   = sites[i];
            GameObject  marker = _pool[i];
            marker.SetActive(true);
            marker.transform.position = GeoUtils.LatLonToWorld(site.lat, site.lon, GeoUtils.EarthRadiusUnits + _surfaceOffset);

            // Transform.position above is world-space and Unity resolves it correctly
            // regardless of the parent hierarchy's scale, but localScale is NOT
            // auto-compensated — markers are parented under Earth, whose Transform
            // applies a x10 scale (its mesh is authored as a unit sphere), so we divide
            // it back out here to keep _markerWorldSize an actual world-space size.
            Vector3 parentScale = transform.lossyScale;
            marker.transform.localScale = new Vector3(
                _markerWorldSize / Mathf.Max(0.0001f, parentScale.x),
                _markerWorldSize / Mathf.Max(0.0001f, parentScale.y),
                _markerWorldSize / Mathf.Max(0.0001f, parentScale.z));

            Color color = territory != null ? AlignmentColor(territory.GetAlignment(site.ownerCountryIdx)) : s_neutralColor;
            MaterialPropertyBlock block = _propBlocks[i];
            block.SetColor("_Color", color);
            marker.GetComponent<MeshRenderer>().SetPropertyBlock(block);
        }
    }

    void EnsurePoolSize(int count)
    {
        while (_pool.Count < count)
        {
            var go = new GameObject($"SiteMarker_{_pool.Count}");
            go.transform.SetParent(transform, false);

            var filter   = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            filter.sharedMesh          = GetMarkerMesh();
            renderer.sharedMaterial    = _markerMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows    = false;

            _pool.Add(go);
            _propBlocks.Add(new MaterialPropertyBlock());
        }
    }

    void BuildMaterial()
    {
        Shader shader = Shader.Find("Space/VertexColorLine");
        _markerMaterial = new Material(shader) { name = "SurfaceSiteMarkerRuntime" };
    }

    static Mesh GetMarkerMesh()
    {
        if (s_markerMesh != null) return s_markerMesh;

        var mesh = new Mesh { name = "SurfaceSiteMarkerQuad" };
        mesh.SetVertices(new List<Vector3>
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
        });
        // Local +Z is this billboard's camera-facing axis (see LateUpdate's
        // LookRotation), so a +Z normal always points at the camera once
        // transformed to world space — required by VertexColorLine.shader's
        // surface-normal discard, which would otherwise treat an unset (zero)
        // normal as facing away and hide every marker.
        mesh.SetNormals(new List<Vector3>
        {
            Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
        });
        mesh.SetColors(new List<Color32> { Color.white, Color.white, Color.white, Color.white });
        // Zero side-expansion vectors: VertexColorLine.shader offsets each vertex along
        // UV0.xyz by UV0.w * _LineHalfWidth to give border ribbons a camera-relative
        // thickness. Markers are sized by their Transform instead, so they opt out by
        // supplying an explicit zero here rather than relying on an absent stream.
        mesh.SetUVs(0, new List<Vector4>
        {
            Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero,
        });
        mesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0);
        mesh.RecalculateBounds();
        s_markerMesh = mesh;
        return mesh;
    }

    static Color AlignmentColor(FactionAlignment a) => a switch
    {
        FactionAlignment.NATO        => s_natoColor,
        FactionAlignment.BRICS       => s_bricsColor,
        FactionAlignment.NonAligned  => s_neutralColor,
        FactionAlignment.SuperNation => s_superNationColor,
        FactionAlignment.Collapsed   => s_collapsedColor,
        _                            => s_neutralColor,
    };
}
