using UnityEngine;

// Uyuyan ejder: yavas nefes alir (govde ve omuzlar hafifce kabarir), bas neredeyse hareketsiz kalir
// (sadece nefesle cok az yukselir), kuyruk ucu ise cok yavas ve kucuk bir salinimla kipirdar.
// Mesh CPU'da bukulur (kucuk mesh; maliyeti onemsiz). Araba ve altindaki govde sabit kalir.
[RequireComponent(typeof(MeshFilter))]
public class DragonAnim : MonoBehaviour
{
    public Renderer cartRenderer;                 // araba (sabit nokta)
    public float swayAmount = 0.04f;             // radyan: sadece kuyruk ucundaki salinim
    public float breathAmount = 0.03f;            // govde nefes yuksekligi (m)
    public float headBreath = 0.012f;             // bas nefesle sadece bu kadar yukselir (m)
    public float speed = 1f;

    Mesh mesh;
    Vector3[] baseV, baseN, v, n;
    float[] tailW;                                // 0..1: kuyruk tarafinda arabadan uzaklastikca artar
    float[] bodyW;                                // 0..1: nefes agirligi (arabadan uzaklastikca azalir)
    float[] headW;                                // 0..1: bas bolgesi
    Vector3 anchor;
    float scaleInv = 1f;

    void Start()
    {
        var mf = GetComponent<MeshFilter>();
        var src = mf.sharedMesh;
        if (src == null) return;
        if (!src.isReadable) { Debug.LogWarning("[DragonAnim] mesh okunabilir degil: " + src.name); enabled = false; return; }
        mesh = Instantiate(src);
        mesh.name = src.name + "_Anim";
        mesh.MarkDynamic();
        mf.sharedMesh = mesh;
        baseV = mesh.vertices; baseN = mesh.normals;
        int cnt = baseV.Length;
        v = new Vector3[cnt]; n = new Vector3[cnt];
        anchor = cartRenderer != null ? transform.InverseTransformPoint(cartRenderer.bounds.center) : mesh.bounds.center;
        scaleInv = 1f / Mathf.Max(transform.lossyScale.x, 1e-4f);

        // Kuyruk yonu: arabadan en uzak tepe; bas ise tam ters tarafta
        int far = 0; float fd = 0f;
        for (int i = 0; i < cnt; i++)
        {
            float dx = baseV[i].x - anchor.x, dz = baseV[i].z - anchor.z;
            float d = dx * dx + dz * dz;
            if (d > fd) { fd = d; far = i; }
        }
        Vector2 tdir = new Vector2(baseV[far].x - anchor.x, baseV[far].z - anchor.z).normalized;
        float maxPos = 0f, maxNeg = 0f;
        var proj = new float[cnt];
        for (int i = 0; i < cnt; i++)
        {
            proj[i] = (baseV[i].x - anchor.x) * tdir.x + (baseV[i].z - anchor.z) * tdir.y;
            if (proj[i] > maxPos) maxPos = proj[i];
            if (-proj[i] > maxNeg) maxNeg = -proj[i];
        }
        tailW = new float[cnt]; bodyW = new float[cnt]; headW = new float[cnt];
        float tailStart = maxPos * 0.35f;
        for (int i = 0; i < cnt; i++)
        {
            float tw = Mathf.Clamp01((proj[i] - tailStart) / Mathf.Max(0.01f, maxPos - tailStart));
            tailW[i] = tw * tw * (3f - 2f * tw);
            float hw = Mathf.Clamp01((-proj[i] - maxNeg * 0.45f) / Mathf.Max(0.01f, maxNeg * 0.55f));
            headW[i] = hw * hw * (3f - 2f * hw);
            float dx = baseV[i].x - anchor.x, dz = baseV[i].z - anchor.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            bodyW[i] = 1f - Mathf.Clamp01((dist - 1.5f * scaleInv) / (4f * scaleInv));
        }
    }

    void Update()
    {
        if (mesh == null) return;
        float t = Time.time * speed;
        float breath = Mathf.Sin(t * 1.25f) * 0.5f + 0.5f;                 // 0..1, yavas ve duzgun nefes (~5 sn)
        float breath2 = Mathf.Sin(t * 1.25f - 0.5f) * 0.5f + 0.5f;         // omuzlar biraz gec kabarir
        for (int i = 0; i < baseV.Length; i++)
        {
            Vector3 p = baseV[i];
            float tw = tailW[i];
            float c = 1f, sn = 0f;
            if (tw > 0.001f)
            {
                // kuyruk ucu: cok yavas, kucuk ve yumusak salinim
                float ang = (Mathf.Sin(t * 0.55f) * swayAmount + Mathf.Sin(t * 0.93f + 1.3f) * swayAmount * 0.3f) * tw;
                c = Mathf.Cos(ang); sn = Mathf.Sin(ang);
                float dx = p.x - anchor.x, dz = p.z - anchor.z;
                p.x = anchor.x + dx * c - dz * sn;
                p.z = anchor.z + dx * sn + dz * c;
            }
            // nefes: govde kabarir; bas sadece cok hafif yukselir
            float lift = breath * breathAmount * bodyW[i] + breath2 * headBreath * headW[i];
            p.y += lift * scaleInv;
            v[i] = p;
            Vector3 nn = baseN[i];
            n[i] = new Vector3(nn.x * c - nn.z * sn, nn.y, nn.x * sn + nn.z * c);
        }
        mesh.vertices = v;
        mesh.normals = n;
        mesh.RecalculateBounds();
    }
}
