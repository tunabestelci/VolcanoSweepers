using UnityEngine;

public class Hud : MonoBehaviour
{
    public PlayerTools tools;
    public VolcanoGrid grid;

    GUIStyle label, big;

    void OnGUI()
    {
        if (tools == null || grid == null) return;
        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.024f) };
            label.normal.textColor = Color.white;
            big = new GUIStyle(label) { fontSize = Mathf.RoundToInt(Screen.height * 0.032f), alignment = TextAnchor.MiddleCenter };
        }

        float w = Screen.width, h = Screen.height;

        // Nisangah
        GUI.color = new Color(1f, 1f, 1f, 0.85f);
        GUI.DrawTexture(new Rect(w / 2f - 2f, h / 2f - 2f, 4f, 4f), Texture2D.whiteTexture);

        // Ilerleme
        float pad = h * 0.025f;
        GUI.color = Color.white;
        GUI.Label(new Rect(pad, pad, w * 0.5f, h * 0.05f), $"Volkan bosaltildi: %{Mathf.RoundToInt(grid.Progress * 100f)}", label);
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(new Rect(pad, pad + h * 0.05f, w * 0.25f, h * 0.014f), Texture2D.whiteTexture);
        GUI.color = new Color(1f, 0.5f, 0.1f, 1f);
        GUI.DrawTexture(new Rect(pad, pad + h * 0.05f, w * 0.25f * grid.Progress, h * 0.014f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(pad, pad + h * 0.075f, w * 0.5f, h * 0.05f), $"Litik cekirdek: {tools.CoreCount}", label);

        // Ipucu
        if (!string.IsNullOrEmpty(tools.Hint))
            GUI.Label(new Rect(0f, h * 0.62f, w, h * 0.06f), tools.Hint, big);

        // Kontroller
        string tool = tools.CurrentTool == Tool.Buckets ? "KOVALAR" : "KAZMA";
        GUI.Label(new Rect(pad, h - pad - h * 0.11f, w, h * 0.05f),
            $"[{tool}]  1: Kovalar  2: Kazma  |  Sol/Sag tik: kullan  |  E: cekirdek al  |  WASD, Shift", label);
    }
}
