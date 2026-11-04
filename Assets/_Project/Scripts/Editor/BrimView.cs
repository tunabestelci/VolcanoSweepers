using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Volkanin genel gorunumunu (editor, oyun calistirmadan) PNG olarak kaydeder.
// batch: -executeMethod BrimView.RunBatch   (grafik cihazi gerekir)
public static class BrimView
{
    public static void RunBatch()
    {
        Run();
        EditorApplication.Exit(0);
    }

    [MenuItem("VolcanoSweepers/Render Brim Views")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Volcano.unity", OpenSceneMode.Single);
        var field = Object.FindAnyObjectByType<LavaField>();
        typeof(LavaField).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(field, null);

        var player = GameObject.Find("Player");
        Vector3 pad = player.transform.position;
        string dir = "Library/BrimViews";
        Directory.CreateDirectory(dir);

        var camGo = new GameObject("BrimCam");
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 1500f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.06f, 0.04f);

        Shot(cam, $"{dir}/1_overview.png", new Vector3(0f, 190f, -30f), new Vector3(0f, 0f, 0f), 1280, 720);
        Shot(cam, $"{dir}/2_from_pad.png", pad + new Vector3(0f, 1.65f, 0f), pad + new Vector3(60f, -4f, 18f), 1280, 720);
        Shot(cam, $"{dir}/3_outside.png", new Vector3(-260f, 60f, -120f), new Vector3(0f, -10f, 0f), 1280, 720);
        Shot(cam, $"{dir}/4_pad_close.png", pad + new Vector3(-8f, 6f, -8f), pad + new Vector3(4f, 1f, 3f), 1280, 720);
        Debug.Log("[BrimView] goruntuler: " + Path.GetFullPath(dir) + " pad=" + pad);
    }

    static void Shot(Camera cam, string path, Vector3 pos, Vector3 look, int w, int h)
    {
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }
}
