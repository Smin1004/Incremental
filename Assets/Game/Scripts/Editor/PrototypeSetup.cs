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
        const string MaterialPath = MaterialsDir + "/DustUnlit.mat";
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
            int added = AddMissing(c, n);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Setup] Data assets ready ({added} missing entries added): {AssetDatabase.GetAssetPath(p)}, {AssetDatabase.GetAssetPath(c)}, {AssetDatabase.GetAssetPath(n)}");
        }

        // ---------------- seed data (keep equal to the assets) ----------------
        // Tiers 1-3 are the week-1 values. Tiers 4-11: mass and price from 10 §3.4, size / color are week-2 placeholders.
        // Skill tree nodes: SkillTreeSeed (12 §6).

        public static List<CelestialTier> SeedTiers() => new List<CelestialTier>
        {
            new CelestialTier { tier = 1, requiredMass = 10, salePrice = 1, sizePx = 12, color = new Color(0.62f, 0.62f, 0.62f) },
            new CelestialTier { tier = 2, requiredMass = 25, salePrice = 6, sizePx = 16, color = new Color(0.55f, 0.8f, 1f) },
            new CelestialTier { tier = 3, requiredMass = 60, salePrice = 40, sizePx = 24, color = new Color(0.62f, 0.42f, 0.25f) },
            new CelestialTier { tier = 4, requiredMass = 150, salePrice = 250, sizePx = 32, color = new Color(0.8f, 0.8f, 0.82f) },
            new CelestialTier { tier = 5, requiredMass = 400, salePrice = 2000, sizePx = 40, color = new Color(0.6f, 0.3f, 0.2f) },
            new CelestialTier { tier = 6, requiredMass = 1000, salePrice = 15000, sizePx = 52, color = new Color(0.25f, 0.5f, 0.95f) },
            new CelestialTier { tier = 7, requiredMass = 2500, salePrice = 1.2e5, sizePx = 68, color = new Color(0.8f, 0.65f, 0.35f) },
            new CelestialTier { tier = 8, requiredMass = 6000, salePrice = 1e6, sizePx = 80, color = new Color(0.45f, 0.2f, 0.14f) },
            new CelestialTier { tier = 9, requiredMass = 15000, salePrice = 1e7, sizePx = 96, color = new Color(0.95f, 0.25f, 0.2f) },
            new CelestialTier { tier = 10, requiredMass = 40000, salePrice = 1e8, sizePx = 116, color = new Color(1f, 0.9f, 0.35f) },
            new CelestialTier { tier = 11, requiredMass = 1e5, salePrice = 1e9, sizePx = 140, color = new Color(0.72f, 0.85f, 1f) },
        };

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
            Debug.Log("[Setup] Render assets ready: " + MaterialPath + ", " + CirclePath);
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
            root.unlitMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
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
