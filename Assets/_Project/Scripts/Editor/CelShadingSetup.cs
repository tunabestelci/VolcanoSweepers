using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Cel-shade gecisini (VolcanoSweepers/CelPost) PC ve Mobile renderer'ina ekler. Tekrar calistirmak guvenlidir.
public static class CelShadingSetup
{
    const string MatPath = "Assets/_Project/Art/Materials/CelPost.mat";
    static readonly string[] Renderers = { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" };

    [MenuItem("VolcanoSweepers/Apply Cel Shading")]
    public static void Apply()
    {
        var shader = Shader.Find("VolcanoSweepers/CelPost");
        if (shader == null) { Debug.LogError("[Cel] CelPost shader bulunamadi"); return; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, MatPath);
        }
        mat.shader = shader;
        mat.SetFloat("_Bands", 4f);
        mat.SetFloat("_Shadow", 0.07f);
        mat.SetFloat("_Saturation", 1.15f);
        mat.SetFloat("_Soften", 1.5f);
        mat.SetFloat("_OutlineWidth", 1.6f);
        mat.SetFloat("_OutlineThreshold", 0.05f);
        mat.SetFloat("_Detail", 0.9f);
        mat.SetFloat("_NormalEdge", 0.42f);
        mat.SetFloat("_NormalEdgeStrength", 0.9f);
        mat.SetColor("_OutlineColor", new Color(0.035f, 0.015f, 0.012f, 1f));
        EditorUtility.SetDirty(mat);

        foreach (var path in Renderers)
        {
            var rd = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            if (rd == null) continue;

            FullScreenPassRendererFeature feat = null;
            foreach (var f in rd.rendererFeatures)
                if (f is FullScreenPassRendererFeature fs && fs.name == "CelShade") feat = fs;

            if (feat == null)
            {
                feat = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                feat.name = "CelShade";
                AssetDatabase.AddObjectToAsset(feat, rd);
                rd.rendererFeatures.Add(feat);
            }
            feat.passMaterial = mat;
            feat.passIndex = 0;
            feat.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
            feat.fetchColorBuffer = true;
            feat.requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Color;
            feat.SetActive(true);
            EditorUtility.SetDirty(feat);

            // SSAO cel-shade bantlarini kirletir; kapali
            foreach (var f in rd.rendererFeatures)
                if (f is ScreenSpaceAmbientOcclusion ao) { ao.SetActive(false); EditorUtility.SetDirty(ao); }

            // Ozellik eslemesini (feature map) yenile
            typeof(ScriptableRendererData).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(rd, null);
            EditorUtility.SetDirty(rd);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[Cel] cel-shade gecisi renderer'a eklendi.");
    }
}
