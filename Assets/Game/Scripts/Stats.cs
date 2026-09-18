using System;

namespace Incremental
{
    /// <summary>Effective per-run values after upgrades (and debug overrides). Every upgrade target is read from here.</summary>
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
    /// The single place where upgrade levels turn into effective numbers. Effects are additive per level,
    /// except dust_mass which compounds (effect ^ level, 02 §3). Disabled upgrades contribute nothing.
    /// </summary>
    public static class Stats
    {
        public static EffectiveStats Compute(GameParams p, UpgradeTable t, MetaState m)
        {
            var s = new EffectiveStats
            {
                spawnRate = p.spawnRate + Add(t, m, UpgradeIds.SpawnRate),
                pullAccel = p.pullAccel * (1.0 + Add(t, m, UpgradeIds.PullAccel)),
                saleMult = p.saleMult * (1.0 + Add(t, m, UpgradeIds.SaleMult)),
                staminaMax = p.staminaMax + Add(t, m, UpgradeIds.StaminaMax),
                thresholdMult = p.thresholdMult * (1.0 - Add(t, m, UpgradeIds.ThresholdMult)),
                dustCap = p.dustCap + Add(t, m, UpgradeIds.DustCap),
                dustMass = p.dustMass * Compound(t, m, UpgradeIds.DustMass),
                gravityRadius = p.gravityRadius * (1.0 + Add(t, m, UpgradeIds.GravityRadius)),
            };
            double drain = Math.Max(0.0, 1.0 - Add(t, m, UpgradeIds.StaminaDrain));
            s.staminaIdleDrain = p.staminaIdleDrain * drain;
            s.staminaHoldDrain = p.staminaHoldDrain * drain;
            s.dustInitial = Math.Min(p.dustInitial + Add(t, m, UpgradeIds.StartBonus), s.dustCap);
            return s;
        }

        /// <summary>effectPerLevel × level, or 0 when the upgrade is missing or disabled.</summary>
        static double Add(UpgradeTable t, MetaState m, string id)
        {
            var def = t != null ? t.Get(id) : null;
            return def != null && def.enabled ? def.effectPerLevel * m.GetLevel(id) : 0.0;
        }

        /// <summary>effectPerLevel ^ level, or 1 when the upgrade is missing or disabled.</summary>
        static double Compound(UpgradeTable t, MetaState m, string id)
        {
            var def = t != null ? t.Get(id) : null;
            return def != null && def.enabled ? Math.Pow(def.effectPerLevel, m.GetLevel(id)) : 1.0;
        }

        /// <summary>True for ids whose effectPerLevel is a fraction shown as a percent.</summary>
        public static bool IsPercentEffect(string id) =>
            id == UpgradeIds.PullAccel || id == UpgradeIds.SaleMult || id == UpgradeIds.ThresholdMult ||
            id == UpgradeIds.GravityRadius || id == UpgradeIds.StaminaDrain;

        /// <summary>Number substituted into the upg.&lt;id&gt;.effect string: percent for fraction effects, raw otherwise.</summary>
        public static double EffectDisplayNumber(UpgradeDef def) =>
            IsPercentEffect(def.id) ? def.effectPerLevel * 100.0 : def.effectPerLevel;

        public static double Threshold(CelestialTier tier, in EffectiveStats s) => tier.requiredMass * s.thresholdMult;

        public static double Cost(UpgradeDef def, int level) => def.baseCost * Math.Pow(def.growth, level);

        /// <summary>
        /// Cost as displayed and charged: rounded up to an integer. A value within binary noise of an integer snaps to it
        /// (200 × 1.6^2 evaluates to 512.00000000000011 and must cost 512, not 513). The tolerance is capped so large
        /// costs are never rounded below their real value.
        /// </summary>
        public static double CostCeil(UpgradeDef def, int level)
        {
            double c = Cost(def, level);
            double r = Math.Round(c);
            if (Math.Abs(c - r) <= Math.Min(Math.Abs(c) * 1e-12, 1e-6)) return r;
            return Math.Ceiling(c);
        }
    }
}
