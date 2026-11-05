using UnityEngine;

public class SfxPlayer : MonoBehaviour
{
    public static SfxPlayer I;

    public AudioClip steamHiss;
    public AudioClip waterPour;
    public AudioClip waterFill;
    public AudioClip bubblePop;
    public AudioClip pickaxeHit;
    public AudioClip obsidianBreak;
    public AudioClip pickup;
    public AudioClip lavaAmbient;
    public AudioClip lavaSizzle;      // su lava dusunce (kaynayan balcik/lav sesi)
    public AudioClip wellPump;        // kuyudan su cekme (pompa)
    public AudioClip bucketPour;      // kuyu kovasindan el kovasina su aktarma

    void Awake()
    {
        I = this;
        if (lavaAmbient == null) return;
        var src = gameObject.AddComponent<AudioSource>();
        src.clip = lavaAmbient;
        src.loop = true;
        src.volume = 0.55f;
        src.spatialBlend = 0f;
        src.Play();
    }

    public static void Play(AudioClip clip, Vector3 pos, float volume = 1f, float pitch = 1f, float pitchVariation = 0.08f)
    {
        if (clip == null) return;
        var go = new GameObject("Sfx_" + clip.name);
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = volume;
        src.pitch = pitch + Random.Range(-pitchVariation, pitchVariation);
        src.spatialBlend = 1f;
        src.minDistance = 2f;
        src.maxDistance = 35f;
        src.Play();
        Destroy(go, clip.length / Mathf.Max(0.1f, src.pitch) + 0.1f);
    }

    // Uzun bir kayittan rastgele bir parca cal; yumusak girip cikar (parcalar ust uste binince surekli ses olur)
    public static void PlaySnippet(AudioClip clip, Vector3 pos, float duration, float volume, float start = -1f)
    {
        if (clip == null) return;
        duration = Mathf.Min(duration, clip.length);
        var go = new GameObject("Snippet_" + clip.name);
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.spatialBlend = 1f;
        src.minDistance = 3f;
        src.maxDistance = 45f;
        src.volume = 0f;
        src.time = start >= 0f ? Mathf.Min(start, Mathf.Max(0f, clip.length - duration)) : Random.Range(0f, Mathf.Max(0f, clip.length - duration));
        src.Play();
        var f = go.AddComponent<SnippetFade>();
        f.src = src; f.duration = duration; f.volume = volume;
    }
}

public class SnippetFade : MonoBehaviour
{
    public AudioSource src;
    public float duration, volume;
    float t;

    void Update()
    {
        t += Time.deltaTime;
        float fade = Mathf.Min(1f, t / 0.25f) * Mathf.Min(1f, (duration - t) / 0.4f);
        src.volume = volume * Mathf.Clamp01(fade);
        if (t >= duration) Destroy(gameObject);
    }
}
