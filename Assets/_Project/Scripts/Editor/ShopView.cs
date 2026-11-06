using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Dukkan ve lav canavari goruntulerini PNG olarak kaydeder (batch, grafik cihazi gerekir).
public static class ShopView
{
    public static void RunBatch()
    {
        VolcanoSweepersSetup.BuildAll();
        Render();
        EditorApplication.Exit(0);
    }

    public static void Render()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Volcano.unity", OpenSceneMode.Single);
        var shop = GameObject.Find("DragonShop");
        var mon = GameObject.Find("LavaMonster");
        string dir = "Library/ShopViews";
        Directory.CreateDirectory(dir);
        if (mon != null) { var a = mon.GetComponent<Animator>(); if (a != null) { a.Update(0f); a.Update(1.2f); } }
        var camGo = new GameObject("ShopCam");
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 50f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 700f;
        cam.clearFlags = CameraClearFlags.Skybox;
        Vector3 sp = shop.transform.position;
        Vector3 fwd = shop.transform.forward;       // tezgah yonu
        Vector3 right = shop.transform.right;
        Shot(cam, $"{dir}/1_front.png", sp + fwd * 7.5f + Vector3.up * 2.2f, sp + Vector3.up * 1.4f);
        Shot(cam, $"{dir}/2_front_close.png", sp + fwd * 3.6f + Vector3.up * 1.7f, sp + Vector3.up * 1.3f);
        Shot(cam, $"{dir}/3_side.png", sp + right * 8f + fwd * 2f + Vector3.up * 3f, sp + Vector3.up * 1.2f);
        Shot(cam, $"{dir}/4_back.png", sp - fwd * 7f + right * 2f + Vector3.up * 3f, sp + Vector3.up * 1.2f);
        Shot(cam, $"{dir}/6_top.png", sp + Vector3.up * 14f + fwd * 0.01f, sp);
        cam.nearClipPlane = 14f + 1.5f - (sp.y + 1.55f) + 0.0f;   // cati altindan kesit: tavan gorunmez
        cam.nearClipPlane = 14f - 1.5f;
        Shot(cam, $"{dir}/7_section.png", sp + Vector3.up * 14f + fwd * 0.01f, sp);
        cam.nearClipPlane = 0.1f;
        if (mon != null) Shot(cam, $"{dir}/5_monster.png", mon.transform.position + fwd * 2.3f + Vector3.up * 1.2f, mon.transform.position + Vector3.up * 0.8f);
        Debug.Log("[ShopView] " + Path.GetFullPath(dir) + " shop=" + (shop != null ? shop.transform.position.ToString() : "yok"));
    }

    static void Shot(Camera cam, string path, Vector3 pos, Vector3 look)
    {
        cam.transform.position = pos; cam.transform.LookAt(look);
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
    }
}
