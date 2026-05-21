using UnityEngine;

public class FactionTextureRenderer : MonoBehaviour
{
    [SerializeField] int _texWidth  = 2048;
    [SerializeField] int _texHeight = 1024;

    Texture2D    _factionTexture;
    byte[]       _regionIdMap;
    Color32[]    _pixels;
    MeshRenderer _earthRenderer;
    MaterialPropertyBlock _propBlock;

    static readonly Color32 s_natoColor       = new Color32(64,  128, 242, 128);
    static readonly Color32 s_bricsColor       = new Color32(230, 64,  51,  128);
    static readonly Color32 s_nonAlignedColor  = new Color32(140, 140, 140, 102);
    static readonly Color32 s_superNationColor = new Color32(217, 230, 38,  153);
    static readonly Color32 s_collapsedColor   = new Color32(31,  15,  15,  166);
    static readonly Color32 s_clearColor       = new Color32(0,   0,   0,   0);

    Texture2D _borderTexture;

    void Start()
    {
        _earthRenderer = transform.parent?.GetComponent<MeshRenderer>();
        if (_earthRenderer == null)
        {
            Debug.LogError("[FactionTex] Earth MeshRenderer not found on parent.");
            return;
        }
        _propBlock = new MaterialPropertyBlock();

        _factionTexture = new Texture2D(_texWidth, _texHeight, TextureFormat.RGBA32, false);
        _factionTexture.wrapMode   = TextureWrapMode.Repeat;
        _factionTexture.filterMode = FilterMode.Bilinear;
        _pixels = new Color32[_texWidth * _texHeight];

        LoadOrBakeRegionIdMap();
        BakeBorderTexture();
        Recolor();

        if (ContactStageManager.Instance != null)
            ContactStageManager.Instance.OnStageChanged += OnStageChanged;
    }

    void OnDestroy()
    {
        if (ContactStageManager.Instance != null)
            ContactStageManager.Instance.OnStageChanged -= OnStageChanged;
        if (_factionTexture != null)
            Destroy(_factionTexture);
        if (_borderTexture != null)
            Destroy(_borderTexture);
    }

    void OnStageChanged(ContactStage old, ContactStage next) => Recolor();

    void LoadOrBakeRegionIdMap()
    {
        int total = _texWidth * _texHeight;
        TextAsset baked = Resources.Load<TextAsset>("WorldPolygons/region_map");
        if (baked != null && baked.bytes.Length == total)
        {
            _regionIdMap = (byte[])baked.bytes.Clone();
            Debug.Log("[FactionTex] Loaded pre-baked region map (accurate).");
            return;
        }
        Debug.Log("[FactionTex] No pre-baked map found — falling back to bounding-box regions. Run Assets > SpaceGame > Bake Region Map from GeoJSON for accurate borders.");
        BakeRegionIdMap();
    }

    void BakeBorderTexture()
    {
        int W = _texWidth, H = _texHeight;
        _borderTexture = new Texture2D(W, H, TextureFormat.RGBA32, false);
        _borderTexture.wrapMode   = TextureWrapMode.Clamp;
        _borderTexture.filterMode = FilterMode.Bilinear;

        byte[] borderPx = new byte[W * H];
        for (int py = 0; py < H; py++)
        {
            for (int px = 0; px < W; px++)
            {
                int i = py * W + px;
                byte id = _regionIdMap[i];
                if (id == 255) continue; // ocean — no border pixel

                bool b = false;
                if (px > 0)     b |= _regionIdMap[i - 1]     != id;
                if (px < W - 1) b |= _regionIdMap[i + 1]     != id;
                if (py > 0)     b |= _regionIdMap[i - W]     != id;
                if (py < H - 1) b |= _regionIdMap[i + W]     != id;
                if (b) borderPx[i] = 255;
            }
        }

        // Shader samples .r channel only; G/B/A unused
        Color32[] c = new Color32[W * H];
        for (int i = 0; i < c.Length; i++)
            c[i] = new Color32(borderPx[i], 0, 0, 255);
        _borderTexture.SetPixels32(c);
        _borderTexture.Apply(false);

        // Assign once — border texture never changes (faction colours change, not geometry)
        _earthRenderer.GetPropertyBlock(_propBlock);
        _propBlock.SetTexture("_BorderTex", _borderTexture);
        _earthRenderer.SetPropertyBlock(_propBlock);
        Debug.Log("[FactionTex] Border texture baked.");
    }

    void BakeRegionIdMap()
    {
        var registry = RegionRegistry.Instance;
        if (registry == null) { Debug.LogWarning("[FactionTex] No RegionRegistry."); return; }

        int count = registry.Regions.Length;
        int total = _texWidth * _texHeight;
        _regionIdMap = new byte[total];

        float[][] boundaries = new float[count][];
        for (int r = 0; r < count; r++)
            boundaries[r] = registry.Regions[r]?.Def?.boundary;

        for (int i = 0; i < total; i++)
        {
            int   px  = i % _texWidth;
            int   py  = i / _texWidth;
            float lat = -90f + (py + 0.5f) / _texHeight * 180f;
            float lon = (px + 0.5f) / _texWidth * 360f - 180f;

            byte best = 255;
            for (int r = 0; r < count; r++)
            {
                if (boundaries[r] != null && PointInPolygon(lat, lon, boundaries[r]))
                {
                    best = (byte)r;
                    break;
                }
            }
            _regionIdMap[i] = best;
        }

        Debug.Log("[FactionTex] Region ID map baked (point-in-polygon).");
    }

    static bool PointInPolygon(float lat, float lon, float[] ring)
    {
        int  n      = ring.Length / 2;
        bool inside = false;
        int  j      = n - 1;
        for (int i = 0; i < n; i++)
        {
            float yi = ring[i * 2], xi = ring[i * 2 + 1];
            float yj = ring[j * 2], xj = ring[j * 2 + 1];
            if ((yi > lat) != (yj > lat) &&
                lon < (xj - xi) * (lat - yi) / (yj - yi) + xi)
                inside = !inside;
            j = i;
        }
        return inside;
    }

    void Recolor()
    {
        var registry = RegionRegistry.Instance;
        if (registry == null) return;
        if (_regionIdMap == null) { Debug.LogError("[FactionTex] Recolor called but region ID map was not baked."); return; }

        int count = registry.Regions.Length;
        Color32[] lookup = new Color32[count];
        for (int r = 0; r < count; r++)
        {
            var region = registry.Regions[r];
            lookup[r] = region != null ? AlignmentColor(region.Alignment) : s_clearColor;
        }

        int total = _texWidth * _texHeight;
        for (int i = 0; i < total; i++)
        {
            byte id = _regionIdMap[i];
            _pixels[i] = id < count ? lookup[id] : s_clearColor;
        }

        _factionTexture.SetPixels32(_pixels);
        _factionTexture.Apply(false);

        _earthRenderer.GetPropertyBlock(_propBlock);
        _propBlock.SetTexture("_FactionTex", _factionTexture);
        _earthRenderer.SetPropertyBlock(_propBlock);
    }

    static Color32 AlignmentColor(FactionAlignment a) => a switch
    {
        FactionAlignment.NATO        => s_natoColor,
        FactionAlignment.BRICS       => s_bricsColor,
        FactionAlignment.NonAligned  => s_nonAlignedColor,
        FactionAlignment.SuperNation => s_superNationColor,
        FactionAlignment.Collapsed   => s_collapsedColor,
        _                            => s_clearColor,
    };
}
