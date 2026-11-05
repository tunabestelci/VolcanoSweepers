using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Cevher deposu: cevherler kovadan tek tek atilir, ucarak hazneye dusup yerlesir (500 cevhere kadar).
public class OreBin : MonoBehaviour
{
    public static OreBin I;
    public int capacity = 500;
    public Collider hitCollider;
    public Transform pile;                // eski yigin (kullanilmaz)
    public Vector3 pileFullSize = new Vector3(3.2f, 0.7f, 0.9f);   // haznenin ic olculeri (yerel)
    public Vector3 pileBaseLocal;         // hazne tabani (yerel)
    public GameObject orePrefab;          // tek bir cevher gorseli

    const float PieceSize = 0.1f;

    public int Count { get; private set; }        // yerlesmis cevher
    int reserved;                                  // yolda olanlar dahil
    int cols, rows, perLayer;
    int[] order;
    readonly List<Transform> pieces = new List<Transform>();

    void Awake()
    {
        I = this;
        if (pile != null) pile.gameObject.SetActive(false);
        cols = Mathf.Max(1, Mathf.FloorToInt(pileFullSize.x / PieceSize));
        rows = Mathf.Max(1, Mathf.FloorToInt(pileFullSize.z / PieceSize));
        perLayer = cols * rows;
        // her katmanda dogal gorunum icin karisik dolum sirasi
        order = new int[perLayer];
        for (int i = 0; i < perLayer; i++) order[i] = i;
        var rnd = new System.Random(7);
        for (int i = perLayer - 1; i > 0; i--) { int j = rnd.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
    }

    public bool LooksAt(Ray ray, float maxDist)
    {
        return hitCollider != null && hitCollider.Raycast(ray, out _, maxDist);
    }

    public bool Full => reserved >= capacity;

    // Bir cevheri 'from' dunya konumundan hazneye firlat; inince sayac artar. Kabul edilirse true.
    public bool Toss(Vector3 from)
    {
        if (reserved >= capacity) return false;
        int slot = reserved++;
        StartCoroutine(Fly(from, slot));
        return true;
    }

    Vector3 SlotLocal(int slot)
    {
        int layer = slot / perLayer;
        int idx = order[slot % perLayer];
        int cx = idx % cols, cz = idx / cols;
        var r = new System.Random(slot * 31 + 5);
        float x = (cx + 0.5f - cols * 0.5f) * PieceSize + ((float)r.NextDouble() - 0.5f) * 0.02f;
        float z = (cz + 0.5f - rows * 0.5f) * PieceSize + ((float)r.NextDouble() - 0.5f) * 0.02f;
        float y = pileBaseLocal.y + PieceSize * 0.5f + layer * PieceSize * 0.8f + ((float)r.NextDouble()) * 0.015f;
        return new Vector3(x, y, z);
    }

    IEnumerator Fly(Vector3 from, int slot)
    {
        Transform o = null;
        if (orePrefab != null)
        {
            o = Instantiate(orePrefab, transform).transform;
            foreach (var c in o.GetComponentsInChildren<Collider>()) Destroy(c);
            var rends = o.GetComponentsInChildren<Renderer>();
            float size = 0.0001f;
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var rr in rends) b.Encapsulate(rr.bounds);
                size = Mathf.Max(b.size.x, b.size.y, b.size.z, 0.0001f);
            }
            float k = (PieceSize * 1.05f / size);
            o.localScale = o.localScale * k;
            o.position = from;
            o.rotation = Random.rotation;
            pieces.Add(o);
        }
        Vector3 to = transform.TransformPoint(SlotLocal(slot));
        float dur = 0.42f;
        Quaternion spin = Random.rotation;
        Quaternion r0 = o != null ? o.rotation : Quaternion.identity;
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            float u = t / dur;
            if (o != null)
            {
                Vector3 p = Vector3.Lerp(from, to, u);
                p.y += Mathf.Sin(u * Mathf.PI) * 0.35f;       // kisa yay
                o.position = p;
                o.rotation = Quaternion.Slerp(r0, spin, u);
            }
            yield return null;
        }
        if (o != null)
        {
            o.position = to;
            o.rotation = Quaternion.Euler(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
        }
        Count++;
    }
}
