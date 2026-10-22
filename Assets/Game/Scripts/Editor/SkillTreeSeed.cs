using System;
using System.Collections.Generic;

namespace Incremental.EditorTools
{
    /// <summary>
    /// Seed data of the skill tree, v2 (29). PrototypeSetup builds the NodeTable asset from it, and Validate Data warns
    /// where the asset and this code differ, so the two stay equal. The v1 seed (12 §6, seven axes, 92 nodes) and its
    /// asset are kept in Data/Backup~ (Unity does not import that folder).
    ///
    /// 29 §2: five categories, each owning 72° of the tree. The stem of a category runs out through the middle of its
    /// sector, one node per ring; side lines branch off it within ±30°, further out the higher the ring. Rule nodes
    /// (29 §5) sit on their own line beside the stem.
    /// 29 §3: the stat lines of v1 are split into nodes of more levels (×5 for most), each line summing to what the v1
    /// line summed to. 13th pass step 1: the split is rough and the rule nodes have no effect yet; step 2 (B1) settles
    /// the numbers (run time +9 s per ring, radius × 0.7 with gravityGrowthScale 1, and so on).
    /// 29 §3.5: the ability unlocks stand on the ability stem in rings 2, 4, 6, 8 and 10; each ability's branch splits
    /// off to the left and to the right in turn.
    /// </summary>
    public static class SkillTreeSeed
    {
        // 29 §2: stem angles (Unity angles, 90° = up), 72° apart.
        public const float CelestialStem = 90f;
        public const float DustStem = 18f;
        public const float GravityStem = 306f;
        public const float AbilityStem = 234f;
        public const float TimeStem = 162f;

        /// <summary>Side lines sit this far and twice this far beside their stem (29 §2: within ±30°).</summary>
        const float Side = 12f;

        /// <summary>Rings 1..11; index 0 is unused.</summary>
        public const int RingCount = 11;

        /// <summary>28 §2: seconds of run time per ring (rings 1-10, +90 s in all). The ring 11 endless node is gone (29 §3.4).</summary>
        public const double RunTimePerRing = 9;

        /// <summary>29 §10.7 S4: spawn_ramp per level (×5 per node, five nodes: +1.0 in all, x2 spawn once the ramp is full).</summary>
        public const double SpawnRampPerLevel = 0.04;

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
        /// node table. The values that survive are written into <see cref="MeasuredIncome"/> and
        /// <see cref="RingCostMult"/> at the end of the pass, so the seed stays the truth.
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

        /// <summary>Entry income of ring k: the measurement when there is one, else the 12 §6 draft (I_1 = 12, ×8 per ring).</summary>
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
        /// 20 §8.2: a node of ring k costs 0.4 × I_k at level 1, a gate 3 × I_k. 29 §3: levels grow ×1.35 (v1: ×1.5)
        /// since a line has more of them now. Rounded to two significant digits.
        /// </summary>
        public const double StatCostShare = 0.4;
        public const double StatCostGrowth = 1.35;
        public const double GateCostShare = 3.0;
        /// <summary>Ring 11 endless nodes climb faster than the capped ones, so they stay a money sink (12 §6).</summary>
        public const double EndlessGrowth = 1.6;
        /// <summary>13th pass B1: a rule node costs this many times a stat node of its ring until the balance pass prices it.</summary>
        public const double RuleCostMult = 1.5;
        /// <summary>24 §21.2: an evolve node costs this many times an unlock of its ring.</summary>
        const double EvolveCostMult = 2;

        static double StatCost(int ring) => Nice(StatCostShare * EntryIncome(ring));
        static double GateCost(int ring) => Nice(GateCostShare * EntryIncome(ring));

        /// <summary>
        /// Per-ring cost multiplier (index = ring), the only knob turned to pace rings (12 §7). Carried over from v1
        /// (Reports/Week7.md); the balance pass (28) measures it again for v2.
        /// </summary>
        public static double[] RingCostMult()
        {
            if (ringMultOverride != null) return (double[])ringMultOverride.Clone();
            var m = new double[RingCount + 1];
            for (int i = 0; i < m.Length; i++) m[i] = 1;
            m[1] = 2.5;
            m[2] = 3.1;
            m[3] = 2.1;
            m[4] = 1.2;
            m[5] = 1.9;
            m[6] = 3.9;
            m[7] = 3.7;
            m[8] = 11.5;
            m[9] = 3.8;
            m[10] = 8.4;
            return m;
        }

        public static List<NodeDef> Nodes()
        {
            var n = new List<NodeDef>();
            Celestial(n);
            Dust(n);
            Gravity(n);
            Time(n);
            Ability(n);
            return n;
        }

        // ---------------- 29 §3.1 천체 (90°) ----------------

        /// <summary>
        /// Stem: the gates (gate_t2 in ring 1 … gate_t11 in ring 10). Left: sale (rings 1-10 ×5, ring 11 endless) and
        /// the rule line. Right: required mass (rings 2-10 ×5) and fragments (rings 3-9).
        /// </summary>
        static void Celestial(List<NodeDef> n)
        {
            const NodeCategory c = NodeCategory.Celestial;
            for (int tier = 2; tier <= RingCount; tier++) n.Add(Gate(tier));

            // v1 sale per ring (sums to 23.25), now spread over 5 levels.
            double[] sale = { 0, 0.25, 0.5, 0.5, 1, 1, 2, 2, 4, 4, 8 };
            Line(n, c, "sale", StatIds.SaleMult, +Side, 1, 10, 5, r => sale[r], r => r == 1 ? null : "sale_r" + (r - 1));
            n.Add(Endless("sale_r11", c, +Side, StatIds.SaleMult, 1.0, "sale_r10"));

            // v1 required mass ended in ring 7 at 0.92 (capped at maxReduction 0.9); v2 runs it to ring 10 with the same sum.
            double[] thr = { 0, 0, 0.14, 0.14, 0.14, 0.12, 0.10, 0.10, 0.08, 0.06, 0.04 };
            Line(n, c, "thr", StatIds.ThresholdMult, -Side, 2, 10, 5, r => thr[r], r => r == 2 ? "gate_t2" : "thr_r" + (r - 1));

            // 20 §5 fragments: unlock 3, chance 4·6·8 (×3), range 5·7·9 (×1, ring 9 new).
            n.Add(Stat("frag_unlock", c, 3, -2 * Side, StatIds.FragUnlock, 0.30, 1, "gate_t3"));
            n.Add(Stat("frag_chance_r4", c, 4, -2 * Side, StatIds.FragChance, 0.05, 3, "frag_unlock"));
            n.Add(Stat("frag_range_r5", c, 5, -2 * Side, StatIds.FragOffset, 1, 1, "frag_chance_r4"));
            n.Add(Stat("frag_chance_r6", c, 6, -2 * Side, StatIds.FragChance, 0.05, 3, "frag_range_r5"));
            n.Add(Stat("frag_range_r7", c, 7, -2 * Side, StatIds.FragOffset, 1, 1, "frag_chance_r6"));
            n.Add(Stat("frag_chance_r8", c, 8, -2 * Side, StatIds.FragChance, 0.05, 3, "frag_range_r7"));
            n.Add(Stat("frag_range_r9", c, 9, -2 * Side, StatIds.FragOffset, 1, 1, "frag_chance_r8"));

            // 29 §5.4 rules: C3 ring 2, C1 ring 3, C2 ring 5 (×3), C4 ring 7 (×2).
            n.Add(Rule("C3", c, 2, +2 * Side, 1, "gate_t2"));
            n.Add(Rule("C1", c, 3, +2 * Side, 1, "rule_C3"));
            n.Add(Rule("C2", c, 5, +2 * Side, 3, "rule_C1"));
            n.Add(Rule("C4", c, 7, +2 * Side, 2, "rule_C2"));
        }

        // ---------------- 29 §3.2 먼지 (18°) ----------------

        /// <summary>
        /// Stem: spawn rate (rings 1-10 ×5, ring 11 endless). Left: dust cap (even rings), start bonus (3·6·9) and the
        /// rule line (4·5·7). Right: dust kinds (unlock 3·5·7·9, chance 4·6·8·10) and dust mass (5·7·9·11).
        /// </summary>
        static void Dust(List<NodeDef> n)
        {
            const NodeCategory c = NodeCategory.Dust;
            // v1 spawn per ring 2/4/-/6/-/10/-/16/-/24 (62), spread so every ring has its share.
            double[] spawn = { 0, 2, 4, 3, 3, 5, 5, 8, 8, 12, 12 };
            Line(n, c, "spawn", StatIds.SpawnRate, 0f, 1, 10, 5, r => spawn[r], r => r == 1 ? null : "spawn_r" + (r - 1));
            n.Add(Endless("spawn_r11", c, 0f, StatIds.SpawnRate, 12, "spawn_r10"));

            // v1 cap 200/600/1000/1600 in rings 2·5·7·9 (3400); same sum over 2·4·6·8·10.
            double[] cap = { 0, 0, 200, 0, 300, 0, 600, 0, 1000, 0, 1300 };
            Line(n, c, "cap", StatIds.DustCap, +Side, 2, 10, 5, r => cap[r], r => r == 2 ? "spawn_r1" : "cap_r" + (r - 2), 2);

            // v1 start 100 (ring 3) + 400 (ring 8) = 500.
            n.Add(Stat("start_r3", c, 3, +2 * Side, StatIds.StartBonus, 100 / 3.0, 3, "spawn_r2"));
            n.Add(Stat("start_r6", c, 6, +2 * Side, StatIds.StartBonus, 50, 3, "start_r3"));
            n.Add(Stat("start_r9", c, 9, +2 * Side, StatIds.StartBonus, 250 / 3.0, 3, "start_r6"));

            // 20 §4 dust kinds, one line: unlock in odd rings, its chance in the ring after: the v1 sum (4 levels) over
            // 5 levels. Per level it has to equal GameParams.dustKinds[].chancePerLevel (Validate Data checks).
            n.Add(Kind("medium", 3, "spawn_r2"));
            n.Add(Chance("medium", 4, 0.024, "dust_medium"));
            n.Add(Kind("large", 5, "chance_medium_r4"));
            n.Add(Chance("large", 6, 0.012, "dust_large"));
            n.Add(Kind("crystal", 7, "chance_large_r6"));
            n.Add(Chance("crystal", 8, 0.008, "dust_crystal"));
            n.Add(Kind("interstellar", 9, "chance_crystal_r8"));
            n.Add(Chance("interstellar", 10, 0.008, "dust_interstellar"));

            // 29 §3.2 dust mass (new): ×1.1 per level, compound, ×3.
            n.Add(Stat("mass_r5", c, 5, -2 * Side, StatIds.DustMass, 1.1, 3, "spawn_r4"));
            n.Add(Stat("mass_r7", c, 7, -2 * Side, StatIds.DustMass, 1.1, 3, "mass_r5"));
            n.Add(Stat("mass_r9", c, 9, -2 * Side, StatIds.DustMass, 1.1, 3, "mass_r7"));
            n.Add(Stat("mass_r11", c, 11, -2 * Side, StatIds.DustMass, 1.1, 3, "mass_r9"));

            // 29 §10.4 rules: D1 ring 4 (×3), D3 ring 5 (×3), D2 ring 6 (×3), D4 ring 7 (×3), D5 ring 8 (×3). D2 sits a
            // little further out than the rule line because start_r6 holds that place in ring 6.
            n.Add(Rule("D1", c, 4, +2 * Side, 3, "spawn_r3"));
            n.Add(Rule("D3", c, 5, +2 * Side, 3, "rule_D1"));
            n.Add(Rule("D2", c, 6, +2.5f * Side, 3, "rule_D3"));
            n.Add(Rule("D4", c, 7, +2 * Side, 3, "rule_D2"));
            n.Add(Rule("D5", c, 8, +2 * Side, 3, "rule_D4"));
        }

        static NodeDef Kind(string kind, int ring, string prereq) =>
            new NodeDef
            {
                id = "dust_" + kind, kind = NodeKind.DustKindUnlock, dustKindId = kind, maxLevel = 1, baseCost = StatCost(ring), growth = 1,
                minTier = ring, prereqs = new List<string> { prereq }, angleDeg = Angle(NodeCategory.Dust, -Side),
                category = NodeCategory.Dust, enabled = true,
            };

        static NodeDef Chance(string kind, int ring, double perLevel, string prereq)
        {
            var d = Stat("chance_" + kind + "_r" + ring, NodeCategory.Dust, ring, -Side, StatIds.DustChance, perLevel, 5, prereq);
            d.dustKindId = kind;
            return d;
        }

        // ---------------- 29 §3.3 중력 (306°) ----------------

        /// <summary>
        /// Stem: pull (rings 1-10 ×5). Left: radius (2-10 ×5) and the rule line. Right: follow speed (even rings ×3)
        /// and the dust top speed (3·6·9 ×3, new).
        /// </summary>
        static void Gravity(List<NodeDef> n)
        {
            const NodeCategory c = NodeCategory.Gravity;
            // v1 pull 0.4/0.6/0.8/1.0/1.2 in the odd rings (4.0), split between each odd ring and the one after.
            double[] pull = { 0, 0.2, 0.2, 0.3, 0.3, 0.4, 0.4, 0.5, 0.5, 0.6, 0.6 };
            Line(n, c, "pull", StatIds.PullAccel, 0f, 1, 10, 5, r => pull[r], r => r == 1 ? null : "pull_r" + (r - 1));

            // v1 radius 0.2/0.2/0.28/0.28/0.34 in the even rings (1.3) x 0.7 (24 §23.2, gravityGrowthScale is 1 now) = 0.91.
            double[] radius = { 0, 0, 0.098, 0.07, 0.07, 0.091, 0.098, 0.098, 0.112, 0.126, 0.147 };
            Line(n, c, "radius", StatIds.GravityRadius, +Side, 2, 10, 5, r => radius[r], r => r == 2 ? "pull_r1" : "radius_r" + (r - 1));

            // 22 §3 follow: +15%p per v1 node, now ×3.
            for (int r = 2; r <= 10; r += 2)
                n.Add(Stat("follow_r" + r, c, r, -Side, StatIds.FollowSpeed, 0.05, 3, r == 2 ? "pull_r1" : "follow_r" + (r - 2)));

            // 29 §3.3 dust top speed (new): +5% per level, ×3.
            for (int r = 3; r <= 9; r += 3)
                n.Add(Stat("speed_r" + r, c, r, -2 * Side, StatIds.DustMaxSpeed, 0.05, 3, r == 3 ? "pull_r2" : "speed_r" + (r - 3)));

            // 29 §10.1: no rule nodes in this category. The 13th pass lines stay here, effect code and values included
            // (Rules.cs, GameParams.rules); uncomment one to bring it back.
            // n.Add(Rule("G1", c, 3, +2 * Side, 1, "pull_r2"));
            // n.Add(Rule("G2", c, 5, +2 * Side, 1, "rule_G1"));
            // n.Add(Rule("G3", c, 7, +2 * Side, 1, "rule_G2"));
        }

        // ---------------- 29 §3.4 시간 (162°) ----------------

        /// <summary>
        /// Stem: run time (rings 1-10 ×5, +9 s per ring). 29 §10.7 H1: from ring 2 the ring's 9 s are split between the stem
        /// node and a side node (+Side, ×5 each), so the category has more nodes and the run is no longer. Right of the
        /// stem: S4 무르익는 하늘 (spawn_ramp, rings 2·4·6·8·10 ×5) and, further out, the rule line.
        /// </summary>
        static void Time(List<NodeDef> n)
        {
            const NodeCategory c = NodeCategory.Time;
            Line(n, c, "time", StatIds.RunTime, 0f, 1, 10, 5, r => r == 1 ? RunTimePerRing : RunTimePerRing * 0.5, r => r == 1 ? null : "time_r" + (r - 1));
            Line(n, c, "time_side", StatIds.RunTime, +Side, 2, 10, 5, r => RunTimePerRing * 0.5, r => r == 2 ? "time_r1" : "time_side_r" + (r - 1));
            Line(n, c, "ramp", StatIds.SpawnRamp, -Side, 2, 10, 5, r => SpawnRampPerLevel * 5, r => r == 2 ? "time_r1" : "ramp_r" + (r - 2), 2);

            // 29 §10.4 rules: T3 ring 6 only ("시간은 빡빡하게"). T1, T2 and T5 stay here, effect code and values included;
            // uncomment one to bring it back.
            // n.Add(Rule("T1", c, 2, +Side, 3, "time_r1"));
            // n.Add(Rule("T2", c, 4, -Side, 1, "time_r3"));
            // n.Add(Rule("T5", c, 5, +Side, 1, "time_r4"));
            n.Add(Rule("T3", c, 6, -2 * Side, 1, "time_r5"));
        }

        // ---------------- 29 §3.5 능력 (234°) ----------------

        /// <summary>
        /// Stem: the five unlocks in rings 2, 4, 6, 8, 10, each needing the one before. Branches alternate left (+) and
        /// right (−) and lean further out the further they run: 결정 left, 수축 right, 궤도석 left, 유성 right,
        /// 블랙홀 left. The two rule nodes sit at the outer edge of the right side.
        /// </summary>
        static void Ability(List<NodeDef> n)
        {
            n.Add(AbilityUnlock("crystal", 2, null));
            n.Add(AbilityUnlock("implode", 4, "ability_crystal"));
            n.Add(AbilityUnlock("satellite", 6, "ability_implode"));
            n.Add(AbilityUnlock("meteor", 8, "ability_satellite"));
            n.Add(AbilityUnlock("blackhole", 10, "ability_meteor"));

            // 증폭 결정 (left): power 3 ×5, buff duration 4 ×3, spawn interval 5 ×3, evolve 7.
            n.Add(AbilityStat("crystal_power_r3", "crystal", 3, +8, StatIds.AbilityPower, 0.06, 5, "ability_crystal"));
            n.Add(AbilityStat("crystal_duration_r4", "crystal", 4, +14, StatIds.AbilityDuration, 0.5, 3, "crystal_power_r3"));
            n.Add(AbilityStat("crystal_interval_r5", "crystal", 5, +20, StatIds.AbilityCooldown, 0.05, 3, "crystal_duration_r4"));
            n.Add(AbilityEvolve("crystal", 7, +20, "crystal_interval_r5"));

            // 수축 (right): range 5 ×3 (last level the whole screen), power 6 ×3, cooldown 7 ×3.
            n.Add(AbilityStat("implode_range_r5", "implode", 5, -8, StatIds.AbilityRange, 1, 3, "ability_implode"));
            n.Add(AbilityStat("implode_power_r6", "implode", 6, -14, StatIds.AbilityPower, 0.1, 3, "implode_range_r5"));
            n.Add(AbilityStat("implode_cooldown_r7", "implode", 7, -20, StatIds.AbilityCooldown, 0.05, 3, "implode_power_r6"));

            // 궤도석 (left): count 7 ×3, tow 8 ×3, orbit size 9 ×3.
            n.Add(AbilityStat("satellite_count_r7", "satellite", 7, +8, StatIds.AbilityCount, 1, 3, "ability_satellite"));
            n.Add(AbilityStat("satellite_tow_r8", "satellite", 8, +14, StatIds.AbilityPower, 0.1, 3, "satellite_count_r7"));
            n.Add(AbilityStat("satellite_orbit_r9", "satellite", 9, +20, StatIds.AbilityRange, 1, 3, "satellite_tow_r8"));

            // 유성 (right): amount 9 ×3, interval 9 ×3, evolve 10.
            n.Add(AbilityStat("meteor_amount_r9", "meteor", 9, -8, StatIds.AbilityAmount, 0.03, 3, "ability_meteor"));
            n.Add(AbilityStat("meteor_interval_r9", "meteor", 9, -18, StatIds.AbilityCooldown, 0.05, 3, "meteor_amount_r9"));
            n.Add(AbilityEvolve("meteor", 10, -14, "meteor_interval_r9"));

            // 블랙홀 (left): duration 11 ×3, cooldown 11 ×3.
            n.Add(AbilityStat("blackhole_duration_r11", "blackhole", 11, +8, StatIds.AbilityDuration, 1, 3, "ability_blackhole"));
            n.Add(AbilityStat("blackhole_cooldown_r11", "blackhole", 11, +16, StatIds.AbilityCooldown, 0.05, 3, "blackhole_duration_r11"));

            // 29 §10.4 rules: A1 ring 7 off the stem, A2 ring 9 after it (moved from ring 5), on the outer edge of the right
            // side; A5 ring 11 on the black hole's branch.
            n.Add(Rule("A1", NodeCategory.Ability, 7, -28, 1, "ability_satellite"));
            n.Add(Rule("A2", NodeCategory.Ability, 9, -28, 1, "rule_A1"));
            n.Add(Rule("A5", NodeCategory.Ability, 11, +24, 1, "ability_blackhole"));
        }

        static NodeDef AbilityUnlock(string ability, int ring, string prereq) =>
            new NodeDef
            {
                id = "ability_" + ability, kind = NodeKind.AbilityUnlock, abilityId = ability, maxLevel = 1, baseCost = StatCost(ring),
                growth = 1, minTier = ring, prereqs = prereq != null ? new List<string> { prereq } : new List<string>(),
                angleDeg = Angle(NodeCategory.Ability, 0f), category = NodeCategory.Ability, enabled = true,
            };

        static NodeDef AbilityStat(string id, string ability, int ring, float offset, string stat, double perLevel, int maxLevel, string prereq)
        {
            var s = Stat(id, NodeCategory.Ability, ring, offset, stat, perLevel, maxLevel, prereq);
            s.abilityId = ability;
            return s;
        }

        static NodeDef AbilityEvolve(string ability, int ring, float offset, string prereq) =>
            new NodeDef
            {
                id = "evolve_" + ability, kind = NodeKind.AbilityEvolve, abilityId = ability, maxLevel = 1,
                baseCost = Nice(StatCost(ring) * EvolveCostMult), growth = 1, minTier = ring, prereqs = new List<string> { prereq },
                angleDeg = Angle(NodeCategory.Ability, offset), category = NodeCategory.Ability, enabled = true,
            };

        // ---------------- building blocks ----------------

        static float Stem(NodeCategory c)
        {
            switch (c)
            {
                case NodeCategory.Celestial: return CelestialStem;
                case NodeCategory.Dust: return DustStem;
                case NodeCategory.Gravity: return GravityStem;
                case NodeCategory.Ability: return AbilityStem;
                default: return TimeStem;
            }
        }

        /// <summary>The seed angle of a node: its stem plus the offset, kept in 0..360.</summary>
        static float Angle(NodeCategory c, float offset)
        {
            float a = (Stem(c) + offset) % 360f;
            return a < 0f ? a + 360f : a;
        }

        /// <summary>
        /// The inner rings are short: a side line sits this much further from its stem there, so its node clears the
        /// stem node (ring 1: 12° is about 50 px, less than a gate and a stat node side by side). Still within ±30°.
        /// </summary>
        static float Spread(int ring) => ring <= 1 ? 1.6f : ring == 2 ? 1.25f : 1f;

        /// <summary>
        /// One stat line: a node in every <paramref name="step"/>-th ring from <paramref name="first"/> to
        /// <paramref name="last"/>, worth <paramref name="ringTotal"/>(ring) over <paramref name="levels"/> levels.
        /// </summary>
        static void Line(List<NodeDef> n, NodeCategory c, string name, string stat, float offset, int first, int last, int levels,
            Func<int, double> ringTotal, Func<int, string> prereq, int step = 1)
        {
            for (int r = first; r <= last; r += step)
            {
                var pre = prereq(r);
                n.Add(Stat(name + "_r" + r, c, r, offset, stat, ringTotal(r) / levels, levels, pre != null ? new[] { pre } : new string[0]));
            }
        }

        static NodeDef Stat(string id, NodeCategory c, int ring, float offset, string stat, double effect, int maxLevel, params string[] prereqs) =>
            new NodeDef
            {
                id = id, kind = NodeKind.Stat, statId = stat, effectPerLevel = effect, maxLevel = maxLevel,
                baseCost = StatCost(ring), growth = StatCostGrowth, minTier = ring, prereqs = new List<string>(prereqs),
                angleDeg = Angle(c, offset * Spread(ring)), category = c, enabled = true,
            };

        /// <summary>A ring 11 node with unlimited levels (12 §6), climbing faster than the capped ones.</summary>
        static NodeDef Endless(string id, NodeCategory c, float offset, string stat, double effect, string prereq)
        {
            var e = Stat(id, c, 11, offset, stat, effect, 0, prereq);
            e.growth = EndlessGrowth;
            return e;
        }

        /// <summary>29 §5: a rule node. Costs <see cref="RuleCostMult"/> × a stat node of its ring; no effect value yet.</summary>
        static NodeDef Rule(string ruleId, NodeCategory c, int ring, float offset, int maxLevel, string prereq) =>
            new NodeDef
            {
                id = "rule_" + ruleId, kind = NodeKind.Rule, ruleId = ruleId, maxLevel = maxLevel,
                baseCost = Nice(StatCost(ring) * RuleCostMult), growth = StatCostGrowth, minTier = ring,
                prereqs = new List<string> { prereq }, angleDeg = Angle(c, offset * Spread(ring)), category = c, enabled = true,
            };

        /// <summary>Gate to tier t: the celestial stem in ring t − 1, after the previous gate. 3 × I of its ring (20 §8.2).</summary>
        static NodeDef Gate(int tier) =>
            new NodeDef
            {
                id = "gate_t" + tier, kind = NodeKind.TierUnlock, tier = tier, maxLevel = 1, baseCost = GateCost(tier - 1), growth = 1,
                minTier = tier - 1, prereqs = tier > 2 ? new List<string> { "gate_t" + (tier - 1) } : new List<string>(),
                angleDeg = CelestialStem, category = NodeCategory.Celestial, enabled = true,
            };
    }
}
