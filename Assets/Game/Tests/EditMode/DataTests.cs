using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace Incremental.Tests
{
    /// <summary>Data validation (project assets and broken tables) and the table-driven run_log.csv.</summary>
    public class DataTests
    {
        [Test]
        public void ProjectAssets_PassValidation()
        {
            var c = AssetDatabase.LoadAssetAtPath<CelestialTable>("Assets/Game/Data/CelestialTable.asset");
            var u = AssetDatabase.LoadAssetAtPath<UpgradeTable>("Assets/Game/Data/UpgradeTable.asset");
            Assert.IsNotNull(c);
            Assert.IsNotNull(u);
            var errors = DataValidator.Validate(c, u);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(11, c.TierCount);
            Assert.AreEqual(UpgradeIds.All.Length, u.upgrades.Count);
        }

        [Test]
        public void TestTables_PassValidation() =>
            Assert.IsEmpty(DataValidator.Validate(TestData.Tiers(), TestData.Upgrades()));

        [Test]
        public void Detects_TierGap_AndNonIncreasingMass()
        {
            var c = TestData.Tiers();
            c.tiers[3].tier = 7;
            c.tiers[5].requiredMass = c.tiers[4].requiredMass;
            var errors = DataValidator.Validate(c, TestData.Upgrades());
            Assert.IsTrue(errors.Exists(e => e.Contains("expected 4")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("is not greater than")), string.Join("\n", errors));
        }

        [Test]
        public void Detects_DuplicateAndUnknownIds()
        {
            var u = TestData.Upgrades();
            u.upgrades.Add(TestData.Def(UpgradeIds.SpawnRate, 1, 1, 1.1, 0));
            u.upgrades.Add(TestData.Def("mystery", 1, 1, 1.1, 0));
            var errors = DataValidator.Validate(TestData.Tiers(), u);
            Assert.IsTrue(errors.Exists(e => e.Contains("duplicate id 'spawn_rate'")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("'mystery' has no effect")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("upg.mystery.name")), string.Join("\n", errors));
        }

        [Test]
        public void Detects_UnlockForMissingTier_AndTierWithoutUnlock()
        {
            var u = TestData.Upgrades();
            u.unlocks.RemoveAll(x => x.tier == 6);
            u.unlocks.Add(new UnlockDef { tier = 12, cost = 1 });
            var errors = DataValidator.Validate(TestData.Tiers(), u);
            Assert.IsTrue(errors.Exists(e => e.Contains("tier 12, which does not exist")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("tier 6 has no unlock")), string.Join("\n", errors));
        }

        [Test]
        public void Detects_MissingTierString()
        {
            var errors = DataValidator.Validate(TestData.Tiers(12), TestData.Upgrades());
            Assert.IsTrue(errors.Exists(e => e.Contains("'tier.12'")), string.Join("\n", errors));
        }

        // ---------------- run_log.csv ----------------

        [Test]
        public void RunLog_ColumnsComeFromTables_AndRotateOnHeaderChange()
        {
            string dir = Path.Combine(Path.GetTempPath(), "IncrementalTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var c = TestData.Tiers();
                var u = TestData.Upgrades();
                string header = RunLogger.Header(c, u);
                StringAssert.StartsWith("run,duration_s,income,t1,t2,", header);
                StringAssert.Contains(",t11,ratio_vs_last,lvl_spawn_rate,", header);
                StringAssert.EndsWith(",lvl_start_bonus,end_reason", header);

                var rec = new RunRecord
                {
                    run = 2, durationSec = 50, income = 12.5, tierCounts = new[] { 12, 1 }, ratioVsLast = 1.25,
                    startLevels = new List<UpgradeLevel> { new UpgradeLevel(UpgradeIds.DustMass, 3) }, endReason = EndReason.Quit,
                };
                Assert.IsNull(RunLogger.Append(dir, rec, c, u));
                var lines = File.ReadAllLines(RunLogger.FilePath(dir));
                Assert.AreEqual(2, lines.Length);
                Assert.AreEqual(header.Split(',').Length, lines[1].Split(',').Length);
                StringAssert.StartsWith("2,50.0,12.5,12,1,0,", lines[1]);
                StringAssert.EndsWith(",quit", lines[1]);

                // A table change (new tier) changes the header: the old file is moved aside.
                string rotated = RunLogger.Append(dir, rec, TestData.Tiers(12), u);
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
