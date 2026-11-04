using System.Collections;
using UnityEngine;

public enum CellState { Lava, Cooling, Obsidian, Cleared }

public class VolcanoCell : MonoBehaviour
{
    public const int HitsToBreak = 3;

    public CellState State { get; private set; } = CellState.Lava;

    VolcanoGrid grid;
    GameObject lavaTop;
    GameObject obsidian;
    GameObject floor;
    int hits;
    float size;

    public void Init(VolcanoGrid g, int x, int y)
    {
        grid = g;
        size = g.cellSize;

        var box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(size, 1f, size);
        box.center = new Vector3(0f, 0.22f, 0f);

        lavaTop = Slab("Lava", g.lavaMat, 0.2f, -0.13f);
        lavaTop.transform.localRotation = Quaternion.Euler(0f, 90f * Random.Range(0, 4), 0f);
        floor = Slab("Floor", g.clearedMat, 0.2f, -0.13f);
        floor.SetActive(false);
        obsidian = Slab("Obsidian", g.obsidianMat, 0.5f, 0.22f);
        obsidian.SetActive(false);
    }

    GameObject Slab(string name, Material mat, float height, float y)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localScale = new Vector3(size * 0.99f, height, size * 0.99f);
        go.transform.localPosition = new Vector3(0f, y, 0f);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    public void Cool()
    {
        if (State != CellState.Lava) return;
        StartCoroutine(CoolRoutine());
    }

    IEnumerator CoolRoutine()
    {
        State = CellState.Cooling;
        Vector3 top = transform.position + Vector3.up * 0.1f;
        Fx.Steam(top, size * 0.6f);
        Fx.Splash(top);
        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.steamHiss : null, top, 1f);

        obsidian.SetActive(true);
        var t = obsidian.transform;
        float h = 0.5f;
        float endY = 0.22f;
        float startY = endY - h * 0.95f;
        const float dur = 1.4f;
        for (float e = 0f; e < dur; e += Time.deltaTime)
        {
            float k = e / dur;
            float eased = 1f - (1f - k) * (1f - k);
            t.localPosition = new Vector3(0f, Mathf.Lerp(startY, endY, eased), 0f);
            t.localScale = new Vector3(size * 0.99f, h, size * 0.99f);
            if (Random.value < 0.08f) Fx.Steam(top + Random.insideUnitSphere * size * 0.3f, size * 0.3f);
            yield return null;
        }
        t.localPosition = new Vector3(0f, endY, 0f);
        lavaTop.SetActive(false);
        State = CellState.Obsidian;
    }

    public void Hit(Vector3 point)
    {
        if (State != CellState.Obsidian) return;
        hits++;
        Fx.Chips(point);
        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.pickaxeHit : null, point, 1f);
        StartCoroutine(Shake());
        if (hits >= HitsToBreak) Break(point);
    }

    IEnumerator Shake()
    {
        var t = obsidian.transform;
        Vector3 basePos = new Vector3(0f, 0.22f, 0f);
        for (float e = 0f; e < 0.15f; e += Time.deltaTime)
        {
            t.localPosition = basePos + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * 0.03f;
            yield return null;
        }
        if (State == CellState.Obsidian) t.localPosition = basePos;
    }

    void Break(Vector3 point)
    {
        State = CellState.Cleared;
        obsidian.SetActive(false);
        floor.SetActive(true);
        Fx.Chips(point + Vector3.up * 0.2f, 24);
        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.obsidianBreak : null, point, 1f);
        grid.OnCellCleared();
        int cores = Random.Range(1, 3);
        for (int i = 0; i < cores; i++) grid.SpawnCore(transform.position + Vector3.up * 0.8f);
    }
}
