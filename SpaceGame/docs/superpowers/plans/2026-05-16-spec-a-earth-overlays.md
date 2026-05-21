# Earth Overlays Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace LineRenderer region borders with a dynamic CPU-generated faction color texture and a static country-border overlay, both blended in Earth.shader, with F1–F4 keyboard toggles for all globe layers.

**Architecture:** Three-layer approach in Earth.shader (faction fill → border lines → existing heatmap). `FactionTextureRenderer` bakes a region-ID map once on Start, then recolors in ~5ms on every faction/stage change. `GlobeOverlayToggle` handles keyboard input and exposes a clean API for future UI. No per-frame CPU cost after startup.

**Tech Stack:** Unity 6 URP, HLSL, C#, Unity Test Framework. Project root: `C:\Users\space\ClaudeTest`. Earth scale: 10 units radius. All HLSL comments ASCII-only. No `static const float PI`. Use `SAMPLE_TEXTURE2D` not `tex2D`.

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `Assets/Shaders/Earth.shader` | Modify | Add `_FactionTex`, `_FactionStrength`, `_BorderTex`, `_BorderStrength` uniforms and blend logic |
| `Assets/Scripts/World/FactionTextureRenderer.cs` | Create | Two-phase faction texture bake — region ID map on Start, recolor on alignment change |
| `Assets/Scripts/World/GlobeOverlayToggle.cs` | Create | F1–F4 keyboard toggles + public API for all four globe layers |
| `Assets/Scenes/SampleScene.unity` | Modify | Remove `RegionBorderRenderer`, add `FactionTextureRenderer` + `GlobeOverlayToggle` to `Earth/GlobeOverlays` |

---

## Task 1: Earth.shader — Add Faction + Border Texture Slots

**Files:**
- Modify: `Assets/Shaders/Earth.shader`

SRP Batcher requires identical CBUFFER layout across all three passes. Add the two new float uniforms to all three CBUFFERs. Add texture declarations and blend logic only in ForwardLit.

- [ ] **Step 1: Add properties to Properties block**

In `Assets/Shaders/Earth.shader`, after line 18 (`_HeatmapStrength` line), add:

```hlsl
        _FactionTex      ("Faction Overlay",  2D)         = "black" {}
        _FactionStrength ("Faction Strength", Range(0,1)) = 0.4
        _BorderTex       ("Border Overlay",   2D)         = "black" {}
        _BorderStrength  ("Border Strength",  Range(0,1)) = 0.6
```

- [ ] **Step 2: Add TEXTURE2D declarations in ForwardLit pass**

After line 46 (`TEXTURE2D(_PopulationHeatmap); SAMPLER(sampler_PopulationHeatmap);`), add:

```hlsl
            TEXTURE2D(_FactionTex);  SAMPLER(sampler_FactionTex);
            TEXTURE2D(_BorderTex);   SAMPLER(sampler_BorderTex);
```

- [ ] **Step 3: Add float uniforms to ForwardLit CBUFFER**

In the ForwardLit CBUFFER (line 48–57), after `_HeatmapStrength`, add:

```hlsl
                float  _FactionStrength;
                float  _BorderStrength;
```

- [ ] **Step 4: Add float uniforms to ShadowCaster CBUFFER**

In the ShadowCaster CBUFFER (lines 155–164), after `_HeatmapStrength`, add:

```hlsl
                float  _FactionStrength;
                float  _BorderStrength;
```

- [ ] **Step 5: Add float uniforms to DepthOnly CBUFFER**

In the DepthOnly CBUFFER (lines 204–213), after `_HeatmapStrength`, add:

```hlsl
                float  _FactionStrength;
                float  _BorderStrength;
```

- [ ] **Step 6: Add blend logic in ForwardLit frag**

After line 131 (the heatmap line: `color.rgb += heat * ...`), before `return half4(color, 1.0);`, add:

```hlsl
                half4 faction = SAMPLE_TEXTURE2D(_FactionTex, sampler_FactionTex, IN.uv);
                color.rgb = lerp(color.rgb, faction.rgb, faction.a * _FactionStrength);

                half border = SAMPLE_TEXTURE2D(_BorderTex, sampler_BorderTex, IN.uv).r * _BorderStrength;
                color.rgb += border;
```

- [ ] **Step 7: Verify shader compiles**

In Unity: select `Assets/Materials/Earth.mat` in Project window. Inspector should show no pink/error. Console should be clear. The two new float sliders (`Faction Strength`, `Border Strength`) and two new texture slots (`Faction Overlay`, `Border Overlay`) should appear in the material inspector.

- [ ] **Step 8: Commit**

```
git add Assets/Shaders/Earth.shader
git commit -m "feat: add faction fill and border line texture slots to Earth shader"
```

---

## Task 2: FactionTextureRenderer

**Files:**
- Create: `Assets/Scripts/World/FactionTextureRenderer.cs`

Two-phase design:
- **Phase 1** (Start, once): bake `byte[] _regionIdMap` — for each pixel compute lat/lon, find owning region by bounding box, fall back to nearest capital. ~100ms, runs once.
- **Phase 2** (Start + OnStageChanged): iterate `_regionIdMap`, write faction Color32 per region, upload via `SetPixels32` + `Apply(false)`. ~5ms, runs only on alignment change.

- [ ] **Step 1: Create FactionTextureRenderer.cs**

Create `Assets/Scripts/World/FactionTextureRenderer.cs`:

```csharp
using UnityEngine;

public class FactionTextureRenderer : MonoBehaviour
{
    [SerializeField] int _texWidth  = 2048;
    [SerializeField] int _texHeight = 1024;

    Texture2D _factionTexture;
    byte[]    _regionIdMap;
    Color32[] _pixels;
    Material  _earthMat;

    static readonly Color32 s_natoColor        = new Color32(64,  128, 242, 128);
    static readonly Color32 s_bricsColor        = new Color32(230, 64,  51,  128);
    static readonly Color32 s_nonAlignedColor   = new Color32(140, 140, 140, 102);
    static readonly Color32 s_superNationColor  = new Color32(217, 230, 38,  153);
    static readonly Color32 s_collapsedColor    = new Color32(31,  15,  15,  166);
    static readonly Color32 s_clearColor        = new Color32(0,   0,   0,   0);

    void Start()
    {
        _earthMat = GameObject.Find("Earth")?.GetComponent<MeshRenderer>()?.sharedMaterial;
        if (_earthMat == null)
        {
            Debug.LogError("[FactionTex] Earth MeshRenderer not found.");
            return;
        }

        _factionTexture = new Texture2D(_texWidth, _texHeight, TextureFormat.RGBA32, false);
        _factionTexture.wrapMode   = TextureWrapMode.Repeat;
        _factionTexture.filterMode = FilterMode.Bilinear;
        _pixels = new Color32[_texWidth * _texHeight];

        BakeRegionIdMap();
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
    }

    void OnStageChanged(ContactStage old, ContactStage next) => Recolor();

    void BakeRegionIdMap()
    {
        var registry = RegionRegistry.Instance;
        if (registry == null) { Debug.LogWarning("[FactionTex] No RegionRegistry."); return; }

        int count = registry.Regions.Length;
        int total = _texWidth * _texHeight;
        _regionIdMap = new byte[total];

        float[] minLat = new float[count];
        float[] maxLat = new float[count];
        float[] minLon = new float[count];
        float[] maxLon = new float[count];
        float[] area   = new float[count];

        for (int r = 0; r < count; r++)
        {
            var def = registry.Regions[r]?.Def;
            if (def == null) { minLat[r] = 9999f; continue; }
            ComputeBounds(def.boundary, out minLat[r], out maxLat[r], out minLon[r], out maxLon[r]);
            area[r] = (maxLat[r] - minLat[r]) * (maxLon[r] - minLon[r]);
        }

        for (int i = 0; i < total; i++)
        {
            int   px  = i % _texWidth;
            int   py  = i / _texWidth;
            float u   = (px + 0.5f) / _texWidth;
            float v   = (py + 0.5f) / _texHeight;
            float lat =  90f - v * 180f;
            float lon = u * 360f - 180f;

            int   bestRegion = -1;
            float bestArea   = float.MaxValue;

            for (int r = 0; r < count; r++)
            {
                if (minLat[r] > 999f) continue;
                if (lat >= minLat[r] && lat <= maxLat[r] &&
                    lon >= minLon[r] && lon <= maxLon[r])
                {
                    if (area[r] < bestArea) { bestArea = area[r]; bestRegion = r; }
                }
            }

            if (bestRegion < 0)
            {
                float bestDist = float.MaxValue;
                for (int r = 0; r < count; r++)
                {
                    var def = registry.Regions[r]?.Def;
                    if (def == null) continue;
                    float d = GreatCircleDeg(lat, lon, def.capitalLat, def.capitalLon);
                    if (d < bestDist) { bestDist = d; bestRegion = r; }
                }
            }

            _regionIdMap[i] = bestRegion >= 0 ? (byte)bestRegion : (byte)255;
        }

        Debug.Log("[FactionTex] Region ID map baked.");
    }

    void Recolor()
    {
        var registry = RegionRegistry.Instance;
        if (registry == null || _regionIdMap == null) return;

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
        _earthMat.SetTexture("_FactionTex", _factionTexture);
    }

    static void ComputeBounds(float[] boundary,
        out float minLat, out float maxLat,
        out float minLon, out float maxLon)
    {
        minLat = float.MaxValue; maxLat = float.MinValue;
        minLon = float.MaxValue; maxLon = float.MinValue;
        if (boundary == null || boundary.Length < 2) return;
        for (int i = 0; i + 1 < boundary.Length; i += 2)
        {
            float lat = boundary[i], lon = boundary[i + 1];
            if (lat < minLat) minLat = lat;
            if (lat > maxLat) maxLat = lat;
            if (lon < minLon) minLon = lon;
            if (lon > maxLon) maxLon = lon;
        }
    }

    static float GreatCircleDeg(float lat1, float lon1, float lat2, float lon2)
    {
        float dLat = (lat2 - lat1) * Mathf.Deg2Rad;
        float dLon = (lon2 - lon1) * Mathf.Deg2Rad;
        float a = Mathf.Sin(dLat * 0.5f) * Mathf.Sin(dLat * 0.5f)
                + Mathf.Cos(lat1 * Mathf.Deg2Rad) * Mathf.Cos(lat2 * Mathf.Deg2Rad)
                  * Mathf.Sin(dLon * 0.5f) * Mathf.Sin(dLon * 0.5f);
        return Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(Mathf.Max(0f, 1f - a))) * Mathf.Rad2Deg;
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
```

- [ ] **Step 2: Verify no compile errors**

In Unity, wait for recompile. Console should show zero errors. `FactionTextureRenderer` should appear in the Add Component menu.

- [ ] **Step 3: Commit**

```
git add Assets/Scripts/World/FactionTextureRenderer.cs
git commit -m "feat: add FactionTextureRenderer -- two-phase CPU faction color bake"
```

---

## Task 3: GlobeOverlayToggle

**Files:**
- Create: `Assets/Scripts/World/GlobeOverlayToggle.cs`

Handles F1–F4 keyboard input and exposes public toggle API. Sits on `Earth/GlobeOverlays` alongside `CityDotRenderer`, so `GetComponent<CityDotRenderer>()` works directly.

- [ ] **Step 1: Create GlobeOverlayToggle.cs**

Create `Assets/Scripts/World/GlobeOverlayToggle.cs`:

```csharp
using UnityEngine;

public class GlobeOverlayToggle : MonoBehaviour
{
    [Header("Default strengths when ON")]
    [SerializeField] float _factionStrength = 0.4f;
    [SerializeField] float _borderStrength  = 0.6f;
    [SerializeField] float _heatmapStrength = 0.5f;

    bool _factionOn = true;
    bool _bordersOn = true;
    bool _citiesOn  = true;
    bool _heatmapOn = false;

    Material          _earthMat;
    CityDotRenderer   _cityDots;

    void Start()
    {
        _earthMat = GameObject.Find("Earth")?.GetComponent<MeshRenderer>()?.sharedMaterial;
        _cityDots = GetComponent<CityDotRenderer>();

        if (_earthMat == null)
            Debug.LogWarning("[GlobeToggle] Earth material not found.");

        ApplyAll();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) ToggleFaction();
        if (Input.GetKeyDown(KeyCode.F2)) ToggleBorders();
        if (Input.GetKeyDown(KeyCode.F3)) ToggleCities();
        if (Input.GetKeyDown(KeyCode.F4)) ToggleHeatmap();
    }

    public void ToggleFaction()  => SetFactionVisible(!_factionOn);
    public void ToggleBorders()  => SetBordersVisible(!_bordersOn);
    public void ToggleCities()   => SetCitiesVisible(!_citiesOn);
    public void ToggleHeatmap()  => SetHeatmapVisible(!_heatmapOn);

    public void SetFactionVisible(bool v)
    {
        _factionOn = v;
        _earthMat?.SetFloat("_FactionStrength", v ? _factionStrength : 0f);
    }

    public void SetBordersVisible(bool v)
    {
        _bordersOn = v;
        _earthMat?.SetFloat("_BorderStrength", v ? _borderStrength : 0f);
    }

    public void SetCitiesVisible(bool v)
    {
        _citiesOn = v;
        _cityDots?.SetVisible(v);
    }

    public void SetHeatmapVisible(bool v)
    {
        _heatmapOn = v;
        _earthMat?.SetFloat("_HeatmapStrength", v ? _heatmapStrength : 0f);
    }

    void ApplyAll()
    {
        SetFactionVisible(_factionOn);
        SetBordersVisible(_bordersOn);
        SetCitiesVisible(_citiesOn);
        SetHeatmapVisible(_heatmapOn);
    }
}
```

- [ ] **Step 2: Verify no compile errors**

Wait for Unity recompile. Zero errors in console. `GlobeOverlayToggle` appears in Add Component menu.

- [ ] **Step 3: Commit**

```
git add Assets/Scripts/World/GlobeOverlayToggle.cs
git commit -m "feat: add GlobeOverlayToggle -- F1-F4 keyboard toggles for all globe layers"
```

---

## Task 4: Scene Wiring

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity` (via MCP Unity)

Remove the old `RegionBorderRenderer` component (LineRenderers), add the two new scripts to `Earth/GlobeOverlays`.

- [ ] **Step 1: Remove RegionBorderRenderer from GlobeOverlays**

Via MCP Unity `update_component` or manually in Inspector:
Remove `RegionBorderRenderer` component from `Earth/GlobeOverlays`.

*(In Inspector: right-click `RegionBorderRenderer` component header → Remove Component)*

- [ ] **Step 2: Add FactionTextureRenderer to GlobeOverlays**

Via MCP: `update_component` on `Earth/GlobeOverlays`, componentName `FactionTextureRenderer`.

- [ ] **Step 3: Add GlobeOverlayToggle to GlobeOverlays**

Via MCP: `update_component` on `Earth/GlobeOverlays`, componentName `GlobeOverlayToggle`.

- [ ] **Step 4: Verify scene hierarchy**

`Earth/GlobeOverlays` should now have:
- `FactionTextureRenderer`
- `GlobeOverlayToggle`
- `CityDotRenderer`
- NO `RegionBorderRenderer`

- [ ] **Step 5: Save scene**

Via MCP `save_scene` or Ctrl+S in Unity.

- [ ] **Step 6: Commit**

```
git add Assets/Scenes/SampleScene.unity
git commit -m "feat: wire FactionTextureRenderer + GlobeOverlayToggle into scene, remove LineRenderer borders"
```

---

## Task 5: Play Mode Verification

**No code changes — verification only.**

- [ ] **Step 1: Enter Play mode**

Press Ctrl+P in Unity (or Play button).

Expected console output:
```
[RegionRegistry] Loaded 14 regions.
[FactionTex] Region ID map baked.
```
No errors.

- [ ] **Step 2: Verify faction colors on globe**

In Game view: Earth surface should show semi-transparent colored regions:
- North America / W. Europe / Oceania → blue (NATO)
- Russia / E. Asia → red (BRICS)
- Africa / Middle East / S. America → grey (NonAligned)

City gold dots still visible. No LineRenderer borders.

- [ ] **Step 3: Test F1–F4 toggles**

Press each key and confirm:
- **F1** → faction color overlay disappears / reappears
- **F2** → border lines disappear / reappear (if border texture assigned; otherwise no visible change)
- **F3** → city gold dots disappear / reappear
- **F4** → heatmap tint toggles (starts OFF by default)

- [ ] **Step 4: Test stage change updates faction**

In Play mode, open Unity Console and run via script or context menu:
```csharp
ContactStageManager.Instance.RegisterShipAction("move", 0.3f, true, 0.9f);
```
Observe fear level rising in the WorldSimulation HUD (top-left). When fear crosses Stage 6 threshold, all regions should flip to gold (SuperNation).

*(Optional manual test — not required to ship this task)*

---

## Self-Review

**Spec coverage:**
- [x] `_FactionTex` + `_FactionStrength` in all 3 CBUFFERs (Tasks 1 steps 3-5)
- [x] `_BorderTex` + `_BorderStrength` in all 3 CBUFFERs (Tasks 1 steps 3-5)
- [x] Blend order: faction fill → border lines → (heatmap already existed) (Task 1 step 6)
- [x] Two-phase bake: region ID map once, recolor on change (Task 2)
- [x] `OnStageChanged` subscription drives recolor (Task 2)
- [x] Faction color palette matches spec (Task 2 static fields)
- [x] Toggle system F1–F4 (Task 3)
- [x] Public API `SetFactionVisible` / `SetBordersVisible` / `SetCitiesVisible` / `SetHeatmapVisible` (Task 3)
- [x] `RegionBorderRenderer` removed from scene, file preserved (Task 4 step 1)
- [x] Border texture optional — black default = graceful no-op (Task 1, Properties default `"black"`)
- [x] SRP Batcher: identical CBUFFER layout across all 3 passes (Tasks 1 steps 3-5)

**Type consistency:**
- `FactionTextureRenderer.Recolor()` called from `Start()` and `OnStageChanged` — consistent
- `GlobeOverlayToggle` uses `_earthMat.SetFloat("_FactionStrength", ...)` — matches shader property name
- `GlobeOverlayToggle` uses `_earthMat.SetFloat("_BorderStrength", ...)` — matches shader property name
- `GlobeOverlayToggle` uses `_earthMat.SetFloat("_HeatmapStrength", ...)` — matches existing shader property
- `CityDotRenderer.SetVisible(bool)` — method exists in Spec A Task 14 implementation

**No placeholders:** All steps contain complete code or exact instructions.
