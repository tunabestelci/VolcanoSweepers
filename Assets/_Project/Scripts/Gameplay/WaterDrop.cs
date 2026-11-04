using System.Collections.Generic;
using UnityEngine;

// Fizige uygun su damlasi: yercekimi + hava surtunmesi. Arkasinda su izi birakir, boylece damlalar
// birlesik bir akis gibi gorunur. Yuzeye carpinca sicrar; lavda kabuk olusturur ve buhar cikarir.
public class WaterDrop : MonoBehaviour
{
    static readonly Stack<WaterDrop> pool = new Stack<WaterDrop>();
    static GameObject prefab;
    static int alive;

    Vector3 vel;
    float size;
    float life;
    bool secondary;
    TrailRenderer trail;

    public static int Alive => alive;

    public static void Setup(GameObject dropPrefab)
    {
        prefab = dropPrefab;
        pool.Clear();
        alive = 0;
    }

    public static void Emit(Vector3 pos, Vector3 velocity, bool secondary = false)
    {
        if (prefab == null || alive > 1600) return;
        WaterDrop d;
        if (pool.Count > 0) { d = pool.Pop(); d.gameObject.SetActive(true); }
        else
        {
            var go = Instantiate(prefab);
            d = go.AddComponent<WaterDrop>();
            d.trail = go.GetComponent<TrailRenderer>();
        }
        alive++;
        d.secondary = secondary;
        d.transform.position = pos;
        d.vel = velocity;
        d.size = secondary ? Random.Range(0.015f, 0.03f) : Random.Range(0.028f, 0.05f);
        d.life = 0f;
        d.transform.localScale = Vector3.one * d.size;
        if (d.trail != null)
        {
            d.trail.Clear();
            d.trail.widthMultiplier = d.size * (secondary ? 0.9f : 1.2f);
            d.trail.time = secondary ? 0.08f : 0.16f;
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        life += dt;
        vel.y -= 9.81f * dt;
        vel *= 1f - 0.1f * dt;
        Vector3 next = transform.position + vel * dt;

        var field = LavaField.I;
        bool hit = false;
        if (field != null && next.y <= field.HeightAt(next))
        {
            float speed = vel.magnitude;
            Vector3 p = next;
            p.y = field.HeightAt(next);
            if (field.IsLava(next))
            {
                if (!secondary) field.AddCrust(next, 0.34f, 0.12f);
                if (Random.value < 0.06f) Fx.Steam(p, 0.3f);
            }
            else if (!secondary && Random.value < 0.2f) Fx.Splash(p);

            // Sicrama: carpan damla kucuk damlalara bolunup yukari-yana sekerler
            if (!secondary && speed > 2.2f)
            {
                int n = Random.Range(1, 4);
                for (int i = 0; i < n; i++)
                {
                    Vector2 side = Random.insideUnitCircle * (0.6f + speed * 0.12f);
                    Vector3 sv = new Vector3(side.x, 0.8f + Mathf.Abs(vel.y) * 0.22f + Random.value, side.y);
                    Emit(p + Vector3.up * 0.03f, sv, true);
                }
            }
            hit = true;
        }
        if (life > 4f) hit = true;

        if (hit) { Release(); return; }

        transform.position = next;
        if (vel.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(vel.normalized);
            float stretch = 1f + Mathf.Min(vel.magnitude * 0.1f, 1.2f);
            transform.localScale = new Vector3(size, size, size * stretch);
        }
    }

    void Release()
    {
        alive--;
        if (trail != null) trail.emitting = false;
        gameObject.SetActive(false);
        pool.Push(this);
    }

    void OnEnable()
    {
        if (trail != null) trail.emitting = true;
    }
}
