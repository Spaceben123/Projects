using UnityEngine;

// Drives the Earth's alliance-colour territory fill.
//
// The fill is uploaded ONCE as a point-sampled DISTRICT-INDEX texture (16-bit per
// pixel, 65535 = ocean) plus a tiny district → colour LUT; Earth.shader looks the
// index up in the LUT per fragment. Three consequences matter:
//
//   * No colour muddling. An earlier version built an RGBA texture on the CPU and
//     let the GPU filter it bilinearly, so every boundary blended red into grey
//     across a texel — a soft mixed-colour fringe that never lined up with the
//     crisp vector border drawn on top. Point-sampling an index can only ever
//     return a real district, so a boundary is now a hard edge, antialiased in the
//     shader at screen resolution instead of smeared at texture resolution.
//   * Recolouring is free. An alliance/conquest change rewrites ~800 LUT pixels
//     rather than re-running a multi-million-pixel CPU loop, so territory changes
//     never hitch and the index map can be baked at higher resolution.
//   * One fetch, not two. The LUT is keyed by DISTRICT, so the old
//     index → owner → palette two-hop collapses into a single texture read; the
//     owner resolution happens on the CPU inside Recolor().
//
// The CPU-side index map is retained for hit-testing (GetDistrictAtPixel, used by
// NationSelectionSystem for click-to-select).
public class FactionTextureRenderer : MonoBehaviour
{
    // A 128 x N point-sampled RGBA32 texture rather than a structured buffer:
    // identical behaviour on every graphics API and platform, one fetch, and no
    // compute-buffer lifetime management for a table this small.
    const int kLutWidth = 128;

    // Matches WorldDistricts.None and the baker's 16-bit raster fill value.
    const ushort kNoDistrict = 65535;

    [SerializeField] int _texWidth  = 4096;
    [SerializeField] int _texHeight = 2048;

    Texture2D    _indexTexture;  // R16, point-sampled: district index per pixel
    Texture2D    _lutTexture;    // 128 x N RGBA32: district index -> alliance colour
    ushort[]     _districtIdMap; // CPU copy of the index map, kept for hit-testing
    Color32[]    _lut;
    MeshRenderer _earthRenderer;
    MaterialPropertyBlock _propBlock;

    int _selectedCountryIdx = -1;

    /// <summary>Width of the baked district raster — the authoritative pixel space for hit-testing.</summary>
    public int MapWidth => _texWidth;

    /// <summary>Height of the baked district raster — the authoritative pixel space for hit-testing.</summary>
    public int MapHeight => _texHeight;

    /// <summary>Highlights a country as selected. Kept country-level: selection is a nation-scale concept.</summary>
    public void SelectCountry(int countryIdx)
    {
        if (_selectedCountryIdx == countryIdx) return;
        _selectedCountryIdx = countryIdx;
        Recolor();
    }

    /// <summary>Clears any country selection.</summary>
    public void ClearSelection()
    {
        if (_selectedCountryIdx == -1) return;
        _selectedCountryIdx = -1;
        Recolor();
    }

    public int SelectedCountryIdx => _selectedCountryIdx;

    /// <summary>District index at a raster pixel, or 65535 for ocean / out of range.</summary>
    public ushort GetDistrictAtPixel(int px, int py)
    {
        if (_districtIdMap == null) return kNoDistrict;
        if (px < 0 || py < 0 || px >= _texWidth || py >= _texHeight) return kNoDistrict;
        return _districtIdMap[py * _texWidth + px];
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

        LoadDistrictIdMap();
        BuildTextures();
        Recolor();

        if (ContactStageManager.Instance != null)
            ContactStageManager.Instance.OnStageChanged += OnStageChanged;
        if (TerritoryController.Instance != null)
            TerritoryController.Instance.OnTerritoryChanged += Recolor;
    }

    void OnDestroy()
    {
        if (ContactStageManager.Instance != null)
            ContactStageManager.Instance.OnStageChanged -= OnStageChanged;
        if (TerritoryController.Instance != null)
            TerritoryController.Instance.OnTerritoryChanged -= Recolor;

        if (_indexTexture != null) Destroy(_indexTexture);
        if (_lutTexture   != null) Destroy(_lutTexture);
    }

    void OnStageChanged(ContactStage old, ContactStage next) => Recolor();

    // Resolution is derived from the baked file rather than hard-coded, so the bake can
    // be re-run at a higher resolution (WorldRegionBaker's W/H) without having to keep
    // two constants in sync by hand. The map is always an equirectangular 2:1 raster of
    // 16-bit little-endian district indices — i.e. 4 bytes per height² of area.
    void LoadDistrictIdMap()
    {
        TextAsset baked = Resources.Load<TextAsset>("WorldPolygons/district_map");
        if (baked != null && baked.bytes.Length > 0)
        {
            byte[] raw    = baked.bytes;
            int    pixels = raw.Length / 2;
            int    height = Mathf.RoundToInt(Mathf.Sqrt(pixels / 2f));
            if (height > 0 && height * 2 * height * 2 == raw.Length)
            {
                _texWidth      = height * 2;
                _texHeight     = height;
                _districtIdMap = new ushort[pixels];
                for (int i = 0; i < pixels; i++)
                    _districtIdMap[i] = (ushort)(raw[i * 2] | (raw[i * 2 + 1] << 8));

                Debug.Log($"[FactionTex] Loaded pre-baked district map ({_texWidth}x{_texHeight}, 16-bit).");
                return;
            }

            Debug.LogWarning($"[FactionTex] district_map.bytes size ({raw.Length} bytes) is not a 16-bit 2:1 raster — ignoring.");
        }
        else
        {
            Debug.LogWarning("[FactionTex] No baked district map found — run Assets > SpaceGame > Bake Region Map from Shapefile. Globe will show no faction colours.");
        }

        int total = _texWidth * _texHeight;
        _districtIdMap = new ushort[total];
        for (int i = 0; i < total; i++) _districtIdMap[i] = kNoDistrict;
    }

    void BuildTextures()
    {
        // Point filtering is mandatory: R16 is unsigned-normalised, so any bilinear tap
        // between two district indices produces a value that belongs to neither.
        _indexTexture = new Texture2D(_texWidth, _texHeight, TextureFormat.R16, false)
        {
            name       = "DistrictIndexMap",
            wrapMode   = TextureWrapMode.Repeat,
            filterMode = FilterMode.Point,
        };
        _indexTexture.SetPixelData(_districtIdMap, 0);
        _indexTexture.Apply(false, true); // upload once, then release the GPU-side read copy

        int districtCount = Mathf.Max(1, WorldDistricts.Count);
        int lutHeight     = Mathf.CeilToInt(districtCount / (float)kLutWidth);

        _lutTexture = new Texture2D(kLutWidth, lutHeight, TextureFormat.RGBA32, false)
        {
            name       = "DistrictColorLut",
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point,
        };
        _lut = new Color32[kLutWidth * lutHeight];
    }

    // Rewrites only the district LUT. Ownership and alliance state come from
    // TerritoryController (the single source of truth shared with CountryBorderRenderer)
    // and every colour routes through AllianceColors, so a defection or a single-district
    // conquest shows immediately with no re-bake and no per-pixel work.
    void Recolor()
    {
        var territory = TerritoryController.Instance;
        if (territory == null || _lutTexture == null || _lut == null) return;

        int districtCount = Mathf.Min(WorldDistricts.Count, _lut.Length);
        for (int d = 0; d < districtCount; d++)
            _lut[d] = AllianceColors.ColorFor(territory.GetDistrictAlignment((ushort)d));

        // Padding at the end of the last LUT row must be transparent, not stale colour:
        // an index rounding into it would otherwise tint the ocean.
        for (int d = districtCount; d < _lut.Length; d++)
            _lut[d] = AllianceColors.Clear;

        _lutTexture.SetPixels32(_lut);
        _lutTexture.Apply(false);

        _earthRenderer.GetPropertyBlock(_propBlock);
        _propBlock.SetTexture("_DistrictIdxTex",   _indexTexture);
        _propBlock.SetTexture("_DistrictColorLut", _lutTexture);
        _propBlock.SetVector("_DistrictLutSize", new Vector4(
            _lutTexture.width, _lutTexture.height,
            1f / _lutTexture.width, 1f / _lutTexture.height));
        _earthRenderer.SetPropertyBlock(_propBlock);
    }
}
