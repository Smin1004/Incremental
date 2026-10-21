using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.SceneTemplate;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Incremental.EditorTools
{
    /// <summary>
    /// Reproducible project setup: data assets seeded with the spec §6 values, render assets, and the prototype scene.
    /// Every step is idempotent; existing assets are kept and only missing pieces are created.
    /// Menu: Incremental / Setup.
    /// </summary>
    public static class PrototypeSetup
    {
        const string GameDir = "Assets/Game";
        const string DataDir = GameDir + "/Data";
        const string MaterialsDir = GameDir + "/Materials";
        const string ScenesDir = GameDir + "/Scenes";
        const string ParamsPath = DataDir + "/GameParams.asset";
        const string CelestialPath = DataDir + "/CelestialTable.asset";
        const string NodePath = DataDir + "/NodeTable.asset";
        const string AbilityPath = DataDir + "/AbilityTable.asset";
        const string ThemePath = DataDir + "/UITheme.asset";
        const string UiMaterialPath = MaterialsDir + "/UIPanel.mat";
        const string TreeIconMaterialPath = MaterialsDir + "/TreeIcon.mat";
        const string UiShaderName = "Incremental/UIPanel";
        const string FontRegularPath = GameDir + "/UI/Fonts/Pretendard-Regular.otf";
        const string FontBoldPath = GameDir + "/UI/Fonts/Pretendard-SemiBold.otf";
        const string TextShaderName = "TextMeshPro/Mobile/Distance Field";
        const string MaterialPath = MaterialsDir + "/DustUnlit.mat";
        const string PlanetMaterialPath = MaterialsDir + "/Planet.mat";
        const string PlanetShaderName = "Incremental/Planet";
        const string DustMaterialPath = MaterialsDir + "/Dust.mat";
        const string DustShaderName = "Incremental/Dust";
        const string DustTrailMaterialPath = MaterialsDir + "/DustTrail.mat";
        const string DustTrailShaderName = "Incremental/DustTrail";
        const string BackgroundMaterialPath = MaterialsDir + "/Starfield.mat";
        const string BackgroundShaderName = "Incremental/Starfield";
        const string FxMaterialPath = MaterialsDir + "/FxRing.mat";
        const string FxShaderName = "Incremental/FxRing";
        const string FlashMaterialPath = MaterialsDir + "/Flash.mat";
        const string FlashShaderName = "Incremental/Flash";
        const string CirclePath = MaterialsDir + "/Circle.png";
        const string CircleSource = "Packages/com.unity.2d.sprite/Editor/ObjectMenuCreation/DefaultAssets/Textures/v2/Circle.png";
        const string TemplatePath = "Assets/Settings/Lit2DSceneTemplate.scenetemplate";
        const string ScenePath = ScenesDir + "/Prototype.unity";
        const string PrefabPath = GameDir + "/GameRoot.prefab";
        const string ShaderName = "Universal Render Pipeline/2D/Sprite-Unlit-Default";
        const double ReferenceHeightPx = 1080.0;

        [MenuItem("Incremental/Setup/Run All")]
        public static void RunAll()
        {
            CreateDataAssets();
            CreateRenderAssets();
            BuildScene();
        }

        [MenuItem("Incremental/Setup/1. Create Data Assets")]
        public static void CreateDataAssets()
        {
            EnsureFolders();
            // GameParams field initializers already hold the §6 parameter values.
            var p = LoadOrCreate<GameParams>(ParamsPath, _ => { });
            var c = LoadOrCreate<CelestialTable>(CelestialPath, t => t.tiers = SeedTiers());
            var n = LoadOrCreate<NodeTable>(NodePath, t =>
            {
                t.nodes = SkillTreeSeed.Nodes();
                t.ringCostMult = SkillTreeSeed.RingCostMult();
            });
            // AbilityTable field initializers hold the 24 values, the way GameParams holds the §6 ones.
            var a = LoadOrCreate<AbilityTable>(AbilityPath, _ => { });
            // UITheme field initializers hold the 26 values (palette, shapes, times); the file references are wired below.
            var u = LoadOrCreate<UITheme>(ThemePath, _ => { });
            WireTheme(u);
            int added = AddMissing(c, n);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Setup] Data assets ready ({added} missing entries added): {AssetDatabase.GetAssetPath(p)}, {AssetDatabase.GetAssetPath(c)}, " +
                      $"{AssetDatabase.GetAssetPath(n)}, {AssetDatabase.GetAssetPath(a)}, {AssetDatabase.GetAssetPath(u)}");
        }

        /// <summary>
        /// Fills the file references the UITheme lacks (26 §4): the two Pretendard files, the TextMeshPro shader and the
        /// panel material. A reference that is already set is kept, so another font assigned by hand stays.
        /// </summary>
        static void WireTheme(UITheme u)
        {
            if (u == null) return;
            bool changed = false;
            if (u.fontRegular == null) { u.fontRegular = AssetDatabase.LoadAssetAtPath<Font>(FontRegularPath); changed |= u.fontRegular != null; }
            if (u.fontBold == null) { u.fontBold = AssetDatabase.LoadAssetAtPath<Font>(FontBoldPath); changed |= u.fontBold != null; }
            if (u.textShader == null) { u.textShader = Shader.Find(TextShaderName); changed |= u.textShader != null; }
            if (u.panelMaterial == null) { u.panelMaterial = AssetDatabase.LoadAssetAtPath<Material>(UiMaterialPath); changed |= u.panelMaterial != null; }
            if (u.treeIconMaterial == null) { u.treeIconMaterial = AssetDatabase.LoadAssetAtPath<Material>(TreeIconMaterialPath); changed |= u.treeIconMaterial != null; }
            if (changed) EditorUtility.SetDirty(u);
            if (u.fontRegular == null || u.fontBold == null) Debug.LogWarning("[Setup] UITheme: font files not found under " + GameDir + "/UI/Fonts");
            if (u.textShader == null) Debug.LogWarning("[Setup] UITheme: shader '" + TextShaderName + "' not found (Window > TextMeshPro > Import TMP Essential Resources)");
        }

        // ---------------- seed data (keep equal to the assets) ----------------
        // Tiers: CelestialSeed (mass, price, size, look). Skill tree nodes: SkillTreeSeed (12 §6).

        public static List<CelestialTier> SeedTiers() => CelestialSeed.Tiers();

        /// <summary>
        /// Adds seed entries that the existing assets lack (tiers by number, nodes by id, missing ringCostMult entries as 1),
        /// keeping every existing entry and value untouched. Tiers stay sorted by tier. Returns the number added.
        /// </summary>
        public static int AddMissing(CelestialTable c, NodeTable n)
        {
            int added = 0;
            foreach (var t in SeedTiers())
            {
                if (c.Get(t.tier) != null) continue;
                c.tiers.Add(t);
                added++;
            }
            c.tiers.Sort((a, b) => a.tier.CompareTo(b.tier));
            foreach (var d in SkillTreeSeed.Nodes())
            {
                if (n.Get(d.id) != null) continue;
                n.nodes.Add(d);
                n.Invalidate();
                added++;
            }
            var mult = SkillTreeSeed.RingCostMult();
            if (n.ringCostMult == null || n.ringCostMult.Length < mult.Length)
            {
                int have = n.ringCostMult == null ? 0 : n.ringCostMult.Length;
                for (int i = 0; i < have; i++) mult[i] = n.ringCostMult[i];
                n.ringCostMult = mult;
                added += mult.Length - have;
            }
            if (added > 0)
            {
                EditorUtility.SetDirty(c);
                EditorUtility.SetDirty(n);
            }
            return added;
        }

        [MenuItem("Incremental/Setup/2. Create Render Assets")]
        public static void CreateRenderAssets()
        {
            EnsureFolders();
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) == null)
            {
                var shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    Debug.LogError("[Setup] Shader not found: " + ShaderName);
                    return;
                }
                AssetDatabase.CreateAsset(new Material(shader) { name = "DustUnlit" }, MaterialPath);
            }

            EnsureMaterial(PlanetMaterialPath, PlanetShaderName);
            EnsureMaterial(DustMaterialPath, DustShaderName);
            EnsureMaterial(DustTrailMaterialPath, DustTrailShaderName);
            EnsureMaterial(BackgroundMaterialPath, BackgroundShaderName);
            EnsureMaterial(FxMaterialPath, FxShaderName);
            EnsureMaterial(FlashMaterialPath, FlashShaderName);
            EnsureMaterial(UiMaterialPath, UiShaderName);
            WireTheme(AssetDatabase.LoadAssetAtPath<UITheme>(ThemePath));

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(CirclePath) == null)
            {
                if (!AssetDatabase.CopyAsset(CircleSource, CirclePath))
                {
                    Debug.LogError("[Setup] Could not copy the circle sprite from " + CircleSource);
                    return;
                }
                AssetDatabase.ImportAsset(CirclePath, ImportAssetOptions.ForceSynchronousImport);
            }

            if (AssetImporter.GetAtPath(CirclePath) is TextureImporter ti &&
                (ti.textureType != TextureImporterType.Sprite || ti.spriteImportMode != SpriteImportMode.Single))
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = 100;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] Render assets ready: " + MaterialPath + ", " + PlanetMaterialPath + ", " + DustMaterialPath + ", " +
                      BackgroundMaterialPath + ", " + FxMaterialPath + ", " + FlashMaterialPath + ", " + CirclePath);
        }

        /// <summary>Creates a material asset for a project shader when it is missing (procedural shaders, no textures).</summary>
        static void EnsureMaterial(string path, string shaderName)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError("[Setup] Shader not found: " + shaderName);
                return;
            }
            AssetDatabase.CreateAsset(new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) }, path);
        }

        [MenuItem("Incremental/Setup/3. Build Prototype Scene")]
        public static void BuildScene()
        {
            EnsureFolders();
            Scene scene;
            if (File.Exists(ScenePath))
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            else
            {
                var template = AssetDatabase.LoadAssetAtPath<SceneTemplateAsset>(TemplatePath);
                if (template != null)
                {
                    scene = SceneTemplateService.Instantiate(template, false, ScenePath).scene;
                }
                else
                {
                    scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    var camGo = new GameObject("Main Camera", typeof(Camera));
                    camGo.tag = "MainCamera";
                    var lightGo = new GameObject("Global Light 2D");
                    var light = lightGo.AddComponent<Light2D>();
                    light.lightType = Light2D.LightType.Global;
                    light.intensity = 1f;
                    EditorSceneManager.SaveScene(scene, ScenePath);
                }
            }

            var gameParams = AssetDatabase.LoadAssetAtPath<GameParams>(ParamsPath);
            double ppu = gameParams != null ? gameParams.pixelsPerUnit : 100.0;

            var cam = Camera.main;
            if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
            if (cam != null)
            {
                cam.orthographic = true;
                cam.orthographicSize = (float)(ReferenceHeightPx / ppu / 2.0);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.02f, 0.02f, 0.05f, 1f);
                cam.transform.position = new Vector3(0f, 0f, -10f);
                EditorUtility.SetDirty(cam);
            }

            var root = Object.FindFirstObjectByType<GameRoot>();
            GameObject rootGo = root != null ? root.gameObject : new GameObject("GameRoot");
            if (root == null) root = rootGo.AddComponent<GameRoot>();
            root.gameParams = gameParams;
            root.celestialTable = AssetDatabase.LoadAssetAtPath<CelestialTable>(CelestialPath);
            root.nodeTable = AssetDatabase.LoadAssetAtPath<NodeTable>(NodePath);
            root.nodeTableV1 = AssetDatabase.LoadAssetAtPath<NodeTable>(GameDir + "/Data/NodeTable_v1.asset");
            root.nodeTableV5 = AssetDatabase.LoadAssetAtPath<NodeTable>(GameDir + "/Data/NodeTable_v5.asset");
            root.abilityTable = AssetDatabase.LoadAssetAtPath<AbilityTable>(AbilityPath);
            root.uiTheme = AssetDatabase.LoadAssetAtPath<UITheme>(ThemePath);
            root.unlitMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            root.planetMaterial = AssetDatabase.LoadAssetAtPath<Material>(PlanetMaterialPath);
            root.dustMaterial = AssetDatabase.LoadAssetAtPath<Material>(DustMaterialPath);
            root.dustTrailMaterial = AssetDatabase.LoadAssetAtPath<Material>(DustTrailMaterialPath);
            root.backgroundMaterial = AssetDatabase.LoadAssetAtPath<Material>(BackgroundMaterialPath);
            root.fxMaterial = AssetDatabase.LoadAssetAtPath<Material>(FxMaterialPath);
            root.flashMaterial = AssetDatabase.LoadAssetAtPath<Material>(FlashMaterialPath);
            root.circleSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CirclePath);
            EditorUtility.SetDirty(root);

            if (PrefabUtility.IsPartOfPrefabInstance(rootGo))
                PrefabUtility.ApplyPrefabInstance(rootGo, InteractionMode.AutomatedAction);
            else
                PrefabUtility.SaveAsPrefabAssetAndConnect(rootGo, PrefabPath, InteractionMode.AutomatedAction);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // Build scene list: slot 0 becomes the prototype scene.
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            var entry = new EditorBuildSettingsScene(ScenePath, true);
            if (scenes.Count == 0) scenes.Add(entry); else scenes[0] = entry;
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log("[Setup] Scene built and set as build scene 0: " + ScenePath);
        }

        /// <summary>
        /// Menu: replaces the node list and the ring multipliers of the NodeTable asset with the seed. Unlike
        /// <see cref="AddMissing"/> this also drops nodes the seed no longer has, which is what a changed axis needs
        /// (20 §4). Saves in the tree are keyed by node id, so old saves have to be thrown away with a version bump.
        /// </summary>
        [MenuItem("Incremental/Setup/5. Rebuild Node Table From Seed")]
        public static void RebuildNodeTable()
        {
            var n = AssetDatabase.LoadAssetAtPath<NodeTable>(NodePath);
            if (n == null)
            {
                Debug.LogError("[Setup] NodeTable asset not found.");
                return;
            }
            int before = n.nodes.Count;
            n.nodes = SkillTreeSeed.Nodes();
            n.ringCostMult = SkillTreeSeed.RingCostMult();
            n.Invalidate();
            EditorUtility.SetDirty(n);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Setup] node table rebuilt from the seed: {before} → {n.nodes.Count} nodes");
        }

        /// <summary>
        /// Menu: puts the code defaults back into the UITheme asset (palette, shapes, sizes, times) and keeps its file
        /// references. Overwrites what was tuned in the inspector: use it after changing the initializers in UITheme.
        /// </summary>
        [MenuItem("Incremental/Setup/6. Reset UI Theme To Code Defaults")]
        public static void ResetTheme()
        {
            var u = AssetDatabase.LoadAssetAtPath<UITheme>(ThemePath);
            if (u == null)
            {
                Debug.LogError("[Setup] UITheme asset not found (menu Incremental/Setup/1. Create Data Assets).");
                return;
            }
            var regular = u.fontRegular;
            var bold = u.fontBold;
            var shader = u.textShader;
            var material = u.panelMaterial;
            var iconMaterial = u.treeIconMaterial;
            var fresh = ScriptableObject.CreateInstance<UITheme>();
            EditorUtility.CopySerialized(fresh, u);
            Object.DestroyImmediate(fresh);
            u.name = Path.GetFileNameWithoutExtension(ThemePath);
            u.fontRegular = regular;
            u.fontBold = bold;
            u.textShader = shader;
            u.panelMaterial = material;
            u.treeIconMaterial = iconMaterial;
            WireTheme(u);
            EditorUtility.SetDirty(u);
            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] UITheme reset to the code defaults: " + ThemePath);
        }

        static T LoadOrCreate<T>(string path, System.Action<T> seed) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            seed(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static void EnsureFolders()
        {
            EnsureFolder("Assets", "Game");
            foreach (var sub in new[] { "Scripts", "Data", "Scenes", "UI", "Materials" })
                EnsureFolder(GameDir, sub);
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
