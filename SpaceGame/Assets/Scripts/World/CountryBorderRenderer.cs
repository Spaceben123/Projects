using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Draws every district-to-district political border as a crisp, resolution-independent
// ribbon mesh on the Earth surface, shown/hidden per current TerritoryController state.
// Geometry comes from the vector data baked by WorldRegionBaker
// (district_polygons.bytes), which stores several detail levels of the same rings:
//
//   * DETAIL LOD — the mesh set swaps by camera altitude, so a close-up view gets full
//     coastline detail while a whole-globe view pays for a fraction of the vertices.
//     Levels are built lazily, the first time the camera actually reaches them.
//   * CONSTANT SCREEN WIDTH — ribbon half-width is NOT baked into the vertices. Each
//     vertex stores its miter side direction, and VertexColorLine.shader expands it by
//     the _LineHalfWidth uniform this component updates every frame from the camera
//     distance, so a border stays a ~2px hairline whether you're in orbit or zoomed to
//     one country, instead of turning into a fat world-space band as you approach.
//
// Pattern follows NationStatPanel: self-building at runtime, no prefab dependency.
public class CountryBorderRenderer : MonoBehaviour
{
    // Tiny additive lift off the sphere, only so the ribbon isn't exactly coplanar with
    // the Earth mesh. Occlusion correctness comes from VertexColorLine.shader's
    // surface-normal discard, not from this offset.
    const float kSurfaceRadius     = GeoUtils.EarthRadiusUnits + 0.001f;
    const int   kMaxVertsPerMesh   = 60000; // keeps every chunk inside Mesh.IndexFormat.UInt16 range
    const float kMinHalfWidthWorld = 0.0008f;
    const float kMaxHalfWidthWorld = 0.030f;
    // Switching LOD needs a deadband, or a camera parked exactly on a threshold would
    // swap mesh sets every frame.
    const float kLodHysteresis     = 1.15f;

    // Political borders are always drawn a fixed black, independent of alliance —
    // alliance is communicated by the country fill color (FactionTextureRenderer), not
    // by the border stroke.
    static readonly Color32 kBorderColor = new Color32(0, 0, 0, 255);

    // Camera altitude above the surface (world units, Earth radius = 10) at or below
    // which each detail level is used, finest first. Extra levels beyond what the baked
    // file provides are ignored.
    static readonly float[] kLodMaxAltitude = { 2.5f, 12f, float.MaxValue };

    [SerializeField] bool   _visible          = true;
    [SerializeField] float  _screenPixelWidth = 2.0f; // target on-screen border thickness
    [SerializeField] Camera _observerCamera;

    // A border segment is identified by the two DISTRICTS it separates. It draws
    // only while their owners differ — that single comparison is what makes
    // internal district lines invisible at peace and a conquered district's
    // outline appear instantly.
    struct SegmentRef
    {
        public ushort districtA;
        public ushort districtB;
        public int    chunkIndex;
        public int    vertexStart; // 4 consecutive vertices: right0, left0, left1, right1
    }

    class MeshChunk
    {
        public Mesh          mesh;
        public MeshFilter    filter;
        public List<Vector3> positions = new();
        public List<Vector3> normals   = new();
        public List<Vector4> sides     = new(); // xyz = miter side dir (local), w = expansion sign
        public List<Color32> colors    = new();
        public List<int>     triangles = new();
    }

    Material           _borderMaterial;
    List<MeshChunk>[]  _lodChunks;
    List<SegmentRef>[] _lodSegments;
    bool[]             _lodBuilt;
    int                _activeLod = -1;

    void Start()
    {
        if (_observerCamera == null) _observerCamera = Camera.main;

        BuildMaterial();

        if (!WorldDistrictPolygons.TryLoad())
            Debug.LogWarning("[CountryBorderRenderer] No border polygon data — run Assets > SpaceGame > Bake Region Map from Shapefile.");

        int lodCount = Mathf.Max(1, WorldDistrictPolygons.LodCount);
        _lodChunks   = new List<MeshChunk>[lodCount];
        _lodSegments = new List<SegmentRef>[lodCount];
        _lodBuilt    = new bool[lodCount];
        for (int i = 0; i < lodCount; i++)
        {
            _lodChunks[i]   = new List<MeshChunk>();
            _lodSegments[i] = new List<SegmentRef>();
        }

        // Start on the coarsest level: it is the cheapest to build and matches the
        // default whole-globe camera; finer levels are built on first approach.
        ActivateLod(lodCount - 1);
        UpdateLineWidth();

        if (TerritoryController.Instance != null)
            TerritoryController.Instance.OnTerritoryChanged += RecolorAll;
    }

    void OnDestroy()
    {
        if (TerritoryController.Instance != null)
            TerritoryController.Instance.OnTerritoryChanged -= RecolorAll;
    }

    void LateUpdate()
    {
        if (_observerCamera == null) _observerCamera = Camera.main;
        if (_observerCamera == null || _lodChunks == null) return;

        UpdateLineWidth();
        UpdateLod();
    }

    /// <summary>Shows or hides every border segment. Wired to the F2 overlay toggle.</summary>
    public void SetVisible(bool v)
    {
        _visible = v;
        ApplyChunkActivation();
    }

    // ---------------------------------------------------------------------
    // Camera-driven width + detail level
    // ---------------------------------------------------------------------

    float SurfaceAltitude()
    {
        float dist = Vector3.Distance(_observerCamera.transform.position, transform.position);
        return Mathf.Max(0.01f, dist - GeoUtils.EarthRadiusUnits);
    }

    // Keeps the ribbon a constant thickness in PIXELS by converting the target pixel
    // width into world units at the distance of the globe surface, then into this
    // GameObject's local units (it is parented under Earth's x10 scale).
    void UpdateLineWidth()
    {
        if (_observerCamera == null || _borderMaterial == null) return;

        float worldPerPixel = 2f * SurfaceAltitude()
                            * Mathf.Tan(_observerCamera.fieldOfView * 0.5f * Mathf.Deg2Rad)
                            / Mathf.Max(1, _observerCamera.pixelHeight);

        float halfWidthWorld = Mathf.Clamp(_screenPixelWidth * 0.5f * worldPerPixel,
                                           kMinHalfWidthWorld, kMaxHalfWidthWorld);
        float scale = Mathf.Max(0.0001f, transform.lossyScale.x);
        _borderMaterial.SetFloat("_LineHalfWidth", halfWidthWorld / scale);
    }

    void UpdateLod()
    {
        float alt    = SurfaceAltitude();
        int   target = _lodChunks.Length - 1;
        for (int lod = 0; lod < _lodChunks.Length; lod++)
        {
            // Going finer needs the altitude to fall clearly below the threshold, going
            // coarser needs it to rise clearly above — that gap is the deadband.
            float limit = kLodMaxAltitude[Mathf.Min(lod, kLodMaxAltitude.Length - 1)];
            if (limit < float.MaxValue)
            {
                if (lod < _activeLod) limit /= kLodHysteresis;
                if (lod > _activeLod) limit *= kLodHysteresis;
            }

            if (alt <= limit) { target = lod; break; }
        }

        if (target != _activeLod) ActivateLod(target);
    }

    void ActivateLod(int lod)
    {
        lod = Mathf.Clamp(lod, 0, _lodChunks.Length - 1);
        if (!_lodBuilt[lod]) BuildLod(lod);
        _activeLod = lod;
        ApplyChunkActivation();
    }

    void ApplyChunkActivation()
    {
        if (_lodChunks == null) return;
        for (int lod = 0; lod < _lodChunks.Length; lod++)
        {
            bool on = _visible && lod == _activeLod;
            foreach (var chunk in _lodChunks[lod])
                if (chunk.filter != null) chunk.filter.gameObject.SetActive(on);
        }
    }

    // ---------------------------------------------------------------------
    // Geometry
    // ---------------------------------------------------------------------

    void BuildMaterial()
    {
        Shader shader = Shader.Find("Space/VertexColorLine");
        if (shader == null)
        {
            Debug.LogError("[CountryBorderRenderer] Shader 'Space/VertexColorLine' not found; falling back to URP Unlit (borders may render as flat opaque).");
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        _borderMaterial = new Material(shader) { name = "CountryBorderRuntime" };
    }

    void BuildLod(int lod)
    {
        var chunks   = _lodChunks[lod];
        var segments = _lodSegments[lod];

        MeshChunk current      = NewChunk(lod);
        int       currentIndex = chunks.Count - 1;

        var rings = WorldDistrictPolygons.RingsAtLod(lod);
        for (int r = 0; r < rings.Count; r++)
        {
            var ring = rings[r];
            int n    = ring.LatLonFlat.Length / 2;
            if (n < 2) continue;

            var world = new Vector3[n];
            for (int i = 0; i < n; i++)
                world[i] = GeoUtils.LatLonToWorld(ring.LatLonFlat[i * 2], ring.LatLonFlat[i * 2 + 1], kSurfaceRadius);

            for (int i = 0; i < n; i++)
            {
                // Coastline segments (no neighbouring district) are never drawn: only
                // lines that actually separate two districts should render, and skipping
                // the rest removes the majority of the world's ring length.
                if (ring.SegmentNeighbourDistrict[i] == WorldDistricts.None) continue;

                int next = (i + 1) % n;

                if (current.positions.Count + 4 > kMaxVertsPerMesh)
                {
                    current      = NewChunk(lod);
                    currentIndex = chunks.Count - 1;
                }

                Vector3 pA = world[i];
                Vector3 pB = world[next];

                // GeoUtils.LatLonToWorld returns WORLD-space positions but Mesh vertices
                // are read in this GameObject's LOCAL space, and it is parented under
                // Earth's x10 scale — without converting back, that scale would apply a
                // second time and inflate the whole border mesh ~10x.
                Vector3 localA = transform.InverseTransformPoint(pA);
                Vector3 localB = transform.InverseTransformPoint(pB);

                // Side directions are stored, not applied: the shader expands the ribbon
                // by _LineHalfWidth at draw time so thickness can track camera distance.
                Vector3 sideA = transform.InverseTransformDirection(MiterSide(world, n, i)).normalized;
                Vector3 sideB = transform.InverseTransformDirection(MiterSide(world, n, next)).normalized;

                // Earth is centred on this transform's origin, so a point's own direction
                // from the centre is the surface normal there — the shader discards
                // fragments whose normal faces away from the camera.
                Vector3 normA = transform.InverseTransformDirection(pA.normalized).normalized;
                Vector3 normB = transform.InverseTransformDirection(pB.normalized).normalized;

                int baseIdx = current.positions.Count;
                current.positions.Add(localA); current.normals.Add(normA); current.sides.Add(WithSign(sideA, -1f));
                current.positions.Add(localA); current.normals.Add(normA); current.sides.Add(WithSign(sideA, +1f));
                current.positions.Add(localB); current.normals.Add(normB); current.sides.Add(WithSign(sideB, +1f));
                current.positions.Add(localB); current.normals.Add(normB); current.sides.Add(WithSign(sideB, -1f));

                current.colors.Add(AllianceColors.Clear);
                current.colors.Add(AllianceColors.Clear);
                current.colors.Add(AllianceColors.Clear);
                current.colors.Add(AllianceColors.Clear);

                current.triangles.Add(baseIdx + 0);
                current.triangles.Add(baseIdx + 1);
                current.triangles.Add(baseIdx + 2);
                current.triangles.Add(baseIdx + 0);
                current.triangles.Add(baseIdx + 2);
                current.triangles.Add(baseIdx + 3);

                segments.Add(new SegmentRef
                {
                    districtA   = ring.DistrictIdx,
                    districtB   = ring.SegmentNeighbourDistrict[i],
                    chunkIndex  = currentIndex,
                    vertexStart = baseIdx,
                });
            }
        }

        foreach (var chunk in chunks)
            FinalizeChunk(chunk);

        _lodBuilt[lod] = true;
        RecolorLod(lod);
    }

    static Vector4 WithSign(Vector3 side, float sign) => new Vector4(side.x, side.y, side.z, sign);

    MeshChunk NewChunk(int lod)
    {
        var go = new GameObject($"BorderChunk_L{lod}_{_lodChunks[lod].Count}");
        go.transform.SetParent(transform, false);
        go.SetActive(false); // ApplyChunkActivation decides visibility once the LOD is live

        var filter   = go.AddComponent<MeshFilter>();
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial    = _borderMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows    = false;

        var chunk = new MeshChunk { filter = filter };
        _lodChunks[lod].Add(chunk);
        return chunk;
    }

    void FinalizeChunk(MeshChunk chunk)
    {
        var mesh = new Mesh { name = chunk.filter.gameObject.name };
        mesh.indexFormat = chunk.positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(chunk.positions);
        mesh.SetNormals(chunk.normals);
        mesh.SetUVs(0, chunk.sides);
        mesh.SetColors(chunk.colors);
        mesh.SetTriangles(chunk.triangles, 0);
        mesh.RecalculateBounds();

        // Vertices sit on the un-expanded centre line; the shader pushes them out by up
        // to kMaxHalfWidthWorld, so pad the bounds or edge ribbons can be culled early.
        Bounds b = mesh.bounds;
        b.Expand(kMaxHalfWidthWorld * 2f / Mathf.Max(0.0001f, transform.lossyScale.x));
        mesh.bounds = b;

        chunk.mesh = mesh;
        chunk.filter.sharedMesh = mesh;
    }

    // Averages the two segment directions meeting at ring point `i` to produce a mitered
    // perpendicular offset direction in the sphere's tangent plane, so ribbon joints stay
    // visually continuous rather than gapping at corners.
    static Vector3 MiterSide(Vector3[] world, int n, int i)
    {
        Vector3 p      = world[i];
        Vector3 normal = p.normalized;
        Vector3 prev   = world[(i - 1 + n) % n];
        Vector3 next   = world[(i + 1) % n];

        Vector3 dirIn  = (p - prev).normalized;
        Vector3 dirOut = (next - p).normalized;

        Vector3 tangent = dirIn + dirOut;
        tangent = tangent.sqrMagnitude < 1e-8f ? dirOut : tangent.normalized;

        Vector3 side = Vector3.Cross(normal, tangent);
        if (side.sqrMagnitude < 1e-8f) side = Vector3.Cross(normal, dirOut);
        return side.normalized;
    }

    // ---------------------------------------------------------------------
    // Colouring
    // ---------------------------------------------------------------------

    /// <summary>Re-derives every built level's segment colours from TerritoryController state.</summary>
    void RecolorAll()
    {
        if (_lodChunks == null) return;
        for (int lod = 0; lod < _lodChunks.Length; lod++)
            if (_lodBuilt[lod]) RecolorLod(lod);
    }

    // No topology rebuild — only vertex colours are pushed, so this is cheap enough to
    // run on every ownership change. A segment goes fully transparent when both
    // districts share the same owner (an internal line, either a peacetime district
    // boundary or a border erased by annexation); every other segment is the fixed
    // border black.
    void RecolorLod(int lod)
    {
        var chunks    = _lodChunks[lod];
        var territory = TerritoryController.Instance;

        var buffers = new Color32[chunks.Count][];
        for (int c = 0; c < chunks.Count; c++)
            buffers[c] = chunks[c].colors.ToArray();

        foreach (var seg in _lodSegments[lod])
        {
            Color32 color = territory != null &&
                            territory.GetDistrictOwner(seg.districtA) == territory.GetDistrictOwner(seg.districtB)
                ? AllianceColors.Clear
                : kBorderColor;

            Color32[] buf = buffers[seg.chunkIndex];
            buf[seg.vertexStart + 0] = color;
            buf[seg.vertexStart + 1] = color;
            buf[seg.vertexStart + 2] = color;
            buf[seg.vertexStart + 3] = color;
        }

        for (int c = 0; c < chunks.Count; c++)
            chunks[c].mesh.SetColors(buffers[c]);
    }
}
