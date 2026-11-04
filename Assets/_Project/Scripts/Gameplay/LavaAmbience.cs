using System.Collections;
using UnityEngine;

// Lavin canli durmasi: kabarciklar, patlamalar, firlayan magma damlalari, isik titremesi
public class LavaAmbience : MonoBehaviour
{
    public VolcanoGrid grid;
    public Material bubbleMat;
    public GameObject magmaBallPrefab;
    public Light glowLight;
    public float bubblesPerSecond = 7f;
    public float spitEverySeconds = 3.5f;

    float bubbleTimer;
    float spitTimer;
    float baseIntensity;

    void Start()
    {
        if (glowLight != null) baseIntensity = glowLight.intensity;
    }

    void Update()
    {
        bubbleTimer -= Time.deltaTime;
        if (bubbleTimer <= 0f)
        {
            bubbleTimer = Random.Range(0.5f, 1.5f) / bubblesPerSecond;
            SpawnBubble();
        }

        spitTimer -= Time.deltaTime;
        if (spitTimer <= 0f)
        {
            spitTimer = Random.Range(0.5f, 1.5f) * spitEverySeconds;
            SpawnSpit();
        }

        if (glowLight != null)
            glowLight.intensity = baseIntensity * (1f + 0.15f * Mathf.Sin(Time.time * 3.1f) + 0.1f * Mathf.Sin(Time.time * 7.7f));
    }

    Vector3 RandomPointOn(VolcanoCell c)
    {
        Vector2 o = Random.insideUnitCircle * grid.cellSize * 0.4f;
        return c.transform.position + new Vector3(o.x, -0.03f, o.y);
    }

    void SpawnBubble()
    {
        var cell = grid.RandomCellInState(CellState.Lava);
        if (cell == null) return;
        StartCoroutine(BubbleRoutine(RandomPointOn(cell)));
    }

    IEnumerator BubbleRoutine(Vector3 pos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = bubbleMat;
        go.transform.position = pos;
        float r = Random.Range(0.08f, 0.26f);
        float grow = Random.Range(0.7f, 1.6f);
        for (float e = 0f; e < grow; e += Time.deltaTime)
        {
            float k = e / grow;
            float s = r * (k < 0.9f ? k : 0.9f + (k - 0.9f) * 1.2f) * (1f + 0.06f * Mathf.Sin(e * 25f));
            go.transform.localScale = new Vector3(s, s * 0.8f, s);
            go.transform.position = pos + Vector3.up * s * 0.15f;
            yield return null;
        }
        Vector3 popPos = go.transform.position;
        Destroy(go);
        Fx.Pop(popPos, r * 4f);
        if (SfxPlayer.I != null)
            SfxPlayer.Play(SfxPlayer.I.bubblePop, popPos, Random.Range(0.25f, 0.5f), Random.Range(0.8f, 1.3f), 0f);
    }

    void SpawnSpit()
    {
        if (magmaBallPrefab == null) return;
        var from = grid.RandomCellInState(CellState.Lava);
        var to = grid.RandomCellInState(CellState.Lava);
        if (from == null || to == null) return;
        StartCoroutine(SpitRoutine(RandomPointOn(from), RandomPointOn(to)));
    }

    IEnumerator SpitRoutine(Vector3 a, Vector3 b)
    {
        var go = Instantiate(magmaBallPrefab, a, Random.rotation);
        float dur = Random.Range(0.9f, 1.4f);
        float height = Random.Range(1.2f, 2.4f);
        float s = Random.Range(0.5f, 1f);
        go.transform.localScale = Vector3.one * s;
        Fx.Pop(a, 1.4f);
        for (float e = 0f; e < dur; e += Time.deltaTime)
        {
            float k = e / dur;
            Vector3 p = Vector3.Lerp(a, b, k);
            p.y += 4f * height * k * (1f - k);
            go.transform.position = p;
            go.transform.Rotate(180f * Time.deltaTime, 90f * Time.deltaTime, 0f);
            yield return null;
        }
        Vector3 end = go.transform.position;
        Destroy(go);
        Fx.Pop(end, 1.6f);
        if (SfxPlayer.I != null)
            SfxPlayer.Play(SfxPlayer.I.bubblePop, end, 0.5f, 0.6f, 0.1f);
    }
}
