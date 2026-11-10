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

        if (mode == "scrape")
        {
            eco.toolTier = 0; eco.breakerTier = 0;
            tools.DoEquip(Tool.Scraper);
            var fFocus = typeof(PlayerTools).GetField("scrapeFocus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Vector3 f0 = Vector3.zero;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                tools.botHoldScrape = false;
                yield return new WaitForSeconds(0.5f);
                Vector3 p = Stage();
                yield return new WaitForSeconds(0.6f);
                player.LookAt(p);
                field.AddCrust(p, 4.5f, 1f);
                field.AddCrust(p, 3.0f, 1f);
                yield return new WaitForSeconds(0.2f);
                tools.botHoldScrape = true;
                yield return new WaitForSeconds(0.6f);
                f0 = (Vector3)fFocus.GetValue(tools);
                if (f0.sqrMagnitude > 1f) break;
            }
            Log($"scrape focus attempt result: {f0}");
            // kirici ile kirilmis gibi birkac cevher: alet yaklasinca yigina katilmali
            var sc = typeof(PlayerTools).GetMethod("SpawnCores", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            sc.Invoke(tools, new object[] { f0 + new Vector3(0.1f, 0f, 0.55f) });
            yield return new WaitForSeconds(0.8f);
            {
                // iki cevheri kasigin itme yoluna koy (aletin ucunun 25-40 cm onu)
                var fFwd = typeof(PlayerTools).GetField("scrapeFwd", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Vector3 fw = (Vector3)fFwd.GetValue(tools);
                Vector3 tip = ScrapeDebris.I != null ? ScrapeDebris.I.ToolContactWorld : f0;
                for (int kk = 0; kk < 2; kk++)
                {
                    Vector3 cp = tip + fw * (0.3f + 0.3f * kk);
                    cp.y = field.HeightAt(cp) + 0.06f;
                    var go = Instantiate(tools.coreItemPrefab, cp, Random.rotation);
                    go.transform.localScale *= 0.55f;
                }
                yield return null; yield return null;
                Log("  after place: " + ScrapeDebris.I.Stats());
                yield return new WaitForSeconds(0.4f);
                Log("  after 0.4s: " + ScrapeDebris.I.Stats());
                Log($"cores placed ahead of tip {tip} fwd {fw}; coreRoom={typeof(PlayerTools).GetMethod("CoreRoom", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(tools, null)}");
            }
            // duz itis: cevherler kasigin burnuna takilmali
            {
                float tc = Time.time;
                while (Time.time - tc < 1.2f)
                {
                    tools.botScrapeDelta = new Vector2(0f, 3.6f);
                    yield return null;
                }
                tools.botScrapeDelta = Vector2.zero;
                yield return new WaitForSeconds(0.15f);
                Log("  straight push: " + ScrapeDebris.I.Stats());
                yield return Shot("core_push");
            }
            int money0 = eco.money;
            Log($"scrape start: focus={f0} cores={LithicCoreItem.All.Count} money={money0} vesselOres={tools.CoreCount}");
            var deb = ScrapeDebris.I;
            for (int round = 0; round < 4; round++)
            {
                float t0 = Time.time; int lastStroke = -1; float ftSum = 0f, ftMax = 0f; int ftN = 0;
                while (Time.time - t0 < (round == 3 ? 9f : 4.4f))
                {
                    // vuruslar: 0.55 s ileri it (kazir), 0.35 s yana kayarak geri cek (kazimaz)
                    float tt = Time.time - t0;
                    float ph = tt % 0.9f;
                    int stroke = Mathf.FloorToInt(tt / 0.9f);
                    float side = round == 3 ? 1f : (stroke % 2 == 0 ? 1f : -1f) * (round % 2 == 0 ? 1f : -1f);
                    tools.botScrapeDelta = ph < 0.55f ? new Vector2(0.9f * side, 4.6f) : new Vector2(3.2f * side, -7.0f);
                    ftSum += Time.unscaledDeltaTime; ftN++; ftMax = Mathf.Max(ftMax, Time.unscaledDeltaTime);
                    if (stroke != lastStroke && ph > 0.5f) { lastStroke = stroke; Log($"  r{round} stroke {stroke} push-end: {deb.Stats()}"); }
                    if (round == 0 && Mathf.Abs(tt - 1.3f) < 0.02f) yield return Shot("scrape_a");
                    if (Mathf.Abs(tt - 3.05f) < 0.02f) yield return Shot($"scrape_b{round}");
                    yield return null;
                }
                tools.botScrapeDelta = Vector2.zero;
                yield return new WaitForSeconds(0.25f);
                yield return Shot($"scrape_pile{round}");
                if (round == 3) { ScreenCapture.CaptureScreenshot(Path.Combine(dir, "hud_full.png")); yield return null; yield return null; }
                if (round == 0) { ScreenCapture.CaptureScreenshot(Path.Combine(dir, "hud_scrape.png")); yield return null; yield return null; }
                Log($"round {round} frame: avg {ftSum / Mathf.Max(1, ftN) * 1000f:F1} ms, max {ftMax * 1000f:F1} ms over {ftN} frames");
                Log($"round {round}: chips={deb.ChipCount} tipLoad={tools.TipLoad:F1}g/{tools.TipCapacity}g money={eco.money}");
                tools.botHoldScrape = false;              // birak: topla
                yield return new WaitForSeconds(0.12f);
                yield return Shot($"scoop{round}_a");
                yield return new WaitForSeconds(0.22f);
                yield return Shot($"scoop{round}_b");
                yield return new WaitForSeconds(0.25f);
                yield return Shot($"scoop{round}_c");
                yield return new WaitForSeconds(0.8f);
                Log($"scoop {round}: grams={tools.LastScoopGrams:F1} money+={tools.LastScoopMoney} total={eco.money} chipsLeft={deb.ChipCount} ores={tools.CoreCount} hint='{tools.Hint}'");
                yield return Shot($"scoop{round}_after");
                if (round < 3)
                {
                    if (round == 1) { eco.toolTier = 1; tools.RefreshLoadout(); Log("-- spatula --"); }
                    if (round == 2) { eco.toolTier = 0; tools.RefreshLoadout(); Log("-- kasik, uzun: tasma --"); }
                    player.LookAt(f0);
                    yield return new WaitForSeconds(0.3f);
                    tools.botHoldScrape = true;
                    yield return new WaitForSeconds(0.3f);
                }
            }
            Log($"scrape end: money {money0} -> {eco.money} chips={deb.ChipCount} ores={tools.CoreCount}/{tools.CoreCapacity}");
        }

        if (mode == "tools6")
        {
            var fFocus = typeof(PlayerTools).GetField("scrapeFocus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var deb0 = ScrapeDebris.Ensure();
            for (int t = 0; t < Economy.ToolNames.Length; t++)
            {
                eco.toolTier = t; eco.breakerTier = 0;
                tools.botHoldScrape = false;
                yield return new WaitForSeconds(0.4f);
                tools.DoEquip(Tool.Buckets);
                yield return null;
                tools.DoEquip(Tool.Scraper);
                Vector3 f0 = Vector3.zero;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    tools.botHoldScrape = false;
                    yield return new WaitForSeconds(0.4f);
                    Vector3 p = Stage();
                    yield return new WaitForSeconds(0.6f);
                    player.LookAt(p);
                    field.AddCrust(p, 4.5f, 1f); field.AddCrust(p, 3.0f, 1f);
                    yield return new WaitForSeconds(0.3f);
                    if (attempt == 0) yield return Shot($"s{t}_hand");
                    tools.botHoldScrape = true;
                    yield return new WaitForSeconds(0.6f);
                    f0 = (Vector3)fFocus.GetValue(tools);
                    if (f0.sqrMagnitude > 1f) break;
                }
                int m0 = eco.money;
                float t0 = Time.time;
                while (Time.time - t0 < 3.2f)
                {
                    float tt = Time.time - t0, ph = tt % 0.9f;
                    float side = (Mathf.FloorToInt(tt / 0.9f) % 2 == 0) ? 1f : -1f;
                    tools.botScrapeDelta = ph < 0.55f ? new Vector2(0.9f * side, 4.6f) : new Vector2(3.2f * side, -7.0f);
                    if (Mathf.Abs(tt - 1.6f) < 0.02f) yield return Shot($"s{t}_scrape");
                    yield return null;
                }
                tools.botScrapeDelta = Vector2.zero;
                yield return new WaitForSeconds(0.2f);
                Log($"tool {t} {Economy.ToolNames[t]}: tipLoad={tools.TipLoad:F0}/{tools.TipCapacity:F0} g chips={deb0.ChipCount}");
                tools.botHoldScrape = false;
                yield return new WaitForSeconds(0.3f);
                yield return Shot($"s{t}_scoop");
                yield return new WaitForSeconds(1.0f);
                Log($"   scoop grams={tools.LastScoopGrams:F0} money+={eco.money - m0}");
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
                var setCat = typeof(ShopKeeper).GetMethod("SetCat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var catT = typeof(ShopKeeper).GetNestedType("Cat", System.Reflection.BindingFlags.NonPublic);
                setCat.Invoke(keeper, new object[] { System.Enum.ToObject(catT, 1) });
                yield return new WaitForSeconds(0.5f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "shop_breakers.png"));
                yield return new WaitForSeconds(1.0f);
                // satin alma: bir kaziyici al, kartin parlamasi ve para dususu gorunsun
                setCat.Invoke(keeper, new object[] { System.Enum.ToObject(catT, 0) });
                var tryBuy = typeof(ShopKeeper).GetMethod("TryBuy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                tryBuy.Invoke(keeper, new object[] { System.Enum.ToObject(catT, 0), eco.toolTier + 1 });
                yield return new WaitForSeconds(0.25f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "shop_bought.png"));
                yield return new WaitForSeconds(1.0f);
                Log($"shop: toolTier={eco.toolTier} money={eco.money}");
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
