using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Incremental.Tests
{
    /// <summary>
    /// 13th pass, skill tree v2 (29): the shape of the project tree, the v4 → v5 save refund, and three rule effects
    /// (T1 유예, C2 열 번째, G2 잔류 중력; T1 and G2 are out of the tree since the 14th pass, their code is not).
    /// </summary>
    public class TreeV2Tests
    {
        const double Eps = 1e-9;

        static NodeTable ProjectNodes() => AssetDatabase.LoadAssetAtPath<NodeTable>("Assets/Game/Data/NodeTable.asset");

        static readonly float[] Stems = { 90f, 18f, 306f, 234f, 162f };

        // ---------------- shape ----------------

        [Test]
        public void EveryNode_SitsWithin30DegreesOfItsCategoryStem()
        {
            foreach (var n in ProjectNodes().nodes)
            {
                float d = Mathf.Abs(Mathf.DeltaAngle(n.angleDeg, Stems[(int)n.category]));
                Assert.LessOrEqual(d, 30f + 1e-3f, n.id + " leaves its category's sector (29 §2)");
            }
        }

        [Test]
        public void Prereqs_NeverPointToALaterRing_OrAnotherCategory()
        {
            var t = ProjectNodes();
            foreach (var n in t.nodes)
                foreach (var id in n.prereqs)
                {
                    var p = t.Get(id);
                    Assert.IsNotNull(p, n.id + " -> " + id);
                    Assert.LessOrEqual(p.minTier, n.minTier, n.id + " needs a node of a later ring");
                    Assert.AreEqual(n.category, p.category, n.id + " branches off another category");
                }
        }

        [Test]
        public void AbilityUnlocks_StandOnTheStem_InRings2To10()
        {
            var t = ProjectNodes();
            var rings = new List<int>();
            foreach (var n in t.nodes)
                if (n.IsAbilityUnlock)
                {
                    rings.Add(n.minTier);
                    Assert.AreEqual(234f, n.angleDeg, 1e-3, n.id);
                }
            rings.Sort();
            CollectionAssert.AreEqual(new[] { 2, 4, 6, 8, 10 }, rings);
        }

        [Test]
        public void RuleNodes_HaveNamesAndOneEach()
        {
            var t = ProjectNodes();
            var seen = new HashSet<string>();
            foreach (var n in t.nodes)
            {
                if (!n.IsRule) continue;
                Assert.IsTrue(seen.Add(n.ruleId), n.ruleId + " twice");
                Assert.IsTrue(UIStrings.Has(UIStrings.RuleNameKey(n.ruleId)), n.ruleId);
                Assert.AreEqual(TreeIcon.Symbol.Rule, TreeIcon.SymbolFor(n));
            }
            // 29 §10.4 (14th pass): 13 rules. T1, T2, T5 and G1-G3 left the tree; their effect code stays (the tests below still cover T1 and G2).
            CollectionAssert.AreEquivalent(new[] { "T3", "D1", "D2", "D3", "D4", "D5", "C1", "C2", "C3", "C4", "A1", "A2", "A5" }, seen);
        }

        // ---------------- save v5 ----------------

        [Test]
        public void SaveV4_ResetsTheTree_AndRefundsEveryLevelAtV1Prices()
        {
            // The test table stands in for the v1 tree: a_spawn costs 10 x 1.5^l, gate_t2 costs 30.
            var v1 = TestData.Nodes();
            string json = "{\"version\":4,\"meta\":{\"currency\":100.0," +
                          "\"upgradeLevels\":[{\"id\":\"a_spawn\",\"level\":2},{\"id\":\"gate_t2\",\"level\":1},{\"id\":\"gone\",\"level\":3}]," +
                          "\"unlockedMaxTier\":2,\"runCount\":5}}";
            var d = SaveMigration.Migrate(JsonUtility.FromJson<SaveData>(json), v1);
            Assert.AreEqual(SaveMigration.CurrentVersion, d.version);
            Assert.AreEqual(0, d.meta.upgradeLevels.Count, "the tree starts empty (29 Q9)");
            Assert.AreEqual(1, d.meta.unlockedMaxTier, "back to ring 1");
            Assert.AreEqual(100 + 10 + 15 + 30, d.meta.currency, Eps, "a_spawn 10 + 15, the gate 30; an unknown id gives nothing");
            Assert.AreEqual(5, d.meta.runCount, "the rest of the save stays");
            Assert.IsTrue(SaveMigration.ResetsTree(4));
            Assert.IsFalse(SaveMigration.ResetsTree(5));
        }

        // ---------------- rules ----------------

        static (Rules rules, NodeTable t, MetaState m, GameParams p) WithRule(string ruleId, int level)
        {
            var p = TestData.Params();
            var t = TestData.Nodes();
            t.nodes.Add(new NodeDef { id = "rule_" + ruleId, kind = NodeKind.Rule, ruleId = ruleId, maxLevel = 3, baseCost = 1, growth = 1, minTier = 1 });
            t.Invalidate();
            var m = new MetaState();
            m.SetLevel("rule_" + ruleId, level);
            var rules = new Rules(p);
            rules.BeginRun(m, t);
            return (rules, t, m, p);
        }

        [Test]
        public void T1_HoldsTheClockForTheFirstSeconds_ByLevel()
        {
            var w = WithRule("T1", 2);
            Assert.AreEqual(w.p.rules.graceSecPerLevel * 2, w.rules.GraceSec, Eps);
            var run = new RunState(4) { timeLeft = 30 };
            double dt = 0.02;
            // The run loop: elapsed first, then the clock unless the rule holds it.
            for (int i = 0; i < 1000; i++)
            {
                run.elapsed += dt;
                if (!w.rules.InGrace(run)) Session.TickClock(run, dt);
            }
            Assert.AreEqual(30 - (20 - w.rules.GraceSec), run.timeLeft, 0.05, "20 s played, the first 4 s cost nothing");
            Assert.AreEqual(0, new Rules(w.p).GraceSec, Eps, "not owned: no grace");
        }

        [Test]
        public void C2_EveryNthCompletionIsADoubleBundle()
        {
            var w = WithRule("C2", 3);
            int n = w.p.rules.everyNth[2];
            var run = new RunState(4) { timeLeft = 30 };
            int doubled = 0;
            for (int i = 1; i <= n * 2; i++)
            {
                double count = 1;
                w.rules.OnComplete(run, 1, ref count, out bool nth);
                Assert.AreEqual(i % n == 0, nth, "completion " + i);
                Assert.AreEqual(nth ? w.p.rules.nthMult : 1, count, Eps);
                if (nth) doubled++;
            }
            Assert.AreEqual(2, doubled);
        }

        [Test]
        public void G2_KeepsPullingForAMomentAfterTheRelease()
        {
            var w = WithRule("G2", 1);
            double dt = 0.02, linger = w.p.rules.lingerSec;
            Assert.IsTrue(w.rules.DustHolding(dt, false, true), "holding pulls");
            Assert.IsTrue(w.rules.DustHolding(dt, true, false), "the release tick still pulls");
            int ticks = 1;
            while (w.rules.DustHolding(dt, false, false)) ticks++;
            Assert.AreEqual(linger, ticks * dt, dt * 1.5, "for lingerSec after the release");
            var none = new Rules(w.p);
            Assert.IsFalse(none.DustHolding(dt, true, false), "without the rule a release lets go at once");
        }
    }
}
