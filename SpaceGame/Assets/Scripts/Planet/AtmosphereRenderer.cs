using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshRenderer))]
public class AtmosphereRenderer : MonoBehaviour
{
    [Header("References")]
    public ComputeShader opticalDepthCompute;
    public Transform     planetTransform;         // Earth transform (defaults to parent)

    [Header("Geometry")]
    public float planetRadius     = 10.0f;        // Earth world-space radius (scale 10 × mesh radius 1)
    public float atmosphereRadius = 10.173f;      // planetRadius × 1.0173 (110 km atmo)

    [Header("Density")]
    public float scaleHeightR = 0.01334f;         // Rayleigh: 8.5 km → 8.5/637.1 world units
    public float scaleHeightM = 0.00188f;         // Mie: 1.2 km → 1.2/637.1 world units

    [Header("Brightness")]
    [Range(0f, 100f)] public float sunIntensity = 20f;

    const int LUT_RES = 256;

    RenderTexture _lut;
    Material      _mat;

    void Start()
    {
        _mat = GetComponent<MeshRenderer>().material;
        if (planetTransform == null)
            planetTransform = transform.parent;

#if UNITY_EDITOR
        if (opticalDepthCompute == null)
            opticalDepthCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Shaders/AtmosphereDepthLUT.compute");
#endif

        BakeLUT();
        ApplyStaticUniforms();
    }

    void BakeLUT()
    {
        if (_lut != null) _lut.Release();

        _lut = new RenderTexture(LUT_RES, LUT_RES, 0, RenderTextureFormat.RGFloat)
        {
            enableRandomWrite = true,
            filterMode        = FilterMode.Bilinear,
            wrapMode          = TextureWrapMode.Clamp,
            name              = "AtmosphereDepthLUT"
        };
        _lut.Create();

        int kernel = opticalDepthCompute.FindKernel("BakeOpticalDepth");
        opticalDepthCompute.SetTexture(kernel, "Result",         _lut);
        opticalDepthCompute.SetFloat("_PlanetRadius",            planetRadius);
        opticalDepthCompute.SetFloat("_AtmoRadius",              atmosphereRadius);
        opticalDepthCompute.SetFloat("_ScaleHeightR",            scaleHeightR);
        opticalDepthCompute.SetFloat("_ScaleHeightM",            scaleHeightM);
        opticalDepthCompute.SetInt("_LUTResolution",             LUT_RES);

        int groups = Mathf.CeilToInt(LUT_RES / 8f);
        opticalDepthCompute.Dispatch(kernel, groups, groups, 1);
    }

    void ApplyStaticUniforms()
    {
        _mat.SetFloat("_PlanetRadius",      planetRadius);
        _mat.SetFloat("_AtmoRadius",        atmosphereRadius);
        _mat.SetFloat("_DensityFalloff",    scaleHeightR);
        _mat.SetFloat("_MieFalloff",        scaleHeightM);
        _mat.SetFloat("_SunIntensity",      sunIntensity);
        _mat.SetTexture("_OpticalDepthLUT", _lut);
    }

    void Update()
    {
        Vector3 centre = planetTransform != null ? planetTransform.position : Vector3.zero;
        _mat.SetVector("_PlanetCentre", new Vector4(centre.x, centre.y, centre.z, 0f));
    }

    void OnDestroy()
    {
        if (_lut != null) { _lut.Release(); _lut = null; }
    }

    void OnValidate()
    {
        if (!Application.isPlaying || _mat == null) return;
        BakeLUT();
        ApplyStaticUniforms();
    }
}
