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
    }
}
