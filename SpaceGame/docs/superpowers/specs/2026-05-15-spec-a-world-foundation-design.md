# Spec A — World Foundation

**Date:** 2026-05-15
**Project:** SpaceGame (Unity 6 URP, `C:\Users\space\ClaudeTest`)
**Scope:** GeoUtils, RegionSystem, PopulationSystem, EconomySystem, TechnologySystem, ContactStageManager
**Depends on:** Existing Earth shader, scene hierarchy (Earth scale 10, Moon at 301.7 units)
**Not in scope:** Orbital mechanics (Spec B), launch sites, impact VFX, ship movement

---

## Overview

The World Foundation layer is the data backbone of the simulation. It tracks how humanity is doing — where people live, how rich each region is, how advanced their technology is — and how all of that responds to the alien ship's contact stage. Every other system (launch sites, trajectory planning, impact VFX, power grid) reads from this layer.

The contact stage is the master variable. It flows downward into population growth, GDP, and technology, which in turn feed launch rates and military capability. The cycle can run forward (escalation → collapse) and backward (ship hibernates → humanity recovers).

---

## 1. GeoUtils

**File:** `Assets/Scripts/World/GeoUtils.cs`

Static utility class. No MonoBehaviour. Used by every system that places objects on the globe.

```csharp
// World position on sphere surface at given lat/lon
Vector3 LatLonToWorld(float latDeg, float lonDeg, float radius)

// Inverse — world position back to lat/lon
(float lat, float lon) WorldToLatLon(Vector3 worldPos, float radius)

// UV for equirectangular texture sampling
Vector2 LatLonToUV(float latDeg, float lonDeg)

// Great-circle distance between two lat/lon points (radians)
float GreatCircleAngle(float lat1, float lon1, float lat2, float lon2)
```

Scale constants live here:
```csharp
const float EarthRadiusUnits = 10.0f;        // 1 unit = 637.1 km
const float MoonRadiusUnits  = 2.727f;
const float MoonDistanceUnits = 301.7f;
```

**Coordinate convention:** Y-up. `LatLonToWorld(0, 0)` = `(0, 0, EarthRadius)` (prime meridian equator).

---

## 2. RegionData

**File:** `Assets/Scripts/World/RegionData.cs`

ScriptableObject. One asset per region, 14 total. Holds static geographic definition + runtime mutable state.

### Static fields (set in Inspector / JSON)
```csharp
string   regionId;           // "north_america", "e_asia", etc.
string   displayName;
Color    factionColor;       // NATO=blue, BRICS=red, NonAligned=grey
bool     isNuclearPower;
float    baseWealth;         // 0–1, relative economic base at game start
float[]  boundaryLatLons;    // polyline pairs defining region on sphere
Vector2  capitalLatLon;      // lat/lon of capital city (missile target + label)
```

### Runtime state (mutated during play)
```csharp
FactionAlignment alignment;  // NATO, BRICS, NonAligned, SuperNation, Collapsed
float            damageLevel;       // 0=intact, 1=fully destroyed
float            populationM;       // millions, current
float            gdpTrillion;       // current
float            techLevel;         // 0–1
bool             powerGridOnline;
```

**Faction merging:** At Stage 6 all regions flip to `SuperNation`. At Stage 7 (Collapse) all flip to `Collapsed`. On ship hibernation + time, regions recover alignment based on original `FactionAlignment` defaults.

**14 regions:**
N. America, C. America, S. America, W. Europe, E. Europe, Russia, Middle East, N. Africa, S. Africa, E. Asia, S. Asia, SE Asia, C. Asia, Oceania

---

## 3. ContactStageManager

**File:** `Assets/Scripts/World/ContactStageManager.cs`

Singleton MonoBehaviour. The master state machine. All other systems read `ContactStageManager.Instance.CurrentStage` and `FearLevel`.

### Stages
```csharp
enum ContactStage {
    Undetected,      // 1 — ship unseen
    PassiveDetect,   // 2 — spotted, no movement
    NonAggressive,   // 3 — ship acts non-hostilely
    ReactiveUnmanned,// 4A — destroys unmanned objects
    ReactiveCrewed,  // 4B — destroys crewed vehicle
    CounterAttack,   // 5 — responds to attacks
    Indiscriminate,  // 6 — attacks without provocation
    Collapse,        // 7 — civilization broken
}
```

### Fear accumulation (real-time)
```
FearLevel : float [0, 1]

OnShipAction(action):
    baseFear   = action.baseFearValue
    spectacle  = action.witnessedByMillions ? 2.0 : 1.0
    location   = GetPopulationDensityAt(action.position) * 3.0   // max 3×
    repetition = repetitionCounts[action.type]++                  // stacks
    FearLevel += baseFear × spectacle × location × repetition × dt
```

Fear decays slowly when ship is dormant (0.005 / sec toward 0).

### Stage transitions
- Stage rises automatically when `FearLevel` crosses per-stage threshold
- Stage 4A vs 4B: determined by `ScanResult` of last destroyed object
- Stage 7 (Collapse): triggered explicitly by ship action OR automatic if Stage 6 sustained >N minutes
- De-escalation: ship dormant → fear decays → stage can drop, but never below where humanity has permanent knowledge (can't un-discover the ship)

### Spontaneous attack probability (Stage 3+)
Each faction rolls per real-time second:
```
p = baseProbability[faction.hawkishness] × FearLevel × repetitionMultiplier
```
At Stage 3 entry: ~0.001%/sec for diplomatic factions, ~0.01%/sec for hawkish. Fires `OnFactionAttack(faction)` event.

---

## 4. PopulationSystem

**File:** `Assets/Scripts/World/PopulationSystem.cs`

Updates per simulated day (scaled by time-warp factor). Each region's population evolves independently.

### Growth formula
```
growthRate = baseGrowthRate[techLevel] × stageFactor[stage] × (1 - damageLevel)

baseGrowthRate:  techLevel 0 (1800s) → 0.5%/yr,  techLevel 1 (modern) → 1.1%/yr
stageFactor:     Stage 1 = 1.0,  Stage 3 = 0.85,  Stage 5 = 0.5,  Stage 6 = 0.2,  Stage 7 = -0.5 (population declining)
```

Damage level applies directly — a fully destroyed region has zero growth and active population loss.

### Starting populations (millions, approximate)
N. America 500, S. America 450, C. America 180, W. Europe 450, E. Europe 180, Russia 145, Middle East 400, N. Africa 280, S. Africa 700, E. Asia 1500, S. Asia 2000, SE Asia 700, C. Asia 80, Oceania 45

---

## 5. EconomySystem

**File:** `Assets/Scripts/World/EconomySystem.cs`

Updates per simulated month. GDP per region drives launch rates and military budget in Spec B.

### GDP formula
```
// baseWealthPerCapita derived from RegionData.baseWealth (0–1) scaled to [$5k–$55k] per person/year
// e.g. W. Europe baseWealth=0.9 → ~$50k/cap,  S. Africa baseWealth=0.25 → ~$15k/cap
gdp = population × baseWealthPerCapita × techMultiplier × stageFactor × (1 - damageLevel)²

techMultiplier:  1.0 at tech 0,  up to 4.0 at tech 1  (technology compounds wealth)
stageFactor:     Stage 1 = 1.0,  Stage 3 = 0.9,  Stage 5 = 0.6,  Stage 6 = 0.3,  Stage 7 = 0.05
```

`(1 - damageLevel)²` — damage hurts economy harder than population (infrastructure loss).

### Outputs consumed by Spec B
- `GetGlobalMilitaryBudget()` → scales launch rate
- `GetRegionLaunchCapacity(regionId)` → how many launches per year this region can sustain
- `GetResearchBudget()` → feeds TechnologySystem

---

## 6. TechnologySystem

**File:** `Assets/Scripts/World/TechnologySystem.cs`

Tech level per region, 0–1. Drives base growth rate, economy multiplier, and eventually unlocks experimental weapons (Spec B).

### Advancement
```
techAdvanceRate = (researchBudget / gdp) × stageFactor × (1 - damageLevel)

stageFactor:  Stage 1–2 = 1.0 (normal),  Stage 3 = 1.3 (fear drives innovation),
              Stage 4–5 = 1.6 (war footing, rapid military tech),
              Stage 6 = 0.4 (society collapsing),  Stage 7 = 0.0 (no advance)
```

Stage 3–5 intentionally accelerates technology — fear and war drive innovation faster than peacetime. This is the "ant nest" mechanic: poking humanity makes them smarter and more dangerous over time.

### Tech milestones (gates for Spec B)
- `0.3` — orbital launch capability
- `0.6` — lunar transfer capability
- `0.8` — experimental weapons (railguns, directed energy)
- `1.0` — gravimetric detection arrays built (cloak partially defeated — ship visible on human sensors but still unkillable; ship can destroy the detection station to remove the effect)

---

## 7. Collapse & Recovery Cycle

Stage 7 state (ship active or recently active):
- All tech levels decay to 0.1 over ~30 simulated years (exponential decay, half-life 8 years)
- All populations drop to 40–60% of Stage 6 values over ~10 simulated years
- GDP collapses to near zero (stageFactor 0.05)
- All faction alignments → `Collapsed`
- TechnologySystem stageFactor = 0.0 — no advancement while collapse is active

Recovery (ship hibernating — distinct from Stage 7 active):
- `HibernateMode = true` unlocks a separate recovery stageFactor = 0.15 for tech and 0.3 for population/GDP
- Population and GDP grow from Stage 7 baseline — slow, 1800s-equivalent rate
- Tech climbs at ~0.005/simulated year — takes ~18 simulated years to reach tech 0.2 (pre-industrial → early industrial)
- Recovery threshold: global average tech > 0.2 AND total population > 50% of pre-collapse peak
- When threshold met → ContactStage resets to `Undetected`, `HibernateMode = false`
- Player can emerge and begin a new cycle

**Key design intent:** Humanity is not a hard threat to the player. The fun is observation — poking the civilization, watching it react, adapt, and change. Each cycle starts fresh but with potentially different faction alignments depending on which regions recovered fastest.

---

## 8. Globe Visualization

Driven by the Region layer, rendered as overlay on existing Earth shader.

### Layers (all togglable)
1. **Region borders** — faction-colored polylines projected on sphere surface
2. **Population heatmap** — equirectangular density texture, additively blended over day map in Earth.shader
3. **City dots** — top 50 cities placed via `LatLonToWorld`, sized by population, labeled on hover
4. **Damage overlay** — per-region burn mask (drives surface damage shader in future spec)

### Earth.shader additions needed
New uniforms added to `Earth.shader` ForwardLit pass:
```hlsl
sampler2D _PopulationHeatmap;   // equirectangular, R=density 0–1
float     _HeatmapStrength;     // 0=off, 1=full
float     _DamageLevel[14];     // per-region, drives scorch (future)
```

Heatmap blended additively as warm tint over the day texture:
```hlsl
float heat = tex2D(_PopulationHeatmap, IN.uv).r * _HeatmapStrength;
color.rgb += heat * float3(0.8, 0.3, 0.0) * (1.0 - dayBlend * 0.5);
```

---

## 9. Data Files

Region definitions stored as JSON in `Assets/Data/Regions/`:
```
north_america.json  — boundary points, capital, base wealth, starting population
e_asia.json
... (14 files)
```

Top-50 cities stored in `Assets/Data/Cities/cities.json`:
```json
{ "name": "Tokyo", "lat": 35.68, "lon": 139.69, "population": 37.4, "regionId": "e_asia" }
```

Launch sites stored in `Assets/Data/LaunchSites/sites.json` (consumed by Spec B):
```json
{ "name": "Cape Canaveral", "lat": 28.6, "lon": -80.6, "regionId": "north_america", "types": ["rocket","icbm"] }
```

---

## 10. File Layout

```
Assets/
├── Scripts/World/
│   ├── GeoUtils.cs
│   ├── RegionData.cs               (ScriptableObject)
│   ├── ContactStageManager.cs
│   ├── PopulationSystem.cs
│   ├── EconomySystem.cs
│   └── TechnologySystem.cs
├── Data/
│   ├── Regions/                    (14 × .json)
│   ├── Cities/cities.json          (top 50)
│   └── LaunchSites/sites.json      (for Spec B)
├── Shaders/
│   └── Earth.shader                (add _PopulationHeatmap uniform)
└── Textures/Earth/
    └── Earth_Population_8K.png     (NASA GPW density, equirectangular)
```

---

## Open Question

**Should humanity ever genuinely threaten the ship?**
Resolved: ship is unkillable. Tech milestone `1.0` gravimetric arrays partially defeat cloaking — ship becomes visible on human sensors — but humanity cannot destroy it. Ship's counter: scan and destroy the gravimetric station(s) to remove detection. The threat to the player is chaos and unpredictability (random attacks, nukes near ship, EMP) not a winnable war for humanity.
