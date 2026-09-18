using System.Collections.Generic;

namespace Incremental
{
    /// <summary>
    /// Consistency checks for the data tables. Used by the editor menu Incremental/Validate Data and by the EditMode tests.
    /// Returns one message per problem; an empty list means the data is valid.
    /// </summary>
    public static class DataValidator
    {
        public static List<string> Validate(CelestialTable c, UpgradeTable u)
        {
            var errors = new List<string>();
            if (c == null) errors.Add("CelestialTable is missing.");
            if (u == null) errors.Add("UpgradeTable is missing.");
            if (errors.Count > 0) return errors;

            // Tiers: 1..N contiguous ascending, required mass strictly increasing, string key present.
            if (c.tiers.Count == 0) errors.Add("CelestialTable: no tiers.");
            for (int i = 0; i < c.tiers.Count; i++)
            {
                var t = c.tiers[i];
                if (t.tier != i + 1)
                    errors.Add($"CelestialTable: entry {i} has tier {t.tier}, expected {i + 1} (tiers must start at 1, contiguous, ascending).");
                if (i > 0 && t.requiredMass <= c.tiers[i - 1].requiredMass)
                    errors.Add($"CelestialTable: tier {t.tier} requiredMass {t.requiredMass} is not greater than tier {c.tiers[i - 1].tier} ({c.tiers[i - 1].requiredMass}).");
                if (t.requiredMass <= 0) errors.Add($"CelestialTable: tier {t.tier} requiredMass must be > 0.");
                if (t.salePrice <= 0) errors.Add($"CelestialTable: tier {t.tier} salePrice must be > 0.");
                if (!UIStrings.Has(UIStrings.TierKey(t.tier)))
                    errors.Add($"UIStrings: missing key '{UIStrings.TierKey(t.tier)}'.");
            }

            // Upgrades: unique known ids, string keys, sane cost curve, reachable reveal tier.
            var seen = new HashSet<string>();
            for (int i = 0; i < u.upgrades.Count; i++)
            {
                var d = u.upgrades[i];
                if (string.IsNullOrEmpty(d.id))
                {
                    errors.Add($"UpgradeTable: entry {i} has an empty id.");
                    continue;
                }
                if (!seen.Add(d.id)) errors.Add($"UpgradeTable: duplicate id '{d.id}'.");
                if (!UpgradeIds.IsKnown(d.id)) errors.Add($"UpgradeTable: id '{d.id}' has no effect in Stats.Compute.");
                if (!UIStrings.Has(UIStrings.UpgradeNameKey(d.id))) errors.Add($"UIStrings: missing key '{UIStrings.UpgradeNameKey(d.id)}'.");
                if (!UIStrings.Has(UIStrings.UpgradeEffectKey(d.id))) errors.Add($"UIStrings: missing key '{UIStrings.UpgradeEffectKey(d.id)}'.");
                if (d.baseCost <= 0) errors.Add($"UpgradeTable: '{d.id}' baseCost must be > 0.");
                if (d.growth <= 0) errors.Add($"UpgradeTable: '{d.id}' growth must be > 0.");
                if (d.maxLevel < 0) errors.Add($"UpgradeTable: '{d.id}' maxLevel must be >= 0.");
                if (d.revealAtTier < 1 || d.revealAtTier > c.MaxTier)
                    errors.Add($"UpgradeTable: '{d.id}' revealAtTier {d.revealAtTier} is outside 1..{c.MaxTier}; it would never be shown.");
            }

            // Unlocks: point at existing tiers above 1, no duplicates, and every tier above 1 has one (unlocks are sequential).
            var unlockTiers = new HashSet<int>();
            for (int i = 0; i < u.unlocks.Count; i++)
            {
                var un = u.unlocks[i];
                if (c.Get(un.tier) == null) errors.Add($"UpgradeTable: unlock {i} points at tier {un.tier}, which does not exist.");
                else if (un.tier <= 1) errors.Add($"UpgradeTable: unlock {i} points at tier {un.tier}; tier 1 is always unlocked.");
                if (!unlockTiers.Add(un.tier)) errors.Add($"UpgradeTable: duplicate unlock for tier {un.tier}.");
                if (un.cost < 0) errors.Add($"UpgradeTable: unlock for tier {un.tier} has a negative cost.");
            }
            for (int i = 0; i < c.tiers.Count; i++)
            {
                int tier = c.tiers[i].tier;
                if (tier > 1 && !unlockTiers.Contains(tier))
                    errors.Add($"UpgradeTable: tier {tier} has no unlock; unlocks are sequential, so tiers from {tier} up are unreachable.");
            }
            return errors;
        }
    }
}
