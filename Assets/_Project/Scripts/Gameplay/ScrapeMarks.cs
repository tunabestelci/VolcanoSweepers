using System.Collections.Generic;
using UnityEngine;

// Kasik/spatula izi: lav alaninin 20 cm'lik izgarasindan bagimsiz, 1.5 cm cozunurluklu "kazindi" maskesi.
// CPU'da yalnizca kazinan bolgeler (128x128 karolar) tutulur; oyuncunun etrafindaki 61 m'lik pencere GPU'ya
// kucuk karolar halinde kopyalanir ve LavaCrust shader'i bunu "ust tabakasi alinmis parlak obsidyen" olarak cizer.
public class ScrapeMarks : MonoBehaviour
{
    public static ScrapeMarks I;

    public const float Texel = 0.015f;          // metre / texel
    const int Tile = 128;                       // karo kenari (texel)
    const int WinTiles = 32;                    // GPU penceresi: 32 karo = 4096 texel = 61 m
    const int Win = Tile * WinTiles;

    static readonly int IdTex = Shader.PropertyToID("_ScrapeMarks");
    static readonly int IdRect = Shader.PropertyToID("_ScrapeRect");

    readonly Dictionary<int, byte[]> tiles = new Dictionary<int, byte[]>();
    readonly HashSet<int> dirty = new HashSet<int>();
    Vector2 origin;                             // dunya xz: global texel (0,0)
    int winTx = int.MinValue, winTy = int.MinValue;     // pencerenin sol-alt karosu
    RenderTexture rt;
    Texture2D[] staging;
    int stagingIdx;
    float[] tmp = new float[0];
    bool canCopy;

    public static ScrapeMarks Ensure()
    {
        if (I != null) return I;
        var go = new GameObject("ScrapeMarks");
        I = go.AddComponent<ScrapeMarks>();
        return I;
    }

    void Awake()
    {
        I = this;
        var f = LavaField.I;
        float size = f != null ? f.size : 206f;
        Vector3 c = f != null ? f.transform.position : Vector3.zero;
        origin = new Vector2(c.x - size * 0.5f, c.z - size * 0.5f);
        canCopy = (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.TextureToRT) != 0;
        rt = new RenderTexture(Win, Win, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm)
        {
            name = "ScrapeMarks", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false
        };
        rt.Create();
        ClearRT();
        staging = new Texture2D[8];
        for (int i = 0; i < staging.Length; i++)
            staging[i] = new Texture2D(Tile, Tile, TextureFormat.R8, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
        Shader.SetGlobalTexture(IdTex, rt);
        Shader.SetGlobalVector(IdRect, new Vector4(0f, 0f, 0f, 0f));     // pencere kurulana kadar kapali
    }

    void OnDestroy()
    {
        if (I == this) I = null;
        if (rt != null) rt.Release();
        if (staging != null) foreach (var s in staging) if (s != null) Destroy(s);
        Shader.SetGlobalVector(IdRect, Vector4.zero);
    }

    void ClearRT()
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = prev;
    }

    static int Key(int tx, int ty) { return ty * 4096 + tx; }

    byte[] GetTile(int tx, int ty, bool create)
    {
        int k = Key(tx, ty);
        if (tiles.TryGetValue(k, out var t)) return t;
        if (!create) return null;
        t = new byte[Tile * Tile];
        tiles[k] = t;
        return t;
    }

    int GX(float x) { return Mathf.FloorToInt((x - origin.x) / Texel); }
    int GY(float z) { return Mathf.FloorToInt((z - origin.y) / Texel); }
    Vector3 TexelCenter(int gx, int gy) { return new Vector3(origin.x + (gx + 0.5f) * Texel, 0f, origin.y + (gy + 0.5f) * Texel); }

    // 0..1: bu nokta ne kadar kazinmis
    public float At(Vector3 p)
    {
        int gx = GX(p.x), gy = GY(p.z);
        if (gx < 0 || gy < 0) return 0f;
        var t = GetTile(gx / Tile, gy / Tile, false);
        return t == null ? 0f : t[(gy % Tile) * Tile + gx % Tile] * (1f / 255f);
    }

    // Bicak ayak izini a->b yolu boyunca supurur. lateral: bicagin yatay ekseni, head: bicak normali (yatay).
    // Texel degeri = ustten kazinan derinlik (bayt, LavaField.FullBytes = 9 kasik katmani). Bicak bir texelin
    // uzerinden bir kez gectiginde (yol boyu ~ bicak boyu) 'passBytes' kadar derinlesir; ancak kirici ile
    // ufalanmis derinligi asamaz. Dondurulen: kazinan miktar (m^2 * kasik katmani).
    public float Stamp(Vector3 a, Vector3 b, Vector3 lateral, Vector3 head, float halfW, float halfL, float passBytes, LavaField field)
    {
        if (passBytes <= 0f || field == null) return 0f;
        lateral.y = 0f; head.y = 0f;
        lateral = lateral.sqrMagnitude > 1e-8f ? lateral.normalized : Vector3.right;
        head = head.sqrMagnitude > 1e-8f ? head.normalized : Vector3.forward;
        float reach = halfW + halfL + Texel * 2f;
        float minX = Mathf.Min(a.x, b.x) - reach, maxX = Mathf.Max(a.x, b.x) + reach;
        float minZ = Mathf.Min(a.z, b.z) - reach, maxZ = Mathf.Max(a.z, b.z) + reach;
        int x0 = Mathf.Max(0, GX(minX)), x1 = GX(maxX), y0 = Mathf.Max(0, GY(minZ)), y1 = GY(maxZ);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        if (w <= 0 || h <= 0 || w * h > 400000) return 0f;
        if (tmp.Length < w * h) tmp = new float[w * h];
        System.Array.Clear(tmp, 0, w * h);

        // ara damgalar: bicak boyunun %60'i araliklarla
        Vector3 ab = b - a; ab.y = 0f;
        int steps = Mathf.Clamp(Mathf.CeilToInt(ab.magnitude / Mathf.Max(0.004f, halfL * 1.2f)), 1, 64);
        float soft = Texel * 1.2f;
        for (int s = 0; s <= steps; s++)
        {
            Vector3 c = a + ab * (s / (float)steps);
            int sx0 = Mathf.Max(x0, GX(c.x - halfW - halfL - soft)), sx1 = Mathf.Min(x1, GX(c.x + halfW + halfL + soft));
            int sy0 = Mathf.Max(y0, GY(c.z - halfW - halfL - soft)), sy1 = Mathf.Min(y1, GY(c.z + halfW + halfL + soft));
            for (int gy = sy0; gy <= sy1; gy++)
                for (int gx = sx0; gx <= sx1; gx++)
                {
                    Vector3 q = TexelCenter(gx, gy) - c;
                    float l = Mathf.Abs(q.x * lateral.x + q.z * lateral.z);
                    float f = Mathf.Abs(q.x * head.x + q.z * head.z);
                    // stadyum (kasik ucu): yanal eksende uzun, uclari yuvarlak; kenarlarda yumusak gecis
                    float dl = Mathf.Max(0f, l - (halfW - halfL));
                    float dist = Mathf.Sqrt(dl * dl + f * f) - halfL;
                    float cov = Mathf.Clamp01(0.5f - dist / soft);
                    int idx = (gy - y0) * w + (gx - x0);
                    if (cov > tmp[idx]) tmp[idx] = cov;
                }
        }

        // bu karede bicagin kat ettigi yol / bicak boyu: bir texel uzerinden tam gecis = bir katman
        float moveFrac = Mathf.Clamp01(ab.magnitude / Mathf.Max(0.004f, 2f * halfL));
        float ratio = Texel * Texel / field.CellArea;
        float area = 0f, ta = Texel * Texel;
        for (int gy = y0; gy <= y1; gy++)
        {
            int ty = gy / Tile, ly = gy % Tile;
            for (int gx = x0; gx <= x1; gx++)
            {
                float cov = tmp[(gy - y0) * w + (gx - x0)];
                if (cov <= 0.001f) continue;
                Vector3 wp = TexelCenter(gx, gy);
                // yalnizca kirici ile ufalanmis derinlige kadar kazinir
                float limit = field.ScrapeLimitBytes(wp);
                int tx = gx / Tile;
                var t = GetTile(tx, ty, limit > 0.5f);
                if (t == null) continue;
                int li = ly * Tile + gx % Tile;
                int old = t[li];
                if (old >= limit - 0.5f) continue;
                float inc = passBytes * cov * moveFrac;
                int incI = Mathf.FloorToInt(inc + Random.value);
                if (incI <= 0) continue;
                int nv = Mathf.Min(Mathf.Min(255, Mathf.FloorToInt(limit + 0.5f)), old + incI);
                if (nv <= old) continue;
                t[li] = (byte)nv;
                area += (nv - old) / LavaField.LayerBytes * ta;
                dirty.Add(Key(tx, ty));
                field.AddCut(wp, old, nv, ratio);
            }
        }
        return area;
    }

    // Kazilmis derinlik (bayt)
    public float DepthAt(Vector3 p) { return At(p) * 255f; }

    // Su dokulunce yeni kabuk: yaricap icindeki kazima izi tamamen silinir
    public void Clear(Vector3 c, float radius)
    {
        if (tiles.Count == 0) return;
        int x0 = Mathf.Max(0, GX(c.x - radius)), x1 = GX(c.x + radius), y0 = Mathf.Max(0, GY(c.z - radius)), y1 = GY(c.z + radius);
        float r2 = radius * radius;
        for (int ty = y0 / Tile; ty <= y1 / Tile; ty++)
            for (int tx = x0 / Tile; tx <= x1 / Tile; tx++)
            {
                var t = GetTile(tx, ty, false);
                if (t == null) continue;
                bool any = false;
                int gy0 = Mathf.Max(y0, ty * Tile), gy1 = Mathf.Min(y1, ty * Tile + Tile - 1);
                int gx0 = Mathf.Max(x0, tx * Tile), gx1 = Mathf.Min(x1, tx * Tile + Tile - 1);
                for (int gy = gy0; gy <= gy1; gy++)
                    for (int gx = gx0; gx <= gx1; gx++)
                    {
                        Vector3 q = TexelCenter(gx, gy) - c;
                        if (q.x * q.x + q.z * q.z > r2) continue;
                        int li = (gy % Tile) * Tile + gx % Tile;
                        if (t[li] == 0) continue;
                        t[li] = 0; any = true;
                    }
                if (any) dirty.Add(Key(tx, ty));
            }
    }

    // (eski) Yeni kabuk kazinmis izi kademeli kapatir.
    public void Erase(Vector3 c, float radius, float amount)
    {
        if (tiles.Count == 0 || amount <= 0f) return;
        int x0 = Mathf.Max(0, GX(c.x - radius)), x1 = GX(c.x + radius), y0 = Mathf.Max(0, GY(c.z - radius)), y1 = GY(c.z + radius);
        int dec = Mathf.Clamp(Mathf.CeilToInt(amount * 255f * 2.5f), 1, 255);
        float r2 = radius * radius;
        for (int ty = y0 / Tile; ty <= y1 / Tile; ty++)
            for (int tx = x0 / Tile; tx <= x1 / Tile; tx++)
            {
                var t = GetTile(tx, ty, false);
                if (t == null) continue;
                bool any = false;
                int gy0 = Mathf.Max(y0, ty * Tile), gy1 = Mathf.Min(y1, ty * Tile + Tile - 1);
                int gx0 = Mathf.Max(x0, tx * Tile), gx1 = Mathf.Min(x1, tx * Tile + Tile - 1);
                for (int gy = gy0; gy <= gy1; gy++)
                    for (int gx = gx0; gx <= gx1; gx++)
                    {
                        Vector3 q = TexelCenter(gx, gy) - c;
                        float d2 = q.x * q.x + q.z * q.z;
                        if (d2 > r2) continue;
                        int li = (gy % Tile) * Tile + gx % Tile;
                        if (t[li] == 0) continue;
                        float k = 1f - d2 / r2;
                        t[li] = (byte)Mathf.Max(0, t[li] - Mathf.CeilToInt(dec * k));
                        any = true;
                    }
                if (any) dirty.Add(Key(tx, ty));
            }
    }

    // GPU penceresi oyuncuyu izler: kenara yaklasinca yeniden ortalanir ve dolu karolar yeniden yuklenir
    public void Focus(Vector3 p)
    {
        int ctx = GX(p.x) / Tile, cty = GY(p.z) / Tile;
        bool fresh = winTx == int.MinValue;
        if (!fresh)
        {
            int rx = ctx - winTx, ry = cty - winTy;
            if (rx >= 8 && rx < WinTiles - 8 && ry >= 8 && ry < WinTiles - 8) return;
        }
        winTx = ctx - WinTiles / 2; winTy = cty - WinTiles / 2;
        ClearRT();
        dirty.Clear();
        foreach (var k in tiles.Keys) dirty.Add(k);
        Shader.SetGlobalVector(IdRect, new Vector4(origin.x + winTx * Tile * Texel, origin.y + winTy * Tile * Texel, 1f / (Win * Texel), 1f / (Win * Texel)));
    }

    void LateUpdate()
    {
        if (dirty.Count == 0 || winTx == int.MinValue) return;
        foreach (var k in dirty)
        {
            int tx = k % 4096, ty = k / 4096;
            int lx = tx - winTx, ly = ty - winTy;
            if (lx < 0 || ly < 0 || lx >= WinTiles || ly >= WinTiles) continue;
            if (!tiles.TryGetValue(k, out var data)) continue;
            var st = staging[stagingIdx];
            stagingIdx = (stagingIdx + 1) % staging.Length;
            st.SetPixelData(data, 0);
            st.Apply(false);
            if (canCopy) Graphics.CopyTexture(st, 0, 0, 0, 0, Tile, Tile, rt, 0, 0, lx * Tile, ly * Tile);
            else
            {
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, Win, 0, Win);
                Graphics.DrawTexture(new Rect(lx * Tile, ly * Tile, Tile, Tile), st);
                GL.PopMatrix();
                RenderTexture.active = prev;
            }
        }
        dirty.Clear();
    }
}
