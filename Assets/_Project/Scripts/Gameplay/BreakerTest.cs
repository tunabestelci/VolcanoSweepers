#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

// Yalnizca editorde ve "VS_BREAKER_TEST" EditorPrefs anahtari varken: kiricilari otomatik dener,
// elde ve kullanirken ekran goruntusu alir, sonucu Temp/vs_shots/breaker_test.log dosyasina yazar.
public class BreakerTest : MonoBehaviour
{
    string dir;
    StringBuilder log = new StringBuilder();
    PlayerController player;
    PlayerTools tools;
    LavaField field;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string mode = UnityEditor.EditorPrefs.GetString("VS_BREAKER_TEST", "");
        if (string.IsNullOrEmpty(mode)) return;
        UnityEditor.EditorPrefs.DeleteKey("VS_BREAKER_TEST");
        var go = new GameObject("BreakerTest");
        go.AddComponent<BreakerTest>().mode = mode;
    }

    public string mode;

    void Start()
    {
        Application.runInBackground = true;
        dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "vs_shots");
        Directory.CreateDirectory(dir);
        StartCoroutine(Run());
    }

    void Log(string s) { log.AppendLine(s); Debug.Log("[BreakerTest] " + s); }

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
        Destroy(rt); Destroy(tex);
        yield return null;
    }

    // Lav kenarinda, lava bakan bir noktaya yerles
    Vector3 Stage()
    {
        Vector3 p;
        field.RandomLavaPoint(new Vector3(-74f, 0f, -22f), 3f, out p);
        Vector3 toPlat = new Vector3(-84.9f - p.x, 0f, -24.4f - p.z).normalized;
        Vector3 stand = p + toPlat * 3.5f;
        player.Teleport(new Vector3(stand.x, player.transform.position.y + 0.3f, stand.z));
        return p;
    }

    IEnumerator Run()
    {
        yield return new WaitForSeconds(1.5f);
        player = FindFirstObjectByType<PlayerController>();
        tools = FindFirstObjectByType<PlayerTools>();
        field = LavaField.I;
        var eco = Economy.I;
        if (player == null || tools == null || field == null || eco == null) { Log("eksik bilesen"); Finish(); yield break; }
        Log($"breakerPrefabs={(tools.breakerPrefabs == null ? -1 : tools.breakerPrefabs.Length)}");

        if (mode == "hands" || mode == "all")
        {
            for (int t = 1; t <= Economy.BreakerCount; t++)
            {
                eco.breakerTier = t;
                tools.DoEquip(Tool.Breaker);
                Vector3 p = Stage();
                yield return new WaitForSeconds(0.5f);
                player.LookAt(p);
                yield return new WaitForSeconds(0.9f);
                player.LookAt(p);
                yield return new WaitForSeconds(0.2f);
                yield return Shot($"hand_t{t:00}");
                Log($"t{t:00} {Economy.BreakerNames[t]} tool={tools.CurrentTool} price={Economy.BreakerPrice(t)} money/break={Economy.BreakerMoney(t)} radius={Economy.BreakerRadius(t):F2} power={Economy.BreakerPower(t):F2}");
            }
        }

        if (mode == "use" || mode == "all")
        {
            foreach (int t in new[] { 1, 4, 6, 9, 13, 16, 17, 20 })
            {
                eco.breakerTier = t;
                tools.DoEquip(Tool.Breaker);
                Vector3 p = Stage();
                yield return new WaitForSeconds(0.6f);
                player.LookAt(p);
                field.AddCrust(p, 1.6f, 1f);
                int money0 = eco.money;
                float crust0 = field.CrustAt(p);
                var kind = Economy.KindOf(t);
                if (kind == Economy.BreakerKind.Hammer)
                {
                    tools.DoSwing();
                    yield return new WaitForSeconds(0.42f);
                    yield return Shot($"use_t{t:00}_a");
                    yield return new WaitForSeconds(1.2f);
                }
                else
                {
                    tools.botHoldBreak = true;
                    yield return new WaitForSeconds(0.8f);
                    yield return Shot($"use_t{t:00}_a");
                    yield return new WaitForSeconds(1.2f);
                    tools.botHoldBreak = false;
                }
                Log($"use t{t:00} {kind}: crust {crust0:F2} -> {field.CrustAt(p):F2}, money {money0} -> {eco.money}, hint='{tools.Hint}'");
                yield return new WaitForSeconds(0.3f);
            }
        }

        if (mode == "shop" || mode == "all")
        {
            eco.Earn(2000);
            var keeper = FindFirstObjectByType<ShopKeeper>();
            if (keeper != null)
            {
                typeof(ShopKeeper).GetMethod("OpenShop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(keeper, null);
                yield return new WaitForSeconds(0.6f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "shop.png"));
                yield return new WaitForSeconds(1.0f);
                Log("shop screenshot requested");
            }
        }
        Finish();
    }

    void Finish()
    {
        File.WriteAllText(Path.Combine(dir, "breaker_test.log"), log.ToString());
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(0);
        else UnityEditor.EditorApplication.isPlaying = false;
    }
}
#endif
