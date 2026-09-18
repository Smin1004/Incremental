using System;
using System.Collections.Generic;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// Upgrade ids known to <see cref="Stats.Compute"/>. Saves, logs and string keys all use these ids.
    /// A table entry whose id is not listed here has no effect (Validate Data reports it).
    /// </summary>
    public static class UpgradeIds
    {
        public const string SpawnRate = "spawn_rate";
        public const string PullAccel = "pull_accel";
        public const string SaleMult = "sale_mult";
        public const string StaminaMax = "stamina_max";
        public const string ThresholdMult = "threshold_mult";
        public const string DustCap = "dust_cap";
        public const string DustMass = "dust_mass";
        public const string GravityRadius = "gravity_radius";
        public const string StaminaDrain = "stamina_drain";
        public const string StartBonus = "start_bonus";

        public static readonly string[] All =
        {
            SpawnRate, PullAccel, SaleMult, StaminaMax, ThresholdMult,
            DustCap, DustMass, GravityRadius, StaminaDrain, StartBonus,
        };

        public static bool IsKnown(string id) => Array.IndexOf(All, id) >= 0;
    }

    [Serializable]
    public sealed class UpgradeDef
    {
        [Tooltip("Stable string id (see UpgradeIds). Used by saves, logs and UIStrings keys upg.<id>.name / upg.<id>.effect.")]
        public string id;
        [Tooltip("Off: hidden from the shop, not purchasable, and existing levels have no effect.")]
        public bool enabled = true;
        [Tooltip("Shown in the shop once the highest unlocked tier is at least this value.")]
        public int revealAtTier = 1;
        [Tooltip("Per level. Additive for every id except dust_mass (compound: effect ^ level). " +
                 "Fraction ids (pull_accel, sale_mult, threshold_mult, gravity_radius, stamina_drain) are 0.2 = 20%.")]
        public double effectPerLevel;
        public double baseCost;
        public double growth;
        [Tooltip("0 = unlimited")]
        public int maxLevel;
    }

    [Serializable]
    public sealed class UnlockDef
    {
        public int tier;
        public double cost;
    }

    /// <summary>Upgrades and tier unlocks (10 §5, 02 §3). Cost = baseCost × growth ^ level.</summary>
    [CreateAssetMenu(fileName = "UpgradeTable", menuName = "Incremental/Upgrade Table")]
    public sealed class UpgradeTable : ScriptableObject
    {
        public List<UpgradeDef> upgrades = new List<UpgradeDef>();
        public List<UnlockDef> unlocks = new List<UnlockDef>();

        public UpgradeDef Get(string id)
        {
            for (int i = 0; i < upgrades.Count; i++)
                if (upgrades[i].id == id) return upgrades[i];
            return null;
        }

        public UnlockDef GetUnlock(int tier)
        {
            for (int i = 0; i < unlocks.Count; i++)
                if (unlocks[i].tier == tier) return unlocks[i];
            return null;
        }
    }
}
