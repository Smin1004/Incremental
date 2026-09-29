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
        /// <summary>
        /// 22 §3: top speed the gravity source follows the cursor at, px per second. The base is GameParams.followSpeedPx
        /// and the follow nodes add to it; whether it is used at all is GameParams.followEnabled.
        /// </summary>
        public double followSpeedPx;
        /// <summary>
        /// 22 §6: how much wider the play world is than at ring 1. The camera zooms out by it, the dust cap and the
        /// starting dust go up with its square (area), and everything in the world keeps its own size.
        /// </summary>
        public double worldScale;
        /// <summary>
        /// 20 §4: share of newly spawned dust per dust kind, same indices as GameParams.dustKinds. Index 0 is the base
        /// dust and holds whatever the others leave; a locked kind is 0. Never null after <see cref="Stats.Compute"/>.
        /// </summary>
        public double[] dustKindChance;
        /// <summary>Expected mass of one spawned dust with those chances (20 §4: the "링별 평균 먼지 질량").</summary>
        public double dustMassAvg;
        /// <summary>20 §5: chance that crossing a tier threshold drops a fragment. 0 = fragments are not unlocked yet.</summary>
        public double fragChance;
        /// <summary>20 §5: fragments only come from tiers up to unlockedMaxTier − this (never below tier 1).</summary>
        public int fragOffset;

        /// <summary>Share of spawned dust of that kind index, 0 when it is out of range (a save written before 20 §4).</summary>
        public double KindChance(int index) =>
            dustKindChance != null && index >= 0 && index < dustKindChance.Length ? dustKindChance[index] : 0;
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

        public static EffectiveStats Compute(GameParams p, NodeTable t, MetaState m) =>
            Compute(p, t, m, p.staminaHoldDrain, p.staminaIdleDrain);

        /// <summary>
        /// Same, with the two stamina drains taken from somewhere else than the asset: the stamina debug key hands its
        /// preset in (18 §A1) so the node reduction still applies to it. The asset is untouched.
        /// </summary>
        public static EffectiveStats Compute(GameParams p, NodeTable t, MetaState m, double holdDrainBase, double idleDrainBase)
        {
            double spawn = 0, pull = 0, sale = 0, stamina = 0, threshold = 0, cap = 0, radius = 0, drain = 0, start = 0, follow = 0;
            double mass = 1, frag = 0, fragRange = 0;
            // 20 §4: extra share per dust kind from its chance node; the kind only appears once its unlock node is owned.
            int kinds = p.DustKindCount;
            var kindAdd = kinds > 0 ? new double[kinds] : System.Array.Empty<double>();
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
                    case StatIds.DustChance:
                    {
                        int k = p.DustKindIndex(n.dustKindId);
                        if (k > 0) kindAdd[k] += add;
                        break;
                    }
                    case StatIds.SpawnRate: spawn += add; break;
                    case StatIds.PullAccel: pull += add; break;
                    case StatIds.SaleMult: sale += add; break;
                    case StatIds.StaminaMax: stamina += add; break;
                    case StatIds.ThresholdMult: threshold += add; break;
                    case StatIds.DustCap: cap += add; break;
                    case StatIds.DustMass: mass *= Math.Pow(n.effectPerLevel, level); break;
                    case StatIds.GravityRadius: radius += add; break;
                    case StatIds.FollowSpeed: follow += add; break;
                    case StatIds.StaminaDrain: drain += add; break;
                    case StatIds.StartBonus: start += add; break;
                    case StatIds.FragUnlock:
                    case StatIds.FragChance: frag += add; break;
                    case StatIds.FragOffset: fragRange += add; break;
                }
            }

            double world = WorldScale(p, m.unlockedMaxTier);
            double area = world * world;
            var s = new EffectiveStats
            {
                worldScale = world,
                spawnRate = p.spawnRate + spawn,
                pullAccel = p.pullAccel * (1.0 + pull),
                saleMult = p.saleMult * (1.0 + sale),
                staminaMax = p.staminaMax + stamina,
                thresholdMult = p.thresholdMult * (1.0 - Math.Min(threshold, MaxReduction)),
                // 22 §6: the cap follows the area, so a wider world holds the same density of dust.
                dustCap = (p.dustCap + cap) * area,
                dustMass = p.dustMass * mass,
                gravityRadius = p.gravityRadius * (1.0 + radius),
                followSpeedPx = p.followSpeedPx * (1.0 + follow),
            };
            double drainMult = 1.0 - Math.Min(drain, MaxReduction);
            s.staminaIdleDrain = idleDrainBase * drainMult;
            s.staminaHoldDrain = holdDrainBase * drainMult;
            // The starting dust follows the area for the same reason: without it a late run would open on an empty
            // world and take longer than the run itself to fill up.
            s.dustInitial = Math.Min((p.dustInitial + start) * area, s.dustCap);
            s.fragChance = Math.Clamp(frag, 0.0, 1.0);
            s.fragOffset = (int)Math.Max(0, p.fragmentBaseOffset - Math.Round(fragRange));
            FillDustKinds(p, t, m, kindAdd, ref s);
            return s;
        }

        /// <summary>
        /// 20 §4: turns the owned unlock nodes and chance levels into one share per dust kind. Shares do not overlap; if
        /// they ever added up past 1 they are scaled down together, and the base dust keeps the rest. Also works out the
        /// expected mass of a spawned dust, which is what the balance pass reads per ring.
        /// </summary>
        static void FillDustKinds(GameParams p, NodeTable t, MetaState m, double[] kindAdd, ref EffectiveStats s)
        {
            int kinds = p.DustKindCount;
            s.dustKindChance = new double[kinds];
            if (kinds == 0)
            {
                s.dustMassAvg = p.dustMass;
                return;
            }
            double taken = 0;
            for (int k = 1; k < kinds; k++)
            {
                var def = p.DustKind(k);
                if (def == null || t == null) continue;
                var unlock = t.DustUnlock(def.id);
                if (unlock == null || !unlock.enabled || m.GetLevel(unlock.id) < 1) continue;
                double c = def.baseChance + kindAdd[k];
                if (c < 0) c = 0;
                s.dustKindChance[k] = c;
                taken += c;
            }
            if (taken > 1)
            {
                for (int k = 1; k < kinds; k++) s.dustKindChance[k] /= taken;
                taken = 1;
            }
            s.dustKindChance[0] = 1.0 - taken;
            double avg = 0;
            for (int k = 0; k < kinds; k++)
            {
                var def = p.DustKind(k);
                avg += s.dustKindChance[k] * (def != null ? def.mass : 1.0);
            }
            s.dustMassAvg = avg * s.dustMass;
        }

        /// <summary>True for stats whose effectPerLevel is a fraction shown as a percent.</summary>
        public static bool IsPercentEffect(string statId) =>
            statId == StatIds.PullAccel || statId == StatIds.SaleMult || statId == StatIds.ThresholdMult ||
            statId == StatIds.GravityRadius || statId == StatIds.StaminaDrain || statId == StatIds.FollowSpeed ||
            statId == StatIds.DustChance || statId == StatIds.FragUnlock || statId == StatIds.FragChance;

        /// <summary>Number substituted into the stat.&lt;id&gt;.effect string: percent for fraction effects, raw otherwise.</summary>
        public static double EffectDisplayNumber(NodeDef n) =>
            IsPercentEffect(n.statId) ? n.effectPerLevel * 100.0 : n.effectPerLevel;

        /// <summary>
        /// 22 §6: linear size of the play world at ring <paramref name="ring"/>, 1 + worldScalePerRing x (ring - 1).
        /// Ring 1 is 1 and ring 11 is 1.60 at the spec value. A pure function, so the tests can read the rule and the
        /// camera can ask for the target before the effective stats are recomputed.
        /// </summary>
        public static double WorldScale(GameParams p, int ring) =>
            1.0 + p.worldScalePerRing * Math.Max(0, ring - 1);

        public static double Threshold(CelestialTier tier, in EffectiveStats s) => tier.requiredMass * s.thresholdMult;

        /// <summary>
        /// 20 §5: the highest tier a fragment can be, given the tiers unlocked so far. It is the unlocked maximum minus
        /// the offset, but never below tier 1, so the first fragment node already drops something in the ring it is
        /// bought in (the spec leaves the early rings, where the offset is larger than the unlocked maximum, open).
        /// Returns 0 while fragments are locked.
        /// </summary>
        public static int FragmentMaxTier(int unlockedMaxTier, in EffectiveStats s) =>
            s.fragChance > 0 ? Math.Clamp(unlockedMaxTier - s.fragOffset, 1, Math.Max(unlockedMaxTier, 1)) : 0;

        /// <summary>Income of one fragment of this tier: its tier's income times the fragment share (20 §5).</summary>
        public static double FragmentIncome(GameParams p, CelestialTier tier, in EffectiveStats s) =>
            SaleIncome(tier, s) * p.fragmentSaleShare;

        /// <summary>
        /// Income of one planet of this tier: sale price × sale multiplier, fractions kept. The run total is rounded up once
        /// when the run ends (<see cref="RunIncome"/>), so "+25%" stays +25% at tier 1 (14 §8, replaces 12 §10-9).
        /// </summary>
        public static double SaleIncome(CelestialTier tier, in EffectiveStats s) => tier.salePrice * s.saleMult;

        /// <summary>Run income as paid and shown: the raw sum rounded up once, so currency stays an integer (14 §8).</summary>
        public static double RunIncome(double rawIncome) => CeilSnap(rawIncome);

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
