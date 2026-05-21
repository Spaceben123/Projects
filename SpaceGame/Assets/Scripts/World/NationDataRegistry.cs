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
        => (_byCountryIdx != null && idx >= 0 && idx < _byCountryIdx.Length) ? _byCountryIdx[idx] : null;

    public NationRuntime GetByIso3(string iso3)
        => _byIso3.TryGetValue(iso3, out var n) ? n : null;
}
