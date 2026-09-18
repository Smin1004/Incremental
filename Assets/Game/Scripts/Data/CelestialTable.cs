using System;
using System.Collections.Generic;
using UnityEngine;

namespace Incremental
{
    /// <summary>Surface type of the planet shader (14 §3). One shader draws all four; everything else is data.</summary>
    public enum SurfaceType
    {
        /// <summary>Terrain noise, light/dark patches, craters, polar caps. Irregular silhouette for asteroids, tail for comets.</summary>
        Rock,
        /// <summary>Ocean and continents from a noise threshold, polar caps, a cloud layer spinning at its own speed, atmosphere rim.</summary>
        Terran,
        /// <summary>Latitude bands with turbulence, optional ring.</summary>
        Gas,
        /// <summary>Self-lit granulation, limb darkening, corona. Not lit by the light source.</summary>
        Star,
    }

    /// <summary>
    /// Look of one celestial tier for the planet shader (14 §3). Sizes are in planet radii unless noted; colors are linear-ish
    /// 0..1. Fields a surface type does not use are ignored (listed per field). Validate Data checks the ranges.
    /// </summary>
    [Serializable]
    public sealed class PlanetLook
    {
        public SurfaceType surface = SurfaceType.Rock;

        [Header("Colors")]
        [Tooltip("Rock: light terrain. Terran: land. Gas: light bands. Star: bright granules.")]
        public Color baseColor = new Color(0.6f, 0.6f, 0.6f);
        [Tooltip("Rock: dark terrain / maria. Terran: ocean. Gas: dark bands. Star: dark lanes and limb.")]
        public Color darkColor = new Color(0.3f, 0.3f, 0.3f);
        [Tooltip("Polar caps and clouds.")]
        public Color accentColor = Color.white;
        [Tooltip("Outer glow: atmosphere (Terran), coma and tail (comet), red glow (brown dwarf), corona (Star). Also the rim tint.")]
        public Color glowColor = Color.white;

        [Header("Surface")]
        [Tooltip("Feature frequency on the unit sphere (bigger = smaller features).")]
        public float noiseScale = 2f;
        [Tooltip("Rock: where the terrain turns from dark to light (0..1). Terran: sea level.")]
        [Range(0f, 1f)] public float patchLevel = 0.5f;
        [Tooltip("Half width of the dark → light transition (small = sharp coastlines / maria edges).")]
        [Range(0f, 1f)] public float patchSoftness = 0.2f;
        [Tooltip("Rock: share of noise cells that hold a crater (0 = none).")]
        [Range(0f, 1f)] public float craterDensity;
        [Tooltip("Crater radius relative to a noise cell.")]
        [Range(0f, 1f)] public float craterSize = 0.35f;
        [Tooltip("Crater cells per planet radius.")]
        public float craterScale = 3f;
        [Tooltip("Rock / Terran: latitude (|sin|, 0..1) where the polar cap starts. 0 = no cap.")]
        [Range(0f, 1f)] public float polarCap;
        [Tooltip("Rock: depth of the dents in the silhouette (0 = round). Asteroids.")]
        [Range(0f, 0.6f)] public float irregularity;
        [Tooltip("Terran: cloud cover 0..1.")]
        [Range(0f, 1f)] public float cloudCover;
        [Tooltip("Terran: cloud layer spin relative to the surface spin.")]
        public float cloudSpinMult = 1.5f;
        [Tooltip("Gas: number of latitude bands from pole to pole.")]
        public float bandCount;
        [Tooltip("Gas: how much the bands are distorted.")]
        public float turbulence;
        [Tooltip("Gas: storm spot radius (latitude units, 0 = none) and latitude (-1..1), in the accent color. Gives the spin a landmark.")]
        [Range(0f, 0.5f)] public float spotSize;
        [Range(-1f, 1f)] public float spotLatitude;
        [Tooltip("Self-lit share 0..1 (brown dwarf). Stars are always fully self-lit.")]
        [Range(0f, 1f)] public float emission;

        [Header("Motion")]
        [Tooltip("Spin, degrees per second (0 = none).")]
        public float spinDegPerSec;
        [Tooltip("Roll of the spin axis in the screen plane, degrees (the tilt toward the viewer is GameParams.planetAxisPitchDeg).")]
        public float axisTiltDeg;

        [Header("Extras")]
        [Tooltip("Outer glow reach beyond the disc, radii (0 = none).")]
        public float glowSize;
        public float glowStrength;
        [Tooltip("Comet tail length, radii (0 = none). Points away from the light.")]
        public float tailLength;
        [Tooltip("Ring radii (ringOuter 0 = no ring). The ring lies in the equatorial plane.")]
        public float ringInner;
        public float ringOuter;
        public Color ringColor = new Color(1f, 1f, 1f, 0f);

        public bool HasRing => ringOuter > 0f;

        /// <summary>Quad half size in radii: the disc plus the farthest glow, tail or ring, with a small anti-aliasing margin.</summary>
        public float Extent()
        {
            float e = 1f;
            if (glowSize > 0f && glowStrength > 0f) e = Mathf.Max(e, 1f + glowSize);
            if (tailLength > 0f) e = Mathf.Max(e, 1f + tailLength);
            if (HasRing) e = Mathf.Max(e, ringOuter);
            return e + 0.06f;
        }

        public PlanetLook Clone() => (PlanetLook)MemberwiseClone();

        /// <summary>Field-by-field equality (seed drift check).</summary>
        public bool SameAs(PlanetLook o)
        {
            if (o == null) return false;
            return surface == o.surface && baseColor == o.baseColor && darkColor == o.darkColor && accentColor == o.accentColor &&
                   glowColor == o.glowColor && noiseScale == o.noiseScale && patchLevel == o.patchLevel &&
                   patchSoftness == o.patchSoftness && craterDensity == o.craterDensity && craterSize == o.craterSize &&
                   craterScale == o.craterScale && polarCap == o.polarCap && irregularity == o.irregularity &&
                   cloudCover == o.cloudCover && cloudSpinMult == o.cloudSpinMult && bandCount == o.bandCount &&
                   turbulence == o.turbulence && spotSize == o.spotSize && spotLatitude == o.spotLatitude &&
                   emission == o.emission && spinDegPerSec == o.spinDegPerSec &&
                   axisTiltDeg == o.axisTiltDeg && glowSize == o.glowSize && glowStrength == o.glowStrength &&
                   tailLength == o.tailLength && ringInner == o.ringInner && ringOuter == o.ringOuter && ringColor == o.ringColor;
        }
    }

    /// <summary>One celestial tier. The display name lives in UIStrings under <c>tier.&lt;n&gt;</c>, not in the asset.</summary>
    [Serializable]
    public sealed class CelestialTier
    {
        public int tier;
        public double requiredMass;
        public double salePrice;
        [Tooltip("Displayed disc diameter at the 1080p reference, px (run screen; the result screen scales it). Not a game rule.")]
        public double sizePx;
        [Tooltip("Representative color: asteroid belt dots, sky stars, the vanishing point glow.")]
        public Color color = Color.gray;
        [Tooltip("Planet shader look (14 §3).")]
        public PlanetLook look = new PlanetLook();
    }

    /// <summary>Celestial body tiers (10 §3.4). Tier numbers start at 1 and are contiguous and ascending.</summary>
    [CreateAssetMenu(fileName = "CelestialTable", menuName = "Incremental/Celestial Table")]
    public sealed class CelestialTable : ScriptableObject
    {
        public List<CelestialTier> tiers = new List<CelestialTier>();

        public int TierCount => tiers.Count;

        /// <summary>Highest tier number in the table (0 when empty).</summary>
        public int MaxTier => tiers.Count > 0 ? tiers[tiers.Count - 1].tier : 0;

        /// <summary>Returns the tier definition, or null if the tier does not exist.</summary>
        public CelestialTier Get(int tier)
        {
            for (int i = 0; i < tiers.Count; i++)
                if (tiers[i].tier == tier) return tiers[i];
            return null;
        }
    }
}
