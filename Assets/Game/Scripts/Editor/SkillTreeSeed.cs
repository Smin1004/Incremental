using System;
using System.Collections.Generic;

namespace Incremental.EditorTools
{
    /// <summary>
    /// Seed data of the skill tree (12 §6). PrototypeSetup adds the entries the NodeTable asset lacks, and Validate Data
    /// warns where the asset and this code differ, so the two stay equal.
    /// Rings 1-3 are the §6 draft. Rings 4-11 follow the §6 template with formula costs: a stat node costs 0.4 × I_k
    /// (growth 1.5), a dust mass node 1.1 × I_k (growth 1.8), with I_1 = 12 and I_(k+1) = 8 × I_k, rounded to two
    /// significant digits like the draft. Gate costs keep the week-2 unlock costs (tier 2: 30, tier 3: 300).
    /// </summary>
    public static class SkillTreeSeed
    {
        // Axis angles (12 §5). Two nodes of one axis in one ring sit at ±10°.
        public const float GateAxis = 90f;
        public const float DustAxis = 30f;
        public const float MassAxis = 60f;
        public const float PlanetAxis = 150f;
        public const float StaminaAxis = 210f;
        public const float SaleAxis = 270f;
        public const float GravityAxis = 330f;
        const float Pair = 10f;

        /// <summary>Rings 1..11; index 0 is unused.</summary>
        public const int RingCount = 11;

        /// <summary>Gate cost by tier (index = tier).</summary>
        static readonly double[] GateCosts = { 0, 0, 30, 300, 2500, 2e4, 1.5e5, 1.2e6, 1e7, 1e8, 1e9, 1e10 };

        /// <summary>Draft entry income of ring k: I_1 = 12, I_(k+1) = 8 × I_k (12 §6).</summary>
        public static double EntryIncome(int ring) => 12.0 * Math.Pow(8, ring - 1);

        /// <summary>Two significant digits (2457.6 → 2500, 6758.4 → 6800).</summary>
        public static double Nice(double v)
        {
            if (v <= 0) return v;
            double scale = Math.Pow(10, Math.Floor(Math.Log10(v)) - 1);
            return Math.Round(v / scale) * scale;
        }

        static double StatCost(int ring) => Nice(0.4 * EntryIncome(ring));
        static double MassCost(int ring) => Nice(1.1 * EntryIncome(ring));

        /// <summary>
        /// Per-ring cost multiplier (index = ring), the only knob turned to pace rings (12 §7). Rings without a
        /// measurement stay 1. Values and the bot runs behind them: Docs/Reports/SkillTree_Step1.md.
        /// </summary>
        public static double[] RingCostMult()
        {
            var m = new double[RingCount + 1];
            for (int i = 0; i < m.Length; i++) m[i] = 1;
            m[1] = 1.5;
            m[2] = 0.75;
            return m;
        }

        public static List<NodeDef> Nodes()
        {
            const string spawn = StatIds.SpawnRate, pull = StatIds.PullAccel, sale = StatIds.SaleMult, stamina = StatIds.StaminaMax,
                threshold = StatIds.ThresholdMult, cap = StatIds.DustCap, mass = StatIds.DustMass, radius = StatIds.GravityRadius,
                drain = StatIds.StaminaDrain, start = StatIds.StartBonus;
            var n = new List<NodeDef>();

            // Ring 1 (I = 12): two nodes after the first run, gate around run 3-4.
            n.Add(Stat("r1_spawn", 1, DustAxis, spawn, 1, 2, 5, 1.5));
            n.Add(Stat("r1_pull", 1, GravityAxis, pull, 0.2, 2, 5, 1.5));
            n.Add(Stat("r1_sale", 1, SaleAxis, sale, 0.25, 1, 8, 1));
            n.Add(Stat("r1_stamina", 1, StaminaAxis, stamina, 10, 1, 8, 1));
            n.Add(Gate(2));

            // Ring 2 (I = 96): dust cap and gravity radius arrive ("dust surge", 10 §4.3); the planet axis starts here.
            n.Add(Stat("r2_spawn", 2, DustAxis - Pair, spawn, 2, 2, 40, 1.5, "r1_spawn"));
            n.Add(Stat("r2_cap", 2, DustAxis + Pair, cap, 100, 2, 40, 1.5, "r1_spawn"));
            n.Add(Stat("r2_radius", 2, GravityAxis, radius, 0.10, 2, 40, 1.5, "r1_pull"));
            n.Add(Stat("r2_sale", 2, SaleAxis, sale, 0.25, 2, 40, 1.5, "r1_sale"));
            n.Add(Stat("r2_stamina", 2, StaminaAxis, stamina, 10, 2, 40, 1.5, "r1_stamina"));
            n.Add(Stat("r2_threshold", 2, PlanetAxis, threshold, 0.05, 2, 40, 1.5, "gate_t2"));
            n.Add(Gate(3));

            // Ring 3 (I = 768): start bonus, stamina drain and the first dust mass node.
            n.Add(Stat("r3_start", 3, DustAxis, start, 50, 2, 300, 1.5, "r2_cap"));
            n.Add(Stat("r3_pull", 3, GravityAxis, pull, 0.30, 2, 300, 1.5, "r2_radius"));
            n.Add(Stat("r3_sale", 3, SaleAxis, sale, 0.25, 2, 300, 1.5, "r2_sale"));
            n.Add(Stat("r3_drain", 3, StaminaAxis, drain, 0.05, 2, 300, 1.5, "r2_stamina"));
            n.Add(Stat("r3_threshold", 3, PlanetAxis, threshold, 0.05, 2, 300, 1.5, "r2_threshold"));
            n.Add(Stat("r3_mass", 3, MassAxis, mass, 1.5, 1, 850, 1, "gate_t3"));
            n.Add(Gate(4));

            // Rings 4-10: one node per axis (two levels each) + a dust mass node; prereq = the same axis in the ring before.
            Ring(n, 4, (spawn, 3, DustAxis, "r3_start"), (radius, 0.10, GravityAxis, "r3_pull"), (threshold, 0.05, PlanetAxis, "r3_threshold"),
                (sale, 0.50, SaleAxis, "r3_sale"), (stamina, 20, StaminaAxis, "r3_drain"));
            Ring(n, 5, (cap, 300, DustAxis, "r4_spawn"), (pull, 0.40, GravityAxis, "r4_radius"), (threshold, 0.05, PlanetAxis, "r4_threshold"),
                (sale, 0.50, SaleAxis, "r4_sale"), (drain, 0.05, StaminaAxis, "r4_stamina"));
            Ring(n, 6, (spawn, 5, DustAxis, "r5_cap"), (radius, 0.15, GravityAxis, "r5_pull"),
                (sale, 1.00, SaleAxis, "r5_sale"), (stamina, 30, StaminaAxis, "r5_drain"));
            Ring(n, 7, (cap, 500, DustAxis, "r6_spawn"), (pull, 0.50, GravityAxis, "r6_radius"),
                (sale, 1.00, SaleAxis, "r6_sale"), (drain, 0.05, StaminaAxis, "r6_stamina"));
            Ring(n, 8, (spawn, 8, DustAxis - Pair, "r7_cap"), (start, 200, DustAxis + Pair, "r7_cap"), (radius, 0.15, GravityAxis, "r7_pull"),
                (sale, 2.00, SaleAxis, "r7_sale"), (stamina, 40, StaminaAxis, "r7_drain"));
            Ring(n, 9, (cap, 800, DustAxis, "r8_spawn|r8_start"), (pull, 0.60, GravityAxis, "r8_radius"),
                (sale, 2.00, SaleAxis, "r8_sale"), (drain, 0.05, StaminaAxis, "r8_stamina"));
            Ring(n, 10, (spawn, 12, DustAxis, "r9_cap"), (radius, 0.20, GravityAxis, "r9_pull"),
                (sale, 4.00, SaleAxis, "r9_sale"), (stamina, 60, StaminaAxis, "r9_drain"));

            // Ring 11: endless nodes with unlimited levels.
            n.Add(Stat("r11_sale", 11, SaleAxis, sale, 1.00, 0, StatCost(11), 1.6, "r10_sale"));
            n.Add(Stat("r11_mass", 11, MassAxis, mass, 1.5, 0, MassCost(11), 2.0, "r10_mass"));
            n.Add(Stat("r11_stamina", 11, StaminaAxis, stamina, 30, 0, StatCost(11), 1.6, "r10_stamina"));
            return n;
        }

        /// <summary>Ring k of the template: the given axis nodes (2 levels, 0.4 × I_k, growth 1.5), dust mass, next gate.</summary>
        static void Ring(List<NodeDef> n, int ring, params (string stat, double effect, float angle, string prereqs)[] axes)
        {
            foreach (var a in axes)
                n.Add(Stat("r" + ring + "_" + ShortName(a.stat), ring, a.angle, a.stat, a.effect, 2, StatCost(ring), 1.5, a.prereqs.Split('|')));
            n.Add(Stat("r" + ring + "_mass", ring, MassAxis, StatIds.DustMass, 1.5, 2, MassCost(ring), 1.8, "r" + (ring - 1) + "_mass"));
            if (ring + 1 <= RingCount) n.Add(Gate(ring + 1));
        }

        static string ShortName(string stat)
        {
            switch (stat)
            {
                case StatIds.SpawnRate: return "spawn";
                case StatIds.PullAccel: return "pull";
                case StatIds.SaleMult: return "sale";
                case StatIds.StaminaMax: return "stamina";
                case StatIds.ThresholdMult: return "threshold";
                case StatIds.DustCap: return "cap";
                case StatIds.DustMass: return "mass";
                case StatIds.GravityRadius: return "radius";
                case StatIds.StaminaDrain: return "drain";
                case StatIds.StartBonus: return "start";
                default: return stat;
            }
        }

        static NodeDef Stat(string id, int ring, float angle, string stat, double effect, int maxLevel, double baseCost, double growth,
            params string[] prereqs) =>
            new NodeDef
            {
                id = id, kind = NodeKind.Stat, statId = stat, effectPerLevel = effect, maxLevel = maxLevel,
                baseCost = baseCost, growth = growth, minTier = ring, prereqs = new List<string>(prereqs), angleDeg = angle, enabled = true,
            };

        /// <summary>Gate to tier t: in ring t - 1, after the previous gate.</summary>
        static NodeDef Gate(int tier) =>
            new NodeDef
            {
                id = "gate_t" + tier, kind = NodeKind.TierUnlock, tier = tier, maxLevel = 1, baseCost = GateCosts[tier], growth = 1,
                minTier = tier - 1, prereqs = tier > 2 ? new List<string> { "gate_t" + (tier - 1) } : new List<string>(),
                angleDeg = GateAxis, enabled = true,
            };
    }
}
