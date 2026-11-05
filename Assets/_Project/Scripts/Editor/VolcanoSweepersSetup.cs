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
        CelShadingSetup.Apply();
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
            Art + "/Textures/Rock/RockWall_Normal.png",
            Art + "/Terrain/Volcano_Normal.png",
            Art + "/Textures/Water/Water_TileNormal.png",
            Art + "/Textures/Water/Water_BaseNormal.png",
        };
        string[] linear =
        {
            Art + "/Textures/Bucket/Bucket_MetalSmooth.png",
            Art + "/Textures/Pickaxe/Pickaxe_MetalSmooth.png",
            Art + "/Textures/ObsidianTile/Obsidian_MetalSmooth.png",
            Art + "/Textures/LithicCore/LithicCore_Metallic.png",
            Art + "/Textures/Rock/RockWall_MetalSmooth.png",
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
        public Material lava, obsidian, cleared, bubble, bucket, pickaxe, core, magma, water, rock, particle, pondRim, waterDrop, lavaCrust, metal, wood, concrete, waterTrail, liquid, rockWall, oreBin;
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

    static Material TransparentWater(string name, string baseTex, string normalTex, Color color, Vector2 tiling)
    {
        string path = $"{Mats}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find("Universal Render Pipeline/Lit");
        mat.SetTexture("_BaseMap", Tex(baseTex));
        mat.SetTextureScale("_BaseMap", tiling);
        if (normalTex != null)
        {
            mat.SetTexture("_BumpMap", Tex(normalTex));
            mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_BumpScale", 1.2f);
        }
        else
        {
            mat.SetTexture("_BumpMap", null);
            mat.DisableKeyword("_NORMALMAP");
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", 0.4f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_EnvironmentReflections", 0f);
        mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", 5f);
        mat.SetFloat("_DstBlend", 10f);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3000;
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material UnlitTransparent(string name, string baseTex, Color color)
    {
        string path = $"{Mats}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find("Universal Render Pipeline/Unlit");
        mat.SetTexture("_BaseMap", Tex(baseTex));
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", 5f);
        mat.SetFloat("_DstBlend", 10f);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3000;
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
        m.metal = Lit("StationMetal", new Color(0.32f, 0.35f, 0.4f), smooth: 0.65f, metal: 0.85f);
        m.wood = Lit("StationWood", new Color(0.33f, 0.2f, 0.11f), smooth: 0.15f);
        m.concrete = Lit("StationConcrete", new Color(0.3f, 0.28f, 0.27f), smooth: 0.1f);
        m.oreBin = Lit("OreBin", Color.white, T + "OreBin/OreBin_Color.png", smooth: 0.25f);
        m.rockWall = Lit("RockWall", Color.white, T + "Rock/RockWall_Color.png", T + "Rock/RockWall_Normal.png",
            T + "Rock/RockWall_MetalSmooth.png", smooth: 1f);
        m.rockWall.SetTextureScale("_BaseMap", new Vector2(0.45f, 0.45f));
        m.rockWall.SetTextureScale("_BumpMap", new Vector2(0.45f, 0.45f));
        m.rockWall.SetTextureScale("_MetallicGlossMap", new Vector2(0.45f, 0.45f));
        m.rock = Lit("Rock", new Color(0.13f, 0.1f, 0.09f), smooth: 0.1f);
        m.pondRim = Lit("PondRim", new Color(0.2f, 0.17f, 0.16f), smooth: 0.15f);
        m.water = Lit("Water", new Color(0.3f, 0.6f, 0.85f), T + "Water/Water_Tile.png", T + "Water/Water_TileNormal.png",
            smooth: 0.9f);
        m.water.SetFloat("_EnvironmentReflections", 0f);
        m.water.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        m.water.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
        m.water.SetTextureScale("_BumpMap", new Vector2(2f, 2f));
        m.water.SetFloat("_BumpScale", 1.4f);
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
        lc.SetFloat("_FlowSpeed", 0.03f);
        lc.SetTexture("_ObsMap", Tex(T + "ObsidianTile/Obsidian_basecolor.png"));
        lc.SetTexture("_ObsNormal", Tex(T + "ObsidianTile/Obsidian_normal.png"));
        lc.SetTexture("_ObsMS", Tex(T + "ObsidianTile/Obsidian_MetalSmooth.png"));
        EditorUtility.SetDirty(lc);
        m.lavaCrust = lc;

        // Su damlasi ve su izi: seffaf, kullanicinin texture'i + normal harita
        m.waterDrop = TransparentWater("WaterDrop", T + "Water/Water_Base.png", T + "Water/Water_BaseNormal.png", new Color(0.18f, 0.4f, 0.7f, 0.22f), Vector2.one);
        m.waterTrail = UnlitTransparent("WaterTrail", T + "Water/Water_Tile.png", new Color(0.5f, 0.78f, 1f, 0.1f));

        // Kap ici sivi (dunyada yatay kesilen silindir)
        string lqPath = Mats + "/LiquidVessel.mat";
        var lq = AssetDatabase.LoadAssetAtPath<Material>(lqPath);
        if (lq == null)
        {
            lq = new Material(Shader.Find("VolcanoSweepers/LiquidVessel"));
            AssetDatabase.CreateAsset(lq, lqPath);
        }
        lq.shader = Shader.Find("VolcanoSweepers/LiquidVessel");
        lq.SetTexture("_BaseMap", Tex(T + "Water/Water_Tile.png"));
        lq.SetTexture("_BumpMap", Tex(T + "Water/Water_TileNormal.png"));
        lq.SetColor("_Color", new Color(0.35f, 0.62f, 0.9f, 1f));
        lq.SetColor("_SurfaceColor", new Color(0.55f, 0.8f, 1f, 1f));
        lq.SetFloat("_Smoothness", 0.92f);
        EditorUtility.SetDirty(lq);
        m.liquid = lq;

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
        // Suyun hacmi: kovanin gercek ic sekline gore uretilir
        var water = new GameObject("Water");
        water.transform.SetParent(bucket.transform, false);
        var innerProfile = BucketProfile.Inner(bucket);
        var waterMesh = BucketProfile.BuildWaterMesh(innerProfile, out float wVol, out float[] wTable, out float wRim, out float wFloor, out float wFull);
        Directory.CreateDirectory(Art + "/Models/Bucket/Parts");
        AssetDatabase.DeleteAsset(Art + "/Models/Bucket/Parts/BucketWaterVolume.asset");
        AssetDatabase.CreateAsset(waterMesh, Art + "/Models/Bucket/Parts/BucketWaterVolume.asset");
        waterMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Art + "/Models/Bucket/Parts/BucketWaterVolume.asset");
        var bw = water.AddComponent<BucketWater>();
        bw.emptyY = wFloor;
        bw.fullY = wFull;
        bw.rimRadius = wRim;
        bw.totalVolume = wVol;
        bw.levelTable = wTable;
        water.transform.localPosition = new Vector3(0f, bw.emptyY, 0f);
        Debug.Log($"[Bucket] ic profil noktasi={innerProfile.Count} taban={wFloor:F3} agiz={wFull:F3} agiz yaricapi={wRim:F3} hacim={wVol * 1000f:F2} L (kova birimi)");

        var body = new GameObject("WaterBody");
        body.AddComponent<MeshFilter>().sharedMesh = waterMesh;
        body.AddComponent<MeshRenderer>().sharedMaterial = m.liquid;
        body.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        body.transform.SetParent(bucket.transform, false);
        body.SetActive(false);
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
        };

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Atmosfer
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.3f, 0.19f, 0.17f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.2f, 0.07f, 0.05f);
        RenderSettings.fogDensity = 0.0028f;

        // Cel-shade icin guclu, yonlu ve sert golgeli ana isik: net isik/golge bantlari verir
        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.78f, 0.62f);
        sun.intensity = 1.5f;
        sun.shadows = LightShadows.Hard;
        sun.shadowStrength = 0.9f;
        sun.shadowBias = 0.03f;
        sun.shadowNormalBias = 0.4f;
        sunGo.transform.rotation = Quaternion.Euler(42f, -35f, 0f);

        // Volkan arazisi (kullanicinin yukseklik haritasi) + ekipman istasyonu
        var env = new GameObject("Environment").transform;
        Vector3 pad = BuildTerrain(m, env);
        BuildStation(m, pad, prefabs.bucket);

        // Krateri dolduran lav golu (surekli yuzey, kare yok)
        var lavaGo = new GameObject("LavaField");
        lavaGo.transform.position = Vector3.zero;
        lavaGo.AddComponent<MeshFilter>();
        lavaGo.AddComponent<MeshRenderer>();
        var field = lavaGo.AddComponent<LavaField>();
        field.material = m.lavaCrust;
        field.size = basinSize;
        field.res = LavaRes;
        field.meshRes = 512;
        field.digDepth = 12f;
        field.basinData = basinAsset;

        var glowGo = new GameObject("LavaGlow");
        glowGo.transform.position = new Vector3(0f, 3f, 0f);
        var glow = glowGo.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.45f, 0.12f);
        glow.range = 22f;
        glow.intensity = 4f;

        var amb = lavaGo.AddComponent<LavaAmbience>();
        amb.field = field;
        amb.bubbleMat = m.bubble;
        amb.magmaBallPrefab = prefabs.magma;
        amb.glowLight = glow;

        // Ses ve efekt
        var sys = new GameObject("Systems");
        var fx = sys.AddComponent<FxConfig>();
        fx.particleMaterial = m.particle;
        var sim = sys.AddComponent<WaterSim>();
        sim.material = m.waterDrop;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Art + "/Models/Water/WaterDrop.glb"))
        {
            var mesh = o as Mesh;
            if (mesh != null) { sim.mesh = mesh; break; }
        }
        var sfx = sys.AddComponent<SfxPlayer>();
        // Lav sesleri: yalnizca kullanicinin verdigi "heavy bubbles" kaydi (su lava dokulunce)
        sfx.waterPour = AssetDatabase.LoadAssetAtPath<AudioClip>(Sfx + "water_splash.mp3");       // su dokme: water splash
        sfx.wellPump = AssetDatabase.LoadAssetAtPath<AudioClip>(Sfx + "well_crank.mp3");         // kuyu cikrigi: roulette wheel
        sfx.bucketPour = AssetDatabase.LoadAssetAtPath<AudioClip>(Sfx + "bucket_pour.mp3");      // cekilen suyu kovaya alma: water pouring
        sfx.pickaxeHit = Clip("pickaxe_hit");
        sfx.obsidianBreak = Clip("obsidian_break");
        sfx.lavaSizzle = AssetDatabase.LoadAssetAtPath<AudioClip>(Sfx + "water_sizzling_hot_iron.mp3");   // lav obsidyene donusurken buharlasma

        // Oyuncu
        var player = new GameObject("Player");
        player.tag = "Player";
        player.transform.position = pad + new Vector3(0f, 0.05f, 0f);
        player.transform.rotation = Quaternion.Euler(0f, padYaw, 0f);
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
        cam.farClipPlane = 700f;
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
    const int LavaRes = 1024;
    static float basinSize = 200f;     // lav alaninin kare kenari (m)
    static TextAsset basinAsset;       // lav golunun maskesi
    static float padYaw;               // oyuncunun gole bakan yonu (derece)

    // Kullanicinin yukseklik haritasindan arazi kurar. Krater AGZINA KADAR lavla doludur:
    // lav seviyesi kraterin en alcak kenarinin (tasma noktasi) 0.6 m altidir ve dunya y=0'dir.
    // Istasyon platformu tam bu tasma noktasinda, dagin tepesinde, gole bakan kenardadir.
    // Donus: platformun merkezi.
    static Vector3 BuildTerrain(MatSet m, Transform parent)
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
        float heightRange = max * 270f;
        const float cxPx = 540f, cyPx = 520f;           // krater merkezi (resim koordinati, ustten)
        float cyT = (src - 1) - cyPx;
        float mPerPx = worldSize / (src - 1);

        // 1) Kraterin en alcak kenari (tasma noktasi)
        float minCrest = float.MaxValue;
        int sadX = 0, sadY = 0;
        for (int ang = 0; ang < 360; ang += 2)
        {
            float t = ang * Mathf.Deg2Rad;
            float best = 0f; int bx = 0, by = 0;
            for (int r = 20; r < 330; r++)
            {
                int xi = Mathf.RoundToInt(cxPx + r * Mathf.Cos(t)), yi = Mathf.RoundToInt(cyPx + r * Mathf.Sin(t));
                if (xi < 0 || yi < 0 || xi >= src || yi >= src) break;
                float v = h[yi * src + xi];
                if (v > best) { best = v; bx = xi; by = yi; }
            }
            if (best < minCrest) { minCrest = best; sadX = bx; sadY = by; }
        }
        float levelNorm = minCrest - 0.6f / heightRange;            // lav seviyesi (agzin 0.6 m altinda)
        float padH = levelNorm + 1.5f / heightRange;                // platform yuksekligi
        float padX = (sadX - cxPx) * mPerPx, padZ = (cyPx - sadY) * mPerPx;
        padYaw = Mathf.Atan2(-padX, -padZ) * Mathf.Rad2Deg;
        Debug.Log($"[Terrain] tasma noktasi: {minCrest * heightRange:F1} m, lav seviyesi {levelNorm * heightRange:F1} m, platform ({padX:F1}, {padZ:F1}), kratere {Mathf.Sqrt(padX * padX + padZ * padZ):F0} m");

        // 2) Lav golu: merkezden baglantili, lav seviyesinin altindaki hucreler
        float SampleH(float wx, float wz)
        {
            float fx = wx / mPerPx + cxPx, fr = cyPx - wz / mPerPx;
            fx = Mathf.Clamp(fx, 0f, src - 1.001f); fr = Mathf.Clamp(fr, 0f, src - 1.001f);
            int x0 = (int)fx, y0 = (int)fr;
            float tx = fx - x0, ty = fr - y0;
            return Mathf.Lerp(
                Mathf.Lerp(h[y0 * src + x0], h[y0 * src + x0 + 1], tx),
                Mathf.Lerp(h[(y0 + 1) * src + x0], h[(y0 + 1) * src + x0 + 1], tx), ty);
        }
        // Once yaricap: heightmap cozunurlugunde tasma
        float maxDist = 0f;
        {
            var seen = new bool[src * src];
            var q = new System.Collections.Generic.Queue<int>();
            int start = (int)cyPx * src + (int)cxPx;
            seen[start] = true; q.Enqueue(start);
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                int cy = c / src, cx = c % src;
                float dd = Mathf.Sqrt((cx - cxPx) * (cx - cxPx) + (cy - cyPx) * (cy - cyPx)) * mPerPx;
                if (dd > maxDist) maxDist = dd;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= src || ny >= src) continue;
                    int ni = ny * src + nx;
                    if (seen[ni] || h[ni] >= levelNorm) continue;
                    seen[ni] = true; q.Enqueue(ni);
                }
            }
        }
        basinSize = Mathf.Ceil((maxDist + 8f) * 2f / 2f) * 2f;
        var basin = new byte[LavaRes * LavaRes];
        {
            var q = new System.Collections.Generic.Queue<int>();
            int c0 = (LavaRes / 2) * LavaRes + LavaRes / 2;
            basin[c0] = 255; q.Enqueue(c0);
            int count = 0;
            while (q.Count > 0)
            {
                int c = q.Dequeue(); count++;
                int cy = c / LavaRes, cx = c % LavaRes;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= LavaRes || ny >= LavaRes) continue;
                    int ni = ny * LavaRes + nx;
                    if (basin[ni] != 0) continue;
                    float wx = (nx / (LavaRes - 1f) - 0.5f) * basinSize, wz = (ny / (LavaRes - 1f) - 0.5f) * basinSize;
                    if (SampleH(wx, wz) >= levelNorm) continue;
                    basin[ni] = 255; q.Enqueue(ni);
                }
            }
            Debug.Log($"[Terrain] lav golu: kare {basinSize:F0} m, {count} hucre, yaklasik {count * (basinSize / LavaRes) * (basinSize / LavaRes):F0} m2, en uzak nokta {maxDist:F0} m");
        }
        File.WriteAllBytes(TerrainDir + "/LavaBasin.bytes", basin);
        AssetDatabase.ImportAsset(TerrainDir + "/LavaBasin.bytes", ImportAssetOptions.ForceUpdate);
        basinAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(TerrainDir + "/LavaBasin.bytes");

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
                float dx = (fx - cxPx) * mPerPx, dz = (fy - cyT) * mPerPx;

                // Istasyon platformu: duz, kraterin kenarinda
                float pdx = dx - padX, pdz = dz - padZ;
                float pd = Mathf.Sqrt(pdx * pdx + pdz * pdz);
                float pw = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(11f, 19f, pd));
                v = Mathf.Lerp(v, padH, pw);

                data[y, x] = v;
            }

        var td = new TerrainData { heightmapResolution = res };
        td.size = new Vector3(worldSize, heightRange, worldSize);
        td.SetHeights(0, 0, data);

        // Kuyu govdesi icin arazide delik
        {
            Vector3 org = new Vector3(-cxPx * mPerPx, 0f, -cyT * mPerPx);
            Vector3 wellW = new Vector3(padX, 0f, padZ) + WellOffset;
            int hr = td.holesResolution;
            var holes = new bool[hr, hr];
            for (int hy = 0; hy < hr; hy++)
                for (int hx = 0; hx < hr; hx++)
                {
                    float wx = org.x + (hx + 0.5f) / hr * worldSize;
                    float wz = org.z + (hy + 0.5f) / hr * worldSize;
                    float dd = (wx - wellW.x) * (wx - wellW.x) + (wz - wellW.z) * (wz - wellW.z);
                    holes[hy, hx] = dd >= 0.95f * 0.95f;
                }
            td.SetHoles(0, 0, holes);
        }

        var layer = new TerrainLayer
        {
            diffuseTexture = Tex(TerrainDir + "/Volcano_Albedo.png"),
            normalMapTexture = Tex(TerrainDir + "/Volcano_Normal.png"),
            tileSize = new Vector2(worldSize, worldSize),
            smoothness = 0.1f,
        };
        AssetDatabase.DeleteAsset(TerrainDir + "/VolcanoLayer.terrainlayer");
        AssetDatabase.CreateAsset(layer, TerrainDir + "/VolcanoLayer.terrainlayer");
        // Platform zemini: kaya duvar dokusu (saglam tas zemin)
        var rockLayer = new TerrainLayer
        {
            diffuseTexture = Tex(Art + "/Textures/Rock/RockWall_Color.png"),
            normalMapTexture = Tex(Art + "/Textures/Rock/RockWall_Normal.png"),
            tileSize = new Vector2(3.2f, 3.2f),
            smoothness = 0.35f,
            normalScale = 1.2f,
        };
        AssetDatabase.DeleteAsset(TerrainDir + "/RockLayer.terrainlayer");
        AssetDatabase.CreateAsset(rockLayer, TerrainDir + "/RockLayer.terrainlayer");
        td.terrainLayers = new[] { layer, rockLayer };
        {
            td.alphamapResolution = 1024;
            int ar = td.alphamapResolution;
            Vector3 aorg = new Vector3(-cxPx * mPerPx, 0f, -cyT * mPerPx);
            var maps = new float[ar, ar, 2];
            for (int ay = 0; ay < ar; ay++)
                for (int ax = 0; ax < ar; ax++)
                {
                    float wx = aorg.x + (ax + 0.5f) / ar * worldSize;
                    float wz = aorg.z + (ay + 0.5f) / ar * worldSize;
                    float pd2 = Mathf.Sqrt((wx - padX) * (wx - padX) + (wz - padZ) * (wz - padZ));
                    float rw = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(9.4f, 11f, pd2));

                    // Egimli yuzeyler (yamaclar) kaya dokusuyla kaplanir: tek devasa doku yerine her yerde net kaya detayi
                    int hx = Mathf.Clamp(Mathf.RoundToInt((ax + 0.5f) / ar * (res - 1)), 1, res - 2);
                    int hy = Mathf.Clamp(Mathf.RoundToInt((ay + 0.5f) / ar * (res - 1)), 1, res - 2);
                    float cell = worldSize / (res - 1f);
                    float gx = (data[hy, hx + 1] - data[hy, hx - 1]) * heightRange / (2f * cell);
                    float gz = (data[hy + 1, hx] - data[hy - 1, hx]) * heightRange / (2f * cell);
                    float slopeDeg = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
                    float sr = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(22f, 42f, slopeDeg));
                    float rockTotal = rw + (1f - rw) * sr;
                    maps[ay, ax, 0] = 1f - rockTotal;
                    maps[ay, ax, 1] = rockTotal;
                }
            td.SetAlphamaps(0, 0, maps);
        }
        AssetDatabase.DeleteAsset(TerrainDir + "/VolcanoTerrain.asset");
        AssetDatabase.CreateAsset(td, TerrainDir + "/VolcanoTerrain.asset");

        var go = Terrain.CreateTerrainGameObject(td);
        go.name = "VolcanoTerrain";
        go.transform.SetParent(parent, false);
        // Krater merkezi dunya merkezine; lav seviyesi y=0
        float cx0 = cxPx * mPerPx, cz0 = cyT * mPerPx;
        go.transform.position = new Vector3(-cx0, -levelNorm * heightRange, -cz0);
        go.GetComponent<Terrain>().drawInstanced = true;

        return new Vector3(padX, 1.5f, padZ);
    }

    // ---------- Ekipman istasyonu ----------

    static readonly Vector3 WellOffset = new Vector3(4.3f, 0f, -2.5f);   // platform merkezine gore kuyu
    const float WellScale = 1.25f;

    class WellParts { public Mesh baseOpen, wheelClean; public Vector3 axis; }

    // Mesh'i konum kaynakli baglantili parcalara ayirir, secilen parcalari cikarip yeni mesh uretir
    static Mesh CopyWithout(Mesh src, string name, System.Func<Bounds, bool> remove)
    {
        var verts = src.vertices;
        var tris = src.triangles;
        var map = new System.Collections.Generic.Dictionary<Vector3Int, int>();
        var weld = new int[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            var k = new Vector3Int(Mathf.RoundToInt(verts[i].x * 1000f), Mathf.RoundToInt(verts[i].y * 1000f), Mathf.RoundToInt(verts[i].z * 1000f));
            if (!map.TryGetValue(k, out int id)) { id = map.Count; map[k] = id; }
            weld[i] = id;
        }
        var parent = new int[map.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (int t = 0; t < tris.Length; t += 3)
        {
            int a = Find(weld[tris[t]]), b = Find(weld[tris[t + 1]]), c = Find(weld[tris[t + 2]]);
            parent[b] = a;
            parent[Find(c)] = Find(a);
        }
        var bounds = new System.Collections.Generic.Dictionary<int, Bounds>();
        for (int t = 0; t < tris.Length; t += 3)
        {
            int r = Find(weld[tris[t]]);
            for (int j = 0; j < 3; j++)
            {
                var v = verts[tris[t + j]];
                if (!bounds.TryGetValue(r, out var bb)) bb = new Bounds(v, Vector3.zero); else bb.Encapsulate(v);
                bounds[r] = bb;
            }
        }
        var keep = new System.Collections.Generic.List<int>();
        int removed = 0;
        for (int t = 0; t < tris.Length; t += 3)
        {
            if (remove(bounds[Find(weld[tris[t]])])) { removed++; continue; }
            keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]);
        }
        Debug.Log($"[Well] {name}: {tris.Length / 3} ucgen, {removed} cikarildi, {bounds.Count} parca");
        var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.vertices = verts;
        m.normals = src.normals;
        m.tangents = src.tangents;
        m.uv = src.uv;
        m.triangles = keep.ToArray();
        m.RecalculateBounds();
        return m;
    }

    static Mesh SaveMesh(Mesh m, string file)
    {
        string dir = Art + "/Models/Well/Parts";
        Directory.CreateDirectory(dir);
        string path = dir + "/" + file + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return AssetDatabase.LoadAssetAtPath<Mesh>(path);
    }

    static WellParts BuildWellParts()
    {
        var parts = new WellParts();
        Mesh baseM = null, wheelM = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Art + "/Models/Well/Well.glb"))
        {
            var m = o as Mesh;
            if (m == null) continue;
            if (m.name.Contains("Well_Base")) baseM = m;
            else if (m.name.Contains("WheelAcc")) wheelM = m;
        }
        if (baseM == null || wheelM == null) { Debug.LogError("[Well] mesh'ler bulunamadi"); return parts; }

        // Kapagi cikar
        parts.baseOpen = SaveMesh(CopyWithout(baseM, "WellBase_Open", b => b.center.z > 0.9f && b.size.z < 0.2f), "WellBase_Open");
        // Kucuk kova, zinciri ve sarkan ipi cikar
        parts.wheelClean = SaveMesh(CopyWithout(wheelM, "WellWheel_Clean", b =>
            b.min.z < 0.6f
            || (Mathf.Abs(b.center.x) < 0.3f && Mathf.Abs(b.center.y) > 0.3f && Mathf.Abs(b.center.y) < 0.8f && b.center.z > 0.95f && b.center.z < 1.32f && b.size.y < 0.5f)
            || (b.center.z < 1.12f && b.size.z > 0.2f && Mathf.Abs(b.center.x) < 0.3f && Mathf.Abs(b.center.y) < 0.15f)), "WellWheel_Clean");

        // Donderme ekseni: yan carklarin merkezi (x ekseni boyunca)
        float minY = float.MaxValue, maxY = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var v in wheelM.vertices)
        {
            if (Mathf.Abs(v.x) <= 0.95f) continue;
            minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
            minZ = Mathf.Min(minZ, v.z); maxZ = Mathf.Max(maxZ, v.z);
        }
        parts.axis = new Vector3(0f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);
        Debug.Log("[Well] eksen (mesh yerel): " + parts.axis);
        return parts;
    }

    // Icten gorunen (normalleri ice bakan) silindir: kuyu govdesi
    static Mesh ShaftMesh(float radius, float depth, int seg)
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var norms = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var tris = new System.Collections.Generic.List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            float x = Mathf.Cos(a), z = Mathf.Sin(a);
            verts.Add(new Vector3(x * radius, 0f, z * radius)); norms.Add(new Vector3(-x, 0f, -z)); uvs.Add(new Vector2(i / (float)seg * 3f, 0f));
            verts.Add(new Vector3(x * radius, -depth, z * radius)); norms.Add(new Vector3(-x, 0f, -z)); uvs.Add(new Vector2(i / (float)seg * 3f, depth * 0.5f));
        }
        for (int i = 0; i < seg; i++)
        {
            int a = i * 2;
            tris.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 });
        }
        var m = new Mesh { name = "WellShaft" };
        m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
        return m;
    }

    // Kesit halkalarindan (yaricap, yukseklik) donel serit mesh: ustu duz, kenari egimli tas doseme
    static Mesh StripMesh(System.Collections.Generic.List<Vector2> profile, int seg, string name)
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var tris = new System.Collections.Generic.List<int>();
        foreach (var pr in profile)
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                float x = Mathf.Cos(a) * pr.x, z = Mathf.Sin(a) * pr.x;
                verts.Add(new Vector3(x, pr.y, z));
                uvs.Add(new Vector2(x, z));
            }
        for (int r = 0; r < profile.Count - 1; r++)
            for (int i = 0; i < seg; i++)
            {
                int a = r * (seg + 1) + i, b = (r + 1) * (seg + 1) + i;
                tris.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });   // saat yonu: yuzey yukari bakar
            }
        var m = new Mesh { name = name };
        m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0); m.RecalculateNormals();
        return m;
    }

    // Duz halka (kuyunun etrafindaki tas doseme)
    static Mesh RingMesh(float inner, float outer, int seg)
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var tris = new System.Collections.Generic.List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            float x = Mathf.Cos(a), z = Mathf.Sin(a);
            verts.Add(new Vector3(x * inner, 0f, z * inner)); uvs.Add(new Vector2(x * inner, z * inner));
            verts.Add(new Vector3(x * outer, 0f, z * outer)); uvs.Add(new Vector2(x * outer, z * outer));
        }
        for (int i = 0; i < seg; i++)
        {
            int a = i * 2;
            tris.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
        }
        var m = new Mesh { name = "WellApron" };
        m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0); m.RecalculateNormals();
        return m;
    }

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool collider = false)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static void BuildStation(MatSet m, Vector3 c, GameObject bucketPrefab)
    {
        var root = new GameObject("EquipmentStation").transform;
        root.position = c;

        // Beton platform
        // Platform zemini: kaya duvar dokusuyla yuvarlak, kenari yumusak egimli zemin
        var slab = Prim(PrimitiveType.Cube, "Slab", root, c, Vector3.one, m.rockWall);
        slab.GetComponent<MeshFilter>().sharedMesh = StripMesh(new System.Collections.Generic.List<Vector2>
        {
            new Vector2(0.05f, 0.03f), new Vector2(9.3f, 0.03f), new Vector2(9.55f, 0.0f), new Vector2(9.9f, -0.25f),
        }, 128, "PadSlab");

        // Su kuyusu: ustu acik, kova ip ile bagli, donderme makarasi ile iner/cikar
        Vector3 wellPos = c + WellOffset;
        Vector3 toPad = new Vector3(-WellOffset.x, 0f, -WellOffset.z).normalized;
        float yaw = Mathf.Atan2(toPad.x, toPad.z) * Mathf.Rad2Deg;
        var wellRoot = new GameObject("WaterWell");
        wellRoot.transform.SetParent(root, false);
        wellRoot.transform.position = wellPos;
        wellRoot.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        var parts = BuildWellParts();
        var wellAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Models/Well/Well.glb");
        Transform pivot = null;
        if (wellAsset != null && parts.baseOpen != null)
        {
            var wm = (GameObject)PrefabUtility.InstantiatePrefab(wellAsset, wellRoot.transform);
            wm.name = "WellModel";
            PrefabUtility.UnpackPrefabInstance(wm, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            wm.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            wm.transform.localScale = Vector3.one * WellScale;
            wm.transform.localPosition = Vector3.zero;
            foreach (var col in wm.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
            foreach (var mf in wm.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.name.Contains("Well_Base")) mf.sharedMesh = parts.baseOpen;
                else if (mf.name.Contains("WheelAcc"))
                {
                    mf.sharedMesh = parts.wheelClean;
                    var pv = new GameObject("WheelPivot");
                    pv.transform.SetParent(mf.transform.parent, false);
                    pv.transform.localPosition = parts.axis;
                    mf.transform.SetParent(pv.transform, true);
                    pivot = pv.transform;
                }
            }
        }

        var wellCol = new GameObject("WellCollider");
        wellCol.transform.SetParent(wellRoot.transform, false);
        wellCol.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        var wbox = wellCol.AddComponent<BoxCollider>();
        wbox.size = new Vector3(2.2f, 1.6f, 2.2f);

        // Tas doseme, kuyu govdesi ve dipteki su
        var apron = Prim(PrimitiveType.Cube, "WellPaving", root, wellPos, Vector3.one, m.rockWall);
        apron.GetComponent<MeshFilter>().sharedMesh = StripMesh(new System.Collections.Generic.List<Vector2>
        {
            new Vector2(0.66f, 0.07f), new Vector2(3.3f, 0.07f), new Vector2(3.65f, 0.0f), new Vector2(3.9f, -0.12f),
        }, 56, "WellPaving");
        float shaftDepth = 4.4f;
        float waterLevel = wellPos.y - 3.3f;
        var shaft = Prim(PrimitiveType.Cube, "WellShaft", root, wellPos + new Vector3(0f, 0.5f, 0f), Vector3.one, m.rock);
        shaft.GetComponent<MeshFilter>().sharedMesh = ShaftMesh(0.655f, shaftDepth + 0.5f, 28);
        Prim(PrimitiveType.Cylinder, "WellBottom", root, new Vector3(wellPos.x, wellPos.y - shaftDepth, wellPos.z), new Vector3(1.3f, 0.02f, 1.3f), m.cleared);
        var shaftWater = Prim(PrimitiveType.Cylinder, "WellWater", root, new Vector3(wellPos.x, waterLevel, wellPos.z), new Vector3(1.3f, 0.02f, 1.3f), m.water);
        shaftWater.AddComponent<WaterSurface>();
        var wl = new GameObject("WellLight");
        wl.transform.SetParent(root, false);
        wl.transform.position = new Vector3(wellPos.x, waterLevel + 1.2f, wellPos.z);
        var wlight = wl.AddComponent<Light>();
        wlight.type = LightType.Point; wlight.color = new Color(0.45f, 0.65f, 0.9f); wlight.range = 6f; wlight.intensity = 3f;

        // Kuyu kovasi + ip
        float axisY = pivot != null ? pivot.position.y : wellPos.y + 1.7f;
        float logR = 0.14f * WellScale;
        Vector3 anchor = new Vector3(wellPos.x, axisY - logR, wellPos.z);
        float hangScale = Mathf.Pow(12f, 1f / 3f);       // hacim 12 kat => olcek 12^(1/3) = 2.289
        float topY = axisY - logR - 0.34f * hangScale * 0.5f - 0.06f;
        var hang = (GameObject)PrefabUtility.InstantiatePrefab(bucketPrefab, root);
        hang.name = "WellBucket";
        hang.transform.localScale = Vector3.one * hangScale;
        hang.transform.position = new Vector3(wellPos.x, topY, wellPos.z);
        var hangFill = hang.AddComponent<BigBucket>();
        hangFill.capacity = 12;                          // hacmi el kovasinin 12 kati: bir cekis 12 doldurma
        hang.AddComponent<WaterSource>();
        var hcol = hang.AddComponent<CapsuleCollider>();
        hcol.direction = 1;
        hcol.radius = 0.17f;
        hcol.height = 0.34f;
        var rope = Prim(PrimitiveType.Cylinder, "WellRope", root, new Vector3(wellPos.x, topY + 0.3f, wellPos.z), new Vector3(0.035f, 0.3f, 0.035f), m.wood);

        var wellComp = wellRoot.AddComponent<WaterWell>();
        wellComp.wheelPivot = pivot;
        wellComp.bucket = hang.transform;
        wellComp.bucketFill = hangFill;
        wellComp.rope = rope.transform;
        wellComp.anchor = anchor;
        wellComp.topY = topY;
        wellComp.bottomY = waterLevel + 0.25f;
        wellComp.waterY = waterLevel;
        wellComp.logRadius = logR;
        wellComp.rimY = wellPos.y + 0.86f * WellScale;

        // Sandiklar
        // Cevher deposu (tek, yatay genis, 500 cevher kapasiteli)
        {
            Vector3 binPos = c + new Vector3(-5.2f, 0.03f, -1.6f);
            Vector3 toC = c - binPos; toC.y = 0f;
            var binRoot = new GameObject("OreBin");
            binRoot.transform.SetParent(root, false);
            binRoot.transform.position = binPos;
            binRoot.transform.rotation = Quaternion.LookRotation(toC.normalized);
            var binAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Models/OreBin/OreBin.obj");
            float sH = 1.15f / 34.3629f;                       // yukseklik ~1.15 m
            float sW = sH * 2.1f, sD = sH * 1.1f;              // eninde yatay uzatilmis
            if (binAsset != null)
            {
                var bm = (GameObject)PrefabUtility.InstantiatePrefab(binAsset, binRoot.transform);
                bm.name = "OreBinModel";
                PrefabUtility.UnpackPrefabInstance(bm, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                bm.transform.localPosition = Vector3.zero;
                bm.transform.localRotation = Quaternion.identity;
                bm.transform.localScale = new Vector3(sW, sH, sD);
                foreach (var col in bm.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
                foreach (var r in bm.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = m.oreBin;
            }
            float wMeters = 62.7338f * sW, dMeters = 42.2738f * sD, hMeters = 34.3629f * sH;
            var bcol = binRoot.AddComponent<BoxCollider>();
            bcol.center = new Vector3(0f, hMeters * 0.5f, 0f);
            bcol.size = new Vector3(wMeters, hMeters, dMeters);
            var pileGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pileGo.name = "OrePile";
            Object.DestroyImmediate(pileGo.GetComponent<Collider>());
            pileGo.transform.SetParent(binRoot.transform, false);
            pileGo.GetComponent<MeshRenderer>().sharedMaterial = m.obsidian;
            var bin = binRoot.AddComponent<OreBin>();
            bin.capacity = 500;
            bin.hitCollider = bcol;
            bin.pile = pileGo.transform;
            bin.pileFullSize = new Vector3(wMeters * 0.86f, hMeters * 0.72f, dMeters * 0.8f);
            bin.pileBaseLocal = new Vector3(0f, 0.06f, 0f);
            pileGo.SetActive(false);
        }
        // Alet rafi
        Prim(PrimitiveType.Cube, "RackBeam", root, c + new Vector3(-3f, 1.2f, -5.2f), new Vector3(3.2f, 0.12f, 0.12f), m.wood);
        Prim(PrimitiveType.Cube, "RackPostL", root, c + new Vector3(-4.5f, 0.6f, -5.2f), new Vector3(0.14f, 1.2f, 0.14f), m.wood);
        Prim(PrimitiveType.Cube, "RackPostR", root, c + new Vector3(-1.5f, 0.6f, -5.2f), new Vector3(0.14f, 1.2f, 0.14f), m.wood);

        // Lamba diregi (sicak isik)
        foreach (var off in new[] { new Vector3(-2.5f, 0f, 4.5f), new Vector3(7.2f, 0f, 2.0f) })
        {
            Prim(PrimitiveType.Cylinder, "LampPost", root, c + off + new Vector3(0f, 1.6f, 0f), new Vector3(0.12f, 1.6f, 0.12f), m.metal);
            var lampGo = new GameObject("LampLight");
            lampGo.transform.SetParent(root, false);
            lampGo.transform.position = c + off + new Vector3(0f, 3.4f, 0f);
            var l = lampGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.78f, 0.5f);
            l.range = 14f;
            l.intensity = 25f;
        }
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

    // (onizleme araci)
    [MenuItem("VolcanoSweepers/Preview Well")]
    public static void PreviewWell()
    {
        string outDir = "Library/WellPreview";
        Directory.CreateDirectory(outDir);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var lightGo = new GameObject("L");
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.3f;
        lightGo.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Models/Well/Well.glb");
        if (asset == null) { Debug.LogError("Well.glb yuklenemedi"); return; }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        inst.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        var rends = inst.GetComponentsInChildren<Renderer>();
        Bounds b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        Debug.Log("[Well] bounds center=" + b.center + " size=" + b.size + " renderers=" + rends.Length);
        foreach (var r in rends) Debug.Log("[Well] " + r.name + " bounds=" + r.bounds.center + " / " + r.bounds.size);
        var camGo = new GameObject("C");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.35f, 0.4f, 0.5f);
        cam.fieldOfView = 35f;
        float d = b.size.magnitude * 1.4f;
        int i = 0;
        foreach (var dir in new[] { new Vector3(0f, 0.3f, -1f), new Vector3(1f, 0.4f, 0.3f), new Vector3(0f, 1.5f, -0.3f) })
        {
            camGo.transform.position = b.center + dir.normalized * d;
            camGo.transform.LookAt(b.center);
            Shot(cam, $"{outDir}/well_{i++}.png");
        }
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
