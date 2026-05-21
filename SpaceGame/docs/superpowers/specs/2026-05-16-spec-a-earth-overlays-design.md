# Spec A-continuation — Earth Overlays

**Date:** 2026-05-16
**Project:** SpaceGame (Unity 6 URP, `C:\Users\space\ClaudeTest`)
**Scope:** Dynamic faction texture, country border overlay, unified toggle system
**Depends on:** Spec A (GeoUtils, RegionRegistry, ContactStageManager, Earth.shader heatmap slot)
**Not in scope:** Weapon system (Spec B), population heatmap texture (requires external asset)

---

## Overview

Replaces the crude LineRenderer region borders with two shader-blended texture layers:

1. **Faction fill** — CPU-generated texture painting each of the 14 regions with its current faction color. Updates automatically on stage/alignment changes.
2. **Country border overlay** — static equirectangular border-lines image (user-supplied) blended additively, giving crisp country outlines that glow through bloom.

Both layers plus existing city dots and heatmap are individually toggleable at runtime via keyboard shortcuts. The system is optimized via a two-phase bake: region ID map built once on Start, recolored in milliseconds on every faction change.

---

## 1. Shader Additions — `Earth.shader`

Four new uniforms added across all three passes (ForwardLit, ShadowCaster, DepthOnly). Texture declarations in ForwardLit only (outside CBUFFER).

### Properties block
```hlsl
_FactionTex      ("Faction Overlay",  2D)         = "black" {}
_FactionStrength ("Faction Strength", Range(0,1)) = 0.4
_BorderTex       ("Border Overlay",   2D)         = "black" {}
_BorderStrength  ("Border Strength",  Range(0,1)) = 0.6
```

### ForwardLit CBUFFER (after existing `_HeatmapStrength`)
```hlsl
float _FactionStrength;
float _BorderStrength;
```

ShadowCaster and DepthOnly CBUFFERs get the same two floats (SRP Batcher requires identical layout).

### ForwardLit texture declarations (after `_PopulationHeatmap`)
```hlsl
TEXTURE2D(_FactionTex);  SAMPLER(sampler_FactionTex);
TEXTURE2D(_BorderTex);   SAMPLER(sampler_BorderTex);
```

### ForwardLit frag blend order (after heatmap line)
```hlsl
// Faction fill — lerp toward faction color under land/ocean
half4 faction = SAMPLE_TEXTURE2D(_FactionTex, sampler_FactionTex, IN.uv);
color.rgb = lerp(color.rgb, faction.rgb, faction.a * _FactionStrength);

// Border lines — additive white, blooms naturally through post-processing
half border = SAMPLE_TEXTURE2D(_BorderTex, sampler_BorderTex, IN.uv).r * _BorderStrength;
color.rgb += border;
```

### Rules (existing project constraints)
- All HLSL comments ASCII only
- No `static const float PI` declaration
- Use `SAMPLE_TEXTURE2D`, not `tex2D`

---

## 2. FactionTextureRenderer

**File:** `Assets/Scripts/World/FactionTextureRenderer.cs`

MonoBehaviour on `Earth/GlobeOverlays`. Owns the runtime `Texture2D` and drives `Earth.mat._FactionTex`.

### Two-phase design (performance)

**Phase 1 — Region ID bake (once, on Start, ~100ms)**

Allocates a `byte[] regionIdMap` of size `width × height` (2048×1024 = 2M entries). For each pixel:
1. Convert pixel → lat/lon via inverse of `GeoUtils.LatLonToUV`
2. Check each region's bounding box (`RegionDefinition.boundary` flat lat/lon pairs → derive min/max lat/lon)
3. If multiple regions claim the pixel → assign the one with smallest bounding-box area (most specific)
4. If no region claims the pixel → assign the nearest region by great-circle distance to its capital lat/lon
5. Store region index (0–13) in `regionIdMap[i]`

**Phase 2 — Recolor (on demand, ~5ms)**

Allocates `Color32[] pixels` once. On `Start()` and on every `ContactStageManager.OnStageChanged`:
1. Iterate `regionIdMap`, look up current `FactionAlignment` for region index
2. Write faction color + alpha (0.5f) into `pixels[i]`
3. Call `_factionTexture.SetPixels32(pixels)` then `Apply(false)` (no mipmap recalc)
4. Assign to `Earth.mat._FactionTex`

Phase 2 only re-runs when alignment actually changes — no per-frame cost.

### Faction color palette
| Alignment     | RGBA                        | Visual intent            |
|---------------|-----------------------------|--------------------------|
| NATO          | (0.25, 0.50, 0.95, 0.50)   | Cool blue, semi-transparent |
| BRICS         | (0.90, 0.25, 0.20, 0.50)   | Warm red                 |
| NonAligned    | (0.55, 0.55, 0.55, 0.40)   | Neutral grey, more subtle |
| SuperNation   | (0.85, 0.90, 0.15, 0.60)   | Bright gold (Stage 6)    |
| Collapsed     | (0.12, 0.06, 0.06, 0.65)   | Dark charcoal (Stage 7)  |

Alpha is baked into the Color32 so `_FactionStrength` acts as a global master dim (toggleable).

### Texture settings
- Format: `TextureFormat.RGBA32`
- Filter: `Bilinear` (softens hard bounding-box edges at region boundaries)
- Wrap: `Repeat` (equirectangular wraps at antimeridian)
- Mips: none (`false` on Apply) — not needed for this use

---

## 3. Toggle System — `GlobeOverlayToggle`

**File:** `Assets/Scripts/World/GlobeOverlayToggle.cs`

MonoBehaviour on `Earth/GlobeOverlays`. Reads keyboard input each frame and routes to each layer.

### Toggleable layers
| Key | Layer            | Controls                              |
|-----|------------------|---------------------------------------|
| F1  | Faction overlay  | `_FactionStrength` → 0.4 or 0         |
| F2  | Border overlay   | `_BorderStrength` → 0.6 or 0          |
| F3  | City dots        | `CityDotRenderer.SetVisible(bool)`    |
| F4  | Population heatmap | `_HeatmapStrength` → target or 0    |

Each toggle is independent. State tracked as `bool[]` flags. Material property updates go through `Earth.mat.SetFloat(...)` — no Instantiate.

### API (for future UI buttons)
```csharp
public void SetFactionVisible(bool v);
public void SetBordersVisible(bool v);
public void SetCitiesVisible(bool v);
public void SetHeatmapVisible(bool v);
public void ToggleFaction();   // flips current state
public void ToggleBorders();
```

---

## 4. Scene Changes

- `RegionBorderRenderer` component **removed** from `Earth/GlobeOverlays` (script file kept — reused by Spec B for launch site markers)
- `FactionTextureRenderer` added to `Earth/GlobeOverlays`
- `GlobeOverlayToggle` added to `Earth/GlobeOverlays`
- `Earth.mat` updated with `_BorderTex` slot (user assigns border texture in Inspector after import)

---

## 5. Required Asset — Border Texture

**`Assets/Textures/Earth/Earth_Borders_8K.png`**

User-supplied equirectangular country-border image (white lines on black background, 8192×4096 recommended).

Free source: Natural Earth `ne_10m_admin_0_countries` rendered to equirectangular, or any "world outline map" PNG with white-on-black borders. The bloom post-processing will give border lines a subtle glow automatically.

If no texture is assigned, `_BorderTex` defaults to black — faction texture still works fine without it.

---

## 6. File Layout

```
Assets/
├── Scripts/World/
│   ├── FactionTextureRenderer.cs    (new)
│   └── GlobeOverlayToggle.cs        (new)
├── Shaders/
│   └── Earth.shader                 (modify — add faction + border uniforms)
└── Textures/Earth/
    └── Earth_Borders_8K.png         (user-supplied, optional)
```

---

## Self-Review

- No TBDs or placeholders.
- SRP Batcher constraint respected — float uniforms in all 3 CBUFFERs.
- Existing `_HeatmapStrength` + `_PopulationHeatmap` untouched.
- Two-phase bake avoids per-frame CPU cost.
- Toggle API exposed for future UI (Spec B weapon menu can call these).
- Border texture optional — system degrades gracefully without it.
- `RegionBorderRenderer` file preserved for Spec B reuse.
