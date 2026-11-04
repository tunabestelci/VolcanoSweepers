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

    void Awake()
    {
        I = this;
        if (lavaAmbient == null) return;
        var src = gameObject.AddComponent<AudioSource>();
        src.clip = lavaAmbient;
        src.loop = true;
        src.volume = 0.35f;
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
}
