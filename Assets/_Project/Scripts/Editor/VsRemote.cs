using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Dosya tabanli kucuk uzaktan kumanda: <proje>/Temp/vs_cmd/<ad>.cmd dosyasina yazilan komutu calistirir,
// sonucu <ad>.out dosyasina yazar. (MCP baglantisi olmadan Build All / oynat / test icin)
//   menu:<menu yolu>   play   stop   save   refresh   flag:<anahtar>=<deger> (EditorPrefs)   log:<mesaj>
[InitializeOnLoad]
public static class VsRemote
{
    static readonly string Dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "vs_cmd");
    static double next;

    static VsRemote()
    {
        Directory.CreateDirectory(Dir);
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 0.6;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string[] files;
        try { files = Directory.GetFiles(Dir, "*.cmd").OrderBy(f => f).ToArray(); } catch { return; }
        if (files.Length == 0) return;
        string f = files[0];
        string txt;
        try { txt = File.ReadAllText(f).Trim(); File.Delete(f); } catch { return; }
        string res;
        try { res = Run(txt); }
        catch (Exception e) { res = "ERROR " + e; }
        File.WriteAllText(Path.ChangeExtension(f, ".out"), res ?? "ok");
    }

    static string Run(string cmd)
    {
        int i = cmd.IndexOf(':');
        string head = i < 0 ? cmd : cmd.Substring(0, i);
        string arg = i < 0 ? "" : cmd.Substring(i + 1);
        switch (head)
        {
            case "menu": return EditorApplication.ExecuteMenuItem(arg) ? "menu ok" : "menu not found";
            case "play": EditorApplication.isPlaying = true; return "play requested";
            case "stop": EditorApplication.isPlaying = false; return "stop requested";
            case "save": EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets(); return "saved";
            case "refresh": AssetDatabase.Refresh(); return "refreshed";
            case "flag":
                {
                    var kv = arg.Split(new[] { '=' }, 2);
                    EditorPrefs.SetString(kv[0], kv.Length > 1 ? kv[1] : "1");
                    return "flag set " + kv[0];
                }
            case "log": Debug.Log("[VsRemote] " + arg); return "logged";
        }
        return "unknown command: " + cmd;
    }
}
