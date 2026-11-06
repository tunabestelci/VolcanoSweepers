using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// "Dragon Shop" gezici dukkani ve icindeki lav canavari: platforma yerlestirir, cel-shade ile uyumlu materyaller kurar.
public static class ShopSetup
{
    const string Art = "Assets/_Project/Art";
    const string ShopObj = Art + "/Models/Shop/Carrito_Dragon_Armas.obj";
    const string MonsterFbx = Art + "/Models/LavaMonster/LavaMonster.fbx";

    // Dukkanin model uzayi olcegi/konumu
    public const float ShopScale = 1.0f;
    static readonly Vector3 ShopFootprintCenter = new Vector3(0.43f, 0f, -1.43f);  // modelin zemin merkezi (OBJ uzayi)
    // Unity OBJ iceri aktarirken X eksenini aynalar: model uzayindan yerel uzaya
    static Vector3 ML(Vector3 objPos) { return (new Vector3(-objPos.x, objPos.y, objPos.z) - new Vector3(-ShopFootprintCenter.x, 0f, ShopFootprintCenter.z)) * ShopScale; }
    public static readonly Vector3 ShopOffset = new Vector3(0.6f, 0f, 4.6f);       // platform merkezine gore
    // Canavarin duracagi yer: tezgahin arkasi (OBJ uzayi)
    static readonly Vector3 MonsterSpot = new Vector3(0.99f, 1.3f, -1.85f);
    public const float MonsterWorldHeight = 1.38f;                                   // metre

    const string LayoutPath = "Assets/_Project/ShopLayout.json";

    [System.Serializable]
    class Layout
    {
        public Vector3 shopOffset, monsterLocal;
        public float shopYawOffset, monsterYaw, monsterScale;
    }

    // Sahnede elle ayarlanan dukkan/canavar konumunu kaydeder; Build All bunu korur.
    [MenuItem("VolcanoSweepers/Save Shop Layout")]
    public static void SaveLayout()
    {
        var shop = GameObject.Find("DragonShop");
        var mon = GameObject.Find("LavaMonster");
        var st = GameObject.Find("EquipmentStation");
        if (shop == null || mon == null || st == null) { Debug.LogError("[Shop] dukkan/canavar/istasyon bulunamadi"); return; }
        Vector3 toC = st.transform.position - shop.transform.position; toC.y = 0f;
        float baseYaw = Quaternion.LookRotation(toC.normalized).eulerAngles.y;
        var lay = new Layout
        {
            shopOffset = shop.transform.position - st.transform.position,
            shopYawOffset = Mathf.DeltaAngle(baseYaw, shop.transform.eulerAngles.y),
            monsterLocal = mon.transform.localPosition,
            monsterYaw = mon.transform.localEulerAngles.y,
            monsterScale = mon.transform.localScale.x,
        };
        File.WriteAllText(LayoutPath, JsonUtility.ToJson(lay, true));
        AssetDatabase.ImportAsset(LayoutPath);
        Debug.Log("[Shop] duzen kaydedildi: " + LayoutPath);
    }

    static Layout LoadLayout()
    {
        if (!File.Exists(LayoutPath)) return null;
        try { return JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath)); } catch { return null; }
    }

    static Texture2D Tex(string p) { return AssetDatabase.LoadAssetAtPath<Texture2D>(p); }

    static void SetNormal(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti != null && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
    }

    static Material MakeMat(string path, string baseTex, string emissionTex, Color emission, float smooth, string normal = null, float bump = 1f)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = Shader.Find("Universal Render Pipeline/Lit");
        mat.SetTexture("_BaseMap", baseTex != null ? Tex(baseTex) : null);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", smooth);
        mat.SetFloat("_Metallic", 0f);
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", Tex(normal));
            mat.SetFloat("_BumpScale", bump);
            mat.EnableKeyword("_NORMALMAP");
        }
        if (emissionTex != null)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", Tex(emissionTex));
            mat.SetColor("_EmissionColor", emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    public static void Build(Transform root, Vector3 c)
    {
        string T = Art + "/Textures/Shop/";
        string MT = Art + "/Textures/LavaMonster/";
        string Mats = Art + "/Materials/";
        foreach (var p in new[] { "Carrito_Color", "Carrito_Emissive", "Dragon_Color", "Espada_01", "Espada_02", "Espada_03", "Espada_04", "Espada_05", "Daga_01", "Hacha_01", "Hacha_02" })
            AssetDatabase.ImportAsset(T + p + ".png", ImportAssetOptions.ForceUpdate);
        foreach (var p in new[] { "LavaMonster_Base", "LavaMonster_MetalRough", "LavaMonster_Emissive" })
            AssetDatabase.ImportAsset(MT + p + ".png", ImportAssetOptions.ForceUpdate);
        SetNormal(MT + "LavaMonster_Normal.png");

        // ---- Dukkan materyalleri (OBJ icindeki isimlerle) ----
        var shopMats = new System.Collections.Generic.Dictionary<string, Material>
        {
            { "Shader_Carrito", MakeMat(Mats + "Shop_Carrito.mat", T + "Carrito_Color.png", T + "Carrito_Emissive.png", new Color(2.2f, 1.9f, 1.4f), 0.2f) },
            { "Shader_Dragon", MakeMat(Mats + "Shop_Dragon.mat", T + "Dragon_Color.png", null, Color.black, 0.25f) },
            { "Shader_Espada01", MakeMat(Mats + "Shop_Espada01.mat", T + "Espada_01.png", null, Color.black, 0.45f) },
            { "shader_Espada02", MakeMat(Mats + "Shop_Espada02.mat", T + "Espada_02.png", null, Color.black, 0.45f) },
            { "Shader_Espada03", MakeMat(Mats + "Shop_Espada03.mat", T + "Espada_03.png", null, Color.black, 0.45f) },
            { "Shader_Espada04", MakeMat(Mats + "Shop_Espada04.mat", T + "Espada_04.png", null, Color.black, 0.45f) },
            { "Shader_Espada05", MakeMat(Mats + "Shop_Espada05.mat", T + "Espada_05.png", null, Color.black, 0.45f) },
            { "shader_Daga01", MakeMat(Mats + "Shop_Daga01.mat", T + "Daga_01.png", null, Color.black, 0.45f) },
            { "Shader_HAcha01", MakeMat(Mats + "Shop_Hacha01.mat", T + "Hacha_01.png", null, Color.black, 0.4f) },
            { "Shader_Hacha02", MakeMat(Mats + "Shop_Hacha02.mat", T + "Hacha_02.png", null, Color.black, 0.4f) },
        };
        // OBJ ice aktarimi: materyalleri esle
        {
            var mi = AssetImporter.GetAtPath(ShopObj) as ModelImporter;
            if (mi != null)
            {
                mi.isReadable = true;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
                foreach (var kv in shopMats)
                    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
                mi.SaveAndReimport();
            }
        }
        var shopAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ShopObj);

        // ---- Dukkani yerlestir ----
        var layout = LoadLayout();
        Vector3 P = c + (layout != null ? layout.shopOffset : ShopOffset);
        Vector3 toC = c - P; toC.y = 0f;
        var shopRoot = new GameObject("DragonShop");
        shopRoot.transform.SetParent(root, false);
        shopRoot.transform.position = P;
        shopRoot.transform.rotation = Quaternion.LookRotation(toC.normalized) * Quaternion.Euler(0f, layout != null ? layout.shopYawOffset : 0f, 0f);   // tezgah (+Z) platform merkezine bakar
        if (shopAsset != null)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(shopAsset, shopRoot.transform);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "ShopModel";
            model.transform.localScale = Vector3.one * ShopScale;
            model.transform.localPosition = ML(Vector3.zero);
            model.transform.localRotation = Quaternion.identity;
            foreach (var col in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
            // renderer materyallerini ismi uyanlara zorla (guvence)
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>())
            {
                var arr = r.sharedMaterials;
                string[] order = { "Shader_Carrito", "Shader_Dragon", "Shader_Espada03", "shader_Espada02", "Shader_Espada01", "Shader_Espada04", "Shader_Espada05", "shader_Daga01", "Shader_Hacha02", "Shader_HAcha01" };
                // OBJ her grubu ayri nesne olarak getirir: materyal adi "<grup>Mat" (Carrito_TODOMat, TODO_Espada01Mat ...)
                string[] keys = { "Carrito", "Dragon", "Espada03", "Espada02", "Espada01", "Espada04", "Espada05", "Daga", "Hacha02", "Hacha01" };
                var names = new System.Text.StringBuilder();
                for (int i = 0; i < arr.Length; i++)
                {
                    names.Append(arr[i] != null ? arr[i].name : "null").Append(',');
                    string key = arr[i] != null ? arr[i].name.Replace(" (Instance)", "") : null;
                    if (key != null && shopMats.TryGetValue(key, out var mm)) arr[i] = mm;
                    else if (key != null)
                        for (int k = 0; k < keys.Length; k++)
                            if (key.Contains(keys[k])) { arr[i] = shopMats[order[k]]; break; }
                }
                Debug.Log("[Shop] renderer materyalleri: " + names + " (" + arr.Length + " yuva)");
                r.sharedMaterials = arr;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            // Carpisma: gorunmez kutu yerine gercek mesh (tezgahin onune yaklasilabilir)
            Renderer cartR = null, dragonR = null;
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>())
            {
                string n = r.name;
                var mfi = r.GetComponent<MeshFilter>();
                if (n.StartsWith("Carrito")) { cartR = r; }
                else if (n.Contains("Dragon")) { dragonR = r; }
                else continue;
                var mc = r.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mfi.sharedMesh;
            }
            if (dragonR != null)
            {
                var dragonAnim = dragonR.gameObject.AddComponent<DragonAnim>();
                dragonAnim.cartRenderer = cartR;
            }
        }
        else Debug.LogError("[Shop] model yuklenemedi: " + ShopObj);

        // Sicak dukkan isigi: tezgah ustunde (lamba fenerleri) + kapi onunde
        Transform Local(string n, Vector3 modelPos, Color col, float range, float intensity)
        {
            var g = new GameObject(n);
            g.transform.SetParent(shopRoot.transform, false);
            g.transform.localPosition = ML(modelPos);
            var l = g.AddComponent<Light>();
            l.type = LightType.Point; l.color = col; l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
            return g.transform;
        }
        Local("ShopLightInner", new Vector3(1.4f, 2.6f, -1.2f), new Color(1f, 0.7f, 0.4f), 4.2f, 0.9f);
        Local("ShopLanternL", new Vector3(-1.4f, 3.1f, 0.5f), new Color(0.4f, 1f, 0.8f), 2.2f, 0.5f);
        Local("ShopLanternR", new Vector3(4.2f, 3.1f, 0.5f), new Color(0.4f, 1f, 0.8f), 2.2f, 0.5f);

        // ---- Lav canavar: tezgahin arkasinda, cel-shaded, bosta bekleme animasyonu ----
        var monsterAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterFbx);
        if (monsterAsset == null) { Debug.LogError("[Shop] canavar modeli yuklenemedi: " + MonsterFbx); return; }
        {
            var mi = AssetImporter.GetAtPath(MonsterFbx) as ModelImporter;
            if (mi != null)
            {
                bool dirty = false;
                if (mi.animationType != ModelImporterAnimationType.Generic) { mi.animationType = ModelImporterAnimationType.Generic; dirty = true; }
                if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
                var clips = mi.defaultClipAnimations;
                if (clips.Length > 0 && !(mi.clipAnimations.Length > 0 && mi.clipAnimations[0].loopTime))
                {
                    foreach (var cl in clips) { cl.loopTime = true; cl.loopPose = true; }
                    mi.clipAnimations = clips; dirty = true;
                }
                if (dirty) mi.SaveAndReimport();
            }
            monsterAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterFbx);
        }
        var monMat = MakeMat(Mats + "LavaMonster.mat", MT + "LavaMonster_Base.png", MT + "LavaMonster_Emissive.png", new Color(2.4f, 1.3f, 0.7f), 0.55f, MT + "LavaMonster_Normal.png", 1.1f);

        // animasyon kontrolcusu
        AnimationClip clip = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(MonsterFbx))
            if (o is AnimationClip ac && !ac.name.StartsWith("__preview__")) { clip = ac; break; }
        string ctrlPath = Art + "/Models/LavaMonster/LavaMonster.controller";
        AssetDatabase.DeleteAsset(ctrlPath);
        AnimatorController ctrl = clip != null ? AnimatorController.CreateAnimatorControllerAtPathWithClip(ctrlPath, clip) : null;

        var monster = (GameObject)PrefabUtility.InstantiatePrefab(monsterAsset, shopRoot.transform);
        PrefabUtility.UnpackPrefabInstance(monster, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        monster.name = "LavaMonster";
        // boyut: dunyada MonsterWorldHeight metre
        float s = MonsterWorldHeight / 2.14f;
        monster.transform.localScale = Vector3.one * (layout != null && layout.monsterScale > 0f ? layout.monsterScale : s);
        monster.transform.localPosition = layout != null ? layout.monsterLocal : ML(MonsterSpot);
        monster.transform.localRotation = Quaternion.Euler(0f, layout != null ? layout.monsterYaw : MonsterYaw, 0f);
        foreach (var r in monster.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var arr = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < arr.Length; i++) arr[i] = monMat;
            r.sharedMaterials = arr;
            r.updateWhenOffscreen = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        var anim = monster.GetComponent<Animator>();
        if (anim == null) anim = monster.AddComponent<Animator>();
        if (ctrl != null) anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var keeper = monster.AddComponent<ShopKeeper>();
        var look = new GameObject("KeeperLook");
        look.transform.SetParent(monster.transform, false);
        look.transform.localPosition = new Vector3(0f, 1.0f, 0f);
        keeper.lookPoint = look.transform;
        var gl = new GameObject("MonsterGlow");
        gl.transform.SetParent(monster.transform, false);
        gl.transform.localPosition = new Vector3(0f, 1.0f / s * 1f * s, 0.5f);
        var ml = gl.AddComponent<Light>();
        ml.type = LightType.Point; ml.color = new Color(1f, 0.45f, 0.1f); ml.range = 3.2f; ml.intensity = 0.8f; ml.shadows = LightShadows.None;
        Debug.Log("[Shop] dukkan ve lav canavari yerlestirildi: " + P);
    }

    // FBX'in on yonu icin ince ayar (derece)
    public static float MonsterYaw = 25f;
}
