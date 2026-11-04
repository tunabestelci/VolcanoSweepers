using System.Collections;
using System.IO;
using UnityEngine;

// Yalnizca "-vsbot <klasor>" argumaniyla calisir: oyun dongusunu otomatik dener, ekran goruntusu alir.
public class PlaytestBot : MonoBehaviour
{
    public PlayerController player;
    public PlayerTools tools;
    public LavaField field;

    string dir;

    void Awake()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-vsbot");
        if (i >= 0 && i + 1 < args.Length) dir = args[i + 1];
#if UNITY_EDITOR
        string pref = UnityEditor.EditorPrefs.GetString("VS_BOT_DIR", "");
        if (!string.IsNullOrEmpty(pref)) { dir = pref; UnityEditor.EditorPrefs.DeleteKey("VS_BOT_DIR"); }
#endif
        if (dir == null) { Destroy(this); return; }
        Directory.CreateDirectory(dir);
    }

    void Start()
    {
        if (dir != null) StartCoroutine(Run());
    }

    // Kameradan dogrudan goruntu al (editor ve oyunda calisir)
    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        var cam = player.cam;
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
        Destroy(tex);
        yield return null;
    }

    IEnumerator WaitIdle()
    {
        yield return new WaitForSeconds(0.1f);
        while (tools.Busy) yield return null;
    }

    Vector3 Surface(float x, float z)
    {
        var p = new Vector3(x, 0f, z);
        p.y = field.HeightAt(p);
        return p;
    }

    IEnumerator Run()
    {
        Debug.Log("[BOT] basladi");
        float R = field.poolRadius;
        var spawn = player.transform.position;
        yield return new WaitForSeconds(2.5f);
        player.LookAt(Surface(0f, spawn.z + 12f));
        yield return Shot("01_start");

        // Kuyudan buyuk kovaya su cek, sonra kovayi buyuk kovadan doldur
        var well = FindFirstObjectByType<WaterWell>();
        var big = FindFirstObjectByType<BigBucket>();
        Vector3 wp = well.transform.position + Vector3.up * 0.8f;
        player.Teleport(new Vector3(wp.x - 3.2f, spawn.y, wp.z - 0.5f));
        yield return new WaitForSeconds(0.3f);
        player.LookAt(wp);
        yield return new WaitForSeconds(0.2f);
        yield return Shot("01b_well");
        tools.DoPull();
        yield return new WaitForSeconds(1.3f);
        yield return Shot("02_pull");
        yield return new WaitForSeconds(1.6f);
        Debug.Log($"[BOT] buyuk kova dolu: {big.Fill:F2}");
        player.Teleport(new Vector3(big.transform.position.x - 1.4f, spawn.y, big.transform.position.z - 0.2f));
        yield return new WaitForSeconds(0.2f);
        player.LookAt(big.transform.position + Vector3.up * 0.4f);
        yield return new WaitForSeconds(0.2f);
        tools.DoBucket(false);
        yield return new WaitForSeconds(0.8f);
        yield return Shot("02_fill");
        yield return WaitIdle();
        Debug.Log($"[BOT] buyuk kova sonra: {big.Fill:F2}");

        // Lav gol kenarina yuru, sonra dok (basili tutma simulasyonu)
        Vector3 shore = new Vector3(0f, 0f, -(R - 9f));
        shore.y = field.HeightAt(shore);
        player.Teleport(shore);
        yield return new WaitForSeconds(0.6f);
        Vector3 target = Surface(0f, -(R - 14f));
        player.LookAt(target);
        yield return Shot("03_shore");
        tools.botHoldPour = true;
        tools.DoBucket(false);
        yield return new WaitForSeconds(1.4f);
        yield return Shot("04_pour");
        yield return new WaitForSeconds(1.0f);
        yield return WaitIdle();
        tools.botHoldPour = false;
        yield return new WaitForSeconds(0.8f);
        Debug.Log($"[BOT] kabuk: {field.CrustAt(target):F2}");
        yield return Shot("05_crust");

        // Kazma
        tools.DoEquip(Tool.Pickaxe);
        yield return new WaitForSeconds(0.4f);
        player.LookAt(target);
        for (int i = 0; i < 6; i++)
        {
            tools.DoSwing();
            if (i == 0) { yield return new WaitForSeconds(0.2f); yield return Shot("06_swing"); }
            yield return WaitIdle();
            yield return new WaitForSeconds(0.1f);
        }
        yield return new WaitForSeconds(1.2f);
        Debug.Log($"[BOT] kazi: dig={field.DigAt(target):F2} kabuk={field.CrustAt(target):F2} cekirdek={LithicCoreItem.All.Count}");
        yield return Shot("07_dug");

        // Cekirdek al
        var core0 = LithicCoreItem.All.Count > 0 ? LithicCoreItem.All[0] : null;
        if (core0 != null)
        {
            Vector3 cp = core0.transform.position;
            player.Teleport(new Vector3(cp.x, cp.y, cp.z - 1.2f));
            yield return new WaitForSeconds(0.2f);
            tools.DoPickup();
            yield return new WaitForSeconds(0.6f);
        }
        Debug.Log("[BOT] elindeki cekirdek: " + tools.CoreCount);
        player.LookAt(target);
        yield return Shot("08_hold");

        // Kovalar yanlarda asagi bakis
        tools.DoEquip(Tool.Buckets);
        player.LookAt(player.transform.position + new Vector3(0f, -2f, 1f));
        yield return new WaitForSeconds(0.4f);
        yield return Shot("09_buckets_down");

        // Istasyona bak
        player.Teleport(spawn + new Vector3(0f, 0f, 3f));
        player.LookAt(new Vector3(spawn.x + 3f, spawn.y + 1.5f, spawn.z - 6f));
        yield return new WaitForSeconds(0.4f);
        yield return Shot("10_station");

        Debug.Log($"[BOT] bitti. ilerleme={field.Progress:P1}");
#if UNITY_EDITOR
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(0);
        else UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
