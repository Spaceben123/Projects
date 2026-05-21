# Spec: Nation Economy Simulation + Nation Selection UI
**Date:** 2026-05-21  
**Status:** Approved — ready for implementation

---

## Overview

Each of the ~195 nations in `WorldRegionMapper` gets a full economy simulation: real GDP, real population (2024 IMF/World Bank data), a treasury, tech level, and stage-driven budget allocation. Nations are individually selectable on the globe. A stat panel appears top-left when selected. City dot orbs are removed.

The tone: humans are ants. The player watches them build with amusement. Nations cannot threaten the player — but they do react, spend, and eventually build space infrastructure. Future specs will add consequences (attacking the ship is designed separately).

---

## Time Model

| Layer | Unit | Rate |
|---|---|---|
| **Game time** (economy, tech, construction) | Game-month | 1 month = 60 real seconds at 1× warp |
| **Physics time** (launches, trajectories, impacts) | Real deltaTime | Ignores warp scale entirely |

**No lump-sum ticks.** Income accumulates continuously each frame:
```
gameTimeDelta = Time.deltaTime × timeWarpScale
accumulatedMonths += gameTimeDelta / 60f
treasury += (gdpPerMonth × budgetFraction_SpaceRD) × (gameTimeDelta / 60f)
if (accumulatedMonths >= 1f) { RunMonthlyStrategicTick(); accumulatedMonths -= 1f; }
```

Monthly strategic tick handles: population growth, tech advancement, alliance state, stage-response recalculation. All money flows are frame-continuous.

---

## Data Source

**File:** `Assets/Resources/Data/countries.json`  
**Format:**
```json
[
  { "iso3": "USA", "gdp_billions": 29000, "population_millions": 335 },
  { "iso3": "CHN", "gdp_billions": 18500, "population_millions": 1410 },
  ...
]
```
Source: 2024 IMF World Economic Outlook estimates. One entry per ISO3 code in `WorldRegionMapper`. Missing codes default to GDP $10B, pop 1M.

---

## GDP Allocation Model

Allocation splits GDP into four buckets, shifting with contact stage. Applies per-nation each monthly tick.

| Stage | Civilian | Space R&D | Military | Fear Drag |
|---|---|---|---|---|
| 1 — Undetected     | 70% | 22% | 8%  | 0%  |
| 2 — Anomaly        | 62% | 26% | 10% | 2%  |
| 3 — Confirmed Alien| 50% | 28% | 15% | 7%  |
| 4 — Active Obs.    | 42% | 30% | 18% | 10% |
| 5 — Weapons Used   | 30% | 28% | 28% | 14% |
| 6 — Super-Nation   | 22% | 38% | 25% | 15% |
| 7 — Collapse       | 15% | 20% | 35% | 30% |

**Space R&D** drives tech advancement speed and treasury accumulation for launch sites.  
**Fear Drag** is GDP wasted — not available to any bucket.  
**Military** is reserved for future spec (human counter-response design).  
Effective GDP = nominalGDP × (1 − fearDragFraction). Growth applied to nominal GDP only.

**Population growth rate** (applied monthly to base pop):
- Stage 1–2: +0.07%/month (~0.84%/yr, close to real-world average)
- Stage 3–4: +0.03%/month (fear slows births)
- Stage 5–7: −0.01%/month (collapse, casualties from weapons impacts)

GDP grows proportional to effective population × tech multiplier:
```
gdpGrowthRate = 0.003f + (techLevel / 100f) × 0.007f  // 0.3–1% per month
nominalGdp   *= 1f + gdpGrowthRate × (civilianFraction + spaceRdFraction)
```

---

## Tech Level

- Range: 0–100. Starts at real-world-calibrated value per nation (USA ≈ 72, Chad ≈ 8).
- Advances monthly: `techDelta = spaceRdBudget_thisMonth / techCostPerPoint`
- `techCostPerPoint` scales with current tech level (diminishing returns, exponential).
- At tech 100 = "max tech" for current spec. Target: ~100 years at Stage 1, ~200 years at Stage 7.

Initial tech level derived from GDP per capita rank — nations are seeded proportionally, not uniformly zero.

---

## Launch Sites

### Build vs Rent

| Option | Cost | Who can do it |
|---|---|---|
| Rent (use another nation's site) | $2B per launch | Any nation with treasury ≥ $2B |
| Build own site | $50B one-time | Nations with treasury ≥ $50B AND stage ≥ 2 AND tech ≥ 30 |

Nations queue launch decisions each monthly tick. Priority: space R&D budget surplus → launch payload type decision.

### Payload Types

| Type | Cost | Effect |
|---|---|---|
| Probe / satellite | $2–5B | +tech points, science bonus |
| Crewed mission (3 people) | $10B | +morale, +tech, narrative event |
| Infrastructure point | $8B | Accumulates toward space station |

**Space station**: 10 infrastructure launches = 1 space station. Gives +15% Space R&D efficiency to owning nation. Max 3 stations per nation (future: Moon base, Mars outpost thresholds TBD).

Stage threshold for launch activity:
- Stage 1: only major economies (GDP > $1T) launch occasionally
- Stage 2+: GDP > $200B nations start regular launch programs
- Stage 4+: pooled faction budgets enable joint launches

---

## Nation Selection UI

### Globe Click → Nation

1. Raycast from camera against Earth sphere collider → get hit point
2. Convert world position → lat/lon → UV → pixel index in `region_map.bytes`
3. Look up `countryIdx` → `NationDataRegistry.GetNation(countryIdx)`
4. Set `selectedCountryIdx` on `FactionTextureRenderer` → triggers highlight re-render

### Stat Panel (top-left)

Persistent `NationStatPanel` UI component. Hidden when nothing selected. Shows:

```
┌─────────────────────────────┐
│  🌍 United States           │
│  Alliance: NATO             │
├─────────────────────────────┤
│  GDP        $29.0T          │
│  Population 335M            │
│  Tech Level ████████░░ 72   │
│  Treasury   $142B           │
├─────────────────────────────┤
│  Budget (Stage 1)           │
│  Civilian  ████████ 70%     │
│  Space R&D █████ 22%        │
│  Military  █ 8%             │
│  Fear Drag   0%             │
├─────────────────────────────┤
│  Launch Sites   2 (owned)   │
│  Launches       14 total    │
│  Space Stations 1           │
└─────────────────────────────┘
```

Clicking anywhere else on globe deselects. ESC also deselects.

### Selected Nation Highlight

`FactionTextureRenderer.Recolor()` checks `_selectedCountryIdx`. If a country is selected, its pixels use the faction color at alpha 255 (fully opaque) instead of the faction's default alpha (~128). All other countries retain normal transparency.

No shader change needed — alpha is set per-pixel in the CPU-side `countryColors[256]` lookup.

---

## City Dots — Removed

`CityDotRenderer.cs` and `CityData.cs` (if present) are removed from the scene and deleted. The `_PopulationHeatmap` / `_HeatmapStrength` shader properties are removed from `Earth.shader` and its CBUFFER (keeping CBUFFER layout identical across all 3 passes). `GlobeOverlayToggle` F3/F4 bindings are removed or remapped.

---

## New Scripts

| Script | Location | Purpose |
|---|---|---|
| `NationRuntime.cs` | `Scripts/World/` | Per-nation mutable state (treasury, tech, launches, pop) |
| `NationDataRegistry.cs` | `Scripts/World/` | Loads `countries.json`, provides lookup by ISO3 / countryIdx |
| `NationEconomySystem.cs` | `Scripts/World/` | Monthly tick, continuous income, tech advance |
| `NationSelectionSystem.cs` | `Scripts/World/` | Globe raycast → select nation |
| `NationStatPanel.cs` | `Scripts/UI/` | Top-left stat UI |
| `LaunchSiteSystem.cs` | `Scripts/World/` | Build/rent logic, payload queue, station accumulation |

---

## Wiring

- `WorldSimulation` calls `NationEconomySystem.Tick(deltaTime)` each Update.
- `NationSelectionSystem` reads `_regionIdMap` from `FactionTextureRenderer` (shared reference).
- `NationStatPanel` subscribes to `NationSelectionSystem.OnNationSelected`.
- `ContactStageManager.OnStageChanged` → `NationEconomySystem.OnStageChanged` → recalculates allocation fractions.

---

## Out of Scope (future specs)

- Military spending effects (human counter-attack design)
- Alliance formation mechanics (which nations merge at Stage 6)
- Moon base / Mars outpost thresholds
- Economic damage from weapon impacts (partially in existing `RegionRuntime.DamageLevel`)
- Per-nation diplomatic relations
