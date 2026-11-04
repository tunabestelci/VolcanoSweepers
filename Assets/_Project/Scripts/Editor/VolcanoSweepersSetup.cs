using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Menu: VolcanoSweepers/Build All  -> materyaller, prefablar ve sahneyi kurar.
public static class VolcanoSweepersSetup
{
    const string Art = "Assets/_Project/Art";
    const string Mats = Art + "/Materials";
    const string Prefabs = "Assets/_Project/Prefabs";
    const string SceneDir = "Assets/_Project/Scenes";
    const string ScenePath = SceneDir + "/Volcano.unity";
    const string Sfx = "Assets/_Project/Audio/SFX/";

    // Modellerin eksen duzeltmeleri (Blender/ZBrush cikislari)
    static readonly Vector3 BucketEuler = new Vector3(-90f, 0f, 0f);
    static readonly Vector3 PickaxeEuler = new Vector3(-90f, 0f, 0f);
    static readonly Vector3 CoreEuler = new Vector3(0f, 0f, 0f);
    static readonly Vector3 BallEuler = new Vector3(0f, 0f, 0f);

    [MenuItem("VolcanoSweepers/Build All")]
    public static void BuildAll()
    {
        AssetDatabase.Refresh();
        FixTextureImport();
        Directory.CreateDirectory(Mats);
        Directory.CreateDirectory(Prefabs);
        Directory.CreateDirectory(SceneDir);

        var m = BuildMaterials();
        BuildPrefabs(m);
        BuildScene(m);
        AssetDatabase.SaveAssets();
        Debug.Log("[VolcanoSweepers] Build All tamamlandi.");
    }

    // ---------- Texture import ayarlari ----------

    static void FixTextureImport()
    {
        string[] normals =
        {
            Art + "/Textures/Bucket/Bucket_Normal.png",
            Art + "/Textures/Pickaxe/Material.001_Normal_OpenGL.png",
            Art + "/Textures/LithicCore/LithicCore_Normal.png",
            Art + "/Textures/ObsidianTile/Obsidian_normal.png",
            Art + "/Terrain/Volcano_Normal.png",
        };
        string[] linear =
        {
            Art + "/Textures/Bucket/Bucket_MetalSmooth.png",
            Art + "/Textures/Pickaxe/Pickaxe_MetalSmooth.png",
            Art + "/Textures/ObsidianTile/Obsidian_MetalSmooth.png",
            Art + "/Textures/LithicCore/LithicCore_Metallic.png",
        };
        foreach (var p in normals) Reimport(p, ti => ti.textureType = TextureImporterType.NormalMap);
        foreach (var p in linear) Reimport(p, ti => ti.sRGBTexture = false);
        Reimport(Art + "/Textures/FX/SoftCircle.png", ti => { ti.alphaIsTransparency = true; ti.mipmapEnabled = false; });
    }

    static void Reimport(string path, System.Action<TextureImporter> edit)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning("Texture bulunamadi: " + path); return; }
        edit(ti);
        ti.maxTextureSize = 2048;
        ti.SaveAndReimport();
    }

    // ---------- Materyaller ----------

    class MatSet
    {
        public Material lava, obsidian, cleared, bubble, bucket, pickaxe, core, magma, water, rock, particle, pondRim, waterDrop, lavaCrust;
    }

    static Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

    static Material Lit(string name, Color color, string baseTex = null, string normal = null, string metalSmooth = null,
        float smooth = 0.4f, float metal = 0f, string emissionTex = null, Color? emission = null)
    {
        string path = $"{Mats}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetTexture("_BaseMap", baseTex != null ? Tex(baseTex) : null);
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", Tex(normal));
            mat.EnableKeyword("_NORMALMAP");
        }
        if (metalSmooth != null)
        {
            mat.SetTexture("_MetallicGlossMap", Tex(metalSmooth));
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Metallic", 1f);
            mat.SetFloat("_Smoothness", smooth);
        }
        else
        {
            mat.SetFloat("_Metallic", metal);
            mat.SetFloat("_Smoothness", smooth);
        }
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
            if (emissionTex != null) mat.SetTexture("_EmissionMap", Tex(emissionTex));
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static MatSet BuildMaterials()
    {
        var m = new MatSet();
        string T = Art + "/Textures/";
        m.lava = Lit("Lava", Color.white, T + "MagmaBall/Lava_Tile.png", smooth: 0.25f,
            emissionTex: T + "MagmaBall/Lava_Tile.png", emission: new Color(0.75f, 0.28f, 0.05f));
        m.magma = Lit("MagmaBall", Color.white, T + "MagmaBall/MagmaBall_Color.png", smooth: 0.3f,
            emissionTex: T + "MagmaBall/MagmaBall_Color.png", emission: new Color(1.5f, 0.6f, 0.15f));
        m.bubble = Lit("LavaBubble", new Color(1f, 0.45f, 0.08f), smooth: 0.7f, emission: new Color(2.2f, 0.8f, 0.15f));
        m.obsidian = Lit("Obsidian", Color.white, T + "ObsidianTile/Obsidian_basecolor.png", T + "ObsidianTile/Obsidian_normal.png",
            T + "ObsidianTile/Obsidian_MetalSmooth.png", smooth: 1f);
        m.cleared = Lit("ClearedFloor", new Color(0.09f, 0.08f, 0.08f), smooth: 0.15f);
        m.rock = Lit("Rock", new Color(0.13f, 0.1f, 0.09f), smooth: 0.1f);
        m.pondRim = Lit("PondRim", new Color(0.2f, 0.17f, 0.16f), smooth: 0.15f);
        m.water = Lit("Water", new Color(0.1f, 0.45f, 0.85f), smooth: 0.95f);
        m.bucket = Lit("Bucket", Color.white, T + "Bucket/Bucket_Color.png", T + "Bucket/Bucket_Normal.png",
            T + "Bucket/Bucket_MetalSmooth.png", smooth: 1f);
        m.pickaxe = Lit("Pickaxe", Color.white, T + "Pickaxe/Material.001_Base_Color.png", T + "Pickaxe/Material.001_Normal_OpenGL.png",
            T + "Pickaxe/Pickaxe_MetalSmooth.png", smooth: 1f, emissionTex: T + "Pickaxe/Material.001_Emissive.png", emission: Color.white);
        m.core = Lit("LithicCore", Color.white, T + "LithicCore/LithicCore_Color.png", T + "LithicCore/LithicCore_Normal.png",
            T + "LithicCore/LithicCore_Metallic.png", smooth: 0.7f);

        // Surekli lav/obsidyen yuzeyi (ozel shader)
        string lcPath = Mats + "/LavaCrust.mat";
        var lc = AssetDatabase.LoadAssetAtPath<Material>(lcPath);
        if (lc == null)
        {
            lc = new Material(Shader.Find("VolcanoSweepers/LavaCrust"));
            AssetDatabase.CreateAsset(lc, lcPath);
        }
        lc.shader = Shader.Find("VolcanoSweepers/LavaCrust");
        lc.SetTexture("_LavaMap", Tex(T + "MagmaBall/Lava_Tile.png"));
        lc.SetTexture("_ObsMap", Tex(T + "ObsidianTile/Obsidian_basecolor.png"));
        lc.SetTexture("_ObsNormal", Tex(T + "ObsidianTile/Obsidian_normal.png"));
        lc.SetTexture("_ObsMS", Tex(T + "ObsidianTile/Obsidian_MetalSmooth.png"));
        EditorUtility.SetDirty(lc);
        m.lavaCrust = lc;

        // Su damlasi: seffaf
        string wdPath = Mats + "/WaterDrop.mat";
        var wd = AssetDatabase.LoadAssetAtPath<Material>(wdPath);
        if (wd == null)
        {
            wd = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(wd, wdPath);
        }
        wd.SetTexture("_BaseMap", Tex(T + "Water/Water_Base.png"));
        wd.SetColor("_BaseColor", new Color(0.65f, 0.85f, 1f, 0.6f));
        wd.SetFloat("_Smoothness", 0.95f);
        wd.SetFloat("_Metallic", 0f);
        wd.SetFloat("_Surface", 1f);
        wd.SetFloat("_Blend", 0f);
        wd.SetFloat("_SrcBlend", 5f);
        wd.SetFloat("_DstBlend", 10f);
        wd.SetFloat("_ZWrite", 0f);
        wd.SetOverrideTag("RenderType", "Transparent");
        wd.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        wd.renderQueue = 3000;
        EditorUtility.SetDirty(wd);
        m.waterDrop = wd;

        string pPath = Mats + "/Particle.mat";
        var pm = AssetDatabase.LoadAssetAtPath<Material>(pPath);
        if (pm == null)
        {
            pm = new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(pm, pPath);
        }
        pm.mainTexture = Tex(T + "FX/SoftCircle.png");
        EditorUtility.SetDirty(pm);
        m.particle = pm;
        return m;
    }

    // ---------- Prefablar ----------

    static GameObject Wrap(string name, string modelPath, float targetSize, Vector3 euler, Material mat, out Bounds finalBounds)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var root = new GameObject(name);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
        model.name = "Model";
        model.transform.localRotation = Quaternion.Euler(euler);
        model.transform.localPosition = Vector3.zero;
        model.transform.localScale = Vector3.one;

        var rends = model.GetComponentsInChildren<Renderer>();
        foreach (var r in rends) r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();

        Bounds b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        float scale = targetSize / Mathf.Max(b.size.x, b.size.y, b.size.z);
        model.transform.localScale = Vector3.one * scale;
        model.transform.localPosition = -b.center * scale;
        finalBounds = new Bounds(Vector3.zero, b.size * scale);
        return root;
    }

    static GameObject Save(GameObject root, string name)
    {
        var p = $"{Prefabs}/{name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, p);
        Object.DestroyImmediate(root);
        return prefab;
    }

    class PrefabSet
    {
        public GameObject bucket, pickaxe, coreItem, coreVisual, magma, waterDrop;
    }

    static PrefabSet BuildPrefabs(MatSet m)
    {
        var ps = new PrefabSet();

        // Kova + su gorseli
        var bucket = Wrap("Bucket", Art + "/Models/Bucket/Bucket.fbx", 0.34f, BucketEuler, m.bucket, out var bb);
        var water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        water.name = "Water";
        Object.DestroyImmediate(water.GetComponent<Collider>());
        water.GetComponent<MeshRenderer>().sharedMaterial = m.water;
        water.transform.SetParent(bucket.transform, false);
        float dia = Mathf.Min(bb.size.x, bb.size.z) * 0.82f;
        water.transform.localScale = new Vector3(dia, 0.01f, dia);
        var bw = water.AddComponent<BucketWater>();
        bw.emptyY = -bb.extents.y + 0.04f;
        bw.fullY = bb.extents.y - 0.035f;
        water.transform.localPosition = new Vector3(0f, bw.emptyY, 0f);
        ps.bucket = Save(bucket, "Bucket");

        // Kazma
        var pick = Wrap("Pickaxe", Art + "/Models/Pickaxe/Pickaxe.fbx", 0.62f, PickaxeEuler, m.pickaxe, out _);
        ps.pickaxe = Save(pick, "Pickaxe");

        // Litik cekirdek (yerdeki, alinabilir)
        var coreItem = Wrap("LithicCoreItem", Art + "/Models/LithicCore/LithicCore.obj", 0.28f, CoreEuler, m.core, out _);
        coreItem.AddComponent<LithicCoreItem>().radius = 0.12f;
        ps.coreItem = Save(coreItem, "LithicCoreItem");

        // Eldeki gorsel
        var coreVis = Wrap("LithicCoreVisual", Art + "/Models/LithicCore/LithicCore.obj", 0.2f, CoreEuler, m.core, out _);
        ps.coreVisual = Save(coreVis, "LithicCoreVisual");

        // Lav damlasi
        var magma = Wrap("MagmaBall", Art + "/Models/MagmaBall/MagmaBall.fbx", 0.35f, BallEuler, m.magma, out _);
        ps.magma = Save(magma, "MagmaBall");

        // Su damlasi (test.zip -> lan.glb)
        // glb icindeki mesh'leri dogrudan kullan (ic ice prefab olusturma)
        var drop = new GameObject("WaterDrop");
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(Art + "/Models/Water/WaterDrop.glb"))
        {
            var mesh = obj as Mesh;
            if (mesh == null) continue;
            var part = new GameObject(mesh.name);
            part.transform.SetParent(drop.transform, false);
            part.transform.localScale = Vector3.one * 0.5f;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = m.waterDrop;
        }
        ps.waterDrop = Save(drop, "WaterDrop");
        return ps;
    }

    // ---------- Sahne ----------

    static AudioClip Clip(string n) => AssetDatabase.LoadAssetAtPath<AudioClip>(Sfx + n + ".wav");

    static GameObject Cube(string name, Vector3 pos, Vector3 scale, Material mat, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static void BuildScene(MatSet m)
    {
        var prefabs = new PrefabSet
        {
            bucket = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/Bucket.prefab"),
            pickaxe = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/Pickaxe.prefab"),
            coreItem = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/LithicCoreItem.prefab"),
            coreVisual = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/LithicCoreVisual.prefab"),
            magma = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/MagmaBall.prefab"),
            waterDrop = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/WaterDrop.prefab"),
        };

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Atmosfer
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.22f, 0.12f, 0.1f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.16f, 0.06f, 0.04f);
        RenderSettings.fogDensity = 0.006f;

        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.7f, 0.55f);
        sun.intensity = 0.7f;
        sunGo.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

        // Volkan arazisi (kullanicinin yukseklik haritasi)
        var env = new GameObject("Environment").transform;
        BuildTerrain(m, env);

        // Havuz
        var pond = new GameObject("Pond");
        pond.transform.position = new Vector3(0f, 0f, -15f);
        var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rim.name = "PondRim";
        Object.DestroyImmediate(rim.GetComponent<Collider>());
        rim.transform.SetParent(pond.transform, false);
        rim.transform.localScale = new Vector3(5.6f, 0.06f, 5.6f);
        rim.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        rim.GetComponent<MeshRenderer>().sharedMaterial = m.pondRim;
        var pw = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pw.name = "PondWater";
        pw.transform.SetParent(pond.transform, false);
        pw.transform.localScale = new Vector3(4.8f, 0.04f, 4.8f);
        pw.transform.localPosition = new Vector3(0f, 0.07f, 0f);
        pw.GetComponent<MeshRenderer>().sharedMaterial = m.water;
        pw.GetComponent<Collider>().isTrigger = true;
        pw.AddComponent<WaterSource>();

        // Lav havuzu (surekli yuzey, kare yok)
        var lavaGo = new GameObject("LavaField");
        lavaGo.transform.position = new Vector3(0f, -0.04f, 0f);
        lavaGo.AddComponent<MeshFilter>();
        lavaGo.AddComponent<MeshRenderer>();
        var field = lavaGo.AddComponent<LavaField>();
        field.material = m.lavaCrust;
        field.size = 20f;
        field.poolRadius = 9f;
        field.digDepth = 3f;

        var glowGo = new GameObject("LavaGlow");
        glowGo.transform.position = new Vector3(0f, 2.5f, 0f);
        var glow = glowGo.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.45f, 0.12f);
        glow.range = 26f;
        glow.intensity = 60f;

        var amb = lavaGo.AddComponent<LavaAmbience>();
        amb.field = field;
        amb.bubbleMat = m.bubble;
        amb.magmaBallPrefab = prefabs.magma;
        amb.glowLight = glow;

        // Ses ve efekt
        var sys = new GameObject("Systems");
        var fx = sys.AddComponent<FxConfig>();
        fx.particleMaterial = m.particle;
        fx.waterDropPrefab = prefabs.waterDrop;
        var sfx = sys.AddComponent<SfxPlayer>();
        sfx.steamHiss = Clip("steam_hiss");
        sfx.waterPour = Clip("water_pour");
        sfx.waterFill = Clip("water_fill");
        sfx.bubblePop = Clip("bubble_pop");
        sfx.pickaxeHit = Clip("pickaxe_hit");
        sfx.obsidianBreak = Clip("obsidian_break");
        sfx.pickup = Clip("pickup");
        sfx.lavaAmbient = Clip("lava_ambient");

        // Oyuncu
        var player = new GameObject("Player");
        player.tag = "Player";
        player.transform.position = new Vector3(0f, 0.05f, -12f);
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.stepOffset = 0.35f;

        var camGo = new GameObject("PlayerCamera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(player.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 70f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 150f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.06f, 0.04f);
        camGo.AddComponent<AudioListener>();

        var lRig = new GameObject("LeftHandRig").transform;
        lRig.SetParent(player.transform, false);
        lRig.localPosition = new Vector3(0f, 1.65f, 0f);
        var lh = new GameObject("LeftHand").transform;
        lh.SetParent(lRig, false);
        var rRig = new GameObject("RightHandRig").transform;
        rRig.SetParent(player.transform, false);
        rRig.localPosition = new Vector3(0f, 1.65f, 0f);
        var rh = new GameObject("RightHand").transform;
        rh.SetParent(rRig, false);

        var pc = player.AddComponent<PlayerController>();
        pc.cam = cam;
        var tools = player.AddComponent<PlayerTools>();
        tools.player = pc;
        tools.cam = cam;
        tools.leftHandRoot = lh;
        tools.rightHandRoot = rh;
        tools.bucketPrefab = prefabs.bucket;
        tools.pickaxePrefab = prefabs.pickaxe;
        tools.coreVisualPrefab = prefabs.coreVisual;
        tools.coreItemPrefab = prefabs.coreItem;

        var hud = sys.AddComponent<Hud>();
        hud.tools = tools;
        hud.field = field;

        var bot = sys.AddComponent<PlaytestBot>();
        bot.player = pc;
        bot.tools = tools;
        bot.field = field;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    // Test icin Windows derlemesi: VolcanoSweepers/Build Test Player
    [MenuItem("VolcanoSweepers/Build Test Player")]
    public static void BuildTestPlayer()
    {
        string outDir = System.Environment.GetEnvironmentVariable("VS_PLAYER_DIR");
        if (string.IsNullOrEmpty(outDir)) outDir = "Builds/Test";
        Directory.CreateDirectory(outDir);
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, Path.Combine(outDir, "VolcanoSweepers.exe"),
            BuildTarget.StandaloneWindows64, BuildOptions.None);
        Debug.Log("[VolcanoSweepers] Player build: " + report.summary.result);
    }

    // ---------- Arazi ----------

    const string TerrainDir = Art + "/Terrain";

    static void BuildTerrain(MatSet m, Transform parent)
    {
        // RAW: 1009x1009, 16 bit, little-endian
        const int src = 1009;
        byte[] raw = File.ReadAllBytes(TerrainDir + "/Volcano_Height.raw");
        var h = new float[src * src];
        float max = 0f;
        for (int i = 0; i < h.Length; i++)
        {
            h[i] = (raw[i * 2] | (raw[i * 2 + 1] << 8)) / 65535f;
            if (h[i] > max) max = h[i];
        }
        for (int i = 0; i < h.Length; i++) h[i] /= max;

        const int res = 1025;
        const float worldSize = 448f;
        float heightRange = max * 270f;                 // krater kenari ~35 m yuksekte
        const float cxPx = 540f, cyPx = 520f;           // krater merkezi (resim koordinati, ustten)
        float cyT = (src - 1) - cyPx;                   // arazide (alttan) satir; doku ile hizali olsun diye satirlar cevrilir
        float floorH = h[(int)cyPx * src + (int)cxPx];
        const float digDepth = 3f;                      // LavaField.digDepth ile ayni
        float pitDelta = (digDepth + 0.8f) / heightRange;

        float mPerPx = worldSize / (src - 1);
        var data = new float[res, res];
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float fx = x / (float)(res - 1) * (src - 1), fy = y / (float)(res - 1) * (src - 1);
                float ry = (src - 1) - fy;              // ham veride satir (ustten)
                int x0 = Mathf.Min((int)fx, src - 2), y0 = Mathf.Min((int)ry, src - 2);
                float tx = fx - x0, ty = ry - y0;
                float v = Mathf.Lerp(
                    Mathf.Lerp(h[y0 * src + x0], h[y0 * src + x0 + 1], tx),
                    Mathf.Lerp(h[(y0 + 1) * src + x0], h[(y0 + 1) * src + x0 + 1], tx), ty);
                // Arena: merkezi duzlestir, 26 m'ye kadar tam duz, 44 m'de dogal arazi
                float dx = (fx - cxPx) * mPerPx, dz = (fy - cyT) * mPerPx;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(26f, 44f, dist));
                float hh = Mathf.Lerp(floorH, v, blend);
                // Lav havuzu icin cukur (LavaField.PoolRadiusAt ile ayni sekil)
                float th = Mathf.Atan2(dz, dx);
                float poolR = 9f * (1f + 0.06f * Mathf.Sin(3f * th + 1f) + 0.04f * Mathf.Sin(5f * th + 2f));
                float pit = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(poolR + 0.1f, poolR + 1.4f, dist));
                data[y, x] = hh - pit * pitDelta;
            }

        var td = new TerrainData { heightmapResolution = res };
        td.size = new Vector3(worldSize, heightRange, worldSize);
        td.SetHeights(0, 0, data);

        var layer = new TerrainLayer
        {
            diffuseTexture = Tex(TerrainDir + "/Volcano_Albedo.png"),
            normalMapTexture = Tex(TerrainDir + "/Volcano_Normal.png"),
            tileSize = new Vector2(worldSize, worldSize),
            smoothness = 0.1f,
        };
        AssetDatabase.DeleteAsset(TerrainDir + "/VolcanoLayer.terrainlayer");
        AssetDatabase.CreateAsset(layer, TerrainDir + "/VolcanoLayer.terrainlayer");
        td.terrainLayers = new[] { layer };
        AssetDatabase.DeleteAsset(TerrainDir + "/VolcanoTerrain.asset");
        AssetDatabase.CreateAsset(td, TerrainDir + "/VolcanoTerrain.asset");

        var go = Terrain.CreateTerrainGameObject(td);
        go.name = "VolcanoTerrain";
        go.transform.SetParent(parent, false);
        // Krater merkezi dunya merkezine; zemin yuksekligi y=0
        float cx = cxPx * mPerPx, cz = cyT * mPerPx;
        go.transform.position = new Vector3(-cx, -floorH * heightRange, -cz);
        go.GetComponent<Terrain>().drawInstanced = true;
    }

    // ---------- Onizleme goruntuleri (model yonu kontrolu) ----------

    [MenuItem("VolcanoSweepers/Render Previews")]
    public static void RenderPreviews()
    {
        string outDir = System.Environment.GetEnvironmentVariable("VS_PREVIEW_DIR");
        if (string.IsNullOrEmpty(outDir)) outDir = "Library/Previews";
        Directory.CreateDirectory(outDir);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var lightGo = new GameObject("L");
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.2f;
        lightGo.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f);

        var camGo = new GameObject("C");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.35f, 0.4f, 0.5f);
        cam.fieldOfView = 30f;

        foreach (var name in new[] { "Bucket", "Pickaxe", "LithicCoreItem", "MagmaBall" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/{name}.prefab");
            if (prefab == null) continue;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.position = Vector3.zero;
            inst.transform.rotation = Quaternion.identity;
            var rb = inst.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            foreach (var view in new[] { ("front", new Vector3(0f, 0.1f, -2.5f)), ("side", new Vector3(2.5f, 0.1f, 0f)) })
            {
                camGo.transform.position = view.Item2;
                camGo.transform.LookAt(Vector3.zero);
                Shot(cam, $"{outDir}/{name}_{view.Item1}.png");
            }
            Object.DestroyImmediate(inst);
        }
        Debug.Log("[VolcanoSweepers] Onizlemeler: " + Path.GetFullPath(outDir));
    }

    static void Shot(Camera cam, string path)
    {
        var rt = new RenderTexture(512, 512, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }
}
