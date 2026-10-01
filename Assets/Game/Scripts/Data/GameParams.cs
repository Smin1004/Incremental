using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// Background and dust colors for one central-star stage (14 §6): protostar, red dwarf, sun-like, blue giant, and the
    /// remnant nebula after the ending (15 §5). Shared by the run, result and tree screens.
    /// </summary>
    [Serializable]
    public sealed class BackdropPalette
    {
        public Color background = new Color(0.02f, 0.02f, 0.05f);
        [Tooltip("Light bleed from the central star's direction, added in linear color (keep it small); strength is multiplied by effectIntensity.")]
        public Color glowColor = Color.black;
        public float glowStrength;
        public float glowRadiusPx = 900f;
        [Tooltip("Dust colors (sRGB; each dust sits somewhere between the two). A warm hue, unlike the neutral background stars (15 §3).")]
        public Color dustA = Color.white;
        public Color dustB = new Color(0.65f, 0.82f, 1f);
        [Tooltip("Background star tint.")]
        public Color starTint = new Color(0.8f, 0.87f, 1f);
        [Tooltip("Faint nebula noise between two colors (the remnant palette, 15 §5), added in linear color; strength 0 = none.")]
        public Color nebulaA = Color.black;
        public Color nebulaB = Color.black;
        public float nebulaStrength;
        public float nebulaScalePx = 650f;
    }

    /// <summary>
    /// One stamina drain setting the debug key cycles through (18 §A1). The feel is a matter of taste, so the values are
    /// picked in a playtest instead of being fixed here; <see cref="id"/> names the UIStrings key
    /// <c>stamina.preset.&lt;id&gt;</c> shown in the toast.
    /// </summary>
    [Serializable]
    public sealed class StaminaPreset
    {
        public string id = string.Empty;
        [Tooltip("Per second while holding (additional) and per second always.")]
        public double holdDrain = 1;
        public double idleDrain = 0.2;
    }

    /// <summary>Silhouette the dust shader draws for a dust kind (20 §4). Only the shape branches; the hue is the palette's.</summary>
    public enum DustShape
    {
        /// <summary>Base dust: a round point.</summary>
        Dot = 0,
        /// <summary>Medium: a slanted chip.</summary>
        Shard = 1,
        /// <summary>Large: a lumpy clump.</summary>
        Clump = 2,
        /// <summary>Crystal: a six-pointed sparkle.</summary>
        Crystal = 3,
        /// <summary>Interstellar: a wide haze with four diffraction spikes.</summary>
        Interstellar = 4,
    }

    /// <summary>
    /// One kind of dust (20 §4). The first entry is the base dust: it is always there, its chance is whatever the others
    /// leave, and its mass / size / acceleration are the reference 1. Every other kind stays out of the field until its
    /// tree node is bought, then it is mixed in at <see cref="baseChance"/>, which the kind's chance node raises.
    /// Size is the cube root of the mass and the acceleration multiplier the −0.18 power of it, so heavier dust is bigger
    /// and slower; the numbers are written down here (rounded as in the spec table) instead of computed, and Validate Data
    /// checks that they still follow the two formulas.
    /// </summary>
    [Serializable]
    public sealed class DustKindDef
    {
        [Tooltip("Stable key for the tree nodes, the log and the string key dust.<id>.")]
        public string id = string.Empty;
        public double mass = 1;
        [Tooltip("The ring holding the unlock node (0 = the base dust, always there). Validate Data checks the tree against it.")]
        public int unlockRing;
        [Tooltip("Share of newly spawned dust once unlocked, before the chance node (0.03 = 3%).")]
        public double baseChance;
        [Tooltip("The kind's chance node: added share per level, and how many levels it has.")]
        public double chancePerLevel;
        public int chanceMaxLevel = 4;
        [Tooltip("Drawn size and pull acceleration relative to the base dust.")]
        public double sizeMult = 1;
        public double accelMult = 1;
        [Tooltip("Brightness relative to the base dust: heavier dust reads a little brighter.")]
        public double brightMult = 1;
        public DustShape shape = DustShape.Dot;

        /// <summary>Largest share this kind can reach, with its chance node at full level.</summary>
        public double MaxChance => baseChance + chancePerLevel * chanceMaxLevel;
    }

    /// <summary>
    /// Balance parameters. Fields map 1:1 to the spec §6 parameter table (snake_case → camelCase),
    /// plus presentation values that the spec leaves open. All numbers are double; no constants in code.
    /// Pixel values are relative to 1920x1080 and converted through <see cref="pixelsPerUnit"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "GameParams", menuName = "Incremental/Game Params")]
    public sealed class GameParams : ScriptableObject
    {
        [Header("Dust (spec §6)")]
        public double dustInitial = 100;
        public double spawnRate = 4;          // per second, upgrade target
        public double dustCap = 300;
        public double driftSpeed = 22;        // px/s (22 §8: 15 -> 22, influx is proportional to it)
        public double dustMass = 1;

        [Header("Gravity (spec §6)")]
        public double gravityRadius = 180;    // px
        public double pullAccel = 600;        // px/s^2, upgrade target
        public double dustMaxSpeed = 400;     // px/s
        public double captureRadius = 30;     // px

        [Header("Dust pull shaping (20 §3)")]
        [Tooltip("Share of the gravity radius the pull is at full strength within (20 §3 force curve).")]
        public double pullFullRatio = 0.75;
        [Tooltip("Pull left at the gravity radius itself, as a share of full; it falls to 0 at pullOuterRatio × the radius, " +
                 "so a still cursor keeps drawing dust in from outside the ring.")]
        public double pullEdgeShare = 0.15;
        public double pullOuterRatio = 1.4;
        [Tooltip("Within this many capture radii the sideways (tangential) speed is damped, so dust stops orbiting the cursor.")]
        public double captureAssistRatio = 2.5;
        [Tooltip("What is left of the tangential speed after one second inside the assist radius (0.6 = 40% damped per second).")]
        public double tangentialKeepPerSec = 0.6;

        [Header("Widening world (22 §6)")]
        [Tooltip("How much wider the play world gets per ring: the linear scale of ring k is 1 + this x (k - 1). 22 §6 " +
                 "asked for 0.06 (ring 11: 1.60 wide, 2.56 in area). The frame budget allows it, but the balance does not: " +
                 "a wider world spreads the same spawn rate over more area, so less of it lands inside the gravity radius " +
                 "and a full playthrough runs 105 runs / 164 min instead of 74 / 112. 0.0375 is the value F verified.")]
        public double worldScalePerRing = 0.0375;
        [Tooltip("How long the world takes to grow after a gate is bought (seconds).")]
        public double worldExpandSec = 0.6;
        [Tooltip("Smallest a body may be drawn on screen (px), so an asteroid or a fragment does not become a dot as the " +
                 "camera pulls back (22 §6).")]
        public double bodyMinScreenPx = 8;

        [Header("Vortex and trails (22 §5)")]
        [Tooltip("Share of the pull that is turned sideways (counter-clockwise), so dust spirals in instead of falling " +
                 "straight. 0 brings back the straight lines of 20 §3.")]
        public double vortexShare = 0.25;
        [Tooltip("Fastest sideways speed the vortex will push a dust to (px/s). The swirl fades out as the dust reaches " +
                 "it and pushes back above it, so a dust can never gather enough sideways speed to orbit for ever: " +
                 "wherever the pull reaches, the inward part wins and the dust spirals in.")]
        public double vortexTerminalPx = 300;
        [Tooltip("Positions kept per trailed dust, and the fixed time between two of them (seconds).")]
        public int trailPoints = 8;
        public double trailSampleSec = 0.08;
        [Tooltip("Width of the head of a trail as a share of that dust's drawn size; it narrows to 0 at the tail.")]
        public double trailWidthShare = 0.6;
        [Tooltip("Trails drawn at once. Over it, the heavier kinds win, then the ones closer to the gravity source.")]
        public int trailBudget = 300;
        [Tooltip("The trails that only just make it into trailBudget are drawn fainter: the last this-share of the budget " +
                 "fades from full to nothing in that same order. A trail then grows in as its dust moves up the order " +
                 "instead of appearing at full length the moment it gets a place. 0 = the hard cut of 22 §5.")]
        public double trailBudgetFadeShare = 0.35;

        [Header("Gravity source follow (22 §3)")]
        [Tooltip("On since 22 §3: the gravity source follows the cursor at its effective top speed at most and a thin line " +
                 "joins the two, so a fast sweep leaves the gravity behind. Off puts it back on the cursor exactly.")]
        public bool followEnabled = true;
        [Tooltip("Base top speed, px/s. The follow_speed nodes add to it (22 §3: five nodes of +15%p make 1.75 ×).")]
        public double followSpeedPx = 220;
        [Tooltip("The debug key (Insert) cycles off → these multiples of the effective speed → off (22 §3).")]
        public double[] followDebugMults = { 1, 1.5, 2 };
        [Tooltip("The line drawn from the cursor to the gravity source while following: width (px) and color.")]
        public double followLineWidthPx = 1.5;
        public Color followLineColor = new Color(1f, 1f, 1f, 0.35f);

        [Header("Stamina (spec §6)")]
        public double staminaMax = 86;        // upgrade target (22 §8: the first run is 48s at the base drain)
        public double staminaHoldDrain = 1.5; // per second while holding (additional)
        public double staminaIdleDrain = 0.3; // per second, always
        [Tooltip("The stamina debug key (F10) cycles the runtime drain through these, in order (18 §A1); the asset values above " +
                 "must be one of them. Runtime only: nothing is saved, and the next launch is back to the values above.")]
        public StaminaPreset[] staminaPresets =
        {
            new StaminaPreset { id = "loose", holdDrain = 1.0, idleDrain = 0.2 },
            new StaminaPreset { id = "proposed", holdDrain = 1.5, idleDrain = 0.3 },
            new StaminaPreset { id = "fast", holdDrain = 2.0, idleDrain = 0.4 },
        };

        [Header("Dust kinds (20 §4)")]
        [Tooltip("Entry 0 is the base dust and is always in the field; the rest are unlocked in the tree and mixed in by " +
                 "chance. The chances never overlap: one roll at spawn picks the kind, and whatever is left over is base dust.")]
        public DustKindDef[] dustKinds =
        {
            new DustKindDef { id = "base", mass = 1, unlockRing = 0, sizeMult = 1, accelMult = 1, brightMult = 1, shape = DustShape.Dot },
            new DustKindDef
            {
                id = "medium", mass = 5, unlockRing = 3, baseChance = 0.03, chancePerLevel = 0.03, chanceMaxLevel = 4,
                sizeMult = 1.7, accelMult = 0.75, brightMult = 1.15, shape = DustShape.Shard,
            },
            new DustKindDef
            {
                id = "large", mass = 25, unlockRing = 5, baseChance = 0.03, chancePerLevel = 0.015, chanceMaxLevel = 4,
                sizeMult = 2.9, accelMult = 0.55, brightMult = 1.3, shape = DustShape.Clump,
            },
            new DustKindDef
            {
                id = "crystal", mass = 125, unlockRing = 7, baseChance = 0.02, chancePerLevel = 0.01, chanceMaxLevel = 4,
                sizeMult = 4.9, accelMult = 0.40, brightMult = 1.45, shape = DustShape.Crystal,
            },
            new DustKindDef
            {
                id = "interstellar", mass = 625, unlockRing = 9, baseChance = 0.02, chancePerLevel = 0.01, chanceMaxLevel = 4,
                sizeMult = 8.5, accelMult = 0.30, brightMult = 1.6, shape = DustShape.Interstellar,
            },
        };

        [Header("Fragments (20 §5)")]
        [Tooltip("A fragment sells for this share of its tier's price. The tree decides whether fragments happen at all " +
                 "and how likely they are; this is the lever if they end up worth too much of a run.")]
        public double fragmentSaleShare = 0.5;
        [Tooltip("Fragments only come from tiers up to unlockedMaxTier − this, before the frag_offset nodes reduce it. " +
                 "The cap never falls below tier 1, so the first fragment node is worth something the ring it is bought in.")]
        public int fragmentBaseOffset = 4;
        [Tooltip("A fragment is drawn at this share of its tier's display size, and starts this far (in forming-body radii) " +
                 "off the body it fell from.")]
        public double fragmentScale = 0.6;
        public double fragmentSpawnRadii = 1.0;

        [Header("Economy (spec §6)")]
        public double saleMult = 1.0;         // upgrade target
        public double thresholdMult = 1.0;    // upgrade target (required mass multiplier)

        [Header("Presentation (not in the spec table)")]
        public double pixelsPerUnit = 100;
        [Tooltip("0..1. Every flash / shake / pop is multiplied by this.")]
        public double effectIntensity = 1;
        [Tooltip("Max random heading change of drifting dust, radians per second.")]
        public double driftNoise = 2.0;
        [Tooltip("How fast dust speed relaxes back to driftSpeed after being pulled, 1/s.")]
        public double driftRelaxRate = 2.0;
        [Tooltip("Outward speed (px/s) of dust scattered when released below tier 1.")]
        public double scatterSpeedPx = 200;
        [Tooltip("Extra scale at planet spawn (0.5 = 150%), multiplied by effectIntensity.")]
        public double planetPopScale = 0.5;
        public double planetPopDurationSec = 0.15;

        [Header("Planet departure (13 §2, 18 §A2)")]
        [Tooltip("After the pop, the planet stays in place this long.")]
        public double planetHoldSec = 0.15;
        [Tooltip("Then it flies to the vanishing point, shrinking and fading.")]
        public double planetDepartSec = 0.45;
        [Tooltip("Vanishing point in viewport coordinates (0..1); beyond 1 is outside the screen.")]
        public Vector2 vanishPoint = new Vector2(1.05f, 1.05f);
        [Tooltip("Faint glow at the screen edge toward the vanishing point: size (px) and alpha.")]
        public double vanishGlowPx = 520;
        public double vanishGlowAlpha = 0.18;

        [Header("Planet shader (14 §3)")]
        [Tooltip("Ambient light on the dark side of lit bodies (0..1).")]
        public double planetAmbient = 0.08;
        [Tooltip("Light direction = screen direction to the light mixed with this much toward the viewer, so the terminator is not an exact half.")]
        public double planetLightZ = 0.4;
        [Tooltip("Rim light strength and falloff power.")]
        public double planetRimStrength = 0.35;
        public double planetRimPower = 3;
        [Tooltip("Disc radius (px) at which fine surface detail is fully shown; smaller discs fade it out so it does not shimmer.")]
        public double planetDetailPx = 10;
        [Tooltip("How strongly crater walls tilt the surface normal.")]
        public double planetCraterRelief = 0.6;
        [Tooltip("Spin axis tilt toward the viewer, degrees (rings show as ellipses, poles are visible).")]
        public double planetAxisPitchDeg = 20;

        [Header("Forming body (14 §4)")]
        [Tooltip("Look below tier 1: a dark clump of dust.")]
        public PlanetLook formingClumpLook = new PlanetLook
        {
            surface = SurfaceType.Rock,
            baseColor = new Color(0.36f, 0.34f, 0.33f), darkColor = new Color(0.16f, 0.15f, 0.15f),
            glowColor = new Color(0.7f, 0.75f, 0.85f),
            noiseScale = 4f, patchLevel = 0.5f, patchSoftness = 0.3f,
            irregularity = 0.42f,
            spinDegPerSec = 70f, axisTiltDeg = 30f,
        };
        [Tooltip("Clump diameter at zero mass and at the tier 1 threshold, px.")]
        public double formingClumpMinPx = 6;
        public double formingClumpMaxPx = 16;
        [Tooltip("How fast the drawn size follows the mass, 1/s (smooths the per-dust steps).")]
        public double formingSizeLerpRate = 12;
        [Tooltip("Pop when a threshold is crossed: extra scale (× effectIntensity) and duration.")]
        public double formingPopScale = 0.35;
        public double formingPopSec = 0.2;
        [Tooltip("Mass gauge: a thin ring this far outside the body, px; width and alphas.")]
        public double gaugeGapPx = 8;
        public double gaugeWidthPx = 3;
        public double gaugeBgAlpha = 0.18;
        public double gaugeFillAlpha = 0.8;

        [Header("Dust look (14 §5)")]
        [Tooltip("Bright core diameter and halo diameter, px.")]
        public double dustCorePx = 4;
        public double dustHaloPx = 14;
        public double dustHaloAlpha = 0.35;
        [Tooltip("Per-dust size and brightness variation (0.3 = ±30%).")]
        public double dustSizeJitter = 0.3;
        public double dustBrightJitter = 0.3;
        [Tooltip("A new dust fades in from nothing over this many seconds, so one created in plain view does not pop in. " +
                 "Dust is kept out of the gravity radius when it is created but not out of the weak band outside it, " +
                 "and by ring 11 about a fifth of new dust lands there. 0 = there at once.")]
        public double dustSpawnFadeSec = 0.2;
        [Tooltip("Longest a dust may be drawn, as a multiple of its own size. Since 22 §5 this is a ceiling, not the " +
                 "shape: the smear itself is dustStreakExposureSec x the speed.")]
        public double dustStretchMax = 4;
        [Tooltip("Speed (px/s) that counts as full stretch for the brightness and dimming curves of 14 §5.")]
        public double dustStretchFullSpeedPx = 400;
        [Tooltip("How long a 'shutter' the motion smear stands for (seconds). The smear is that time times the dust's " +
                 "speed, so it is the same length for every kind: small dust turns into a streak and heavy dust, which is " +
                 "many times wider, barely changes shape. Multiplying the size instead drew the heavy kinds as bars (22 §5).")]
        public double dustStreakExposureSec = 0.05;
        [Tooltip("Brightness of a fully stretched streak relative to a still dot.")]
        public double dustStreakDim = 1;
        [Tooltip("Extra brightness inside the gravity radius while holding (at the center; fades to 0 at the edge).")]
        public double dustGravityBrighten = 1.5;
        [Tooltip("Brightness of dust outside the active gravity field (15 §3): only dust being pulled keeps full brightness, so the eye goes to the cursor.")]
        public double dustOutsideDim = 0.5;
        [Tooltip("Band outside the gravity radius over which dust dims, px; and the fade time when a hold starts or stops, seconds.")]
        public double dustDimEdgePx = 60;
        public double dustHoldFadeSec = 0.25;
        [Tooltip("Width of a fully stretched streak relative to a still dot (15 §3); the tail end narrows to a point.")]
        public double dustStreakWidth = 0.5;

        [Header("Background (14 §6)")]
        [Tooltip("By central star stage: 0 protostar (below the first star tier), 1 red dwarf, 2 sun-like, 3 blue giant, 4 remnant nebula (after the ending, 15 §5). " +
                 "Dust takes a warm hue of its palette and background stars stay neutral white, so the two never read alike (15 §3).")]
        public BackdropPalette[] palettes =
        {
            new BackdropPalette
            {
                background = new Color(0.02f, 0.02f, 0.05f), glowColor = new Color(0.3f, 0.35f, 0.6f), glowStrength = 0.008f, glowRadiusPx = 800f,
                dustA = new Color(1f, 0.84f, 0.58f), dustB = new Color(1f, 0.74f, 0.48f), starTint = new Color(0.88f, 0.9f, 0.94f),
            },
            new BackdropPalette
            {
                background = new Color(0.05f, 0.02f, 0.07f), glowColor = new Color(0.9f, 0.25f, 0.2f), glowStrength = 0.025f, glowRadiusPx = 800f,
                dustA = new Color(1f, 0.72f, 0.52f), dustB = new Color(1f, 0.6f, 0.45f), starTint = new Color(0.88f, 0.9f, 0.94f),
            },
            new BackdropPalette
            {
                background = new Color(0.035f, 0.045f, 0.1f), glowColor = new Color(1f, 0.72f, 0.38f), glowStrength = 0.04f, glowRadiusPx = 850f,
                dustA = new Color(1f, 0.88f, 0.55f), dustB = new Color(1f, 0.78f, 0.42f), starTint = new Color(0.88f, 0.9f, 0.94f),
            },
            new BackdropPalette
            {
                background = new Color(0.05f, 0.08f, 0.18f), glowColor = new Color(0.6f, 0.78f, 1f), glowStrength = 0.035f, glowRadiusPx = 900f,
                dustA = new Color(1f, 0.82f, 0.62f), dustB = new Color(1f, 0.7f, 0.56f), starTint = new Color(0.88f, 0.9f, 0.94f),
            },
            new BackdropPalette
            {
                background = new Color(0.012f, 0.014f, 0.03f), glowColor = new Color(0.55f, 0.35f, 0.85f), glowStrength = 0.012f, glowRadiusPx = 700f,
                dustA = new Color(1f, 0.8f, 0.6f), dustB = new Color(1f, 0.66f, 0.6f), starTint = new Color(0.88f, 0.9f, 0.94f),
                nebulaA = new Color(0.1f, 0.55f, 0.55f), nebulaB = new Color(0.6f, 0.12f, 0.5f), nebulaStrength = 0.09f, nebulaScalePx = 650f,
            },
        };
        [Tooltip("Palette change when the stage changes outside an ignition, seconds (at load it snaps).")]
        public double paletteBlendSec = 1.5;
        [Tooltip("Background star layers: cell size px, share of cells with a star, star radius px, brightness. A: many faint, B: few brighter.")]
        public Vector4 bgStarLayerA = new Vector4(22f, 0.3f, 0.6f, 0.16f);
        public Vector4 bgStarLayerB = new Vector4(110f, 0.4f, 0.9f, 0.34f);
        [Tooltip("Twinkle speed (rad/s) and depth (0..1).")]
        public double bgTwinkleSpeed = 0.7;
        public double bgTwinkleDepth = 0.5;

        [Header("Flash rule (15 §3)")]
        [Tooltip("Every flash (star ignition, result ignition, supernova): peak alpha at most flashAlphaCap (then × effectIntensity), " +
                 "rise at least flashMinRiseSec, and a radial falloff from its source: flashEdgeShare of the center brightness at flashRadiusPx " +
                 "(540 = half the 1080p screen height, so a flash at the screen center is at most flashEdgeShare × at every edge). " +
                 "20 §6 (W5-1): 0.3 → 0.15, so the edges of the screen stay darker at a blast's brightest frame.")]
        public double flashAlphaCap = 0.35;
        public double flashMinRiseSec = 0.1;
        public double flashEdgeShare = 0.15;
        public double flashRadiusPx = 540;

        [Header("Star ignition (14 §6, 10 §4.3)")]
        [Tooltip("First star ever on the run screen: screen flash + light ring + palette change, seconds. Logic time is untouched.")]
        public double ignitionFirstSec = 1.5;
        [Tooltip("Every later star: short flash + light ring, seconds.")]
        public double ignitionRepeatSec = 0.4;
        [Tooltip("Peak flash alpha (× effectIntensity, first / later star), the time to the peak (seconds) and the flash color.")]
        public double ignitionFlashAlpha = 0.3;
        public double ignitionRepeatFlashAlpha = 0.25;
        public double ignitionFlashRiseSec = 0.1;
        public Color ignitionFlashColor = new Color(1f, 0.97f, 0.9f);
        [Tooltip("Light ring: final diameter px and ring width px (first / later).")]
        public double ignitionRingPx = 2600;
        public double ignitionRepeatRingPx = 700;
        public double ignitionRingWidthPx = 60;
        public double ignitionRingStrength = 0.9;
        [Tooltip("Soft glow at the ring's center (share of the ring strength) and the share of the ignition time the flash fades over.")]
        public double ignitionRingCoreGlow = 0.6;
        public double ignitionFlashDecayShare = 0.6;

        [Header("Gallery debug key (14 §3)")]
        [Tooltip("Gap between bodies in the tier gallery, px.")]
        public double galleryGapPx = 44;
        public double galleryZoomMin = 0.5;
        public double galleryZoomMax = 3;
        [Tooltip("Zoom change per mouse wheel notch.")]
        public double galleryZoomStep = 0.1;

        [Header("Result solar system (13 §3)")]
        [Tooltip("Central star offset from the screen center, px (right).")]
        public double starOffsetPx = 220;
        public double orbitR0Px = 110;
        public double orbitStepPx = 52;
        [Tooltip("Highest tier that gets an orbit; tiers above are stars in the sky.")]
        public int orbitMaxTier = 8;
        public double orbitAlpha = 0.15;
        public double orbitAlphaUsed = 0.3;
        [Tooltip("Angular speed of the innermost orbit, degrees per second; ω(r) = ω0 × (r0 / r)^1.5.")]
        public double orbitOmega0Deg = 40;
        [Tooltip("Planet size on the result screen = sizePx × this.")]
        public double orbitPlanetScale = 0.5;
        [Tooltip("Tiers up to this one become an asteroid belt when there are more than orbitBeltThreshold planets.")]
        public int orbitBeltMaxTier = 3;
        public int orbitBeltThreshold = 16;
        public int orbitBeltMaxDots = 240;
        public double orbitBeltJitterPx = 8;
        public double orbitBeltDotMinPx = 2;
        public double orbitBeltDotMaxPx = 4;
        [Tooltip("Belt dot brightness multiplier on the tier color, so a belt reads as a band (15 §3).")]
        public double orbitBeltDotBrightness = 1.5;
        [Tooltip("Tiers above orbitBeltMaxTier show at most this many planets, then ×N.")]
        public int orbitIndividualMax = 12;
        [Tooltip("Stars (tiers above orbitMaxTier) made this run, drawn in the sky at most this many.")]
        public int skyStarsPerRun = 20;
        [Tooltip("Sky star diameter = the tier's sizePx × this; they are placed at random within skyStarArea (px from the screen center: xMin, yMin, xMax, yMax).")]
        public double skyStarScale = 0.2;
        public Vector4 skyStarArea = new Vector4(-200f, -480f, 900f, 480f);
        [Tooltip("Sky stars keep at least this far (px) outside the outermost orbit so they do not read as planets.")]
        public double skyStarOrbitClearPx = 40;
        [Tooltip("Entry animation and income count-up, seconds (13 §5).")]
        public double resultEntrySec = 1.2;
        public Color protostarColor = new Color(1f, 0.55f, 0.2f);
        [Tooltip("Central star below the first star tier: a faint Star with only a corona (14 §7). Its glow breathes.")]
        public PlanetLook protostarLook = new PlanetLook
        {
            surface = SurfaceType.Star,
            baseColor = new Color(0.8f, 0.45f, 0.2f), darkColor = new Color(0.36f, 0.12f, 0.05f),
            glowColor = new Color(1f, 0.52f, 0.2f),
            noiseScale = 3f, spinDegPerSec = 4f,
            glowSize = 1.8f, glowStrength = 0.6f,
        };
        [Tooltip("Protostar glow breathing: depth (share of the glow) and speed (rad/s).")]
        public double protostarPulse = 0.25;
        public double protostarPulseSpeed = 1.3;
        [Tooltip("Central star disc size and ignition flash base size (px) by stage: protostar, red dwarf, sun-like, blue giant, black hole (15 §5).")]
        public float[] starCorePx = { 30f, 38f, 52f, 72f, 56f };
        public float[] starGlowPx = { 170f, 200f, 260f, 340f, 300f };
        [Tooltip("Central star after the ending (15 §5): a black disc, a tilted bright accretion disc (the ring, turning at the spin speed) and a faint glow at the disc edge.")]
        public PlanetLook blackHoleLook = new PlanetLook
        {
            surface = SurfaceType.BlackHole,
            baseColor = new Color(1f, 0.88f, 0.66f), darkColor = new Color(0.85f, 0.3f, 0.6f),
            glowColor = new Color(0.62f, 0.8f, 1f),
            noiseScale = 3f, spinDegPerSec = 35f, axisTiltDeg = 16f,
            glowSize = 0.3f, glowStrength = 0.9f,
            ringInner = 1.35f, ringOuter = 3.3f, ringColor = new Color(1f, 1f, 1f, 0.95f),
        };
        [Tooltip("Result screen flash when tier 9 is made for the first time: seconds, radius growth relative to the glow, and peak alpha (flash rule, 15 §3).")]
        public double ignitionSec = 0.9;
        public double ignitionScale = 4;
        public double resultIgnitionFlashAlpha = 0.3;

        [Header("Supernova ending, run screen (15 §4.2)")]
        [Tooltip("After the first blue giant: every dust falls into it for endingPullSec, then the screen darkens over endingFadeSec and the run ends.")]
        public double endingPullSec = 2;
        public double endingFadeSec = 1;
        [Tooltip("The pull toward the blue giant (no radius limit): acceleration and top speed (px/s², px/s) and damping of the sideways speed (1/s) so dust falls in instead of orbiting.")]
        public double endingPullAccelPx = 1000;
        public double endingPullMaxSpeedPx = 1400;
        public double endingPullDamping = 3;
        [Tooltip("Dust within this distance of the blue giant glows brighter as it falls in, px.")]
        public double endingDustGlowRadiusPx = 420;
        [Tooltip("Darkness at the end of the fade (0..1).")]
        public double endingFadeAlpha = 1;
        [Tooltip("The blue giant's glow swells by this share while the dust falls in.")]
        public double endingStarGlowGain = 0.6;

        [Header("Supernova ending, result screen (15 §4.3)")]
        [Tooltip("Seconds from the result screen entry: end of the calm (the star grows unstable), of the collapse, of the blast and of the shock ring; " +
                 "the remnant runs until supernovaRemnantEndSec, then the credits.")]
        public double supernovaCalmEndSec = 3.5;
        public double supernovaCollapseEndSec = 4.3;
        public double supernovaBlastEndSec = 4.8;
        public double supernovaShockEndSec = 8;
        public double supernovaRemnantEndSec = 12;
        [Tooltip("Calm: the central star's size pulse goes from start to end frequency (Hz) and amplitude (share of the size), and its colors move this far toward white.")]
        public double supernovaPulseStartHz = 0.7;
        public double supernovaPulseEndHz = 4;
        public double supernovaPulseStartAmp = 0.02;
        public double supernovaPulseEndAmp = 0.14;
        public double supernovaWhiten = 0.85;
        [Tooltip("Glow of the central star: swing per unit of the size pulse, gain by the calm end, and further gain over the collapse.")]
        public double supernovaCalmGlowPulse = 2;
        public double supernovaCalmGlowGain = 0.5;
        public double supernovaCollapseGlowGain = 1;
        [Tooltip("Blast: flash peak alpha (flash rule, 15 §3), rise and total seconds from the collapse end, color; and the soft light at the center (FxRing center glow): strength at its peak and size px.")]
        public double supernovaFlashAlpha = 0.35;
        public double supernovaFlashRiseSec = 0.12;
        public double supernovaFlashSec = 1.4;
        public Color supernovaFlashColor = new Color(0.9f, 0.95f, 1f);
        public double supernovaCoreGlow = 1.2;
        public double supernovaCoreGlowPx = 700;
        [Tooltip("Shock ring: start and end radius (px), ring width (px), strength, color; at effect intensity 0 it keeps supernovaShockMinIntensity of its brightness.")]
        public double supernovaShockStartPx = 60;
        public double supernovaShockEndPx = 1250;
        public double supernovaShockWidthPx = 70;
        public double supernovaShockStrength = 1;
        [Tooltip("Shock ring growth: radius share = 1 − (1 − t)^ease (1 = constant speed, larger = faster start).")]
        public double supernovaShockEase = 1.4;
        public Color supernovaShockColor = new Color(0.72f, 0.86f, 1f);
        public double supernovaShockMinIntensity = 0.3;
        [Tooltip("An orbit line fades out over this many seconds once the ring has passed it.")]
        public double supernovaOrbitFadeSec = 0.25;
        [Tooltip("Scatter: dust per px of a planet's diameter (clamped to min..max per planet), dust per belt dot, dust thrown out by the star itself at the blast.")]
        public double supernovaDustPerPx = 0.45;
        public int supernovaDustPerPlanetMin = 4;
        public int supernovaDustPerPlanetMax = 26;
        public int supernovaDustPerBeltDot = 1;
        public int supernovaEjectaDust = 900;
        [Tooltip("Ejecta start within this radius of the center, px; scattered dust leaves outward with this much random spread (share of the outward direction).")]
        public double supernovaEjectaRadiusPx = 6;
        public double supernovaDustSpread = 0.35;
        [Tooltip("Scattered dust leaves outward at this speed (px/s) ± the jitter share, slows with the drag (1/s) down to the remnant drift speed (px/s); it bounces off the screen edges so the remnant stays on screen.")]
        public double supernovaDustSpeedPx = 420;
        public double supernovaDustSpeedJitter = 0.55;
        public double supernovaDustDrag = 1.1;
        public double remnantDriftSpeedPx = 7;
        [Tooltip("Brightness of the remnant dust (it is not being pulled, but it is the scene).")]
        public double remnantDustBrightness = 0.8;
        [Tooltip("Remnant: the palette blends to the remnant nebula over this many seconds from the shock end; the black hole fades in from blackHoleFadeStartSec over blackHoleFadeSec.")]
        public double remnantPaletteBlendSec = 3;
        public double blackHoleFadeStartSec = 8.5;
        public double blackHoleFadeSec = 3;
        [Tooltip("Skipping (Space / click after the blast) fast-forwards the remnant dust by this many seconds so it has settled.")]
        public double supernovaSkipSettleSec = 6;

        [Header("Credits (15 §4.4)")]
        [Tooltip("Each credit line fades in, holds and fades out over creditLineSec (fade in / out creditFadeSec each), one after another from supernovaRemnantEndSec.")]
        public double creditLineSec = 5;
        public double creditFadeSec = 1;
        [Tooltip("Credit text: font sizes (title line / others), position of the line center from the screen center (px, 1080p reference), color.")]
        public int creditTitleFontSize = 60;
        public int creditFontSize = 38;
        public Vector2 creditOffsetPx = new Vector2(0f, -230f);
        public Color creditColor = new Color(0.92f, 0.94f, 1f);
        [Tooltip("\"Continue\" prompt at the end: font size, position from the bottom center (px), and its slow breathing (depth 0..1, rad/s).")]
        public int continueFontSize = 32;
        public Vector2 continueOffsetPx = new Vector2(0f, 90f);
        public double continuePulseDepth = 0.35;
        public double continuePulseSpeed = 2.2;

        [Header("Result growth highlight (14 §7)")]
        [Tooltip("×N and \"new\" labels sit outside each orbit at this angle (degrees, 0 = right, counter-clockwise), with a dark backing.")]
        public double orbitLabelAngleDeg = -60;
        public double orbitLabelGapPx = 6;
        public Color orbitLabelBacking = new Color(0f, 0f, 0f, 0.6f);
        [Tooltip("Panel increases vs the comparison run (the ratio's run; decreases are not shown, 15 §3) and the \"new body\" highlight color.")]
        public Color deltaUpColor = new Color(0.55f, 1f, 0.6f);
        public Color newTierColor = new Color(1f, 0.85f, 0.4f);
        [Tooltip("A tier made for the first time: its orbit pulses this many times over this many seconds after the entry, once.")]
        public double newTierHighlightSec = 2;
        public int newTierPulses = 2;
        [Tooltip("Orbit line alpha of a new tier after the pulse, and its width multiplier at the pulse peak.")]
        public double newTierOrbitAlpha = 0.45;
        public double newTierOrbitWidth = 3;
        [Tooltip("A star tier made for the first time: one of its sky stars pulses with this extra size at the pulse peak (15 §3).")]
        public double newTierStarPulseScale = 0.6;

        [Header("Skill tree view (12 §5, §9)")]
        [Tooltip("Ring k radius = treeRingR0Px + k × treeRingStepPx (reference 1080p).")]
        public double treeRingR0Px = 40;
        public double treeRingStepPx = 115;
        public double treeHoldDelaySec = 0.35;
        public double treeHoldRepeatSec = 0.08;
        public double treeZoomMin = 0.3;
        public double treeZoomMax = 1.6;
        [Tooltip("Seconds of the camera move to the frontier when the tree opens.")]
        public double treeFocusSec = 0.35;

        [Header("Dust cap policy (22 §7)")]
        [Tooltip("The largest dust count the game will ever ask for. The renderers size their buffers for it once, so a " +
                 "run never stalls growing them, and Validate Data checks the last ring's effective cap stays under it.")]
        public double dustCapCeiling = 8000;
        [Tooltip("Reserved for the options screen's dust quality (22 §7): the shares of the effective cap the settings " +
                 "offer, highest first. Only the data is prepared this week; nothing reads it yet.")]
        public double[] dustQualityShares = { 1, 0.7, 0.45 };

        [Header("Dust outside the screen")]
        [Tooltip("Dust also lives in a band this wide (px) around the visible area, out of sight, so that whatever reaches " +
                 "past the screen has something to pull in: the gravity source at an edge, 수축 at its last level, a " +
                 "satellite far out, a meteor's wake. 0 = no dust outside the screen, as before.")]
        public double dustMarginPx = 200;
        [Tooltip("The most dust the band is filled with, as a share of the dust cap. It is part of the cap and not on top " +
                 "of it: the total never passes the cap, so what is kept outside is room the screen does not have.")]
        public double dustMarginCapShare = 0.15;
        [Tooltip("How fast the band is filled again after something pulled dust out of it: this share of the spawn rate, " +
                 "on top of the spawn on screen, which is unchanged. It is also the most the band can add to the supply of " +
                 "dust. The band has about half the area of the screen, so 0.25 fills it about half as densely.")]
        public double dustMarginSpawnShare = 0.25;
        [Tooltip("Dust of the band comes onto the screen only when it is faster than this (px/s), which means something set " +
                 "it moving: a pull, a push, a wake. While it merely drifts it is turned back at the edge, so the band does " +
                 "not leak in. Dust on screen never leaves the screen. Must be above driftSpeed.")]
        public double dustEdgePassSpeedPx = 45;

        [Header("Performance measurement (22 §4)")]
        [Tooltip("Dust counts the benchmark measures the run screen at, in order.")]
        public double[] perfBenchDustCounts = { 1000, 3700, 8000, 9472 };
        [Tooltip("Seconds of each scene that are thrown away before the sample starts, and how long the sample lasts.")]
        public double perfBenchWarmupSec = 2;
        public double perfBenchSampleSec = 6;
        [Tooltip("Seconds before the very first scene is measured: the window opening and the first shader variants are " +
                 "one-off stalls that would otherwise land in scene 1.")]
        public double perfBenchSettleSec = 4;

        [Header("Save (02 §6)")]
        [Tooltip("While a run is going, its progress is saved this often (seconds) so a killed game keeps the run's income.")]
        public double pendingSaveIntervalSec = 5;

        /// <summary>Number of dust kinds, the base dust included (20 §4).</summary>
        public int DustKindCount => dustKinds != null ? dustKinds.Length : 0;

        /// <summary>The kind at that index, or null. Index 0 is the base dust.</summary>
        public DustKindDef DustKind(int index) =>
            dustKinds != null && index >= 0 && index < dustKinds.Length ? dustKinds[index] : null;

        /// <summary>Index of the dust kind with this id, or -1.</summary>
        public int DustKindIndex(string id)
        {
            if (dustKinds == null || id == null) return -1;
            for (int i = 0; i < dustKinds.Length; i++)
                if (dustKinds[i] != null && dustKinds[i].id == id) return i;
            return -1;
        }

        /// <summary>Index of the stamina preset holding these two drains, or -1 when none does (18 §A1).</summary>
        public int StaminaPresetIndex(double holdDrain, double idleDrain)
        {
            if (staminaPresets == null) return -1;
            for (int i = 0; i < staminaPresets.Length; i++)
            {
                var s = staminaPresets[i];
                if (s == null) continue;
                if (Math.Abs(s.holdDrain - holdDrain) < 1e-9 && Math.Abs(s.idleDrain - idleDrain) < 1e-9) return i;
            }
            return -1;
        }

        /// <summary>
        /// The stamina preset after the one holding these two drains, wrapping around; the first one when none matches
        /// (so a hand-edited asset value lands on a known preset), and null when there are no presets.
        /// </summary>
        public StaminaPreset NextStaminaPreset(double holdDrain, double idleDrain)
        {
            if (staminaPresets == null || staminaPresets.Length == 0) return null;
            return staminaPresets[(StaminaPresetIndex(holdDrain, idleDrain) + 1) % staminaPresets.Length];
        }
    }
}
