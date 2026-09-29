using System;
using System.Collections.Generic;

namespace Incremental.EditorTools
{
    /// <summary>
    /// Seed data of the skill tree (12 §6). PrototypeSetup adds the entries the NodeTable asset lacks, and Validate Data
    /// warns where the asset and this code differ, so the two stay equal.
    /// Rings 1-3 are the §6 draft. Rings 4-11 follow the §6 template with formula costs: a stat node costs 0.4 × I_k
    /// (growth 1.5), a dust kind unlock 1.1 × I_k, with I_1 = 12 and I_(k+1) = 8 × I_k, rounded to two
    /// significant digits like the draft. Gate costs keep the week-2 unlock costs (tier 2: 30, tier 3: 300).
    /// 20 §4: the 60° axis is the dust kind axis. The compound dust_mass nodes are gone; rings 3, 5, 7 and 9 unlock a
    /// dust kind and rings 4, 6, 8 and 10 raise its share.
    /// 20 §5: the fragment nodes run along 140°, beside the threshold nodes of the planet axis, in rings 3 to 8.
    /// </summary>
    public static class SkillTreeSeed
    {
        // Axis angles (12 §5). Two nodes of one axis in one ring sit at ±10°.
        const float Pair = 10f;
        public const float GateAxis = 90f;
        public const float DustAxis = 30f;
        /// <summary>60°: the dust kind axis since 20 §4 (it used to carry the compound dust mass nodes).</summary>
        public const float MassAxis = 60f;
        public const float PlanetAxis = 150f;
        /// <summary>The fragment nodes share the planet axis, offset so they never sit on a threshold node (20 §5).</summary>
        public const float FragmentAxis = PlanetAxis - Pair;
        public const float StaminaAxis = 210f;
        public const float SaleAxis = 270f;
        public const float GravityAxis = 330f;

        /// <summary>Rings 1..11; index 0 is unused.</summary>
        public const int RingCount = 11;

        /// <summary>
        /// Income of a run at the start of ring k, measured with the bot (20 §8.3 step 1; index = ring, 0 = not measured).
        /// Every node cost is derived from it, so the balance pass changes these numbers and nothing else.
        /// The measurement and the runs behind it: Docs/Reports/Week6.md.
        /// </summary>
        static readonly double[] MeasuredIncome =
        {
            0,          // index 0 is unused
            9,          // ring 1
            21,         // ring 2
            130,        // ring 3
            713,        // ring 4
            3893,       // ring 5
            19872,      // ring 6
            171177,     // ring 7
            717207,     // ring 8
            7506047,    // ring 9
            39107361,   // ring 10
            416034083,  // ring 11
        };

        /// <summary>
        /// The balance pass (20 §8.3) measures I_k ring by ring and feeds the numbers back in here before rebuilding the
        /// node table, instead of editing this file once per ring. The values that survive are written into
        /// <see cref="MeasuredIncome"/> and <see cref="RingCostMult"/> at the end of the pass, so the seed stays the truth.
        /// </summary>
        public static void SetMeasuredIncome(int ring, double income)
        {
            if (ring >= 0 && ring < MeasuredIncome.Length) MeasuredIncome[ring] = income;
        }

        public static double[] MeasuredIncomes() => (double[])MeasuredIncome.Clone();

        static double[] ringMultOverride;

        public static void SetRingCostMult(int ring, double mult)
        {
            if (ringMultOverride == null) ringMultOverride = RingCostMult();
            if (ring >= 0 && ring < ringMultOverride.Length) ringMultOverride[ring] = mult;
        }

        /// <summary>
        /// Entry income of ring k. The measurement when there is one, else the 12 §6 draft
        /// (I_1 = 12, I_(k+1) = 8 × I_k), which is what the first pass of the balance run uses.
        /// </summary>
        public static double EntryIncome(int ring)
        {
            if (ring >= 0 && ring < MeasuredIncome.Length && MeasuredIncome[ring] > 0) return MeasuredIncome[ring];
            return 12.0 * Math.Pow(8, ring - 1);
        }

        /// <summary>Two significant digits (2457.6 → 2500, 6758.4 → 6800).</summary>
        public static double Nice(double v)
        {
            if (v <= 0) return v;
            double scale = Math.Pow(10, Math.Floor(Math.Log10(v)) - 1);
            return Math.Round(v / scale) * scale;
        }

        /// <summary>
        /// 20 §8.2: every node of ring k costs 0.4 × I_k at level 1 and grows ×1.5, and a gate costs 3 × I_k. Rounded to
        /// two significant digits so the shop reads well. This is the only place node costs come from; the balance pass
        /// turns <see cref="RingCostMult"/>, never a single node.
        /// </summary>
        public const double StatCostShare = 0.4;
        public const double StatCostGrowth = 1.5;
        public const double GateCostShare = 3.0;
        /// <summary>Ring 11 endless nodes climb faster than the capped ones, so they stay a money sink (12 §6).</summary>
        public const double EndlessGrowth = 1.6;

        static double StatCost(int ring) => Nice(StatCostShare * EntryIncome(ring));
        static double GateCost(int ring) => Nice(GateCostShare * EntryIncome(ring));
        static double KindCost(int ring) => StatCost(ring);

        /// <summary>
        /// The fragment axis (20 §5), by ring: 3 unlocks fragments at 30% with the base offset, 4, 6 and 8 add 15%p each,
        /// and 5 and 7 move the tier cap one tier up. They sit beside the threshold nodes on the planet axis.
        /// </summary>
        static NodeDef FragmentNode(int ring)
        {
            switch (ring)
            {
                case 3: return Frag("frag_unlock", 3, StatIds.FragUnlock, 0.30, "gate_t3");
                case 4: return Frag("r4_frag_chance", 4, StatIds.FragChance, 0.15, "frag_unlock");
                case 5: return Frag("r5_frag_range", 5, StatIds.FragOffset, 1, "r4_frag_chance");
                case 6: return Frag("r6_frag_chance", 6, StatIds.FragChance, 0.15, "r5_frag_range");
                case 7: return Frag("r7_frag_range", 7, StatIds.FragOffset, 1, "r6_frag_chance");
                case 8: return Frag("r8_frag_chance", 8, StatIds.FragChance, 0.15, "r7_frag_range");
                default: return null;
            }
        }

        static NodeDef Frag(string id, int ring, string stat, double effect, params string[] prereqs) =>
            Stat(id, ring, FragmentAxis, stat, effect, 1, StatCost(ring), 1, prereqs);

        /// <summary>An axis node of ring k: the 20 §8.2 cost formula, so only the effect and the level count are written down.</summary>
        static NodeDef Ax(string id, int ring, float angle, string stat, double effect, int maxLevel, params string[] prereqs) =>
            Stat(id, ring, angle, stat, effect, maxLevel, StatCost(ring), StatCostGrowth, prereqs);

        /// <summary>
        /// The dust kind axis (20 §4), by ring: 3, 5, 7 and 9 unlock a kind, the ring after it raises that kind's share
        /// by four levels. Ring 11 has nothing on this axis. Ids are stable keys, so the unlocks read like the gates.
        /// </summary>
        static NodeDef DustKindNode(int ring)
        {
            switch (ring)
            {
                case 3: return DustUnlock("medium", 3, "gate_t3");
                case 4: return DustChance("medium", 4, 0.015, "dust_medium");
                case 5: return DustUnlock("large", 5, "r4_chance_medium");
                case 6: return DustChance("large", 6, 0.015, "dust_large");
                case 7: return DustUnlock("crystal", 7, "r6_chance_large");
                case 8: return DustChance("crystal", 8, 0.01, "dust_crystal");
                case 9: return DustUnlock("interstellar", 9, "r8_chance_crystal");
                case 10: return DustChance("interstellar", 10, 0.0075, "dust_interstellar");
                default: return null;
            }
        }

        static NodeDef DustUnlock(string kind, int ring, params string[] prereqs) =>
            new NodeDef
            {
                id = "dust_" + kind, kind = NodeKind.DustKindUnlock, dustKindId = kind, maxLevel = 1,
                baseCost = KindCost(ring), growth = 1, minTier = ring, prereqs = new List<string>(prereqs),
                angleDeg = MassAxis, enabled = true,
            };

        static NodeDef DustChance(string kind, int ring, double perLevel, params string[] prereqs)
        {
            var n = Stat("r" + ring + "_chance_" + kind, ring, MassAxis, StatIds.DustChance, perLevel, 4, StatCost(ring), 1.5, prereqs);
            n.dustKindId = kind;
            return n;
        }

        /// <summary>
        /// Per-ring cost multiplier (index = ring), the only knob turned to pace rings (12 §7, 20 §8.3 step 4). The
        /// costs themselves come from the measured I_k; a ring that still takes too many or too few runs is nudged here
        /// and nowhere else. Every value was re-measured in the 20 §8 balance pass, so the week 2-5 numbers are gone:
        /// the bot runs behind these are in Docs/Reports/Week6.md.
        /// </summary>
        public static double[] RingCostMult()
        {
            if (ringMultOverride != null) return (double[])ringMultOverride.Clone();
            var m = new double[RingCount + 1];
            for (int i = 0; i < m.Length; i++) m[i] = 1;
            m[2] = 1.4;    // cleared in 3 runs at 1.0 (target 4-6)
            m[5] = 0.75;   // took 6 runs at 1.0 (target 3-5)
            m[7] = 0.55;   // took 6 runs at 1.0 and still 6 at 0.75
            m[9] = 0.8;    // took 6 runs at 1.0
            m[10] = 0.8;   // took 6 runs at 1.0
            return m;
        }

        public static List<NodeDef> Nodes()
        {
            const string spawn = StatIds.SpawnRate, pull = StatIds.PullAccel, sale = StatIds.SaleMult, stamina = StatIds.StaminaMax,
                threshold = StatIds.ThresholdMult, cap = StatIds.DustCap, radius = StatIds.GravityRadius,
                drain = StatIds.StaminaDrain, start = StatIds.StartBonus;
            var n = new List<NodeDef>();

            // Ring 1: two nodes after the first run, gate around run 3-4.
            n.Add(Ax("r1_spawn", 1, DustAxis, spawn, 1, 2));
            n.Add(Ax("r1_pull", 1, GravityAxis, pull, 0.2, 2));
            n.Add(Ax("r1_sale", 1, SaleAxis, sale, 0.25, 1));
            n.Add(Ax("r1_stamina", 1, StaminaAxis, stamina, 10, 1));
            n.Add(Gate(2));

            // Ring 2 (I = 96): dust cap and gravity radius arrive ("dust surge", 10 §4.3); the planet axis starts here.
            n.Add(Ax("r2_spawn", 2, DustAxis - Pair, spawn, 2, 2, "r1_spawn"));
            n.Add(Ax("r2_cap", 2, DustAxis + Pair, cap, 100, 2, "r1_spawn"));
            n.Add(Ax("r2_radius", 2, GravityAxis, radius, 0.10, 2, "r1_pull"));
            n.Add(Ax("r2_sale", 2, SaleAxis, sale, 0.25, 2, "r1_sale"));
            n.Add(Ax("r2_stamina", 2, StaminaAxis, stamina, 10, 2, "r1_stamina"));
            n.Add(Ax("r2_threshold", 2, PlanetAxis, threshold, 0.05, 2, "gate_t2"));
            n.Add(Gate(3));

            // Ring 3 (I = 768): start bonus, stamina drain and the first dust kind unlock (20 §4).
            n.Add(Ax("r3_start", 3, DustAxis, start, 50, 2, "r2_cap"));
            n.Add(Ax("r3_pull", 3, GravityAxis, pull, 0.30, 2, "r2_radius"));
            n.Add(Ax("r3_sale", 3, SaleAxis, sale, 0.25, 2, "r2_sale"));
            n.Add(Ax("r3_drain", 3, StaminaAxis, drain, 0.05, 2, "r2_stamina"));
            n.Add(Ax("r3_threshold", 3, PlanetAxis, threshold, 0.05, 2, "r2_threshold"));
            n.Add(DustKindNode(3));
            n.Add(FragmentNode(3));
            n.Add(Gate(4));

            // Rings 4-10: one node per axis (two levels each) + the dust kind axis node; prereq = the same axis in the ring before.
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

            // Ring 11: endless nodes with unlimited levels. The dust kind axis ends at ring 10, so the third endless node
            // sits on the dust axis instead of the old compound mass node (20 §4).
            n.Add(Stat("r11_sale", 11, SaleAxis, sale, 1.00, 0, StatCost(11), EndlessGrowth, "r10_sale"));
            n.Add(Stat("r11_spawn", 11, DustAxis, spawn, 12, 0, StatCost(11), EndlessGrowth, "r10_spawn"));
            n.Add(Stat("r11_stamina", 11, StaminaAxis, stamina, 30, 0, StatCost(11), EndlessGrowth, "r10_stamina"));
            return n;
        }

        /// <summary>Ring k of the template: the given axis nodes (2 levels, 0.4 × I_k, growth 1.5), the dust kind axis, next gate.</summary>
        static void Ring(List<NodeDef> n, int ring, params (string stat, double effect, float angle, string prereqs)[] axes)
        {
            foreach (var a in axes)
                n.Add(Ax("r" + ring + "_" + ShortName(a.stat), ring, a.angle, a.stat, a.effect, 2, a.prereqs.Split('|')));
            var dust = DustKindNode(ring);
            if (dust != null) n.Add(dust);
            var frag = FragmentNode(ring);
            if (frag != null) n.Add(frag);
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
                case StatIds.FragUnlock: return "frag";
                case StatIds.FragChance: return "fragchance";
                case StatIds.FragOffset: return "fragrange";
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

        /// <summary>Gate to tier t: in ring t - 1, after the previous gate. 3 × the entry income of its ring (20 §8.2).</summary>
        static NodeDef Gate(int tier) =>
            new NodeDef
            {
                id = "gate_t" + tier, kind = NodeKind.TierUnlock, tier = tier, maxLevel = 1, baseCost = GateCost(tier - 1), growth = 1,
                minTier = tier - 1, prereqs = tier > 2 ? new List<string> { "gate_t" + (tier - 1) } : new List<string>(),
                angleDeg = GateAxis, enabled = true,
            };
    }
}
