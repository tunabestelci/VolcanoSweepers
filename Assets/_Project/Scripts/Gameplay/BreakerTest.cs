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
            eco.toolTier = 0; eco.breakerTier = 1;
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
                field.Mine(p, 2.6f, 1f);                  // once kir: kazima yalnizca ufalanmis kabukta
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
                eco.toolTier = t; eco.breakerTier = 1;
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
                    field.AddCrust(p, 4.5f, 1f); field.AddCrust(p, 3.0f, 1f); field.Mine(p, 2.6f, 1f);
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

        // Oyun dongusu: su ile kabuk > kirmadan kazinmaz > cekicle ufala > her yone (daire) kazi > topla
        if (mode == "loop")
        {
            Log($"start loadout: tool={eco.toolTier} breaker={eco.breakerTier} ({Economy.BreakerNames[eco.breakerTier]}) vessel={eco.vesselTier}");
            var fDir = typeof(PlayerTools).GetField("scrapeToolDir", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fFocus = typeof(PlayerTools).GetField("scrapeFocus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Vector3 p = Vector3.zero;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                p = Stage();
                yield return new WaitForSeconds(0.6f);
                player.LookAt(p);
                yield return new WaitForSeconds(0.2f);
                var ray = new Ray(player.cam.transform.position, player.cam.transform.forward);
                if (field.Raycast(ray, tools.reach, out var hp) && field.IsLava(hp, 1.5f) && (hp - p).magnitude < 0.6f) break;
                Log($"stage attempt {attempt} unreachable");
            }
            field.AddCrust(p, 4.5f, 1f); field.AddCrust(p, 3.0f, 1f);
            yield return new WaitForSeconds(0.3f);
            Log($"crust={field.CrustAt(p):F2} broken={field.BrokenAt(p):F2}");
            // 1) kirmadan kazima denemesi
            tools.DoEquip(Tool.Scraper);
            yield return new WaitForSeconds(0.3f);
            tools.botHoldScrape = true;
            yield return new WaitForSeconds(0.5f);
            float tq = Time.time;
            while (Time.time - tq < 1.6f) { tools.botScrapeDelta = new Vector2(0f, 4f); yield return null; }
            tools.botScrapeDelta = Vector2.zero;
            Log($"unbroken scrape: tipLoad={tools.TipLoad:F1} g hint='{tools.Hint}'");
            yield return Shot("loop_1_unbroken");
            tools.botHoldScrape = false;
            yield return new WaitForSeconds(1.0f);
            // 2) cekicle ufala
            tools.DoEquip(Tool.Breaker);
            yield return new WaitForSeconds(0.5f);
            int m0 = eco.money;
            for (int k = 0; k < 4; k++)
            {
                player.LookAt(p);
                yield return new WaitForSeconds(0.25f);
                tools.DoSwing();
                yield return new WaitForSeconds(0.42f);
                if (k == 0) yield return Shot("loop_2_hit");
                yield return new WaitForSeconds(0.7f);
                Log($"hit {k}: crust={field.CrustAt(p):F2} broken={field.BrokenAt(p):F2} money={eco.money} cores={LithicCoreItem.All.Count} hint='{tools.Hint}'");
            }
            // cevre de ufalansin (daire kazimak icin genis alan)
            field.Mine(p, 1.4f, 1f);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("loop_3_softened");
            // 3) her yone kazima: daire ciz
            tools.DoEquip(Tool.Scraper);
            yield return new WaitForSeconds(0.3f);
            player.LookAt(p);
            yield return new WaitForSeconds(0.2f);
            tools.botHoldScrape = true;
            yield return new WaitForSeconds(0.5f);
            float c0 = field.CrustAt(p);
            float t0 = Time.time; float lastA = 999f; float turned = 0f;
            int cores0 = LithicCoreItem.All.Count;
            while (Time.time - t0 < 4.0f)
            {
                float tt = Time.time - t0;
                float ang = tt * 2.2f;                                   // ~1.4 tur
                tools.botScrapeDelta = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 4.2f;
                Vector3 d = (Vector3)fDir.GetValue(tools);
                float a = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                if (lastA < 900f) turned += Mathf.DeltaAngle(lastA, a);
                lastA = a;
                if (Mathf.Abs(tt - 1.0f) < 0.02f) yield return Shot("loop_4_circle_a");
                if (Mathf.Abs(tt - 2.4f) < 0.02f) yield return Shot("loop_4_circle_b");
                yield return null;
            }
            tools.botScrapeDelta = Vector2.zero;
            Vector3 fp = (Vector3)fFocus.GetValue(tools);
            Log($"circle scrape: tool turned {turned:F0} deg, tipLoad={tools.TipLoad:F1}/{tools.TipCapacity:F0} g, crust {c0:F2}->{field.CrustAt(p):F2} broken@p={field.BrokenAt(p):F2} @focus={field.BrokenAt(fp):F2} coresRevealed={LithicCoreItem.All.Count - cores0}");
            yield return Shot("loop_5_pile");
            tools.botHoldScrape = false;
            yield return new WaitForSeconds(1.6f);
            Log($"scoop: grams={tools.LastScoopGrams:F0} money {m0} -> {eco.money} ores={tools.CoreCount}/{tools.CoreCapacity}");
            yield return Shot("loop_6_after");
            // 4) ayni yeri tekrar tekrar kirip kaz: kabuk incelir, sonunda lav acilir
            int cycles = 0;
            for (; cycles < 6 && field.CrustAt(p) >= 0.3f; cycles++)
            {
                field.Mine(p, 1.4f, 1f);
                float c1 = field.CrustAt(p);
                tools.botHoldScrape = true;
                yield return new WaitForSeconds(0.4f);
                float ts = Time.time;
                while (Time.time - ts < 3f) { float ang = (Time.time - ts) * 2.6f; tools.botScrapeDelta = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 3.4f; yield return null; }
                tools.botScrapeDelta = Vector2.zero;
                tools.botHoldScrape = false;
                yield return new WaitForSeconds(1.4f);
                Log($"cycle {cycles}: crust {c1:F2} -> {field.CrustAt(p):F2}, lavaLevel={field.LavaLevel:F3} money={eco.money}");
            }
            yield return Shot("loop_7_cycles");
        }

        // Katmanlar: kir (3 katman) > 3 surtme > 4. surtme bos > tekrar kir ... 9 katmanda lav acilir
        if (mode == "layers")
        {
            Log($"start loadout: tool={eco.toolTier} breaker={eco.breakerTier} passLayers={tools.PassLayers} breakLayers={Economy.BreakLayers(eco.breakerTier)}");
            var fFocus = typeof(PlayerTools).GetField("scrapeFocus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fFwd = typeof(PlayerTools).GetField("scrapeFwd", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Vector3 p = Vector3.zero;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                p = Stage();
                yield return new WaitForSeconds(0.6f);
                player.LookAt(p);
                yield return new WaitForSeconds(0.2f);
                var ray = new Ray(player.cam.transform.position, player.cam.transform.forward);
                if (field.Raycast(ray, tools.reach, out var hp) && field.IsLava(hp, 1.5f) && (hp - p).magnitude < 0.6f) break;
                Log($"stage attempt {attempt} unreachable");
            }
            field.AddCrust(p, 4.5f, 1f); field.AddCrust(p, 3.0f, 1f);
            yield return new WaitForSeconds(0.3f);
            Vector3 fwd = player.transform.forward; fwd.y = 0f; fwd.Normalize();
            float ftMax = 0f, ftSum = 0f; int ftN = 0;
            int strokeTotal = 0;
            for (int cycle = 0; cycle < 4 && field.CrustAt(p + fwd * 0.12f) >= 0.3f; cycle++)
            {
                // kir: 3 noktaya ikiser vurus (surtme cizgisi boyunca)
                tools.DoEquip(Tool.Breaker);
                yield return new WaitForSeconds(0.4f);
                for (int k = 0; k < 6; k++)
                {
                    Vector3 hpnt = p + fwd * (0.12f * (k % 3));
                    player.LookAt(hpnt);
                    yield return new WaitForSeconds(0.15f);
                    tools.DoSwing();
                    yield return new WaitForSeconds(0.9f);
                }
                if (cycle == 0) yield return Shot("layers_broken");
                Log($"cycle {cycle} break: hint='{tools.Hint}' soft@mid={field.SoftBytesAt(p + fwd * 0.12f) / LavaField.LayerBytes:F2} layers, usable={field.UsableBytesAt(p + fwd * 0.12f) / LavaField.LayerBytes:F2}");
                // fazladan vurus: ufalanmis kazinmadan derine kirilmamali
                if (cycle == 0)
                {
                    float sb = field.SoftBytesAt(p + fwd * 0.12f);
                    player.LookAt(p + fwd * 0.12f); yield return new WaitForSeconds(0.15f);
                    tools.DoSwing(); yield return new WaitForSeconds(0.9f);
                    Log($"  extra hit: soft {sb / LavaField.LayerBytes:F2} -> {field.SoftBytesAt(p + fwd * 0.12f) / LavaField.LayerBytes:F2}, hint='{tools.Hint}'");
                }
                // kazi: 4 surtme, her biri ayni cizgide
                tools.DoEquip(Tool.Scraper);
                yield return new WaitForSeconds(0.3f);
                Vector3 rgt = Vector3.Cross(Vector3.up, fwd);
                for (int st = 0; st < 12; st++)
                {
                    player.LookAt(p + rgt * (0.07f * (st % 3 - 1)));
                    yield return new WaitForSeconds(0.25f);
                    tools.botHoldScrape = true;
                    yield return new WaitForSeconds(0.45f);
                    Vector3 a0 = (Vector3)fFocus.GetValue(tools);
                    float passes0 = tools.PassesLeftHere, left0 = tools.LayersLeftHere;
                    float ts = Time.time;
                    while (Time.time - ts < 0.42f)
                    {
                        tools.botScrapeDelta = new Vector2(0f, 4f);
                        ftSum += Time.unscaledDeltaTime; ftN++; ftMax = Mathf.Max(ftMax, Time.unscaledDeltaTime);
                        yield return null;
                    }
                    tools.botScrapeDelta = Vector2.zero;
                    yield return new WaitForSeconds(0.1f);
                    Vector3 a1 = (Vector3)fFocus.GetValue(tools);
                    float load = tools.TipLoad;
                    if (cycle == 0 && st == 4) yield return Shot("layers_stroke2");
                    if (cycle == 1 && st == 7) yield return Shot("layers_cycle1");
                    tools.botHoldScrape = false;
                    yield return new WaitForSeconds(1.3f);
                    strokeTotal++;
                    if (st % 3 == 2 || st >= 9) Log($"  stroke {st / 3}.{st % 3}: len={(a1 - a0).magnitude:F2} m grams={load:F1} (before: passesLeft={passes0:F2} layersLeft={left0:F2}) money={eco.money} hint='{tools.Hint}' crust@mid={field.CrustAt(p + fwd * 0.12f):F2}");
                }
            }
            Log($"end: crust@mid={field.CrustAt(p + fwd * 0.12f):F2} crust@p={field.CrustAt(p):F2} isLava@mid={field.CrustAt(p + fwd * 0.12f) < 0.3f}");
            Log($"frames during strokes: avg {ftSum / Mathf.Max(1, ftN) * 1000f:F1} ms max {ftMax * 1000f:F1} ms; lavaLevel={field.LavaLevel:F3}");
            yield return Shot("layers_end");
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
