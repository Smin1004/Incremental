using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Incremental.Tests
{
    /// <summary>
    /// Playtest round 1 quick fixes (18 §A): the shortened planet departure and the comet tail shape. Both are data, so
    /// the tests check the project assets and the rules around them. (The stamina drain presets of A1 went with the
    /// stamina itself, 28 §2.)
    /// </summary>
    public class QuickFix1Tests
    {
        const double Eps = 1e-9;

        static GameParams ProjectParams() => AssetDatabase.LoadAssetAtPath<GameParams>("Assets/Game/Data/GameParams.asset");
        static CelestialTable ProjectTiers() => AssetDatabase.LoadAssetAtPath<CelestialTable>("Assets/Game/Data/CelestialTable.asset");

        // ---------------- A2 planet departure ----------------

        [Test]
        public void Departure_IsShorterThanBefore()
        {
            var p = ProjectParams();
            Assert.AreEqual(0.15, p.planetHoldSec, Eps, "stay in place (18 §A2)");
            Assert.AreEqual(0.45, p.planetDepartSec, Eps, "flight to the vanishing point (18 §A2)");
            double total = p.planetPopDurationSec + p.planetHoldSec + p.planetDepartSec;
            Assert.AreEqual(0.75, total, 1e-6, "pop + hold + departure (was 1.35)");
        }

        [Test]
        public void Departure_Validator_NeedsAPositiveDepartureTime()
        {
            var p = TestData.Params();
            p.planetDepartSec = 0;
            Assert.IsTrue(Has(DataValidator.ValidateParams(p), "planetDepartSec must be > 0"));
            p.planetDepartSec = 0.45;
            p.planetHoldSec = -1;
            Assert.IsTrue(Has(DataValidator.ValidateParams(p), "planetHoldSec must be >= 0"));
        }

        // ---------------- A3 comet tail ----------------

        [Test]
        public void CometTail_IsShortNarrowAndFadesOut()
        {
            var look = ProjectTiers().Get(2).look;
            Assert.AreEqual(3.5f, look.tailLength, 1e-4f, "tail length in radii (18 §A3)");
            Assert.AreEqual(1.1f, look.glowSize, 1e-4f, "coma glow (18 §A3)");

            // The shader defaults are the old cone; every tail number moved away from it.
            var old = new PlanetLook();
            Assert.Less(look.tailWidth, old.tailWidth, "narrower at the nucleus");
            Assert.Less(look.tailSpread, old.tailSpread, "widens more slowly");
            Assert.Greater(look.tailFadePower, old.tailFadePower, "the end dies out sooner");

            // Half width at the tail end, radii: it stays a tail instead of fanning into a cone.
            float endWidth = look.tailWidth + look.tailLength * look.tailSpread;
            float oldEndWidth = old.tailWidth + 6f * old.tailSpread; // the 6-radii cone it was
            Assert.Less(endWidth, oldEndWidth * 0.6f, "the tail end is far narrower than the old cone's");
            Assert.Less(endWidth, look.tailLength * 0.5f, "longer than it is wide");
        }

        [Test]
        public void CometTail_ShortensTheQuad()
        {
            var look = ProjectTiers().Get(2).look;
            Assert.Less(look.Extent(), 6f, "the quad follows the shorter tail (was 1 + 6 radii)");
            Assert.GreaterOrEqual(look.Extent(), 1f + look.tailLength, "and still covers it");
        }

        [Test]
        public void CometTail_Validator_NeedsAWidthAndAFade()
        {
            var c = TestData.Tiers();
            c.tiers[0].look.tailLength = 3.5f;
            c.tiers[0].look.tailWidth = 0f;
            c.tiers[1].look.tailLength = 3.5f;
            c.tiers[1].look.tailFadePower = 0f;
            var r = DataValidator.Validate(c, TestData.Nodes());
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("tier 1: look.tailWidth")), string.Join("\n", r.Errors));
            Assert.IsTrue(r.Errors.Exists(e => e.Contains("tier 2: look.tailFadePower")), string.Join("\n", r.Errors));
        }

        static bool Has(ValidationReport r, string text) => r.Errors.Exists(e => e.Contains(text));
    }
}
