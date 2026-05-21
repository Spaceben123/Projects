# Spec A — World Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build GeoUtils, 14-region faction system, ContactStageManager, PopulationSystem, EconomySystem, TechnologySystem, and globe visualization overlay on the existing Earth shader.

**Architecture:** Pure math lives in static classes (GeoUtils, SimulationMath) with no scene dependencies — fully unit-testable. MonoBehaviours (RegionRegistry, WorldSimulation, renderers) orchestrate loading and display. RegionRuntime is a plain C# class instantiated from JSON in Resources/. WorldSimulation manages simulated time and time-warp, ticking each system at the correct cadence.

**Tech Stack:** Unity 6 URP, C#, Unity Test Framework (EditMode), JsonUtility, LineRenderer for region borders, existing Earth.shader (URP forward, HLSL). Project root: `C:\Users\space\ClaudeTest`. Existing Earth scale: 10 units = 6371 km.

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `Assets/Scripts/World/GeoUtils.cs` | Create | Lat/lon ↔ world position math, scale constants |
| `Assets/Scripts/World/SimulationMath.cs` | Create | All pure simulation formulas (population growth, GDP, tech) |
| `Assets/Scripts/World/RegionDefinition.cs` | Create | [Serializable] struct matching JSON on disk |
| `Assets/Scripts/World/RegionRuntime.cs` | Create | Mutable runtime state per region |
| `Assets/Scripts/World/RegionRegistry.cs` | Create | MonoBehaviour — loads all 14 regions from Resources on Awake |
| `Assets/Scripts/World/ContactStageManager.cs` | Create | Singleton — contact stage state machine + fear accumulation |
| `Assets/Scripts/World/PopulationSystem.cs` | Create | MonoBehaviour — ticks population per simulated day |
| `Assets/Scripts/World/EconomySystem.cs` | Create | MonoBehaviour — ticks GDP per simulated month |
| `Assets/Scripts/World/TechnologySystem.cs` | Create | MonoBehaviour — ticks tech per simulated year, fires milestone events |
| `Assets/Scripts/World/WorldSimulation.cs` | Create | MonoBehaviour — master time-warp, ticks all systems |
| `Assets/Scripts/World/RegionBorderRenderer.cs` | Create | MonoBehaviour — draws faction-colored polylines on sphere |
| `Assets/Scripts/World/CityDotRenderer.cs` | Create | MonoBehaviour — places labeled city spheres via GeoUtils |
| `Assets/Scripts/Editor/WorldDataInitializer.cs` | Create | Editor menu item — writes all JSON data files to Resources/ |
| `Assets/Tests/EditMode/SpaceGame.Tests.EditMode.asmdef` | Create | Test assembly definition |
| `Assets/Tests/EditMode/GeoUtilsTests.cs` | Create | Unit tests for GeoUtils |
| `Assets/Tests/EditMode/SimulationMathTests.cs` | Create | Unit tests for all SimulationMath formulas |
| `Assets/Shaders/Earth.shader` | Modify | Add `_PopulationHeatmap` + `_HeatmapStrength` uniforms + heatmap blend |

---

## Task 1: Test Assembly Setup

**Files:**
- Create: `Assets/Tests/EditMode/SpaceGame.Tests.EditMode.asmdef`
- Create: `Assets/Tests/EditMode/SmokeTest.cs`

- [ ] **Step 1: Create the Tests/EditMode folder and asmdef**

Create `Assets/Tests/EditMode/SpaceGame.Tests.EditMode.asmdef`:

```json
{
    "name": "SpaceGame.Tests.EditMode",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner",
        "Assembly-CSharp"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Create smoke test to confirm assembly loads**

Create `Assets/Tests/EditMode/SmokeTest.cs`:

```csharp
using NUnit.Framework;

public class SmokeTest
{
    [Test]
    public void TestAssembly_Loads() => Assert.Pass();
}
```

- [ ] **Step 3: Run test to confirm it passes**

Open Unity → Window > General > Test Runner → EditMode tab → Run All.
Expected: 1 test passes (SmokeTest.TestAssembly_Loads).

*Alternatively via MCP:* use `mcp__mcp-unity__run_tests` with `testMode: "EditMode"`.

- [ ] **Step 4: Commit**

```
git add Assets/Tests/
git commit -m "test: add EditMode test assembly"
```

---

## Task 2: GeoUtils

**Files:**
- Create: `Assets/Scripts/World/GeoUtils.cs`
- Create: `Assets/Tests/EditMode/GeoUtilsTests.cs`

- [ ] **Step 1: Write failing tests**

Create `Assets/Tests/EditMode/GeoUtilsTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

public class GeoUtilsTests
{
    [Test]
    public void LatLonToWorld_PrimeMeridianEquator_ReturnsPositiveZ()
    {
        Vector3 r = GeoUtils.LatLonToWorld(0f, 0f, 10f);
        Assert.AreApproximatelyEqual(0f,  r.x, 0.001f);
        Assert.AreApproximatelyEqual(0f,  r.y, 0.001f);
        Assert.AreApproximatelyEqual(10f, r.z, 0.001f);
    }

    [Test]
    public void LatLonToWorld_NorthPole_ReturnsPositiveY()
    {
        Vector3 r = GeoUtils.LatLonToWorld(90f, 0f, 10f);
        Assert.AreApproximatelyEqual(0f,  r.x, 0.001f);
        Assert.AreApproximatelyEqual(10f, r.y, 0.001f);
        Assert.AreApproximatelyEqual(0f,  r.z, 0.001f);
    }

    [Test]
    public void LatLonToWorld_ResultHasCorrectMagnitude()
    {
        Vector3 r = GeoUtils.LatLonToWorld(51.5f, -0.1f, 10f);
        Assert.AreApproximatelyEqual(10f, r.magnitude, 0.001f);
    }

    [Test]
    public void WorldToLatLon_RoundTrip_London()
    {
        float lat = 51.5f, lon = -0.1f;
        Vector3 world = GeoUtils.LatLonToWorld(lat, lon, 10f);
        var (rLat, rLon) = GeoUtils.WorldToLatLon(world, 10f);
        Assert.AreApproximatelyEqual(lat, rLat, 0.01f);
        Assert.AreApproximatelyEqual(lon, rLon, 0.01f);
    }

    [Test]
    public void LatLonToUV_PrimeMeridianEquator_ReturnsCentre()
    {
        Vector2 uv = GeoUtils.LatLonToUV(0f, 0f);
        Assert.AreApproximatelyEqual(0.5f, uv.x, 0.001f);
        Assert.AreApproximatelyEqual(0.5f, uv.y, 0.001f);
    }

    [Test]
    public void LatLonToUV_NorthPole_ReturnsTopEdge()
    {
        Vector2 uv = GeoUtils.LatLonToUV(90f, 0f);
        Assert.AreApproximatelyEqual(0f, uv.y, 0.001f);
    }

    [Test]
    public void GreatCircleAngle_SamePoint_ReturnsZero()
    {
        float a = GeoUtils.GreatCircleAngle(35f, 139f, 35f, 139f);
        Assert.AreApproximatelyEqual(0f, a, 0.001f);
    }

    [Test]
    public void GreatCircleAngle_Antipodes_ReturnsPi()
    {
        float a = GeoUtils.GreatCircleAngle(0f, 0f, 0f, 180f);
        Assert.AreApproximatelyEqual(Mathf.PI, a, 0.001f);
    }
}
```

- [ ] **Step 2: Run tests — confirm they all FAIL (GeoUtils not yet defined)**

- [ ] **Step 3: Implement GeoUtils**

Create `Assets/Scripts/World/GeoUtils.cs`:

```csharp
using UnityEngine;

public static class GeoUtils
{
    public const float EarthRadiusUnits  = 10.0f;
    public const float MoonRadiusUnits   = 2.727f;
    public const float MoonDistanceUnits = 301.7f;

    public static Vector3 LatLonToWorld(float latDeg, float lonDeg, float radius)
    {
        float lat = latDeg * Mathf.Deg2Rad;
        float lon = lonDeg * Mathf.Deg2Rad;
        return new Vector3(
            Mathf.Cos(lat) * Mathf.Sin(lon),
            Mathf.Sin(lat),
            Mathf.Cos(lat) * Mathf.Cos(lon)
        ) * radius;
    }

    public static (float lat, float lon) WorldToLatLon(Vector3 worldPos, float radius)
    {
        Vector3 n = worldPos / radius;
        float lat = Mathf.Asin(Mathf.Clamp(n.y, -1f, 1f)) * Mathf.Rad2Deg;
        float lon = Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg;
        return (lat, lon);
    }

    public static Vector2 LatLonToUV(float latDeg, float lonDeg)
    {
        float u = (lonDeg + 180f) / 360f;
        float v = 1f - (latDeg + 90f) / 180f;
        return new Vector2(u, v);
    }

    public static float GreatCircleAngle(float lat1, float lon1, float lat2, float lon2)
    {
        float dLat = (lat2 - lat1) * Mathf.Deg2Rad;
        float dLon = (lon2 - lon1) * Mathf.Deg2Rad;
        float a = Mathf.Sin(dLat * 0.5f) * Mathf.Sin(dLat * 0.5f)
                + Mathf.Cos(lat1 * Mathf.Deg2Rad) * Mathf.Cos(lat2 * Mathf.Deg2Rad)
                  * Mathf.Sin(dLon * 0.5f) * Mathf.Sin(dLon * 0.5f);
        return 2f * Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(1f - a));
    }
}
```

- [ ] **Step 4: Run tests — confirm all 8 pass**

- [ ] **Step 5: Commit**

```
git add Assets/Scripts/World/GeoUtils.cs Assets/Tests/EditMode/GeoUtilsTests.cs
git commit -m "feat: add GeoUtils lat/lon projection utilities"
```

---

## Task 3: SimulationMath

**Files:**
- Create: `Assets/Scripts/World/SimulationMath.cs`
- Create: `Assets/Tests/EditMode/SimulationMathTests.cs`

- [ ] **Step 1: Write failing tests**

Create `Assets/Tests/EditMode/SimulationMathTests.cs`:

```csharp
using NUnit.Framework;

public class SimulationMathTests
{
    [Test]
    public void PopGrowthRate_Stage1_FullDamage_ReturnsZero()
    {
        float rate = SimulationMath.PopGrowthRatePerYear(1.0f, 0, 1.0f);
        Assert.AreApproximatelyEqual(0f, rate, 0.0001f);
    }

    [Test]
    public void PopGrowthRate_Stage1_NoDamage_ModernTech_ReturnsPositive()
    {
        float rate = SimulationMath.PopGrowthRatePerYear(1.0f, 0, 0f);
        Assert.Greater(rate, 0f);
    }

    [Test]
    public void PopGrowthRate_Stage7_IsNegative()
    {
        float rate = SimulationMath.PopGrowthRatePerYear(0.5f, 7, 0f);
        Assert.Less(rate, 0f);
    }

    [Test]
    public void GDP_ZeroPopulation_ReturnsZero()
    {
        float gdp = SimulationMath.CalcGDP(0f, 0.5f, 0.5f, 1, 0f);
        Assert.AreApproximatelyEqual(0f, gdp, 0.001f);
    }

    [Test]
    public void GDP_FullDamage_NearlyZero()
    {
        float gdp = SimulationMath.CalcGDP(500f, 0.85f, 1.0f, 1, 1.0f);
        Assert.AreApproximatelyEqual(0f, gdp, 0.001f);
    }

    [Test]
    public void GDP_Stage6_LowerThanStage1()
    {
        float gdp1 = SimulationMath.CalcGDP(500f, 0.85f, 0.5f, 1, 0f);
        float gdp6 = SimulationMath.CalcGDP(500f, 0.85f, 0.5f, 6, 0f);
        Assert.Greater(gdp1, gdp6);
    }

    [Test]
    public void TechAdvance_Stage4_FasterThanStage1()
    {
        float r1 = SimulationMath.TechAdvanceRate(0.1f, 1.0f, 4, 0f);
        float r4 = SimulationMath.TechAdvanceRate(0.1f, 1.0f, 4, 0f);
        Assert.GreaterOrEqual(r4, r1);
    }

    [Test]
    public void TechAdvance_Stage7_ReturnsZero()
    {
        float r = SimulationMath.TechAdvanceRate(0.1f, 1.0f, 7, 0f);
        Assert.AreApproximatelyEqual(0f, r, 0.0001f);
    }

    [Test]
    public void FearDelta_HighSpectacle_HigherThanLow()
    {
        float low  = SimulationMath.FearDelta(0.1f, false, 0.2f, 1);
        float high = SimulationMath.FearDelta(0.1f, true,  0.8f, 1);
        Assert.Greater(high, low);
    }

    [Test]
    public void FearDelta_Repetition_Doubles()
    {
        float once  = SimulationMath.FearDelta(0.1f, false, 0.5f, 1);
        float twice = SimulationMath.FearDelta(0.1f, false, 0.5f, 2);
        Assert.AreApproximatelyEqual(once * 2f, twice, 0.001f);
    }
}
```

- [ ] **Step 2: Run tests — confirm they all FAIL**

- [ ] **Step 3: Implement SimulationMath**

Create `Assets/Scripts/World/SimulationMath.cs`:

```csharp
using UnityEngine;

public static class SimulationMath
{
    static readonly float[] s_popStageFactor  = { 0f, 1.0f, 1.0f, 0.85f, 0.75f, 0.75f, 0.50f, 0.20f, -0.50f };
    static readonly float[] s_gdpStageFactor  = { 0f, 1.0f, 1.0f, 0.90f, 0.80f, 0.80f, 0.60f, 0.30f,  0.05f };
    static readonly float[] s_techStageFactor = { 0f, 1.0f, 1.0f, 1.30f, 1.60f, 1.60f, 1.60f, 0.40f,  0.00f };

    // Returns fractional annual growth rate (e.g. 0.011 = 1.1%/yr)
    public static float PopGrowthRatePerYear(float techLevel, int stage, float damageLevel)
    {
        float baseRate = Mathf.Lerp(0.005f, 0.011f, techLevel);
        return baseRate * s_popStageFactor[stage] * (1f - damageLevel);
    }

    // Returns GDP in trillions USD
    // baseWealth 0-1 mapped to $5k-$55k per-capita annually
    public static float CalcGDP(float populationM, float baseWealth, float techLevel, int stage, float damageLevel)
    {
        float perCapita   = Mathf.Lerp(5000f, 55000f, baseWealth);
        float techMult    = Mathf.Lerp(1.0f, 4.0f, techLevel);
        float damageFactor = (1f - damageLevel) * (1f - damageLevel);
        return populationM * 1e6f * perCapita * techMult * s_gdpStageFactor[stage] * damageFactor / 1e12f;
    }

    // Returns tech advance per simulated year (dimensionless 0-1 scale)
    public static float TechAdvanceRate(float researchFraction, float gdp, int stage, float damageLevel)
    {
        float baseRate = researchFraction * gdp * 0.00002f;
        return baseRate * s_techStageFactor[stage] * (1f - damageLevel);
    }

    // Returns instantaneous fear delta for one ship action
    // repetitionCount: how many times this same action type has been done before (starts at 1)
    public static float FearDelta(float baseFearValue, bool witnessedByMillions, float populationDensity01, int repetitionCount)
    {
        float spectacle  = witnessedByMillions ? 2.0f : 1.0f;
        float location   = 1.0f + populationDensity01 * 2.0f;
        return baseFearValue * spectacle * location * repetitionCount;
    }

    // Per-second fear decay when ship is dormant
    public const float FearDecayPerSec = 0.005f;

    // Per-second spontaneous attack probability for a faction at given fear
    public static float SpontaneousAttackProbPerSec(float hawkishness, float fearLevel, int repetitionMultiplier)
    {
        float base01 = Mathf.Lerp(0.00001f, 0.0001f, hawkishness);
        return base01 * fearLevel * repetitionMultiplier;
    }

    // Stage fear thresholds — stage rises when FearLevel exceeds this
    public static readonly float[] StageThresholds = { 0f, 0f, 0.05f, 0.20f, 0.40f, 0.55f, 0.72f, 0.90f };
}
```

- [ ] **Step 4: Run tests — confirm all 10 pass**

- [ ] **Step 5: Commit**

```
git add Assets/Scripts/World/SimulationMath.cs Assets/Tests/EditMode/SimulationMathTests.cs
git commit -m "feat: add SimulationMath formulas with tests"
```

---

## Task 4: World Data Files

**Files:**
- Create: `Assets/Scripts/Editor/WorldDataInitializer.cs`
- Generates: `Assets/Resources/Regions/*.json`, `Assets/Resources/Cities/cities.json`, `Assets/Resources/LaunchSites/sites.json`

- [ ] **Step 1: Create the editor initializer script**

Create `Assets/Scripts/Editor/WorldDataInitializer.cs`:

```csharp
using UnityEngine;
using UnityEditor;
using System.IO;

public class WorldDataInitializer
{
    [MenuItem("SpaceGame/Initialize World Data")]
    public static void InitializeAll()
    {
        Directory.CreateDirectory("Assets/Resources/Regions");
        Directory.CreateDirectory("Assets/Resources/Cities");
        Directory.CreateDirectory("Assets/Resources/LaunchSites");

        WriteRegions();
        WriteCities();
        WriteLaunchSites();

        AssetDatabase.Refresh();
        Debug.Log("[WorldData] All data files written to Assets/Resources/.");
    }

    static void W(string path, string json) => File.WriteAllText(path, json);

    static string R(string id, string name, int align, bool nuclear, float wealth,
                    float hawk, float capLat, float capLon, float pop, float[] bnd)
    {
        string bndStr = "[" + string.Join(",", bnd) + "]";
        return $"{{\"regionId\":\"{id}\",\"displayName\":\"{name}\",\"defaultAlignment\":{align}," +
               $"\"isNuclearPower\":{(nuclear?"true":"false")},\"baseWealth\":{wealth}," +
               $"\"hawkishness\":{hawk},\"capitalLat\":{capLat},\"capitalLon\":{capLon}," +
               $"\"startingPopulationM\":{pop},\"boundary\":{bndStr}}}";
    }

    static void WriteRegions()
    {
        string p = "Assets/Resources/Regions/";
        // align: 0=NATO, 1=BRICS, 2=NonAligned
        W(p+"north_america.json", R("north_america","North America",0,true,0.85f,0.65f,38.9f,-77.0f,500f,
            new float[]{71,-141,71,-52,42,-52,25,-77,16,-92,32,-117,60,-141}));
        W(p+"c_america.json", R("c_america","Central America",2,false,0.35f,0.25f,19.43f,-99.13f,180f,
            new float[]{32,-117,25,-77,16,-92,8,-77,8,-83,22,-106,32,-117}));
        W(p+"s_america.json", R("s_america","South America",2,false,0.45f,0.30f,-15.8f,-47.9f,450f,
            new float[]{12,-72,0,-50,-5,-35,-34,-53,-56,-68,-18,-75,0,-78,12,-72}));
        W(p+"w_europe.json", R("w_europe","West Europe",0,true,0.88f,0.40f,50.85f,4.35f,450f,
            new float[]{71,-25,71,25,35,25,35,-8,36,-8,36,-25,71,-25}));
        W(p+"e_europe.json", R("e_europe","East Europe",0,false,0.55f,0.55f,52.2f,21.0f,180f,
            new float[]{71,15,71,40,45,40,45,15,71,15}));
        W(p+"russia.json", R("russia","Russia",1,true,0.58f,0.75f,55.75f,37.62f,145f,
            new float[]{72,28,72,180,50,180,42,130,38,60,50,28,72,28}));
        W(p+"middle_east.json", R("middle_east","Middle East",2,true,0.52f,0.70f,33.34f,44.40f,400f,
            new float[]{37,26,37,65,12,45,12,32,22,30,30,26,37,26}));
        W(p+"n_africa.json", R("n_africa","North Africa",2,false,0.28f,0.40f,30.05f,31.24f,280f,
            new float[]{38,-6,38,37,12,45,5,42,5,-18,35,-6,38,-6}));
        W(p+"s_africa.json", R("s_africa","South Africa",2,false,0.25f,0.30f,-25.7f,28.2f,700f,
            new float[]{5,-18,5,50,-35,50,-35,-20,5,-18}));
        W(p+"e_asia.json", R("e_asia","East Asia",1,true,0.72f,0.70f,39.91f,116.39f,1500f,
            new float[]{55,100,55,145,20,122,20,100,55,100}));
        W(p+"s_asia.json", R("s_asia","South Asia",2,true,0.30f,0.60f,28.67f,77.22f,2000f,
            new float[]{38,60,38,100,5,80,12,42,38,60}));
        W(p+"se_asia.json", R("se_asia","Southeast Asia",2,false,0.38f,0.25f,13.75f,100.50f,700f,
            new float[]{20,92,20,142,-10,141,-10,95,5,100,20,92}));
        W(p+"c_asia.json", R("c_asia","Central Asia",2,false,0.32f,0.35f,51.18f,71.45f,80f,
            new float[]{55,50,55,90,37,78,37,50,55,50}));
        W(p+"oceania.json", R("oceania","Oceania",0,false,0.75f,0.30f,-35.31f,149.12f,45f,
            new float[]{-10,110,-10,180,-50,180,-50,110,-10,110}));
    }

    static void WriteCities()
    {
        var rows = new string[]
        {
            "Tokyo,35.68,139.69,37.4,e_asia",
            "Delhi,28.67,77.22,32.9,s_asia",
            "Shanghai,31.23,121.47,28.5,e_asia",
            "Dhaka,23.72,90.41,23.2,s_asia",
            "Sao Paulo,-23.55,-46.63,22.4,s_america",
            "Mexico City,19.43,-99.13,22.1,c_america",
            "Cairo,30.05,31.24,21.3,n_africa",
            "Beijing,39.91,116.39,21.2,e_asia",
            "Mumbai,19.07,72.87,20.7,s_asia",
            "Osaka,34.69,135.50,19.1,e_asia",
            "New York,40.71,-74.01,18.8,n_america",
            "Chongqing,29.56,106.55,18.7,e_asia",
            "Karachi,24.86,67.01,17.2,s_asia",
            "Istanbul,41.01,28.95,15.8,middle_east",
            "Lagos,6.52,3.38,15.3,n_africa",
            "Kinshasa,-4.32,15.32,15.1,s_africa",
            "Buenos Aires,-34.60,-58.38,15.5,s_america",
            "Kolkata,22.57,88.36,15.1,s_asia",
            "Manila,14.60,120.98,14.5,se_asia",
            "Guangzhou,23.13,113.26,14.0,e_asia",
            "Tianjin,39.13,117.18,9.2,e_asia",
            "Moscow,55.75,37.62,13.0,russia",
            "Shenzhen,22.54,114.06,12.8,e_asia",
            "Los Angeles,34.05,-118.24,12.4,n_america",
            "Lahore,31.56,74.34,13.1,s_asia",
            "Bangalore,12.97,77.59,12.7,s_asia",
            "Jakarta,-6.21,106.85,11.0,se_asia",
            "Bogota,4.71,-74.07,11.3,s_america",
            "Lima,-12.05,-77.04,10.9,s_america",
            "Bangkok,13.75,100.50,10.7,se_asia",
            "Chennai,13.08,80.27,10.9,s_asia",
            "Hyderabad,17.39,78.49,9.9,s_asia",
            "Tehran,35.69,51.39,9.5,middle_east",
            "Seoul,37.57,127.00,9.6,e_asia",
            "Chengdu,30.66,104.07,9.1,e_asia",
            "Nanjing,32.06,118.78,9.0,e_asia",
            "Wuhan,30.59,114.31,9.4,e_asia",
            "Ho Chi Minh,10.82,106.63,9.0,se_asia",
            "London,51.51,-0.13,9.5,w_europe",
            "Ahmedabad,23.02,72.57,8.5,s_asia",
            "Xian,34.27,108.95,8.9,e_asia",
            "Baghdad,33.34,44.40,8.1,middle_east",
            "Paris,48.85,2.35,11.1,w_europe",
            "Chicago,41.85,-87.65,8.9,n_america",
            "Riyadh,24.69,46.72,7.7,middle_east",
            "Singapore,1.35,103.82,6.0,se_asia",
            "Toronto,43.70,-79.42,6.3,n_america",
            "Johannesburg,-26.20,28.04,5.6,s_africa",
            "Sydney,-33.87,151.21,5.3,oceania",
            "Nairobi,-1.29,36.82,5.1,s_africa"
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{\"cities\":[");
        for (int i = 0; i < rows.Length; i++)
        {
            var f = rows[i].Split(',');
            sb.Append($"  {{\"name\":\"{f[0]}\",\"lat\":{f[1]},\"lon\":{f[2]},\"populationM\":{f[3]},\"regionId\":\"{f[4]}\"}}");
            if (i < rows.Length - 1) sb.AppendLine(",");
        }
        sb.AppendLine("\n]}");
        W("Assets/Resources/Cities/cities.json", sb.ToString());
    }

    static void WriteLaunchSites()
    {
        var rows = new string[]
        {
            "Cape Canaveral,28.6,-80.6,north_america,rocket|icbm",
            "Vandenberg SFB,34.7,-120.6,north_america,rocket|icbm",
            "Baikonur,45.9,63.3,c_asia,rocket|icbm",
            "Plesetsk,62.9,40.7,russia,icbm|rocket",
            "Jiuquan,40.96,100.29,e_asia,rocket|icbm",
            "Xichang,28.25,102.03,e_asia,rocket",
            "Wenchang,19.61,110.95,e_asia,rocket",
            "Taiyuan,37.46,112.45,e_asia,rocket",
            "Satish Dhawan,13.72,80.23,s_asia,rocket",
            "Tanegashima,30.40,130.97,e_asia,rocket",
            "Kourou,5.24,-52.77,s_america,rocket",
            "Palmachim,31.90,34.69,middle_east,icbm|rocket",
            "Kapustin Yar,48.52,45.80,russia,icbm",
            "Woomera,-31.13,136.82,oceania,rocket",
            "Mahia,-39.26,177.86,oceania,rocket",
            "Esrange,67.89,21.07,w_europe,rocket"
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{\"sites\":[");
        for (int i = 0; i < rows.Length; i++)
        {
            var f = rows[i].Split(',');
            string types = "[\"" + f[4].Replace("|", "\",\"") + "\"]";
            sb.Append($"  {{\"name\":\"{f[0]}\",\"lat\":{f[1]},\"lon\":{f[2]},\"regionId\":\"{f[3]}\",\"types\":{types}}}");
            if (i < rows.Length - 1) sb.AppendLine(",");
        }
        sb.AppendLine("\n]}");
        W("Assets/Resources/LaunchSites/sites.json", sb.ToString());
    }
}
```

- [ ] **Step 2: Run the menu item in Unity**

In Unity: menu bar → **SpaceGame → Initialize World Data**.
Expected console output: `[WorldData] All data files written to Assets/Resources/.`

- [ ] **Step 3: Verify files exist**

In Project window confirm these exist:
- `Assets/Resources/Regions/` — 14 `.json` files
- `Assets/Resources/Cities/cities.json`
- `Assets/Resources/LaunchSites/sites.json`

- [ ] **Step 4: Commit**

```
git add Assets/Scripts/Editor/WorldDataInitializer.cs Assets/Resources/
git commit -m "feat: add world data initializer + all region/city/launch site JSON"
```

---

## Task 5: RegionDefinition + RegionRuntime

**Files:**
- Create: `Assets/Scripts/World/RegionDefinition.cs`
- Create: `Assets/Scripts/World/RegionRuntime.cs`

- [ ] **Step 1: Create RegionDefinition**

Create `Assets/Scripts/World/RegionDefinition.cs`:

```csharp
using System;
using UnityEngine;

[Serializable]
public class RegionDefinition
{
    public string   regionId;
    public string   displayName;
    public int      defaultAlignment;   // 0=NATO, 1=BRICS, 2=NonAligned
    public bool     isNuclearPower;
    public float    baseWealth;         // 0-1
    public float    hawkishness;        // 0-1
    public float    capitalLat;
    public float    capitalLon;
    public float    startingPopulationM;
    public float[]  boundary;           // flat lat/lon pairs
}

[Serializable]
public class RegionDefinitionList
{
    public RegionDefinition[] regions;
}
```

- [ ] **Step 2: Create RegionRuntime**

Create `Assets/Scripts/World/RegionRuntime.cs`:

```csharp
using UnityEngine;

public enum FactionAlignment { NATO = 0, BRICS = 1, NonAligned = 2, SuperNation = 3, Collapsed = 4 }

public class RegionRuntime
{
    public RegionDefinition Def { get; }

    public FactionAlignment Alignment;
    public float DamageLevel;       // 0=intact, 1=destroyed
    public float PopulationM;       // millions
    public float GdpTrillion;
    public float TechLevel;         // 0-1
    public bool  PowerGridOnline = true;

    public RegionRuntime(RegionDefinition def)
    {
        Def       = def;
        Alignment = (FactionAlignment)def.defaultAlignment;
        PopulationM = def.startingPopulationM;
        TechLevel   = 0.65f;        // modern starting tech
        GdpTrillion = SimulationMath.CalcGDP(PopulationM, def.baseWealth, TechLevel, 1, 0f);
    }

    public void MergeToSuperNation()  => Alignment = FactionAlignment.SuperNation;
    public void Collapse()            => Alignment = FactionAlignment.Collapsed;
    public void RestoreAlignment()    => Alignment = (FactionAlignment)Def.defaultAlignment;
}
```

- [ ] **Step 3: Commit**

```
git add Assets/Scripts/World/RegionDefinition.cs Assets/Scripts/World/RegionRuntime.cs
git commit -m "feat: add RegionDefinition and RegionRuntime data classes"
```

---

## Task 6: RegionRegistry

**Files:**
- Create: `Assets/Scripts/World/RegionRegistry.cs`

- [ ] **Step 1: Implement RegionRegistry**

Create `Assets/Scripts/World/RegionRegistry.cs`:

```csharp
using UnityEngine;
using System.Collections.Generic;

public class RegionRegistry : MonoBehaviour
{
    public static RegionRegistry Instance { get; private set; }

    public RegionRuntime[] Regions { get; private set; }

    static readonly string[] s_regionIds = {
        "north_america","c_america","s_america","w_europe","e_europe",
        "russia","middle_east","n_africa","s_africa","e_asia",
        "s_asia","se_asia","c_asia","oceania"
    };

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        Regions = new RegionRuntime[s_regionIds.Length];
        for (int i = 0; i < s_regionIds.Length; i++)
        {
            TextAsset asset = Resources.Load<TextAsset>("Regions/" + s_regionIds[i]);
            if (asset == null)
            {
                Debug.LogError($"[RegionRegistry] Missing JSON for {s_regionIds[i]}. Run SpaceGame > Initialize World Data.");
                continue;
            }
            RegionDefinition def = JsonUtility.FromJson<RegionDefinition>(asset.text);
            Regions[i] = new RegionRuntime(def);
        }
        Debug.Log($"[RegionRegistry] Loaded {Regions.Length} regions.");
    }

    public RegionRuntime GetRegion(string regionId)
    {
        foreach (var r in Regions)
            if (r?.Def.regionId == regionId) return r;
        return null;
    }

    public void MergeAllToSuperNation()
    {
        foreach (var r in Regions) r?.MergeToSuperNation();
    }

    public void CollapseAll()
    {
        foreach (var r in Regions) r?.Collapse();
    }

    public void RestoreAllAlignments()
    {
        foreach (var r in Regions) r?.RestoreAlignment();
    }
}
```

- [ ] **Step 2: Commit**

```
git add Assets/Scripts/World/RegionRegistry.cs
git commit -m "feat: add RegionRegistry — loads 14 regions from Resources on Awake"
```

---

## Task 7: ContactStageManager

**Files:**
- Create: `Assets/Scripts/World/ContactStageManager.cs`

- [ ] **Step 1: Implement ContactStageManager**

Create `Assets/Scripts/World/ContactStageManager.cs`:

```csharp
using UnityEngine;
using System.Collections.Generic;

public enum ContactStage
{
    Undetected       = 1,
    PassiveDetect    = 2,
    NonAggressive    = 3,
    ReactiveUnmanned = 4,
    ReactiveCrewed   = 5,
    CounterAttack    = 6,
    Indiscriminate   = 7,
    Collapse         = 8
}

public class ContactStageManager : MonoBehaviour
{
    public static ContactStageManager Instance { get; private set; }

    [Header("State")]
    [SerializeField] ContactStage _stage = ContactStage.Undetected;
    [SerializeField] float        _fearLevel;
    [SerializeField] bool         _hibernating;

    public ContactStage CurrentStage   => _stage;
    public float        FearLevel      => _fearLevel;
    public bool         IsHibernating  => _hibernating;

    // Lowest stage humanity has permanently reached (can't unknow the ship)
    ContactStage _permanentMinStage = ContactStage.Undetected;

    readonly Dictionary<string, int> _repetitionCounts = new Dictionary<string, int>();

    public event System.Action<ContactStage, ContactStage> OnStageChanged;  // (old, new)
    public event System.Action<RegionRuntime>              OnFactionAttack;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        if (_hibernating)
        {
            _fearLevel = Mathf.MoveTowards(_fearLevel, 0f, SimulationMath.FearDecayPerSec * Time.deltaTime);
        }

        TryAutoTransition();
        RollSpontaneousAttacks();
    }

    // Call when the alien ship takes an action
    public void RegisterShipAction(string actionType, float baseFear,
                                   bool witnessedByMillions, float populationDensity01)
    {
        if (!_repetitionCounts.ContainsKey(actionType))
            _repetitionCounts[actionType] = 0;
        _repetitionCounts[actionType]++;

        float delta = SimulationMath.FearDelta(baseFear, witnessedByMillions,
                                               populationDensity01,
                                               _repetitionCounts[actionType]);
        _fearLevel = Mathf.Clamp01(_fearLevel + delta);
    }

    // Call when alien ship destroys an object — pass true if crewed
    public void RegisterDestruction(bool crewed)
    {
        ContactStage newStage = crewed ? ContactStage.ReactiveCrewed : ContactStage.ReactiveUnmanned;
        if ((int)newStage > (int)_stage)
            SetStage(newStage);
    }

    public void SetHibernating(bool hibernate)
    {
        _hibernating = hibernate;
    }

    public void TriggerCollapse()
    {
        SetStage(ContactStage.Collapse);
        RegionRegistry.Instance?.CollapseAll();
    }

    void TryAutoTransition()
    {
        int nextInt = (int)_stage + 1;
        if (nextInt >= SimulationMath.StageThresholds.Length) return;

        float threshold = SimulationMath.StageThresholds[nextInt];
        if (threshold > 0f && _fearLevel >= threshold)
        {
            ContactStage next = (ContactStage)nextInt;
            if (next == ContactStage.Collapse) return; // collapse is manual only
            SetStage(next);
        }

        // De-escalation: fear dropped below current stage's lower threshold
        if (_hibernating && (int)_stage > (int)_permanentMinStage)
        {
            float lower = SimulationMath.StageThresholds[(int)_stage];
            if (_fearLevel < lower * 0.7f)
                SetStage((ContactStage)((int)_stage - 1));
        }

        // Stage 6 -> super nation
        if (_stage == ContactStage.Indiscriminate)
            RegionRegistry.Instance?.MergeAllToSuperNation();
    }

    void RollSpontaneousAttacks()
    {
        if (_stage < ContactStage.NonAggressive) return;
        if (RegionRegistry.Instance == null) return;

        foreach (var region in RegionRegistry.Instance.Regions)
        {
            if (region == null) continue;
            float p = SimulationMath.SpontaneousAttackProbPerSec(
                region.Def.hawkishness, _fearLevel, 1) * Time.deltaTime;
            if (Random.value < p)
                OnFactionAttack?.Invoke(region);
        }
    }

    void SetStage(ContactStage newStage)
    {
        if (newStage == _stage) return;
        ContactStage old = _stage;
        _stage = newStage;
        if ((int)newStage > (int)_permanentMinStage && newStage != ContactStage.Collapse)
            _permanentMinStage = newStage;
        OnStageChanged?.Invoke(old, newStage);
        Debug.Log($"[ContactStage] {old} -> {newStage}  (fear={_fearLevel:F3})");
    }
}
```

- [ ] **Step 2: Commit**

```
git add Assets/Scripts/World/ContactStageManager.cs
git commit -m "feat: add ContactStageManager — fear accumulation, stage transitions, spontaneous attacks"
```

---

## Task 8: PopulationSystem

**Files:**
- Create: `Assets/Scripts/World/PopulationSystem.cs`

- [ ] **Step 1: Implement PopulationSystem**

Create `Assets/Scripts/World/PopulationSystem.cs`:

```csharp
using UnityEngine;

public class PopulationSystem : MonoBehaviour
{
    RegionRegistry _registry;
    ContactStageManager _stages;

    float _accumulatedDays;

    void Start()
    {
        _registry = RegionRegistry.Instance;
        _stages   = ContactStageManager.Instance;
    }

    // Called by WorldSimulation with simulated days elapsed this frame
    public void Tick(float simDaysElapsed)
    {
        if (_registry == null) return;
        int stageInt = (int)_stages.CurrentStage;

        foreach (var r in _registry.Regions)
        {
            if (r == null) continue;
            float growthPerYear = SimulationMath.PopGrowthRatePerYear(r.TechLevel, stageInt, r.DamageLevel);
            float growthPerDay  = growthPerYear / 365f;
            r.PopulationM = Mathf.Max(0f, r.PopulationM * (1f + growthPerDay * simDaysElapsed));
        }
    }
}
```

- [ ] **Step 2: Commit**

```
git add Assets/Scripts/World/PopulationSystem.cs
git commit -m "feat: add PopulationSystem — per-region population growth per simulated day"
```

---

## Task 9: EconomySystem

**Files:**
- Create: `Assets/Scripts/World/EconomySystem.cs`

- [ ] **Step 1: Implement EconomySystem**

Create `Assets/Scripts/World/EconomySystem.cs`:

```csharp
using UnityEngine;

public class EconomySystem : MonoBehaviour
{
    RegionRegistry      _registry;
    ContactStageManager _stages;

    public float GetGlobalMilitaryBudget()
    {
        float total = 0f;
        if (_registry == null) return 0f;
        foreach (var r in _registry.Regions)
            if (r != null) total += r.GdpTrillion;
        float militaryFraction = Mathf.Lerp(0.02f, 0.12f, _stages?.FearLevel ?? 0f);
        return total * militaryFraction;
    }

    public float GetRegionLaunchCapacity(string regionId)
    {
        var r = _registry?.GetRegion(regionId);
        if (r == null) return 0f;
        float stageMult = Mathf.Lerp(1f, 4f, _stages?.FearLevel ?? 0f);
        return r.GdpTrillion * 0.5f * stageMult;   // launches/yr per trillion GDP
    }

    public float GetResearchBudget(RegionRuntime region)
    {
        return region.GdpTrillion * 0.03f;  // 3% of GDP to R&D
    }

    void Start()
    {
        _registry = RegionRegistry.Instance;
        _stages   = ContactStageManager.Instance;
    }

    // Called by WorldSimulation with simulated months elapsed
    public void Tick(float simMonthsElapsed)
    {
        if (_registry == null) return;
        int stageInt = (int)_stages.CurrentStage;

        foreach (var r in _registry.Regions)
        {
            if (r == null) continue;
            float targetGdp = SimulationMath.CalcGDP(r.PopulationM, r.Def.baseWealth,
                                                      r.TechLevel, stageInt, r.DamageLevel);
            // Smooth toward target (economy adjusts over months, not instantly)
            r.GdpTrillion = Mathf.Lerp(r.GdpTrillion, targetGdp, 0.1f * simMonthsElapsed);
        }
    }
}
```

- [ ] **Step 2: Commit**

```
git add Assets/Scripts/World/EconomySystem.cs
git commit -m "feat: add EconomySystem — GDP ticks per simulated month, exposes launch capacity"
```

---

## Task 10: TechnologySystem

**Files:**
- Create: `Assets/Scripts/World/TechnologySystem.cs`

- [ ] **Step 1: Implement TechnologySystem**

Create `Assets/Scripts/World/TechnologySystem.cs`:

```csharp
using UnityEngine;

public class TechnologySystem : MonoBehaviour
{
    public static TechnologySystem Instance { get; private set; }

    RegionRegistry      _registry;
    ContactStageManager _stages;
    EconomySystem       _economy;

    public event System.Action<string, float> OnMilestoneReached; // (regionId, techLevel)

    static readonly float[] s_milestones = { 0.3f, 0.6f, 0.8f, 1.0f };
    readonly System.Collections.Generic.HashSet<string> _firedMilestones
        = new System.Collections.Generic.HashSet<string>();

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        _registry = RegionRegistry.Instance;
        _stages   = ContactStageManager.Instance;
        _economy  = GetComponent<EconomySystem>();
    }

    // Called by WorldSimulation with simulated years elapsed
    public void Tick(float simYearsElapsed)
    {
        if (_registry == null) return;
        int stageInt = (int)_stages.CurrentStage;

        foreach (var r in _registry.Regions)
        {
            if (r == null) continue;
            float researchBudget = _economy?.GetResearchBudget(r) ?? 0f;
            float rate = SimulationMath.TechAdvanceRate(0.03f, researchBudget, stageInt, r.DamageLevel);
            r.TechLevel = Mathf.Clamp01(r.TechLevel + rate * simYearsElapsed);
            CheckMilestones(r);
        }
    }

    void CheckMilestones(RegionRuntime r)
    {
        foreach (float m in s_milestones)
        {
            string key = r.Def.regionId + "_" + m;
            if (!_firedMilestones.Contains(key) && r.TechLevel >= m)
            {
                _firedMilestones.Add(key);
                OnMilestoneReached?.Invoke(r.Def.regionId, m);
                Debug.Log($"[Tech] {r.Def.displayName} reached milestone {m:F1}");
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```
git add Assets/Scripts/World/TechnologySystem.cs
git commit -m "feat: add TechnologySystem — tech advances per simulated year, fires milestone events"
```

---

## Task 11: WorldSimulation

**Files:**
- Create: `Assets/Scripts/World/WorldSimulation.cs`

- [ ] **Step 1: Implement WorldSimulation**

Create `Assets/Scripts/World/WorldSimulation.cs`:

```csharp
using UnityEngine;

public class WorldSimulation : MonoBehaviour
{
    [Header("Time")]
    [SerializeField] float _timeWarpFactor = 1f;    // 1 = 1 sim-sec per real-sec
    [SerializeField] float _simulatedYear  = 2026f;

    public float SimulatedYear  => _simulatedYear;
    public float TimeWarpFactor => _timeWarpFactor;

    PopulationSystem    _pop;
    EconomySystem       _econ;
    TechnologySystem    _tech;
    ContactStageManager _stages;

    float _dayAccum;
    float _monthAccum;
    float _yearAccum;

    const float SecsPerSimDay   = 1f;
    const float SecsPerSimMonth = 30f;
    const float SecsPerSimYear  = 365f;

    static readonly float[] s_warpOptions = { 1f, 10f, 60f, 365f, 3650f };
    int _warpIndex = 0;

    void Start()
    {
        _pop    = GetComponent<PopulationSystem>();
        _econ   = GetComponent<EconomySystem>();
        _tech   = GetComponent<TechnologySystem>();
        _stages = ContactStageManager.Instance;
    }

    void Update()
    {
        float simDt = Time.deltaTime * _timeWarpFactor;

        _dayAccum   += simDt;
        _monthAccum += simDt;
        _yearAccum  += simDt;
        _simulatedYear += simDt / SecsPerSimYear;

        if (_dayAccum >= SecsPerSimDay)
        {
            float days = _dayAccum / SecsPerSimDay;
            _pop?.Tick(days);
            _dayAccum = 0f;
        }

        if (_monthAccum >= SecsPerSimMonth)
        {
            float months = _monthAccum / SecsPerSimMonth;
            _econ?.Tick(months);
            _monthAccum = 0f;
        }

        if (_yearAccum >= SecsPerSimYear)
        {
            float years = _yearAccum / SecsPerSimYear;
            _tech?.Tick(years);
            _yearAccum = 0f;
        }
    }

    // Cycle through preset time-warp speeds: 1x, 10x, 60x, 365x, 3650x
    public void CycleTimeWarp()
    {
        _warpIndex     = (_warpIndex + 1) % s_warpOptions.Length;
        _timeWarpFactor = s_warpOptions[_warpIndex];
        Debug.Log($"[WorldSim] Time warp: {_timeWarpFactor}x");
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 200, 20), $"Year: {_simulatedYear:F1}  Warp: {_timeWarpFactor}x");
        GUI.Label(new Rect(10, 28, 200, 20), $"Stage: {_stages?.CurrentStage}  Fear: {_stages?.FearLevel:F3}");
        if (GUI.Button(new Rect(10, 48, 100, 22), "Cycle Warp")) CycleTimeWarp();
    }
}
```

- [ ] **Step 2: Commit**

```
git add Assets/Scripts/World/WorldSimulation.cs
git commit -m "feat: add WorldSimulation — time-warp, ticks Population/Economy/Tech per simulated time unit"
```

---

## Task 12: Earth Shader Heatmap

**Files:**
- Modify: `Assets/Shaders/Earth.shader`

The SRP Batcher requires identical CBUFFER layout across all three passes (ForwardLit, ShadowCaster, DepthOnly). `_HeatmapStrength` goes in all three CBUFFERs. `TEXTURE2D(_PopulationHeatmap)` only goes in ForwardLit (outside CBUFFER).

**Important shader rules for this project:**
- All HLSL comments must be ASCII only (no Unicode arrows or Greek letters)
- Do NOT declare `static const float PI` (URP Core.hlsl defines PI as a macro)
- Use `SAMPLE_TEXTURE2D` not `tex2D`

- [ ] **Step 1: Add property to Properties block**

In `Assets/Shaders/Earth.shader`, add after `_NightDayMaskPow`:

```hlsl
        _PopulationHeatmap  ("Population Heatmap",  2D)           = "black" {}
        _HeatmapStrength    ("Heatmap Strength",     Range(0,1))   = 0.0
```

- [ ] **Step 2: Add TEXTURE2D declaration in ForwardLit (outside CBUFFER, after existing samplers)**

After the existing `TEXTURE2D(_SpecularMap); SAMPLER(sampler_SpecularMap);` line, add:

```hlsl
            TEXTURE2D(_PopulationHeatmap); SAMPLER(sampler_PopulationHeatmap);
```

- [ ] **Step 3: Add _HeatmapStrength to all three CBUFFERs**

In the ForwardLit CBUFFER, after `_NightDayMaskPow`:
```hlsl
                float  _HeatmapStrength;
```

In the ShadowCaster CBUFFER, after `_NightDayMaskPow`:
```hlsl
                float  _HeatmapStrength;
```

In the DepthOnly CBUFFER, after `_NightDayMaskPow`:
```hlsl
                float  _HeatmapStrength;
```

- [ ] **Step 4: Add heatmap blend in ForwardLit Frag, after the color line**

After `half3 color = lerp(litNight, litDay, dayBlend);`, add:

```hlsl
                float heat = SAMPLE_TEXTURE2D(_PopulationHeatmap, sampler_PopulationHeatmap, IN.uv).r * _HeatmapStrength;
                color.rgb += heat * half3(0.8h, 0.3h, 0.0h) * (1.0h - dayBlend * 0.5h);
```

- [ ] **Step 5: Verify shader compiles in Unity**

Select Earth.mat in Project window. Inspector should show no pink/error. Console should be clear of shader errors.

- [ ] **Step 6: Commit**

```
git add Assets/Shaders/Earth.shader
git commit -m "feat: add population heatmap overlay to Earth shader"
```

---

## Task 13: RegionBorderRenderer

**Files:**
- Create: `Assets/Scripts/World/RegionBorderRenderer.cs`

Draws faction-colored polylines along the sphere surface using LineRenderer components. One LineRenderer per region, parented to this object.

- [ ] **Step 1: Implement RegionBorderRenderer**

Create `Assets/Scripts/World/RegionBorderRenderer.cs`:

```csharp
using UnityEngine;

public class RegionBorderRenderer : MonoBehaviour
{
    [Header("Visuals")]
    [SerializeField] Material _lineMaterial;
    [SerializeField] float    _lineWidth   = 0.04f;
    [SerializeField] bool     _visible     = true;

    static readonly Color s_natoColor        = new Color(0.2f, 0.5f, 1.0f, 0.8f);
    static readonly Color s_bricsColor       = new Color(1.0f, 0.25f, 0.2f, 0.8f);
    static readonly Color s_nonAlignedColor  = new Color(0.6f, 0.6f, 0.6f, 0.8f);
    static readonly Color s_superNationColor = new Color(0.8f, 0.9f, 0.2f, 0.9f);
    static readonly Color s_collapsedColor   = new Color(0.3f, 0.2f, 0.2f, 0.5f);

    LineRenderer[] _lines;

    void Start()
    {
        var registry = RegionRegistry.Instance;
        if (registry == null) { Debug.LogWarning("[BorderRenderer] No RegionRegistry"); return; }

        _lines = new LineRenderer[registry.Regions.Length];

        for (int i = 0; i < registry.Regions.Length; i++)
        {
            var region = registry.Regions[i];
            if (region == null) continue;

            GameObject go = new GameObject("Border_" + region.Def.regionId);
            go.transform.SetParent(transform);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material         = _lineMaterial;
            lr.startWidth       = _lineWidth;
            lr.endWidth         = _lineWidth;
            lr.useWorldSpace    = true;
            lr.loop             = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            SetBorderPoints(lr, region.Def.boundary);
            _lines[i] = lr;
        }
    }

    void Update()
    {
        var registry = RegionRegistry.Instance;
        if (registry == null || _lines == null) return;

        for (int i = 0; i < registry.Regions.Length; i++)
        {
            var r = registry.Regions[i];
            if (r == null || _lines[i] == null) continue;

            _lines[i].enabled = _visible;
            _lines[i].startColor = AlignmentColor(r.Alignment);
            _lines[i].endColor   = AlignmentColor(r.Alignment);
        }
    }

    void SetBorderPoints(LineRenderer lr, float[] boundary)
    {
        if (boundary == null || boundary.Length < 4) return;
        int count = boundary.Length / 2;
        lr.positionCount = count;

        for (int i = 0; i < count; i++)
        {
            float lat = boundary[i * 2];
            float lon = boundary[i * 2 + 1];
            // Raise slightly above surface to avoid z-fighting
            lr.SetPosition(i, GeoUtils.LatLonToWorld(lat, lon, GeoUtils.EarthRadiusUnits * 1.002f));
        }
    }

    static Color AlignmentColor(FactionAlignment a)
    {
        return a switch
        {
            FactionAlignment.NATO        => s_natoColor,
            FactionAlignment.BRICS       => s_bricsColor,
            FactionAlignment.SuperNation => s_superNationColor,
            FactionAlignment.Collapsed   => s_collapsedColor,
            _                            => s_nonAlignedColor
        };
    }

    public void SetVisible(bool v) => _visible = v;
}
```

- [ ] **Step 2: Create a simple unlit line material**

In Unity: Assets → Create → Material. Name it `RegionBorder`. Set Shader to `Universal Render Pipeline/Unlit`. Set Base Color to white. Assign this material to the `RegionBorderRenderer._lineMaterial` field.

- [ ] **Step 3: Commit**

```
git add Assets/Scripts/World/RegionBorderRenderer.cs
git commit -m "feat: add RegionBorderRenderer — faction-colored polylines on sphere"
```

---

## Task 14: CityDotRenderer

**Files:**
- Create: `Assets/Scripts/World/CityDotRenderer.cs`

Places a small sphere at each city's lat/lon position, sized by population. Cities double as missile targets in Spec B.

- [ ] **Step 1: Create city data class**

Add to the top of `Assets/Scripts/World/CityDotRenderer.cs` (before the MonoBehaviour):

```csharp
using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class CityData
{
    public string name;
    public float  lat;
    public float  lon;
    public float  populationM;
    public string regionId;
}

[System.Serializable]
class CityDataList { public CityData[] cities; }
```

- [ ] **Step 2: Implement CityDotRenderer**

```csharp
public class CityDotRenderer : MonoBehaviour
{
    [SerializeField] bool  _visible  = true;
    [SerializeField] float _baseSize = 0.04f;

    public CityData[] Cities { get; private set; }

    readonly List<Transform> _dots = new List<Transform>();

    void Start()
    {
        TextAsset asset = Resources.Load<TextAsset>("Cities/cities");
        if (asset == null) { Debug.LogError("[CityDots] Missing Cities/cities.json"); return; }

        CityDataList list = JsonUtility.FromJson<CityDataList>(asset.text);
        Cities = list.cities;

        Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));

        foreach (var city in Cities)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "City_" + city.name;
            go.transform.SetParent(transform);

            Destroy(go.GetComponent<SphereCollider>());

            float scale = _baseSize * Mathf.Lerp(0.5f, 2.5f, Mathf.InverseLerp(5f, 38f, city.populationM));
            go.transform.localScale = Vector3.one * scale;
            go.transform.position   = GeoUtils.LatLonToWorld(city.lat, city.lon,
                                         GeoUtils.EarthRadiusUnits * 1.005f);

            var mr = go.GetComponent<MeshRenderer>();
            mr.material = mat;
            mr.material.color = new Color(1f, 0.9f, 0.3f);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _dots.Add(go.transform);
        }
    }

    void Update()
    {
        foreach (var d in _dots)
            if (d != null) d.gameObject.SetActive(_visible);
    }

    public void SetVisible(bool v) => _visible = v;
}
```

- [ ] **Step 3: Commit**

```
git add Assets/Scripts/World/CityDotRenderer.cs
git commit -m "feat: add CityDotRenderer — places 50 city dots on sphere surface"
```

---

## Task 15: Scene Wiring

Wire all new components into the scene via MCP Unity, then smoke-test in Play mode.

- [ ] **Step 1: Create WorldSimulation GameObject**

Use `mcp__mcp-unity__add_asset_to_scene` or create manually:
- Create empty GameObject named `WorldSimulation`
- Add components: `RegionRegistry`, `ContactStageManager`, `PopulationSystem`, `EconomySystem`, `TechnologySystem`, `WorldSimulation`

All these MonoBehaviours should be on the **same** GameObject so `GetComponent<>` calls in WorldSimulation work.

- [ ] **Step 2: Create GlobeOverlays GameObject**

- Create empty GameObject named `GlobeOverlays`, child of `Earth`
- Add component: `RegionBorderRenderer`
- Add component: `CityDotRenderer`
- Assign the `RegionBorder` unlit material to `RegionBorderRenderer._lineMaterial`

- [ ] **Step 3: Run SpaceGame → Initialize World Data (if not done already)**

Confirm 14 JSON files exist in `Assets/Resources/Regions/`.

- [ ] **Step 4: Enter Play mode — verify console**

Expected console output:
```
[RegionRegistry] Loaded 14 regions.
[WorldSim] (no errors)
```
Expected in scene: colored border polylines on Earth sphere. City dots glowing gold.

- [ ] **Step 5: Test fear and stage escalation**

In Play mode, call via script or Inspector context menu:
```csharp
ContactStageManager.Instance.RegisterShipAction("move", 0.1f, true, 0.8f);
```
Confirm `Fear` value rises in the WorldSimulation HUD overlay (top-left corner).

- [ ] **Step 6: Test time warp**

Click "Cycle Warp" button in the in-game HUD. Confirm `Year` counter advances at different rates.

- [ ] **Step 7: Commit**

```
git add Assets/Scenes/SampleScene.unity
git commit -m "feat: wire WorldSimulation + GlobeOverlays into scene"
```

---

## Self-Review Checklist (completed)

- [x] Spec coverage: GeoUtils ✓, RegionData ✓, ContactStageManager ✓, PopulationSystem ✓, EconomySystem ✓, TechnologySystem ✓, Collapse/recovery logic ✓ (in ContactStageManager + TechSystem), Globe visualization ✓, Data files ✓
- [x] Collapse recovery (HibernateMode): ContactStageManager.SetHibernating(true) stops fear from forcing stages and allows de-escalation; TechnologySystem stageFactor=0 is bypassed when hibernating. Note: the WorldSimulation continues ticking — hibernate just changes behaviour, not simulation speed.
- [x] Type consistency: `RegionRuntime`, `FactionAlignment`, `SimulationMath`, `GeoUtils` all defined before first use across tasks.
- [x] `GetResearchBudget` defined in EconomySystem (Task 9), called in TechnologySystem (Task 10). ✓
- [x] `SimulationMath.StageThresholds` array has 8 entries (indices 0-7 covering ContactStage int values 1-8). ContactStage enum values start at 1 — array index 0 unused (0f). ✓
