using System.Collections.Generic;
using NUnit.Framework;

namespace Incremental.Tests
{
    /// <summary>Skill tree purchase rules (12 §2), node states (12 §4), gates and the bot's purchase choice.</summary>
    public class ShopTests
    {
        NodeTable t;

        [SetUp]
        public void SetUp() => t = TestData.Nodes();

        NodeDef N(string id) => t.Get(id);

        // ---------------- cost ----------------

        [Test]
        public void Cost_IsBaseTimesRingMultTimesGrowthPowLevel_RoundedUp()
        {
            var m = new MetaState();
            Assert.AreEqual(10, Shop.Cost(t, m, "a_spawn"));
            m.SetLevel("a_spawn", 1);
            Assert.AreEqual(15, Shop.Cost(t, m, "a_spawn"));
            m.SetLevel("a_spawn", 2);
            Assert.AreEqual(23, Shop.Cost(t, m, "a_spawn")); // 22.5
            t.ringCostMult[1] = 2;
            Assert.AreEqual(45, Shop.Cost(t, m, "a_spawn"), "ringCostMult scales the whole ring");
            Assert.AreEqual(60, Shop.Cost(t, m, "gate_t2"), "gates too");
            Assert.AreEqual(40, Shop.Cost(t, m, "b_spawn"), "other rings keep their multiplier");
        }

        [Test]
        public void Cost_MissingRingMultCountsAsOne()
        {
            t.ringCostMult = new double[] { 1, 1 };
            Assert.AreEqual(1000, Shop.Cost(t, new MetaState(), "d_sale"));
        }

        [Test]
        public void Cost_SnapsBinaryNoiseButNeverGoesBelowRealValue()
        {
            var n = TestData.Stat("x", 1, 0, StatIds.DustMass, 1.5, 0, 200, 1.6);
            Assert.AreEqual(512, Stats.CostCeil(t, n, 2)); // 512.00000000000011
            foreach (var node in t.nodes)
                for (int level = 0; level <= 80; level++)
                {
                    double raw = Stats.Cost(t, node, level);
                    double charged = Stats.CostCeil(t, node, level);
                    Assert.AreEqual(System.Math.Floor(charged), charged);
                    Assert.GreaterOrEqual(charged, raw - 1e-6, $"{node.id} level {level}");
                    Assert.Less(charged - raw, 1.0, $"{node.id} level {level}");
                }
        }

        // ---------------- buying ----------------

        [Test]
        public void Buy_ChargesAndAddsLevel()
        {
            var m = new MetaState { currency = 30 };
            Assert.IsTrue(Shop.Buy(t, m, "a_spawn"));
            Assert.AreEqual(20, m.currency);
            Assert.AreEqual(1, m.GetLevel("a_spawn"));
            Assert.IsTrue(Shop.Buy(t, m, "a_spawn"));
            Assert.AreEqual(5, m.currency);
            Assert.IsFalse(Shop.Buy(t, m, "a_spawn"), "23 > 5");
            Assert.AreEqual(5, m.currency);
            Assert.AreEqual(2, m.GetLevel("a_spawn"));
        }

        [Test]
        public void MaxLevel_StopsPurchases_ZeroIsUnlimited()
        {
            var m = new MetaState { currency = 1e9 };
            m.SetLevel("a_pull", 2);
            Assert.IsTrue(Shop.IsMaxed(m, N("a_pull")));
            Assert.IsFalse(Shop.Buy(t, m, "a_pull"));
            Assert.AreEqual(2, m.GetLevel("a_pull"));

            m.unlockedMaxTier = 4;
            m.SetLevel("c_threshold", 1);
            m.SetLevel("d_sale", 500);
            Assert.IsFalse(Shop.IsMaxed(m, N("d_sale")));
            Assert.IsTrue(Shop.IsPurchasable(t, m, N("d_sale")));
        }

        [Test]
        public void Ring_NeedsUnlockedTier()
        {
            var m = TestData.Meta(("a_spawn", 1));
            m.currency = 1e6;
            Assert.IsFalse(Shop.IsPurchasable(t, m, N("b_spawn")), "ring 2 before gate_t2");
            Assert.IsTrue(Shop.Buy(t, m, "gate_t2"));
            Assert.AreEqual(2, m.unlockedMaxTier);
            Assert.IsTrue(Shop.Buy(t, m, "b_spawn"));
        }

        [Test]
        public void Prereqs_AreAnyOf()
        {
            var m = new MetaState { currency = 1e6, unlockedMaxTier = 2 };
            Assert.IsFalse(Shop.PrereqsMet(t, m, N("b_mass")));
            m.SetLevel("a_pull", 1);
            Assert.IsTrue(Shop.PrereqsMet(t, m, N("b_mass")), "a_pull alone is enough");
            Assert.IsFalse(Shop.PrereqsMet(t, m, N("b_spawn")), "b_spawn needs a_spawn");
            Assert.IsTrue(Shop.PrereqsMet(t, m, N("a_spawn")), "no prereqs: next to the center");
        }

        [Test]
        public void DisabledNode_IsNotPurchasable_AndDoesNotCountAsPrereq()
        {
            var m = TestData.Meta(("a_spawn", 1));
            m.currency = 1e6;
            m.unlockedMaxTier = 2;
            N("a_spawn").enabled = false;
            Assert.IsFalse(Shop.IsPurchasable(t, m, N("a_spawn")));
            Assert.IsFalse(Shop.IsPurchasable(t, m, N("b_spawn")));
        }

        [Test]
        public void UnknownId_CannotBeBought()
        {
            var m = new MetaState { currency = 1e6 };
            Assert.IsFalse(Shop.Buy(t, m, "no_such_node"));
            Assert.AreEqual(double.PositiveInfinity, Shop.Cost(t, m, "no_such_node"));
        }

        // ---------------- gates ----------------

        [Test]
        public void Gates_AreSequential_AndSetUnlockedMaxTier()
        {
            var m = new MetaState { currency = 1e6 };
            Assert.AreSame(N("gate_t2"), Shop.NextGate(t, m));
            Assert.IsFalse(Shop.Buy(t, m, "gate_t3"), "gate_t3 before gate_t2");
            Assert.IsTrue(Shop.Buy(t, m, "gate_t2"));
            Assert.AreEqual(2, m.unlockedMaxTier);
            Assert.IsFalse(Shop.Buy(t, m, "gate_t2"), "one level");
            Assert.AreSame(N("gate_t3"), Shop.NextGate(t, m));
            Assert.IsTrue(Shop.Buy(t, m, "gate_t3"));
            Assert.IsTrue(Shop.Buy(t, m, "gate_t4"));
            Assert.AreEqual(4, m.unlockedMaxTier);
            Assert.IsNull(Shop.NextGate(t, m));
            Assert.AreEqual(1e6 - 30 - 300 - 3000, m.currency);
        }

        [Test]
        public void Gate_NeedsNoStatNodes()
        {
            var m = new MetaState { currency = 30 };
            Assert.IsTrue(Shop.Buy(t, m, "gate_t2"));
        }

        [Test]
        public void UnlockedMaxTier_IsDerivedFromConsecutiveGates()
        {
            var m = TestData.Meta(("gate_t2", 1), ("gate_t4", 1));
            m.unlockedMaxTier = 9;
            Assert.AreEqual(2, Shop.RecomputeUnlockedMaxTier(t, m));
            m.SetLevel("gate_t3", 1);
            Assert.AreEqual(4, Shop.RecomputeUnlockedMaxTier(t, m));
            N("gate_t3").enabled = false;
            Assert.AreEqual(2, Shop.RecomputeUnlockedMaxTier(t, m), "a disabled gate counts as absent");
        }

        [Test]
        public void UnlockNextFree_RaisesGateLevel_WithoutCharging()
        {
            var m = new MetaState { currency = 5 };
            Assert.AreEqual(2, Shop.UnlockNextFree(t, m));
            Assert.AreEqual(1, m.GetLevel("gate_t2"));
            Assert.AreEqual(2, m.unlockedMaxTier);
            Assert.AreEqual(5, m.currency);
            Shop.UnlockNextFree(t, m);
            Shop.UnlockNextFree(t, m);
            Assert.AreEqual(4, m.unlockedMaxTier);
            Assert.AreEqual(0, Shop.UnlockNextFree(t, m));
        }

        // ---------------- states and list ----------------

        [Test]
        public void States()
        {
            var m = new MetaState();
            Assert.AreEqual(NodeState.Available, Shop.State(t, m, N("a_spawn")));
            Assert.AreEqual(NodeState.Locked, Shop.State(t, m, N("b_spawn")), "next ring");
            Assert.AreEqual(NodeState.Hidden, Shop.State(t, m, N("c_threshold")), "two rings ahead");
            m.SetLevel("a_spawn", 1);
            Assert.AreEqual(NodeState.Owned, Shop.State(t, m, N("a_spawn")));
            m.SetLevel("a_spawn", 3);
            Assert.AreEqual(NodeState.Maxed, Shop.State(t, m, N("a_spawn")));
            m.unlockedMaxTier = 2;
            Assert.AreEqual(NodeState.Available, Shop.State(t, m, N("b_spawn")));
            Assert.AreEqual(NodeState.Locked, Shop.State(t, m, N("c_drain")), "next ring, prereq missing");
        }

        [Test]
        public void PurchasableStatNodes_AreSortedByCost_WithoutGatesOrMaxed()
        {
            var m = TestData.Meta(("e_sale", 4));
            var list = new List<NodeDef>();
            Shop.PurchasableStatNodes(t, m, list);
            Assert.IsFalse(list.Exists(n => n.IsGate));
            Assert.IsFalse(list.Contains(N("e_sale")), "maxed");
            Assert.IsFalse(list.Contains(N("b_spawn")), "next ring");
            for (int i = 1; i < list.Count; i++)
                Assert.LessOrEqual(Shop.Cost(t, m, list[i - 1]), Shop.Cost(t, m, list[i]));
            Assert.AreEqual(10, Shop.Cost(t, m, list[list.Count - 1]));
        }

        // ---------------- bot ----------------

        [Test]
        public void Bot_SavesForGate_WhenNextRunWouldPayForIt()
        {
            var scratch = new List<NodeDef>();
            var m = new MetaState { currency = 25, lastRunIncome = 10 };
            Assert.IsNull(AutoplayBot.ChooseNext(t, m, GateSaving.Stop, scratch), "25 + 10 >= 30: save");
            m.currency = 30;
            Assert.AreSame(N("gate_t2"), AutoplayBot.ChooseNext(t, m, GateSaving.Stop, scratch), "gate first when affordable");
            m.currency = 5;
            m.lastRunIncome = 10;
            var pick = AutoplayBot.ChooseNext(t, m, GateSaving.Stop, scratch);
            Assert.IsNotNull(pick, "5 + 10 < 30: spend");
            Assert.AreEqual(5, Shop.Cost(t, m, pick));
        }

        [Test]
        public void Bot_WithoutSaving_BuysCheapest()
        {
            var scratch = new List<NodeDef>();
            var m = new MetaState { currency = 30, lastRunIncome = 100 };
            var pick = AutoplayBot.ChooseNext(t, m, GateSaving.Off, scratch);
            Assert.IsFalse(pick.IsGate);
            Assert.AreEqual(5, Shop.Cost(t, m, pick));
            m.currency = 4;
            Assert.IsNull(AutoplayBot.ChooseNext(t, m, GateSaving.Off, scratch));
        }

        [Test]
        public void Bot_KeepReserve_SpendsOnlyTheSurplus()
        {
            var scratch = new List<NodeDef>();
            // Gate 30, last income 20: keep 10. Currency 14 leaves 4 to spend: nothing costs 4.
            var m = new MetaState { currency = 14, lastRunIncome = 20 };
            Assert.IsNull(AutoplayBot.ChooseNext(t, m, GateSaving.KeepReserve, scratch));
            // Currency 16 leaves 6: a cost-5 node.
            m.currency = 16;
            var pick = AutoplayBot.ChooseNext(t, m, GateSaving.KeepReserve, scratch);
            Assert.IsNotNull(pick);
            Assert.AreEqual(5, Shop.Cost(t, m, pick));
            Assert.GreaterOrEqual(m.currency - Shop.Cost(t, m, pick) + m.lastRunIncome, 30);
        }
    }
}
