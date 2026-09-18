using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Incremental.Tests
{
    /// <summary>
    /// save.json / settings.json through SaveStore, in a fresh temp directory per test (the real save is never touched),
    /// plus migration and pending-run handling.
    /// </summary>
    public class SaveTests
    {
        string dir;
        SaveStore store;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "IncrementalTests_" + Guid.NewGuid().ToString("N"));
            store = new SaveStore(dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        static MetaState FullMeta()
        {
            var m = TestData.Meta(("a_spawn", 3), ("b_mass", 2), ("gate_t2", 1), ("retired_upgrade", 4));
            m.currency = 12345.75;
            m.unlockedMaxTier = 5;
            m.runCount = 2;
            m.lastRunIncome = 300;
            m.bestRunIncome = 300;
            m.totalIncome = 420;
            m.totalPlayTimeSec = 131.5;
            m.totalPlanets = new[] { 40, 7, 2, 0, 1 };
            m.highestTierCreated = 5;
            m.runHistory = new List<RunRecord>
            {
                new RunRecord { run = 1, durationSec = 50.1, income = 120, tierCounts = new[] { 12, 0, 0, 0, 0 }, ratioVsLast = -1,
                    startLevels = new List<UpgradeLevel>(), unlockedMaxTier = 1, endReason = EndReason.Stamina,
                    startStats = new EffectiveStats { spawnRate = 4, saleMult = 1, dustMass = 1.5 } },
                new RunRecord { run = 2, durationSec = 81.4, income = 300, tierCounts = new[] { 28, 7, 2, 0, 1 }, ratioVsLast = 2.5,
                    startLevels = new List<UpgradeLevel> { new UpgradeLevel("a_spawn", 3) }, unlockedMaxTier = 5, endReason = EndReason.Quit },
            };
            return m;
        }

        static void WriteRaw(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }

        // ---------------- round trip ----------------

        [Test]
        public void RoundTrip_MetaStateIsIdentical()
        {
            var meta = FullMeta();
            store.Write(new SaveData { meta = meta });
            var loaded = store.Load(out var status);
            Assert.AreEqual(LoadStatus.Loaded, status);
            Assert.AreEqual(JsonUtility.ToJson(meta), JsonUtility.ToJson(loaded.meta));
            Assert.AreEqual(SaveMigration.CurrentVersion, loaded.version);
            Assert.IsFalse(loaded.pending.active);
        }

        [Test]
        public void RoundTrip_PendingRun()
        {
            var pending = Session.MakePending(3, 12.5, 40, new[] { 5, 1 }, new List<UpgradeLevel> { new UpgradeLevel("a_pull", 2) },
                new EffectiveStats { spawnRate = 5, pullAccel = 840 });
            store.Write(new SaveData { meta = FullMeta(), pending = pending });
            var loaded = store.Load(out _);
            Assert.AreEqual(JsonUtility.ToJson(pending), JsonUtility.ToJson(loaded.pending));
        }

        [Test]
        public void Save_HasNoNaN()
        {
            var meta = FullMeta();
            meta.runHistory[0].ratioVsLast = -1;
            store.Write(new SaveData { meta = meta });
            StringAssert.DoesNotContain("NaN", File.ReadAllText(store.SavePath));
        }

        [Test]
        public void NoFiles_IsNoSave()
        {
            Assert.IsNull(store.Load(out var status));
            Assert.AreEqual(LoadStatus.NoSave, status);
            Assert.IsFalse(store.HasSave);
        }

        [Test]
        public void Write_KeepsPreviousFileAsBak_AndLeavesNoTmp()
        {
            store.Write(new SaveData { meta = new MetaState { currency = 1 } });
            Assert.IsFalse(File.Exists(store.BakPath));
            store.Write(new SaveData { meta = new MetaState { currency = 2 } });
            Assert.IsTrue(File.Exists(store.BakPath));
            Assert.IsFalse(File.Exists(store.TmpPath));
            var bak = JsonUtility.FromJson<SaveData>(File.ReadAllText(store.BakPath));
            Assert.AreEqual(1, bak.meta.currency);
            Assert.AreEqual(2, store.Load(out _).meta.currency);
        }

        // ---------------- unknown and missing ids ----------------

        [Test]
        public void UnknownId_IsKeptAndIgnored()
        {
            store.Write(new SaveData { meta = FullMeta() });
            var meta = store.Load(out _).meta;
            Assert.AreEqual(4, meta.GetLevel("retired_upgrade"), "unknown id must survive a load");

            var p = TestData.Params();
            var t = TestData.Nodes();
            var withUnknown = Stats.Compute(p, t, meta);
            meta.upgradeLevels.RemoveAll(l => l.id == "retired_upgrade");
            var without = Stats.Compute(p, t, meta);
            Assert.AreEqual(JsonUtility.ToJson(without), JsonUtility.ToJson(withUnknown));

            meta.SetLevel("retired_upgrade", 4);
            store.Write(new SaveData { meta = meta });
            StringAssert.Contains("retired_upgrade", File.ReadAllText(store.SavePath), "unknown id must survive a save");
        }

        [Test]
        public void MissingId_StartsAtLevelZero()
        {
            // A save written before b_mass existed.
            WriteRaw(store.SavePath, "{\"version\":2,\"meta\":{\"currency\":50.0,\"upgradeLevels\":[{\"id\":\"a_spawn\",\"level\":2}],\"unlockedMaxTier\":1,\"runCount\":4}}");
            var loaded = store.Load(out var status);
            Assert.AreEqual(LoadStatus.Loaded, status);
            Assert.AreEqual(2, loaded.meta.GetLevel("a_spawn"));
            Assert.AreEqual(0, loaded.meta.GetLevel("b_mass"));
            Assert.AreEqual(1, Stats.Compute(TestData.Params(), TestData.Nodes(), loaded.meta).dustMass);
            Assert.IsNotNull(loaded.meta.runHistory);
            Assert.IsNotNull(loaded.meta.totalPlanets);
            Assert.IsFalse(loaded.pending.active);
        }

        // ---------------- broken files ----------------

        [Test]
        public void BrokenJson_RecoversFromBak_AndKeepsCorruptFile()
        {
            store.Write(new SaveData { meta = new MetaState { currency = 1 } });
            store.Write(new SaveData { meta = new MetaState { currency = 2 } });
            string json = File.ReadAllText(store.SavePath);
            WriteRaw(store.SavePath, json.Substring(0, json.Length / 2));

            LogAssert.Expect(LogType.Warning, new Regex(@"save\.json could not be read"));
            LogAssert.Expect(LogType.Warning, new Regex(@"restored from save\.bak"));
            var loaded = store.Load(out var status);

            Assert.AreEqual(LoadStatus.RestoredFromBackup, status);
            Assert.AreEqual(1, loaded.meta.currency);
            Assert.AreEqual(1, Directory.GetFiles(dir, "save.corrupt_*.json").Length);
            Assert.IsFalse(File.Exists(store.SavePath), "the broken file is moved aside");
        }

        [Test]
        public void MissingMain_WithBak_Recovers()
        {
            // Killed between "save.json → save.bak" and "save.tmp → save.json".
            store.Write(new SaveData { meta = new MetaState { currency = 7 } });
            File.Move(store.SavePath, store.BakPath);
            LogAssert.Expect(LogType.Warning, new Regex(@"restored from save\.bak"));
            var loaded = store.Load(out var status);
            Assert.AreEqual(LoadStatus.RestoredFromBackup, status);
            Assert.AreEqual(7, loaded.meta.currency);
        }

        [Test]
        public void BothBroken_StartsFresh_AndKeepsBothFiles()
        {
            Directory.CreateDirectory(dir);
            WriteRaw(store.SavePath, "{\"version\":1,\"meta\":{\"currency\":");
            WriteRaw(store.BakPath, "garbage");
            LogAssert.Expect(LogType.Warning, new Regex(@"save\.json could not be read"));
            LogAssert.Expect(LogType.Warning, new Regex(@"save\.bak could not be read"));
            LogAssert.Expect(LogType.Warning, new Regex(@"starting a new game"));
            Assert.IsNull(store.Load(out var status));
            Assert.AreEqual(LoadStatus.Corrupt, status);
            Assert.AreEqual(2, Directory.GetFiles(dir, "save.corrupt_*.json").Length);
        }

        [TestCase("")]
        [TestCase("{}")]
        [TestCase("[1,2,3]")]
        public void NonSaveContent_IsTreatedAsBroken(string content)
        {
            Directory.CreateDirectory(dir);
            WriteRaw(store.SavePath, content);
            LogAssert.Expect(LogType.Warning, new Regex(@"save\.json could not be read"));
            LogAssert.Expect(LogType.Warning, new Regex(@"starting a new game"));
            Assert.IsNull(store.Load(out var status));
            Assert.AreEqual(LoadStatus.Corrupt, status);
        }

        // ---------------- pending run ----------------

        [Test]
        public void PendingRun_IsClosedAsCrash_WithIncomeKept()
        {
            var meta = new MetaState { currency = 5, runCount = 3, lastRunIncome = 10, bestRunIncome = 15, totalIncome = 25 };
            var levels = new List<UpgradeLevel> { new UpgradeLevel("a_spawn", 1) };
            var stats = new EffectiveStats { spawnRate = 5 };
            store.Write(new SaveData { meta = meta, pending = Session.MakePending(3, 42.5, 20, new[] { 10, 2 }, levels, stats) });

            var data = store.Load(out _);
            var rec = Session.FinalizePending(data, EndReason.Crash);

            Assert.IsNotNull(rec);
            Assert.AreEqual(EndReason.Crash, rec.endReason);
            Assert.AreEqual(3, rec.run);
            Assert.AreEqual(42.5, rec.durationSec);
            Assert.IsFalse(rec.HasRatio, "crash runs get no ratio (12 §10-3)");
            Assert.AreEqual(1, MetaState.LevelIn(rec.startLevels, "a_spawn"));
            Assert.AreEqual(5, rec.startStats.spawnRate);
            Assert.AreEqual(25, data.meta.currency);
            Assert.AreEqual(10, data.meta.lastRunIncome, "the ratio base stays the last full run");
            Assert.AreEqual(15, data.meta.bestRunIncome, "crash runs do not set the best");
            Assert.AreEqual(45, data.meta.totalIncome);
            Assert.AreEqual(42.5, data.meta.totalPlayTimeSec);
            CollectionAssert.AreEqual(new[] { 10, 2 }, data.meta.totalPlanets);
            Assert.AreEqual(2, data.meta.highestTierCreated);
            Assert.AreSame(rec, data.meta.LastRun);
            Assert.IsFalse(data.pending.active);
            Assert.IsNull(Session.FinalizePending(data, EndReason.Crash), "a closed pending run is not closed twice");

            store.Write(data);
            Assert.IsFalse(store.Load(out _).pending.active);
        }

        [Test]
        public void NoPending_FinalizeDoesNothing()
        {
            var data = new SaveData { meta = new MetaState { currency = 5 } };
            Assert.IsNull(Session.FinalizePending(data, EndReason.Crash));
            Assert.AreEqual(5, data.meta.currency);
            Assert.AreEqual(0, data.meta.runHistory.Count);
        }

        [Test]
        public void FirstRun_HasNoRatio()
        {
            var rec = Session.MakeRecord(new MetaState(), 1, 50, 12, new int[1], null, default, EndReason.Stamina);
            Assert.IsFalse(rec.HasRatio);
            Assert.AreEqual(-1, rec.ratioVsLast);
        }

        [Test]
        public void Ratio_ComparesWithLastFullRun_QuitAndCrashExcluded()
        {
            var meta = new MetaState();
            Run(meta, 1, 10, EndReason.Stamina);
            var quit = Run(meta, 2, 2, EndReason.Quit);
            Assert.IsFalse(quit.HasRatio, "quit runs get no ratio");
            Assert.AreEqual(10, meta.lastRunIncome, "quit does not move the base");
            var crash = Run(meta, 3, 50, EndReason.Crash);
            Assert.IsFalse(crash.HasRatio);
            Assert.AreEqual(10, meta.bestRunIncome, "crash does not set the best");
            var full = Run(meta, 4, 15, EndReason.Stamina);
            Assert.AreEqual(1.5, full.ratioVsLast, 1e-12, "compared with run 1, not the quit run");
            Assert.AreEqual(15, meta.bestRunIncome);
            Assert.AreEqual(10 + 2 + 50 + 15, meta.currency, "every run pays");
            Assert.AreEqual(4, meta.runHistory.Count, "every run is recorded");
        }

        static RunRecord Run(MetaState meta, int run, double income, string reason)
        {
            var rec = Session.MakeRecord(meta, run, 30, income, new int[1], null, default, reason);
            Session.ApplyRunEnd(meta, rec);
            return rec;
        }

        [Test]
        public void ApplyRunEnd_GrowsTotalPlanetsForNewTiers()
        {
            var meta = new MetaState { totalPlanets = new[] { 3 } };
            Session.ApplyRunEnd(meta, Session.MakeRecord(meta, 1, 10, 0, new[] { 1, 0, 4 }, null, default, EndReason.Quit));
            CollectionAssert.AreEqual(new[] { 4, 0, 4 }, meta.totalPlanets);
            Assert.AreEqual(3, meta.highestTierCreated);
        }

        // ---------------- migration, delete, settings ----------------

        [Test]
        public void V1Save_IsReplacedByNewGame_AndKeptAsCopy()
        {
            WriteRaw(store.SavePath, "{\"version\":1,\"meta\":{\"currency\":500.0,\"upgradeLevels\":[{\"id\":\"spawn_rate\",\"level\":4}]," +
                                     "\"unlockedMaxTier\":3,\"runCount\":9,\"totalIncome\":900.0}}");
            store.WriteSettings(new Settings { notation = Notation.Scientific });
            LogAssert.Expect(LogType.Warning, new Regex(@"old save kept as save\.v1_"));
            LogAssert.Expect(LogType.Warning, new Regex(@"version 1 save .* replaced by a new game"));

            var loaded = store.Load(out var status);

            Assert.AreEqual(LoadStatus.Loaded, status);
            Assert.AreEqual(2, loaded.version);
            Assert.AreEqual(0, loaded.meta.currency);
            Assert.AreEqual(0, loaded.meta.runCount);
            Assert.AreEqual(0, loaded.meta.totalIncome, "statistics reset too");
            Assert.AreEqual(1, loaded.meta.unlockedMaxTier);
            Assert.IsEmpty(loaded.meta.upgradeLevels);
            Assert.AreEqual(1, Directory.GetFiles(dir, "save.v1_*.json").Length);
            Assert.AreEqual(Notation.Scientific, store.LoadSettings().notation, "settings stay");
        }

        [Test]
        public void V2Save_IsKept()
        {
            store.Write(new SaveData { meta = FullMeta() });
            var loaded = store.Load(out _);
            Assert.AreEqual(12345.75, loaded.meta.currency);
            Assert.AreEqual(0, Directory.GetFiles(dir, "save.v*_*.json").Length);
        }

        [Test]
        public void Migrate_FillsMissingPartsAndClamps()
        {
            var d = new SaveData { version = 2, meta = new MetaState { upgradeLevels = null, runHistory = null, totalPlanets = null, unlockedMaxTier = 0 }, pending = null };
            SaveMigration.Migrate(d);
            Assert.AreEqual(SaveMigration.CurrentVersion, d.version);
            Assert.IsNotNull(d.meta.upgradeLevels);
            Assert.IsNotNull(d.meta.runHistory);
            Assert.IsNotNull(d.meta.totalPlanets);
            Assert.IsNotNull(d.pending);
            Assert.AreEqual(1, d.meta.unlockedMaxTier);
        }

        [Test]
        public void DeleteSave_KeepsSettings()
        {
            store.WriteSettings(new Settings { notation = Notation.Scientific, pauseOnFocusLoss = false });
            store.Write(new SaveData());
            store.Write(new SaveData());
            store.DeleteSave();
            Assert.IsFalse(store.HasSave);
            var s = store.LoadSettings();
            Assert.AreEqual(Notation.Scientific, s.notation);
            Assert.IsFalse(s.pauseOnFocusLoss);
        }

        [Test]
        public void Settings_DefaultWhenMissing()
        {
            var s = store.LoadSettings();
            Assert.AreEqual(Notation.Letters, s.notation);
            Assert.IsTrue(s.pauseOnFocusLoss);
        }
    }
}
