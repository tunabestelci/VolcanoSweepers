using UnityEngine;
using UnityEngine.InputSystem;

// Lav Adam'in dukkani: yakina gelip ona bakinca E ile acilir.
// Sol: kategori sekmeleri (kaziyici / kirici / kap), orta: urun kartlari (ikonlu), sag: secili urunun ayrintisi ve satin al.
// Urunler sirayla acilir: bir kategoride ancak bir oncekine sahipken sonraki alinabilir.
public class ShopKeeper : MonoBehaviour
{
    public static bool Open { get; private set; }
    public static int LastClose = -1;

    public PlayerTools tools;
    public PlayerController player;
    public Transform lookPoint;               // canavarin gogsu
    public float talkRange = 7f;

    // Editor'de prefablardan cizilen ikonlar (ShopIcons): [kademe]
    public Texture2D[] toolIcons;
    public Texture2D[] breakerIcons;          // [0] bos, [1..20]
    public Texture2D[] vesselIcons;

    enum Cat { Scraper, Breaker, Vessel }
    Cat cat = Cat.Scraper;
    int sel = -1;                             // secili kademe
    Vector2 scroll;
    float openT;                              // acilis animasyonu
    float shownMoney;
    float flashT = -10f; int flashTier = -1; Cat flashCat;
    string message = ""; float messageUntil;
    float buyFloatT = -10f; int buyFloatAmount;

    // ---------- giris / cikis ----------

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || player == null) return;
        if (Open)
        {
            if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame) Close();
            else if (kb.tabKey.wasPressedThisFrame) SetCat((Cat)(((int)cat + 1) % 3));
            else if (kb.digit1Key.wasPressedThisFrame) SetCat(Cat.Scraper);
            else if (kb.digit2Key.wasPressedThisFrame) SetCat(Cat.Breaker);
            else if (kb.digit3Key.wasPressedThisFrame) SetCat(Cat.Vessel);
            else if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) TryBuy(cat, sel);
            return;
        }
        if (Cursor.lockState != CursorLockMode.Locked) return;
        if (CanTalk() && kb.eKey.wasPressedThisFrame) OpenShop();
    }

    bool CanTalk()
    {
        if (lookPoint == null || tools == null) return false;
        Transform cam = tools.cam.transform;
        Vector3 to = lookPoint.position - cam.position;
        float d = to.magnitude;
        if (d > talkRange) return false;
        return Vector3.Dot(cam.forward, to / Mathf.Max(d, 0.01f)) > 0.82f;
    }

    void OpenShop()
    {
        Open = true;
        openT = 0f;
        if (Economy.I != null) shownMoney = Economy.I.money;
        SetCat(cat);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Close()
    {
        Open = false;
        LastClose = Time.frameCount;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnDisable() { Open = false; }

    void SetCat(Cat c)
    {
        cat = c;
        scroll = Vector2.zero;
        var eco = Economy.I;
        if (eco == null) { sel = 1; return; }
        // varsayilan secim: siradaki alinabilir urun (yoksa en son sahip olunan)
        sel = Mathf.Clamp(Have(eco, c) + 1, First(c), Count(c) - 1);
    }

    // ---------- kategori verisi ----------

    static int Count(Cat c) => c == Cat.Scraper ? Economy.ToolNames.Length : (c == Cat.Breaker ? Economy.BreakerCount + 1 : Economy.VesselNames.Length);
    static int First(Cat c) => c == Cat.Breaker ? 1 : 0;
    static int Have(Economy e, Cat c) => c == Cat.Scraper ? e.toolTier : (c == Cat.Breaker ? e.breakerTier : e.vesselTier);
    static string Name(Cat c, int t) => c == Cat.Scraper ? Economy.ToolNames[t] : (c == Cat.Breaker ? Economy.BreakerNames[t] : Economy.VesselNames[t]);
    static int Price(Cat c, int t) => c == Cat.Scraper ? Economy.ToolPrice[t] : (c == Cat.Breaker ? Economy.BreakerPrice(t) : Economy.VesselPrice[t]);
    static string Info(Cat c, int t) => c == Cat.Scraper ? Economy.ToolInfo[t] : (c == Cat.Breaker ? Economy.BreakerInfo(t) : Economy.VesselInfo[t]);
    Texture2D Icon(Cat c, int t)
    {
        var arr = c == Cat.Scraper ? toolIcons : (c == Cat.Breaker ? breakerIcons : vesselIcons);
        return arr != null && t >= 0 && t < arr.Length ? arr[t] : null;
    }
    static string CatTitle(Cat c) => c == Cat.Scraper ? "KAZIYICILAR" : (c == Cat.Breaker ? "KIRICILAR" : "KAPLAR");
    static string CatSub(Cat c) => c == Cat.Scraper ? "ust tabakayi soyar, kirintiyi toplar" : (c == Cat.Breaker ? "kabugu kirar, cevher cikarir" : "su ve cevher tasir");

    bool TryBuy(Cat c, int t)
    {
        var eco = Economy.I;
        if (eco == null || t < First(c) || t >= Count(c)) return false;
        if (Have(eco, c) != t - 1 || eco.money < Price(c, t)) return false;
        int price = Price(c, t);
        bool ok = c == Cat.Scraper ? eco.BuyTool() : (c == Cat.Breaker ? eco.BuyBreaker() : eco.BuyVessel());
        if (!ok) return false;
        tools.RefreshLoadout();
        message = $"{Name(c, t)} senin!  " + (c == Cat.Scraper ? "(3: kaziyici)" : c == Cat.Breaker ? "(4: kirici)" : "(1: kaplar)");
        messageUntil = Time.unscaledTime + 3f;
        flashT = Time.unscaledTime; flashTier = t; flashCat = c;
        buyFloatT = Time.unscaledTime; buyFloatAmount = price;
        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.pickaxeHit : null, player.transform.position, 0.35f, 2.2f, 0.02f);
        // siradakine gec
        if (t + 1 < Count(c)) sel = t + 1;
        return true;
    }

    // ---------- cizim yardimcilari ----------

    static Texture2D white;
    GUIStyle sTitle, sH1, sH2, sBody, sSmall, sPrice, sBtn, sTab, sBadge, sCenter, sRight;
    int styleH;

    static Texture2D White
    {
        get
        {
            if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); white.hideFlags = HideFlags.DontSave; }
            return white;
        }
    }

    // yuvarlak koseli dolu dikdortgen (GUI.color'in saydamligi da uygulanir)
    static void Box(Rect r, Color col, float radius = 10f)
    {
        col.a *= GUI.color.a;
        float k = Mathf.Min(radius, Mathf.Min(r.width, r.height) * 0.5f);
        GUI.DrawTexture(r, White, ScaleMode.StretchToFill, true, 0f, col, Vector4.zero, new Vector4(k, k, k, k));
    }

    static void Fill(Rect r, Color col)
    {
        col.a *= GUI.color.a;
        GUI.DrawTexture(r, White, ScaleMode.StretchToFill, true, 0f, col, Vector4.zero, Vector4.zero);
    }

    void Styles(float h)
    {
        int H = Mathf.RoundToInt(h);
        if (sTitle != null && styleH == H) return;
        styleH = H;
        GUIStyle Mk(float size, Color c, FontStyle fs = FontStyle.Normal, TextAnchor a = TextAnchor.UpperLeft, bool wrap = false)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(9, Mathf.RoundToInt(h * size)), fontStyle = fs, alignment = a, wordWrap = wrap, richText = true, clipping = TextClipping.Clip };
            s.normal.textColor = c; s.padding = new RectOffset(0, 0, 0, 0); s.margin = new RectOffset(0, 0, 0, 0);
            return s;
        }
        sTitle = Mk(0.042f, new Color(1f, 0.74f, 0.3f), FontStyle.Bold, TextAnchor.MiddleLeft);
        sH1 = Mk(0.032f, Color.white, FontStyle.Bold, TextAnchor.MiddleLeft);
        sH2 = Mk(0.022f, new Color(1f, 0.93f, 0.85f), FontStyle.Bold, TextAnchor.MiddleLeft);
        sBody = Mk(0.019f, new Color(0.88f, 0.82f, 0.76f), FontStyle.Normal, TextAnchor.UpperLeft, true);
        sSmall = Mk(0.0155f, new Color(0.75f, 0.68f, 0.62f), FontStyle.Normal, TextAnchor.MiddleLeft);
        sPrice = Mk(0.02f, new Color(1f, 0.82f, 0.35f), FontStyle.Bold, TextAnchor.MiddleLeft);
        sBtn = Mk(0.026f, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
        sTab = Mk(0.021f, Color.white, FontStyle.Bold, TextAnchor.MiddleLeft);
        sBadge = Mk(0.0145f, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
        sCenter = Mk(0.02f, new Color(1f, 0.9f, 0.6f), FontStyle.Bold, TextAnchor.MiddleCenter, true);
        sRight = Mk(0.03f, new Color(1f, 0.85f, 0.35f), FontStyle.Bold, TextAnchor.MiddleRight);
    }

    // renkler
    static readonly Color cPanel = new Color(0.085f, 0.045f, 0.035f, 0.97f);
    static readonly Color cPanel2 = new Color(0.14f, 0.07f, 0.05f, 1f);
    static readonly Color cCard = new Color(0.17f, 0.09f, 0.065f, 1f);
    static readonly Color cWell = new Color(0.115f, 0.06f, 0.045f, 1f);
    static readonly Color cLava = new Color(1f, 0.48f, 0.12f, 1f);
    static readonly Color cGold = new Color(1f, 0.78f, 0.3f, 1f);
    static readonly Color cOwned = new Color(0.36f, 0.78f, 0.42f, 1f);
    static readonly Color cLocked = new Color(0.45f, 0.4f, 0.38f, 1f);

    // ---------- OnGUI ----------

    void OnGUI()
    {
        var eco = Economy.I;
        if (eco == null) return;
        float w = Screen.width, h = Screen.height;
        Styles(h);

        if (!Open)
        {
            if (CanTalk() && Cursor.lockState == CursorLockMode.Locked)
            {
                var r = new Rect(w * 0.5f - h * 0.17f, h * 0.56f, h * 0.34f, h * 0.05f);
                Box(r, new Color(0.08f, 0.04f, 0.03f, 0.8f), 12);
                GUI.Label(r, "<b>E</b>  Lav Adam'in dukkani", sCenter);
            }
            return;
        }

        openT = Mathf.MoveTowards(openT, 1f, Time.unscaledDeltaTime / 0.18f);
        float ease = 1f - Mathf.Pow(1f - openT, 3f);
        shownMoney = Mathf.Lerp(shownMoney, eco.money, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
        if (Mathf.Abs(shownMoney - eco.money) < 0.5f) shownMoney = eco.money;

        // karartma
        Fill(new Rect(0, 0, w, h), new Color(0.02f, 0.01f, 0.01f, 0.72f * ease));

        float pw = Mathf.Min(w * 0.9f, h * 1.75f), ph = h * 0.86f;
        float scale = Mathf.Lerp(0.94f, 1f, ease);
        var box = new Rect((w - pw * scale) / 2f, (h - ph * scale) / 2f + (1f - ease) * h * 0.02f, pw * scale, ph * scale);
        var oldMatrix = GUI.matrix;
        var oldCol = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, ease);

        // panel + lav cizgisi + hafif ic isilti
        Box(new Rect(box.x - 3, box.y - 3, box.width + 6, box.height + 6), new Color(1f, 0.45f, 0.1f, 0.35f), 20);
        Box(box, cPanel, 18);
        Box(new Rect(box.x + 14, box.y + 8, box.width - 28, 5), cLava, 3);

        float pad = h * 0.022f;
        // baslik + para
        var head = new Rect(box.x + pad, box.y + pad * 0.9f, box.width - pad * 2, h * 0.07f);
        GUI.Label(new Rect(head.x, head.y, head.width * 0.6f, head.height * 0.62f), "LAV ADAM'IN DUKKANI", sTitle);
        GUI.Label(new Rect(head.x, head.y + head.height * 0.6f, head.width * 0.6f, head.height * 0.4f), "obsidyen kaz, para kazan, daha iyisini al", sSmall);
        DrawMoney(new Rect(head.xMax - h * 0.3f, head.y + h * 0.006f, h * 0.3f, h * 0.055f));

        float top = head.yMax + pad * 0.6f;
        float bottom = box.yMax - pad - h * 0.04f;
        // sol: sekmeler
        float tabW = pw * 0.19f;
        var tabsR = new Rect(box.x + pad, top, tabW, bottom - top);
        DrawTabs(eco, tabsR);
        // sag: ayrinti
        float detW = pw * 0.29f;
        var detR = new Rect(box.xMax - pad - detW, top, detW, bottom - top);
        // orta: kartlar
        var gridR = new Rect(tabsR.xMax + pad, top, detR.x - tabsR.xMax - pad * 2, bottom - top);
        DrawGrid(eco, gridR);
        DrawDetail(eco, detR);

        // alt satir: mesaj + kontroller
        var foot = new Rect(box.x + pad, bottom + pad * 0.35f, box.width - pad * 2, h * 0.035f);
        if (Time.unscaledTime < messageUntil)
        {
            float a = Mathf.Clamp01((messageUntil - Time.unscaledTime) / 0.4f);
            var oc = GUI.color; GUI.color = new Color(1f, 1f, 1f, a * ease);
            GUI.Label(new Rect(foot.x, foot.y, foot.width * 0.6f, foot.height), message, new GUIStyle(sH2) { normal = { textColor = cGold } });
            GUI.color = oc;
        }
        GUI.Label(new Rect(foot.x, foot.y, foot.width, foot.height), "Tab / 1-2-3: kategori   |   Tikla: sec   |   Enter: satin al   |   E / Esc: kapat", new GUIStyle(sSmall) { alignment = TextAnchor.MiddleRight });

        GUI.color = oldCol;
        GUI.matrix = oldMatrix;
    }

    void DrawMoney(Rect r)
    {
        Box(r, cPanel2, 14);
        float s = r.height * 0.62f;
        var coin = new Rect(r.x + r.height * 0.22f, r.y + (r.height - s) / 2f, s, s);
        Box(coin, cGold, s / 2f);
        Box(new Rect(coin.x + s * 0.2f, coin.y + s * 0.2f, s * 0.6f, s * 0.6f), new Color(0.85f, 0.55f, 0.15f), s * 0.3f);
        GUI.Label(new Rect(coin.xMax, r.y, r.width - (coin.xMax - r.x) - r.height * 0.25f, r.height), $"{Mathf.RoundToInt(shownMoney)}", sRight);
        // satin almada ucan "-fiyat"
        float bt = Time.unscaledTime - buyFloatT;
        if (bt < 1.1f)
        {
            var oc = GUI.color; GUI.color = new Color(1f, 0.45f, 0.35f, 1f - bt / 1.1f);
            GUI.Label(new Rect(r.x, r.yMax + bt * r.height * 0.6f, r.width - r.height * 0.25f, r.height * 0.8f), $"-{buyFloatAmount}", new GUIStyle(sRight) { fontSize = Mathf.RoundToInt(sRight.fontSize * 0.75f) });
            GUI.color = oc;
        }
    }

    void DrawTabs(Economy eco, Rect r)
    {
        float th = r.height * 0.17f, gap = r.height * 0.025f;
        for (int i = 0; i < 3; i++)
        {
            var c = (Cat)i;
            var tr = new Rect(r.x, r.y + i * (th + gap), r.width, th);
            bool on = c == cat;
            bool hover = tr.Contains(Event.current.mousePosition);
            Box(tr, on ? new Color(0.32f, 0.14f, 0.07f) : (hover ? new Color(0.2f, 0.1f, 0.07f) : cPanel2), 12);
            if (on) Box(new Rect(tr.x, tr.y + th * 0.18f, 5, th * 0.64f), cLava, 2);
            // kategori ikonu: o kategorinin sahip olunan en iyi urunu
            int best = Mathf.Max(First(c), Have(eco, c));
            var ic = Icon(c, best);
            float isz = th * 0.72f;
            var irect = new Rect(tr.x + th * 0.2f, tr.y + (th - isz) / 2f, isz, isz);
            Box(irect, cWell, 8);
            if (ic != null) GUI.DrawTexture(irect, ic, ScaleMode.ScaleToFit);
            float tx = irect.xMax + th * 0.18f;
            GUI.Label(new Rect(tx, tr.y + th * 0.12f, tr.xMax - tx - 6, th * 0.42f), CatTitle(c), sTab);
            int owned = Have(eco, c) - First(c) + 1, total = Count(c) - First(c);
            GUI.Label(new Rect(tx, tr.y + th * 0.52f, tr.xMax - tx - 6, th * 0.32f), $"{owned}/{total} sahipsin", sSmall);
            // ilerleme cubugu
            var pr = new Rect(tx, tr.yMax - th * 0.14f, tr.xMax - tx - th * 0.25f, Mathf.Max(3f, th * 0.05f));
            Box(pr, cWell, 2);
            Box(new Rect(pr.x, pr.y, pr.width * owned / Mathf.Max(1f, total), pr.height), on ? cLava : cGold * 0.8f, 2);
            if (GUI.Button(tr, GUIContent.none, GUIStyle.none)) SetCat(c);
        }
        // altta: secili kategori ipucu
        var hint = new Rect(r.x, r.y + 3 * (th + gap) + gap, r.width, r.height - 3 * (th + gap) - gap);
        Box(hint, new Color(0.11f, 0.055f, 0.04f, 1f), 12);
        string tip = cat == Cat.Scraper ? "Sol tik basili tut ve fareyi sur: ust tabaka soyulur, kirintilar aletin ucunda birikir. Birakinca toplanir. Tekerlek: alet acisi."
                   : cat == Cat.Breaker ? "Cekic: tikla ve vur. Matkap / kirici: sol tik basili tut. Kirdigin kabuk kirintiya ve cevhere doner - sonra kaziyiciyla topla."
                   : "Kuyu kovasindan su al, lavin ustune dok: kabuk olusur. Buyuk kap daha cok su ve cevher tasir.";
        GUI.Label(new Rect(hint.x + 10, hint.y + 8, hint.width - 20, hint.height - 16), tip, sBody);
    }

    void DrawGrid(Economy eco, Rect r)
    {
        Box(r, new Color(0.06f, 0.03f, 0.025f, 0.9f), 14);
        int first = First(cat), count = Count(cat);
        int n = count - first;
        int cols = cat == Cat.Breaker ? 4 : 3;
        float gap = r.width * 0.022f;
        float cw = (r.width - gap * (cols + 1) - 14f) / cols;
        float ch = cw * 1.22f;
        int rows = Mathf.CeilToInt(n / (float)cols);
        float contentH = rows * (ch + gap) + gap;
        var view = new Rect(0, 0, r.width - 16f, contentH);
        scroll = GUI.BeginScrollView(new Rect(r.x, r.y + 2, r.width, r.height - 4), scroll, view, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
        for (int i = 0; i < n; i++)
        {
            int t = first + i;
            int row = i / cols, col = i % cols;
            var cr = new Rect(gap + col * (cw + gap), gap + row * (ch + gap), cw, ch);
            DrawCard(eco, cr, t, r, view);
        }
        GUI.EndScrollView();
    }

    void DrawCard(Economy eco, Rect cr, int t, Rect gridR, Rect view)
    {
        int have = Have(eco, cat);
        bool owned = have >= t, next = have == t - 1, afford = eco.money >= Price(cat, t);
        bool selected = sel == t;
        bool hover = cr.Contains(Event.current.mousePosition);
        float lift = hover && !selected ? -2f : 0f;
        cr.y += lift;
        // secili: lav cerceve; siradaki: altin isilti (nabiz)
        if (selected) Box(new Rect(cr.x - 3, cr.y - 3, cr.width + 6, cr.height + 6), cLava, 14);
        else if (next)
        {
            float pulse = 0.35f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f);
            Box(new Rect(cr.x - 2, cr.y - 2, cr.width + 4, cr.height + 4), new Color(cGold.r, cGold.g, cGold.b, pulse), 13);
        }
        Box(cr, hover ? new Color(0.21f, 0.11f, 0.08f) : cCard, 12);
        // satin alma parlamasi
        if (flashTier == t && flashCat == cat)
        {
            float f = Mathf.Clamp01(1f - (Time.unscaledTime - flashT) / 0.8f);
            if (f > 0f) Box(cr, new Color(1f, 0.85f, 0.4f, 0.55f * f), 12);
        }
        float p = cr.width * 0.07f;
        var ir = new Rect(cr.x + p, cr.y + p, cr.width - p * 2, cr.width - p * 2);
        Box(ir, cWell, 10);
        var ic = Icon(cat, t);
        var oc = GUI.color;
        if (!owned && !next) GUI.color = new Color(0.45f, 0.42f, 0.42f, oc.a);
        if (ic != null) GUI.DrawTexture(new Rect(ir.x + 3, ir.y + 3, ir.width - 6, ir.height - 6), ic, ScaleMode.ScaleToFit);
        GUI.color = oc;
        // kademe rozeti
        var tb = new Rect(ir.x + 5, ir.y + 5, cr.width * 0.2f, cr.width * 0.12f);
        Box(tb, new Color(0f, 0f, 0f, 0.55f), 6);
        GUI.Label(tb, $"{t - First(cat) + 1}", sBadge);
        // ad
        var nr = new Rect(cr.x + p, ir.yMax + p * 0.4f, cr.width - p * 2, cr.height * 0.12f);
        GUI.Label(nr, Name(cat, t), new GUIStyle(sH2) { fontSize = Mathf.RoundToInt(sH2.fontSize * (cat == Cat.Breaker ? 0.82f : 0.95f)) });
        // durum rozeti
        var br = new Rect(cr.x + p, nr.yMax + p * 0.2f, cr.width - p * 2, cr.height * 0.1f);
        if (owned)
        {
            Box(br, new Color(cOwned.r * 0.4f, cOwned.g * 0.4f, cOwned.b * 0.4f, 1f), 8);
            GUI.Label(br, have == t ? "ELINDE" : "SAHIPSIN", new GUIStyle(sBadge) { normal = { textColor = cOwned } });
        }
        else
        {
            Box(br, next ? (afford ? new Color(0.45f, 0.25f, 0.05f) : new Color(0.25f, 0.13f, 0.06f)) : new Color(0.16f, 0.13f, 0.12f), 8);
            string txt = next ? $"{Price(cat, t)}" : $"KILITLI  {Price(cat, t)}";
            GUI.Label(br, txt, new GUIStyle(sBadge) { normal = { textColor = next ? (afford ? cGold : new Color(0.9f, 0.5f, 0.4f)) : cLocked } });
        }
        if (GUI.Button(cr, GUIContent.none, GUIStyle.none))
        {
            if (sel == t && Event.current.clickCount >= 2) TryBuy(cat, t);
            sel = t;
        }
    }

    void DrawDetail(Economy eco, Rect r)
    {
        Box(r, cPanel2, 14);
        if (sel < First(cat) || sel >= Count(cat)) return;
        int t = sel;
        int have = Have(eco, cat);
        bool owned = have >= t, next = have == t - 1;
        int price = Price(cat, t);
        bool afford = eco.money >= price;
        float p = r.width * 0.06f;
        float isz = r.width - p * 2;
        var ir = new Rect(r.x + p, r.y + p, isz, Mathf.Min(isz * 0.7f, r.height * 0.34f));
        Box(ir, cWell, 12);
        var ic = Icon(cat, t);
        if (ic != null)
        {
            // yavas salinan buyuk ikon
            float bob = Mathf.Sin(Time.unscaledTime * 1.6f) * ir.height * 0.012f;
            GUI.DrawTexture(new Rect(ir.x + 6, ir.y + 6 + bob, ir.width - 12, ir.height - 12), ic, ScaleMode.ScaleToFit);
        }
        float y = ir.yMax + p * 0.5f;
        GUI.Label(new Rect(r.x + p, y, r.width - p * 2, r.height * 0.06f), Name(cat, t), sH1);
        y += r.height * 0.06f;
        string kind = cat == Cat.Breaker ? (Economy.KindOf(t) == Economy.BreakerKind.Hammer ? "Cekic / balyoz" : Economy.KindOf(t) == Economy.BreakerKind.Drill ? "Matkap" : "Kirici") : (cat == Cat.Scraper ? "Kaziyici" : "Kap");
        GUI.Label(new Rect(r.x + p, y, r.width - p * 2, r.height * 0.035f), $"{kind}  -  kademe {t - First(cat) + 1}/{Count(cat) - First(cat)}", sSmall);
        y += r.height * 0.045f;
        float infoH = r.height * 0.075f;
        GUI.Label(new Rect(r.x + p, y, r.width - p * 2, infoH), Info(cat, t), sBody);
        y += infoH + p * 0.2f;

        // ozellik cubuklari
        foreach (var st in Stats(cat, t))
        {
            GUI.Label(new Rect(r.x + p, y, r.width * 0.42f, r.height * 0.035f), st.label, sSmall);
            GUI.Label(new Rect(r.x + p, y, r.width - p * 2, r.height * 0.035f), st.value, new GUIStyle(sSmall) { alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(1f, 0.92f, 0.8f) } });
            y += r.height * 0.035f;
            var bar = new Rect(r.x + p, y, r.width - p * 2, Mathf.Max(4f, r.height * 0.011f));
            Box(bar, cWell, 3);
            float cur = Mathf.Clamp01(st.frac);
            // sahip olunanla karsilastirma: daha iyiyse yesil fark
            float mine = Mathf.Clamp01(st.ownedFrac);
            Box(new Rect(bar.x, bar.y, bar.width * Mathf.Min(cur, mine), bar.height), cGold * 0.85f, 3);
            if (cur > mine) Box(new Rect(bar.x + bar.width * mine, bar.y, bar.width * (cur - mine), bar.height), cOwned, 3);
            y += bar.height + r.height * 0.016f;
        }

        // satin al dugmesi
        var b = new Rect(r.x + p, r.yMax - p - r.height * 0.1f, r.width - p * 2, r.height * 0.1f);
        if (owned)
        {
            Box(b, new Color(0.13f, 0.25f, 0.14f), 12);
            GUI.Label(b, have == t ? "ELINDE" : "SAHIPSIN", new GUIStyle(sBtn) { normal = { textColor = cOwned } });
        }
        else if (!next)
        {
            Box(b, new Color(0.17f, 0.14f, 0.13f), 12);
            GUI.Label(b, $"Once: {Name(cat, t - 1)}", new GUIStyle(sBtn) { fontSize = Mathf.RoundToInt(sBtn.fontSize * 0.7f), normal = { textColor = cLocked } });
        }
        else
        {
            bool hover = b.Contains(Event.current.mousePosition);
            Box(b, afford ? (hover ? new Color(1f, 0.58f, 0.18f) : new Color(0.92f, 0.45f, 0.1f)) : new Color(0.3f, 0.16f, 0.08f), 12);
            GUI.Label(b, afford ? $"SATIN AL   {price}" : $"{price - eco.money} para eksik", new GUIStyle(sBtn) { normal = { textColor = afford ? Color.white : new Color(0.95f, 0.6f, 0.45f) } });
            if (!afford)
            {
                // ne kadar kaldi
                var pr = new Rect(b.x, b.y - r.height * 0.02f, b.width, Mathf.Max(3f, r.height * 0.008f));
                Box(pr, cWell, 2);
                Box(new Rect(pr.x, pr.y, pr.width * Mathf.Clamp01(eco.money / (float)price), pr.height), cGold, 2);
            }
            if (afford && GUI.Button(b, GUIContent.none, GUIStyle.none)) TryBuy(cat, t);
        }
    }

    struct Stat { public string label, value; public float frac, ownedFrac; }

    System.Collections.Generic.List<Stat> Stats(Cat c, int t)
    {
        var l = new System.Collections.Generic.List<Stat>();
        var eco = Economy.I;
        int have = Mathf.Max(First(c), Have(eco, c));
        if (c == Cat.Scraper)
        {
            float W(int k) => PlayerTools.ScraperWidthCm(k);
            float maxW = W(Economy.ToolNames.Length - 1);
            float maxG = Economy.ToolScoopGrams[Economy.ToolScoopGrams.Length - 1];
            l.Add(new Stat { label = "Bicak genisligi", value = $"{W(t):F0} cm", frac = W(t) / maxW, ownedFrac = W(have) / maxW });
            l.Add(new Stat { label = "Kepce kapasitesi", value = $"{Economy.ToolScoopGrams[t]:F0} g", frac = Economy.ToolScoopGrams[t] / maxG, ownedFrac = Economy.ToolScoopGrams[have] / maxG });
            float wt = PlayerTools.ScraperWeight(t);
            l.Add(new Stat { label = "Agirlik", value = wt < 0.1f ? "hafif" : wt < 0.35f ? "orta" : "agir", frac = 0.15f + wt, ownedFrac = 0.15f + PlayerTools.ScraperWeight(have) });
        }
        else if (c == Cat.Breaker)
        {
            float maxR = Economy.BreakerRadius(Economy.BreakerCount), maxM = Economy.BreakerMoney(Economy.BreakerCount);
            l.Add(new Stat { label = "Etki alani", value = $"{Economy.BreakerRadius(t):F2} m", frac = Economy.BreakerRadius(t) / maxR, ownedFrac = have > 0 ? Economy.BreakerRadius(have) / maxR : 0f });
            float Pw(int k) => Economy.KindOf(k) == Economy.BreakerKind.Hammer ? Economy.BreakerPower(k) : Economy.BreakerPower(k) / Economy.BreakerTempo(k) * 0.08f;
            float maxP = Pw(Economy.BreakerCount);
            l.Add(new Stat { label = "Kirma gucu", value = Economy.KindOf(t) == Economy.BreakerKind.Hammer ? "vurus" : "surekli", frac = Pw(t) / maxP, ownedFrac = have > 0 ? Pw(have) / maxP : 0f });
            l.Add(new Stat { label = "Kirilan basina para", value = $"+{Economy.BreakerMoney(t)}", frac = Economy.BreakerMoney(t) / maxM, ownedFrac = have > 0 ? Economy.BreakerMoney(have) / maxM : 0f });
        }
        else
        {
            l.Add(new Stat { label = "Su", value = $"%{Economy.VesselCapacity[t] * 100f:F0} kova", frac = Economy.VesselCapacity[t], ownedFrac = Economy.VesselCapacity[have] });
            l.Add(new Stat { label = "Cevher", value = $"{Economy.VesselOres[t]} adet", frac = Economy.VesselOres[t] / 5f, ownedFrac = Economy.VesselOres[have] / 5f });
        }
        return l;
    }
}
