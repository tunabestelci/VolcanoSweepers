using UnityEngine;

// Ejderi canlandirir: nefes alir, govde dalgalanir, kuyruk ve bas yana savrulur. Mesh CPU'da bukulur
// (kamera etrafindaki kucuk mesh; maliyeti onemsiz). Dukkanin altina sabit kalan govde arabanin altinda durur.
[RequireComponent(typeof(MeshFilter))]
public class DragonAnim : MonoBehaviour
{
    public Renderer cartRenderer;                 // araba (sabit nokta)
    public float swayAmount = 0.16f;              // radyan: uclardaki savrulma
    public float breathAmount = 0.02f;            // govde nefes yuksekligi (m)
    public float speed = 1f;

    Mesh mesh;
    Vector3[] baseV, baseN, v, n;
    float[] dist;
    Vector3 anchor;
    float maxDist;

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
        v = new Vector3[baseV.Length]; n = new Vector3[baseV.Length];
        anchor = cartRenderer != null ? transform.InverseTransformPoint(cartRenderer.bounds.center) : mesh.bounds.center;
        dist = new float[baseV.Length];
        for (int i = 0; i < baseV.Length; i++)
        {
            float dx = baseV[i].x - anchor.x, dz = baseV[i].z - anchor.z;
            dist[i] = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist[i] > maxDist) maxDist = dist[i];
        }
        float s = transform.lossyScale.x;
        scaleInv = 1f / Mathf.Max(s, 1e-4f);
    }

    float scaleInv = 1f;

    void Update()
    {
        if (mesh == null) return;
        float t = Time.time * speed;
        float near = 1.6f * scaleInv, far = Mathf.Max(near + 1f, maxDist * 0.95f);
        float breath = Mathf.Sin(t * 1.5f);
        for (int i = 0; i < baseV.Length; i++)
        {
            Vector3 p = baseV[i];
            float d = dist[i];
            float w = Mathf.Clamp01((d - near) / (far - near));
            w = w * w * (3f - 2f * w);
            // iki dalga: yavas savrulma + daha hizli ince dalga (kuyruk ucunda)
            float ang = (Mathf.Sin(t * 1.1f - d * 0.9f / scaleInv) * swayAmount + Mathf.Sin(t * 2.3f - d * 1.6f / scaleInv) * swayAmount * 0.35f) * w;
            float c = Mathf.Cos(ang), sn = Mathf.Sin(ang);
            float dx = p.x - anchor.x, dz = p.z - anchor.z;
            p.x = anchor.x + dx * c - dz * sn;
            p.z = anchor.z + dx * sn + dz * c;
            // nefes: govde hafif kabarir, uclar hafif sallanir
            p.y += (breath * breathAmount * (1f - w * 0.6f) + Mathf.Sin(t * 1.5f + d * 0.7f / scaleInv) * 0.05f * w) * scaleInv;
            v[i] = p;
            Vector3 nn = baseN[i];
            n[i] = new Vector3(nn.x * c - nn.z * sn, nn.y, nn.x * sn + nn.z * c);
        }
        mesh.vertices = v;
        mesh.normals = n;
        mesh.RecalculateBounds();
    }
}
