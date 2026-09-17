using System;

namespace Incremental
{
    /// <summary>
    /// Purchase rules (spec §5) as pure functions over MetaState + UpgradeTable.
    /// The shop panel and the autoplay bot both go through here. Costs are rounded up and charged as displayed.
    /// </summary>
    public static class Shop
    {
        public static bool IsMaxed(UpgradeTable t, MetaState m, UpgradeId id)
        {
            var def = t.Get(id);
            return def != null && def.maxLevel > 0 && m.GetLevel(id) >= def.maxLevel;
        }

        /// <summary>Cost of the next level: baseCost × growth ^ level, rounded up.</summary>
        public static double UpgradeCost(UpgradeTable t, MetaState m, UpgradeId id)
        {
            var def = t.Get(id);
            return def == null ? double.PositiveInfinity : Stats.CostCeil(def, m.GetLevel(id));
        }

        public static bool CanBuyUpgrade(UpgradeTable t, MetaState m, UpgradeId id) =>
            t.Get(id) != null && !IsMaxed(t, m, id) && m.currency >= UpgradeCost(t, m, id);

        public static bool BuyUpgrade(UpgradeTable t, MetaState m, UpgradeId id)
        {
            if (!CanBuyUpgrade(t, m, id)) return false;
            m.currency -= UpgradeCost(t, m, id);
            m.SetLevel(id, m.GetLevel(id) + 1);
            return true;
        }

        public static bool IsUnlocked(MetaState m, int tier) => tier <= m.unlockedMaxTier;

        /// <summary>Unlocks are sequential: only the tier right above the current maximum can be bought.</summary>
        public static bool IsNextUnlock(MetaState m, int tier) => tier == m.unlockedMaxTier + 1;

        public static double UnlockCost(UpgradeTable t, int tier)
        {
            var u = t.GetUnlock(tier);
            return u == null ? double.PositiveInfinity : Math.Ceiling(u.cost);
        }

        public static bool CanUnlock(UpgradeTable t, MetaState m, int tier) =>
            t.GetUnlock(tier) != null && IsNextUnlock(m, tier) && m.currency >= UnlockCost(t, tier);

        public static bool Unlock(UpgradeTable t, MetaState m, int tier)
        {
            if (!CanUnlock(t, m, tier)) return false;
            m.currency -= UnlockCost(t, tier);
            m.unlockedMaxTier = tier;
            return true;
        }
    }
}
