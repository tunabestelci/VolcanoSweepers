using System.Collections.Generic;
using UnityEngine;

public class VolcanoGrid : MonoBehaviour
{
    public static VolcanoGrid I;

    public int size = 10;
    public float cellSize = 1.6f;
    public Material lavaMat;
    public Material obsidianMat;
    public Material clearedMat;
    public GameObject coreItemPrefab;

    public int ClearedCount { get; private set; }
    public int TotalCells => size * size;
    public float Progress => (float)ClearedCount / TotalCells;

    VolcanoCell[,] cells;
    readonly List<VolcanoCell> all = new List<VolcanoCell>();
    Material lavaInstance;

    void Awake()
    {
        I = this;
        lavaInstance = new Material(lavaMat);
        lavaMat = lavaInstance;
        cells = new VolcanoCell[size, size];
        float off = (size - 1) * cellSize * 0.5f;
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                var go = new GameObject($"Cell_{x}_{y}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(x * cellSize - off, 0f, y * cellSize - off);
                var c = go.AddComponent<VolcanoCell>();
                c.Init(this, x, y);
                cells[x, y] = c;
                all.Add(c);
            }
        }
    }

    void Update()
    {
        // Lav akisi: dokuyu kaydir, parlamayi nabiz gibi oynat
        lavaInstance.mainTextureOffset += new Vector2(0.012f, 0.007f) * Time.deltaTime;
        float pulse = 0.8f + 0.25f * Mathf.Sin(Time.time * 1.7f) + 0.1f * Mathf.Sin(Time.time * 4.3f);
        lavaInstance.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.08f) * pulse);
    }

    public VolcanoCell RandomCellInState(CellState state)
    {
        for (int i = 0; i < 12; i++)
        {
            var c = all[Random.Range(0, all.Count)];
            if (c.State == state) return c;
        }
        return null;
    }

    public void OnCellCleared()
    {
        ClearedCount++;
    }

    public void SpawnCore(Vector3 pos)
    {
        if (coreItemPrefab == null) return;
        var go = Instantiate(coreItemPrefab, pos, Random.rotation);
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null) rb.AddForce(new Vector3(Random.Range(-1.5f, 1.5f), 2.5f, Random.Range(-1.5f, 1.5f)), ForceMode.VelocityChange);
    }
}
