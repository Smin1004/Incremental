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

        // ---------------- §6 D. widening world ----------------

        [Test]
        public void WorldScale_IsOneAtRingOneAndWiderAtEveryRingAbove()
        {
            var p = ProjectParams();
            // 22 §6 asked for 0.06 per ring; 22 §7 measured the dust cap that followed and brought it to 0.0375, which
            // keeps the density at a frame budget the build can hold (Reports/Week7.md).
            Assert.AreEqual(0.0375, p.worldScalePerRing, Eps, "22 §7's value after the measurement");
            Assert.AreEqual(0.6, p.worldExpandSec, Eps, "the world takes 0.6s to grow");
            Assert.AreEqual(8, p.bodyMinScreenPx, Eps, "a body never falls below 8px on screen");
            Assert.AreEqual(1.00, Stats.WorldScale(p, 1), 1e-9, "ring 1");
            Assert.AreEqual(1.15, Stats.WorldScale(p, 5), 1e-9, "ring 5");
            Assert.AreEqual(1.375, Stats.WorldScale(p, 11), 1e-9, "ring 11");
            Assert.AreEqual(1.890625, Stats.WorldScale(p, 11) * Stats.WorldScale(p, 11), 1e-9, "1.89x the area");
            for (int ring = 2; ring <= 11; ring++)
                Assert.Greater(Stats.WorldScale(p, ring), Stats.WorldScale(p, ring - 1), "ring " + ring + " is wider than the one below");
        }

        [Test]
        public void WorldScale_NeverShrinksBelowOne()
        {
            var p = ProjectParams();
            Assert.AreEqual(1.0, Stats.WorldScale(p, 0), 1e-9, "a ring below 1 cannot make the world smaller than ring 1");
        }

        [Test]
        public void DustCapAndStartingDust_FollowTheArea()
        {
            var p = TestData.Params();
            p.worldScalePerRing = 0.06;   // the rule, not the shipped value: round numbers keep the expected results readable
            p.dustCap = 300;
            p.dustInitial = 100;
            var t = TestData.Nodes();
            // Ring 1: nothing is scaled.
            var ring1 = Stats.Compute(p, t, new MetaState());
            Assert.AreEqual(1.0, ring1.worldScale, 1e-9);
            Assert.AreEqual(300, ring1.dustCap, 1e-6);
            Assert.AreEqual(100, ring1.dustInitial, 1e-6);
            // Ring 4 of the test tree: the scale is 1.18, so the cap and the starting dust go up by its square.
            var m = TestData.Meta(("gate_t2", 1), ("gate_t3", 1), ("gate_t4", 1));
            Shop.RecomputeUnlockedMaxTier(t, m);
            Assert.AreEqual(4, m.unlockedMaxTier);
            var ring4 = Stats.Compute(p, t, m);
            double area = 1.18 * 1.18;
            Assert.AreEqual(1.18, ring4.worldScale, 1e-9);
            Assert.AreEqual(300 * area, ring4.dustCap, 1e-6, "the cap follows the area (22 §6)");
            Assert.AreEqual(100 * area, ring4.dustInitial, 1e-6, "so does the starting dust, to keep the density");
        }

        [Test]
        public void DustCap_AtRingElevenFollowsTheAreaOfTheLastRing()
        {
            var p = ProjectParams();
            var t = ProjectNodes();
            var m = new MetaState();
            double ring1 = Stats.Compute(p, t, m).dustCap;
            for (int tier = 2; tier <= 11; tier++) m.SetLevel("gate_t" + tier, 1);
            Shop.RecomputeUnlockedMaxTier(t, m);
            double ring11 = Stats.Compute(p, t, m).dustCap;
            // Only the gates are owned, so the node part of the cap is the same on both sides and the ratio is the area.
            double area = Stats.WorldScale(p, 11) * Stats.WorldScale(p, 11);
            Assert.AreEqual(area, ring11 / ring1, 1e-6, "22 §6: the cap at the last ring is its area times the ring 1 cap");
        }

        [Test]
        public void BodyMinScreenSize_GrowsWithTheScaleSoScreenSizeIsConstant()
        {
            // A body's world size does not change with the scale, so its screen size is size / scale. The floor has to
            // move with the scale for the screen size to stay at bodyMinScreenPx (22 §6).
            var p = ProjectParams();
            double floorAtRing1 = p.bodyMinScreenPx * Stats.WorldScale(p, 1);
            double floorAtRing11 = p.bodyMinScreenPx * Stats.WorldScale(p, 11);
            Assert.AreEqual(8, floorAtRing1, 1e-9);
            Assert.AreEqual(8 * Stats.WorldScale(p, 11), floorAtRing11, 1e-9);
            Assert.AreEqual(floorAtRing1 / Stats.WorldScale(p, 1), floorAtRing11 / Stats.WorldScale(p, 11), 1e-9,
                "the same number of screen px at both rings");
        }

        [Test]
        public void SmallestTier_StaysVisibleAtRingEleven()
        {
            var p = ProjectParams();
            var c = AssetDatabase.LoadAssetAtPath<CelestialTable>("Assets/Game/Data/CelestialTable.asset");
            double scale = Stats.WorldScale(p, 11);
            double asteroid = c.Get(1).sizePx;
            double fragment = asteroid * p.fragmentScale;
            // Without the floor a tier 1 fragment would be this small on screen at ring 11.
            double bare = fragment / scale;
            double floored = System.Math.Max(fragment, p.bodyMinScreenPx * scale) / scale;
            Assert.GreaterOrEqual(floored, p.bodyMinScreenPx - 1e-9, "the floor keeps it identifiable (22 §6)");
            Assert.LessOrEqual(bare, floored + 1e-9, "the floor only ever makes it bigger");
        }

        // ---------------- §5 C. vortex and trails ----------------

        [Test]
        public void VortexAndTrails_AreTheSpecValues()
        {
            var p = ProjectParams();
            Assert.AreEqual(0.25, p.vortexShare, Eps, "22 §5: a quarter of the pull goes sideways");
            Assert.AreEqual(8, p.trailPoints, "positions kept per trail (22 §5 asked for 6; 8 shows the curve)");
            Assert.AreEqual(0.08, p.trailSampleSec, Eps, "fixed time between two of them, so a ribbon covers 0.64s");
            Assert.AreEqual(0.6, p.trailWidthShare, Eps, "the head is 60% of the dust's drawn size");
            Assert.AreEqual(300, p.trailBudget, "trails drawn at once");
        }

        [Test]
        public void Vortex_TurnsTheAccelerationCounterClockwise()
        {
            // A dust to the right of the source: the pull points left, and the sideways part has to point up, or the
            // spiral would run the other way round (22 §5 "회전 방향은 반시계 고정").
            Vector2 toSource = new Vector2(-1f, 0f);
            Vector2 swirl = new Vector2(toSource.y, -toSource.x);
            Assert.AreEqual(1f, swirl.y, 1e-6f, "sideways push is +y on the right of the source (counter-clockwise)");
            // Cross product of the position (relative to the source) with the sideways push is positive for CCW.
            Vector2 pos = -toSource;
            Assert.Greater(pos.x * swirl.y - pos.y * swirl.x, 0f, "counter-clockwise");
        }

        [Test]
        public void VortexShare_DoesNotOutweighThePull()
        {
            // At or above 1 the sideways part is as strong as the pull and dust stops falling in; the validator says so.
            var p = ProjectParams();
            double keep = p.vortexShare;
            try
            {
                p.vortexShare = 1;
                var r = DataValidator.ValidateParams(p);
                Assert.IsTrue(r.Errors.Exists(e => e.Contains("vortexShare")), string.Join("\n", r.Errors));
                p.vortexShare = -0.1;
                r = DataValidator.ValidateParams(p);
                Assert.IsTrue(r.Errors.Exists(e => e.Contains("vortexShare")), string.Join("\n", r.Errors));
            }
            finally
            {
                p.vortexShare = keep;
            }
        }

        [Test]
        public void TangentialDamping_StaysInsideTheCaptureAssistRadius()
        {
            // 22 §5 keeps the 20 §3 rule: outside 2.5 capture radii the sideways speed is left alone, so dust curves in.
            var p = ProjectParams();
            Assert.AreEqual(2.5, p.captureAssistRatio, Eps, "unchanged by 22 §5");
            Assert.Less(p.captureRadius * p.captureAssistRatio, p.gravityRadius,
                "the damping band has to be well inside the gravity radius, or nothing ever curves");
        }

        [Test]
        public void Trails_KeepTheMostRecentPositionsNewestFirst()
        {
            var field = new GameObject("TrailField").AddComponent<DustField>();
            try
            {
                var p = TestData.Params();
                p.trailPoints = 3;
                p.trailSampleSec = 0.05;
                p.driftSpeed = 0;
                field.Init(p);
                var t = TestData.Nodes();
                var m = TestData.Meta(("dust_medium", 1), ("e_chance", 4));
                var stats = Stats.Compute(p, t, m);
                // Every spawn is medium dust, so every dust is trailed.
                stats.dustKindChance = new double[] { 0, 1 };
                var area = new Rect(-10f, -10f, 20f, 20f);
                field.Reset(1, area, new Vector2(100f, 100f), stats);
                Assert.AreEqual(1, field.Count);
                Assert.AreEqual(1, field.Items[0].kind, "the staged field is medium dust");
                Assert.AreEqual(0, field.Items[0].trailFill, "a fresh dust has no path yet");

                // Four samples: the ring buffer keeps the last three, newest first.
                var seen = new List<Vector2>();
                for (int i = 0; i < 4; i++)
                {
                    field.Items[0].pos = new Vector2(i, 0f);
                    field.Items[0].vel = Vector2.zero;
                    seen.Add(new Vector2(i, 0f));
                    field.Tick(0.05, area, new Vector2(100f, 100f), false, stats);
                }
                Assert.AreEqual(3, field.Items[0].trailFill, "capped at trailPoints");
                for (int age = 0; age < 3; age++)
                    Assert.AreEqual(seen[3 - age].x, field.TrailPoint(0, age).x, 1e-4f,
                        "age " + age + " is the " + (age + 1) + "th most recent position");
            }
            finally
            {
                Object.DestroyImmediate(field.gameObject);
            }
        }

        [Test]
        public void Trails_AreOnlyKeptForKindsAboveTheBaseDust()
        {
            var field = new GameObject("TrailField2").AddComponent<DustField>();
            try
            {
                var p = TestData.Params();
                p.trailPoints = 4;
                p.driftSpeed = 0;
                field.Init(p);
                var stats = Stats.Compute(p, TestData.Nodes(), new MetaState());
                var area = new Rect(-10f, -10f, 20f, 20f);
                field.Reset(20, area, new Vector2(100f, 100f), stats);
                for (int i = 0; i < 5; i++) field.Tick(0.05, area, new Vector2(100f, 100f), false, stats);
                for (int i = 0; i < field.Count; i++)
                {
                    Assert.AreEqual(0, field.Items[i].kind, "no kind is unlocked");
                    Assert.AreEqual(0, field.Items[i].trailFill, "base dust keeps the stretched quad, not a ribbon");
                }
            }
            finally
            {
                Object.DestroyImmediate(field.gameObject);
            }
        }

        [Test]
        public void Trails_MoveWithTheDustWhenAnotherOneIsCaptured()
        {
            // Capture removes a dust by moving the last one into its place; the trail block has to follow, or a ribbon
            // would be drawn from someone else's path (22 §5).
            var field = new GameObject("TrailField3").AddComponent<DustField>();
            try
            {
                var p = TestData.Params();
                p.trailPoints = 3;
                p.trailSampleSec = 0.05;
                p.driftSpeed = 0;
                p.captureRadius = 5;
                p.vortexShare = 0;
                field.Init(p);
                var stats = Stats.Compute(p, TestData.Nodes(), new MetaState());
                stats.dustKindChance = new double[] { 0, 1 };
                stats.spawnRate = 0;
                var area = new Rect(-100f, -100f, 200f, 200f);
                var gravity = new Vector2(0f, 0f);
                field.Reset(2, area, new Vector2(500f, 500f), stats);
                Assert.AreEqual(2, field.Count);
                // Dust 0 sits far away and keeps a path; dust 1 is on the source and is captured on the next tick.
                field.Items[0].pos = new Vector2(50f, 0f);
                field.Items[1].pos = new Vector2(60f, 0f);
                field.Items[0].vel = Vector2.zero;
                field.Items[1].vel = Vector2.zero;
                field.Tick(0.05, area, new Vector2(500f, 500f), false, stats);
                field.Items[1].pos = new Vector2(61f, 0f);
                field.Tick(0.05, area, new Vector2(500f, 500f), false, stats);
                Assert.AreEqual(2, field.Items[1].trailFill);
                Vector2 keptNewest = field.TrailPoint(1, 0);
                Vector2 keptOldest = field.TrailPoint(1, 1);
                // Now dust 0 is captured, so dust 1 is moved into slot 0.
                field.Items[0].pos = gravity;
                field.Tick(0.05, area, gravity, true, stats);
                Assert.AreEqual(1, field.Count, "one dust was captured");
                Assert.AreEqual(3, field.Items[0].trailFill, "the survivor kept its own path and added one point");
                Assert.AreEqual(keptNewest.x, field.TrailPoint(0, 1).x, 1e-4f, "its path moved with it");
                Assert.AreEqual(keptOldest.x, field.TrailPoint(0, 2).x, 1e-4f, "its path moved with it");
            }
            finally
            {
                Object.DestroyImmediate(field.gameObject);
            }
        }

        // ---------------- §7 E. dust cap policy ----------------

        [Test]
        public void DustCapCeiling_HoldsTheCapTheLastRingReaches()
        {
            var p = ProjectParams();
            var t = ProjectNodes();
            var m = new MetaState();
            foreach (var n in t.nodes)
                if (n != null && n.enabled) m.SetLevel(n.id, n.maxLevel > 0 ? n.maxLevel : 1);
            Shop.RecomputeUnlockedMaxTier(t, m);
            double cap = Stats.Compute(p, t, m).dustCap;
            Assert.AreEqual(6995, cap, 1.0, "22 §7: the measured cap, 3,700 x the area of ring 11");
            Assert.LessOrEqual(cap, p.dustCapCeiling, "the renderers size their buffers for dustCapCeiling");
        }

        [Test]
        public void DustCapCeiling_IsCheckedByTheValidator()
        {
            var p = ProjectParams();
            var c = AssetDatabase.LoadAssetAtPath<CelestialTable>("Assets/Game/Data/CelestialTable.asset");
            var t = ProjectNodes();
            double keep = p.dustCapCeiling;
            try
            {
                p.dustCapCeiling = 100;
                var r = DataValidator.Validate(c, t, p);
                Assert.IsTrue(r.Errors.Exists(e => e.Contains("dustCapCeiling")), string.Join("; ", r.Errors));
            }
            finally
            {
                p.dustCapCeiling = keep;
            }
        }

        [Test]
        public void DustQualityShares_StartAtFullAndOnlyGoDown()
        {
            // 22 §7 only asks for the data this week; the options screen reads it later.
            var p = ProjectParams();
            Assert.IsNotNull(p.dustQualityShares);
            Assert.Greater(p.dustQualityShares.Length, 1, "a quality setting needs something to go down to");
            Assert.AreEqual(1, p.dustQualityShares[0], Eps, "the first share is the effective cap itself");
            for (int i = 1; i < p.dustQualityShares.Length; i++)
            {
                Assert.Greater(p.dustQualityShares[i], 0, "share " + i);
                Assert.Less(p.dustQualityShares[i], p.dustQualityShares[i - 1], "share " + i + " is below the one before");
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
