using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Incremental.Tests
{
    /// <summary>Week 5 (15): review fixes of week 4 (§3).</summary>
    public class Week5Tests
    {
        static string Joined(List<string> list) => string.Join("\n", list);

        // ---------------- flash rule (15 §3) ----------------

        [Test]
        public void FlashEnvelope_RisesSmoothlyThenFadesOut()
        {
            Assert.AreEqual(0f, ScreenFlash.Envelope(-0.01f, 0.1f, 1f));
            Assert.AreEqual(0f, ScreenFlash.Envelope(0f, 0.1f, 1f), 1e-6f, "starts dark: no instant flash");
            Assert.Less(ScreenFlash.Envelope(0.03f, 0.1f, 1f), 0.3f, "still rising after 30 ms");
            Assert.AreEqual(1f, ScreenFlash.Envelope(0.1f, 0.1f, 1f), 1e-6f, "peak at the end of the rise");
            Assert.Less(ScreenFlash.Envelope(0.6f, 0.1f, 1f), ScreenFlash.Envelope(0.3f, 0.1f, 1f), "fades after the peak");
            Assert.AreEqual(0f, ScreenFlash.Envelope(1f, 0.1f, 1f), "over at the end");
        }

        [Test]
        public void Detects_FlashAboveCapOrRisingTooFast()
        {
            var p = TestData.Params();
            p.ignitionFlashAlpha = 0.7;
            p.ignitionFlashRiseSec = 0.05;
            var r = DataValidator.ValidateParams(p);
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("ignitionFlashAlpha 0.7 is above flashAlphaCap")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("ignitionFlashRiseSec 0.05 is shorter than flashMinRiseSec")), Joined(r.Errors));
            Object.DestroyImmediate(p);
        }

        [Test]
        public void Defaults_FollowTheFlashRule()
        {
            var p = TestData.Params();
            foreach (var f in DataValidator.FlashPeaks(p)) Assert.LessOrEqual(f.Value, p.flashAlphaCap, f.Key);
            foreach (var f in DataValidator.FlashRises(p)) Assert.GreaterOrEqual(f.Value, p.flashMinRiseSec, f.Key);
            Assert.AreEqual(0.3, p.ignitionFlashAlpha, 1e-9, "first ignition peak 0.7 → 0.3 (15 §3)");
            Assert.AreEqual(0.3, p.flashEdgeShare, 1e-9, "screen edge at 0.3 × the center");
            Object.DestroyImmediate(p);
        }

        // ---------------- result panel (15 §3) ----------------

        [Test]
        public void ResultPanel_ShowsIncreasesOnly()
        {
            var before = new RunRecord { run = 1, endReason = EndReason.Stamina, tierCounts = new[] { 14, 1 } };
            var now = new RunRecord { run = 2, endReason = EndReason.Stamina, tierCounts = new[] { 6, 4 } };
            Assert.AreEqual(-8, ResultMath.Delta(now, before, 1));
            Assert.AreEqual(0, ResultMath.ShownIncrease(now, before, 1), "a decrease is left blank");
            Assert.AreEqual(3, ResultMath.ShownIncrease(now, before, 2));
            Assert.AreEqual(0, ResultMath.ShownIncrease(now, null, 2), "no comparison run");
        }

        // ---------------- dust and stars (15 §3) ----------------

        [Test]
        public void Palettes_WarmDustNeutralStars()
        {
            var p = TestData.Params();
            foreach (var pal in p.palettes)
            {
                Assert.Greater(pal.dustA.r, pal.dustA.b + 0.05f, "dust is warm");
                Assert.Greater(pal.dustB.r, pal.dustB.b + 0.05f, "dust is warm");
                float chroma = Mathf.Max(pal.starTint.r, pal.starTint.g, pal.starTint.b) - Mathf.Min(pal.starTint.r, pal.starTint.g, pal.starTint.b);
                Assert.Less(chroma, 0.1f, "background stars are neutral");
            }
            Assert.AreEqual(0.5, p.dustOutsideDim, 1e-9);
            Object.DestroyImmediate(p);
        }

        [Test]
        public void Detects_ColoredStarsOrColdDust()
        {
            var p = TestData.Params();
            p.palettes[0].starTint = new Color(0.6f, 0.8f, 1f);
            p.palettes[1].dustA = new Color(0.7f, 0.85f, 1f);
            var r = DataValidator.ValidateParams(p);
            Assert.IsTrue(r.Warnings.Exists(w => w.Contains("palettes[0] starTint should be neutral")), Joined(r.Warnings));
            Assert.IsTrue(r.Warnings.Exists(w => w.Contains("palettes[1] dust colors should be warm")), Joined(r.Warnings));
            Object.DestroyImmediate(p);
        }
    }
}
