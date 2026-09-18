using System.Collections.Generic;

namespace Incremental
{
    /// <summary>Display state of a node (12 §4).</summary>
    public enum NodeState
    {
        /// <summary>minTier > unlockedMaxTier + 1: not drawn.</summary>
        Hidden,
        /// <summary>Next ring, or no prereq owned: drawn dim, with name / effect / cost.</summary>
        Locked,
        /// <summary>Purchase conditions 1-4 met and level 0 (currency decides whether the buy succeeds).</summary>
        Available,
        /// <summary>Level ≥ 1, not maxed (still purchasable).</summary>
        Owned,
        /// <summary>Level at maxLevel.</summary>
        Maxed,
    }

    /// <summary>
    /// Skill tree purchase rules (12 §2) as pure functions over MetaState + NodeTable. The shop panel and the autoplay bot
    /// both go through here. Costs are rounded up and charged as displayed.
    /// Gate levels are the single source of truth for unlocked tiers; <see cref="MetaState.unlockedMaxTier"/> is derived
    /// from them by <see cref="RecomputeUnlockedMaxTier"/>.
    /// </summary>
    public static class Shop
    {
        public static int Level(MetaState m, NodeDef n) => n != null ? m.GetLevel(n.id) : 0;

        /// <summary>Owned = level ≥ 1 and enabled (a disabled node counts as absent).</summary>
        public static bool IsOwned(MetaState m, NodeDef n) => n != null && n.enabled && m.GetLevel(n.id) >= 1;

        public static bool IsMaxed(MetaState m, NodeDef n) => n != null && n.maxLevel > 0 && m.GetLevel(n.id) >= n.maxLevel;

        /// <summary>Any-of: at least one prereq owned. No prereqs = next to the center, always met.</summary>
        public static bool PrereqsMet(NodeTable t, MetaState m, NodeDef n)
        {
            if (n.prereqs == null || n.prereqs.Count == 0) return true;
            for (int i = 0; i < n.prereqs.Count; i++)
                if (IsOwned(m, t.Get(n.prereqs[i]))) return true;
            return false;
        }

        /// <summary>Purchase conditions 1-4 of 12 §2 (everything except currency). Gates also need to be the next tier.</summary>
        public static bool IsPurchasable(NodeTable t, MetaState m, NodeDef n)
        {
            if (n == null || !n.enabled) return false;
            if (m.unlockedMaxTier < n.minTier) return false;
            if (!PrereqsMet(t, m, n)) return false;
            if (IsMaxed(m, n)) return false;
            if (n.IsGate && n.tier != m.unlockedMaxTier + 1) return false;
            return true;
        }

        /// <summary>Cost of the next level, rounded up.</summary>
        public static double Cost(NodeTable t, MetaState m, NodeDef n) =>
            n == null ? double.PositiveInfinity : Stats.CostCeil(t, n, m.GetLevel(n.id));

        public static double Cost(NodeTable t, MetaState m, string id) => Cost(t, m, t.Get(id));

        public static bool CanBuy(NodeTable t, MetaState m, string id)
        {
            var n = t.Get(id);
            return IsPurchasable(t, m, n) && m.currency >= Cost(t, m, n);
        }

        public static bool Buy(NodeTable t, MetaState m, string id)
        {
            if (!CanBuy(t, m, id)) return false;
            var n = t.Get(id);
            m.currency -= Cost(t, m, n);
            m.SetLevel(id, m.GetLevel(id) + 1);
            if (n.IsGate) RecomputeUnlockedMaxTier(t, m);
            return true;
        }

        public static NodeState State(NodeTable t, MetaState m, NodeDef n)
        {
            if (n.minTier > m.unlockedMaxTier + 1) return NodeState.Hidden;
            if (IsMaxed(m, n)) return NodeState.Maxed;
            if (!IsPurchasable(t, m, n)) return NodeState.Locked;
            return m.GetLevel(n.id) >= 1 ? NodeState.Owned : NodeState.Available;
        }

        // ---------------- gates ----------------

        /// <summary>The gate of the tier right above the unlocked maximum, or null when every tier is unlocked.</summary>
        public static NodeDef NextGate(NodeTable t, MetaState m)
        {
            var g = t.Gate(m.unlockedMaxTier + 1);
            return g != null && g.enabled ? g : null;
        }

        /// <summary>unlockedMaxTier = 1 + number of consecutively owned gates (tier 2, 3, …). Returns the new value.</summary>
        public static int RecomputeUnlockedMaxTier(NodeTable t, MetaState m)
        {
            int tier = 1;
            while (IsOwned(m, t.Gate(tier + 1))) tier++;
            m.unlockedMaxTier = tier;
            return tier;
        }

        /// <summary>Debug (F6): raise the next gate to level 1 without paying. Returns the unlocked tier, or 0.</summary>
        public static int UnlockNextFree(NodeTable t, MetaState m)
        {
            var g = NextGate(t, m);
            if (g == null) return 0;
            m.SetLevel(g.id, 1);
            RecomputeUnlockedMaxTier(t, m);
            return g.tier;
        }

        /// <summary>Purchasable stat nodes (12 §2 conditions 1-4), cheapest first. Gates are not included.</summary>
        public static void PurchasableStatNodes(NodeTable t, MetaState m, List<NodeDef> result)
        {
            result.Clear();
            for (int i = 0; i < t.nodes.Count; i++)
            {
                var n = t.nodes[i];
                if (!n.IsGate && IsPurchasable(t, m, n)) result.Add(n);
            }
            // Stable insertion sort by cost (few dozen nodes at most); equal costs keep table order.
            for (int i = 1; i < result.Count; i++)
            {
                var x = result[i];
                double cx = Cost(t, m, x);
                int j = i - 1;
                while (j >= 0 && Cost(t, m, result[j]) > cx)
                {
                    result[j + 1] = result[j];
                    j--;
                }
                result[j + 1] = x;
            }
        }
    }
}
