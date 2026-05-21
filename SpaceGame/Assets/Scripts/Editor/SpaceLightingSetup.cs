using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[InitializeOnLoad]
static class SpaceLightingSetup
{
    static SpaceLightingSetup() => EditorApplication.delayCall += Apply;

    static void Apply()
    {
        // Zero ambient — space is pure black outside the sun
        RenderSettings.ambientMode         = AmbientMode.Flat;
        RenderSettings.ambientLight        = Color.black;
        RenderSettings.ambientIntensity    = 0f;
        RenderSettings.reflectionIntensity = 0f;

        var skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Skybox_Stars.mat");
        if (skybox != null) RenderSettings.skybox = skybox;

        SetupPostProcess();

        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
    }

    static void SetupPostProcess()
    {
        // Reuse existing volume or create one
        var go  = GameObject.Find("PostProcessVolume") ?? new GameObject("PostProcessVolume");
        var vol = go.GetComponent<Volume>() ?? go.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 1;

        // Create or reuse profile asset
        const string dir  = "Assets/Settings";
        const string path = dir + "/SpacePostProcess.asset";
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets", "Settings");

        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        vol.sharedProfile = profile;

        // ACES tonemapping — compresses HDR highlights, deep blacks in shadows
        Override<Tonemapping>(profile, t => t.mode.Override(TonemappingMode.ACES));

        // Bloom — lit Earth specular and atmosphere glow spill light realistically
        Override<Bloom>(profile, b =>
        {
            b.intensity.Override(1.2f);
            b.threshold.Override(0.85f);   // only the brightest parts bloom
            b.scatter.Override(0.35f);
        });

        // Contrast + saturation — more pop, colours punch through
        Override<ColorAdjustments>(profile, c =>
        {
            c.contrast.Override(25f);
            c.saturation.Override(20f);
        });

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
    }

    static void Override<T>(VolumeProfile p, System.Action<T> configure) where T : VolumeComponent
    {
        if (!p.TryGet<T>(out var comp)) comp = p.Add<T>();
        configure(comp);
    }
}
