using NUnit.Framework;

namespace Incremental.Tests
{
    /// <summary>Stats.Compute: one test per upgrade id, plus level 0, disabled and missing entries.</summary>
    public class StatsTests
    {
        const double Eps = 1e-9;
        GameParams p;
        UpgradeTable t;

        [SetUp]
        public void SetUp()
        {
            p = TestData.Params();
            t = TestData.Upgrades();
        }

        EffectiveStats With(string id, int level) => Stats.Compute(p, t, TestData.Meta((id, level)));

        [Test]
        public void LevelZero_IsBaseParams()
        {
            var s = Stats.Compute(p, t, new MetaState());
            Assert.AreEqual(4, s.spawnRate, Eps);
            Assert.AreEqual(600, s.pullAccel, Eps);
            Assert.AreEqual(1, s.saleMult, Eps);
            Assert.AreEqual(60, s.staminaMax, Eps);
            Assert.AreEqual(1, s.thresholdMult, Eps);
            Assert.AreEqual(300, s.dustCap, Eps);
            Assert.AreEqual(1, s.dustMass, Eps);
            Assert.AreEqual(180, s.gravityRadius, Eps);
            Assert.AreEqual(0.2, s.staminaIdleDrain, Eps);
            Assert.AreEqual(1.0, s.staminaHoldDrain, Eps);
            Assert.AreEqual(100, s.dustInitial, Eps);
        }

        [Test] public void SpawnRate_AddsPerLevel() => Assert.AreEqual(4 + 3, With(UpgradeIds.SpawnRate, 3).spawnRate, Eps);

        [Test] public void PullAccel_AddsPercentPerLevel() => Assert.AreEqual(600 * 1.4, With(UpgradeIds.PullAccel, 2).pullAccel, Eps);

        [Test] public void SaleMult_AddsPercentPerLevel() => Assert.AreEqual(1.5, With(UpgradeIds.SaleMult, 2).saleMult, Eps);

        [Test] public void StaminaMax_AddsPerLevel() => Assert.AreEqual(60 + 30, With(UpgradeIds.StaminaMax, 3).staminaMax, Eps);

        [Test] public void ThresholdMult_SubtractsPercentPerLevel() => Assert.AreEqual(0.8, With(UpgradeIds.ThresholdMult, 5).thresholdMult, Eps);

        [Test] public void DustCap_AddsPerLevel() => Assert.AreEqual(300 + 400, With(UpgradeIds.DustCap, 4).dustCap, Eps);

        [Test]
        public void DustMass_Compounds()
        {
            Assert.AreEqual(1.5 * 1.5 * 1.5, With(UpgradeIds.DustMass, 3).dustMass, Eps);
            Assert.AreEqual(System.Math.Pow(1.5, 20), With(UpgradeIds.DustMass, 20).dustMass, 1e-6);
        }

        [Test] public void GravityRadius_AddsPercentPerLevel() => Assert.AreEqual(180 * 1.5, With(UpgradeIds.GravityRadius, 5).gravityRadius, Eps);

        [Test]
        public void StaminaDrain_ReducesIdleAndHoldDrain()
        {
            var s = With(UpgradeIds.StaminaDrain, 4);
            Assert.AreEqual(0.2 * 0.8, s.staminaIdleDrain, Eps);
            Assert.AreEqual(1.0 * 0.8, s.staminaHoldDrain, Eps);
        }

        [Test]
        public void StaminaDrain_NeverBelowZero()
        {
            t.Get(UpgradeIds.StaminaDrain).maxLevel = 0;
            var s = With(UpgradeIds.StaminaDrain, 30); // -150%
            Assert.AreEqual(0, s.staminaIdleDrain, Eps);
            Assert.AreEqual(0, s.staminaHoldDrain, Eps);
        }

        [Test] public void StartBonus_AddsInitialDust() => Assert.AreEqual(100 + 100, With(UpgradeIds.StartBonus, 2).dustInitial, Eps);

        [Test]
        public void StartBonus_CappedByDustCap()
        {
            Assert.AreEqual(300, With(UpgradeIds.StartBonus, 10).dustInitial, Eps);
            var s = Stats.Compute(p, t, TestData.Meta((UpgradeIds.StartBonus, 10), (UpgradeIds.DustCap, 1)));
            Assert.AreEqual(400, s.dustInitial, Eps);
        }

        [Test]
        public void EachUpgrade_ChangesOnlyItsOwnValues()
        {
            var baseStats = Stats.Compute(p, t, new MetaState());
            foreach (var id in UpgradeIds.All)
            {
                var s = With(id, 1);
                int changed = 0;
                if (s.spawnRate != baseStats.spawnRate) changed++;
                if (s.pullAccel != baseStats.pullAccel) changed++;
                if (s.saleMult != baseStats.saleMult) changed++;
                if (s.staminaMax != baseStats.staminaMax) changed++;
                if (s.thresholdMult != baseStats.thresholdMult) changed++;
                if (s.dustCap != baseStats.dustCap) changed++;
                if (s.dustMass != baseStats.dustMass) changed++;
                if (s.gravityRadius != baseStats.gravityRadius) changed++;
                if (s.staminaIdleDrain != baseStats.staminaIdleDrain || s.staminaHoldDrain != baseStats.staminaHoldDrain) changed++;
                if (s.dustInitial != baseStats.dustInitial) changed++;
                Assert.AreEqual(1, changed, id + " should change exactly one effective value");
            }
        }

        [Test]
        public void DisabledUpgrade_HasNoEffect()
        {
            t.Get(UpgradeIds.SpawnRate).enabled = false;
            t.Get(UpgradeIds.DustMass).enabled = false;
            var s = Stats.Compute(p, t, TestData.Meta((UpgradeIds.SpawnRate, 5), (UpgradeIds.DustMass, 5)));
            Assert.AreEqual(4, s.spawnRate, Eps);
            Assert.AreEqual(1, s.dustMass, Eps);
        }

        [Test]
        public void UnknownId_IsIgnored()
        {
            var baseStats = Stats.Compute(p, t, new MetaState());
            var s = Stats.Compute(p, t, TestData.Meta(("removed_upgrade", 7)));
            Assert.AreEqual(baseStats.spawnRate, s.spawnRate, Eps);
            Assert.AreEqual(baseStats.dustMass, s.dustMass, Eps);
        }

        [Test]
        public void MissingTableEntry_HasNoEffect()
        {
            t.upgrades.RemoveAll(d => d.id == UpgradeIds.GravityRadius);
            Assert.AreEqual(180, With(UpgradeIds.GravityRadius, 5).gravityRadius, Eps);
        }

        [Test]
        public void Threshold_UsesThresholdMult()
        {
            var tier = new CelestialTier { tier = 1, requiredMass = 10 };
            Assert.AreEqual(8, Stats.Threshold(tier, With(UpgradeIds.ThresholdMult, 5)), Eps);
        }
    }
}
