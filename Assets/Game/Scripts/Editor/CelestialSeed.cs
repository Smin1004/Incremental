using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Incremental.EditorTools
{
    /// <summary>
    /// Seed data of the celestial table (keep equal to Assets/Game/Data/CelestialTable.asset; Validate Data warns on drift).
    /// Mass and price: tiers 1-3 week-1 values, tiers 4-11 from 10 §3.4. Display size and look: 14 §3.
    /// </summary>
    public static class CelestialSeed
    {
        public static List<CelestialTier> Tiers() => new List<CelestialTier>
        {
            Tier(1, 10, 1, 20, new Color(0.62f, 0.62f, 0.62f), Asteroid()),
            Tier(2, 25, 6, 26, new Color(0.55f, 0.8f, 1f), Comet()),
            Tier(3, 60, 40, 34, new Color(0.62f, 0.42f, 0.25f), DwarfPlanet()),
            Tier(4, 150, 250, 42, new Color(0.8f, 0.8f, 0.82f), Moon()),
            Tier(5, 400, 2000, 50, new Color(0.6f, 0.3f, 0.2f), RockyPlanet()),
            Tier(6, 1000, 15000, 60, new Color(0.25f, 0.5f, 0.95f), Terran()),
            Tier(7, 2500, 1.2e5, 76, new Color(0.8f, 0.65f, 0.35f), GasGiant()),
            Tier(8, 6000, 1e6, 88, new Color(0.45f, 0.2f, 0.14f), BrownDwarf()),
            Tier(9, 15000, 1e7, 104, new Color(0.95f, 0.25f, 0.2f), RedDwarf()),
            Tier(10, 40000, 1e8, 124, new Color(1f, 0.9f, 0.35f), SunLike()),
            Tier(11, 1e5, 1e9, 148, new Color(0.72f, 0.85f, 1f), BlueGiant()),
        };

        static CelestialTier Tier(int tier, double mass, double price, double sizePx, Color color, PlanetLook look) =>
            new CelestialTier { tier = tier, requiredMass = mass, salePrice = price, sizePx = sizePx, color = color, look = look };

        // Rock: grey, mottled, small craters, dented silhouette, tumbling.
        static PlanetLook Asteroid() => new PlanetLook
        {
            surface = SurfaceType.Rock,
            baseColor = new Color(0.6f, 0.58f, 0.55f), darkColor = new Color(0.3f, 0.29f, 0.28f),
            glowColor = new Color(0.8f, 0.8f, 0.8f),
            noiseScale = 3.2f, patchLevel = 0.5f, patchSoftness = 0.22f,
            craterDensity = 0.55f, craterSize = 0.4f, craterScale = 3.2f,
            irregularity = 0.32f,
            spinDegPerSec = 45f, axisTiltDeg = 25f,
        };

        // Rock, bright blue-white nucleus, coma and a tail away from the light. No spin.
        // 18 §A3: a shorter, narrower tail that dies out at its end, and a wider coma, instead of the cone it was.
        static PlanetLook Comet() => new PlanetLook
        {
            surface = SurfaceType.Rock,
            baseColor = new Color(0.88f, 0.95f, 1f), darkColor = new Color(0.52f, 0.62f, 0.74f),
            glowColor = new Color(0.5f, 0.78f, 1f),
            noiseScale = 2.6f, patchLevel = 0.5f, patchSoftness = 0.3f,
            irregularity = 0.12f,
            glowSize = 1.1f, glowStrength = 0.75f,
            tailLength = 3.5f, tailWidth = 0.4f, tailSpread = 0.2f, tailFadePower = 1.8f,
        };

        // Rock: round, brown, cratered.
        static PlanetLook DwarfPlanet() => new PlanetLook
        {
            surface = SurfaceType.Rock,
            baseColor = new Color(0.66f, 0.5f, 0.36f), darkColor = new Color(0.38f, 0.26f, 0.17f),
            glowColor = new Color(0.9f, 0.75f, 0.6f),
            noiseScale = 2.2f, patchLevel = 0.5f, patchSoftness = 0.18f,
            craterDensity = 0.5f, craterSize = 0.42f, craterScale = 3f,
            spinDegPerSec = 28f, axisTiltDeg = -15f,
        };

        // Rock: light grey, large craters, dark maria.
        static PlanetLook Moon() => new PlanetLook
        {
            surface = SurfaceType.Rock,
            baseColor = new Color(0.84f, 0.84f, 0.86f), darkColor = new Color(0.4f, 0.4f, 0.44f),
            glowColor = new Color(0.9f, 0.9f, 0.95f),
            noiseScale = 1.5f, patchLevel = 0.44f, patchSoftness = 0.05f,
            craterDensity = 0.6f, craterSize = 0.5f, craterScale = 2.4f,
            spinDegPerSec = 18f, axisTiltDeg = 8f,
        };

        // Rock: red-brown terrain, white polar caps.
        static PlanetLook RockyPlanet() => new PlanetLook
        {
            surface = SurfaceType.Rock,
            baseColor = new Color(0.78f, 0.42f, 0.24f), darkColor = new Color(0.42f, 0.19f, 0.12f),
            accentColor = new Color(0.96f, 0.96f, 1f), glowColor = new Color(1f, 0.7f, 0.55f),
            noiseScale = 2.4f, patchLevel = 0.5f, patchSoftness = 0.12f,
            craterDensity = 0.2f, craterSize = 0.3f, craterScale = 4f,
            polarCap = 0.86f,
            spinDegPerSec = 32f, axisTiltDeg = 25f,
        };

        // Terran: oceans, continents, clouds on their own spin, blue atmosphere rim.
        static PlanetLook Terran() => new PlanetLook
        {
            surface = SurfaceType.Terran,
            baseColor = new Color(0.3f, 0.5f, 0.24f), darkColor = new Color(0.07f, 0.26f, 0.66f),
            accentColor = new Color(0.97f, 0.98f, 1f), glowColor = new Color(0.42f, 0.7f, 1f),
            noiseScale = 1.8f, patchLevel = 0.55f, patchSoftness = 0.035f,
            polarCap = 0.9f, cloudCover = 0.42f, cloudSpinMult = 1.6f,
            spinDegPerSec = 30f, axisTiltDeg = 23f,
            glowSize = 0.14f, glowStrength = 0.9f,
        };

        // Gas: ochre bands, a storm spot, ring.
        static PlanetLook GasGiant() => new PlanetLook
        {
            surface = SurfaceType.Gas,
            baseColor = new Color(0.94f, 0.84f, 0.64f), darkColor = new Color(0.72f, 0.47f, 0.24f),
            accentColor = new Color(0.8f, 0.36f, 0.22f), glowColor = new Color(1f, 0.9f, 0.7f),
            noiseScale = 2f, bandCount = 7f, turbulence = 0.9f, spotSize = 0.24f, spotLatitude = 0.3f,
            spinDegPerSec = 40f, axisTiltDeg = 12f,
            ringInner = 1.35f, ringOuter = 2.2f, ringColor = new Color(0.9f, 0.82f, 0.64f, 0.85f),
        };

        // Gas: dark bands, self-lit, red outer glow (reads on the dark background).
        static PlanetLook BrownDwarf() => new PlanetLook
        {
            surface = SurfaceType.Gas,
            baseColor = new Color(0.5f, 0.22f, 0.13f), darkColor = new Color(0.2f, 0.07f, 0.05f),
            glowColor = new Color(1f, 0.3f, 0.12f),
            noiseScale = 2f, bandCount = 5f, turbulence = 0.8f,
            emission = 0.35f,
            spinDegPerSec = 26f, axisTiltDeg = -8f,
            glowSize = 0.5f, glowStrength = 0.8f,
        };

        // Stars: self-lit granulation, limb darkening, corona growing with the tier.
        static PlanetLook RedDwarf() => new PlanetLook
        {
            surface = SurfaceType.Star,
            baseColor = new Color(1f, 0.42f, 0.2f), darkColor = new Color(0.66f, 0.09f, 0.04f),
            glowColor = new Color(1f, 0.32f, 0.16f),
            noiseScale = 4.5f,
            spinDegPerSec = 8f,
            glowSize = 0.45f, glowStrength = 0.9f,
        };

        static PlanetLook SunLike() => new PlanetLook
        {
            surface = SurfaceType.Star,
            baseColor = new Color(1f, 0.95f, 0.62f), darkColor = new Color(0.95f, 0.52f, 0.1f),
            glowColor = new Color(1f, 0.8f, 0.36f),
            noiseScale = 5.5f,
            spinDegPerSec = 6f,
            glowSize = 0.9f, glowStrength = 1f,
        };

        static PlanetLook BlueGiant() => new PlanetLook
        {
            surface = SurfaceType.Star,
            baseColor = new Color(0.86f, 0.94f, 1f), darkColor = new Color(0.32f, 0.52f, 1f),
            glowColor = new Color(0.55f, 0.75f, 1f),
            noiseScale = 5f,
            spinDegPerSec = 5f,
            glowSize = 1.3f, glowStrength = 1.1f,
        };

        /// <summary>
        /// Menu: copies display size and look of every seed tier into the asset (mass, price and color stay as they are).
        /// The looks are presentation data; this keeps the asset equal to the seed after the seed changes.
        /// </summary>
        [MenuItem("Incremental/Setup/4. Apply Seed Looks")]
        public static void ApplySeedLooks()
        {
            var c = AssetDatabase.LoadAssetAtPath<CelestialTable>("Assets/Game/Data/CelestialTable.asset");
            if (c == null)
            {
                Debug.LogError("[Setup] CelestialTable asset not found.");
                return;
            }
            int n = 0;
            foreach (var s in Tiers())
            {
                var a = c.Get(s.tier);
                if (a == null) continue;
                a.sizePx = s.sizePx;
                a.look = s.look.Clone();
                n++;
            }
            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Setup] seed looks applied to {n} tiers");
        }
    }
}
