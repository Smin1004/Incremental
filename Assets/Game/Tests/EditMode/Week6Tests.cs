using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Incremental.Tests
{
    /// <summary>Week 6 (20): dust pull shaping (§3), dust kinds (§4), fragments (§5), the three week-5 answers (§6) and the balance pass (§8).</summary>
    public class Week6Tests
    {
        const double Eps = 1e-9;

        static GameParams ProjectParams() => AssetDatabase.LoadAssetAtPath<GameParams>("Assets/Game/Data/GameParams.asset");
        static CelestialTable ProjectTiers() => AssetDatabase.LoadAssetAtPath<CelestialTable>("Assets/Game/Data/CelestialTable.asset");
        static NodeTable ProjectNodes() => AssetDatabase.LoadAssetAtPath<NodeTable>("Assets/Game/Data/NodeTable.asset");

        // ---------------- §3 A. dust pull ----------------

        [Test]
        public void PullCurve_FollowsTheSpecTable()
        {
            const double r = 180, full = 0.75, edge = 0.15, outer = 1.4;
            Assert.AreEqual(1.0, DustField.PullFactor(0, r, full, edge, outer), Eps, "at the cursor");
            Assert.AreEqual(1.0, DustField.PullFactor(r * 0.75, r, full, edge, outer), Eps, "full out to 0.75R");
            Assert.AreEqual(edge, DustField.PullFactor(r, r, full, edge, outer), Eps, "edge share at the radius");
            Assert.AreEqual(edge * 0.5, DustField.PullFactor(r * 1.2, r, full, edge, outer), 1e-6, "halfway through the outer band");
            Assert.AreEqual(0.0, DustField.PullFactor(r * 1.4, r, full, edge, outer), Eps, "gone at 1.4R");
            Assert.AreEqual(0.0, DustField.PullFactor(r * 2, r, full, edge, outer), Eps, "gone beyond 1.4R");
        }

        [Test]
        public void PullCurve_DecreasesAndIsSmoothAtTheHandover()
        {
            const double r = 180, full = 0.75, edge = 0.15, outer = 1.4;
            double prev = 2;
            for (int i = 0; i <= 300; i++)
            {
                double f = DustField.PullFactor(r * outer * i / 300.0, r, full, edge, outer);
                Assert.LessOrEqual(f, prev + 1e-12, "the pull never grows with distance");
                prev = f;
            }
            // No cliff at the radius: the two pieces meet at edgeShare.
            double inside = DustField.PullFactor(r - 0.01, r, full, edge, outer);
            double outside = DustField.PullFactor(r + 0.01, r, full, edge, outer);
            Assert.AreEqual(inside, outside, 2e-3, "the curve is continuous at the gravity radius");
        }

        [Test]
        public void SweptCapture_CatchesDustThatWouldStepOverTheCircle()
        {
            var center = new Vector2(0f, 0f);
            // A dust flying straight through the circle in one tick: both ends are outside, the segment is not.
            Assert.IsTrue(DustField.SegmentHitsCircle(new Vector2(-1f, 0f), new Vector2(1f, 0f), center, 0.3f), "passes through");
            Assert.IsFalse(DustField.SegmentHitsCircle(new Vector2(-1f, 1f), new Vector2(1f, 1f), center, 0.3f), "misses to the side");
            Assert.IsTrue(DustField.SegmentHitsCircle(new Vector2(0.1f, 0f), new Vector2(0.2f, 0f), center, 0.3f), "ends inside");
            Assert.IsFalse(DustField.SegmentHitsCircle(new Vector2(2f, 0f), new Vector2(2f, 0f), center, 0.3f), "standing still outside");
        }

        [Test]
        public void PullParams_AreTheSpecValues()
        {
            var p = ProjectParams();
            Assert.AreEqual(0.75, p.pullFullRatio, Eps);
            Assert.AreEqual(0.15, p.pullEdgeShare, Eps);
            Assert.AreEqual(1.4, p.pullOuterRatio, Eps);
            Assert.AreEqual(2.5, p.captureAssistRatio, Eps);
            Assert.AreEqual(0.6, p.tangentialKeepPerSec, Eps, "40% of the tangential speed is damped per second");
        }

        [Test]
        public void PullParams_AreValidated()
        {
            var p = TestData.Params();
            p.pullFullRatio = 1.2;
            p.pullEdgeShare = 1.5;
            p.pullOuterRatio = 0.9;
            p.captureAssistRatio = 0.5;
            p.tangentialKeepPerSec = 0;
            var r = DataValidator.ValidateParams(p);
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("pullFullRatio")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("pullEdgeShare")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("pullOuterRatio")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("captureAssistRatio")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("tangentialKeepPerSec")), Joined(r.Errors));
            Object.DestroyImmediate(p);
        }

        // ---------------- §4 B. dust kinds ----------------

        [Test]
        public void DustKinds_MatchTheSpecTable()
        {
            var p = ProjectParams();
            Assert.AreEqual(5, p.DustKindCount, "base + medium, large, crystal, interstellar (20 §4)");
            var expected = new (string id, double mass, int ring, double baseChance, double perLevel, double max, double size, double accel)[]
            {
                // 22 §8 raised the medium share to 15% (21 §6-3) and the interstellar one to 6%; the rest is 20 §4.
                // 29 §3.2: the same full chance over 5 levels instead of 4.
                ("medium", 5, 3, 0.03, 0.024, 0.15, 1.7, 0.75),
                ("large", 25, 5, 0.03, 0.012, 0.09, 2.9, 0.55),
                ("crystal", 125, 7, 0.02, 0.008, 0.06, 4.9, 0.40),
                ("interstellar", 625, 9, 0.02, 0.008, 0.06, 8.5, 0.30),
            };
            Assert.AreEqual("base", p.DustKind(0).id);
            Assert.AreEqual(1, p.DustKind(0).mass, Eps);
            for (int i = 0; i < expected.Length; i++)
            {
                var e = expected[i];
                var k = p.DustKind(i + 1);
                Assert.AreEqual(e.id, k.id);
                Assert.AreEqual(e.mass, k.mass, Eps, e.id + " mass");
                Assert.AreEqual(e.ring, k.unlockRing, e.id + " unlock ring");
                Assert.AreEqual(e.baseChance, k.baseChance, Eps, e.id + " unlock chance");
                Assert.AreEqual(e.perLevel, k.chancePerLevel, Eps, e.id + " chance per level");
                Assert.AreEqual(5, k.chanceMaxLevel, e.id + " chance levels");
                Assert.AreEqual(e.max, k.MaxChance, 1e-9, e.id + " chance at full level");
                Assert.AreEqual(e.size, k.sizeMult, Eps, e.id + " size");
                Assert.AreEqual(e.accel, k.accelMult, Eps, e.id + " pull acceleration");
                Assert.Greater(k.brightMult, 1.0, e.id + " reads a little brighter than base dust");
            }
        }

        [Test]
        public void DustKindAxis_UnlocksInRings3579_AndRaisesInBetween()
        {
            var n = ProjectNodes();
            var p = ProjectParams();
            foreach (var kindId in new[] { "medium", "large", "crystal", "interstellar" })
            {
                var unlock = n.DustUnlock(kindId);
                Assert.IsNotNull(unlock, "unlock node for " + kindId);
                Assert.AreEqual(NodeKind.DustKindUnlock, unlock.kind);
                Assert.AreEqual(1, unlock.maxLevel);
                Assert.AreEqual(p.DustKind(p.DustKindIndex(kindId)).unlockRing, unlock.minTier, kindId + " unlock ring");
                var chance = n.nodes.Find(x => x.statId == StatIds.DustChance && x.dustKindId == kindId);
                Assert.IsNotNull(chance, "chance node for " + kindId);
                Assert.AreEqual(5, chance.maxLevel);
                Assert.AreEqual(unlock.minTier + 1, chance.minTier, kindId + " chance node sits one ring above its unlock");
                // 29 §3.2: one line beside the dust stem.
                Assert.AreEqual(NodeCategory.Dust, unlock.category);
                Assert.AreEqual(unlock.angleDeg, chance.angleDeg, 1e-3, "the kind line runs straight out");
            }
        }

        [Test]
        public void DustMassNodes_AreBackAsALine_AndCompound()
        {
            // 29 §3.2 brought dust_mass back as its own line (rings 5, 7, 9, 11, x1.1 per level, compound).
            var n = ProjectNodes();
            var m = new MetaState();
            int lines = 0;
            foreach (var node in n.nodes)
                if (node.statId == StatIds.DustMass)
                {
                    lines++;
                    Assert.Greater(node.effectPerLevel, 1, node.id);
                    m.SetLevel(node.id, node.maxLevel);
                }
            Assert.AreEqual(4, lines);
            Assert.AreEqual(System.Math.Pow(1.1, 12), Stats.Compute(ProjectParams(), n, m).dustMass / ProjectParams().dustMass, 1e-9);
            var r = DataValidator.Validate(ProjectTiers(), n, ProjectParams());
            Assert.IsEmpty(r.Errors, Joined(r.Errors));
            Assert.IsEmpty(r.Warnings, Joined(r.Warnings));
        }

        [Test]
        public void LockedKind_NeverSpawns_AndUnlockingMixesItIn()
        {
            var p = ProjectParams();
            var n = ProjectNodes();
            var locked = Stats.Compute(p, n, new MetaState());
            for (int k = 1; k < p.DustKindCount; k++) Assert.AreEqual(0, locked.KindChance(k), Eps, p.DustKind(k).id + " is locked");
            Assert.AreEqual(1, locked.KindChance(0), Eps, "every dust is base dust");
            Assert.AreEqual(p.dustMass, locked.dustMassAvg, Eps);

            var m = new MetaState();
            m.SetLevel("dust_medium", 1);
            var open = Stats.Compute(p, n, m);
            Assert.AreEqual(0.03, open.KindChance(1), Eps, "the unlock alone gives the base chance");
            Assert.AreEqual(0.97, open.KindChance(0), Eps);
            Assert.AreEqual(0.97 * 1 + 0.03 * 5, open.dustMassAvg, 1e-9);

            m.SetLevel("chance_medium_r4", 5);
            Assert.AreEqual(0.15, Stats.Compute(p, n, m).KindChance(1), 1e-9, "five levels of +2.4%p (29 §3.2)");
        }

        [Test]
        public void ChanceNode_DoesNothingWithoutItsUnlock()
        {
            var m = new MetaState();
            m.SetLevel("chance_medium_r4", 5);
            Assert.AreEqual(0, Stats.Compute(ProjectParams(), ProjectNodes(), m).KindChance(1), Eps);
        }

        [Test]
        public void EveryKindUnlocked_KeepsRoomForBaseDust_AndRaisesTheAverageMass()
        {
            var p = ProjectParams();
            var n = ProjectNodes();
            var m = new MetaState();
            foreach (var id in new[] { "medium", "large", "crystal", "interstellar" }) m.SetLevel(n.DustUnlock(id).id, 1);
            foreach (var node in n.nodes)
                if (node.statId == StatIds.DustChance) m.SetLevel(node.id, node.maxLevel);
            var s = Stats.Compute(p, n, m);
            double total = 0;
            for (int k = 1; k < p.DustKindCount; k++) total += s.KindChance(k);
            Assert.Less(total, 1.0, "the base dust always keeps a share");
            Assert.AreEqual(1.0 - total, s.KindChance(0), 1e-12);
            // 20 §4: the whole axis has to be worth a lot more than the compound node it replaced.
            Assert.Greater(s.dustMassAvg, 30, "every kind at full chance lifts the average dust mass well above 1");
        }

        [Test]
        public void SaveVersion_IsBumped_AndOlderSavesStartOver()
        {
            // 20 §4 made v3 (the dust kind axis changed node ids); 28 §2 made v4, which migrates v3 instead of resetting it.
            Assert.GreaterOrEqual(SaveMigration.CurrentVersion, 3, "the dust kind axis changed node ids (20 §4)");
            Assert.IsTrue(SaveMigration.ResetsProgress(2));
            Assert.IsFalse(SaveMigration.ResetsProgress(3));
        }

        [Test]
        public void DustKindNodes_AreNamedAfterTheirKind()
        {
            var n = ProjectNodes();
            var p = ProjectParams();
            Assert.AreEqual("중형 먼지", UIStrings.NodeName(n, n.DustUnlock("medium")));
            StringAssert.StartsWith("중형 먼지", UIStrings.NodeName(n, n.Get("chance_medium_r4")));
            StringAssert.EndsWith("I", UIStrings.NodeName(n, n.Get("chance_medium_r4")), "each kind numbers its own chance node from I");
            StringAssert.StartsWith("성간 먼지", UIStrings.NodeName(n, n.Get("chance_interstellar_r10")));
            StringAssert.EndsWith("I", UIStrings.NodeName(n, n.Get("chance_interstellar_r10")));
            // 29 §8 N1: the crystal dust is called 광석 먼지 now (the id stays).
            Assert.AreEqual("광석 먼지", UIStrings.NodeName(n, n.DustUnlock("crystal")));
            StringAssert.Contains("3", UIStrings.NodeEffect(n.DustUnlock("medium"), p), "the unlock says the starting share");
        }

        [Test]
        public void BrokenDustData_IsReported()
        {
            var p = TestData.Params();
            p.dustKinds[1].baseChance = 0.9;
            p.dustKinds[2].baseChance = 0.9;
            var r = DataValidator.ValidateParams(p);
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("add up")), Joined(r.Errors));
            Object.DestroyImmediate(p);
        }

        // ---------------- §5 C. fragments ----------------

        [Test]
        public void FragmentLine_HasTheSevenNodesOfTheSpecTable()
        {
            // 29 §3.1: unlock 3, chance 4·6·8 (x3 levels of +5%p), range 5·7·9 (one level, ring 9 new).
            var n = ProjectNodes();
            var expected = new (string id, int ring, string stat, double effect, int levels)[]
            {
                ("frag_unlock", 3, StatIds.FragUnlock, 0.30, 1),
                ("frag_chance_r4", 4, StatIds.FragChance, 0.05, 3),
                ("frag_range_r5", 5, StatIds.FragOffset, 1, 1),
                ("frag_chance_r6", 6, StatIds.FragChance, 0.05, 3),
                ("frag_range_r7", 7, StatIds.FragOffset, 1, 1),
                ("frag_chance_r8", 8, StatIds.FragChance, 0.05, 3),
                ("frag_range_r9", 9, StatIds.FragOffset, 1, 1),
            };
            float angle = n.Get("frag_unlock").angleDeg;
            foreach (var e in expected)
            {
                var node = n.Get(e.id);
                Assert.IsNotNull(node, e.id);
                Assert.AreEqual(e.ring, node.minTier, e.id + " ring");
                Assert.AreEqual(e.stat, node.statId, e.id + " stat");
                Assert.AreEqual(e.effect, node.effectPerLevel, Eps, e.id + " effect");
                Assert.AreEqual(e.levels, node.maxLevel, e.id + " levels");
                Assert.AreEqual(NodeCategory.Celestial, node.category, e.id);
                Assert.AreEqual(angle, node.angleDeg, 1e-3, "one straight line beside the celestial stem");
            }
        }

        [Test]
        public void FragmentChanceAndRange_AddUpAlongTheAxis()
        {
            var p = ProjectParams();
            var n = ProjectNodes();
            var m = new MetaState();
            Assert.AreEqual(0, Stats.Compute(p, n, m).fragChance, Eps, "fragments are off before the unlock");
            Assert.AreEqual(0, Stats.FragmentMaxTier(8, Stats.Compute(p, n, m)), "and nothing drops");

            m.SetLevel("frag_unlock", 1);
            var s = Stats.Compute(p, n, m);
            Assert.AreEqual(0.30, s.fragChance, Eps, "unlock: 30%");
            Assert.AreEqual(4, s.fragOffset, "unlock: offset 4");

            m.SetLevel("frag_chance_r4", 3);
            Assert.AreEqual(0.45, Stats.Compute(p, n, m).fragChance, Eps);
            m.SetLevel("frag_range_r5", 1);
            Assert.AreEqual(3, Stats.Compute(p, n, m).fragOffset);
            m.SetLevel("frag_chance_r6", 3);
            Assert.AreEqual(0.60, Stats.Compute(p, n, m).fragChance, Eps);
            m.SetLevel("frag_range_r7", 1);
            Assert.AreEqual(2, Stats.Compute(p, n, m).fragOffset);
            m.SetLevel("frag_chance_r8", 3);
            m.SetLevel("frag_range_r9", 1);
            var full = Stats.Compute(p, n, m);
            Assert.AreEqual(0.75, full.fragChance, Eps, "the whole line: 75%");
            Assert.AreEqual(1, full.fragOffset);
            Assert.AreEqual(10, Stats.FragmentMaxTier(11, full), "at the top ring a fragment can be one tier below (29 §3.1: ring 9 range)");
        }

        [Test]
        public void FragmentTierCap_NeverFallsBelowTierOne()
        {
            var p = ProjectParams();
            var m = new MetaState();
            m.SetLevel("frag_unlock", 1);
            var s = Stats.Compute(p, ProjectNodes(), m);
            // 20 §5 leaves this open: with offset 4 the cap would be below 1 in rings 3 and 4, and the node the player
            // just bought would do nothing at all, so the cap is clamped to tier 1.
            Assert.AreEqual(1, Stats.FragmentMaxTier(3, s), "ring 3 drops asteroids");
            Assert.AreEqual(1, Stats.FragmentMaxTier(4, s));
            Assert.AreEqual(1, Stats.FragmentMaxTier(5, s));
            Assert.AreEqual(2, Stats.FragmentMaxTier(6, s));
            Assert.AreEqual(7, Stats.FragmentMaxTier(11, s));
        }

        [Test]
        public void FragmentSellsForHalfItsTier()
        {
            var p = ProjectParams();
            var c = ProjectTiers();
            Assert.AreEqual(0.5, p.fragmentSaleShare, Eps, "20 §5");
            Assert.AreEqual(4, p.fragmentBaseOffset);
            var s = Stats.Compute(p, ProjectNodes(), new MetaState());
            var tier = c.Get(5);
            Assert.AreEqual(Stats.SaleIncome(tier, s) * 0.5, Stats.FragmentIncome(p, tier, s), Eps);
        }

        [Test]
        public void FragmentsAreCountedApartButShownTogether()
        {
            var rec = new RunRecord { tierCounts = new[] { 2, 1, 0 }, fragmentCounts = new[] { 5, 0, 3 } };
            Assert.AreEqual(2, rec.DirectCount(1));
            Assert.AreEqual(5, rec.FragmentCount(1));
            Assert.AreEqual(7, rec.TierCount(1), "the result panel adds them up (20 §5)");
            Assert.AreEqual(1, rec.TierCount(2));
            Assert.AreEqual(3, rec.TierCount(3), "a tier reached only by fragments still shows");
            Assert.AreEqual(0, rec.TierCount(9), "out of range");
        }

        [Test]
        public void FragmentsDoNotRaiseTheHighestTierMade()
        {
            var meta = new MetaState();
            var rec = new RunRecord
            {
                run = 1, income = 10, endReason = EndReason.TimeUp,
                tierCounts = new[] { 3, 0, 0 }, fragmentCounts = new[] { 0, 0, 4 }, fragmentIncome = 2,
            };
            Session.ApplyRunEnd(meta, rec);
            Assert.AreEqual(1, meta.highestTierCreated, "only bodies made from held mass move the central star stage");
            Assert.AreEqual(3, meta.totalPlanets[0]);
            Assert.AreEqual(4, meta.totalFragments[2]);
            Assert.AreEqual(0, meta.totalFragments[0]);
        }

        [Test]
        public void BrokenFragmentData_IsReported()
        {
            var p = TestData.Params();
            p.fragmentSaleShare = 1.5;
            p.fragmentBaseOffset = 0;
            p.fragmentScale = 0;
            var r = DataValidator.ValidateParams(p);
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("fragmentSaleShare")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("fragmentBaseOffset")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("fragmentScale")), Joined(r.Errors));
            Object.DestroyImmediate(p);
        }

        // ---------------- §6 D-1. the three week-5 answers ----------------

        [Test]
        public void W5_1_FlashEdgeShareIsHalved()
        {
            Assert.AreEqual(0.15, ProjectParams().flashEdgeShare, Eps, "0.3 → 0.15 (20 §6 W5-1)");
        }

        [Test]
        public void W5_2_EndingShowsWhicheverSystemIsFuller()
        {
            var poor = new RunRecord { run = 9, tierCounts = new[] { 1 }, endReason = EndReason.Ending };
            var rich = new RunRecord { run = 5, tierCounts = new[] { 4, 2 }, fragmentCounts = new[] { 3 }, endReason = EndReason.TimeUp };
            Assert.AreSame(rich, Session.RicherSystem(poor, rich), "a two-second ending run must not leave the sky empty");
            Assert.AreSame(rich, Session.RicherSystem(rich, poor));
            Assert.AreSame(rich, Session.RicherSystem(null, rich));
            Assert.AreSame(poor, Session.RicherSystem(poor, null));
            Assert.IsNull(Session.RicherSystem(null, null));
            Assert.AreEqual(9, Session.TotalBodies(rich), "fragments count toward how full a system looks");
            // A tie keeps this run's own system.
            var same = new RunRecord { run = 5, tierCounts = new[] { 1 }, endReason = EndReason.TimeUp };
            Assert.AreSame(poor, Session.RicherSystem(poor, same));
        }

        [Test]
        public void W5_2_BestRunIsTheTimeUpRunWithTheHighestIncome()
        {
            var m = new MetaState();
            Session.ApplyRunEnd(m, new RunRecord { run = 1, income = 100, endReason = EndReason.TimeUp });
            Session.ApplyRunEnd(m, new RunRecord { run = 2, income = 500, endReason = EndReason.TimeUp });
            Session.ApplyRunEnd(m, new RunRecord { run = 3, income = 900, endReason = EndReason.Quit });
            Session.ApplyRunEnd(m, new RunRecord { run = 4, income = 300, endReason = EndReason.TimeUp });
            Assert.AreEqual(2, m.BestRun.run, "quit runs are not the best, however rich");
            Assert.AreEqual(500, m.bestRunIncome, Eps);
        }

        [Test]
        public void W5_3_EndingRunIsOutOfTheRatioAndTheBest()
        {
            var m = new MetaState();
            Session.ApplyRunEnd(m, Session.MakeRecord(m, 1, 60, 100, new[] { 3 }, null, default(EffectiveStats), EndReason.TimeUp));
            var ending = Session.MakeRecord(m, 2, 2, 5000, new[] { 0, 0, 1 }, null, default(EffectiveStats), EndReason.Ending);
            Assert.AreEqual(-1, ending.ratioVsLast, Eps, "no ratio, like a quit run");
            Session.ApplyRunEnd(m, ending);
            Assert.AreEqual(100, m.lastRunIncome, Eps, "the ratio base stays the last run that ran out of time");
            Assert.AreEqual(100, m.bestRunIncome, Eps, "and so does the best");
            Assert.AreEqual(5100, m.totalIncome, Eps, "the income and the statistics still count it");
            Assert.AreEqual(2, m.endingRun, "and it is still the ending run");

            var after = Session.MakeRecord(m, 3, 60, 200, new[] { 4 }, null, default(EffectiveStats), EndReason.TimeUp);
            Assert.AreEqual(2.0, after.ratioVsLast, Eps, "the run after the ending is measured against run 1, not the ending");
        }

        [Test]
        public void ComparisonRun_SkipsTheEndingRun()
        {
            var history = new List<RunRecord>
            {
                new RunRecord { run = 1, endReason = EndReason.TimeUp, tierCounts = new[] { 2 } },
                new RunRecord { run = 2, endReason = EndReason.Ending, tierCounts = new[] { 0 } },
                new RunRecord { run = 3, endReason = EndReason.TimeUp, tierCounts = new[] { 5 } },
            };
            Assert.AreEqual(1, ResultMath.ComparisonRun(history, history[2]).run);
        }

        // ---------------- §7 D-2. gravity source follow ----------------
        // The 20 §7 experiment became a rule in 22 §3 (on by default, five tree nodes), so what it does now is
        // checked in Week7Tests instead.

        // ---------------- §8 E. price curve and costs ----------------

        [Test]
        public void PriceCurve_IsTheSpecTable()
        {
            var c = ProjectTiers();
            double[] expected = { 1, 4, 15, 61, 262, 1050, 4190, 16100, 64400, 275000, 1100000 };
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], c.Get(i + 1).salePrice, Eps, "tier " + (i + 1) + " sale price (20 §8.1)");
        }

        [Test]
        public void PriceCurve_RaisesTheValueOfOneUnitOfMassBy16PercentPerTier()
        {
            var c = ProjectTiers();
            double prev = 0;
            for (int t = 1; t <= c.MaxTier; t++)
            {
                var tier = c.Get(t);
                // The written price is the formula rounded for reading, so allow 3%.
                double raw = tier.requiredMass * 0.1 * System.Math.Pow(1.6, t - 1);
                Assert.AreEqual(raw, tier.salePrice, raw * 0.03, "tier " + t + " is off its formula price");
                double perMass = tier.salePrice / tier.requiredMass;
                if (t > 1) Assert.AreEqual(1.6, perMass / prev, 0.05, "tier " + t + " value per unit of mass");
                prev = perMass;
            }
            // 19 §1: tier 11 used to be 100,000 times tier 1 per unit of mass; the point of the change is that it is not.
            Assert.AreEqual(110, (c.Get(11).salePrice / c.Get(11).requiredMass) / (c.Get(1).salePrice / c.Get(1).requiredMass), 3,
                "tier 11 is about 110 times tier 1 per unit of mass, not 100,000");
        }

        [Test]
        public void EveryNodeCost_ComesFromTheRingFormula()
        {
            var t = ProjectNodes();
            // 20 §8.2: a node is 0.4 × I_k with growth 1.5, a gate is 3 × I_k. Only ringCostMult differs between rings,
            // so within one ring every capped node must start at the same price.
            for (int ring = 1; ring <= 11; ring++)
            {
                double baseCost = -1;
                foreach (var n in t.nodes)
                {
                    if (n.minTier != ring || n.IsGate || n.maxLevel == 0) continue;
                    // 24 §21.2: an evolve node is priced at its ability's unlock x 2 until the balance pass decides.
                    if (n.IsAbilityEvolve) continue;
                    // 13th pass B1: a rule node is 1.5 x its ring's node price until the balance pass prices it.
                    if (n.IsRule) continue;
                    if (baseCost < 0) baseCost = n.baseCost;
                    Assert.AreEqual(baseCost, n.baseCost, Eps, "'" + n.id + "' does not use its ring's node price (20 §8.2)");
                }
                var gate = t.Gate(ring + 1);
                if (gate == null || baseCost < 0) continue;
                // gate / node = 3 / 0.4 = 7.5, up to the two-significant-digit rounding of both.
                Assert.AreEqual(7.5, gate.baseCost / baseCost, 0.4, "gate of ring " + ring + " against its node price");
            }
        }

        [Test]
        public void CappedNodes_GrowBy135PercentPerLevel()
        {
            var t = ProjectNodes();
            foreach (var n in t.nodes)
            {
                if (n.IsGate || n.maxLevel == 1) continue;
                if (n.maxLevel == 0) Assert.AreEqual(1.6, n.growth, Eps, "'" + n.id + "' is endless (12 §6)");
                else Assert.AreEqual(1.35, n.growth, Eps, "'" + n.id + "' growth (29 §3: x1.35, was x1.5)");
            }
        }

        [Test]
        public void RingCostMult_IsTheOnlyPerRingKnob_AndStaysSane()
        {
            var t = ProjectNodes();
            Assert.AreEqual(12, t.ringCostMult.Length, "one entry per ring, plus the unused index 0");
            foreach (var mult in t.ringCostMult)
            {
                Assert.Greater(mult, 0.0);
                // 22 §8 changed the growth axes without re-measuring I_k, so the multipliers carry that difference too;
                // rings 8-10 need 11.5, 3.8 and 8.4. Week7.md §9-4 lists re-measuring I_k as the way to bring them back.
                Assert.That(mult, Is.InRange(0.3, 12.0), "a multiplier this far from 1 means the measured I_k is wrong");
            }
        }

        static string Joined(List<string> list) => string.Join("\n", list);
    }
}
