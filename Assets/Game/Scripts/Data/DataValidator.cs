using System;
using System.Collections.Generic;

namespace Incremental
{
    /// <summary>Result of <see cref="DataValidator.Validate"/>: errors break the game, warnings are balance switches worth a look.</summary>
    public sealed class ValidationReport
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool Ok => Errors.Count == 0;
    }

    /// <summary>
    /// Consistency checks for the celestial table and the skill tree (12 §3). Used by the editor menu Incremental/Validate Data
    /// and by the EditMode tests.
    /// </summary>
    public static class DataValidator
    {
        /// <summary>Two nodes of the same ring closer than this (degrees) overlap in the tree view.</summary>
        public const float MinAngleGapDeg = 5f;

        /// <summary>
        /// With <paramref name="p"/> the dust kind nodes are checked against GameParams.dustKinds too (20 §4); without it
        /// (the small test tables) that part is skipped.
        /// </summary>
        public static ValidationReport Validate(CelestialTable c, NodeTable t, GameParams p = null)
        {
            var r = new ValidationReport();
            if (c == null) r.Errors.Add("CelestialTable is missing.");
            if (t == null) r.Errors.Add("NodeTable is missing.");
            if (!r.Ok) return r;

            ValidateTiers(c, r);
            foreach (var key in EndingView.CreditKeys)
                if (!UIStrings.Has(key)) r.Errors.Add($"UIStrings: missing key '{key}' (credits, 15 §4.4).");
            ValidateNodes(c, t, r);
            ValidateGates(c, t, r);
            ValidateGraph(t, r);
            ValidateAngles(t, r);
            ValidateRingCostMult(c, t, r);
            if (p != null) ValidateDustNodes(p, t, r);
            return r;
        }

        /// <summary>
        /// The dust kind axis (20 §4): every kind but the base has exactly one unlock node, in the ring its data names,
        /// every unlock and chance node points at a kind that exists, and no node raises the retired compound dust mass.
        /// </summary>
        static void ValidateDustNodes(GameParams p, NodeTable t, ValidationReport r)
        {
            foreach (var n in t.nodes)
            {
                if (n == null) continue;
                if (n.statId == StatIds.DustMass && n.kind == NodeKind.Stat && n.enabled)
                    r.Errors.Add($"NodeTable: '{n.id}' raises dust_mass; the compound dust mass nodes were replaced by the dust kind axis (20 §4).");
                bool needsKind = n.IsDustUnlock || (n.kind == NodeKind.Stat && StatIds.NeedsDustKind(n.statId));
                if (!needsKind)
                {
                    if (!string.IsNullOrEmpty(n.dustKindId))
                        r.Warnings.Add($"NodeTable: '{n.id}' names dust kind '{n.dustKindId}' but does not use it.");
                    continue;
                }
                int k = p.DustKindIndex(n.dustKindId);
                if (k <= 0)
                {
                    r.Errors.Add($"NodeTable: '{n.id}' names dust kind '{n.dustKindId}', which is not one of GameParams.dustKinds (the base dust cannot be unlocked).");
                    continue;
                }
                if (n.IsDustUnlock)
                {
                    if (n.maxLevel != 1) r.Errors.Add($"NodeTable: dust kind unlock '{n.id}' must have maxLevel 1.");
                    if (n.minTier != p.DustKind(k).unlockRing)
                        r.Errors.Add($"NodeTable: '{n.id}' is in ring {n.minTier}, but dust kind '{n.dustKindId}' says ring {p.DustKind(k).unlockRing} (20 §4).");
                }
                else if (n.maxLevel != p.DustKind(k).chanceMaxLevel)
                    r.Errors.Add($"NodeTable: '{n.id}' has {n.maxLevel} levels, but dust kind '{n.dustKindId}' says {p.DustKind(k).chanceMaxLevel} (20 §4).");
            }
            for (int k = 1; k < p.DustKindCount; k++)
            {
                var def = p.DustKind(k);
                if (def == null || string.IsNullOrEmpty(def.id)) continue;
                int unlocks = 0, chances = 0;
                foreach (var n in t.nodes)
                {
                    if (n == null || n.dustKindId != def.id) continue;
                    if (n.IsDustUnlock) unlocks++;
                    else if (n.statId == StatIds.DustChance) chances++;
                }
                if (unlocks != 1)
                    r.Errors.Add($"NodeTable: dust kind '{def.id}' has {unlocks} unlock nodes, expected exactly one (20 §4).");
                if (chances != 1 && def.chancePerLevel > 0)
                    r.Warnings.Add($"NodeTable: dust kind '{def.id}' has {chances} chance nodes, expected one (20 §4).");
            }
        }

        // Tiers: 1..N contiguous ascending, required mass strictly increasing, positive price, string key present.
        static void ValidateTiers(CelestialTable c, ValidationReport r)
        {
            if (c.tiers.Count == 0) r.Errors.Add("CelestialTable: no tiers.");
            for (int i = 0; i < c.tiers.Count; i++)
            {
                var tier = c.tiers[i];
                if (tier.tier != i + 1)
                    r.Errors.Add($"CelestialTable: entry {i} has tier {tier.tier}, expected {i + 1} (tiers must start at 1, contiguous, ascending).");
                if (i > 0 && tier.requiredMass <= c.tiers[i - 1].requiredMass)
                    r.Errors.Add($"CelestialTable: tier {tier.tier} requiredMass {tier.requiredMass} is not greater than tier {c.tiers[i - 1].tier} ({c.tiers[i - 1].requiredMass}).");
                if (tier.requiredMass <= 0) r.Errors.Add($"CelestialTable: tier {tier.tier} requiredMass must be > 0.");
                if (tier.salePrice <= 0) r.Errors.Add($"CelestialTable: tier {tier.tier} salePrice must be > 0.");
                if (!UIStrings.Has(UIStrings.TierKey(tier.tier)))
                    r.Errors.Add($"UIStrings: missing key '{UIStrings.TierKey(tier.tier)}'.");
                if (tier.sizePx <= 0) r.Errors.Add($"CelestialTable: tier {tier.tier} sizePx must be > 0.");
                else if (i > 0 && tier.sizePx <= c.tiers[i - 1].sizePx)
                    r.Warnings.Add($"CelestialTable: tier {tier.tier} sizePx {tier.sizePx} is not larger than tier {c.tiers[i - 1].tier}; the forming body shrinks at that step.");
                ValidateLook("CelestialTable: tier " + tier.tier, tier.look, r);
            }
        }

        /// <summary>Planet shader look ranges (14 §3). Also used for the looks in GameParams.</summary>
        public static void ValidateLook(string where, PlanetLook l, ValidationReport r)
        {
            if (l == null)
            {
                r.Errors.Add(where + ": look is missing.");
                return;
            }
            if (!(l.noiseScale > 0)) r.Errors.Add(where + ": look.noiseScale must be > 0.");
            if (l.glowSize < 0 || l.glowStrength < 0) r.Errors.Add(where + ": look.glowSize and glowStrength must be >= 0.");
            if (l.tailLength < 0) r.Errors.Add(where + ": look.tailLength must be >= 0.");
            if (l.tailLength > 0)
            {
                // 18 §A3: a tail needs a width, may only widen, and must fade toward its end.
                if (!(l.tailWidth > 0)) r.Errors.Add(where + ": look.tailWidth must be > 0 when there is a tail.");
                if (l.tailSpread < 0) r.Errors.Add(where + ": look.tailSpread must be >= 0.");
                if (!(l.tailFadePower > 0)) r.Errors.Add(where + ": look.tailFadePower must be > 0.");
            }
            if (l.patchLevel < 0 || l.patchLevel > 1 || l.patchSoftness < 0 || l.patchSoftness > 1)
                r.Errors.Add(where + ": look.patchLevel and patchSoftness must be within 0..1.");
            if (l.craterDensity < 0 || l.craterDensity > 1) r.Errors.Add(where + ": look.craterDensity must be within 0..1.");
            if (l.craterDensity > 0 && (!(l.craterSize > 0) || !(l.craterScale > 0)))
                r.Errors.Add(where + ": look has craters but craterSize or craterScale is not > 0.");
            if (l.polarCap < 0 || l.polarCap > 1) r.Errors.Add(where + ": look.polarCap must be within 0..1.");
            if (l.irregularity < 0 || l.irregularity > 0.6f) r.Errors.Add(where + ": look.irregularity must be within 0..0.6.");
            if (l.cloudCover < 0 || l.cloudCover > 1) r.Errors.Add(where + ": look.cloudCover must be within 0..1.");
            if (l.emission < 0 || l.emission > 1) r.Errors.Add(where + ": look.emission must be within 0..1.");
            if (float.IsNaN(l.spinDegPerSec) || float.IsInfinity(l.spinDegPerSec)) r.Errors.Add(where + ": look.spinDegPerSec is not a number.");
            if (l.HasRing)
            {
                if (!(l.ringInner > 1f)) r.Errors.Add(where + ": look.ringInner must be > 1 (outside the disc).");
                if (!(l.ringOuter > l.ringInner)) r.Errors.Add(where + ": look.ringOuter must be > ringInner.");
                if (!(l.ringColor.a > 0)) r.Warnings.Add(where + ": look has a ring with alpha 0 (invisible).");
            }
            else if (l.ringOuter < 0) r.Errors.Add(where + ": look.ringOuter must be >= 0.");
            if (l.surface == SurfaceType.Gas && !(l.bandCount > 0)) r.Errors.Add(where + ": Gas look needs bandCount > 0.");
            if (l.spotSize < 0 || l.spotSize > 0.5f || l.spotLatitude < -1 || l.spotLatitude > 1)
                r.Errors.Add(where + ": look.spotSize must be within 0..0.5 and spotLatitude within -1..1.");
            if (l.surface == SurfaceType.Star && !(l.glowStrength > 0 && l.glowSize > 0)) r.Warnings.Add(where + ": Star look without a corona.");
            if (l.surface == SurfaceType.BlackHole && !l.HasRing) r.Warnings.Add(where + ": BlackHole look without an accretion disc (ring).");
            if (l.surface == SurfaceType.Terran && l.cloudCover > 0 && l.cloudSpinMult == 0) r.Warnings.Add(where + ": clouds do not move (cloudSpinMult 0).");
        }

        /// <summary>
        /// Presentation parameters (14): ranges that would break the shaders or the views. With the celestial table, also
        /// checks that there is a background palette for every central star stage.
        /// </summary>
        public static ValidationReport ValidateParams(GameParams p, CelestialTable c = null)
        {
            var r = new ValidationReport();
            if (p == null)
            {
                r.Errors.Add("GameParams is missing.");
                return r;
            }
            ValidateStamina(p, r);
            ValidatePull(p, r);
            ValidateDustKinds(p, r);
            ValidateFragments(p, r);
            // 18 §A2: the pop, the stay in place and the flight to the vanishing point.
            if (p.planetPopDurationSec < 0 || p.planetHoldSec < 0) r.Errors.Add("GameParams: planetPopDurationSec and planetHoldSec must be >= 0.");
            if (!(p.planetDepartSec > 0)) r.Errors.Add("GameParams: planetDepartSec must be > 0.");
            if (p.planetAmbient < 0 || p.planetAmbient > 1) r.Errors.Add("GameParams: planetAmbient must be within 0..1.");
            if (!(p.planetLightZ >= 0)) r.Errors.Add("GameParams: planetLightZ must be >= 0.");
            if (!(p.planetDetailPx > 0)) r.Errors.Add("GameParams: planetDetailPx must be > 0.");
            if (p.planetAxisPitchDeg <= 1 || p.planetAxisPitchDeg >= 89) r.Errors.Add("GameParams: planetAxisPitchDeg must be within 1..89 (rings need a visible tilt).");
            if (!(p.galleryZoomMin > 0) || !(p.galleryZoomMax >= p.galleryZoomMin)) r.Errors.Add("GameParams: gallery zoom range is invalid.");
            if (p.effectIntensity < 0 || p.effectIntensity > 1) r.Errors.Add("GameParams: effectIntensity must be within 0..1.");
            ValidateLook("GameParams: formingClumpLook", p.formingClumpLook, r);
            if (!(p.formingClumpMinPx > 0) || !(p.formingClumpMaxPx >= p.formingClumpMinPx))
                r.Errors.Add("GameParams: formingClumpMinPx must be > 0 and formingClumpMaxPx >= formingClumpMinPx.");
            if (!(p.formingSizeLerpRate > 0)) r.Errors.Add("GameParams: formingSizeLerpRate must be > 0.");
            if (!(p.dustHaloPx > 0) || !(p.dustCorePx > 0) || p.dustCorePx > p.dustHaloPx)
                r.Errors.Add("GameParams: dust core and halo must be > 0 with the core inside the halo.");
            if (p.dustSizeJitter < 0 || p.dustSizeJitter >= 1 || p.dustBrightJitter < 0 || p.dustBrightJitter >= 1)
                r.Errors.Add("GameParams: dust jitter must be within 0..1 (exclusive).");
            if (!(p.dustStretchMax >= 1)) r.Errors.Add("GameParams: dustStretchMax must be >= 1.");
            if (!(p.dustStretchFullSpeedPx > p.driftSpeed)) r.Errors.Add("GameParams: dustStretchFullSpeedPx must be above driftSpeed.");

            if (p.palettes == null || p.palettes.Length == 0) r.Errors.Add("GameParams: palettes is empty (14 §6).");
            else
            {
                if (c != null)
                {
                    int stages = CentralStages(p, c);
                    if (p.palettes.Length < stages)
                        r.Errors.Add($"GameParams: {p.palettes.Length} palettes for {stages} central star stages (protostar + one per star tier + remnant, 15 §5).");
                }
                for (int i = 0; i < p.palettes.Length; i++)
                {
                    var pal = p.palettes[i];
                    if (pal == null)
                    {
                        r.Errors.Add($"GameParams: palettes[{i}] is missing.");
                        continue;
                    }
                    if (pal.glowStrength < 0 || !(pal.glowRadiusPx > 0)) r.Errors.Add($"GameParams: palettes[{i}] glow strength must be >= 0 and radius > 0.");
                    // 15 §3: dust takes a warm hue, background stars stay neutral, so the two never read alike.
                    if (!IsWarm(pal.dustA) || !IsWarm(pal.dustB)) r.Warnings.Add($"GameParams: palettes[{i}] dust colors should be warm (red above blue), unlike the neutral stars (15 §3).");
                    if (Chroma(pal.starTint) > 0.1f) r.Warnings.Add($"GameParams: palettes[{i}] starTint should be neutral white (15 §3).");
                }
            }
            if (!(p.paletteBlendSec > 0)) r.Errors.Add("GameParams: paletteBlendSec must be > 0.");
            if (!(p.bgStarLayerA.x >= 4) || !(p.bgStarLayerB.x >= 4)) r.Errors.Add("GameParams: background star cells must be >= 4 px.");
            if (p.bgStarLayerA.y < 0 || p.bgStarLayerA.y > 1 || p.bgStarLayerB.y < 0 || p.bgStarLayerB.y > 1)
                r.Errors.Add("GameParams: background star density must be within 0..1.");
            if (!(p.ignitionFirstSec > 0) || !(p.ignitionRepeatSec > 0)) r.Errors.Add("GameParams: ignition durations must be > 0.");
            if (p.ignitionFlashAlpha < 0 || p.ignitionFlashAlpha > 1 || p.ignitionRepeatFlashAlpha < 0 || p.ignitionRepeatFlashAlpha > 1)
                r.Errors.Add("GameParams: ignition flash alphas must be within 0..1.");
            if (!(p.ignitionFlashRiseSec > 0) || !(p.ignitionFlashRiseSec < p.ignitionRepeatSec))
                r.Errors.Add("GameParams: ignitionFlashRiseSec must be > 0 and shorter than ignitionRepeatSec.");
            ValidateFlashRule(p, r);
            if (p.dustOutsideDim < 0 || p.dustOutsideDim > 1) r.Errors.Add("GameParams: dustOutsideDim must be within 0..1.");
            if (!(p.dustDimEdgePx > 0) || !(p.dustHoldFadeSec > 0)) r.Errors.Add("GameParams: dustDimEdgePx and dustHoldFadeSec must be > 0.");
            if (!(p.dustStreakWidth > 0) || p.dustStreakWidth > 1) r.Errors.Add("GameParams: dustStreakWidth must be within 0..1 (0 excluded).");
            if (!(p.orbitBeltDotBrightness > 0)) r.Errors.Add("GameParams: orbitBeltDotBrightness must be > 0.");
            if (p.newTierStarPulseScale < 0) r.Errors.Add("GameParams: newTierStarPulseScale must be >= 0.");

            ValidateLook("GameParams: protostarLook", p.protostarLook, r);
            if (p.protostarLook != null && p.protostarLook.surface != SurfaceType.Star)
                r.Errors.Add("GameParams: protostarLook must be a Star look (14 §7).");
            if (p.starCorePx == null || p.starCorePx.Length == 0 || p.starGlowPx == null || p.starGlowPx.Length == 0)
                r.Errors.Add("GameParams: starCorePx and starGlowPx need one entry per central star stage.");
            else if (c != null)
            {
                int stages = CentralStages(p, c);
                if (p.starCorePx.Length < stages || p.starGlowPx.Length < stages)
                    r.Warnings.Add($"GameParams: starCorePx / starGlowPx have fewer than {stages} entries; later stages reuse the last one.");
            }
            if (!(p.skyStarScale > 0) || !(p.skyStarArea.z > p.skyStarArea.x) || !(p.skyStarArea.w > p.skyStarArea.y))
                r.Errors.Add("GameParams: skyStarScale must be > 0 and skyStarArea must have max > min.");
            if (!(p.newTierHighlightSec > 0) || p.newTierPulses < 1) r.Errors.Add("GameParams: newTierHighlightSec must be > 0 and newTierPulses >= 1.");
            ValidateEnding(p, r);
            return r;
        }

        /// <summary>
        /// Stamina (spec §6) and the debug key's drain presets (18 §A1): positive drains, a display name per preset id, and
        /// the asset's own drains must be one of the presets so the key starts from a known one.
        /// </summary>
        static void ValidateStamina(GameParams p, ValidationReport r)
        {
            if (!(p.staminaMax > 0)) r.Errors.Add("GameParams: staminaMax must be > 0.");
            if (!(p.staminaHoldDrain > 0) || !(p.staminaIdleDrain > 0))
                r.Errors.Add("GameParams: staminaHoldDrain and staminaIdleDrain must be > 0.");
            if (p.staminaPresets == null || p.staminaPresets.Length == 0)
            {
                r.Errors.Add("GameParams: staminaPresets is empty (the debug key has nothing to cycle, 18 §A1).");
                return;
            }
            var ids = new HashSet<string>();
            for (int i = 0; i < p.staminaPresets.Length; i++)
            {
                var s = p.staminaPresets[i];
                if (s == null)
                {
                    r.Errors.Add($"GameParams: staminaPresets[{i}] is missing.");
                    continue;
                }
                if (string.IsNullOrEmpty(s.id)) r.Errors.Add($"GameParams: staminaPresets[{i}] has no id.");
                else if (!ids.Add(s.id)) r.Errors.Add($"GameParams: staminaPresets id '{s.id}' appears twice.");
                else if (!UIStrings.Has(UIStrings.StaminaPresetKey(s.id)))
                    r.Errors.Add($"UIStrings: missing key '{UIStrings.StaminaPresetKey(s.id)}' (stamina preset, 18 §A1).");
                if (!(s.holdDrain > 0) || !(s.idleDrain > 0))
                    r.Errors.Add($"GameParams: staminaPresets[{i}] drains must be > 0.");
            }
            if (p.StaminaPresetIndex(p.staminaHoldDrain, p.staminaIdleDrain) < 0)
                r.Warnings.Add($"GameParams: the stamina drains ({p.staminaHoldDrain} / {p.staminaIdleDrain}) are not one of staminaPresets; " +
                               "the debug key starts from the first preset (18 §A1).");
        }

        /// <summary>
        /// Dust kinds (20 §4): entry 0 is the base dust, ids are unique and named, the chances cannot add up past 1, and
        /// size / acceleration still follow the mass (cube root and the −0.18 power) within the rounding of the spec table.
        /// </summary>
        static void ValidateDustKinds(GameParams p, ValidationReport r)
        {
            if (p.dustKinds == null || p.dustKinds.Length == 0)
            {
                r.Errors.Add("GameParams: dustKinds is empty; entry 0 is the base dust (20 §4).");
                return;
            }
            var ids = new HashSet<string>();
            double maxTotal = 0;
            for (int i = 0; i < p.dustKinds.Length; i++)
            {
                var k = p.dustKinds[i];
                string where = $"GameParams: dustKinds[{i}]";
                if (k == null)
                {
                    r.Errors.Add(where + " is missing.");
                    continue;
                }
                if (string.IsNullOrEmpty(k.id)) r.Errors.Add(where + " has no id.");
                else if (!ids.Add(k.id)) r.Errors.Add($"GameParams: dust kind id '{k.id}' appears twice.");
                else if (!UIStrings.Has(UIStrings.DustKindKey(k.id))) r.Errors.Add($"UIStrings: missing key '{UIStrings.DustKindKey(k.id)}' (dust kind, 20 §4).");
                if (!(k.mass > 0)) r.Errors.Add(where + " mass must be > 0.");
                if (!(k.sizeMult > 0) || !(k.accelMult > 0) || !(k.brightMult > 0))
                    r.Errors.Add(where + " sizeMult, accelMult and brightMult must be > 0.");
                if (k.baseChance < 0 || k.chancePerLevel < 0 || k.chanceMaxLevel < 0)
                    r.Errors.Add(where + " chances and level count must be >= 0.");
                if (i == 0)
                {
                    // The base dust is the reference: it is always in the field and never rolled for.
                    if (k.mass != 1 || k.sizeMult != 1 || k.accelMult != 1)
                        r.Errors.Add(where + " is the base dust and must have mass, sizeMult and accelMult 1 (20 §4).");
                    if (k.baseChance != 0 || k.chancePerLevel != 0)
                        r.Errors.Add(where + " is the base dust and takes whatever the other kinds leave, so its chances must be 0.");
                    if (k.shape != DustShape.Dot) r.Warnings.Add(where + " is the base dust and should be drawn as a dot.");
                    continue;
                }
                if (!(k.baseChance > 0)) r.Errors.Add(where + " needs a baseChance > 0, or unlocking it does nothing.");
                if (k.unlockRing < 1) r.Errors.Add(where + " needs the ring of its unlock node (20 §4).");
                if (!(k.mass > p.dustKinds[i - 1].mass)) r.Errors.Add(where + " mass must be above the kind before it.");
                maxTotal += k.MaxChance;
                // 20 §4: size is mass^(1/3), pull acceleration mass^(−0.18). The asset rounds them, so allow 5%.
                double size = Math.Pow(k.mass, 1.0 / 3.0), accel = Math.Pow(k.mass, -0.18);
                if (Math.Abs(k.sizeMult - size) > size * 0.05)
                    r.Warnings.Add(where + $" sizeMult {k.sizeMult} is not the cube root of the mass ({size:F2}).");
                if (Math.Abs(k.accelMult - accel) > accel * 0.05)
                    r.Warnings.Add(where + $" accelMult {k.accelMult} is not mass^-0.18 ({accel:F2}).");
            }
            if (maxTotal >= 1)
                r.Errors.Add($"GameParams: the dust kind chances add up to {maxTotal:P0} at full level; they must leave room for the base dust (20 §4).");
        }

        /// <summary>
        /// Dust pull shaping (20 §3): the force curve only makes sense when the full-strength ring is inside the gravity
        /// radius, the radius keeps a share of the pull, the weak band reaches outside it, and the tangential damping bites.
        /// Also the gravity source follow experiment (20 §7).
        /// </summary>
        static void ValidatePull(GameParams p, ValidationReport r)
        {
            if (!(p.pullFullRatio > 0) || !(p.pullFullRatio < 1))
                r.Errors.Add("GameParams: pullFullRatio must be within 0..1 (both excluded); it is where the pull starts falling off.");
            if (!(p.pullEdgeShare > 0) || !(p.pullEdgeShare < 1))
                r.Errors.Add("GameParams: pullEdgeShare must be within 0..1 (both excluded); 0 brings back the hard edge of 18 §B.");
            if (!(p.pullOuterRatio > 1))
                r.Errors.Add("GameParams: pullOuterRatio must be > 1; the weak pull has to reach outside the gravity radius (20 §3).");
            if (!(p.captureAssistRatio >= 1))
                r.Errors.Add("GameParams: captureAssistRatio must be >= 1 (it is measured in capture radii).");
            if (!(p.tangentialKeepPerSec > 0) || !(p.tangentialKeepPerSec < 1))
                r.Errors.Add("GameParams: tangentialKeepPerSec must be within 0..1 (both excluded); 1 damps nothing, 0 kills the sideways speed.");
            if (!(p.followSpeedPx > 0)) r.Errors.Add("GameParams: followSpeedPx must be > 0 (20 §7).");
            if (p.followSpeedSteps == null || p.followSpeedSteps.Length == 0)
                r.Errors.Add("GameParams: followSpeedSteps is empty (the debug key has nothing to cycle, 20 §7).");
            else
            {
                for (int i = 0; i < p.followSpeedSteps.Length; i++)
                    if (!(p.followSpeedSteps[i] > 0)) r.Errors.Add($"GameParams: followSpeedSteps[{i}] must be > 0.");
                if (System.Array.IndexOf(p.followSpeedSteps, p.followSpeedPx) < 0)
                    r.Warnings.Add($"GameParams: followSpeedPx {p.followSpeedPx} is not one of followSpeedSteps (20 §7).");
            }
            if (!(p.followLineWidthPx > 0)) r.Errors.Add("GameParams: followLineWidthPx must be > 0.");
        }

        /// <summary>Fragments (20 §5): a fragment is worth part of its tier, comes from below the top tier and is drawn smaller.</summary>
        static void ValidateFragments(GameParams p, ValidationReport r)
        {
            if (!(p.fragmentSaleShare > 0) || !(p.fragmentSaleShare < 1))
                r.Errors.Add("GameParams: fragmentSaleShare must be within 0..1 (both excluded); a fragment is worth part of its tier (20 §5).");
            if (p.fragmentBaseOffset < 1)
                r.Errors.Add("GameParams: fragmentBaseOffset must be >= 1, or fragments would come from the top tier itself (20 §5).");
            if (!(p.fragmentScale > 0) || !(p.fragmentScale < 1))
                r.Errors.Add("GameParams: fragmentScale must be within 0..1 (both excluded); a fragment is smaller than its tier.");
            if (p.fragmentSpawnRadii < 0) r.Errors.Add("GameParams: fragmentSpawnRadii must be >= 0.");
        }

        /// <summary>
        /// Flash rule (15 §3): the shape is sane, and every flash's peak alpha is at most flashAlphaCap and its rise takes at
        /// least flashMinRiseSec.
        /// </summary>
        static void ValidateFlashRule(GameParams p, ValidationReport r)
        {
            if (!(p.flashAlphaCap > 0) || p.flashAlphaCap > 1) r.Errors.Add("GameParams: flashAlphaCap must be within 0..1 (0 excluded).");
            if (!(p.flashMinRiseSec > 0)) r.Errors.Add("GameParams: flashMinRiseSec must be > 0.");
            if (!(p.flashEdgeShare > 0) || !(p.flashEdgeShare < 1)) r.Errors.Add("GameParams: flashEdgeShare must be within 0..1 (both excluded).");
            if (!(p.flashRadiusPx > 0)) r.Errors.Add("GameParams: flashRadiusPx must be > 0.");
            foreach (var f in FlashPeaks(p))
                if (f.Value > p.flashAlphaCap) r.Errors.Add($"GameParams: {f.Key} {f.Value} is above flashAlphaCap {p.flashAlphaCap} (15 §3).");
            foreach (var f in FlashRises(p))
                if (f.Value < p.flashMinRiseSec) r.Errors.Add($"GameParams: {f.Key} {f.Value} is shorter than flashMinRiseSec {p.flashMinRiseSec} (15 §3).");
        }

        /// <summary>Peak alpha of every flash in the game, by field name.</summary>
        public static Dictionary<string, double> FlashPeaks(GameParams p) => new Dictionary<string, double>
        {
            { "ignitionFlashAlpha", p.ignitionFlashAlpha },
            { "ignitionRepeatFlashAlpha", p.ignitionRepeatFlashAlpha },
            { "resultIgnitionFlashAlpha", p.resultIgnitionFlashAlpha },
            { "supernovaFlashAlpha", p.supernovaFlashAlpha },
        };

        /// <summary>Rise time of every flash in the game, by field name (the result ignition shares ignitionFlashRiseSec).</summary>
        public static Dictionary<string, double> FlashRises(GameParams p) => new Dictionary<string, double>
        {
            { "ignitionFlashRiseSec", p.ignitionFlashRiseSec },
            { "supernovaFlashRiseSec", p.supernovaFlashRiseSec },
        };

        /// <summary>Central star / palette stages: protostar, one per star tier, and the remnant after the ending (15 §5).</summary>
        public static int CentralStages(GameParams p, CelestialTable c) => BackdropView.RemnantStage(c.MaxTier, p.orbitMaxTier) + 1;

        /// <summary>Ending (15 §4, §5): a timeline in order, positive durations and counts, a proper black hole look.</summary>
        static void ValidateEnding(GameParams p, ValidationReport r)
        {
            if (!(p.endingPullSec > 0) || !(p.endingFadeSec > 0)) r.Errors.Add("GameParams: endingPullSec and endingFadeSec must be > 0.");
            if (!(p.endingPullAccelPx > 0) || !(p.endingPullMaxSpeedPx > 0) || p.endingPullDamping < 0)
                r.Errors.Add("GameParams: the ending pull needs accel and top speed > 0 and damping >= 0.");
            if (p.endingFadeAlpha < 0 || p.endingFadeAlpha > 1) r.Errors.Add("GameParams: endingFadeAlpha must be within 0..1.");
            double[] times = { p.resultEntrySec, p.supernovaCalmEndSec, p.supernovaCollapseEndSec, p.supernovaBlastEndSec, p.supernovaShockEndSec, p.supernovaRemnantEndSec };
            for (int i = 1; i < times.Length; i++)
                if (!(times[i] > times[i - 1]))
                    r.Errors.Add("GameParams: the supernova timeline must increase: resultEntrySec < supernovaCalmEndSec < CollapseEnd < BlastEnd < ShockEnd < RemnantEnd.");
            if (!(p.supernovaShockEase > 0)) r.Errors.Add("GameParams: supernovaShockEase must be > 0.");
            if (!(p.supernovaShockEndPx > p.supernovaShockStartPx) || !(p.supernovaShockWidthPx > 0))
                r.Errors.Add("GameParams: the shock ring must grow (supernovaShockEndPx > supernovaShockStartPx) and have a width > 0.");
            if (p.supernovaShockMinIntensity < 0 || p.supernovaShockMinIntensity > 1) r.Errors.Add("GameParams: supernovaShockMinIntensity must be within 0..1.");
            if (!(p.supernovaFlashSec > p.supernovaFlashRiseSec)) r.Errors.Add("GameParams: supernovaFlashSec must be longer than supernovaFlashRiseSec.");
            if (p.supernovaDustPerPlanetMin < 1 || p.supernovaDustPerPlanetMax < p.supernovaDustPerPlanetMin || p.supernovaDustPerBeltDot < 0 || p.supernovaEjectaDust < 0)
                r.Errors.Add("GameParams: supernova dust counts are invalid (per planet 1 <= min <= max, others >= 0).");
            if (!(p.supernovaDustSpeedPx > 0) || !(p.supernovaDustDrag > 0) || p.remnantDriftSpeedPx < 0)
                r.Errors.Add("GameParams: supernova dust speed and drag must be > 0 and the drift speed >= 0.");
            if (!(p.creditLineSec > 0) || !(p.creditFadeSec > 0) || p.creditFadeSec * 2 > p.creditLineSec)
                r.Errors.Add("GameParams: creditLineSec must be > 0 and hold two creditFadeSec.");
            if (p.creditTitleFontSize < 1 || p.creditFontSize < 1 || p.continueFontSize < 1) r.Errors.Add("GameParams: credit and continue font sizes must be >= 1.");
            if (!(p.blackHoleFadeSec > 0) || !(p.remnantPaletteBlendSec > 0)) r.Errors.Add("GameParams: blackHoleFadeSec and remnantPaletteBlendSec must be > 0.");
            ValidateLook("GameParams: blackHoleLook", p.blackHoleLook, r);
            if (p.blackHoleLook != null && p.blackHoleLook.surface != SurfaceType.BlackHole)
                r.Errors.Add("GameParams: blackHoleLook must be a BlackHole look (15 §5).");
        }

        static bool IsWarm(UnityEngine.Color c) => c.r > c.b + 0.05f;

        static float Chroma(UnityEngine.Color c) => Math.Max(c.r, Math.Max(c.g, c.b)) - Math.Min(c.r, Math.Min(c.g, c.b));

        // Per node: unique id, known stat, string keys, sane cost curve, ring in range.
        static void ValidateNodes(CelestialTable c, NodeTable t, ValidationReport r)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < t.nodes.Count; i++)
            {
                var n = t.nodes[i];
                if (n == null || string.IsNullOrEmpty(n.id))
                {
                    r.Errors.Add($"NodeTable: entry {i} has an empty id.");
                    continue;
                }
                if (!seen.Add(n.id)) r.Errors.Add($"NodeTable: duplicate id '{n.id}'.");
                if (n.minTier < 1 || n.minTier > c.MaxTier)
                    r.Errors.Add($"NodeTable: '{n.id}' minTier {n.minTier} is outside 1..{c.MaxTier}.");
                if (n.baseCost <= 0) r.Errors.Add($"NodeTable: '{n.id}' baseCost must be > 0.");
                if (n.growth <= 0) r.Errors.Add($"NodeTable: '{n.id}' growth must be > 0.");
                if (n.maxLevel < 0) r.Errors.Add($"NodeTable: '{n.id}' maxLevel must be >= 0.");

                if (n.kind == NodeKind.Stat)
                {
                    if (!StatIds.IsKnown(n.statId)) r.Errors.Add($"NodeTable: '{n.id}' has unknown statId '{n.statId}'.");
                    else if (StatIds.NeedsDustKind(n.statId) && string.IsNullOrEmpty(n.dustKindId))
                        r.Errors.Add($"NodeTable: '{n.id}' raises {n.statId} but names no dust kind (20 §4).");
                    else
                    {
                        if (!UIStrings.Has(UIStrings.StatNameKey(n.statId))) r.Errors.Add($"UIStrings: missing key '{UIStrings.StatNameKey(n.statId)}'.");
                        if (!UIStrings.Has(UIStrings.StatEffectKey(n.statId))) r.Errors.Add($"UIStrings: missing key '{UIStrings.StatEffectKey(n.statId)}'.");
                    }
                    if (n.statId == StatIds.DustMass ? n.effectPerLevel <= 1 : n.effectPerLevel <= 0)
                        r.Errors.Add($"NodeTable: '{n.id}' effectPerLevel {n.effectPerLevel} has no effect.");
                    if (n.maxLevel == 0 && n.minTier != c.MaxTier)
                        r.Warnings.Add($"NodeTable: '{n.id}' has unlimited levels outside the last ring ({c.MaxTier}).");
                }
            }
        }

        // Gates: exactly one per tier 2..N, tier exists, one level.
        static void ValidateGates(CelestialTable c, NodeTable t, ValidationReport r)
        {
            var gateTiers = new HashSet<int>();
            foreach (var n in t.nodes)
            {
                if (n == null || !n.IsGate) continue;
                if (c.Get(n.tier) == null) r.Errors.Add($"NodeTable: gate '{n.id}' unlocks tier {n.tier}, which does not exist.");
                else if (n.tier <= 1) r.Errors.Add($"NodeTable: gate '{n.id}' unlocks tier {n.tier}; tier 1 is always unlocked.");
                if (!gateTiers.Add(n.tier)) r.Errors.Add($"NodeTable: more than one gate unlocks tier {n.tier}.");
                if (n.maxLevel != 1) r.Errors.Add($"NodeTable: gate '{n.id}' must have maxLevel 1.");
            }
            for (int i = 0; i < c.tiers.Count; i++)
            {
                int tier = c.tiers[i].tier;
                if (tier > 1 && !gateTiers.Contains(tier))
                    r.Errors.Add($"NodeTable: tier {tier} has no gate; gates are sequential, so tiers from {tier} up are unreachable.");
            }
        }

        // Prereqs exist, no cycles, every node reachable from the center; disabled nodes that cut a path are warned about.
        static void ValidateGraph(NodeTable t, ValidationReport r)
        {
            var ids = new HashSet<string>();
            foreach (var n in t.nodes) if (n != null && !string.IsNullOrEmpty(n.id)) ids.Add(n.id);

            foreach (var n in t.nodes)
            {
                if (n?.prereqs == null) continue;
                foreach (var p in n.prereqs)
                {
                    if (!ids.Contains(p)) r.Errors.Add($"NodeTable: '{n.id}' has prereq '{p}', which does not exist.");
                    else if (p == n.id) r.Errors.Add($"NodeTable: '{n.id}' lists itself as a prereq.");
                }
            }

            foreach (var cycle in FindCycles(t)) r.Errors.Add("NodeTable: prereq cycle " + cycle + ".");

            var reachable = Reachable(t, false);
            var reachableEnabled = Reachable(t, true);
            foreach (var n in t.nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.id)) continue;
                if (!reachable.Contains(n.id)) r.Errors.Add($"NodeTable: '{n.id}' cannot be reached from the center.");
                else if (n.enabled && !reachableEnabled.Contains(n.id))
                    r.Warnings.Add($"NodeTable: '{n.id}' is only reachable through disabled nodes ({string.Join(", ", n.prereqs)}).");
            }
        }

        /// <summary>Any-of reachability: a node is reachable when it has no prereqs or one of its prereqs is reachable.</summary>
        static HashSet<string> Reachable(NodeTable t, bool enabledOnly)
        {
            var reached = new HashSet<string>();
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var n in t.nodes)
                {
                    if (n == null || string.IsNullOrEmpty(n.id) || reached.Contains(n.id)) continue;
                    if (enabledOnly && !n.enabled) continue;
                    bool ok = n.prereqs == null || n.prereqs.Count == 0;
                    if (!ok)
                        foreach (var p in n.prereqs)
                            if (reached.Contains(p)) { ok = true; break; }
                    if (!ok) continue;
                    reached.Add(n.id);
                    changed = true;
                }
            }
            return reached;
        }

        static List<string> FindCycles(NodeTable t)
        {
            var cycles = new List<string>();
            var state = new Dictionary<string, int>(); // 0 new, 1 on stack, 2 done
            var stack = new List<string>();
            foreach (var n in t.nodes)
                if (n != null && !string.IsNullOrEmpty(n.id)) Visit(t, n.id, state, stack, cycles);
            return cycles;
        }

        static void Visit(NodeTable t, string id, Dictionary<string, int> state, List<string> stack, List<string> cycles)
        {
            state.TryGetValue(id, out int s);
            if (s == 2) return;
            if (s == 1)
            {
                int start = stack.IndexOf(id);
                cycles.Add(string.Join(" → ", stack.GetRange(start, stack.Count - start)) + " → " + id);
                return;
            }
            var n = t.Get(id);
            if (n == null) return;
            state[id] = 1;
            stack.Add(id);
            if (n.prereqs != null)
                foreach (var p in n.prereqs) Visit(t, p, state, stack, cycles);
            stack.RemoveAt(stack.Count - 1);
            state[id] = 2;
        }

        // Same ring, angles closer than MinAngleGapDeg: the tree view would draw them on top of each other.
        static void ValidateAngles(NodeTable t, ValidationReport r)
        {
            for (int i = 0; i < t.nodes.Count; i++)
            for (int j = i + 1; j < t.nodes.Count; j++)
            {
                var a = t.nodes[i];
                var b = t.nodes[j];
                if (a == null || b == null || a.minTier != b.minTier) continue;
                float d = Math.Abs(a.angleDeg - b.angleDeg) % 360f;
                if (d > 180f) d = 360f - d;
                if (d < MinAngleGapDeg)
                    r.Errors.Add($"NodeTable: '{a.id}' and '{b.id}' overlap on ring {a.minTier} ({a.angleDeg}° / {b.angleDeg}°).");
            }
        }

        static void ValidateRingCostMult(CelestialTable c, NodeTable t, ValidationReport r)
        {
            int rings = c.MaxTier + 1;
            if (t.ringCostMult == null || t.ringCostMult.Length < rings)
                r.Warnings.Add($"NodeTable: ringCostMult has {(t.ringCostMult == null ? 0 : t.ringCostMult.Length)} entries, expected {rings} (missing rings count as 1).");
            if (t.ringCostMult != null)
                for (int i = 0; i < t.ringCostMult.Length; i++)
                    if (t.ringCostMult[i] <= 0) r.Errors.Add($"NodeTable: ringCostMult[{i}] must be > 0.");
        }
    }
}
