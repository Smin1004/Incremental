using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Incremental.Tests
{
    /// <summary>
    /// Playtest round 1 quick fixes (18 §A): the stamina drain values and their debug preset cycle, the shortened planet
    /// departure, and the comet tail shape. All three are data, so the tests check the project assets and the rules around them.
    /// </summary>
    public class QuickFix1Tests
    {
        const double Eps = 1e-9;

        static GameParams ProjectParams() => AssetDatabase.LoadAssetAtPath<GameParams>("Assets/Game/Data/GameParams.asset");
        static CelestialTable ProjectTiers() => AssetDatabase.LoadAssetAtPath<CelestialTable>("Assets/Game/Data/CelestialTable.asset");

        // ---------------- A1 stamina ----------------

        [Test]
        public void Stamina_ProjectDrains_AreTheProposedPreset()
        {
            var p = ProjectParams();
            Assert.AreEqual(1.5, p.staminaHoldDrain, Eps, "hold drain (18 §A1)");
            Assert.AreEqual(0.3, p.staminaIdleDrain, Eps, "idle drain (18 §A1)");
            int i = p.StaminaPresetIndex(p.staminaHoldDrain, p.staminaIdleDrain);
            Assert.GreaterOrEqual(i, 0, "the asset drains must be one of the presets");
            Assert.AreEqual("proposed", p.staminaPresets[i].id);
        }

        [Test]
        public void Stamina_ThreePresetsRiseTogether_AndEachHasAName()
        {
            var p = ProjectParams();
            Assert.AreEqual(3, p.staminaPresets.Length, "loose / proposed / fast (18 §A1)");
            for (int i = 0; i < p.staminaPresets.Length; i++)
            {
                var s = p.staminaPresets[i];
                Assert.IsTrue(UIStrings.Has(UIStrings.StaminaPresetKey(s.id)), "name for preset '" + s.id + "'");
                if (i == 0) continue;
                Assert.Greater(s.holdDrain, p.staminaPresets[i - 1].holdDrain, "hold drain rises through the presets");
                Assert.Greater(s.idleDrain, p.staminaPresets[i - 1].idleDrain, "idle drain rises through the presets");
            }
        }

        [Test]
        public void Stamina_PresetCycle_WrapsAround()
        {
            var p = ProjectParams();
            var loose = p.staminaPresets[0];
            var proposed = p.staminaPresets[1];
            var fast = p.staminaPresets[2];

            // From the asset value (the proposed preset) the key goes to fast, then wraps to loose.
            Assert.AreSame(fast, p.NextStaminaPreset(p.staminaHoldDrain, p.staminaIdleDrain));
            Assert.AreSame(loose, p.NextStaminaPreset(fast.holdDrain, fast.idleDrain));
            Assert.AreSame(proposed, p.NextStaminaPreset(loose.holdDrain, loose.idleDrain));
        }

        [Test]
        public void Stamina_PresetCycle_FromAnUnknownValue_StartsAtTheFirstPreset()
        {
            var p = ProjectParams();
            Assert.AreEqual(-1, p.StaminaPresetIndex(1.234, 0.567));
            Assert.AreSame(p.staminaPresets[0], p.NextStaminaPreset(1.234, 0.567));
        }

        [Test]
        public void Stamina_PresetOverride_KeepsTheNodeReduction()
        {
            var p = TestData.Params();
            var t = TestData.Nodes();
            var m = new MetaState();
            m.SetLevel("c_drain", 1); // stamina_drain −50%

            var asset = Stats.Compute(p, t, m);
            Assert.AreEqual(1.0 * 0.5, asset.staminaHoldDrain, Eps);
            Assert.AreEqual(0.2 * 0.5, asset.staminaIdleDrain, Eps);

            var preset = Stats.Compute(p, t, m, 2.0, 0.4);
            Assert.AreEqual(2.0 * 0.5, preset.staminaHoldDrain, Eps, "the preset replaces the base, not the reduction");
            Assert.AreEqual(0.4 * 0.5, preset.staminaIdleDrain, Eps);
            Assert.AreEqual(asset.staminaMax, preset.staminaMax, Eps, "nothing else changes");
            Assert.AreEqual(1.0, p.staminaHoldDrain, Eps, "the asset is untouched");
        }

        [Test]
        public void Stamina_Validator_NeedsPresetsWithNamesAndPositiveDrains()
        {
            var p = TestData.Params();
            p.staminaPresets = new StaminaPreset[0];
            Assert.IsTrue(Has(DataValidator.ValidateParams(p), "staminaPresets is empty"));

            p.staminaPresets = new[]
            {
                new StaminaPreset { id = "loose", holdDrain = 1.0, idleDrain = 0.2 },
                new StaminaPreset { id = "loose", holdDrain = 1.5, idleDrain = 0.3 },
            };
            Assert.IsTrue(Has(DataValidator.ValidateParams(p), "appears twice"));

            p.staminaPresets = new[] { new StaminaPreset { id = "nonesuch", holdDrain = 1.0, idleDrain = 0.2 } };
            Assert.IsTrue(Has(DataValidator.ValidateParams(p), "stamina.preset.nonesuch"));

            p.staminaPresets = new[] { new StaminaPreset { id = "loose", holdDrain = 0, idleDrain = 0.2 } };
            Assert.IsTrue(Has(DataValidator.ValidateParams(p), "drains must be > 0"));
        }

        [Test]
        public void Stamina_Validator_WarnsWhenTheAssetDrainsAreNoPreset()
        {
            var p = TestData.Params();
            p.staminaHoldDrain = 1.23;
            var r = DataValidator.ValidateParams(p);
            Assert.IsTrue(r.Warnings.Exists(w => w.Contains("not one of staminaPresets")), string.Join("\n", r.Warnings));
        }

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
