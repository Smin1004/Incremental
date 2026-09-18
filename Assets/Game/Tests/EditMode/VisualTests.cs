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
        }

        [Test]
        public void ProjectParams_PassValidation()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameParams>("Assets/Game/Data/GameParams.asset");
            var r = DataValidator.ValidateParams(p);
            Assert.IsEmpty(r.Errors, Joined(r.Errors));
            Assert.IsEmpty(r.Warnings, Joined(r.Warnings));
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
