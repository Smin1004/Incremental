using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// 26 §12: which items of the visual pass are on now. Starts from the data (GameParams, AbilityTable); the debug keys
    /// B, N and J change it at runtime only. Nothing here is saved.
    /// </summary>
    public sealed class VisualToggles
    {
        // 26 §12.1 background
        public bool nebula, parallax, farObject, nebulaSwirl;
        // 26 §12.2 lenses
        public bool gravityLens, blackHoleSwirl, implodeRipple, bundleRipple;
        // 26 §12.3 effects
        public bool bundlePop, holdPulse, buffVignette, hitStopShake;

        public void FromData(GameParams p, AbilityTable t)
        {
            nebula = p.backdropNebula;
            parallax = p.parallax;
            farObject = p.farObject;
            nebulaSwirl = p.nebulaSwirl;
            gravityLens = p.gravityLens;
            blackHoleSwirl = t != null && t.blackHole.lensTwistDeg != 0;
            implodeRipple = p.implodeRipple;
            bundleRipple = p.bundleRipple;
            bundlePop = p.bundlePop;
            holdPulse = p.holdPulse;
            buffVignette = p.buffVignette;
            hitStopShake = p.hitStopShake;
        }
    }

    /// <summary>
    /// 26 §12: the parts of the visual pass that belong to no single view. The lenses of the gravity source (D1), the
    /// implode wave (D2) and the bundle ripple (D4) in their LensSlots. Render only, and all of it follows the effect
    /// intensity: at 0 none of it moves anything.
    /// </summary>
    public sealed class VisualFx : MonoBehaviour
    {
        GameRoot root;
        GameParams p;

        /// <summary>D1: the push as of the last held frame, and where; it lets go from there after the release.</summary>
        float gravityPush;
        Vector2 gravityAt;
        float gravityRadius;
        float releaseFrom = -1f, releaseAge;

        /// <summary>D4: the ripple running now (age −1 = none).</summary>
        float rippleAge = -1f;
        Vector2 rippleAt;
        float rippleStrength;

        VisualToggles T => root.Visual;

        public void Init(GameRoot gameRoot)
        {
            root = gameRoot;
            p = root.gameParams;
            LensSlots.ClearAll();
        }

        /// <summary>A bundle of <paramref name="count"/> was made at <paramref name="world"/> (24 §21.3).</summary>
        public void OnBundle(Vector2 world, double count)
        {
            if (T.bundleRipple && count > 1)
            {
                rippleAge = 0f;
                rippleAt = world;
                rippleStrength = (float)(p.bundleRippleStrength * System.Math.Log(count));
                Debug.Log("[Visual] bundle ripple x" + count.ToString("0") + ": push " + rippleStrength.ToString("0.###") + " of the band width");
            }
        }

        void LateUpdate()
        {
            bool run = root.Phase == GamePhase.Run && root.Run != null && !root.EndingActive;
            float intensity = root.EffectIntensity;
            float dt = Time.deltaTime;
            float ppu = (float)p.pixelsPerUnit;
            float lead = root.Abilities != null ? root.Abilities.Lead : 0f;

            // D1 the gravity source's lens.
            bool holding = run && root.Run.holding && !root.Paused;
            if (holding && T.gravityLens)
            {
                int ring = root.Meta.unlockedMaxTier;
                float target = (float)(p.gravityLensStrength * root.GaugeFill() * (1.0 + ring * p.gravityLensRingBonus));
                // A body made while holding empties the gauge; the lens eases down instead of snapping.
                gravityPush += (target - gravityPush) * (1f - Mathf.Exp(-dt / Mathf.Max(0.01f, (float)p.gravityLensReleaseSec * 0.5f)));
                gravityAt = root.GravityWorld;
                gravityRadius = (float)(root.Effective.gravityRadius * p.gravityLensReach / ppu);
                releaseFrom = -1f;
            }
            else if (gravityPush > 0f)
            {
                // From the strength at the release to 0 in the release time, whatever the strength was.
                if (releaseFrom < 0f)
                {
                    releaseFrom = gravityPush;
                    releaseAge = 0f;
                }
                releaseAge += dt;
                float k = Mathf.Clamp01(releaseAge / Mathf.Max(0.01f, (float)p.gravityLensReleaseSec));
                gravityPush = !T.gravityLens || !run ? 0f : releaseFrom * (1f - k);
                if (gravityPush <= 0f) releaseFrom = -1f;
            }
            if (gravityPush * intensity > 1e-4f)
                LensSlots.Set(LensSlots.Gravity, gravityAt, gravityRadius, gravityPush * intensity, 1f, 0f, 0f, LensSlots.KindBulge);
            else LensSlots.Clear(LensSlots.Gravity);

            // D2 the implode wave.
            var implode = root.Abilities != null ? root.Abilities.Get("implode") as ImplodeAbility : null;
            if (run && T.implodeRipple && intensity > 0f && implode != null && implode.WaveOn && implode.Level.unlocked)
            {
                float radius = implode.WaveRadius(lead);
                float push = (float)p.implodeRippleStrength * implode.WaveAlpha(lead) * intensity;
                if (radius > 1e-3f && push > 1e-4f)
                    LensSlots.Set(LensSlots.Implode, implode.WaveOrigin, radius, push, 1f, 0f, (float)p.implodeRippleWidth, LensSlots.KindBand);
                else LensSlots.Clear(LensSlots.Implode);
            }
            else LensSlots.Clear(LensSlots.Implode);

            // D4 the bundle ripple.
            if (rippleAge >= 0f)
            {
                float sec = Mathf.Max(0.01f, (float)p.bundleRippleSec);
                rippleAge += dt;
                float k = rippleAge / sec;
                if (k >= 1f || !run || !T.bundleRipple) rippleAge = -1f;
                else
                {
                    float radius = (float)(p.bundleRippleRadiusPx * root.WorldScale / ppu) * (1f - (1f - k) * (1f - k));
                    float push = rippleStrength * (1f - k) * intensity;
                    if (radius > 1e-3f && push > 1e-4f)
                        LensSlots.Set(LensSlots.Bundle, rippleAt, radius, push, 1f, 0f, (float)p.bundleRippleWidth, LensSlots.KindBand);
                    else LensSlots.Clear(LensSlots.Bundle);
                }
            }
            if (rippleAge < 0f) LensSlots.Clear(LensSlots.Bundle);
        }

        void OnDisable() => LensSlots.ClearAll();
    }
}
