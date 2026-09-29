using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Incremental.Tests
{
    /// <summary>Week 6 (20): dust pull shaping (§3), dust kinds (§4), fragments (§5), the three week-5 answers (§6) and the balance pass (§8).</summary>
    public class Week6Tests
    {
        const double Eps = 1e-9;

        static GameParams ProjectParams() => AssetDatabase.LoadAssetAtPath<GameParams>("Assets/Game/Data/GameParams.asset");
        static CelestialTable ProjectTiers() => AssetDatabase.LoadAssetAtPath<CelestialTable>("Assets/Game/Data/CelestialTable.asset");
        static NodeTable ProjectNodes() => AssetDatabase.LoadAssetAtPath<NodeTable>("Assets/Game/Data/NodeTable.asset");

        // ---------------- §3 A. dust pull ----------------

        [Test]
        public void PullCurve_FollowsTheSpecTable()
        {
            const double r = 180, full = 0.75, edge = 0.15, outer = 1.4;
            Assert.AreEqual(1.0, DustField.PullFactor(0, r, full, edge, outer), Eps, "at the cursor");
            Assert.AreEqual(1.0, DustField.PullFactor(r * 0.75, r, full, edge, outer), Eps, "full out to 0.75R");
            Assert.AreEqual(edge, DustField.PullFactor(r, r, full, edge, outer), Eps, "edge share at the radius");
            Assert.AreEqual(edge * 0.5, DustField.PullFactor(r * 1.2, r, full, edge, outer), 1e-6, "halfway through the outer band");
            Assert.AreEqual(0.0, DustField.PullFactor(r * 1.4, r, full, edge, outer), Eps, "gone at 1.4R");
            Assert.AreEqual(0.0, DustField.PullFactor(r * 2, r, full, edge, outer), Eps, "gone beyond 1.4R");
        }

        [Test]
        public void PullCurve_DecreasesAndIsSmoothAtTheHandover()
        {
            const double r = 180, full = 0.75, edge = 0.15, outer = 1.4;
            double prev = 2;
            for (int i = 0; i <= 300; i++)
            {
                double f = DustField.PullFactor(r * outer * i / 300.0, r, full, edge, outer);
                Assert.LessOrEqual(f, prev + 1e-12, "the pull never grows with distance");
                prev = f;
            }
            // No cliff at the radius: the two pieces meet at edgeShare.
            double inside = DustField.PullFactor(r - 0.01, r, full, edge, outer);
            double outside = DustField.PullFactor(r + 0.01, r, full, edge, outer);
            Assert.AreEqual(inside, outside, 2e-3, "the curve is continuous at the gravity radius");
        }

        [Test]
        public void SweptCapture_CatchesDustThatWouldStepOverTheCircle()
        {
            var center = new Vector2(0f, 0f);
            // A dust flying straight through the circle in one tick: both ends are outside, the segment is not.
            Assert.IsTrue(DustField.SegmentHitsCircle(new Vector2(-1f, 0f), new Vector2(1f, 0f), center, 0.3f), "passes through");
            Assert.IsFalse(DustField.SegmentHitsCircle(new Vector2(-1f, 1f), new Vector2(1f, 1f), center, 0.3f), "misses to the side");
            Assert.IsTrue(DustField.SegmentHitsCircle(new Vector2(0.1f, 0f), new Vector2(0.2f, 0f), center, 0.3f), "ends inside");
            Assert.IsFalse(DustField.SegmentHitsCircle(new Vector2(2f, 0f), new Vector2(2f, 0f), center, 0.3f), "standing still outside");
        }

        [Test]
        public void PullParams_AreTheSpecValues()
        {
            var p = ProjectParams();
            Assert.AreEqual(0.75, p.pullFullRatio, Eps);
            Assert.AreEqual(0.15, p.pullEdgeShare, Eps);
            Assert.AreEqual(1.4, p.pullOuterRatio, Eps);
            Assert.AreEqual(2.5, p.captureAssistRatio, Eps);
            Assert.AreEqual(0.6, p.tangentialKeepPerSec, Eps, "40% of the tangential speed is damped per second");
        }

        [Test]
        public void PullParams_AreValidated()
        {
            var p = TestData.Params();
            p.pullFullRatio = 1.2;
            p.pullEdgeShare = 1.5;
            p.pullOuterRatio = 0.9;
            p.captureAssistRatio = 0.5;
            p.tangentialKeepPerSec = 0;
            var r = DataValidator.ValidateParams(p);
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("pullFullRatio")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("pullEdgeShare")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("pullOuterRatio")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("captureAssistRatio")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("tangentialKeepPerSec")), Joined(r.Errors));
            Object.DestroyImmediate(p);
        }

        static string Joined(List<string> list) => string.Join("\n", list);
    }
}
