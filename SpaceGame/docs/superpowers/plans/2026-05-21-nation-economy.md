# Nation Economy Simulation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every nation its own GDP/population simulation, a nation-selection click on the globe, a top-left stat panel, and a launch site system — while removing city dot orbs.

**Architecture:** `NationDataRegistry` loads a JSON snapshot of 2024 IMF data and exposes per-nation `NationRuntime` objects by country index. `NationEconomySystem` ticks continuously via deltaTime with a monthly strategic accumulator. `NationSelectionSystem` raycasts against Earth on mouse click and raises a selection event. `NationStatPanel` listens and draws the panel. `LaunchSiteSystem` queues build/launch decisions per monthly tick.

**Tech Stack:** Unity 6 URP, C# 10, Unity Input System (Mouse.current), UnityEngine.UI + TextMeshPro, Unity Test Framework (EditMode), JSON via JsonUtility.

---

## File Map

| Action | Path |
|---|---|
| Create | `Assets/Resources/Data/countries.json` |
| Create | `Assets/Scripts/World/NationRuntime.cs` |
| Create | `Assets/Scripts/World/NationDataRegistry.cs` |
| Create | `Assets/Scripts/World/NationEconomySystem.cs` |
| Create | `Assets/Scripts/World/NationSelectionSystem.cs` |
| Create | `Assets/Scripts/World/NationStatPanel.cs` |
| Create | `Assets/Scripts/World/LaunchSiteSystem.cs` |
| Create | `Assets/Tests/EditMode/NationEconomyTests.cs` |
| Modify | `Assets/Scripts/World/WorldSimulation.cs` |
| Modify | `Assets/Scripts/World/FactionTextureRenderer.cs` |
| Modify | `Assets/Scripts/World/GlobeOverlayToggle.cs` |
| Modify | `Assets/Shaders/Earth.shader` |
| Delete | `Assets/Scripts/World/CityDotRenderer.cs` |

---

## Task 1: Cleanup — City Orbs, Shader Heatmap, Time Constant

### Files:
- Modify: `Assets/Shaders/Earth.shader`
- Modify: `Assets/Scripts/World/GlobeOverlayToggle.cs`
- Modify: `Assets/Scripts/World/WorldSimulation.cs`
- Delete: `Assets/Scripts/World/CityDotRenderer.cs`

- [ ] **Step 1: Remove heatmap from Earth.shader Properties block**

In `Assets/Shaders/Earth.shader`, remove these two lines from the `Properties` block:
```hlsl
// DELETE these two lines:
_PopulationHeatmap  ("Population Heatmap",  2D)           = "black" {}
_HeatmapStrength    ("Heatmap Strength",     Range(0,1))   = 0.0
```

- [ ] **Step 2: Remove heatmap texture declaration from ForwardLit pass**

In the ForwardLit pass, remove:
```hlsl
// DELETE this line:
TEXTURE2D(_PopulationHeatmap); SAMPLER(sampler_PopulationHeatmap);
```

- [ ] **Step 3: Remove _HeatmapStrength from all three CBUFFERs**

Remove `float  _HeatmapStrength;` from **all three** `CBUFFER_START(UnityPerMaterial)` blocks (ForwardLit, ShadowCaster, DepthOnly). Each CBUFFER must be identical — all three must be changed.

Result — each CBUFFER should look like:
```hlsl
CBUFFER_START(UnityPerMaterial)
    float4 _DayTex_ST;
    float4 _SpecularColor;
    float  _Shininess;
    float  _TerminatorSharpness;
    float  _NightBrightness;
    float  _NightDayMaskPow;
    float  _NormalStrength;
    float  _FactionStrength;
    float  _BorderStrength;
CBUFFER_END
```

- [ ] **Step 4: Remove heatmap sampling from ForwardLit fragment shader**

Remove these two lines from the `Frag` function:
```hlsl
// DELETE these two lines:
float heat = SAMPLE_TEXTURE2D(_PopulationHeatmap, sampler_PopulationHeatmap, IN.uv).r * _HeatmapStrength;
color.rgb += heat * half3(0.8h, 0.3h, 0.0h) * (1.0h - dayBlend * 0.5h);
```

- [ ] **Step 5: Delete CityDotRenderer.cs**

Delete the file `Assets/Scripts/World/CityDotRenderer.cs`.

- [ ] **Step 6: Rewrite GlobeOverlayToggle.cs — remove city/heatmap, keep F1/F2**

Replace the entire file with:

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

public class GlobeOverlayToggle : MonoBehaviour
{
    [Header("Default strengths when ON")]
    [SerializeField] float _factionStrength = 0.4f;
    [SerializeField] float _borderStrength  = 0.6f;

    bool _factionOn = true;
    bool _bordersOn = true;

    MeshRenderer          _earthRenderer;
    MaterialPropertyBlock _propBlock;

    void Start()
    {
        _earthRenderer = transform.parent?.GetComponent<MeshRenderer>();
        _propBlock     = new MaterialPropertyBlock();
        if (_earthRenderer == null)
            Debug.LogWarning("[GlobeToggle] Earth MeshRenderer not found on parent.");
        ApplyAll();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.f1Key.wasPressedThisFrame) SetFactionVisible(!_factionOn);
        if (kb.f2Key.wasPressedThisFrame) SetBordersVisible(!_bordersOn);
    }

    public void SetFactionVisible(bool v)
    {
        _factionOn = v;
        SetPropFloat("_FactionStrength", v ? _factionStrength : 0f);
    }

    public void SetBordersVisible(bool v)
    {
        _bordersOn = v;
        SetPropFloat("_BorderStrength", v ? _borderStrength : 0f);
    }

    void SetPropFloat(string name, float value)
    {
        if (_earthRenderer == null) return;
        _earthRenderer.GetPropertyBlock(_propBlock);
        _propBlock.SetFloat(name, value);
        _earthRenderer.SetPropertyBlock(_propBlock);
    }

    void ApplyAll()
    {
        SetFactionVisible(_factionOn);
        SetBordersVisible(_bordersOn);
    }
}
```

- [ ] **Step 7: Update WorldSimulation.cs — change SecsPerSimMonth to 60f**

In `Assets/Scripts/World/WorldSimulation.cs`, change:
```csharp
const float SecsPerSimMonth = 30f;
```
to:
```csharp
const float SecsPerSimMonth = 60f;
```

- [ ] **Step 8: Verify Unity compiles**

Open Unity. Confirm no compile errors in the Console. F1/F2 should still toggle faction/border overlays. City orbs gone.

- [ ] **Step 9: Commit**

```
git add Assets/Shaders/Earth.shader Assets/Scripts/World/GlobeOverlayToggle.cs Assets/Scripts/World/WorldSimulation.cs
git rm Assets/Scripts/World/CityDotRenderer.cs
git commit -m "cleanup: remove city dot orbs and heatmap overlay, set sim month to 60s"
```

---

## Task 2: countries.json — Real GDP + Population Data

### Files:
- Create: `Assets/Resources/Data/countries.json`

- [ ] **Step 1: Create directory and write countries.json**

Create `Assets/Resources/Data/` and write the file. Each entry uses the ISO3 code matching `WorldRegionMapper`. GDP in billions USD (2024 IMF estimates). Population in millions.

```json
[
  {"iso3":"ABW","gdp":3.1,"pop":0.11},
  {"iso3":"AFG","gdp":14.0,"pop":42.0},
  {"iso3":"AGO","gdp":91.0,"pop":36.0},
  {"iso3":"AIA","gdp":0.4,"pop":0.018},
  {"iso3":"ALB","gdp":23.0,"pop":2.8},
  {"iso3":"AND","gdp":3.7,"pop":0.077},
  {"iso3":"ARE","gdp":530.0,"pop":10.0},
  {"iso3":"ARG","gdp":640.0,"pop":46.0},
  {"iso3":"ARM","gdp":24.0,"pop":2.8},
  {"iso3":"ASM","gdp":0.6,"pop":0.055},
  {"iso3":"ATG","gdp":2.0,"pop":0.1},
  {"iso3":"AUS","gdp":1759.0,"pop":26.0},
  {"iso3":"AUT","gdp":527.0,"pop":9.1},
  {"iso3":"AZE","gdp":78.0,"pop":10.0},
  {"iso3":"BDI","gdp":3.8,"pop":13.0},
  {"iso3":"BEL","gdp":629.0,"pop":11.6},
  {"iso3":"BEN","gdp":17.0,"pop":13.0},
  {"iso3":"BHR","gdp":44.0,"pop":1.5},
  {"iso3":"BHS","gdp":14.0,"pop":0.4},
  {"iso3":"BIH","gdp":25.0,"pop":3.2},
  {"iso3":"BLM","gdp":0.7,"pop":0.01},
  {"iso3":"BLR","gdp":73.0,"pop":9.5},
  {"iso3":"BLZ","gdp":2.7,"pop":0.4},
  {"iso3":"BMU","gdp":8.0,"pop":0.064},
  {"iso3":"BOL","gdp":43.0,"pop":12.0},
  {"iso3":"BRA","gdp":2330.0,"pop":215.0},
  {"iso3":"BRB","gdp":5.7,"pop":0.3},
  {"iso3":"BRN","gdp":16.0,"pop":0.45},
  {"iso3":"BTN","gdp":3.2,"pop":0.77},
  {"iso3":"BWA","gdp":19.0,"pop":2.6},
  {"iso3":"CAF","gdp":2.7,"pop":5.6},
  {"iso3":"CAN","gdp":2272.0,"pop":38.0},
  {"iso3":"CHL","gdp":344.0,"pop":19.0},
  {"iso3":"CHN","gdp":18500.0,"pop":1410.0},
  {"iso3":"CIV","gdp":78.0,"pop":27.0},
  {"iso3":"CMR","gdp":47.0,"pop":28.0},
  {"iso3":"COD","gdp":62.0,"pop":100.0},
  {"iso3":"COG","gdp":12.0,"pop":6.0},
  {"iso3":"COK","gdp":0.35,"pop":0.017},
  {"iso3":"COL","gdp":363.0,"pop":52.0},
  {"iso3":"COM","gdp":1.4,"pop":0.9},
  {"iso3":"CPV","gdp":2.6,"pop":0.56},
  {"iso3":"CRI","gdp":77.0,"pop":5.2},
  {"iso3":"CUB","gdp":107.0,"pop":11.0},
  {"iso3":"CUW","gdp":3.2,"pop":0.15},
  {"iso3":"CYM","gdp":6.0,"pop":0.065},
  {"iso3":"CYP","gdp":30.0,"pop":1.2},
  {"iso3":"CZE","gdp":346.0,"pop":10.8},
  {"iso3":"DEU","gdp":4590.0,"pop":84.0},
  {"iso3":"DJI","gdp":4.2,"pop":1.0},
  {"iso3":"DMA","gdp":0.6,"pop":0.073},
  {"iso3":"DNK","gdp":406.0,"pop":5.9},
  {"iso3":"DOM","gdp":120.0,"pop":11.0},
  {"iso3":"DZA","gdp":240.0,"pop":46.0},
  {"iso3":"ECU","gdp":120.0,"pop":18.0},
  {"iso3":"EGY","gdp":395.0,"pop":105.0},
  {"iso3":"ERI","gdp":2.4,"pop":3.6},
  {"iso3":"ESH","gdp":2.5,"pop":0.6},
  {"iso3":"ESP","gdp":1581.0,"pop":47.4},
  {"iso3":"EST","gdp":40.0,"pop":1.4},
  {"iso3":"ETH","gdp":175.0,"pop":125.0},
  {"iso3":"FIN","gdp":306.0,"pop":5.6},
  {"iso3":"FJI","gdp":4.9,"pop":0.93},
  {"iso3":"FLK","gdp":0.2,"pop":0.003},
  {"iso3":"FRA","gdp":3130.0,"pop":68.0},
  {"iso3":"FRO","gdp":3.7,"pop":0.054},
  {"iso3":"FSM","gdp":0.44,"pop":0.11},
  {"iso3":"GAB","gdp":18.0,"pop":2.3},
  {"iso3":"GBR","gdp":3340.0,"pop":68.0},
  {"iso3":"GEO","gdp":30.0,"pop":3.7},
  {"iso3":"GHA","gdp":72.0,"pop":33.0},
  {"iso3":"GIN","gdp":21.0,"pop":13.0},
  {"iso3":"GLP","gdp":12.0,"pop":0.38},
  {"iso3":"GMB","gdp":2.4,"pop":2.6},
  {"iso3":"GNB","gdp":1.8,"pop":2.1},
  {"iso3":"GNQ","gdp":12.0,"pop":1.5},
  {"iso3":"GRC","gdp":243.0,"pop":10.4},
  {"iso3":"GRD","gdp":1.4,"pop":0.12},
  {"iso3":"GRL","gdp":3.1,"pop":0.056},
  {"iso3":"GTM","gdp":95.0,"pop":18.0},
  {"iso3":"GUF","gdp":5.6,"pop":0.3},
  {"iso3":"GUM","gdp":6.1,"pop":0.17},
  {"iso3":"GUY","gdp":28.0,"pop":0.8},
  {"iso3":"HKG","gdp":373.0,"pop":7.5},
  {"iso3":"HND","gdp":32.0,"pop":10.0},
  {"iso3":"HRV","gdp":82.0,"pop":3.9},
  {"iso3":"HTI","gdp":21.0,"pop":11.5},
  {"iso3":"HUN","gdp":220.0,"pop":9.7},
  {"iso3":"IDN","gdp":1420.0,"pop":275.0},
  {"iso3":"IND","gdp":3890.0,"pop":1440.0},
  {"iso3":"IRL","gdp":590.0,"pop":5.2},
  {"iso3":"IRN","gdp":401.0,"pop":89.0},
  {"iso3":"IRQ","gdp":265.0,"pop":43.0},
  {"iso3":"ISL","gdp":31.0,"pop":0.37},
  {"iso3":"ISR","gdp":525.0,"pop":9.5},
  {"iso3":"ITA","gdp":2330.0,"pop":59.0},
  {"iso3":"JAM","gdp":19.0,"pop":3.0},
  {"iso3":"JOR","gdp":50.0,"pop":11.0},
  {"iso3":"JPN","gdp":4213.0,"pop":125.0},
  {"iso3":"KAZ","gdp":262.0,"pop":19.0},
  {"iso3":"KEN","gdp":113.0,"pop":55.0},
  {"iso3":"KGZ","gdp":13.0,"pop":7.0},
  {"iso3":"KHM","gdp":31.0,"pop":17.0},
  {"iso3":"KIR","gdp":0.25,"pop":0.12},
  {"iso3":"KNA","gdp":1.1,"pop":0.05},
  {"iso3":"KOR","gdp":1897.0,"pop":51.0},
  {"iso3":"KWT","gdp":163.0,"pop":4.4},
  {"iso3":"LAO","gdp":15.0,"pop":7.4},
  {"iso3":"LBN","gdp":23.0,"pop":5.4},
  {"iso3":"LBR","gdp":4.0,"pop":5.4},
  {"iso3":"LBY","gdp":42.0,"pop":7.3},
  {"iso3":"LCA","gdp":2.5,"pop":0.18},
  {"iso3":"LIE","gdp":7.6,"pop":0.038},
  {"iso3":"LSO","gdp":2.2,"pop":2.2},
  {"iso3":"LTU","gdp":78.0,"pop":2.8},
  {"iso3":"LUX","gdp":85.0,"pop":0.67},
  {"iso3":"LVA","gdp":44.0,"pop":1.8},
  {"iso3":"MAC","gdp":44.0,"pop":0.68},
  {"iso3":"MAF","gdp":0.6,"pop":0.04},
  {"iso3":"MAR","gdp":141.0,"pop":38.0},
  {"iso3":"MCO","gdp":8.8,"pop":0.037},
  {"iso3":"MDA","gdp":17.0,"pop":2.6},
  {"iso3":"MDG","gdp":16.0,"pop":29.0},
  {"iso3":"MDV","gdp":7.1,"pop":0.54},
  {"iso3":"MHL","gdp":0.28,"pop":0.042},
  {"iso3":"MKD","gdp":14.0,"pop":2.1},
  {"iso3":"MLI","gdp":20.0,"pop":22.0},
  {"iso3":"MLT","gdp":20.0,"pop":0.54},
  {"iso3":"MMR","gdp":66.0,"pop":54.0},
  {"iso3":"MNE","gdp":7.2,"pop":0.62},
  {"iso3":"MNG","gdp":20.0,"pop":3.4},
  {"iso3":"MNP","gdp":1.2,"pop":0.05},
  {"iso3":"MOZ","gdp":20.0,"pop":32.0},
  {"iso3":"MRT","gdp":11.0,"pop":4.7},
  {"iso3":"MSR","gdp":0.07,"pop":0.005},
  {"iso3":"MTQ","gdp":11.0,"pop":0.36},
  {"iso3":"MUS","gdp":14.0,"pop":1.3},
  {"iso3":"MWI","gdp":13.0,"pop":20.0},
  {"iso3":"MYS","gdp":430.0,"pop":33.0},
  {"iso3":"NAM","gdp":12.0,"pop":2.6},
  {"iso3":"NCL","gdp":10.0,"pop":0.27},
  {"iso3":"NER","gdp":17.0,"pop":25.0},
  {"iso3":"NGA","gdp":362.0,"pop":220.0},
  {"iso3":"NIC","gdp":16.0,"pop":6.7},
  {"iso3":"NIU","gdp":0.05,"pop":0.0017},
  {"iso3":"NLD","gdp":1117.0,"pop":17.9},
  {"iso3":"NOR","gdp":547.0,"pop":5.5},
  {"iso3":"NPL","gdp":43.0,"pop":30.0},
  {"iso3":"NRU","gdp":0.15,"pop":0.011},
  {"iso3":"NZL","gdp":247.0,"pop":5.1},
  {"iso3":"OMN","gdp":108.0,"pop":4.7},
  {"iso3":"PAK","gdp":350.0,"pop":230.0},
  {"iso3":"PAN","gdp":78.0,"pop":4.4},
  {"iso3":"PER","gdp":247.0,"pop":33.0},
  {"iso3":"PHL","gdp":437.0,"pop":115.0},
  {"iso3":"PLW","gdp":0.31,"pop":0.018},
  {"iso3":"PNG","gdp":30.0,"pop":10.0},
  {"iso3":"POL","gdp":842.0,"pop":37.0},
  {"iso3":"PRK","gdp":18.0,"pop":26.0},
  {"iso3":"PRI","gdp":117.0,"pop":3.2},
  {"iso3":"PRY","gdp":44.0,"pop":7.4},
  {"iso3":"PSE","gdp":18.0,"pop":5.4},
  {"iso3":"PRT","gdp":287.0,"pop":10.3},
  {"iso3":"PYF","gdp":6.0,"pop":0.28},
  {"iso3":"QAT","gdp":215.0,"pop":2.9},
  {"iso3":"ROU","gdp":362.0,"pop":19.0},
  {"iso3":"RUS","gdp":2077.0,"pop":144.0},
  {"iso3":"RWA","gdp":14.0,"pop":14.0},
  {"iso3":"SAU","gdp":1108.0,"pop":37.0},
  {"iso3":"SDN","gdp":43.0,"pop":47.0},
  {"iso3":"SEN","gdp":31.0,"pop":17.0},
  {"iso3":"SGP","gdp":497.0,"pop":5.9},
  {"iso3":"SLB","gdp":1.7,"pop":0.74},
  {"iso3":"SLE","gdp":5.0,"pop":8.4},
  {"iso3":"SLV","gdp":32.0,"pop":6.5},
  {"iso3":"SMR","gdp":2.1,"pop":0.034},
  {"iso3":"SOM","gdp":10.0,"pop":18.0},
  {"iso3":"SRB","gdp":77.0,"pop":6.8},
  {"iso3":"SSD","gdp":4.0,"pop":11.0},
  {"iso3":"STP","gdp":0.6,"pop":0.23},
  {"iso3":"SUR","gdp":4.0,"pop":0.6},
  {"iso3":"SVK","gdp":126.0,"pop":5.5},
  {"iso3":"SVN","gdp":65.0,"pop":2.1},
  {"iso3":"SWE","gdp":599.0,"pop":10.5},
  {"iso3":"SWZ","gdp":4.5,"pop":1.2},
  {"iso3":"SXM","gdp":1.2,"pop":0.04},
  {"iso3":"SYC","gdp":2.2,"pop":0.1},
  {"iso3":"SYR","gdp":21.0,"pop":21.0},
  {"iso3":"TCA","gdp":1.0,"pop":0.045},
  {"iso3":"TCD","gdp":12.0,"pop":17.0},
  {"iso3":"TGO","gdp":9.7,"pop":8.9},
  {"iso3":"THA","gdp":514.0,"pop":70.0},
  {"iso3":"TJK","gdp":12.0,"pop":10.0},
  {"iso3":"TKM","gdp":56.0,"pop":6.2},
  {"iso3":"TLS","gdp":3.8,"pop":1.4},
  {"iso3":"TON","gdp":0.5,"pop":0.1},
  {"iso3":"TTO","gdp":27.0,"pop":1.5},
  {"iso3":"TUN","gdp":51.0,"pop":12.0},
  {"iso3":"TUR","gdp":1344.0,"pop":85.0},
  {"iso3":"TUV","gdp":0.06,"pop":0.011},
  {"iso3":"TWN","gdp":791.0,"pop":23.0},
  {"iso3":"TZA","gdp":79.0,"pop":63.0},
  {"iso3":"UGA","gdp":51.0,"pop":47.0},
  {"iso3":"UKR","gdp":178.0,"pop":41.0},
  {"iso3":"URY","gdp":77.0,"pop":3.5},
  {"iso3":"USA","gdp":29000.0,"pop":335.0},
  {"iso3":"UZB","gdp":100.0,"pop":36.0},
  {"iso3":"VAT","gdp":0.5,"pop":0.0008},
  {"iso3":"VCT","gdp":1.2,"pop":0.11},
  {"iso3":"VEN","gdp":97.0,"pop":30.0},
  {"iso3":"VGB","gdp":1.6,"pop":0.03},
  {"iso3":"VIR","gdp":4.0,"pop":0.1},
  {"iso3":"VNM","gdp":430.0,"pop":98.0},
  {"iso3":"VUT","gdp":1.1,"pop":0.35},
  {"iso3":"WLF","gdp":0.2,"pop":0.011},
  {"iso3":"WSM","gdp":0.9,"pop":0.22},
  {"iso3":"XKX","gdp":10.0,"pop":1.8},
  {"iso3":"YEM","gdp":21.0,"pop":34.0},
  {"iso3":"ZAF","gdp":390.0,"pop":60.0},
  {"iso3":"ZMB","gdp":28.0,"pop":19.0},
  {"iso3":"ZWE","gdp":26.0,"pop":16.0}
]
```

- [ ] **Step 2: Commit**

```
git add Assets/Resources/Data/countries.json
git commit -m "data: add countries.json with 2024 IMF GDP and population for all 195 nations"
```

---

## Task 3: NationRuntime + NationDataRegistry

### Files:
- Create: `Assets/Scripts/World/NationRuntime.cs`
- Create: `Assets/Scripts/World/NationDataRegistry.cs`
- Create: `Assets/Tests/EditMode/NationEconomyTests.cs`

- [ ] **Step 1: Write NationRuntime.cs**

```csharp
// Per-nation mutable simulation state. Plain C# class — no MonoBehaviour.
[System.Serializable]
public class NationRuntime
{
    public string iso3;
    public byte   countryIdx;       // WorldRegionMapper alphabetical index

    // Seeded from JSON
    public float gdpBillions;       // nominal GDP, grows each month
    public float populationM;       // population in millions

    // Derived at registry load
    public float techLevel;         // 0–100, seeded from GDP/capita rank

    // Simulation state
    public float treasury;          // accumulated unspent Space R&D budget (billions)
    public float accumulatedMonths; // deltaTime fraction accumulator

    // Launch site state
    public int   launchSitesOwned;
    public int   totalLaunches;
    public int   infrastructurePoints;
    public int   spaceStations;
}
```

- [ ] **Step 2: Write NationDataRegistry.cs**

```csharp
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class NationDataRegistry : MonoBehaviour
{
    public static NationDataRegistry Instance { get; private set; }

    [System.Serializable]
    struct CountryEntry { public string iso3; public float gdp; public float pop; }

    [System.Serializable]
    struct EntryList { public CountryEntry[] items; }

    NationRuntime[] _byCountryIdx; // indexed by WorldRegionMapper country index
    Dictionary<string, NationRuntime> _byIso3 = new();

    public NationRuntime[] All => _byCountryIdx;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        LoadFromResources();
    }

    void LoadFromResources()
    {
        TextAsset asset = Resources.Load<TextAsset>("Data/countries");
        if (asset == null) { Debug.LogError("[NationDataRegistry] countries.json not found in Resources/Data/"); return; }
        LoadFromJson(asset.text);
    }

    public void LoadFromJson(string json)
    {
        // JsonUtility doesn't support top-level arrays — wrap it
        string wrapped = "{\"items\":" + json + "}";
        var list = JsonUtility.FromJson<EntryList>(wrapped);

        // Determine max country index
        int maxIdx = 0;
        foreach (var e in list.items)
        {
            if (WorldRegionMapper.TryGetCountryIndex(e.iso3, out byte idx))
                if (idx > maxIdx) maxIdx = idx;
        }

        _byCountryIdx = new NationRuntime[maxIdx + 1];
        _byIso3.Clear();

        // Collect GDP/capita values to seed tech levels
        var gdpPerCapita = list.items
            .Where(e => WorldRegionMapper.TryGetCountryIndex(e.iso3, out _) && e.pop > 0)
            .Select(e => e.gdp / e.pop)
            .OrderBy(x => x)
            .ToArray();
        float maxGdpPc = gdpPerCapita.Length > 0 ? gdpPerCapita[^1] : 1f;

        foreach (var e in list.items)
        {
            if (!WorldRegionMapper.TryGetCountryIndex(e.iso3, out byte idx)) continue;

            float gdpPerCap = e.pop > 0 ? e.gdp / e.pop : 0f;
            float techSeed  = Mathf.Lerp(5f, 78f, Mathf.Sqrt(gdpPerCap / maxGdpPc));

            var nation = new NationRuntime
            {
                iso3         = e.iso3,
                countryIdx   = idx,
                gdpBillions  = e.gdp,
                populationM  = e.pop,
                techLevel    = techSeed,
                treasury     = 0f,
            };

            _byCountryIdx[idx] = nation;
            _byIso3[e.iso3]    = nation;
        }

        Debug.Log($"[NationDataRegistry] Loaded {_byIso3.Count} nations.");
    }

    // Returns null if idx out of range or not loaded
    public NationRuntime GetByCountryIndex(int idx)
        => (idx >= 0 && idx < _byCountryIdx.Length) ? _byCountryIdx[idx] : null;

    public NationRuntime GetByIso3(string iso3)
        => _byIso3.TryGetValue(iso3, out var n) ? n : null;
}
```

- [ ] **Step 3: Write failing tests**

Create `Assets/Tests/EditMode/NationEconomyTests.cs`:

```csharp
using NUnit.Framework;

public class NationEconomyTests
{
    const string MinJson = @"[
      {""iso3"":""USA"",""gdp"":29000.0,""pop"":335.0},
      {""iso3"":""CAN"",""gdp"":2272.0,""pop"":38.0},
      {""iso3"":""CHN"",""gdp"":18500.0,""pop"":1410.0}
    ]";

    NationDataRegistry CreateRegistry()
    {
        var go  = new UnityEngine.GameObject();
        var reg = go.AddComponent<NationDataRegistry>();
        reg.LoadFromJson(MinJson);
        return reg;
    }

    [Test]
    public void GetByIso3_ReturnsCorrectGdp()
    {
        var reg = CreateRegistry();
        var usa = reg.GetByIso3("USA");
        Assert.IsNotNull(usa);
        Assert.AreEqual(29000f, usa.gdpBillions, 0.1f);
    }

    [Test]
    public void GetByCountryIndex_ReturnsNation()
    {
        var reg = CreateRegistry();
        WorldRegionMapper.TryGetCountryIndex("USA", out byte idx);
        var nation = reg.GetByCountryIndex(idx);
        Assert.IsNotNull(nation);
        Assert.AreEqual("USA", nation.iso3);
    }

    [Test]
    public void TechLevel_HigherGdpPerCapita_HigherTech()
    {
        var reg = CreateRegistry();
        var usa = reg.GetByIso3("USA");
        var chn = reg.GetByIso3("CHN");
        Assert.IsNotNull(usa);
        Assert.IsNotNull(chn);
        // USA GDP/capita ~86B/M, CHN ~13B/M — USA should have higher tech seed
        Assert.Greater(usa.techLevel, chn.techLevel);
    }

    [Test]
    public void GetByIso3_Unknown_ReturnsNull()
    {
        var reg = CreateRegistry();
        Assert.IsNull(reg.GetByIso3("ZZZ"));
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var go in UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(
            UnityEngine.FindObjectsSortMode.None))
            UnityEngine.Object.DestroyImmediate(go);
    }
}
```

- [ ] **Step 4: Run tests — expect FAIL (NationDataRegistry not compiled yet)**

In Unity: Window → General → Test Runner → EditMode → Run All.  
Expected: red — `NationDataRegistry` doesn't exist yet.

- [ ] **Step 5: Confirm scripts compile in Unity, run tests — expect PASS**

After writing both scripts, confirm Unity compiles. Run tests again.  
Expected: all 4 tests green.

- [ ] **Step 6: Commit**

```
git add Assets/Scripts/World/NationRuntime.cs Assets/Scripts/World/NationDataRegistry.cs Assets/Tests/EditMode/NationEconomyTests.cs
git commit -m "feat: add NationRuntime and NationDataRegistry with tech seeding and tests"
```

---

## Task 4: NationEconomySystem

### Files:
- Create: `Assets/Scripts/World/NationEconomySystem.cs`
- Modify: `Assets/Scripts/World/WorldSimulation.cs`

- [ ] **Step 1: Write NationEconomySystem.cs**

```csharp
using UnityEngine;

// Drives per-nation economy each frame. Call Tick(deltaTime) from WorldSimulation.Update.
// Economy accumulates continuously — no lump-sum monthly payouts.
public class NationEconomySystem : MonoBehaviour
{
    // GDP allocation fractions per ContactStage (index = (int)stage - 1, stages 1-8)
    static readonly float[] s_spaceRdFrac  = { 0.22f, 0.26f, 0.28f, 0.30f, 0.28f, 0.38f, 0.20f, 0.10f };
    static readonly float[] s_fearDragFrac = { 0.00f, 0.02f, 0.07f, 0.10f, 0.14f, 0.15f, 0.30f, 0.40f };
    static readonly float[] s_civFrac      = { 0.70f, 0.62f, 0.50f, 0.42f, 0.30f, 0.22f, 0.15f, 0.10f };

    // Population growth per month by stage
    static readonly float[] s_popGrowthPerMonth = { 0.00070f, 0.00060f, 0.00030f, 0.00020f,
                                                     0.00010f, 0.00000f,-0.00010f,-0.00020f };

    // Tech cost per point scales exponentially with current tech
    const float TechBaseCost = 0.5f;   // billions per tech point at tech=0
    const float TechCostExp  = 1.055f; // multiplier per existing tech level

    // Seconds per game-month (must match WorldSimulation.SecsPerSimMonth = 60f)
    const float SecsPerMonth = 60f;

    ContactStageManager _stages;

    void Start() => _stages = ContactStageManager.Instance;

    public void Tick(float deltaTime, float timeWarpFactor)
    {
        if (NationDataRegistry.Instance == null) return;

        float simDt    = deltaTime * timeWarpFactor;
        int   stageIdx = _stages != null ? Mathf.Clamp((int)_stages.CurrentStage - 1, 0, 7) : 0;

        float spaceRdFrac  = s_spaceRdFrac[stageIdx];
        float fearDragFrac = s_fearDragFrac[stageIdx];
        float popGrowth    = s_popGrowthPerMonth[stageIdx];

        var nations = NationDataRegistry.Instance.All;
        foreach (var nation in nations)
        {
            if (nation == null) continue;
            TickNation(nation, simDt, spaceRdFrac, fearDragFrac, popGrowth);
        }
    }

    void TickNation(NationRuntime n, float simDt,
                    float spaceRdFrac, float fearDragFrac, float popGrowth)
    {
        float monthFraction = simDt / SecsPerMonth;

        // Effective GDP after fear drag
        float effectiveGdp = n.gdpBillions * (1f - fearDragFrac);

        // Treasury drip: Space R&D budget flows in continuously
        float spaceRdMonthly = effectiveGdp * spaceRdFrac / 12f;
        n.treasury += spaceRdMonthly * monthFraction;

        // Accumulate for monthly strategic tick
        n.accumulatedMonths += monthFraction;
        if (n.accumulatedMonths >= 1f)
        {
            RunMonthlyTick(n, popGrowth, spaceRdFrac, fearDragFrac);
            n.accumulatedMonths -= 1f;
        }
    }

    void RunMonthlyTick(NationRuntime n, float popGrowth,
                        float spaceRdFrac, float fearDragFrac)
    {
        // Population growth
        n.populationM *= (1f + popGrowth);

        // GDP growth: driven by civilian + space fractions × tech multiplier
        float effectiveGdp  = n.gdpBillions * (1f - fearDragFrac);
        float civFrac       = s_civFrac[Mathf.Clamp(
            _stages != null ? (int)_stages.CurrentStage - 1 : 0, 0, 7)];
        float gdpGrowthRate = 0.003f + (n.techLevel / 100f) * 0.007f;
        n.gdpBillions      *= 1f + gdpGrowthRate * (civFrac + spaceRdFrac);

        // Tech advancement — diminishing returns
        float spaceRdBudget = effectiveGdp * spaceRdFrac / 12f;
        float costPerPoint  = TechBaseCost * Mathf.Pow(TechCostExp, n.techLevel);
        if (costPerPoint > 0f)
            n.techLevel = Mathf.Min(100f, n.techLevel + spaceRdBudget / costPerPoint);
    }
}
```

- [ ] **Step 2: Add economy tests to NationEconomyTests.cs**

Append these test methods inside the `NationEconomyTests` class:

```csharp
    [Test]
    public void Treasury_IncreasesEachTick_AtStage1()
    {
        var reg    = CreateRegistry();
        var usa    = reg.GetByIso3("USA");
        float before = usa.treasury;

        // Simulate 1 game-second of deltaTime at 1x warp, Stage 1 (spaceRdFrac=0.22)
        float simDt       = 1f;
        float spaceRdFrac = 0.22f;
        float fearDrag    = 0.00f;
        float monthFrac   = simDt / 60f;
        float effectiveGdp = usa.gdpBillions * (1f - fearDrag);
        usa.treasury += (effectiveGdp * spaceRdFrac / 12f) * monthFrac;

        Assert.Greater(usa.treasury, before);
    }

    [Test]
    public void TechLevel_NeverExceeds100()
    {
        var reg = CreateRegistry();
        var usa = reg.GetByIso3("USA");
        usa.techLevel = 99.9f;
        // Force many months of ticks — tech should cap at 100
        float spaceRdFrac = 0.38f; // Stage 6 max
        float fearDrag    = 0.00f;
        for (int i = 0; i < 1000; i++)
        {
            float effectiveGdp = usa.gdpBillions * (1f - fearDrag);
            float spaceRdBudget = effectiveGdp * spaceRdFrac / 12f;
            float costPerPoint  = 0.5f * Mathf.Pow(1.055f, usa.techLevel);
            usa.techLevel = Mathf.Min(100f, usa.techLevel + spaceRdBudget / costPerPoint);
        }
        Assert.LessOrEqual(usa.techLevel, 100f);
    }

    [Test]
    public void Population_DecreasesAtCollapse()
    {
        var reg = CreateRegistry();
        var usa = reg.GetByIso3("USA");
        float before = usa.populationM;
        // Stage 8 = Collapse → popGrowth = -0.0002f
        usa.populationM *= (1f + (-0.00020f));
        Assert.Less(usa.populationM, before);
    }
```

- [ ] **Step 3: Run tests — expect PASS**

All 7 tests should pass (the 3 above are pure math, no MonoBehaviour dependency).

- [ ] **Step 4: Wire NationEconomySystem into WorldSimulation**

In `Assets/Scripts/World/WorldSimulation.cs`, add the field and wire it up:

Add to the field declarations:
```csharp
NationEconomySystem _nationEcon;
```

Add to `Start()`:
```csharp
_nationEcon = GetComponent<NationEconomySystem>();
```

In `Update()`, after the existing `_dayAccum` block, add:
```csharp
_nationEcon?.Tick(Time.deltaTime, _timeWarpFactor);
```

- [ ] **Step 5: Commit**

```
git add Assets/Scripts/World/NationEconomySystem.cs Assets/Scripts/World/WorldSimulation.cs Assets/Tests/EditMode/NationEconomyTests.cs
git commit -m "feat: add NationEconomySystem with continuous GDP accumulation and tech advancement"
```

---

## Task 5: FactionTextureRenderer — Selected Nation Highlight

### Files:
- Modify: `Assets/Scripts/World/FactionTextureRenderer.cs`

- [ ] **Step 1: Add selection state and SelectCountry method**

In `FactionTextureRenderer.cs`, add these members after the `_borderTexture` field declaration:

```csharp
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
```

- [ ] **Step 2: Modify Recolor() to highlight the selected nation**

In `Recolor()`, find the loop that builds `countryColors` and replace it:

```csharp
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
```

- [ ] **Step 3: Verify Unity compiles, no errors**

- [ ] **Step 4: Commit**

```
git add Assets/Scripts/World/FactionTextureRenderer.cs
git commit -m "feat: add nation selection highlight — selected country renders fully opaque"
```

---

## Task 6: NationSelectionSystem

### Files:
- Create: `Assets/Scripts/World/NationSelectionSystem.cs`

- [ ] **Step 1: Add SphereCollider to Earth in scene**

In Unity scene hierarchy, select the `Earth` GameObject. In the Inspector, Add Component → Physics → Sphere Collider. Set `Is Trigger = false`, `Radius = 1` (local space — with scale=10 gives world-radius=10). The Atmosphere child inherits scale but has its own mesh — the SphereCollider on Earth is sufficient.

- [ ] **Step 2: Write NationSelectionSystem.cs**

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

// Attach to a persistent GameObject (e.g. WorldSimulation).
// Raycasts against Earth on left-click → reads country index from region_map → selects nation.
public class NationSelectionSystem : MonoBehaviour
{
    [SerializeField] Transform           _earthTransform;
    [SerializeField] FactionTextureRenderer _factionRenderer;

    public event System.Action<NationRuntime> OnNationSelected;
    public event System.Action               OnNationDeselected;

    const int MapW = 2048;
    const int MapH = 1024;

    Camera _cam;

    void Start()
    {
        _cam = Camera.main;
        if (_earthTransform == null)
            _earthTransform = GameObject.Find("Earth")?.transform;
        if (_factionRenderer == null && _earthTransform != null)
            _factionRenderer = _earthTransform.GetComponentInChildren<FactionTextureRenderer>();
    }

    void Update()
    {
        if (Mouse.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
            HandleClick();

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            Deselect();
    }

    void HandleClick()
    {
        Vector2 screenPos = Mouse.current.position.ReadValue();
        Ray ray = _cam.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));

        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        // Check hit is on Earth (not atmosphere or Moon)
        if (_earthTransform != null && hit.collider.transform != _earthTransform) return;

        // Convert world hit point → lat/lon using GeoUtils convention:
        // x = -cos(lat)*cos(lon), y = sin(lat), z = -cos(lat)*sin(lon)
        Vector3 local = _earthTransform.InverseTransformPoint(hit.point).normalized;
        float lat = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
        float lon = Mathf.Atan2(-local.z, -local.x) * Mathf.Rad2Deg;

        int px = Mathf.Clamp(Mathf.FloorToInt((lon + 180f) / 360f * MapW), 0, MapW - 1);
        int py = Mathf.Clamp(Mathf.FloorToInt((lat +  90f) / 180f * MapH), 0, MapH - 1);

        if (_factionRenderer == null) return;
        // Access the region id map via internal field — expose via property
        byte countryIdx = _factionRenderer.GetCountryAtPixel(px, py);

        if (countryIdx == 255)
        {
            Deselect();
            return;
        }

        _factionRenderer.SelectCountry(countryIdx);

        var nation = NationDataRegistry.Instance?.GetByCountryIndex(countryIdx);
        if (nation != null) OnNationSelected?.Invoke(nation);
        else Deselect();
    }

    void Deselect()
    {
        _factionRenderer?.ClearSelection();
        OnNationDeselected?.Invoke();
    }
}
```

- [ ] **Step 3: Expose GetCountryAtPixel on FactionTextureRenderer**

In `FactionTextureRenderer.cs`, add this method (after `ClearSelection`):

```csharp
    public byte GetCountryAtPixel(int px, int py)
    {
        if (_regionIdMap == null) return 255;
        int idx = py * _texWidth + px;
        if (idx < 0 || idx >= _regionIdMap.Length) return 255;
        return _regionIdMap[idx];
    }
```

- [ ] **Step 4: Commit**

```
git add Assets/Scripts/World/NationSelectionSystem.cs Assets/Scripts/World/FactionTextureRenderer.cs
git commit -m "feat: add NationSelectionSystem — globe raycast selects country, highlights on globe"
```

---

## Task 7: NationStatPanel

### Files:
- Create: `Assets/Scripts/World/NationStatPanel.cs`

- [ ] **Step 1: Write NationStatPanel.cs**

This script creates its own Canvas and UI elements in Awake — no prefab setup needed.

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Creates a stat panel in the top-left corner. Attach to any persistent GameObject.
// Assign the NationSelectionSystem reference in Inspector.
public class NationStatPanel : MonoBehaviour
{
    [SerializeField] NationSelectionSystem _selectionSystem;

    Canvas         _canvas;
    GameObject     _panel;
    TextMeshProUGUI _text;

    void Awake()
    {
        BuildUI();
    }

    void Start()
    {
        if (_selectionSystem == null)
            _selectionSystem = FindFirstObjectByType<NationSelectionSystem>();

        if (_selectionSystem != null)
        {
            _selectionSystem.OnNationSelected   += Show;
            _selectionSystem.OnNationDeselected += Hide;
        }

        Hide();
    }

    void OnDestroy()
    {
        if (_selectionSystem != null)
        {
            _selectionSystem.OnNationSelected   -= Show;
            _selectionSystem.OnNationDeselected -= Hide;
        }
    }

    void BuildUI()
    {
        // Canvas
        var canvasGo = new GameObject("NationStatCanvas");
        canvasGo.transform.SetParent(transform);
        _canvas                  = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode       = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder     = 10;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        // Panel background
        _panel = new GameObject("StatPanel");
        _panel.transform.SetParent(canvasGo.transform, false);
        var panelRect = _panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 1);
        panelRect.anchorMax = new Vector2(0, 1);
        panelRect.pivot     = new Vector2(0, 1);
        panelRect.anchoredPosition = new Vector2(16, -16);
        panelRect.sizeDelta        = new Vector2(280, 340);

        var bg = _panel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.72f);

        // Text
        var textGo = new GameObject("StatText");
        textGo.transform.SetParent(_panel.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin        = Vector2.zero;
        textRect.anchorMax        = Vector2.one;
        textRect.offsetMin        = new Vector2(12, 12);
        textRect.offsetMax        = new Vector2(-12, -12);
        _text                     = textGo.AddComponent<TextMeshProUGUI>();
        _text.fontSize            = 13f;
        _text.color               = Color.white;
        _text.alignment           = TextAlignmentOptions.TopLeft;
        _text.enableWordWrapping  = false;
    }

    public void Show(NationRuntime nation)
    {
        if (nation == null) { Hide(); return; }

        var regionByte = WorldRegionMapper.GetRegionForCountry(nation.countryIdx);
        string regionName = regionByte < 14 ? GetRegionDisplayName(regionByte) : "Unknown";

        string stationStr  = nation.spaceStations > 0 ? $"{nation.spaceStations}" : "—";
        string sitesStr    = nation.launchSitesOwned > 0 ? $"{nation.launchSitesOwned} owned" : "renting";
        float  techPct     = nation.techLevel;
        int    barFilled   = Mathf.RoundToInt(techPct / 10f);
        string techBar     = new string('█', barFilled) + new string('░', 10 - barFilled);

        _text.text =
            $"<b>{nation.iso3}</b>   [{regionName}]\n" +
            $"─────────────────────\n" +
            $"GDP        ${FormatBillions(nation.gdpBillions)}\n" +
            $"Population {nation.populationM:F1}M\n" +
            $"Tech  {techBar} {techPct:F0}\n" +
            $"Treasury  ${FormatBillions(nation.treasury)}\n" +
            $"─────────────────────\n" +
            $"Launch sites  {sitesStr}\n" +
            $"Total launches  {nation.totalLaunches}\n" +
            $"Infra points  {nation.infrastructurePoints}\n" +
            $"Space stations  {stationStr}\n" +
            $"─────────────────────\n" +
            $"[ESC or click to deselect]";

        _panel.SetActive(true);
    }

    public void Hide()
    {
        if (_panel != null) _panel.SetActive(false);
    }

    static string FormatBillions(float b)
    {
        if (b >= 1000f) return $"{b / 1000f:F1}T";
        return $"{b:F0}B";
    }

    static string GetRegionDisplayName(byte idx) => idx switch
    {
        0  => "N.America",
        1  => "C.America",
        2  => "S.America",
        3  => "W.Europe",
        4  => "E.Europe",
        5  => "Russia",
        6  => "Middle East",
        7  => "N.Africa",
        8  => "S.Africa",
        9  => "E.Asia",
        10 => "S.Asia",
        11 => "SE.Asia",
        12 => "C.Asia",
        13 => "Oceania",
        _  => "Unknown"
    };
}
```

- [ ] **Step 2: Commit**

```
git add Assets/Scripts/World/NationStatPanel.cs
git commit -m "feat: add NationStatPanel — top-left stat overlay on nation select"
```

---

## Task 8: LaunchSiteSystem

### Files:
- Create: `Assets/Scripts/World/LaunchSiteSystem.cs`

- [ ] **Step 1: Write LaunchSiteSystem.cs**

```csharp
using UnityEngine;

// Runs per monthly strategic tick. Decides per-nation whether to build a launch site
// or queue a launch payload. Called from NationEconomySystem.RunMonthlyTick.
public static class LaunchSiteSystem
{
    const float BuildCost      = 50f;   // billions to build own site
    const float RentCostPerLaunch = 2f; // billions per rental launch
    const float ProbeCost      = 3f;   // billions
    const float CrewedCost     = 10f;  // billions
    const float InfraCost      = 8f;   // billions per infrastructure point
    const int   BuildTechMin   = 30;
    const int   InfraPerStation = 10;

    public static void ProcessMonthlyDecision(NationRuntime n, int stageIndex)
    {
        if (stageIndex < 1) return; // stage 1 = Undetected — only major economies launch

        // Build own launch site if affordable and eligible
        if (n.launchSitesOwned == 0
            && n.techLevel >= BuildTechMin
            && n.treasury >= BuildCost
            && n.gdpBillions >= 200f)
        {
            n.treasury     -= BuildCost;
            n.launchSitesOwned++;
            Debug.Log($"[LaunchSite] {n.iso3} built a launch site. Treasury: ${n.treasury:F0}B");
        }

        // Queue a launch if the nation has access (owned or can rent) and funds
        bool canLaunch  = n.launchSitesOwned > 0;
        bool canRent    = n.treasury >= RentCostPerLaunch && n.gdpBillions >= 50f;
        if (!canLaunch && !canRent) return;

        float launchCost = canLaunch ? ProbeCost : ProbeCost + RentCostPerLaunch;

        if (n.treasury < launchCost) return;

        // Choose payload type
        if (n.techLevel >= 50f && n.treasury >= (canLaunch ? CrewedCost : CrewedCost + RentCostPerLaunch))
        {
            float cost = canLaunch ? CrewedCost : CrewedCost + RentCostPerLaunch;
            n.treasury -= cost;
            n.totalLaunches++;
            Debug.Log($"[LaunchSite] {n.iso3} — crewed mission launched.");
        }
        else if (n.treasury >= (canLaunch ? InfraCost : InfraCost + RentCostPerLaunch))
        {
            float cost = canLaunch ? InfraCost : InfraCost + RentCostPerLaunch;
            n.treasury -= cost;
            n.totalLaunches++;
            n.infrastructurePoints++;
            if (n.infrastructurePoints >= InfraPerStation)
            {
                n.infrastructurePoints -= InfraPerStation;
                n.spaceStations++;
                Debug.Log($"[LaunchSite] {n.iso3} completed a space station! Total: {n.spaceStations}");
            }
        }
        else
        {
            n.treasury -= launchCost;
            n.totalLaunches++;
            Debug.Log($"[LaunchSite] {n.iso3} — probe launched.");
        }
    }
}
```

- [ ] **Step 2: Wire LaunchSiteSystem into NationEconomySystem.RunMonthlyTick**

In `NationEconomySystem.cs`, at the end of `RunMonthlyTick`, add:

```csharp
        int stageIdx = _stages != null ? (int)_stages.CurrentStage - 1 : 0;
        LaunchSiteSystem.ProcessMonthlyDecision(n, stageIdx);
```

- [ ] **Step 3: Commit**

```
git add Assets/Scripts/World/LaunchSiteSystem.cs Assets/Scripts/World/NationEconomySystem.cs
git commit -m "feat: add LaunchSiteSystem — nations build sites, queue launches, accumulate space stations"
```

---

## Task 9: Scene Wiring

### In Unity Editor (not code changes)

- [ ] **Step 1: Add NationDataRegistry to WorldSimulation GameObject**

Select `WorldSimulation` in scene hierarchy → Add Component → `NationDataRegistry`.

- [ ] **Step 2: Add NationEconomySystem to WorldSimulation GameObject**

Select `WorldSimulation` → Add Component → `NationEconomySystem`.

- [ ] **Step 3: Add NationSelectionSystem to WorldSimulation GameObject**

Select `WorldSimulation` → Add Component → `NationSelectionSystem`.  
In the Inspector, assign:
- `Earth Transform` → drag the `Earth` GameObject
- `Faction Renderer` → drag `Earth/GlobeOverlays` (or whichever child has `FactionTextureRenderer`)

- [ ] **Step 4: Add NationStatPanel to WorldSimulation GameObject**

Select `WorldSimulation` → Add Component → `NationStatPanel`.  
In Inspector, assign `Selection System` → the `NationSelectionSystem` on the same GameObject.

- [ ] **Step 5: Verify CityDotRenderer is removed from scene**

Select `Earth/GlobeOverlays`. Confirm no `CityDotRenderer` component remains.

- [ ] **Step 6: Hit Play — smoke test**

1. Press Play. Confirm no Console errors.
2. Confirm globe renders faction colours with country-level borders.
3. Click on a nation. Confirm:
   - That country brightens (full opacity).
   - Stat panel appears top-left with ISO3 code, GDP, population, tech bar.
4. Press ESC. Panel hides, nation returns to normal opacity.
5. Click ocean. Panel hides.
6. Let sim run 60 real seconds. Confirm year ticks forward in WorldSimulation HUD.
7. Open Console — confirm `[NationDataRegistry] Loaded 195 nations.` log.

- [ ] **Step 7: Final commit**

```
git add -A
git commit -m "feat: wire nation economy scene — NationDataRegistry, EconomySystem, SelectionSystem, StatPanel connected"
```
