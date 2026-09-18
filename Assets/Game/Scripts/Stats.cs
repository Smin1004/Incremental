using System;

namespace Incremental
{
    /// <summary>
    /// Effective per-run values after skill tree nodes (and debug overrides). Every stat target is read from here.
    /// Serializable because the values at run start are kept in the run history and run_log.csv.
    /// </summary>
    [Serializable]
    public struct EffectiveStats
    {
        public double spawnRate;
        public double pullAccel;
        public double saleMult;
        public double staminaMax;
        public double thresholdMult;
        public double dustCap;
        public double dustMass;
        public double gravityRadius;
        public double staminaIdleDrain;
        public double staminaHoldDrain;
        public double dustInitial;
    }

    /// <summary>
    /// The single place where node levels turn into effective numbers (12 §3 "스탯 합산").
    /// Nodes with the same statId add up (effect × level); dust_mass multiplies (effect ^ level);
    /// threshold_mult and stamina_drain reductions are capped at 90%. Disabled nodes contribute nothing.
    /// </summary>
    public static class Stats
    {
        /// <summary>Largest total reduction for threshold_mult and stamina_drain.</summary>
        public const double MaxReduction = 0.9;

        public static EffectiveStats Compute(GameParams p, NodeTable t, MetaState m)
        {
            double spawn = 0, pull = 0, sale = 0, stamina = 0, threshold = 0, cap = 0, radius = 0, drain = 0, start = 0;
            double mass = 1;
            var levels = m.upgradeLevels;
            for (int i = 0; i < levels.Count; i++)
            {
                int level = levels[i].level;
                if (level <= 0 || t == null) continue;
                var n = t.Get(levels[i].id);
                if (n == null || !n.enabled || n.kind != NodeKind.Stat) continue;
                if (n.maxLevel > 0 && level > n.maxLevel) level = n.maxLevel;
                double add = n.effectPerLevel * level;
                switch (n.statId)
                {
                    case StatIds.SpawnRate: spawn += add; break;
                    case StatIds.PullAccel: pull += add; break;
                    case StatIds.SaleMult: sale += add; break;
                    case StatIds.StaminaMax: stamina += add; break;
                    case StatIds.ThresholdMult: threshold += add; break;
                    case StatIds.DustCap: cap += add; break;
                    case StatIds.DustMass: mass *= Math.Pow(n.effectPerLevel, level); break;
                    case StatIds.GravityRadius: radius += add; break;
                    case StatIds.StaminaDrain: drain += add; break;
                    case StatIds.StartBonus: start += add; break;
                }
            }

            var s = new EffectiveStats
            {
                spawnRate = p.spawnRate + spawn,
                pullAccel = p.pullAccel * (1.0 + pull),
                saleMult = p.saleMult * (1.0 + sale),
                staminaMax = p.staminaMax + stamina,
                thresholdMult = p.thresholdMult * (1.0 - Math.Min(threshold, MaxReduction)),
                dustCap = p.dustCap + cap,
                dustMass = p.dustMass * mass,
                gravityRadius = p.gravityRadius * (1.0 + radius),
            };
            double drainMult = 1.0 - Math.Min(drain, MaxReduction);
            s.staminaIdleDrain = p.staminaIdleDrain * drainMult;
            s.staminaHoldDrain = p.staminaHoldDrain * drainMult;
            s.dustInitial = Math.Min(p.dustInitial + start, s.dustCap);
            return s;
        }

        /// <summary>True for stats whose effectPerLevel is a fraction shown as a percent.</summary>
        public static bool IsPercentEffect(string statId) =>
            statId == StatIds.PullAccel || statId == StatIds.SaleMult || statId == StatIds.ThresholdMult ||
            statId == StatIds.GravityRadius || statId == StatIds.StaminaDrain;

        /// <summary>Number substituted into the stat.&lt;id&gt;.effect string: percent for fraction effects, raw otherwise.</summary>
        public static double EffectDisplayNumber(NodeDef n) =>
            IsPercentEffect(n.statId) ? n.effectPerLevel * 100.0 : n.effectPerLevel;

        public static double Threshold(CelestialTier tier, in EffectiveStats s) => tier.requiredMass * s.thresholdMult;

        /// <summary>Income of one planet of this tier: sale price × sale multiplier, rounded up, so currency stays an integer (12 §10-9).</summary>
        public static double SaleIncome(CelestialTier tier, in EffectiveStats s) => CeilSnap(tier.salePrice * s.saleMult);

        /// <summary>Raw cost of the next level: baseCost × ringCostMult[minTier] × growth ^ level (12 §2).</summary>
        public static double Cost(NodeTable t, NodeDef n, int level) =>
            n.baseCost * (t != null ? t.RingCostMult(n.minTier) : 1.0) * Math.Pow(n.growth, level);

        /// <summary>Cost as displayed and charged: rounded up to an integer.</summary>
        public static double CostCeil(NodeTable t, NodeDef n, int level) => CeilSnap(Cost(t, n, level));

        /// <summary>
        /// Rounds up to an integer. A value within binary noise of an integer snaps to it
        /// (200 × 1.6^2 evaluates to 512.00000000000011 and must give 512, not 513). The tolerance is capped so large
        /// values are never rounded below their real value.
        /// </summary>
        public static double CeilSnap(double v)
        {
            double r = Math.Round(v);
            if (Math.Abs(v - r) <= Math.Min(Math.Abs(v) * 1e-12, 1e-6)) return r;
            return Math.Ceiling(v);
        }
    }
}
