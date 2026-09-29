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
            Assert.AreEqual(0.15, p.flashEdgeShare, 1e-9, "screen edge at 0.15 × the center (20 §6 W5-1: was 0.3)");
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

        // ---------------- ending: rules and save (15 §4.1, §4.2, §4.5) ----------------

        static RunRecord Rec(int run, string reason, double income, params int[] counts) =>
            new RunRecord { run = run, endReason = reason, income = income, tierCounts = counts };

        [Test]
        public void Ending_StartsOnlyWithTheLastTier_AndOnlyOnce()
        {
            var m = new MetaState();
            Assert.IsTrue(GameRoot.StartsEnding(m, 11, 11), "first blue giant");
            Assert.IsFalse(GameRoot.StartsEnding(m, 10, 11), "not the last tier");
            m.endingRun = 40;
            Assert.IsFalse(GameRoot.StartsEnding(m, 11, 11), "ending already reached: later blue giants are sky stars");
        }

        [Test]
        public void EndingRun_IsAFullRun_AndRecordsTheEnding()
        {
            var m = new MetaState();
            Session.ApplyRunEnd(m, Session.MakeRecord(m, 1, 60, 100, new[] { 3 }, null, default(EffectiveStats), EndReason.Stamina));
            var rec = Session.MakeRecord(m, 2, 50, 250, new[] { 1 }, null, default(EffectiveStats), EndReason.Ending);
            // 20 §6 (W5-3): the ending run is left out of the ratio and the best, like a quit run.
            Assert.AreEqual(-1, rec.ratioVsLast, 1e-9, "the ending run gets no ratio");
            Session.ApplyRunEnd(m, rec);
            Assert.AreEqual(100, m.lastRunIncome, 1e-9, "and does not move the ratio base");
            Assert.AreEqual(100, m.bestRunIncome, 1e-9, "nor the best");
            Assert.AreEqual(350, m.totalIncome, 1e-9, "its income still counts");
            Assert.AreEqual(2, m.endingRun);
            Assert.IsFalse(m.endingSeen);
            Assert.IsTrue(EndingView.IsUnseen(m));
            Assert.IsTrue(EndingView.PlaysFor(m, rec));
            Assert.AreSame(rec, m.EndingRecord);

            m.endingSeen = true;
            Assert.IsFalse(EndingView.PlaysFor(m, rec), "seen: the result screen from now on");
            Session.ApplyRunEnd(m, Rec(3, EndReason.Ending, 10));
            Assert.AreEqual(2, m.endingRun, "the first ending run stays");
            Assert.IsTrue(m.endingSeen);
        }

        [Test]
        public void EndingFields_RoundTrip_AndOldSavesReadDefaults()
        {
            var m = new MetaState { endingRun = 41, endingSeen = true };
            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(new SaveData { meta = m }));
            Assert.AreEqual(41, back.meta.endingRun);
            Assert.IsTrue(back.meta.endingSeen);

            // A week-4 save has neither field: JsonUtility keeps the defaults, so no version bump is needed.
            var old = JsonUtility.FromJson<SaveData>("{\"version\":2,\"meta\":{\"currency\":5,\"runCount\":3},\"pending\":{\"active\":false}}");
            Assert.AreEqual(0, old.meta.endingRun);
            Assert.IsFalse(old.meta.endingSeen);
            Assert.IsFalse(old.pending.ending);
            Assert.IsFalse(EndingView.IsUnseen(old.meta));
        }

        [Test]
        public void KilledDuringTheEnding_ClosesAsTheEndingRun()
        {
            var data = new SaveData();
            data.pending = Session.MakePending(7, 30, 90.4, new[] { 2 }, null, default(EffectiveStats), true);
            var rec = Session.FinalizePending(data, EndReason.Crash);
            Assert.AreEqual(EndReason.Ending, rec.endReason);
            Assert.AreEqual(91, rec.income, 1e-9, "income kept, rounded up once");
            Assert.AreEqual(7, data.meta.endingRun);
            Assert.IsTrue(EndingView.IsUnseen(data.meta), "the next launch plays the sequence");

            var plain = new SaveData();
            plain.pending = Session.MakePending(8, 30, 10, new[] { 2 }, null, default(EffectiveStats));
            Assert.AreEqual(EndReason.Crash, Session.FinalizePending(plain, EndReason.Crash).endReason);
            Assert.AreEqual(0, plain.meta.endingRun);
        }

        // ---------------- stages, palettes, black hole (15 §5) ----------------

        [Test]
        public void Stage_IsTheRemnantOnceTheEndingWasReached()
        {
            Assert.AreEqual(4, BackdropView.RemnantStage(11, 8));
            Assert.AreEqual(3, BackdropView.NaturalStage(0, 11, 8, 11), "blue giant before the ending");
            Assert.AreEqual(0, BackdropView.NaturalStage(0, 5, 8, 11));
            Assert.AreEqual(4, BackdropView.NaturalStage(40, 11, 8, 11), "remnant after the ending, seen or not");
            Assert.AreEqual(3, BackdropView.StarStage(14, 8, 4), "star stages stop below the remnant");
        }

        [Test]
        public void Defaults_HaveTheRemnantStage()
        {
            var p = TestData.Params();
            var c = TestData.Tiers(11);
            int stages = DataValidator.CentralStages(p, c);
            Assert.AreEqual(5, stages);
            Assert.AreEqual(stages, p.palettes.Length);
            Assert.AreEqual(stages, p.starCorePx.Length);
            Assert.AreEqual(stages, p.starGlowPx.Length);
            Assert.Greater(p.palettes[4].nebulaStrength, 0f, "remnant nebula");
            Assert.AreEqual(0f, p.palettes[3].nebulaStrength, "no nebula before the ending");
            Assert.AreEqual(SurfaceType.BlackHole, p.blackHoleLook.surface);
            Assert.IsTrue(p.blackHoleLook.HasRing, "accretion disc");
            Object.DestroyImmediate(p);
        }

        [Test]
        public void Detects_BrokenEndingParams()
        {
            var p = TestData.Params();
            p.supernovaBlastEndSec = p.supernovaCollapseEndSec;
            p.supernovaFlashAlpha = 0.5;
            p.blackHoleLook = new PlanetLook { surface = SurfaceType.Star, glowSize = 1, glowStrength = 1 };
            var r = DataValidator.ValidateParams(p);
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("supernova timeline must increase")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("supernovaFlashAlpha 0.5 is above flashAlphaCap")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("blackHoleLook must be a BlackHole look")), Joined(r.Errors));
            Object.DestroyImmediate(p);
        }

        [Test]
        public void Credits_ArePlaceholderKeys()
        {
            Assert.AreEqual(5, EndingView.CreditKeys.Length);
            foreach (var k in EndingView.CreditKeys) Assert.IsTrue(UIStrings.Has(k), k);
            Assert.AreEqual("(게임 제목, D7 미정)", UIStrings.Get("credits.title"));
            Assert.AreEqual("제작: (개발자 이름)", UIStrings.Get("credits.made_by"));
            Assert.AreEqual("Unity로 만들었습니다", UIStrings.Get("credits.engine"));
            Assert.AreEqual("플레이해 주셔서 감사합니다", UIStrings.Get("credits.thanks"));
        }

        // ---------------- ending pull (15 §4.2) ----------------

        [Test]
        public void EndingPull_TakesEveryDustInWithinThePullTime()
        {
            // A 1080p field full of dust, the star off-center: with the default pull every dust is absorbed in endingPullSec.
            var p = TestData.Params();
            var s = Stats.Compute(p, TestData.Nodes(), new MetaState());
            var area = new Rect(-9.6f, -5.4f, 19.2f, 10.8f);
            var go = new GameObject("dust");
            try
            {
                var f = go.AddComponent<DustField>();
                f.Init(p);
                f.Reseed(3);
                f.Reset(800, area, new Vector2(8f, 4f), s);
                float ppu = (float)p.pixelsPerUnit;
                var center = new Vector2(5f, 2.5f);
                float capture = 148f * 0.5f / ppu;
                double dt = 0.02;
                int ticks = (int)System.Math.Round(p.endingPullSec / dt);
                for (int i = 0; i < ticks; i++)
                    f.TickPull(dt, center, (float)(p.endingPullAccelPx / ppu), (float)(p.endingPullMaxSpeedPx / ppu), (float)p.endingPullDamping, capture);
                Assert.AreEqual(0, f.Count, "dust left after the pull");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(p);
            }
        }

        [Test]
        public void SnapshotPath_IsPerRing()
        {
            string path = GameRoot.SnapshotPath("C:/data", 7).Replace('\\', '/');
            Assert.AreEqual("C:/data/snapshots/ring_7.json", path);
        }
    }
}
