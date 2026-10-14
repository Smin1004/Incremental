using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace Incremental.Tests
{
    /// <summary>Data validation (project assets and broken tables) and the run_log.csv columns.</summary>
    public class DataTests
    {
        static string Joined(List<string> list) => string.Join("\n", list);

        // ---------------- project assets ----------------

        [Test]
        public void ProjectAssets_PassValidation_WithoutWarnings()
        {
            var c = AssetDatabase.LoadAssetAtPath<CelestialTable>("Assets/Game/Data/CelestialTable.asset");
            var n = AssetDatabase.LoadAssetAtPath<NodeTable>("Assets/Game/Data/NodeTable.asset");
            var p = AssetDatabase.LoadAssetAtPath<GameParams>("Assets/Game/Data/GameParams.asset");
            Assert.IsNotNull(c);
            Assert.IsNotNull(n);
            Assert.IsNotNull(p);
            var r = DataValidator.Validate(c, n, p);
            Assert.IsEmpty(r.Errors, Joined(r.Errors));
            Assert.IsEmpty(r.Warnings, Joined(r.Warnings));
            Assert.AreEqual(11, c.TierCount);
        }

        [Test]
        public void ProjectTree_MatchesSpecShape()
        {
            var n = AssetDatabase.LoadAssetAtPath<NodeTable>("Assets/Game/Data/NodeTable.asset");
            int gates = 0, endless = 0, stats = 0;
            foreach (var node in n.nodes)
            {
                if (node.IsGate) gates++;
                else if (node.maxLevel == 0) endless++;
                else stats++;
            }
            Assert.AreEqual(10, gates, "gates for tiers 2-11");
            Assert.AreEqual(3, endless, "ring 11 endless nodes");
            // 24 §20.2 added the crystal nodes and 24 §21.2 one evolve node per ability (rings 7-11).
            Assert.That(stats, Is.InRange(45, 90), "about 50 stat nodes (12 §6), plus the 22 §3 follow axis, the 22 §8 planet axis and the 24 §21.2 evolve nodes");
            // 20 §8.2: every cost comes from the measured entry income of its ring, 0.4 × I_k for a node and 3 × I_k
            // for a gate, so ring 1 (I = 9) is a node at 3.6 and the tier 2 gate at 27.
            Assert.AreEqual(3.6, n.Get("r1_spawn").baseCost, 1e-9);
            Assert.AreEqual(27, n.Get("gate_t2").baseCost, 1e-9);
            Assert.AreEqual(63, n.Get("gate_t3").baseCost, 1e-9);
            Assert.AreEqual(2, n.Get("r2_cap").minTier, "dust cap in ring 2 (12 §10-1)");
            Assert.AreEqual(3, n.Get("dust_medium").minTier, "the first dust kind unlocks in ring 3 (20 §4)");
        }

        // ---------------- validator ----------------

        [Test]
        public void TestTables_PassValidation()
        {
            var r = DataValidator.Validate(TestData.Tiers(), TestData.Nodes());
            Assert.IsEmpty(r.Errors, Joined(r.Errors));
            Assert.IsEmpty(r.Warnings, Joined(r.Warnings));
        }

        [Test]
        public void Detects_TierGap_AndNonIncreasingMass()
        {
            var c = TestData.Tiers();
            c.tiers[2].tier = 7;
            c.tiers[3].requiredMass = c.tiers[2].requiredMass;
            var r = DataValidator.Validate(c, TestData.Nodes());
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("expected 3")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("is not greater than")), Joined(r.Errors));
        }

        [Test]
        public void Detects_DuplicateId_UnknownStat_MissingPrereq()
        {
            var t = TestData.Nodes();
            t.nodes.Add(TestData.Stat("a_spawn", 1, 100, StatIds.SpawnRate, 1, 1, 1, 1));
            t.nodes.Add(TestData.Stat("mystery", 1, 110, "luck", 1, 1, 1, 1));
            t.nodes.Add(TestData.Stat("orphan", 2, 120, StatIds.SpawnRate, 1, 1, 1, 1, "nowhere"));
            var r = DataValidator.Validate(TestData.Tiers(), t);
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("duplicate id 'a_spawn'")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("unknown statId 'luck'")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("prereq 'nowhere', which does not exist")), Joined(r.Errors));
        }

        [Test]
        public void Detects_Cycle_AndUnreachable()
        {
            var t = TestData.Nodes();
            t.nodes.Add(TestData.Stat("x", 2, 100, StatIds.SpawnRate, 1, 1, 1, 1, "y"));
            t.nodes.Add(TestData.Stat("y", 2, 120, StatIds.SpawnRate, 1, 1, 1, 1, "x"));
            var r = DataValidator.Validate(TestData.Tiers(), t);
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("prereq cycle")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("'x' cannot be reached")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("'y' cannot be reached")), Joined(r.Errors));
        }

        [Test]
        public void Detects_GateGap_AndDuplicateGate()
        {
            var t = TestData.Nodes();
            t.nodes.RemoveAll(n => n.id == "gate_t3");
            t.nodes.Add(TestData.Gate("gate_t2b", 2, 1, 10));
            var r = DataValidator.Validate(TestData.Tiers(), t);
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("tier 3 has no gate")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("more than one gate unlocks tier 2")), Joined(r.Errors));
        }

        [Test]
        public void Detects_AngleOverlapInOneRing()
        {
            var t = TestData.Nodes();
            t.Get("a_pull").angleDeg = 32; // a_spawn is at 30
            t.Get("b_spawn").angleDeg = 330; // ring 2, no overlap with ring 1's a_pull (330 before the change)
            var r = DataValidator.Validate(TestData.Tiers(), t);
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("'a_spawn' and 'a_pull' overlap on ring 1")), Joined(r.Errors));
            Assert.IsFalse(r.Errors.Exists(e => e.Contains("b_spawn")), Joined(r.Errors));
        }

        [Test]
        public void Warns_DisabledSolePrereq_AndUnlimitedOutsideLastRing()
        {
            var t = TestData.Nodes();
            t.Get("b_spawn").enabled = false;   // the only prereq of c_threshold
            t.Get("a_pull").maxLevel = 0;
            var r = DataValidator.Validate(TestData.Tiers(), t);
            Assert.IsEmpty(r.Errors, Joined(r.Errors));
            Assert.IsTrue(r.Warnings.Exists(w => w.Contains("'c_threshold' is only reachable through disabled nodes")), Joined(r.Warnings));
            Assert.IsFalse(r.Warnings.Exists(w => w.Contains("'c_drain'")), "c_drain comes through b_mass");
            Assert.IsTrue(r.Warnings.Exists(w => w.Contains("'a_pull' has unlimited levels")), Joined(r.Warnings));
        }

        [Test]
        public void Detects_MissingTierString()
        {
            var t = TestData.Nodes();
            t.nodes.Add(TestData.Gate("gate_t12", 12, 11, 1, "gate_t4"));
            var r = DataValidator.Validate(TestData.Tiers(12), t);
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("'tier.12'")), Joined(r.Errors));
        }

        // ---------------- run_log.csv ----------------

        [Test]
        public void RunLog_HasTierAndStatColumns_AndRotatesOnHeaderChange()
        {
            string dir = Path.Combine(Path.GetTempPath(), "IncrementalTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var c = TestData.Tiers();
                string header = RunLogger.Header(c);
                Assert.AreEqual("run,duration_s,income,t1,t2,t3,t4,f1,f2,f3,f4,frag_income,ratio_vs_last,max_tier,node_levels," +
                                "spawn_rate,pull_accel,sale_mult,run_time,threshold_mult,dust_cap,dust_mass,gravity_radius," +
                                "follow_speed,world_scale,dust_initial,dust_mass_avg,end_reason", header);

                var stats = Stats.Compute(TestData.Params(), TestData.Nodes(), TestData.Meta(("a_spawn", 2)));
                var rec = new RunRecord
                {
                    run = 2, durationSec = 50, income = 13, tierCounts = new[] { 12, 1 }, ratioVsLast = 1.25, unlockedMaxTier = 2,
                    startLevels = new List<UpgradeLevel> { new UpgradeLevel("a_spawn", 2), new UpgradeLevel("gate_t2", 1) },
                    startStats = stats, endReason = EndReason.TimeUp,
                };
                Assert.IsNull(RunLogger.Append(dir, rec, c));
                var lines = File.ReadAllLines(RunLogger.FilePath(dir));
                Assert.AreEqual(2, lines.Length);
                Assert.AreEqual(header.Split(',').Length, lines[1].Split(',').Length);
                Assert.AreEqual("2,50.0,13,12,1,0,0,0,0,0,0,0,1.250,2,3,6,600,1,60,1,300,1,180,220,1,100,1,time", lines[1]);

                string rotated = RunLogger.Append(dir, rec, TestData.Tiers(5));
                Assert.IsNotNull(rotated);
                Assert.IsTrue(File.Exists(rotated));
                Assert.AreEqual(2, File.ReadAllLines(RunLogger.FilePath(dir)).Length);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
