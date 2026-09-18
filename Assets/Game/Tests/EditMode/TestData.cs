using System.Collections.Generic;
using UnityEngine;

namespace Incremental.Tests
{
    /// <summary>
    /// Test-local tables and parameters with round numbers, independent of the balance values in the project assets,
    /// so expected results can be written down directly.
    /// </summary>
    static class TestData
    {
        public static GameParams Params()
        {
            var p = ScriptableObject.CreateInstance<GameParams>();
            p.dustInitial = 100;
            p.spawnRate = 4;
            p.dustCap = 300;
            p.dustMass = 1;
            p.gravityRadius = 180;
            p.pullAccel = 600;
            p.staminaMax = 60;
            p.staminaHoldDrain = 1.0;
            p.staminaIdleDrain = 0.2;
            p.saleMult = 1.0;
            p.thresholdMult = 1.0;
            return p;
        }

        public static UpgradeTable Upgrades()
        {
            var t = ScriptableObject.CreateInstance<UpgradeTable>();
            t.upgrades = new List<UpgradeDef>
            {
                Def(UpgradeIds.SpawnRate, 1, 10, 1.25, 0),
                Def(UpgradeIds.PullAccel, 0.2, 10, 1.25, 0),
                Def(UpgradeIds.SaleMult, 0.25, 15, 1.3, 0),
                Def(UpgradeIds.StaminaMax, 10, 20, 1.3, 0),
                Def(UpgradeIds.ThresholdMult, 0.04, 25, 1.35, 10),
                Def(UpgradeIds.DustCap, 100, 30, 1.3, 27),
                Def(UpgradeIds.DustMass, 1.5, 200, 1.6, 0),
                Def(UpgradeIds.GravityRadius, 0.1, 20, 1.3, 20),
                Def(UpgradeIds.StaminaDrain, 0.05, 40, 1.35, 10),
                Def(UpgradeIds.StartBonus, 50, 25, 1.3, 20),
            };
            t.unlocks = new List<UnlockDef>();
            for (int tier = 2; tier <= 11; tier++) t.unlocks.Add(new UnlockDef { tier = tier, cost = 100 * tier });
            return t;
        }

        public static CelestialTable Tiers(int count = 11)
        {
            var c = ScriptableObject.CreateInstance<CelestialTable>();
            c.tiers = new List<CelestialTier>();
            double mass = 10, price = 1;
            for (int tier = 1; tier <= count; tier++)
            {
                c.tiers.Add(new CelestialTier { tier = tier, requiredMass = mass, salePrice = price, sizePx = 10 + tier });
                mass *= 2.5;
                price *= 8;
            }
            return c;
        }

        public static UpgradeDef Def(string id, double effect, double baseCost, double growth, int maxLevel) =>
            new UpgradeDef { id = id, enabled = true, revealAtTier = 1, effectPerLevel = effect, baseCost = baseCost, growth = growth, maxLevel = maxLevel };

        public static MetaState Meta(params (string id, int level)[] levels)
        {
            var m = new MetaState();
            foreach (var l in levels) m.SetLevel(l.id, l.level);
            return m;
        }
    }
}
