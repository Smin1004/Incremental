using NUnit.Framework;

namespace Incremental.Tests
{
    public class ShopTests
    {
        UpgradeTable t;

        [SetUp]
        public void SetUp() => t = TestData.Upgrades();

        [Test]
        public void Cost_IsBaseTimesGrowthPowLevel_RoundedUp()
        {
            var m = new MetaState();
            Assert.AreEqual(10, Shop.UpgradeCost(t, m, UpgradeIds.SpawnRate));
            m.SetLevel(UpgradeIds.SpawnRate, 3);
            Assert.AreEqual(20, Shop.UpgradeCost(t, m, UpgradeIds.SpawnRate)); // 10 × 1.25^3 = 19.53
            m.SetLevel(UpgradeIds.DustMass, 2);
            Assert.AreEqual(512, Shop.UpgradeCost(t, m, UpgradeIds.DustMass)); // 200 × 1.6^2 = 512
        }

        [Test]
        public void Cost_IsNeverBelowRealValue_AndSnapsOnlyBinaryNoise()
        {
            foreach (var def in t.upgrades)
            {
                for (int level = 0; level <= 80; level++)
                {
                    double raw = Stats.Cost(def, level);
                    double charged = Stats.CostCeil(def, level);
                    Assert.AreEqual(System.Math.Floor(charged), charged, "integer cost");
                    Assert.GreaterOrEqual(charged, raw - 1e-6, $"{def.id} level {level}: charged {charged} < real {raw}");
                    Assert.Less(charged - raw, 1.0, $"{def.id} level {level}: charged {charged} is more than 1 above {raw}");
                }
            }
        }

        [Test]
        public void Buy_ChargesCostAndAddsLevel()
        {
            var m = new MetaState { currency = 25 };
            Assert.IsTrue(Shop.BuyUpgrade(t, m, UpgradeIds.SpawnRate));
            Assert.AreEqual(15, m.currency);
            Assert.AreEqual(1, m.GetLevel(UpgradeIds.SpawnRate));
            Assert.IsTrue(Shop.BuyUpgrade(t, m, UpgradeIds.SpawnRate)); // 12.5 → 13
            Assert.AreEqual(2, m.currency);
            Assert.AreEqual(2, m.GetLevel(UpgradeIds.SpawnRate));
        }

        [Test]
        public void Buy_FailsWithoutEnoughCurrency_AndChangesNothing()
        {
            var m = new MetaState { currency = 9.99 };
            Assert.IsFalse(Shop.CanBuyUpgrade(t, m, UpgradeIds.SpawnRate));
            Assert.IsFalse(Shop.BuyUpgrade(t, m, UpgradeIds.SpawnRate));
            Assert.AreEqual(9.99, m.currency);
            Assert.AreEqual(0, m.GetLevel(UpgradeIds.SpawnRate));
        }

        [Test]
        public void MaxLevel_StopsPurchases()
        {
            var m = new MetaState { currency = 1e12 };
            m.SetLevel(UpgradeIds.ThresholdMult, 9);
            Assert.IsFalse(Shop.IsMaxed(t, m, UpgradeIds.ThresholdMult));
            Assert.IsTrue(Shop.BuyUpgrade(t, m, UpgradeIds.ThresholdMult));
            Assert.IsTrue(Shop.IsMaxed(t, m, UpgradeIds.ThresholdMult));
            double before = m.currency;
            Assert.IsFalse(Shop.BuyUpgrade(t, m, UpgradeIds.ThresholdMult));
            Assert.AreEqual(10, m.GetLevel(UpgradeIds.ThresholdMult));
            Assert.AreEqual(before, m.currency);
        }

        [Test]
        public void MaxLevelZero_IsUnlimited()
        {
            var m = new MetaState { currency = 1e12 };
            m.SetLevel(UpgradeIds.SpawnRate, 500);
            Assert.IsFalse(Shop.IsMaxed(t, m, UpgradeIds.SpawnRate));
        }

        [Test]
        public void Unlocks_AreSequential()
        {
            var m = new MetaState { currency = 1e6 };
            Assert.AreEqual(2, Shop.NextUnlockTier(t, m));
            Assert.IsFalse(Shop.CanUnlock(t, m, 3), "tier 3 before tier 2");
            Assert.IsFalse(Shop.Unlock(t, m, 3));
            Assert.IsTrue(Shop.Unlock(t, m, 2));
            Assert.AreEqual(2, m.unlockedMaxTier);
            Assert.AreEqual(1e6 - 200, m.currency);
            Assert.IsFalse(Shop.Unlock(t, m, 2), "already unlocked");
            Assert.AreEqual(3, Shop.NextUnlockTier(t, m));
            Assert.IsTrue(Shop.Unlock(t, m, 3));
        }

        [Test]
        public void Unlock_NeedsCurrency()
        {
            var m = new MetaState { currency = 199 };
            Assert.IsFalse(Shop.Unlock(t, m, 2));
            Assert.AreEqual(1, m.unlockedMaxTier);
        }

        [Test]
        public void NextUnlock_IsZeroWhenAllUnlocked()
        {
            var m = new MetaState { unlockedMaxTier = 11 };
            Assert.AreEqual(0, Shop.NextUnlockTier(t, m));
            Assert.AreEqual(0, Shop.UnlockNextFree(t, m));
            Assert.AreEqual(11, m.unlockedMaxTier);
        }

        [Test]
        public void UnlockNextFree_DoesNotCharge()
        {
            var m = new MetaState { currency = 5 };
            Assert.AreEqual(2, Shop.UnlockNextFree(t, m));
            Assert.AreEqual(2, m.unlockedMaxTier);
            Assert.AreEqual(5, m.currency);
        }

        [Test]
        public void RevealAtTier_HidesAndBlocksUntilReached()
        {
            var def = t.Get(UpgradeIds.DustMass);
            def.revealAtTier = 3;
            var m = new MetaState { currency = 1e6 };
            Assert.IsFalse(Shop.IsVisible(def, m));
            Assert.IsFalse(Shop.BuyUpgrade(t, m, UpgradeIds.DustMass));
            m.unlockedMaxTier = 3;
            Assert.IsTrue(Shop.IsVisible(def, m));
            Assert.IsTrue(Shop.BuyUpgrade(t, m, UpgradeIds.DustMass));
        }

        [Test]
        public void Disabled_IsHiddenAndNotPurchasable()
        {
            var def = t.Get(UpgradeIds.StartBonus);
            def.enabled = false;
            var m = new MetaState { currency = 1e6 };
            Assert.IsFalse(Shop.IsVisible(def, m));
            Assert.IsFalse(Shop.BuyUpgrade(t, m, UpgradeIds.StartBonus));
        }

        [Test]
        public void UnknownId_CannotBeBought()
        {
            var m = new MetaState { currency = 1e6 };
            Assert.IsFalse(Shop.BuyUpgrade(t, m, "no_such_upgrade"));
            Assert.AreEqual(double.PositiveInfinity, Shop.UpgradeCost(t, m, "no_such_upgrade"));
        }
    }
}
