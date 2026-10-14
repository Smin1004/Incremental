using UnityEngine;
using UnityEngine.Rendering;

namespace Incremental
{
    /// <summary>
    /// Star ignition on the run screen (14 §6, 10 §4.3). The first star ever: a radial flash from the new star (the flash
    /// rule of 15 §3), a light ring spreading from it and the background palette change, ignitionFirstSec in all. Every
    /// later star: a short flash and a smaller ring (ignitionRepeatSec). The blue giant that starts the ending (15 §4.2)
    /// gets the first-star version. Flash and ring are multiplied by effectIntensity; at 0 only the palette changes.
    /// Logic time and input are untouched. Render only.
    /// </summary>
    public sealed class IgnitionFx : MonoBehaviour
    {
        static readonly int IdColor = Shader.PropertyToID("_Color");
        static readonly int IdRing = Shader.PropertyToID("_Ring");

        GameRoot root;
        GameParams p;
        ScreenFlash flash;
        MeshRenderer ring;
        MaterialPropertyBlock mpb;
        bool ignitedThisRun;
        float age = -1f;
        float duration;
        float flashPeak;
        float ringFromPx, ringToPx;
        /// <summary>24 §24.3: 1, or AbilityTable.blackHole.blackHoleIgnitionScale for a star made while the black hole is on.</summary>
        float muted = 1f;
        Color ringColor;
        Vector3 center;

        /// <summary>Seconds since the last ignition started, or −1 (for captures and checks).</summary>
        public float Age => age;
        public bool LastWasFirst { get; private set; }

        public void Init(GameRoot gameRoot, Material ringMaterial)
        {
            root = gameRoot;
            p = root.gameParams;

            flash = ScreenFlash.Create("Flash", transform, root, 60);

            var ringGo = new GameObject("LightRing", typeof(MeshFilter), typeof(MeshRenderer));
            ringGo.transform.SetParent(transform, false);
            ringGo.GetComponent<MeshFilter>().sharedMesh = PlanetVisual.Quad();
            ring = ringGo.GetComponent<MeshRenderer>();
            if (ringMaterial != null) ring.sharedMaterial = ringMaterial;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.sortingOrder = 55;
            mpb = new MaterialPropertyBlock();

            root.PlanetCreated += OnPlanetCreated;
            root.RunStarted += () => ignitedThisRun = false;
            Hide();
        }

        /// <summary>True for tiers drawn as stars (above the last orbit tier, 13 §3).</summary>
        public static bool IsStarTier(int tier, int orbitMaxTier) => tier > orbitMaxTier;

        /// <summary>The first star ever: no star in any finished run and none earlier in this run.</summary>
        public static bool IsFirstIgnition(int highestTierBeforeRun, bool starEarlierThisRun, int orbitMaxTier) =>
            !starEarlierThisRun && !IsStarTier(highestTierBeforeRun, orbitMaxTier);

        void OnPlanetCreated(CelestialTier tier)
        {
            if (root.Phase != GamePhase.Run || !IsStarTier(tier.tier, p.orbitMaxTier)) return;
            bool firstStar = IsFirstIgnition(root.Meta.highestTierCreated, ignitedThisRun, p.orbitMaxTier);
            bool first = firstStar || root.EndingActive;
            ignitedThisRun = true;
            LastWasFirst = first;
            duration = Mathf.Max(0.05f, (float)(first ? p.ignitionFirstSec : p.ignitionRepeatSec));
            flashPeak = (float)(first ? p.ignitionFlashAlpha : p.ignitionRepeatFlashAlpha);
            ringFromPx = (float)tier.sizePx;
            ringToPx = (float)(first ? p.ignitionRingPx : p.ignitionRepeatRingPx);
            ringColor = tier.look != null ? tier.look.glowColor : tier.color;
            center = new Vector3(root.CursorWorld.x, root.CursorWorld.y, 0f);
            ring.transform.position = center;
            age = 0f;
            // 24 §24.3: a star made under the black hole lights up under it; its flash and ring are drawn over
            // everything (orders 55 and 60) and would wash the black disc grey.
            muted = root.GravityBodiesShrunk && root.abilityTable != null ? Mathf.Clamp01((float)root.abilityTable.blackHole.blackHoleIgnitionScale) : 1f;
            if (firstStar) root.Backdrop.BlendNextChange(duration);
        }

        void Update()
        {
            if (age < 0f) return;
            age += Time.deltaTime;
            float k = age / duration;
            float intensity = root.EffectIntensity * muted;
            if (k >= 1f || intensity <= 0f)
            {
                Hide();
                if (k >= 1f) age = -1f;
                return;
            }

            // Radial flash from the star: smooth rise, then fades over the first ignitionFlashDecayShare of the ignition.
            float env = ScreenFlash.Envelope(age, (float)p.ignitionFlashRiseSec, duration * (float)p.ignitionFlashDecayShare);
            flash.Show(center, p.ignitionFlashColor, flashPeak * env * intensity);

            // Light ring from the star outward.
            float e = 1f - (1f - k) * (1f - k);
            float diameterPx = Mathf.Lerp(ringFromPx, ringToPx, e);
            float quadPx = diameterPx + (float)p.ignitionRingWidthPx * 2f;
            float s = quadPx / (float)p.pixelsPerUnit;
            ring.transform.localScale = new Vector3(s, s, 1f);
            float fade = Mathf.Pow(1f - k, 1.5f);
            mpb.SetColor(IdColor, new Color(ringColor.r, ringColor.g, ringColor.b, (float)p.ignitionRingStrength * fade * intensity));
            mpb.SetVector(IdRing, new Vector4(diameterPx / quadPx, (float)p.ignitionRingWidthPx / quadPx, (float)p.ignitionRingCoreGlow * fade, 0f));
            ring.SetPropertyBlock(mpb);
            ring.enabled = true;
        }

        void Hide()
        {
            flash.Hide();
            if (ring.enabled) ring.enabled = false;
        }
    }
}
