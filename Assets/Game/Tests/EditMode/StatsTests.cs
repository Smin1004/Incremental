using NUnit.Framework;

namespace Incremental.Tests
{
    /// <summary>Stats.Compute over skill tree nodes (12 §3 "스탯 합산"): one test per stat, sums across nodes, caps, switches.</summary>
    public class StatsTests
    {
        const double Eps = 1e-9;
        GameParams p;
        NodeTable t;

        [SetUp]
        public void SetUp()
        {
            p = TestData.Params();
            t = TestData.Nodes();
        }

        EffectiveStats With(params (string id, int level)[] levels) => Stats.Compute(p, t, TestData.Meta(levels));

        [Test]
        public void NoNodes_IsBaseParams()
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

        [Test] public void SpawnRate_AddsPerLevel() => Assert.AreEqual(4 + 3, With(("a_spawn", 3)).spawnRate, Eps);

        [Test] public void SpawnRate_SumsAcrossNodes() => Assert.AreEqual(4 + 3 + 4, With(("a_spawn", 3), ("b_spawn", 2)).spawnRate, Eps);

        [Test] public void PullAccel_AddsPercent() => Assert.AreEqual(600 * 1.4, With(("a_pull", 2)).pullAccel, Eps);

        [Test] public void SaleMult_SumsPercentAcrossNodes() => Assert.AreEqual(1 + 0.5 + 1.0, With(("e_sale", 2), ("d_sale", 1)).saleMult, Eps);

        [Test] public void StaminaMax_AddsPerLevel() => Assert.AreEqual(60 + 30, With(("e_stamina", 3)).staminaMax, Eps);

        [Test] public void ThresholdMult_SubtractsPercent() => Assert.AreEqual(0.8, With(("e_threshold", 5)).thresholdMult, Eps);

        [Test]
        public void ThresholdMult_ReductionCappedAt90Percent() =>
            Assert.AreEqual(0.1, With(("e_threshold", 5), ("c_threshold", 3)).thresholdMult, Eps);

        [Test] public void DustCap_AddsPerLevel() => Assert.AreEqual(300 + 400, With(("e_cap", 4)).dustCap, Eps);

        [Test]
        public void DustMass_CompoundsAndMultipliesAcrossNodes()
        {
            Assert.AreEqual(1.5 * 1.5 * 1.5, With(("e_mass", 3)).dustMass, Eps);
            Assert.AreEqual(3.375 * 2.25, With(("e_mass", 3), ("b_mass", 2)).dustMass, Eps);
        }

        [Test] public void GravityRadius_AddsPercent() => Assert.AreEqual(180 * 1.5, With(("e_radius", 5)).gravityRadius, Eps);

        [Test]
        public void StaminaDrain_ReducesIdleAndHoldDrain()
        {
            var s = With(("e_drain", 4));
            Assert.AreEqual(0.2 * 0.8, s.staminaIdleDrain, Eps);
            Assert.AreEqual(1.0 * 0.8, s.staminaHoldDrain, Eps);
        }

        [Test]
        public void StaminaDrain_ReductionCappedAt90Percent()
        {
            var s = With(("c_drain", 3)); // -150%
            Assert.AreEqual(0.2 * 0.1, s.staminaIdleDrain, Eps);
            Assert.AreEqual(1.0 * 0.1, s.staminaHoldDrain, Eps);
        }

        [Test] public void StartBonus_AddsInitialDust() => Assert.AreEqual(100 + 100, With(("e_start", 2)).dustInitial, Eps);

        [Test]
        public void StartBonus_CappedByDustCap()
        {
            Assert.AreEqual(300, With(("e_start", 10)).dustInitial, Eps);
            Assert.AreEqual(400, With(("e_start", 10), ("e_cap", 1)).dustInitial, Eps);
        }

        [Test]
        public void EachStatNode_ChangesOnlyItsOwnValue()
        {
            var b = Stats.Compute(p, t, new MetaState());
            foreach (var n in t.nodes)
            {
                if (n.IsGate) continue;
                var s = With((n.id, 1));
                int changed = 0;
                if (s.spawnRate != b.spawnRate) changed++;
                if (s.pullAccel != b.pullAccel) changed++;
                if (s.saleMult != b.saleMult) changed++;
                if (s.staminaMax != b.staminaMax) changed++;
                if (s.thresholdMult != b.thresholdMult) changed++;
                if (s.dustCap != b.dustCap) changed++;
                if (s.dustMass != b.dustMass) changed++;
                if (s.gravityRadius != b.gravityRadius) changed++;
                if (s.staminaIdleDrain != b.staminaIdleDrain || s.staminaHoldDrain != b.staminaHoldDrain) changed++;
                if (s.dustInitial != b.dustInitial) changed++;
                Assert.AreEqual(1, changed, n.id + " should change exactly one effective value");
            }
        }

        [Test]
        public void EveryStatId_IsCovered()
        {
            foreach (var id in StatIds.All)
                Assert.IsTrue(t.nodes.Exists(n => n.statId == id), "test table lacks a node for " + id);
        }

        [Test]
        public void DisabledNode_HasNoEffect()
        {
            t.Get("a_spawn").enabled = false;
            t.Get("e_mass").enabled = false;
            var s = With(("a_spawn", 3), ("e_mass", 3));
            Assert.AreEqual(4, s.spawnRate, Eps);
            Assert.AreEqual(1, s.dustMass, Eps);
        }

        [Test]
        public void GatesAndUnknownIds_HaveNoEffect()
        {
            var b = Stats.Compute(p, t, new MetaState());
            var s = With(("gate_t2", 1), ("gate_t3", 1), ("retired_node", 7));
            Assert.AreEqual(UnityEngine.JsonUtility.ToJson(b), UnityEngine.JsonUtility.ToJson(s));
        }

        [Test]
        public void LevelAboveMax_IsClamped() => Assert.AreEqual(4 + 3, With(("a_spawn", 9)).spawnRate, Eps);

        [Test]
        public void Threshold_UsesThresholdMult()
        {
            var tier = new CelestialTier { tier = 1, requiredMass = 10 };
            Assert.AreEqual(8, Stats.Threshold(tier, With(("e_threshold", 5))), Eps);
        }

        [Test]
        public void SaleIncome_RoundsUpToInteger()
        {
            var tier = new CelestialTier { tier = 2, salePrice = 6 };
            Assert.AreEqual(6, Stats.SaleIncome(tier, With()), Eps);
            Assert.AreEqual(8, Stats.SaleIncome(tier, With(("e_sale", 1))), Eps); // 6 × 1.25 = 7.5
            var noisy = new CelestialTier { tier = 4, salePrice = 250 };
            Assert.AreEqual(275, Stats.SaleIncome(noisy, new EffectiveStats { saleMult = 1.1 }), Eps); // 275.00000000000006
        }
    }
}
