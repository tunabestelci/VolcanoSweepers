using UnityEngine;

public class Hud : MonoBehaviour
{
    public PlayerTools tools;
    public LavaField field;

    GUIStyle label, big;

    void OnGUI()
    {
        if (tools == null || field == null) return;
        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.024f) };
            label.normal.textColor = Color.white;
            big = new GUIStyle(label) { fontSize = Mathf.RoundToInt(Screen.height * 0.032f), alignment = TextAnchor.MiddleCenter };
        }

        float w = Screen.width, h = Screen.height;

        // Yanma: kenarlardan kirmizi, olunce tam kirmizi/siyah ekran
        var pc = tools.player;
        if (pc != null && (pc.Burn > 0.01f || pc.DeathFade > 0.01f))
        {
            float a = Mathf.Max(pc.Burn * 0.55f, pc.DeathFade);
            GUI.color = new Color(pc.DeathFade > 0.01f ? 0.25f : 1f, pc.DeathFade > 0.01f ? 0f : 0.2f, 0f, a);
            GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            string msg = pc.DeathFade > 0.01f ? "Yandin!" : "LAV! Zipla / uzaklas!";
            GUI.Label(new Rect(0f, h * 0.4f, w, h * 0.08f), msg, big);
        }

        // Nisangah
        GUI.color = new Color(1f, 1f, 1f, 0.85f);
        GUI.DrawTexture(new Rect(w / 2f - 2f, h / 2f - 2f, 4f, 4f), Texture2D.whiteTexture);

        // Ilerleme
        float pad = h * 0.025f;
        GUI.color = Color.white;
        GUI.Label(new Rect(pad, pad, w * 0.5f, h * 0.05f), $"Volkan bosaltildi: %{Mathf.RoundToInt(field.Progress * 100f)}", label);
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(new Rect(pad, pad + h * 0.05f, w * 0.25f, h * 0.014f), Texture2D.whiteTexture);
        GUI.color = new Color(1f, 0.5f, 0.1f, 1f);
        GUI.DrawTexture(new Rect(pad, pad + h * 0.05f, w * 0.25f * field.Progress, h * 0.014f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(pad, pad + h * 0.075f, w * 0.5f, h * 0.05f), $"Kovadaki cevher: {tools.CoreCount}/{tools.CoreCapacity}  (kova basi {PlayerTools.OresPerBucket})", label);

        if (OreBin.I != null)
            GUI.Label(new Rect(pad, pad + h * 0.1f, w * 0.5f, h * 0.05f), $"Cevher deposu: {OreBin.I.Count}/{OreBin.I.capacity}", label);

        // Ipucu
        if (!string.IsNullOrEmpty(tools.Hint))
            GUI.Label(new Rect(0f, h * 0.62f, w, h * 0.06f), tools.Hint, big);

        // Kontroller
        string tool = tools.CurrentTool == Tool.Buckets ? "KOVALAR" : "KAZMA";
        GUI.Label(new Rect(pad, h - pad - h * 0.11f, w, h * 0.05f),
            $"[{tool}]  1: Kovalar (kovalar elindeyken 1/2: sol/sag kovayi birak)  3: Kazma (Q: birak)  |  Sol/Sag tik: kullan  |  E: yerden al / cevher bosalt / kuyuda basili tut: kova indir, doluyken bas: cek", label);
    }
}
