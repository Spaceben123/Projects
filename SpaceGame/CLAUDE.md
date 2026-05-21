# SpaceGame — Claude Code Project Reference

## Project

Unity 6 URP space game. Current: photorealistic Earth + atmosphere. Planned: intergalactic warfare, civ growth, orbital mechanics, nation borders, population tracking, atmosphere/ocean destruction, reentry effects.

## Unity Setup

- Engine: Unity 6, URP
- MCP: `com.gamelovers.mcp-unity`, WebSocket `localhost:8090`
  - MCP reconnect: `cd Assets/../Library/PackageCache/com.gamelovers.mcp-unity@.../Server~ && npm install && npm run build`
- Scene: `Assets/Scenes/SampleScene.unity`
- Gravity: `(0,0,0)` in `ProjectSettings/DynamicsManager.asset` — space, no gravity

---

## Scene Hierarchy

```
SampleScene
├── Main Camera
├── Sun                          ← Directional Light, 5778 K, intensity 3
├── Earth                        ← scale (10,10,10), no Rigidbody
│   └── Atmosphere               ← child, local scale (1.0173,1.0173,1.0173)
├── Moon                         ← scale (2.727,2.727,2.727), pos (301.7,0,0)
└── PostProcessVolume            ← Global Volume, created by SpaceLightingSetup.cs
```

Scale: 1 unit = 637.1 km (Earth r=6371 km = 10 units)
- Moon 2.727 = 1737/6371 (real ratio)
- Moon dist 301.7 = 384 400 km ÷ 2 / 637.1

---

## Built Systems

### Earth Mesh — `Assets/Scripts/Planet/UVSphere.cs`

Procedural UV sphere, `[ExecuteInEditMode]`.

- Vertices: `(latSegments+1) × (lonSegments+1)` — extra col closes UV seam
- Normals: analytical per-vertex (outward), not recalculated
- UVs: `(lon/lonSegments, 1 - lat/latSegments)` — equirectangular
- Winding: CCW from outside (`a,c,b` / `c,d,b`) — Cull Back correct
- Tangents: `mesh.RecalculateTangents()` — required for normal map TBN
- Index format: `UInt32` — supports >65 535 vertices
- Live update: `OnValidate` → `EditorApplication.delayCall` → `Generate()`
- Earth: 64×64 segments. Atmosphere: 256×256

### Earth Shader — `Assets/Shaders/Earth.shader` → `Space/Earth`

Custom URP forward, not PBR. Passes: `ForwardLit`, `ShadowCaster`, `DepthOnly`. SRP Batcher compatible (identical CBUFFER all passes).

| Feature | Implementation |
|---|---|
| Day tex | `_DayTex` × NdotL × `light.color` (HDR) |
| Night lights | `_NightTex` × `_NightBrightness` × `pow(1-dayBlend, _NightDayMaskPow)` |
| Normal map | Tangent-space TBN, `_NormalStrength` |
| Specular | Blinn-Phong, `_SpecularMap` masks ocean only |
| Terminator | `smoothstep(-1/k, 1/k, NdotL_geo)`, k=8 ≈ 7° twilight |
| Atmosphere rim | Rayleigh Beer-Lambert on surface, pure blue |

Constants: `kRayleigh=(0.156,0.422,1.0)` (λ⁻⁴ 700/546/440 nm), `kSun=(1.0,0.953,0.885)` (5778 K Planck).
Multiply by `light.color` → HDR drives bloom/tonemapping.

### Atmosphere — `Assets/Shaders/Atmosphere.shader` → `Space/Atmosphere`

Separate transparent additive child sphere. `Blend One One` — industry standard for rim glow.

Beer-Lambert fill:
```
optPath = kRayleigh × (opticalDepth / max(VdotN, 0.015))
extinct = exp(-optPath)
scatter = kRayleigh × (1 - extinct) × kSun
```
`1/VdotN` grows path toward limb → bright rim + blue fill across disc. No `pow(1-VdotN)`.

Rim: `pow(1-VdotN, AtmosphereRimPow-1)` on top of Beer-Lambert. Low pow=fill, high pow=ring.
Edge dissolve: `smoothstep(0, 0.06, VdotN)` → glow zeroes before silhouette.
No Mie. Pure Rayleigh = always blue.
Scale: 1.0173 = (6371+110)/6371 (110 km atmo).

### Materials

| File | Shader | Key settings |
|---|---|---|
| `Assets/Materials/Earth.mat` | `Space/Earth` | NightBrightness 0.35, NightDayMaskPow 3, TerminatorSharpness 8, AtmoOpticalDepth 0.25, AtmoStrength 0.6 |
| `Assets/Materials/Atmosphere.mat` | `Space/Atmosphere` | OpticalDepth 0.15, Strength 2.5, RimPow 4 |
| `Assets/Materials/Moon.mat` | `Space/Moon` | DisplacementStrength 0.5, TerminatorSharpness 6 |
| `Assets/Materials/Skybox_Stars.mat` | `Skybox/Panoramic` | `_MainTex` = 8k_stars_milky_way.jpg |

### Post-Processing — `Assets/Settings/SpacePostProcess.asset`

Auto-created by `SpaceLightingSetup.cs` on domain reload.

| Effect | Settings | Purpose |
|---|---|---|
| ACES Tonemapping | — | Deep blacks + compressed highlights |
| Bloom | threshold 0.85, intensity 1.2, scatter 0.35 | Specular + atmo spill |
| Color Adjustments | contrast +25, saturation +20 | Pop |

Camera must have HDR enabled (Camera → Rendering → HDR).

### Editor Scripts

**`Assets/Scripts/Editor/SpaceLightingSetup.cs`** — `[InitializeOnLoad]`, runs on domain reload:
- Ambient = Flat/Black (zero)
- Reflection intensity = 0
- Skybox → `Skybox_Stars.mat`
- Creates/updates `PostProcessVolume` (ACES + Bloom + Color Adjustments)
- Creates `Assets/Settings/SpacePostProcess.asset` VolumeProfile if missing

**`Assets/Scripts/Editor/SkyboxTextureImporter.cs`** — `AssetPostprocessor`:
- `Assets/Textures/Skybox/*` → Wrap=Clamp, 8192 max, no mipmaps, uncompressed

---

## Textures

### Skybox
- `Assets/Textures/Skybox/8k_stars_milky_way.jpg` — equirectangular panorama

### Earth (import needed)
Download 8K from `solarsystemscope.com/textures`, place in `Assets/Textures/Earth/`:

| File | Unity Texture Type | Slot |
|---|---|---|
| `Earth_Day_8K.jpg` | Default, sRGB on | `_DayTex` |
| `Earth_Night_8K.jpg` | Default, sRGB on | `_NightTex` |
| `Earth_Normal_8K.png` | Normal map | `_NormalMap` |
| `Earth_Specular_8K.png` | Default, sRGB off | `_SpecularMap` |

---

## Sun

- GameObject: `Sun`, Directional Light
- Color: `(1.0, 0.953, 0.885)` linear — 5778 K blackbody
- Intensity: 3, Shadows: Hard

---

## File Locations

```
Assets/
├── Materials/          Atmosphere.mat, Earth.mat, Skybox_Stars.mat
├── Scenes/             SampleScene.unity
├── Scripts/
│   ├── Editor/         SpaceLightingSetup.cs, SkyboxTextureImporter.cs
│   └── Planet/         UVSphere.cs
├── Settings/           SpacePostProcess.asset
├── Shaders/            Atmosphere.shader, Earth.shader, Moon.shader
└── Textures/
    ├── Earth/          drop 8K textures here
    ├── Moon/           Moon_Color_8K.tif, Moon_Displacement.tif
    └── Skybox/         8k_stars_milky_way.jpg
ProjectSettings/        DynamicsManager.asset (gravity=0)
```

## Conventions

- Shaders: `Assets/Shaders/`, namespace `Space/<Name>`
- Planet scripts: `Assets/Scripts/Planet/`
- Editor scripts: `Assets/Scripts/Editor/`
- Materials: `Assets/Materials/`
- Textures: `Assets/Textures/<subject>/`

---

## Atmosphere — Ray-March (DONE)

Ref: Sebastian Lague "Coding Adventure: Atmosphere".

| File | Role |
|---|---|
| `Assets/Shaders/Atmosphere.shader` | 16-step ray-march shader, Rayleigh+Mie, LUT sun depth |
| `Assets/Shaders/AtmosphereDepthLUT.compute` | Bakes 256×256 RGFloat optical depth LUT at startup |
| `Assets/Scripts/Planet/AtmosphereRenderer.cs` | Component on `Earth/Atmosphere` — bakes LUT on Start, sets uniforms each frame |

### How it works

1. Fragment fires on outer shell of `Earth/Atmosphere` mesh
2. Ray-sphere intersect: atmo entry/exit, planet clip
3. 16 view steps: accumulate Rayleigh+Mie density along view ray
4. Per step: sample `_OpticalDepthLUT(normHeight, cosZenith)` for sun optical depth
5. Transmittance = exp(-(kRayleigh×(viewR+sunR) + kMie×(viewM+sunM)))
6. Accumulate scatter; multiply by Rayleigh+Mie phase functions
7. Output: `_SunIntensity × sunColor × scatter` (additive, HDR → ACES)

### Physical constants (per world-unit, 1 unit = 637.1 km)

```hlsl
kRayleigh = float3(5.5, 13.0, 22.4)   // λ^-4 at 700/546/440 nm
kMie      = float3(21.0, 21.0, 21.0)  // grey Mie scatter
kMieG     = 0.76                       // Henyey-Greenstein g
```

### Key uniforms

| Uniform | Default | Set by |
|---|---|---|
| `_PlanetRadius` | 10.0 | AtmosphereRenderer |
| `_AtmoRadius` | 10.173 | AtmosphereRenderer |
| `_DensityFalloff` | 0.01334 | AtmosphereRenderer |
| `_MieFalloff` | 0.00188 | AtmosphereRenderer |
| `_SunIntensity` | 20 | AtmosphereRenderer |
| `_PlanetCentre` | Earth pos | AtmosphereRenderer.Update |
| `_OpticalDepthLUT` | RGFloat RT | AtmosphereRenderer.Start |
| sun dir/color | — | `GetMainLight()` in shader |

### Notes

- `opticalDepthCompute` auto-found via AssetDatabase in editor Play mode (assign manually for builds)
- Render: Option B (mesh sphere, `Cull Back, ZTest LEqual, Blend One One`)
- Upgrade path: Option A (URP custom render pass) when needed

---

## Planned (not started)

- Nation borders on sphere
- Dynamic atmosphere destruction (nuke)
- Dynamic ocean
- Reentry / aerobraking trails
- Population density map (war stats + civ growth)
- Orbital mechanics (Kepler/N-body, aerobraking)
- Camera / orbit controller
- Sun mesh + corona
