using System.Collections.Generic;
using UnityEngine;

// Yere birakilmis kova veya kazma: E ile geri alinir. Kova suyunu ve cevherlerini korur.
public class DroppedItem : MonoBehaviour
{
    public enum Kind { Bucket, Pickaxe }
    public static readonly List<DroppedItem> All = new List<DroppedItem>();

    public Kind kind;
    public float fill;          // kova suyu (0..1)
    public int ores;            // kovadaki cevher
    public Vector3 respawnPos;  // lava/ucuruma duserse geri konacagi yer

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Update()
    {
        if (transform.position.y < -0.6f)
        {
            transform.position = respawnPos;
            var rb = GetComponent<Rigidbody>();
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }
    }

    public static DroppedItem Nearest(Vector3 pos, float range)
    {
        DroppedItem best = null; float bd = range * range;
        foreach (var d in All)
        {
            if (d == null) continue;
            float s = (d.transform.position - pos).sqrMagnitude;
            if (s < bd) { bd = s; best = d; }
        }
        return best;
    }

    // Eldeki nesneyi dunyaya birak
    public static DroppedItem Create(GameObject item, Kind kind, Vector3 pos, Vector3 velocity, float fill, int ores)
    {
        item.transform.SetParent(null, true);
        item.transform.position = pos;
        item.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // Cakisma kutusu: gorsel sinirlardan
        var rends = item.GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds(item.transform.position, Vector3.zero);
        bool first = true;
        foreach (var r in rends)
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
        }
        var col = item.AddComponent<BoxCollider>();
        Vector3 ls = item.transform.lossyScale;
        col.center = item.transform.InverseTransformPoint(b.center);
        col.size = new Vector3(b.size.x / Mathf.Max(ls.x, 1e-4f), b.size.y / Mathf.Max(ls.y, 1e-4f), b.size.z / Mathf.Max(ls.z, 1e-4f));

        var rb = item.AddComponent<Rigidbody>();
        rb.mass = kind == Kind.Bucket ? 2f : 3f;
        rb.linearDamping = 0.2f;
        rb.angularDamping = 1.5f;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.linearVelocity = velocity;

        var d = item.AddComponent<DroppedItem>();
        d.kind = kind; d.fill = fill; d.ores = ores; d.respawnPos = pos + Vector3.up * 0.5f;
        return d;
    }
}
