using UnityEngine;

public class FactionTextureRenderer : MonoBehaviour
{
    [SerializeField] int _texWidth  = 2048;
    [SerializeField] int _texHeight = 1024;

    Texture2D    _factionTexture;
    byte[]       _regionIdMap;  // stores country index per pixel (0-N), 255=ocean
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

    int _selectedCountryIdx = -1;

    public void SelectCountry(int countryIdx)
    {
        _selectedCountryIdx = countryIdx;
        Recolor();
    }

    public void ClearSelection()
    {
        _selectedCountryIdx = -1;
        Recolor();
    }

    public int SelectedCountryIdx => _selectedCountryIdx;

    public byte GetCountryAtPixel(int px, int py)
    {
        if (_regionIdMap == null) return 255;
        int idx = py * _texWidth + px;
        if (idx < 0 || idx >= _regionIdMap.Length) return 255;
        return _regionIdMap[idx];
    }

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

        LoadRegionIdMap();
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

    void LoadRegionIdMap()
    {
        int total = _texWidth * _texHeight;
        TextAsset baked = Resources.Load<TextAsset>("WorldPolygons/region_map");
        if (baked != null && baked.bytes.Length == total)
        {
            _regionIdMap = (byte[])baked.bytes.Clone();
            Debug.Log("[FactionTex] Loaded pre-baked country map.");
            return;
        }
        Debug.LogWarning("[FactionTex] No baked country map found — run Assets > SpaceGame > Bake Region Map from Shapefile. Globe will show no faction colours.");
        _regionIdMap = new byte[total];
        for (int i = 0; i < total; i++) _regionIdMap[i] = 255;
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
                if (px > 0)     b |= _regionIdMap[i - 1] != id;
                if (px < W - 1) b |= _regionIdMap[i + 1] != id;
                if (py > 0)     b |= _regionIdMap[i - W] != id;
                if (py < H - 1) b |= _regionIdMap[i + W] != id;
                if (b) borderPx[i] = 255;
            }
        }

        // Shader samples .r channel only
        Color32[] c = new Color32[W * H];
        for (int i = 0; i < c.Length; i++)
            c[i] = new Color32(borderPx[i], 0, 0, 255);
        _borderTexture.SetPixels32(c);
        _borderTexture.Apply(false);

        _earthRenderer.GetPropertyBlock(_propBlock);
        _propBlock.SetTexture("_BorderTex", _borderTexture);
        _earthRenderer.SetPropertyBlock(_propBlock);
        Debug.Log("[FactionTex] Border texture baked.");
    }

    void Recolor()
    {
        var registry = RegionRegistry.Instance;
        if (registry == null) return;
        if (_regionIdMap == null)
        {
            Debug.LogError("[FactionTex] Recolor called but region ID map is null.");
            return;
        }

        int regionCount = registry.Regions.Length; // 14 macro-regions

        // Region index → faction color
        Color32[] regionColors = new Color32[regionCount];
        for (int r = 0; r < regionCount; r++)
        {
            var region = registry.Regions[r];
            regionColors[r] = region != null ? AlignmentColor(region.Alignment) : s_clearColor;
        }

        // Country index (byte 0-254) → faction color via region lookup
        Color32[] countryColors = new Color32[256];
        for (int c = 0; c < 255; c++)
        {
            byte regionIdx = WorldRegionMapper.GetRegionForCountry((byte)c);
            if (regionIdx >= regionCount) { countryColors[c] = s_clearColor; continue; }

            Color32 baseColor = regionColors[regionIdx];
            if (c == _selectedCountryIdx)
            {
                // Selected: boost to fully opaque
                countryColors[c] = new Color32(baseColor.r, baseColor.g, baseColor.b, 255);
            }
            else
            {
                countryColors[c] = baseColor;
            }
        }
        countryColors[255] = s_clearColor;

        int total = _texWidth * _texHeight;
        for (int i = 0; i < total; i++)
            _pixels[i] = countryColors[_regionIdMap[i]];

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
