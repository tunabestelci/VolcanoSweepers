using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Proje acilinca Volcano sahnesini acar ve Play her zaman bu sahneyle baslar.
[InitializeOnLoad]
public static class AutoOpenScene
{
    const string ScenePath = "Assets/_Project/Scenes/Volcano.unity";

    static AutoOpenScene()
    {
        EditorApplication.delayCall += Setup;
    }

    static void Setup()
    {
        if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        if (scene == null) return;
        EditorSceneManager.playModeStartScene = scene;

        var active = SceneManager.GetActiveScene();
        bool untitled = string.IsNullOrEmpty(active.path);
        bool isSample = active.path.EndsWith("SampleScene.unity");
        if ((untitled || isSample) && !active.isDirty)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }
}
