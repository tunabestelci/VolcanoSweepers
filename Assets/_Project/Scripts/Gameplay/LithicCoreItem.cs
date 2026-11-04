using System.Collections.Generic;
using UnityEngine;

// Kazilan obsidyen parcasi: kendi basit fizigiyle yuzeye duser, E ile alinir.
public class LithicCoreItem : MonoBehaviour
{
    public static readonly List<LithicCoreItem> All = new List<LithicCoreItem>();

    public float radius = 0.1f;
    Vector3 vel;
    Vector3 spin;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public void Launch(Vector3 velocity)
    {
        vel = velocity;
        spin = Random.insideUnitSphere * 300f;
    }

    void Update()
    {
        var field = LavaField.I;
        float dt = Time.deltaTime;
        vel.y -= 9.81f * dt;
        Vector3 p = transform.position + vel * dt;
        float floor = (field != null ? field.HeightAt(p) : 0f) + radius * 0.6f;
        if (p.y <= floor)
        {
            p.y = floor;
            if (vel.y < -1.2f) vel = new Vector3(vel.x * 0.4f, -vel.y * 0.3f, vel.z * 0.4f);
            else { vel = Vector3.zero; spin = Vector3.zero; }
        }
        transform.position = p;
        transform.Rotate(spin * dt);
    }

    public static LithicCoreItem Nearest(Vector3 pos, float maxDist)
    {
        LithicCoreItem best = null;
        float bestD = maxDist;
        foreach (var c in All)
        {
            float d = Vector3.Distance(c.transform.position, pos);
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }
}
