using System.Collections.Generic;
using UnityEngine;

// Fizige uygun su damlasi: yercekimi + hava surtunmesi, yuzeye carpinca lavi sogutur.
public class WaterDrop : MonoBehaviour
{
    static readonly Stack<WaterDrop> pool = new Stack<WaterDrop>();
    static GameObject prefab;
    static int alive;

    Vector3 vel;
    float size;
    float life;

    public static int Alive => alive;

    public static void Setup(GameObject dropPrefab)
    {
        prefab = dropPrefab;
        pool.Clear();
        alive = 0;
    }

    public static void Emit(Vector3 pos, Vector3 velocity)
    {
        if (prefab == null || alive > 900) return;
        WaterDrop d;
        if (pool.Count > 0) { d = pool.Pop(); d.gameObject.SetActive(true); }
        else
        {
            var go = Instantiate(prefab);
            d = go.AddComponent<WaterDrop>();
        }
        alive++;
        d.transform.position = pos;
        d.vel = velocity;
        d.size = Random.Range(0.035f, 0.075f);
        d.life = 0f;
        d.transform.localScale = Vector3.one * d.size;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        life += dt;
        vel.y -= 9.81f * dt;
        vel *= 1f - 0.15f * dt;
        Vector3 next = transform.position + vel * dt;

        var field = LavaField.I;
        bool hit = false;
        if (field != null)
        {
            if (next.y <= field.HeightAt(next))
            {
                if (field.IsLava(next))
                {
                    field.AddCrust(next, 0.34f, 0.2f);
                    if (Random.value < 0.05f) Fx.Steam(next, 0.35f);
                }
                else if (Random.value < 0.15f) Fx.Splash(next);
                hit = true;
            }
        }
        if (life > 4f) hit = true;

        if (hit) { Release(); return; }

        transform.position = next;
        if (vel.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(vel.normalized);
            float stretch = 1f + Mathf.Min(vel.magnitude * 0.12f, 1.6f);
            transform.localScale = new Vector3(size, size, size * stretch);
        }
    }

    void Release()
    {
        alive--;
        gameObject.SetActive(false);
        pool.Push(this);
    }
}
