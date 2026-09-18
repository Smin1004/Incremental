using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Incremental.Tests
{
    /// <summary>Week 4 visuals (14): look data and its validation, and the pure helpers behind the views.</summary>
    public class VisualTests
    {
        static string Joined(List<string> list) => string.Join("\n", list);

        // ---------------- look data (14 §3) ----------------

        [Test]
        public void ProjectLooks_CoverFourSurfaces_AndGrowWithTier()
        {
            var c = AssetDatabase.LoadAssetAtPath<CelestialTable>("Assets/Game/Data/CelestialTable.asset");
            var surfaces = new HashSet<SurfaceType>();
            for (int i = 0; i < c.tiers.Count; i++)
            {
                surfaces.Add(c.tiers[i].look.surface);
                if (i > 0) Assert.Greater(c.tiers[i].sizePx, c.tiers[i - 1].sizePx, "display size grows with the tier");
            }
            Assert.AreEqual(4, surfaces.Count);
            Assert.Greater(c.Get(2).look.tailLength, 0f, "comet tail (14 §3)");
            Assert.IsTrue(c.Get(7).look.HasRing, "gas giant ring (14 §3)");
            Assert.AreEqual(0f, c.Get(2).look.spinDegPerSec, "comet does not spin");
            for (int t = 9; t <= 11; t++) Assert.AreEqual(SurfaceType.Star, c.Get(t).look.surface);
            for (int t = 5; t <= 7; t++) Assert.Greater(c.Get(t).look.spinDegPerSec, 0f, "tiers 5-7 spin visibly (14 §9)");
            Assert.Greater(c.Get(7).look.spotSize, 0f, "bands alone do not show the spin: the gas giant has a storm spot");
        }

        [Test]
        public void ProjectParams_PassValidation()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameParams>("Assets/Game/Data/GameParams.asset");
            var c = AssetDatabase.LoadAssetAtPath<CelestialTable>("Assets/Game/Data/CelestialTable.asset");
            var r = DataValidator.ValidateParams(p, c);
            Assert.IsEmpty(r.Errors, Joined(r.Errors));
            Assert.IsEmpty(r.Warnings, Joined(r.Warnings));
        }

        [Test]
        public void ProjectParams_MatchCodeDefaults()
        {
            // GameParams initializers are the seed; the asset must hold the same values (Validate Data warns otherwise).
            var asset = AssetDatabase.LoadAssetAtPath<GameParams>("Assets/Game/Data/GameParams.asset");
            var fresh = ScriptableObject.CreateInstance<GameParams>();
            var diffs = new List<string>();
            var a = new SerializedObject(asset);
            var it = new SerializedObject(fresh).GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.propertyPath == "m_Script") continue;
                var ap = a.FindProperty(it.propertyPath);
                if (ap == null || !SerializedProperty.DataEquals(ap, it)) diffs.Add(it.propertyPath);
            }
            Object.DestroyImmediate(fresh);
            Assert.IsEmpty(diffs, string.Join(", ", diffs));
        }

        [Test]
        public void Look_Extent_CoversGlowTailAndRing()
        {
            var l = new PlanetLook();
            Assert.AreEqual(1f, l.Extent(), 0.1f);
            l.glowSize = 0.5f;
            l.glowStrength = 1f;
            Assert.GreaterOrEqual(l.Extent(), 1.5f);
            l.tailLength = 4f;
            Assert.GreaterOrEqual(l.Extent(), 5f);
            l.tailLength = 0f;
            l.ringInner = 1.3f;
            l.ringOuter = 2.4f;
            Assert.GreaterOrEqual(l.Extent(), 2.4f);
        }

        [Test]
        public void Detects_BrokenLooks()
        {
            var c = TestData.Tiers();
            c.tiers[0].look.ringInner = 0.8f;
            c.tiers[0].look.ringOuter = 2f;
            c.tiers[1].look.surface = SurfaceType.Gas;
            c.tiers[1].look.bandCount = 0f;
            c.tiers[2].look.noiseScale = 0f;
            c.tiers[3].look = null;
            var r = DataValidator.Validate(c, TestData.Nodes());
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("tier 1: look.ringInner")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("tier 2: Gas look needs bandCount")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("tier 3: look.noiseScale")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("tier 4: look is missing")), Joined(r.Errors));
        }

        [Test]
        public void Warns_ShrinkingDisplaySize()
        {
            var c = TestData.Tiers();
            c.tiers[2].sizePx = c.tiers[1].sizePx - 1;
            var r = DataValidator.Validate(c, TestData.Nodes());
            Assert.IsEmpty(r.Errors, Joined(r.Errors));
            Assert.IsTrue(r.Warnings.Exists(w => w.Contains("tier 3 sizePx")), Joined(r.Warnings));
        }

        // ---------------- forming body (14 §4) ----------------

        [Test]
        public void FormingSize_GrowsFromCurrentToNextTierSize()
        {
            // Tier 3 (threshold 60, 34 px) toward tier 4 (threshold 150, 42 px).
            Assert.AreEqual(34, CursorView.FormingSizePx(60, 60, 34, 150, 42), 1e-9);
            Assert.AreEqual(38, CursorView.FormingSizePx(105, 60, 34, 150, 42), 1e-9);
            Assert.AreEqual(42, CursorView.FormingSizePx(150, 60, 34, 150, 42), 1e-9);
            Assert.AreEqual(42, CursorView.FormingSizePx(400, 60, 34, 150, 42), 1e-9, "clamped at the next size");
            // Below tier 1: the clump grows from its min to its max size up to the tier 1 threshold.
            Assert.AreEqual(6, CursorView.FormingSizePx(0, 0, 6, 10, 16), 1e-9);
            Assert.AreEqual(11, CursorView.FormingSizePx(5, 0, 6, 10, 16), 1e-9);
            // No next tier (top unlocked): the size stays.
            Assert.AreEqual(50, CursorView.FormingSizePx(999, 400, 50, 400, 60), 1e-9);
        }

        [Test]
        public void ProjectParams_ClumpIsSmallerThanTier1()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameParams>("Assets/Game/Data/GameParams.asset");
            var c = AssetDatabase.LoadAssetAtPath<CelestialTable>("Assets/Game/Data/CelestialTable.asset");
            Assert.Less(p.formingClumpMaxPx, c.Get(1).sizePx, "tier 1 pops out of the clump");
            Assert.AreEqual(SurfaceType.Rock, p.formingClumpLook.surface);
        }

        // ---------------- dust (14 §5) ----------------

        [Test]
        public void DustVariation_DoesNotChangeTheSimulation()
        {
            // Two fields with the same seed must place and move dust identically; the render-only size and brightness
            // come from a separate RNG, so they are allowed to differ.
            var p = TestData.Params();
            var s = Stats.Compute(p, TestData.Nodes(), new MetaState());
            var area = new Rect(-9.6f, -5.4f, 19.2f, 10.8f);
            var go1 = new GameObject("a");
            var go2 = new GameObject("b");
            try
            {
                var a = go1.AddComponent<DustField>();
                var b = go2.AddComponent<DustField>();
                a.Init(p);
                b.Init(p);
                a.Reseed(5);
                b.Reseed(5);
                a.Reset(50, area, Vector2.zero, s);
                b.Reset(50, area, Vector2.zero, s);
                for (int i = 0; i < 30; i++)
                {
                    a.Tick(0.02, area, Vector2.zero, true, s);
                    b.Tick(0.02, area, Vector2.zero, true, s);
                }
                Assert.AreEqual(a.Count, b.Count);
                for (int i = 0; i < a.Count; i++)
                {
                    Assert.AreEqual(a.Items[i].pos, b.Items[i].pos);
                    Assert.AreEqual(a.Items[i].tint, b.Items[i].tint);
                    Assert.That(a.Items[i].size, Is.InRange(1f - (float)p.dustSizeJitter, 1f + (float)p.dustSizeJitter));
                    Assert.That(a.Items[i].bright, Is.InRange(1f - (float)p.dustBrightJitter, 1f + (float)p.dustBrightJitter));
                }
            }
            finally
            {
                Object.DestroyImmediate(go1);
                Object.DestroyImmediate(go2);
                Object.DestroyImmediate(p);
            }
        }

        // ---------------- background and ignition (14 §6) ----------------

        [Test]
        public void StarStage_FollowsTheHighestStarTier()
        {
            Assert.AreEqual(0, BackdropView.StarStage(0, 8, 4));
            Assert.AreEqual(0, BackdropView.StarStage(8, 8, 4), "brown dwarf is not a star");
            Assert.AreEqual(1, BackdropView.StarStage(9, 8, 4), "red dwarf");
            Assert.AreEqual(2, BackdropView.StarStage(10, 8, 4), "sun-like");
            Assert.AreEqual(3, BackdropView.StarStage(11, 8, 4), "blue giant");
            Assert.AreEqual(3, BackdropView.StarStage(14, 8, 4), "capped at the last palette");
        }

        [Test]
        public void Ignition_FirstOnlyOnce()
        {
            Assert.IsTrue(IgnitionFx.IsStarTier(9, 8));
            Assert.IsFalse(IgnitionFx.IsStarTier(8, 8));
            Assert.IsTrue(IgnitionFx.IsFirstIgnition(8, false, 8), "no star before: first ignition");
            Assert.IsFalse(IgnitionFx.IsFirstIgnition(8, true, 8), "a star earlier this run");
            Assert.IsFalse(IgnitionFx.IsFirstIgnition(9, false, 8), "a star in an earlier run");
        }

        [Test]
        public void Detects_MissingPalettes()
        {
            var p = TestData.Params();
            p.palettes = new[] { new BackdropPalette(), new BackdropPalette() };
            var c = TestData.Tiers(11);
            var r = DataValidator.ValidateParams(p, c);
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("2 palettes for 4 central star stages")), Joined(r.Errors));
            Object.DestroyImmediate(p);
        }

        // ---------------- result growth highlight (14 §7) ----------------

        static RunRecord Rec(int run, string reason, params int[] counts) =>
            new RunRecord { run = run, endReason = reason, tierCounts = counts };

        [Test]
        public void ComparisonRun_IsTheLastStaminaRunBefore()
        {
            var r1 = Rec(1, EndReason.Stamina, 10);
            var r2 = Rec(2, EndReason.Stamina, 14, 1);
            var r3 = Rec(3, EndReason.Quit, 3);
            var r4 = Rec(4, EndReason.Stamina, 12, 4);
            var history = new List<RunRecord> { r1, r2, r3, r4 };
            Assert.AreSame(r2, ResultMath.ComparisonRun(history, r4), "quit runs are skipped, like the ratio");
            Assert.AreSame(r2, ResultMath.ComparisonRun(history, r3));
            Assert.IsNull(ResultMath.ComparisonRun(history, r1), "first run: nothing to compare");
            Assert.AreEqual(-2, ResultMath.Delta(r4, r2, 1));
            Assert.AreEqual(3, ResultMath.Delta(r4, r2, 2));
            Assert.AreEqual(0, ResultMath.Delta(r4, null, 2));
        }

        [Test]
        public void NewTier_UsesTotalsBeforeAndAfterTheRun()
        {
            var meta = new MetaState();
            var r1 = Rec(1, EndReason.Stamina, 10);
            Session.ApplyRunEnd(meta, r1);
            Assert.IsTrue(ResultMath.IsNewTier(meta, r1, 1), "first asteroid ever");
            var r2 = Rec(2, EndReason.Stamina, 6, 2);
            Session.ApplyRunEnd(meta, r2);
            Assert.IsFalse(ResultMath.IsNewTier(meta, r2, 1), "asteroids were made before");
            Assert.IsTrue(ResultMath.IsNewTier(meta, r2, 2), "first comet ever");
            Assert.IsFalse(ResultMath.IsNewTier(meta, r2, 3), "not made");
        }

        [Test]
        public void Detects_BadParams()
        {
            var p = TestData.Params();
            p.planetAxisPitchDeg = 0;
            p.effectIntensity = 2;
            var r = DataValidator.ValidateParams(p);
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("planetAxisPitchDeg")), Joined(r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("effectIntensity")), Joined(r.Errors));
            Object.DestroyImmediate(p);
        }
    }
}
