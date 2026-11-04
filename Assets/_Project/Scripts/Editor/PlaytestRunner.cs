using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Arka planda (batchmode) Volcano sahnesini Play modunda calistirir; PlaytestBot oyunu dener ve cikar.
//   Unity.exe -batchmode -projectPath <proje> -executeMethod PlaytestRunner.Run -vsbot <klasor>
public static class PlaytestRunner
{
    [MenuItem("VolcanoSweepers/Run Demo Bot")]
    public static void RunDemo()
    {
        EditorPrefs.SetString("VS_BOT_DIR", System.IO.Path.GetFullPath("Library/BotShots"));
        if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Volcano.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }
}
