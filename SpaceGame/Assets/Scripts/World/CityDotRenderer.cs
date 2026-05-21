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

public class CityDotRenderer : MonoBehaviour
{
    [SerializeField] bool  _visible  = true;
    [SerializeField] float _baseSize = 0.04f;

    [Header("Heatmap")]
    [SerializeField] int   _hmapWidth  = 1024;
    [SerializeField] int   _hmapHeight = 512;
    [SerializeField] float _hmapSigmaU = 0.012f; // gaussian radius in UV space

    public CityData[] Cities { get; private set; }

    readonly List<Transform> _dots = new List<Transform>();

    void Start()
    {
        TextAsset asset = Resources.Load<TextAsset>("Cities/cities");
        if (asset == null) { Debug.LogError("[CityDots] Missing Cities/cities.json"); return; }

        CityDataList list = JsonUtility.FromJson<CityDataList>(asset.text);
        Cities = list.cities;

        Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.color = new Color(1f, 0.9f, 0.3f);

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
            mr.material             = mat;
            mr.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;

            _dots.Add(go.transform);
        }

        BakeHeatmap();
    }

    void Update()
    {
        foreach (var d in _dots)
            if (d != null) d.gameObject.SetActive(_visible);
    }

    public void SetVisible(bool v) => _visible = v;

    void BakeHeatmap()
    {
        if (Cities == null || Cities.Length == 0) return;

        MeshRenderer earthRenderer = transform.parent?.GetComponent<MeshRenderer>();
        if (earthRenderer == null) return;

        int W = _hmapWidth, H = _hmapHeight;
        float[] heat = new float[W * H];
        float sigV = _hmapSigmaU * 0.5f; // aspect-correct sigma in V space (UV space is 2:1)
        float sigU = _hmapSigmaU;
        float r = sigU * 4f; // 4-sigma radius in U space

        foreach (var city in Cities)
        {
            float uCity = (city.lon + 180f) / 360f;
            float vCity = (city.lat +  90f) / 180f;
            float weight = city.populationM;

            int x0 = Mathf.Max(0,     Mathf.FloorToInt((uCity - r) * W));
            int x1 = Mathf.Min(W - 1, Mathf.CeilToInt ((uCity + r) * W));
            int y0 = Mathf.Max(0,     Mathf.FloorToInt((vCity - r) * H));
            int y1 = Mathf.Min(H - 1, Mathf.CeilToInt ((vCity + r) * H));

            float inv2su = 1f / (2f * sigU * sigU);
            float inv2sv = 1f / (2f * sigV * sigV);

            for (int py = y0; py <= y1; py++)
            {
                float dv = (py + 0.5f) / H - vCity;
                for (int px = x0; px <= x1; px++)
                {
                    float du = (px + 0.5f) / W - uCity;
                    float g = Mathf.Exp(-(du * du * inv2su + dv * dv * inv2sv));
                    heat[py * W + px] += g * weight;
                }
            }
        }

        // Normalise — sqrt curve boosts dim areas so mid-density cities are visible at default strength
        float maxHeat = 0f;
        for (int i = 0; i < heat.Length; i++) if (heat[i] > maxHeat) maxHeat = heat[i];
        if (maxHeat < 1e-6f) return;
        float invMax = 1f / maxHeat;

        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.wrapMode   = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color32[] pixels = new Color32[W * H];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color32((byte)(Mathf.Sqrt(heat[i] * invMax) * 255f), 0, 0, 255);
        tex.SetPixels32(pixels);
        tex.Apply(false);

        var propBlock = new MaterialPropertyBlock();
        earthRenderer.GetPropertyBlock(propBlock);
        propBlock.SetTexture("_PopulationHeatmap", tex);
        earthRenderer.SetPropertyBlock(propBlock);
        Debug.Log("[CityDots] Heatmap baked.");
    }
}
