using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Batch modu girisleri (Unity acik degilken komut satirindan):
//   -executeMethod VsBatch.BuildAll            : prefablari + sahneyi kurar, kaydeder
//   -executeMethod VsBatch.PlayTest -vsmode X  : sahneyi acip oynatir, BreakerTest calisir (hands|use|shop|all)
public static class VsBatch
{
    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    public static void BuildAll()
    {
        AssetDatabase.Refresh();
        bool ok = EditorApplication.ExecuteMenuItem("VolcanoSweepers/Build All");
        Debug.Log("[VsBatch] Build All menu: " + ok);
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        Debug.Log("[VsBatch] BuildAll bitti");
        EditorApplication.Exit(ok ? 0 : 2);
    }

    public static void PlayTest()
    {
        AssetDatabase.Refresh();
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Volcano.unity");
        EditorPrefs.SetString("VS_BREAKER_TEST", Arg("-vsmode", "all"));
        Debug.Log("[VsBatch] oynat: " + Arg("-vsmode", "all"));
        EditorApplication.EnterPlaymode();
    }
}
