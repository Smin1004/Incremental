using UnityEngine;

namespace Incremental
{
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
        public double dustSizePx = 3;
        [Tooltip("Max random heading change of drifting dust, radians per second.")]
        public double driftNoise = 2.0;
        [Tooltip("How fast dust speed relaxes back to driftSpeed after being pulled, 1/s.")]
        public double driftRelaxRate = 2.0;
        [Tooltip("Outward speed (px/s) of dust scattered when released below tier 1.")]
        public double scatterSpeedPx = 200;
        public double planetLifetimeSec = 1.5;
        [Tooltip("Extra scale at planet spawn (0.5 = 150%), multiplied by effectIntensity.")]
        public double planetPopScale = 0.5;
        public double planetPopDurationSec = 0.15;
        public Color dustColorA = Color.white;
        public Color dustColorB = new Color(0.65f, 0.82f, 1f);

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
        [Tooltip("Darkness of the crescent shadow on planets (0..1).")]
        public double planetShadow = 0.75;

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
        [Tooltip("Tiers above orbitBeltMaxTier show at most this many planets, then ×N.")]
        public int orbitIndividualMax = 12;
        [Tooltip("Stars (tiers above orbitMaxTier) made this run, drawn in the sky at most this many.")]
        public int skyStarsPerRun = 20;
        public int skyBackgroundStars = 160;
        [Tooltip("Entry animation and income count-up, seconds (13 §5).")]
        public double resultEntrySec = 1.2;
        public Color protostarColor = new Color(1f, 0.55f, 0.2f);

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
