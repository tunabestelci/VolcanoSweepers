using UnityEngine;
using UnityEngine.InputSystem;

// Lav Adam: yakina gelip ona bakinca E ile dukkan acilir. Dukkanda aletler ve kaplar sirayla satin alinir.
public class ShopKeeper : MonoBehaviour
{
    public static bool Open { get; private set; }
    public static int LastClose = -1;

    public PlayerTools tools;
    public PlayerController player;
    public Transform lookPoint;               // canavarin gogsu
    public float talkRange = 7f;

    GUIStyle title, label, small, btn, btnOff, hintStyle;
    string message = "";
    float messageUntil;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || player == null) return;
        if (Open)
        {
            if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame) Close();
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

    void Styles()
    {
        if (title != null) return;
        int fs = Mathf.RoundToInt(Screen.height * 0.026f);
        label = new GUIStyle(GUI.skin.label) { fontSize = fs, wordWrap = true };
        label.normal.textColor = Color.white;
        title = new GUIStyle(label) { fontSize = Mathf.RoundToInt(Screen.height * 0.04f), alignment = TextAnchor.MiddleCenter };
        title.normal.textColor = new Color(1f, 0.75f, 0.25f);
        small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(Screen.height * 0.02f) };
        small.normal.textColor = new Color(0.85f, 0.8f, 0.75f);
        btn = new GUIStyle(GUI.skin.button) { fontSize = fs, fontStyle = FontStyle.Bold };
        btnOff = new GUIStyle(btn);
        btnOff.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
        hintStyle = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Screen.height * 0.032f) };
        hintStyle.normal.textColor = new Color(1f, 0.9f, 0.6f);
    }

    void OnGUI()
    {
        var eco = Economy.I;
        if (eco == null) return;
        Styles();
        float w = Screen.width, h = Screen.height;

        if (!Open)
        {
            if (CanTalk() && Cursor.lockState == CursorLockMode.Locked)
                GUI.Label(new Rect(0, h * 0.55f, w, h * 0.06f), "E: Lav Adam'la konus", hintStyle);
            return;
        }

        GUI.color = new Color(0f, 0f, 0f, 0.62f);
        GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float pw = Mathf.Min(w * 0.78f, h * 1.5f), ph = h * 0.78f;
        var box = new Rect((w - pw) / 2f, (h - ph) / 2f, pw, ph);
        GUI.color = new Color(0.1f, 0.04f, 0.03f, 0.96f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = new Color(1f, 0.55f, 0.15f, 1f);
        GUI.DrawTexture(new Rect(box.x, box.y, box.width, 4f), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(box.x + pw * 0.04f, box.y + ph * 0.03f, pw * 0.92f, ph * 0.94f));
        GUILayout.Label("LAV ADAM'IN DUKKANI", title);
        GUILayout.Label($"Paran: {eco.money}", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold });
        GUILayout.Space(h * 0.008f);

        scroll = GUILayout.BeginScrollView(scroll, false, true, GUILayout.Height(ph * 0.72f));
        GUILayout.Label("KAZIYICILAR (fareyi surerek kazar)", small);
        for (int t = 1; t < Economy.ToolNames.Length; t++)
            Row(eco, Economy.ToolNames[t], Economy.ToolInfo[t], Economy.ToolPrice[t], eco.toolTier, t, () => eco.BuyTool());
        GUILayout.Label("Kurek: yakinda...", small);
        GUILayout.Space(h * 0.012f);
        GUILayout.Label("KIRICILAR (cekic, balyoz, matkap, kirici)", small);
        for (int t = 1; t <= Economy.BreakerCount; t++)
            Row(eco, $"{t}. {Economy.BreakerNames[t]}", Economy.BreakerInfo(t), Economy.BreakerPrice(t), eco.breakerTier, t, () => eco.BuyBreaker());
        GUILayout.Space(h * 0.012f);
        GUILayout.Label("KAPLAR (su tasima)", small);
        for (int t = 1; t < Economy.VesselNames.Length; t++)
            Row(eco, Economy.VesselNames[t], Economy.VesselInfo[t], Economy.VesselPrice[t], eco.vesselTier, t, () => eco.BuyVessel());
        GUILayout.EndScrollView();

        GUILayout.FlexibleSpace();
        if (Time.time < messageUntil) GUILayout.Label(message, hintStyle);
        GUILayout.Label("Fare tekerlegi: listeyi kaydir   |   E / Esc: kapat", small);
        GUILayout.EndArea();
    }

    Vector2 scroll;

    void Row(Economy eco, string name, string info, int price, int have, int tier, System.Func<bool> buy)
    {
        bool owned = have >= tier;
        bool unlocked = have == tier - 1;
        bool afford = eco.money >= price;

        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical();
        GUILayout.Label(name + (owned ? "  (sende)" : ""), label);
        GUILayout.Label(info, small);
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        string txt = owned ? "SAHIPSIN" : (unlocked ? $"Satin al  ({price})" : $"Kilitli  ({price})");
        bool can = !owned && unlocked && afford;
        GUI.enabled = can;
        if (GUILayout.Button(txt, can ? btn : btnOff, GUILayout.Width(Screen.width * 0.2f), GUILayout.Height(Screen.height * 0.06f)))
        {
            if (buy())
            {
                tools.RefreshLoadout();
                message = $"{name} satin alindi!";
                messageUntil = Time.time + 2.5f;
            }
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        if (!owned && unlocked && !afford)
            GUILayout.Label($"Biraz daha obsidyen kaz: {price - eco.money} para eksik", small);
        GUILayout.Space(Screen.height * 0.008f);
    }
}
