using UnityEngine;

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
