using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Incremental.EditorTools
{
    /// <summary>
    /// Menu: Incremental / Validate Data. Runs <see cref="DataValidator"/> on every CelestialTable / UpgradeTable pair in the
    /// project and logs each problem as a console error. Also warns where an asset differs from the PrototypeSetup seed.
    /// </summary>
    public static class DataValidationMenu
    {
        [MenuItem("Incremental/Validate Data")]
        public static void ValidateMenu() => Validate();

        /// <summary>Returns the number of errors (warnings for seed drift are not counted).</summary>
        public static int Validate()
        {
            var celestial = LoadAll<CelestialTable>();
            var upgrades = LoadAll<UpgradeTable>();
            if (celestial.Count != 1 || upgrades.Count != 1)
            {
                Debug.LogError($"[Validate] expected exactly one CelestialTable and one UpgradeTable, found {celestial.Count} and {upgrades.Count}.");
                return 1;
            }
            var c = celestial[0];
            var u = upgrades[0];

            var errors = DataValidator.Validate(c, u);
            foreach (var e in errors) Debug.LogError("[Validate] " + e, e.StartsWith("CelestialTable") ? (Object)c : u);

            int drift = WarnSeedDrift(c, u);
            if (errors.Count == 0)
                Debug.Log($"[Validate] OK: {c.tiers.Count} tiers, {u.upgrades.Count} upgrades, {u.unlocks.Count} unlocks" +
                          (drift > 0 ? $" ({drift} seed differences, see warnings)" : string.Empty));
            return errors.Count;
        }

        /// <summary>PrototypeSetup seed code and the assets must hold the same values; warn where they differ.</summary>
        static int WarnSeedDrift(CelestialTable c, UpgradeTable u)
        {
            var diffs = new List<string>();
            foreach (var s in PrototypeSetup.SeedTiers())
            {
                var a = c.Get(s.tier);
                if (a == null) diffs.Add($"tier {s.tier} is in the seed but not in the asset");
                else if (a.requiredMass != s.requiredMass || a.salePrice != s.salePrice || a.sizePx != s.sizePx || a.color != s.color)
                    diffs.Add($"tier {s.tier} differs from the seed");
            }
            foreach (var s in PrototypeSetup.SeedUpgrades())
            {
                var a = u.Get(s.id);
                if (a == null) diffs.Add($"upgrade '{s.id}' is in the seed but not in the asset");
                else if (a.enabled != s.enabled || a.revealAtTier != s.revealAtTier || a.effectPerLevel != s.effectPerLevel ||
                         a.baseCost != s.baseCost || a.growth != s.growth || a.maxLevel != s.maxLevel)
                    diffs.Add($"upgrade '{s.id}' differs from the seed");
            }
            foreach (var s in PrototypeSetup.SeedUnlocks())
            {
                var a = u.GetUnlock(s.tier);
                if (a == null) diffs.Add($"unlock {s.tier} is in the seed but not in the asset");
                else if (a.cost != s.cost) diffs.Add($"unlock {s.tier} cost differs from the seed");
            }
            foreach (var d in diffs) Debug.LogWarning("[Validate] seed drift: " + d + " (update PrototypeSetup or the asset)");
            return diffs.Count;
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
