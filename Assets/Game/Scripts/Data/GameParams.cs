using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// Background and dust colors for one central-star stage (14 §6): protostar, red dwarf, sun-like, blue giant.
    /// Shared by the run, result and tree screens.
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
        public double driftSpeed = 15;        // px/s
        public double dustMass = 1;

        [Header("Gravity (spec §6)")]
        public double gravityRadius = 180;    // px
        public double pullAccel = 600;        // px/s^2, upgrade target
        public double dustMaxSpeed = 400;     // px/s
        public double captureRadius = 30;     // px

        [Header("Stamina (spec §6)")]
        public double staminaMax = 60;        // upgrade target
        public double staminaHoldDrain = 1.0; // per second while holding (additional)
        public double staminaIdleDrain = 0.2; // per second, always

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

        [Header("Planet departure (13 §2)")]
        [Tooltip("After the pop, the planet stays in place this long.")]
        public double planetHoldSec = 0.4;
        [Tooltip("Then it flies to the vanishing point, shrinking and fading.")]
        public double planetDepartSec = 0.8;
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
        [Tooltip("Fast dust stretches along its velocity: length × up to dustStretchMax, reached at dustStretchFullSpeedPx (px/s).")]
        public double dustStretchMax = 4;
        public double dustStretchFullSpeedPx = 400;
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
        [Tooltip("By central star stage: 0 protostar (below the first star tier), 1 red dwarf, 2 sun-like, 3 blue giant. " +
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
                 "(540 = half the 1080p screen height, so a flash at the screen center is at most 0.3 × at every edge).")]
        public double flashAlphaCap = 0.35;
        public double flashMinRiseSec = 0.1;
        public double flashEdgeShare = 0.3;
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
        [Tooltip("Central star disc size and ignition flash base size (px) by stage: protostar, red dwarf, sun-like, blue giant.")]
        public float[] starCorePx = { 30f, 38f, 52f, 72f };
        public float[] starGlowPx = { 170f, 200f, 260f, 340f };
        [Tooltip("Result screen flash when tier 9 is made for the first time: seconds, radius growth relative to the glow, and peak alpha (flash rule, 15 §3).")]
        public double ignitionSec = 0.9;
        public double ignitionScale = 4;
        public double resultIgnitionFlashAlpha = 0.3;

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

        [Header("Save (02 §6)")]
        [Tooltip("While a run is going, its progress is saved this often (seconds) so a killed game keeps the run's income.")]
        public double pendingSaveIntervalSec = 5;
    }
}
