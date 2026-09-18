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
            if (celestial.Count != 1 || nodes.Count != 1)
            {
                Debug.LogError($"[Validate] expected exactly one CelestialTable and one NodeTable, found {celestial.Count} and {nodes.Count}.");
                return 1;
            }
            var c = celestial[0];
            var n = nodes[0];

            var report = DataValidator.Validate(c, n);
            foreach (var e in report.Errors) Debug.LogError("[Validate] " + e, e.StartsWith("CelestialTable") ? (Object)c : n);
            foreach (var w in report.Warnings) Debug.LogWarning("[Validate] " + w, n);

            int drift = WarnSeedDrift(c, n);
            if (report.Ok)
            {
                int gates = 0;
                foreach (var node in n.nodes) if (node.IsGate) gates++;
                Debug.Log($"[Validate] OK: {c.tiers.Count} tiers, {n.nodes.Count - gates} stat nodes, {gates} gates" +
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

        static bool SameNode(NodeDef a, NodeDef s)
        {
            // Unity serializes a null string as "".
            if (a.kind != s.kind || (a.statId ?? "") != (s.statId ?? "") || a.effectPerLevel != s.effectPerLevel || a.maxLevel != s.maxLevel ||
                a.baseCost != s.baseCost || a.growth != s.growth || a.tier != s.tier || a.minTier != s.minTier ||
                a.angleDeg != s.angleDeg || a.enabled != s.enabled)
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
