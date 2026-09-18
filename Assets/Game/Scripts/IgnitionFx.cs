using UnityEngine;
using UnityEngine.Rendering;

namespace Incremental
{
    /// <summary>
    /// Star ignition on the run screen (14 §6, 10 §4.3). The first star ever: a screen flash, a light ring spreading from the
    /// new star and the background palette change, ignitionFirstSec in all. Every later star: a short flash and a smaller
    /// ring (ignitionRepeatSec). Flash and ring are multiplied by effectIntensity; at 0 only the palette changes. Logic time
    /// and input are untouched. Render only.
    /// </summary>
    public sealed class IgnitionFx : MonoBehaviour
    {
        static readonly int IdColor = Shader.PropertyToID("_Color");
        static readonly int IdRing = Shader.PropertyToID("_Ring");

        GameRoot root;
        GameParams p;
        SpriteRenderer flash;
        MeshRenderer ring;
        MaterialPropertyBlock mpb;
        bool ignitedThisRun;
        float age = -1f;
        float duration;
        float flashPeak;
        float ringFromPx, ringToPx;
        Color ringColor;

        /// <summary>Seconds since the last ignition started, or −1 (for captures and checks).</summary>
        public float Age => age;
        public bool LastWasFirst { get; private set; }

        public void Init(GameRoot gameRoot, Material ringMaterial)
        {
            root = gameRoot;
            p = root.gameParams;

            flash = new GameObject("Flash").AddComponent<SpriteRenderer>();
            flash.transform.SetParent(transform, false);
            flash.sprite = UIBuilder.SolidSprite();
            if (root.unlitMaterial != null) flash.sharedMaterial = root.unlitMaterial;
            flash.sortingOrder = 60;

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
            bool first = IsFirstIgnition(root.Meta.highestTierCreated, ignitedThisRun, p.orbitMaxTier);
            ignitedThisRun = true;
            LastWasFirst = first;
            duration = Mathf.Max(0.05f, (float)(first ? p.ignitionFirstSec : p.ignitionRepeatSec));
            flashPeak = (float)(first ? p.ignitionFlashAlpha : p.ignitionRepeatFlashAlpha);
            ringFromPx = (float)tier.sizePx;
            ringToPx = (float)(first ? p.ignitionRingPx : p.ignitionRepeatRingPx);
            ringColor = tier.look != null ? tier.look.glowColor : tier.color;
            ring.transform.position = new Vector3(root.CursorWorld.x, root.CursorWorld.y, 0f);
            age = 0f;
            if (first) root.Backdrop.BlendNextChange(duration);
        }

        void Update()
        {
            if (age < 0f) return;
            age += Time.deltaTime;
            float k = age / duration;
            float intensity = root.EffectIntensity;
            if (k >= 1f || intensity <= 0f)
            {
                Hide();
                if (k >= 1f) age = -1f;
                return;
            }

            // Screen flash: fast rise, then fades over the rest.
            var cam = root.Cam;
            float h = cam.orthographicSize * 2f, w = h * cam.aspect;
            var c = cam.transform.position;
            flash.transform.position = new Vector3(c.x, c.y, 0f);
            var b = flash.sprite.bounds.size;
            flash.transform.localScale = new Vector3(w * 1.05f / b.x, h * 1.05f / b.y, 1f);
            float rise = Mathf.Max(0.001f, (float)p.ignitionFlashRiseSec);
            float env = age < rise ? age / rise : Mathf.Pow(1f - Mathf.Clamp01((age - rise) / Mathf.Max(0.01f, duration * (float)p.ignitionFlashDecayShare - rise)), 2f);
            flash.color = new Color(1f, 0.97f, 0.9f, flashPeak * env * intensity);
            flash.enabled = true;

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
            if (flash.enabled) flash.enabled = false;
            if (ring.enabled) ring.enabled = false;
        }
    }
}
