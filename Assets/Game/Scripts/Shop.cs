using System;

namespace Incremental
{
    /// <summary>
    /// Purchase rules as pure functions over MetaState + UpgradeTable.
    /// The shop panel and the autoplay bot both go through here. Costs are rounded up and charged as displayed.
    /// </summary>
    public static class Shop
    {
        /// <summary>Shown in the shop: enabled, and the highest unlocked tier has reached revealAtTier.</summary>
        public static bool IsVisible(UpgradeDef def, MetaState m) =>
            def != null && def.enabled && m.unlockedMaxTier >= def.revealAtTier;

        public static bool IsMaxed(UpgradeDef def, MetaState m) =>
            def != null && def.maxLevel > 0 && m.GetLevel(def.id) >= def.maxLevel;

        public static bool IsMaxed(UpgradeTable t, MetaState m, string id) => IsMaxed(t.Get(id), m);

        /// <summary>Cost of the next level: baseCost × growth ^ level, rounded up.</summary>
        public static double UpgradeCost(UpgradeDef def, MetaState m) =>
            def == null ? double.PositiveInfinity : Stats.CostCeil(def, m.GetLevel(def.id));

        public static double UpgradeCost(UpgradeTable t, MetaState m, string id) => UpgradeCost(t.Get(id), m);

        public static bool CanBuyUpgrade(UpgradeTable t, MetaState m, string id)
        {
            var def = t.Get(id);
            return IsVisible(def, m) && !IsMaxed(def, m) && m.currency >= UpgradeCost(def, m);
        }

        public static bool BuyUpgrade(UpgradeTable t, MetaState m, string id)
        {
            if (!CanBuyUpgrade(t, m, id)) return false;
            m.currency -= UpgradeCost(t, m, id);
            m.SetLevel(id, m.GetLevel(id) + 1);
            return true;
        }

        public static bool IsUnlocked(MetaState m, int tier) => tier <= m.unlockedMaxTier;

        /// <summary>Unlocks are sequential: only the tier right above the current maximum can be bought.</summary>
        public static bool IsNextUnlock(MetaState m, int tier) => tier == m.unlockedMaxTier + 1;

        /// <summary>The tier the shop offers next, or 0 when every unlock in the table is bought.</summary>
        public static int NextUnlockTier(UpgradeTable t, MetaState m)
        {
            var u = t.GetUnlock(m.unlockedMaxTier + 1);
            return u != null ? u.tier : 0;
        }

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

        /// <summary>Debug (F6): unlock the next tier without paying. Returns the unlocked tier, or 0.</summary>
        public static int UnlockNextFree(UpgradeTable t, MetaState m)
        {
            int next = NextUnlockTier(t, m);
            if (next > 0) m.unlockedMaxTier = next;
            return next;
        }
    }
}
