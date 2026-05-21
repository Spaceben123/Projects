# SpaceGame — Project AI Memory

> Always read `GLOBAL_RULES.md` at the monorepo root FIRST, then this file.

## Project Overview

**Path:** `C:\Users\space\Projects\SpaceGame`
**Engine:** Unity 6 (URP 17.4)
**Game Design:** Real-time human-civ vs alien spaceship sim. Player IS the alien ship, escalating through 6 contact stages against 14 Earth factions. Not turn-based — real-time pressure and consequences.
**MCP:** Unity MCP connected on WebSocket `localhost:8090`

## Current Status

| Spec | Name | Status |
|---|---|---|
| A | World Foundation + Earth Overlays | ✅ Complete (all 15 tasks, 18/18 tests passing) |
| A-cont | F1–F4 Globe Overlays | ✅ Working |
| B | Weapon System | 🔜 Next |

**Full project context:** Always read `C:\Users\space\Projects\SpaceGame\CLAUDE.md` at session start.

## Spec B — Weapon System (Approved Design, Plan Not Yet Written)

- ScriptableObject-based weapon definitions (name, category, blast radius, deploy time, speed)
- Ship menu UI: Weapons → Category → Type → Deploy → Left-click surface to fire
- Physical projectile in scene with cinematic pre-computed trajectory
- Line-of-sight check using Moon as occluder: if clear → direct high-speed shot; if blocked → cinematic orbital arc (Bezier)
- Targets: Earth surface OR Moon surface
- ShipAnchor: empty GameObject placeholder (real ship added later)
- Impact system: sample population density from CityData gaussian at hit point, compute casualties/fear, update `RegionRuntime.DamageLevel`

## Scene Structure

- **Scene:** `Assets/Scenes/SampleScene.unity`
- **Scale:** 1 unit = 637.1 km (Earth radius = 10 world units)
- **Earth:** scale 10; **Atmosphere child:** scale 1.0173; **Moon**, **Sun** (directional), **PostProcessVolume**
- **Post-processing:** ACES + Bloom (threshold 0.85) + Color Adjustments
- **Gravity:** 0 (space game)
- **WorldSimulation** GameObject has all sim components
- **Earth/GlobeOverlays** has CityDotRenderer, FactionTextureRenderer, GlobeOverlayToggle

## Strict Technical Patterns (Hard Rules — Never Break These)

1. **MaterialPropertyBlock:** NEVER use `sharedMaterial` for runtime changes. Always use `MaterialPropertyBlock` per-renderer to avoid polluting `.mat` assets on disk.
2. **Input System:** New Input System ONLY. `using UnityEngine.InputSystem;` and `Keyboard.current` / `Mouse.current`. NEVER `UnityEngine.Input`.
3. **Assembly:** `SpaceGame.World.asmdef` references `Unity.InputSystem`. Test scripts in `SpaceGame.Tests.EditMode`.
4. **Renderer Lookups:** NEVER use `GameObject.Find` for material/renderer access at runtime. Use `transform.parent?.GetComponent<MeshRenderer>()`.
5. **Texture V-Flip:** Equirectangular textures: `lat = -90f + v * 180f` (NOT `90f - v * 180f`).
6. **URP Shaders:** NEVER declare `static const float PI` — URP Core.hlsl already defines it. All HLSL comments must be ASCII-only. SRP Batcher: CBUFFER layout must be IDENTICAL across all 3 passes.

## Region / Faction System

14 sub-regions: N.America, C.America, S.America, W.Europe, E.Europe, Russia, Middle East, N.Africa, S.Africa, E.Asia, S.Asia, SE Asia, C.Asia, Oceania. Factions can flip on damage; at Stage 6 all merge into one super-nation.

## Contact Stage System

8 stages (1=Undetected → 7=Collapse). Stage affects population growth, GDP, tech speed. See `game_design_spacegame.md` in Claude's memory for full table.

## Known Issues (open)

- Some region borders still missing — countries with unusual ISO codes in Natural Earth data not covered by `WorldRegionMapper`. Needs investigation.
- City dot renderer base size not working as expected (low priority).
