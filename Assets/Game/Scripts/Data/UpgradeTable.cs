using System;
using System.Collections.Generic;
using UnityEngine;

namespace Incremental
{
    /// <summary>Index into MetaState.upgradeLevels. Do not reorder.</summary>
    public enum UpgradeId
    {
        SpawnRate = 0,
        PullAccel = 1,
        SaleMult = 2,
        StaminaMax = 3,
        ThresholdMult = 4,
    }

    [Serializable]
    public sealed class UpgradeDef
    {
        public UpgradeId id;
        [Tooltip("Additive per level. SpawnRate: +n per second. StaminaMax: +n. PullAccel / SaleMult: +fraction. ThresholdMult: -fraction.")]
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

    /// <summary>Upgrades and tier unlocks (spec §6). Cost = baseCost × growth ^ level.</summary>
    [CreateAssetMenu(fileName = "UpgradeTable", menuName = "Incremental/Upgrade Table")]
    public sealed class UpgradeTable : ScriptableObject
    {
        /// <summary>Number of entries in <see cref="UpgradeId"/>. Structural, not a balance value.</summary>
        public const int UpgradeCount = 5;

        public List<UpgradeDef> upgrades = new List<UpgradeDef>();
        public List<UnlockDef> unlocks = new List<UnlockDef>();

        public UpgradeDef Get(UpgradeId id)
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
