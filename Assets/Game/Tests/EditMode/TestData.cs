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

        /// <summary>
        /// A small tree over 4 tiers:
        /// ring 1: a_spawn (spawn +1, max 3, cost 10 ×1.5), a_pull (pull +20%, max 2), gate_t2 (cost 30);
        /// ring 2: b_spawn (spawn +2, prereq a_spawn), b_mass (dust_mass ×1.5, max 2, cost 100 ×2, prereq a_spawn | a_pull), gate_t3 (cost 300);
        /// ring 3: c_threshold (−50%, max 3, prereq b_spawn), c_drain (−50%, max 3, prereq b_mass), gate_t4 (cost 3000);
        /// ring 4: d_sale (+100%, unlimited, prereq c_threshold).
        /// One node per remaining stat in ring 1 (e_*) with max 1, cost 5, for the per-stat tests.
        /// </summary>
        public static NodeTable Nodes()
        {
            var t = ScriptableObject.CreateInstance<NodeTable>();
            t.nodes = new List<NodeDef>
            {
                Stat("a_spawn", 1, 30, StatIds.SpawnRate, 1, 3, 10, 1.5),
                Stat("a_pull", 1, 330, StatIds.PullAccel, 0.2, 2, 10, 1.5),
                Stat("e_sale", 1, 270, StatIds.SaleMult, 0.25, 4, 5, 1),
                Stat("e_stamina", 1, 210, StatIds.StaminaMax, 10, 4, 5, 1),
                Stat("e_cap", 1, 40, StatIds.DustCap, 100, 4, 5, 1),
                Stat("e_radius", 1, 320, StatIds.GravityRadius, 0.1, 5, 5, 1),
                Stat("e_start", 1, 20, StatIds.StartBonus, 50, 10, 5, 1),
                Stat("e_threshold", 1, 150, StatIds.ThresholdMult, 0.04, 5, 5, 1),
                Stat("e_drain", 1, 200, StatIds.StaminaDrain, 0.05, 4, 5, 1),
                Stat("e_mass", 1, 60, StatIds.DustMass, 1.5, 3, 5, 1),
                Gate("gate_t2", 2, 1, 30),
                Stat("b_spawn", 2, 30, StatIds.SpawnRate, 2, 2, 40, 1.5, "a_spawn"),
                Stat("b_mass", 2, 60, StatIds.DustMass, 1.5, 2, 100, 2, "a_spawn", "a_pull"),
                Gate("gate_t3", 3, 2, 300, "gate_t2"),
                Stat("c_threshold", 3, 150, StatIds.ThresholdMult, 0.5, 3, 200, 1.5, "b_spawn"),
                Stat("c_drain", 3, 210, StatIds.StaminaDrain, 0.5, 3, 200, 1.5, "b_mass"),
                Gate("gate_t4", 4, 3, 3000, "gate_t3"),
                Stat("d_sale", 4, 270, StatIds.SaleMult, 1.0, 0, 1000, 1.6, "c_threshold"),
            };
            t.ringCostMult = new double[] { 1, 1, 1, 1, 1 };
            return t;
        }

        public static CelestialTable Tiers(int count = 4)
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

        public static NodeDef Stat(string id, int ring, float angle, string stat, double effect, int maxLevel, double baseCost, double growth,
            params string[] prereqs) =>
            new NodeDef
            {
                id = id, kind = NodeKind.Stat, statId = stat, effectPerLevel = effect, maxLevel = maxLevel, baseCost = baseCost,
                growth = growth, minTier = ring, angleDeg = angle, prereqs = new List<string>(prereqs), enabled = true,
            };

        public static NodeDef Gate(string id, int tier, int ring, double cost, params string[] prereqs) =>
            new NodeDef
            {
                id = id, kind = NodeKind.TierUnlock, tier = tier, maxLevel = 1, baseCost = cost, growth = 1, minTier = ring,
                angleDeg = 90, prereqs = new List<string>(prereqs), enabled = true,
            };

        public static MetaState Meta(params (string id, int level)[] levels)
        {
            var m = new MetaState();
            foreach (var l in levels) m.SetLevel(l.id, l.level);
            return m;
        }
    }
}
