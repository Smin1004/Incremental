using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// 24 §3, §16.1 수축. When its cooldown fills, a ring leaves the gravity source and reaches the end of its range in
    /// ringSec, whatever the range is: a share of the screen at first, more with every level of the range nodes, and the
    /// far end of the screen at their last level. The dust the ring passes in a tick gets one push toward the gravity
    /// source, the same for every dust whether the gravity reaches it or not, and lights up (24 §8); so the field inside
    /// the range turns inward as one, and the reaction spreads outward with the ring. Dust already captured is mass,
    /// not dust, and is not touched. The ring is drawn the way the gravity radius ring is, in blue-white; it stops at
    /// the end of its range and fades out there, which is how the player sees how far it reached.
    ///
    /// 15th pass feedback: with ImplodeDef.shockwave on (the default) the ring is drawn as a shockwave instead of the line:
    /// a thin bright front, a wide soft wake just behind it and a flash where it leaves (GlowFx, FxRing quads), on top of
    /// the lens ripple (26 §12.2 D2). Same radius and timing as the push, so it is a picture only.
    /// </summary>
    public sealed class ImplodeAbility : Ability
    {
        /// <summary>With the gravity radius ring: over the dust, under the bodies.</summary>
        const int SortingOrder = 5;

        LineRenderer ring;
        Vector3[] points = new Vector3[0];
        bool active;
        Vector2 origin;
        double age;
        float reach;
        int pushed;

        ImplodeDef D => root.abilityTable.implode;

        public override float EffectAge => active ? (float)age : -1f;

        /// <summary>One ring at a time: a charge that fills while a ring is still on its way waits for it.</summary>
        protected override bool Ready => !active;

        protected override void Build(Transform parent)
        {
            var go = new GameObject("ImplodeRing");
            go.transform.SetParent(parent, false);
            ring = go.AddComponent<LineRenderer>();
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.alignment = LineAlignment.TransformZ;
            ring.textureMode = LineTextureMode.Stretch;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            if (root.unlitMaterial != null) ring.sharedMaterial = root.unlitMaterial;
            ring.sortingOrder = SortingOrder;
            UIBuilder.UseWhiteTexture(ring);
            ring.enabled = false;
        }

        protected override void Trigger(in EffectiveStats s)
        {
            var d = D;
            active = true;
            age = 0;
            // The ring stays where it started; the gravity source may move on while it spreads.
            origin = root.GravityWorld;
            // The whole screen is the diagonal of everything dust can be in, the band outside the screen included: from
            // wherever the gravity source is, no dust is further than that.
            Rect view = root.CameraArea();
            Rect all = root.Dust.BoundsOf(view);
            float farthest = Mathf.Sqrt(all.width * all.width + all.height * all.height);
            // 24 §17.1: below the last level the reach is a share of the screen's half diagonal, the middle of the
            // screen to a corner, at the world scale of the moment; and never less than a few gravity radii.
            float halfDiagonal = Mathf.Sqrt(view.width * view.width + view.height * view.height) * 0.5f;
            bool whole = d.WholeScreen(Level);
            double share = d.RangeScreenShare(Level);
            float byScreen = halfDiagonal * (float)share;
            float least = (float)(s.gravityRadius * d.rangeMinRadii / p.pixelsPerUnit);
            reach = whole ? farthest : Mathf.Min(farthest, Mathf.Max(byScreen, least));
            pushed = 0;
            string why = whole ? "last range level: the whole screen"
                : least > byScreen ? "the least, " + d.rangeMinRadii.ToString("0.##") + " gravity radii; " + (share * 100).ToString("F0") + "% of the half diagonal would be " + (byScreen * p.pixelsPerUnit).ToString("F0") + " px"
                : (share * 100).ToString("F0") + "% of the screen's half diagonal " + (halfDiagonal * p.pixelsPerUnit).ToString("F0") + " px";
            Log("fired at " + RunClock + ": push " + d.PullSpeedPx(Level).ToString("F0") + " px/s, reach " +
                (reach * p.pixelsPerUnit).ToString("F0") + " px (" + why + "), ring out in " + d.ringSec.ToString("0.##") + "s");
        }

        protected override void Step(double dt, bool holding, in EffectiveStats s)
        {
            if (!active) return;
            var d = D;
            bool spreading = age < d.ringSec;
            if (spreading)
            {
                // Each tick pushes the band the ring crossed in it, so every dust is hit once as the ring goes by.
                float speed = (float)(d.PullSpeedPx(Level) / p.pixelsPerUnit);
                pushed += root.Dust.Impulse(origin, RadiusAt(age), RadiusAt(age + dt), root.GravityWorld, speed);
            }
            age += dt;
            if (spreading && age >= d.ringSec)
                Log("ring done: " + pushed + " dust pushed, " + (root.Dust.Count - root.Dust.OutsideCount) + " on screen and " +
                    root.Dust.OutsideCount + " outside it now");
            // The ring is at the end of its range now and only fades (24 §16.1); the next one waits for that.
            if (age >= d.ringSec + Math.Max(0.0, d.ringFadeSec)) active = false;
        }

        /// <summary>26 §12.2 D2: true while a ring is out (spreading or fading where it stopped).</summary>
        public bool WaveOn => active;
        public Vector2 WaveOrigin => origin;
        /// <summary>The ring's radius (world) <paramref name="lead"/> seconds after the last tick.</summary>
        public float WaveRadius(float lead) => RadiusAt(age + lead);
        /// <summary>1 while it spreads, down to 0 as it fades where it stopped.</summary>
        public float WaveAlpha(float lead)
        {
            var d = D;
            return d.ringFadeSec > 0 ? 1f - Mathf.Clamp01((float)((age + lead - d.ringSec) / d.ringFadeSec)) : (age + lead < d.ringSec ? 1f : 0f);
        }

        float RadiusAt(double seconds) => reach * Mathf.Clamp01((float)(seconds / Math.Max(1e-3, D.ringSec)));

        protected override void OnClear()
        {
            active = false;
            age = 0;
        }

        public override void Render(bool show, float lead)
        {
            float intensity = root.EffectIntensity;
            bool on = show && active && intensity > 0f;
            if (ring.enabled != on) ring.enabled = on;
            if (!on) return;

            var d = D;
            float radius = RadiusAt(age + lead);
            if (d.shockwave)
            {
                if (ring.enabled) ring.enabled = false;
                DrawShock(d, radius, lead, intensity);
                return;
            }
            int n = Mathf.Max(3, d.ringSegments);
            if (points.Length != n)
            {
                points = new Vector3[n];
                ring.positionCount = n;
            }
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                points[i] = new Vector3(origin.x + Mathf.Cos(a) * radius, origin.y + Mathf.Sin(a) * radius, 0f);
            }
            ring.SetPositions(points);
            // As with the gravity ring (22 §6), the width follows the world scale so it is the same on screen.
            ring.widthMultiplier = (float)(d.ringWidthPx * root.WorldScale / p.pixelsPerUnit);
            // Full while it spreads, then it fades where it stopped.
            float fade = d.ringFadeSec > 0 ? Mathf.Clamp01((float)((age + lead - d.ringSec) / d.ringFadeSec)) : 0f;
            Color c = d.ringColor;
            c.a *= (1f - fade) * intensity;
            ring.startColor = ring.endColor = c;
        }

        /// <summary>15th pass feedback: the shockwave at <paramref name="radius"/> (world).</summary>
        void DrawShock(ImplodeDef d, float radius, float lead, float intensity)
        {
            var glow = root.Glow;
            if (glow == null) return;
            float ppu = (float)p.pixelsPerUnit;
            float scale = (float)root.WorldScale / ppu;
            float t = (float)(age + lead);
            float spread = Mathf.Clamp01(t / Mathf.Max(1e-3f, (float)d.ringSec));
            float fade = d.ringFadeSec > 0 ? Mathf.Clamp01((float)((t - d.ringSec) / d.ringFadeSec)) : (spread >= 1f ? 1f : 0f);
            float alive = (1f - fade) * intensity;
            Color c = d.shockColor.linear;

            // The flash where it leaves: up at once, gone in shockFlashSec.
            float flashSec = (float)d.shockFlashSec;
            if (t < flashSec && flashSec > 0f)
            {
                float k = t / flashSec;
                float f = (float)d.shockFlashBright * (1f - k) * (1f - k) * intensity;
                glow.Draw(origin, (float)d.shockFlashPx * scale * (0.6f + 0.6f * k), new Color(c.r, c.g, c.b, f), 0.25f + 0.4f * k, 0.25f, 1f - k);
            }
            if (alive <= 0f || radius <= 1e-4f) return;

            // The front dims as the wave spreads, as one would; the wake rides just inside it.
            float bright = Mathf.Lerp(1f, (float)d.shockEndBright, spread) * alive;
            float front = (float)d.shockFrontWidthPx * scale;
            float wake = (float)d.shockWakeWidthPx * scale;
            // One quad a little larger than the circle (FxRing fades the last eighth of the quad out); its ring radius and
            // width are shares of the half size.
            float half = radius * 1.15f + wake + front;
            float frontAt = radius / half;
            glow.Draw(origin, half * 2f, new Color(c.r, c.g, c.b, (float)d.shockFrontBright * bright), frontAt, front * 0.5f / half, 0f);
            float wakeAt = Mathf.Max(0f, radius - wake * (float)d.shockWakeLag) / half;
            glow.Draw(origin, half * 2f, new Color(c.r, c.g, c.b, (float)d.shockWakeBright * bright), wakeAt, wake * 0.5f / half, 0f);
        }
    }
}
