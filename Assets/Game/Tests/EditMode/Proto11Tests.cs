using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Incremental.Tests
{
    /// <summary>
    /// 28 §2, time-based runs (11th pass): the clock ends the run at 0 whatever the player does, the run_time nodes add
    /// seconds (the old stamina node ids included), and a v3 save comes through the migration with its levels and its
    /// stamina-ended runs counted as full runs.
    /// </summary>
    public class Proto11Tests
    {
        const double Eps = 1e-9;

        static NodeTable ProjectNodes() => AssetDatabase.LoadAssetAtPath<NodeTable>("Assets/Game/Data/NodeTable.asset");
        static GameParams ProjectParams() => AssetDatabase.LoadAssetAtPath<GameParams>("Assets/Game/Data/GameParams.asset");

        // ---------------- the clock ----------------

        [Test]
        public void Clock_EndsTheRunExactlyAtZero()
        {
            var run = new RunState(4) { timeLeft = 1.0 };
            Assert.IsFalse(Session.TickClock(run, 0.4), "0.6 s left");
            Assert.AreEqual(0.6, run.timeLeft, Eps);
            Assert.IsFalse(Session.TickClock(run, 0.5), "0.1 s left");
            Assert.IsTrue(Session.TickClock(run, 0.5), "past 0: the run ends");
            Assert.AreEqual(0.0, run.timeLeft, Eps, "it stops at 0, never below");
        }

        [Test]
        public void Clock_RunsDownTheSameHeldOrNot()
        {
            // The hold has no cost any more (28 §2): the run is a fixed length, 30 s with nothing bought.
            var p = TestData.Params();
            p.runTimeSec = 30;
            var s = Stats.Compute(p, TestData.Nodes(), new MetaState());
            var run = new RunState(4) { timeLeft = s.runTimeSec };
            int ticks = 0;
            const double dt = 0.02;
            while (!Session.TickClock(run, dt))
            {
                run.holding = ticks % 2 == 0;
                ticks++;
            }
            Assert.AreEqual(30.0 / dt, ticks + 1, 1.0, "30 s of fixed ticks, holding every other one");
        }

        [Test]
        public void TimeUp_IsAFullRunThatSetsTheBest()
        {
            var m = new MetaState();
            Session.ApplyRunEnd(m, Session.MakeRecord(m, 1, 30, 40, new[] { 2 }, null, default, EndReason.TimeUp));
            Assert.AreEqual(40, m.lastRunIncome, Eps);
            Assert.AreEqual(40, m.bestRunIncome, Eps);
            Assert.IsTrue(Session.IsFullRun(EndReason.TimeUp));
        }

        // ---------------- run_time nodes ----------------

        [Test]
        public void ProjectTree_TheOldStaminaNodesAllAddRunTime()
        {
            var t = ProjectNodes();
            foreach (var id in new[] { "r1_stamina", "r2_stamina", "r3_drain", "r4_stamina", "r5_drain", "r6_stamina",
                         "r7_drain", "r8_stamina", "r9_drain", "r10_stamina", "r11_stamina" })
            {
                var n = t.Get(id);
                Assert.IsNotNull(n, id + " keeps its id (save key)");
                Assert.AreEqual(StatIds.RunTime, n.statId, id);
                Assert.Greater(n.effectPerLevel, 0, id);
            }
        }

        [Test]
        public void ProjectTree_RunTimeGoesFrom30To120ByRing10_EvenlyPerRing()
        {
            // 28 §2 / §3 (provisional): 30 s at ring 1, +90 s once every run_time node of rings 1-10 is bought, the same
            // number of seconds in every ring.
            var p = ProjectParams();
            var t = ProjectNodes();
            Assert.AreEqual(30, p.runTimeSec, Eps);
            var perRing = new double[12];
            foreach (var n in t.nodes)
                if (n != null && n.enabled && n.statId == StatIds.RunTime && n.maxLevel > 0)
                    perRing[n.minTier] += n.effectPerLevel * n.maxLevel;
            double total = 0;
            for (int ring = 1; ring <= 10; ring++)
            {
                Assert.AreEqual(perRing[1], perRing[ring], Eps, "ring " + ring + " adds as much as ring 1");
                total += perRing[ring];
            }
            Assert.AreEqual(90, total, Eps, "rings 1-10 add 90 s");
            var m = new MetaState();
            foreach (var n in t.nodes)
                if (n != null && n.enabled && n.maxLevel > 0 && n.minTier <= 10) m.SetLevel(n.id, n.maxLevel);
            Shop.RecomputeUnlockedMaxTier(t, m);
            Assert.AreEqual(120, Stats.Compute(p, t, m).runTimeSec, Eps, "a ring 11 run with nothing endless bought");
        }

        // ---------------- save v3 -> v4 ----------------

        [Test]
        public void SaveV3_MigratesToV4_KeepingLevelsAndRenamingStaminaRuns()
        {
            string json = "{\"version\":3,\"meta\":{\"currency\":500.0," +
                          "\"upgradeLevels\":[{\"id\":\"r1_stamina\",\"level\":1},{\"id\":\"r3_drain\",\"level\":2}]," +
                          "\"unlockedMaxTier\":3,\"runCount\":2,\"lastRunIncome\":70.0,\"bestRunIncome\":70.0," +
                          "\"runHistory\":[" +
                          "{\"run\":1,\"durationSec\":48.0,\"income\":70.0,\"endReason\":\"stamina\"," +
                          "\"startStats\":{\"staminaMax\":86.0,\"staminaHoldDrain\":1.5,\"staminaIdleDrain\":0.3}}," +
                          "{\"run\":2,\"durationSec\":3.0,\"income\":5.0,\"endReason\":\"quit\"}]}," +
                          "\"pending\":{\"active\":true,\"run\":3,\"elapsed\":12.0,\"income\":9.0}}";
            var d = SaveMigration.Migrate(JsonUtility.FromJson<SaveData>(json));
            Assert.AreEqual(4, d.version);
            Assert.AreEqual(SaveMigration.CurrentVersion, d.version);
            Assert.AreEqual(1, d.meta.GetLevel("r1_stamina"), "node levels stay (the ids are the same)");
            Assert.AreEqual(2, d.meta.GetLevel("r3_drain"));
            Assert.AreEqual(EndReason.TimeUp, d.meta.runHistory[0].endReason, "stamina -> time");
            Assert.AreEqual(EndReason.Quit, d.meta.runHistory[1].endReason, "other endings untouched");
            Assert.AreEqual(0, d.meta.runHistory[0].startStats.runTimeSec, Eps, "the old stamina numbers are just not read");

            // The pending run closes the usual way, under the new rules; the old stamina-ended run still sets the ratio base.
            var rec = Session.FinalizePending(d, EndReason.Crash);
            Assert.AreEqual(EndReason.Crash, rec.endReason);
            Assert.AreEqual(509, d.meta.currency, Eps);
            Assert.AreEqual(70, d.meta.lastRunIncome, Eps);
        }
    }
}
