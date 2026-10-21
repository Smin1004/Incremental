using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Incremental.EditorTools
{
    /// <summary>
    /// Menu: Incremental / Validate Data. Runs <see cref="DataValidator"/> on the CelestialTable / NodeTable pair in the
    /// project: errors go to the console as errors, warnings as warnings. Also warns where an asset differs from the seed code.
    /// </summary>
    public static class DataValidationMenu
    {
        [MenuItem("Incremental/Validate Data")]
        public static void ValidateMenu() => Validate();

        /// <summary>Returns the number of errors (warnings are not counted).</summary>
        public static int Validate()
        {
            var celestial = LoadAll<CelestialTable>();
            var nodes = LoadAll<NodeTable>();
            // 29 Q9, §10.1: Data/NodeTable_v1.asset and _v5.asset are old trees, kept only to price the save refunds; the tree is the other one.
            nodes.RemoveAll(t => AssetDatabase.GetAssetPath(t).EndsWith("_v1.asset") || AssetDatabase.GetAssetPath(t).EndsWith("_v5.asset"));
            if (celestial.Count != 1 || nodes.Count != 1)
            {
                Debug.LogError($"[Validate] expected exactly one CelestialTable and one NodeTable, found {celestial.Count} and {nodes.Count}.");
                return 1;
            }
            var c = celestial[0];
            var n = nodes[0];

            var gameParams = LoadAll<GameParams>();
            if (gameParams.Count != 1)
            {
                Debug.LogError($"[Validate] expected exactly one GameParams, found {gameParams.Count}.");
                return 1;
            }

            var abilities = LoadAll<AbilityTable>();
            if (abilities.Count != 1)
            {
                Debug.LogError($"[Validate] expected exactly one AbilityTable, found {abilities.Count} (menu Incremental/Setup/1. Create Data Assets).");
                return 1;
            }
            var a = abilities[0];

            var report = DataValidator.Validate(c, n, gameParams[0], a);
            // 13th pass B5: the project tree's size, and its line sums beside the v1 tree (29 §3).
            DataValidator.ValidateTreeSize(n, report);
            var v1 = AssetDatabase.LoadAssetAtPath<NodeTable>("Assets/Game/Data/NodeTable_v1.asset");
            Debug.Log("[Validate] line totals, tree v2 against v1 (29 §3):\n" + DataValidator.LineTotals(n, v1));
            foreach (var e in report.Errors)
                Debug.LogError("[Validate] " + e, e.StartsWith("CelestialTable") ? (Object)c : e.StartsWith("AbilityTable") ? (Object)a : n);
            foreach (var w in report.Warnings) Debug.LogWarning("[Validate] " + w, n);

            var paramReport = DataValidator.ValidateParams(gameParams[0], c);
            foreach (var e in paramReport.Errors) Debug.LogError("[Validate] " + e, gameParams[0]);
            foreach (var w in paramReport.Warnings) Debug.LogWarning("[Validate] " + w, gameParams[0]);
            report.Errors.AddRange(paramReport.Errors);
            report.Warnings.AddRange(paramReport.Warnings);

            // 26 §4: the UI theme. Its values are tuned by eye in the inspector, so only what would break the UI is an error.
            var themes = LoadAll<UITheme>();
            if (themes.Count != 1)
            {
                Debug.LogError($"[Validate] expected exactly one UITheme, found {themes.Count} (menu Incremental/Setup/1. Create Data Assets).");
                return 1;
            }
            var themeReport = DataValidator.ValidateTheme(themes[0]);
            foreach (var e in themeReport.Errors) Debug.LogError("[Validate] " + e, themes[0]);
            foreach (var w in themeReport.Warnings) Debug.LogWarning("[Validate] " + w, themes[0]);
            report.Errors.AddRange(themeReport.Errors);
            report.Warnings.AddRange(themeReport.Warnings);

            int drift = WarnSeedDrift(c, n) + WarnParamsDrift(gameParams[0]) + WarnAbilityDrift(a);
            if (report.Ok)
            {
                int gates = 0;
                foreach (var node in n.nodes) if (node.IsGate) gates++;
                Debug.Log($"[Validate] OK: {c.tiers.Count} tiers, {n.nodes.Count - gates} stat nodes, {gates} gates, {a.Count} abilities" +
                          (report.Warnings.Count > 0 ? $", {report.Warnings.Count} warnings" : string.Empty) +
                          (drift > 0 ? $", {drift} seed differences (see warnings)" : string.Empty));
            }
            return report.Errors.Count;
        }

        /// <summary>PrototypeSetup / SkillTreeSeed and the assets must hold the same values; warn where they differ.</summary>
        static int WarnSeedDrift(CelestialTable c, NodeTable n)
        {
            var diffs = new List<string>();
            foreach (var s in PrototypeSetup.SeedTiers())
            {
                var a = c.Get(s.tier);
                if (a == null) diffs.Add($"tier {s.tier} is in the seed but not in the asset");
                else if (a.requiredMass != s.requiredMass || a.salePrice != s.salePrice || a.sizePx != s.sizePx || a.color != s.color)
                    diffs.Add($"tier {s.tier} differs from the seed");
                else if (!s.look.SameAs(a.look))
                    diffs.Add($"tier {s.tier} look differs from the seed (menu Incremental/Setup/4. Apply Seed Looks)");
            }
            var seedIds = new HashSet<string>();
            foreach (var s in SkillTreeSeed.Nodes())
            {
                seedIds.Add(s.id);
                var a = n.Get(s.id);
                if (a == null) diffs.Add($"node '{s.id}' is in the seed but not in the asset");
                else if (!SameNode(a, s)) diffs.Add($"node '{s.id}' differs from the seed");
            }
            foreach (var a in n.nodes)
                if (a != null && !seedIds.Contains(a.id)) diffs.Add($"node '{a.id}' is in the asset but not in the seed");
            var mult = SkillTreeSeed.RingCostMult();
            for (int i = 0; i < mult.Length; i++)
                if (n.RingCostMult(i) != mult[i]) diffs.Add($"ringCostMult[{i}] is {n.RingCostMult(i)}, seed {mult[i]}");
            foreach (var d in diffs) Debug.LogWarning("[Validate] seed drift: " + d + " (update the seed code or the asset)");
            return diffs.Count;
        }

        /// <summary>
        /// GameParams field initializers are its seed (PrototypeSetup creates the asset from them); warn per top-level field
        /// where the asset holds another value. Copy a field over with <see cref="CopyParamsFromCode"/>.
        /// </summary>
        static int WarnParamsDrift(GameParams asset)
        {
            var fresh = ScriptableObject.CreateInstance<GameParams>();
            try
            {
                var diffs = ParamsDrift(asset, fresh);
                foreach (var d in diffs) Debug.LogWarning("[Validate] seed drift: GameParams." + d + " differs from the code default (update the initializer or the asset)", asset);
                return diffs.Count;
            }
            finally
            {
                Object.DestroyImmediate(fresh);
            }
        }

        static List<string> ParamsDrift(GameParams asset, GameParams fresh)
        {
            var diffs = new List<string>();
            var a = new SerializedObject(asset);
            var b = new SerializedObject(fresh);
            var it = b.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.propertyPath == "m_Script") continue;
                var ap = a.FindProperty(it.propertyPath);
                if (ap == null || !SerializedProperty.DataEquals(ap, it)) diffs.Add(it.propertyPath);
            }
            return diffs;
        }

        /// <summary>
        /// The AbilityTable's field initializers are its seed, as with GameParams (24 §8). The developer tunes the asset
        /// in the inspector, so this names each value that moved, leaf by leaf: the warning is the list of what was tuned.
        /// </summary>
        static int WarnAbilityDrift(AbilityTable asset)
        {
            var fresh = ScriptableObject.CreateInstance<AbilityTable>();
            try
            {
                var diffs = LeafDrift(asset, fresh);
                foreach (var d in diffs)
                    Debug.LogWarning("[Validate] seed drift: AbilityTable." + d + " differs from the code default (update the initializer or the asset)", asset);
                return diffs.Count;
            }
            finally
            {
                Object.DestroyImmediate(fresh);
            }
        }

        /// <summary>Property paths where two objects of one type differ, walking into nested rows down to single values.</summary>
        static List<string> LeafDrift(Object asset, Object fresh)
        {
            var diffs = new List<string>();
            var a = new SerializedObject(asset);
            var b = new SerializedObject(fresh);
            var it = b.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = it.propertyType == SerializedPropertyType.Generic && !it.isArray;
                if (enter || it.propertyPath == "m_Script") continue;
                var ap = a.FindProperty(it.propertyPath);
                if (ap == null || !SerializedProperty.DataEquals(ap, it)) diffs.Add(it.propertyPath);
            }
            return diffs;
        }

        /// <summary>
        /// Copies every value where the AbilityTable asset differs from the code defaults (after changing initializers).
        /// Returns the copied property paths. Overwrites what was tuned in the inspector: use it only when the code is the truth.
        /// </summary>
        public static string[] SyncAbilitiesFromCode()
        {
            var asset = LoadAll<AbilityTable>()[0];
            var fresh = ScriptableObject.CreateInstance<AbilityTable>();
            string[] diffs;
            try
            {
                diffs = LeafDrift(asset, fresh).ToArray();
            }
            finally
            {
                Object.DestroyImmediate(fresh);
            }
            if (diffs.Length > 0) CopyAbilitiesFromCode(diffs);
            return diffs;
        }

        /// <summary>
        /// Copies the named values (property paths such as "meteor.passMaxRadii") from the code defaults into the
        /// AbilityTable asset and saves it, leaving every other value as it is. This is the one to use after changing an
        /// initializer: the developer tunes this asset by hand, and <see cref="SyncAbilitiesFromCode"/> would undo that.
        /// A path the asset does not differ in is written all the same, which is what puts a newly added field into the file.
        /// </summary>
        public static void CopyAbilitiesFromCode(params string[] paths)
        {
            var asset = LoadAll<AbilityTable>()[0];
            var fresh = ScriptableObject.CreateInstance<AbilityTable>();
            try
            {
                var a = new SerializedObject(asset);
                var b = new SerializedObject(fresh);
                foreach (var path in paths)
                {
                    var src = b.FindProperty(path);
                    if (src == null)
                    {
                        Debug.LogError("[Setup] AbilityTable has no value at " + path);
                        continue;
                    }
                    a.CopyFromSerializedProperty(src);
                }
                a.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                Object.DestroyImmediate(fresh);
            }
        }

        /// <summary>Copies the named top-level fields from the code defaults into the GameParams asset (after changing an initializer).</summary>
        public static void CopyParamsFromCode(params string[] fields)
        {
            var asset = LoadAll<GameParams>()[0];
            var fresh = ScriptableObject.CreateInstance<GameParams>();
            var a = new SerializedObject(asset);
            var b = new SerializedObject(fresh);
            foreach (var f in fields)
            {
                var src = b.FindProperty(f);
                if (src == null)
                {
                    Debug.LogError("[Setup] GameParams has no field " + f);
                    continue;
                }
                a.CopyFromSerializedProperty(src);
            }
            a.ApplyModifiedPropertiesWithoutUndo();
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Copies every top-level field where the GameParams asset differs from the code defaults (after changing
        /// initializers). Returns the copied field names. Overwrites hand edits in the asset: use it only when the code is the truth.
        /// </summary>
        public static string[] SyncParamsFromCode()
        {
            var asset = LoadAll<GameParams>()[0];
            var fresh = ScriptableObject.CreateInstance<GameParams>();
            string[] diffs;
            try
            {
                diffs = ParamsDrift(asset, fresh).ToArray();
            }
            finally
            {
                Object.DestroyImmediate(fresh);
            }
            if (diffs.Length > 0) CopyParamsFromCode(diffs);
            return diffs;
        }

        static bool SameNode(NodeDef a, NodeDef s)
        {
            // Unity serializes a null string as "".
            if (a.kind != s.kind || (a.statId ?? "") != (s.statId ?? "") || a.effectPerLevel != s.effectPerLevel || a.maxLevel != s.maxLevel ||
                a.baseCost != s.baseCost || a.growth != s.growth || a.tier != s.tier || a.minTier != s.minTier ||
                a.angleDeg != s.angleDeg || a.enabled != s.enabled || (a.abilityId ?? "") != (s.abilityId ?? "") ||
                a.radiusOffset != s.radiusOffset || a.category != s.category || (a.ruleId ?? "") != (s.ruleId ?? "") ||
                (a.dustKindId ?? "") != (s.dustKindId ?? ""))
                return false;
            var ap = a.prereqs ?? new List<string>();
            var sp = s.prereqs ?? new List<string>();
            if (ap.Count != sp.Count) return false;
            for (int i = 0; i < ap.Count; i++) if (ap[i] != sp[i]) return false;
            return true;
        }

        static List<T> LoadAll<T>() where T : ScriptableObject
        {
            var list = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) list.Add(asset);
            }
            return list;
        }
    }
}
