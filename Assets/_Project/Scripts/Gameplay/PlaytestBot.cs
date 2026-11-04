using System.Collections;
using System.IO;
using UnityEngine;

// Yalnizca "-vsbot <klasor>" argumaniyla calisir: oyun dongusunu otomatik dener, ekran goruntusu alir.
public class PlaytestBot : MonoBehaviour
{
    public PlayerController player;
    public PlayerTools tools;
    public VolcanoGrid grid;

    string dir;

    void Awake()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-vsbot");
        if (i < 0 || i + 1 >= args.Length) { Destroy(this); return; }
        dir = args[i + 1];
        Directory.CreateDirectory(dir);
    }

    void Start()
    {
        if (dir != null) StartCoroutine(Run());
    }

    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
        yield return null;
    }

    IEnumerator WaitIdle()
    {
        yield return new WaitForSeconds(0.1f);
        while (tools.Busy) yield return null;
    }

    VolcanoCell Cell(int x, int y) => GameObject.Find($"Cell_{x}_{y}").GetComponent<VolcanoCell>();

    IEnumerator Run()
    {
        Debug.Log("[BOT] basladi");
        yield return new WaitForSeconds(1.5f);
        yield return Shot("01_start");

        // Sag kova: havuzdan doldur
        player.LookAt(new Vector3(0f, 0.07f, -14f));
        yield return new WaitForSeconds(0.3f);
        tools.DoBucket(false);
        yield return new WaitForSeconds(0.8f);
        yield return Shot("02_fill");
        yield return WaitIdle();
        Debug.Log("[BOT] sag kova dolduruldu");

        // Lava dok
        var c1 = Cell(4, 0);
        player.LookAt(c1.transform.position);
        tools.DoBucket(false);
        yield return new WaitForSeconds(0.9f);
        yield return Shot("03_pour");
        yield return WaitIdle();
        yield return new WaitForSeconds(0.6f);
        yield return Shot("04_steam");
        yield return new WaitForSeconds(1.2f);
        Debug.Log("[BOT] hucre durumu: " + c1.State);
        yield return Shot("05_obsidian");

        // Kazma
        tools.DoEquip(Tool.Pickaxe);
        yield return new WaitForSeconds(0.3f);
        player.LookAt(c1.transform.position);
        for (int i = 0; i < 3; i++)
        {
            tools.DoSwing();
            if (i == 0) { yield return new WaitForSeconds(0.2f); yield return Shot("06_swing"); }
            yield return WaitIdle();
        }
        yield return new WaitForSeconds(1.2f);
        Debug.Log("[BOT] hucre durumu (kazi sonrasi): " + c1.State + " cekirdek sayisi: " + LithicCoreItem.All.Count);
        yield return Shot("07_cores");

        // Cekirdek al
        var core0 = LithicCoreItem.All.Count > 0 ? LithicCoreItem.All[0] : null;
        player.Teleport((core0 != null ? core0.transform.position : c1.transform.position) + new Vector3(0f, 0f, -1.2f));
        yield return new WaitForSeconds(0.2f);
        tools.DoPickup();
        yield return new WaitForSeconds(0.6f);
        Debug.Log("[BOT] elindeki cekirdek: " + tools.CoreCount);
        player.LookAt(c1.transform.position + new Vector3(0f, -1f, 2f));
        yield return Shot("08_hold");

        // Kovalarla yurume / asagi bakis
        tools.DoEquip(Tool.Buckets);
        player.LookAt(player.transform.position + new Vector3(0f, -2f, 1f));
        yield return new WaitForSeconds(0.4f);
        yield return Shot("09_buckets_down");

        Debug.Log($"[BOT] bitti. ilerleme={grid.Progress:P0}");
        Application.Quit();
    }
}
