using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Incremental.Tests
{
    /// <summary>
    /// Week 7 (22): the gravity source follow as a rule (§3), the vortex and trails (§5), the widening world (§6),
    /// the dust cap policy (§7) and the rebalance (§8).
    /// </summary>
    public class Week7Tests
    {
        const double Eps = 1e-9;

        static GameParams ProjectParams() => AssetDatabase.LoadAssetAtPath<GameParams>("Assets/Game/Data/GameParams.asset");
        static NodeTable ProjectNodes() => AssetDatabase.LoadAssetAtPath<NodeTable>("Assets/Game/Data/NodeTable.asset");

        // ---------------- §3 A. gravity source follow ----------------

        [Test]
        public void Follow_IsOnByDefaultAt220()
        {
            var p = ProjectParams();
            Assert.IsTrue(p.followEnabled, "22 §3 turns the follow on as a rule");
            Assert.AreEqual(220, p.followSpeedPx, Eps, "base top speed");
        }

        [Test]
        public void FollowDebugMults_StartAtTheEffectiveSpeed()
        {
            var p = ProjectParams();
            CollectionAssert.AreEqual(new double[] { 1, 1.5, 2 }, p.followDebugMults, "off / base / ×1.5 / ×2 (22 §3)");
        }

        [Test]
        public void FollowNodes_AreOnePerEvenRingUpToTen()
        {
            var t = ProjectNodes();
            var rings = new List<int>();
            foreach (var n in t.nodes)
                if (n.kind == NodeKind.Stat && n.statId == StatIds.FollowSpeed) rings.Add(n.minTier);
            rings.Sort();
            CollectionAssert.AreEqual(new[] { 2, 4, 6, 8, 10 }, rings, "22 §3: rings 2, 4, 6, 8, 10");
        }

        [Test]
        public void FollowNodes_AddFifteenPointsEachAndReach385()
        {
            var p = ProjectParams();
            var t = ProjectNodes();
            var m = new MetaState();
            Assert.AreEqual(220, Stats.Compute(p, t, m).followSpeedPx, 1e-6, "nothing bought: the base speed");
            foreach (var n in t.nodes)
                if (n.kind == NodeKind.Stat && n.statId == StatIds.FollowSpeed)
                {
                    Assert.AreEqual(0.15, n.effectPerLevel, Eps, n.id + " adds 15%p of the base");
                    Assert.AreEqual(1, n.maxLevel, n.id + " has one level");
                    m.SetLevel(n.id, 1);
                }
            Assert.AreEqual(385, Stats.Compute(p, t, m).followSpeedPx, 1e-6, "220 × 1.75 with all five (22 §3)");
        }

        [Test]
        public void FollowNodes_SitOnTheGravityAxisWithoutOverlappingIt()
        {
            var t = ProjectNodes();
            foreach (var n in t.nodes)
            {
                if (n.kind != NodeKind.Stat || n.statId != StatIds.FollowSpeed) continue;
                foreach (var o in t.nodes)
                {
                    if (o == n || o.minTier != n.minTier) continue;
                    Assert.Greater(Mathf.Abs(o.angleDeg - n.angleDeg), 1e-3f,
                        n.id + " and " + o.id + " would be drawn on top of each other");
                }
            }
        }

        [Test]
        public void FollowNodes_ChainAlongTheGravityAxis()
        {
            var t = ProjectNodes();
            var m = new MetaState();
            // With only the ring 1 pull node owned the first follow node is reachable and the later ones are not.
            m.SetLevel("r1_pull", 1);
            Assert.IsTrue(Shop.PrereqsMet(t, m, t.Get("r2_follow")), "r2_follow follows the ring 1 pull node");
            foreach (var id in new[] { "r4_follow", "r6_follow", "r8_follow", "r10_follow" })
                Assert.IsFalse(Shop.PrereqsMet(t, m, t.Get(id)), id + " waits for the follow node before it");
            foreach (var id in new[] { "r2_follow", "r4_follow", "r6_follow", "r8_follow" })
            {
                m.SetLevel(id, 1);
                Assert.IsTrue(Shop.PrereqsMet(t, m, t.Get(NextFollow(id))), NextFollow(id) + " opens after " + id);
            }
        }

        static string NextFollow(string id)
        {
            switch (id)
            {
                case "r2_follow": return "r4_follow";
                case "r4_follow": return "r6_follow";
                case "r6_follow": return "r8_follow";
                default: return "r10_follow";
            }
        }
    }
}
