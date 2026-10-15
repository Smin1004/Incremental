using UnityEngine;
using UnityEngine.Rendering;

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
    /// implode wave (D2) and the bundle ripple (D4) in their LensSlots; the buff frame (E5); the hit stop and the shake
    /// (E1). Everything but the hit stop is drawing only, and all of it follows the effect intensity: at 0 none of it
    /// moves or lights anything. The hit stop holds the simulation still for a few ticks and changes nothing it adds up.
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

        /// <summary>E1: seconds of hit stop left, and of shake.</summary>
        double hitStopLeft;
        float shakeAge = float.MaxValue;
        float shakeSeed;
        Vector3 shakeRestore;
        bool shaken;
        /// <summary>E1: the highest tier made before this one, to know a first.</summary>
        int highestSeen;

        /// <summary>E5: the frame, and the most buff time seen since this buff started (its full length).</summary>
        MeshRenderer frame;
        Mesh frameMesh;
        readonly Vector3[] frameVerts = new Vector3[8];
        readonly Color[] frameColors = new Color[8];
        double buffFull;
        bool loggedBuff;

        /// <summary>E1: true while the simulation is held still (captures and checks).</summary>
        public bool HitStopping => hitStopLeft > 0;
        /// <summary>E1: seconds since the last shake started, or MaxValue.</summary>
        public float ShakeAge => shakeAge;

        VisualToggles T => root.Visual;

        public void Init(GameRoot gameRoot)
        {
            root = gameRoot;
            p = root.gameParams;
            LensSlots.ClearAll();
            BuildFrame();
            root.PlanetCreated += OnPlanetCreated;
            root.RunStarted += () => { highestSeen = root.Meta.highestTierCreated; hitStopLeft = 0; };
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
        }

        /// <summary>The frame is a quad ring from the edge of the view (gold) to a rectangle inside it (clear), like the black hole's darkness.</summary>
        void BuildFrame()
        {
            var go = new GameObject("BuffFrame", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            frameMesh = new Mesh { name = "BuffFrame" };
            frameMesh.MarkDynamic();
            var tris = new int[24];
            for (int i = 0; i < 4; i++)
            {
                int o0 = i, o1 = (i + 1) % 4, i0 = 4 + i, i1 = 4 + (i + 1) % 4;
                tris[i * 6] = o0; tris[i * 6 + 1] = o1; tris[i * 6 + 2] = i1;
                tris[i * 6 + 3] = o0; tris[i * 6 + 4] = i1; tris[i * 6 + 5] = i0;
            }
            frameMesh.vertices = frameVerts;
            frameMesh.colors = frameColors;
            frameMesh.triangles = tris;
            go.GetComponent<MeshFilter>().sharedMesh = frameMesh;
            frame = go.GetComponent<MeshRenderer>();
            if (root.unlitMaterial != null) frame.sharedMaterial = root.unlitMaterial;
            frame.shadowCastingMode = ShadowCastingMode.Off;
            frame.receiveShadows = false;
            frame.lightProbeUsage = LightProbeUsage.Off;
            // Under the black hole's darkness (40), over the dust and the bodies.
            frame.sortingOrder = 39;
            UIBuilder.UseWhiteTexture(frame);
            frame.enabled = false;
        }

        // ---------------- E1 hit stop and shake ----------------

        void OnPlanetCreated(CelestialTier tier)
        {
            if (tier == null) return;
            bool first = tier.tier > highestSeen;
            if (tier.tier > highestSeen) highestSeen = tier.tier;
            if (first) Kick("tier " + tier.tier + " made for the first time");
        }

        /// <summary>The black hole lets go (24 §19.1).</summary>
        public void OnBlackHoleRelease() => Kick("black hole release");

        /// <summary>E1: the hit stop and the shake, when they are on and the effect intensity is above 0.</summary>
        void Kick(string why)
        {
            if (!T.hitStopShake || root.EffectIntensity <= 0f) return;
            hitStopLeft = System.Math.Max(hitStopLeft, p.hitStopSec);
            shakeAge = 0f;
            shakeSeed = Random.value * 100f;
            Debug.Log("[Visual] hit stop " + (p.hitStopSec * 1000).ToString("0") + " ms + shake " + p.shakePx.ToString("0.#") + " px: " + why);
        }

        /// <summary>
        /// E1: called by the fixed tick. True when this tick is held still: the run does not move on, so nothing it
        /// counts (capture, income, time) changes; only when it happens moves by a few ticks.
        /// </summary>
        public bool TakeHitStop(double dt)
        {
            if (hitStopLeft <= 0) return false;
            hitStopLeft -= dt;
            return true;
        }

        /// <summary>E1: the shake moves the camera for the drawing only and puts it back right after, so the input and the simulation never see it.</summary>
        void BeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (root == null || cam != root.Cam || shaken) return;
            float sec = Mathf.Max(0.01f, (float)p.shakeSec);
            if (shakeAge >= sec || !T.hitStopShake) return;
            float k = 1f - shakeAge / sec;
            float px = (float)p.shakePx * k * k * root.EffectIntensity;
            float worldPerPx = cam.orthographicSize * 2f / Mathf.Max(1, cam.pixelHeight);
            float t = shakeAge * 60f + shakeSeed;
            var offset = new Vector3(Mathf.PerlinNoise(t, 0.3f) * 2f - 1f, Mathf.PerlinNoise(0.7f, t) * 2f - 1f, 0f) * px * worldPerPx;
            shakeRestore = cam.transform.position;
            cam.transform.position = shakeRestore + offset;
            shaken = true;
        }

        void EndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (!shaken || root == null || cam != root.Cam) return;
            cam.transform.position = shakeRestore;
            shaken = false;
        }

        // ---------------- E5 buff frame ----------------

        void UpdateFrame(bool run, float intensity)
        {
            var crystal = run && T.buffVignette && root.Abilities != null ? root.Abilities.Get("crystal") as CrystalAbility : null;
            float strength = crystal != null && crystal.Level.unlocked ? crystal.BuffStrength : 0f;
            if (crystal == null || strength <= 0f || intensity <= 0f)
            {
                buffFull = 0;
                if (frame.enabled) frame.enabled = false;
                return;
            }
            double left = crystal.BuffLeft;
            // A crystal taken during the buff starts the clock over; the longest left seen is the buff's full length.
            if (left > buffFull) buffFull = left;
            float share = buffFull > 0 ? Mathf.Clamp01((float)(left / buffFull)) : 0f;
            float period = Mathf.Max(0.1f, (float)p.buffVignettePulseSec);
            float pulse = 1f - (float)p.buffVignettePulseDepth * (0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f / period));
            float stacks = crystal.Stacks >= 2 ? (float)p.buffVignetteStackMult : 1f;
            float alpha = (float)p.buffVignetteAlpha * strength * (0.35f + 0.65f * share) * pulse * stacks * intensity;
            if (!loggedBuff)
            {
                loggedBuff = true;
                Debug.Log("[Visual] buff frame (E5) on: alpha " + p.buffVignetteAlpha.ToString("0.###") + ", outer " + (p.buffVignetteEdge * 100).ToString("0") + "% of the screen");
            }
            var cam = root.Cam;
            float h = cam.orthographicSize * 1.02f, w = h * cam.aspect;
            var c = cam.transform.position;
            float edge = Mathf.Clamp01((float)p.buffVignetteEdge) * 2f;
            float ih = h * (1f - edge), iw = w * (1f - edge * cam.pixelHeight / Mathf.Max(1f, cam.pixelWidth));
            frameVerts[0] = new Vector3(c.x - w, c.y - h, 0f);
            frameVerts[1] = new Vector3(c.x + w, c.y - h, 0f);
            frameVerts[2] = new Vector3(c.x + w, c.y + h, 0f);
            frameVerts[3] = new Vector3(c.x - w, c.y + h, 0f);
            frameVerts[4] = new Vector3(c.x - iw, c.y - ih, 0f);
            frameVerts[5] = new Vector3(c.x + iw, c.y - ih, 0f);
            frameVerts[6] = new Vector3(c.x + iw, c.y + ih, 0f);
            frameVerts[7] = new Vector3(c.x - iw, c.y + ih, 0f);
            Color gold = p.buffVignetteColor;
            var outer = new Color(gold.r, gold.g, gold.b, Mathf.Clamp01(alpha));
            var inner = new Color(gold.r, gold.g, gold.b, 0f);
            for (int i = 0; i < 4; i++)
            {
                frameColors[i] = outer;
                frameColors[4 + i] = inner;
            }
            frameMesh.vertices = frameVerts;
            frameMesh.colors = frameColors;
            frameMesh.RecalculateBounds();
            if (!frame.enabled) frame.enabled = true;
        }

        /// <summary>A bundle of <paramref name="count"/> was made at <paramref name="world"/> (24 §21.3).</summary>
        public void OnBundle(Vector2 world, double count)
        {
            if (count >= p.hitStopMinBundle) Kick("bundle x" + count.ToString("0"));
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
            if (shakeAge < float.MaxValue) shakeAge += Time.unscaledDeltaTime;
            UpdateFrame(run, intensity);

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
