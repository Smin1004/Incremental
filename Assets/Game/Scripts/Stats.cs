using System;

namespace Incremental
{
    /// <summary>Effective per-run values after upgrades (and debug overrides).</summary>
    public struct EffectiveStats
    {
        public double spawnRate;
        public double pullAccel;
        public double saleMult;
        public double staminaMax;
        public double thresholdMult;
        public double dustCap;
    }

    /// <summary>The single place where upgrade levels turn into effective numbers. Effects are additive per level.</summary>
    public static class Stats
    {
        public static EffectiveStats Compute(GameParams p, UpgradeTable t, MetaState m)
        {
            double Effect(UpgradeId id)
            {
                var def = t != null ? t.Get(id) : null;
                return def != null ? def.effectPerLevel * m.GetLevel(id) : 0.0;
            }

            return new EffectiveStats
            {
                spawnRate = p.spawnRate + Effect(UpgradeId.SpawnRate),
                pullAccel = p.pullAccel * (1.0 + Effect(UpgradeId.PullAccel)),
                saleMult = p.saleMult * (1.0 + Effect(UpgradeId.SaleMult)),
                staminaMax = p.staminaMax + Effect(UpgradeId.StaminaMax),
                thresholdMult = p.thresholdMult * (1.0 - Effect(UpgradeId.ThresholdMult)),
                dustCap = p.dustCap,
            };
        }

        public static double Threshold(CelestialTier tier, in EffectiveStats s) => tier.requiredMass * s.thresholdMult;

        public static double Cost(UpgradeDef def, int level) => def.baseCost * Math.Pow(def.growth, level);

        /// <summary>Cost as displayed and charged: rounded up to an integer.</summary>
        public static double CostCeil(UpgradeDef def, int level) => Math.Ceiling(Cost(def, level));
    }
}
