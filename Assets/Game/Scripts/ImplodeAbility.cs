using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// 24 §3, §16.1 수축. When its cooldown fills, a ring leaves the gravity source and reaches the end of its range in
    /// ringSec, whatever the range is: a few gravity radii at first, more with every level of the range nodes, and the
    /// far end of the screen at their last level. The dust the ring passes in a tick gets one push toward the gravity
    /// source, the same for every dust whether the gravity reaches it or not, and lights up (24 §8); so the field inside
    /// the range turns inward as one, and the reaction spreads outward with the ring. Dust already captured is mass,
    /// not dust, and is not touched. The ring is drawn the way the gravity radius ring is, in blue-white; it stops at
    /// the end of its range and fades out there, which is how the player sees how far it reached.
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
            // The far end of the screen is the diagonal: from wherever the gravity source is, no dust is further than that.
            Rect a = root.CameraArea();
            float screen = Mathf.Sqrt(a.width * a.width + a.height * a.height);
            bool whole = d.WholeScreen(Level);
            double radii = d.RangeRadii(Level);
            reach = whole ? screen : Mathf.Min(screen, (float)(s.gravityRadius * radii / p.pixelsPerUnit));
            pushed = 0;
            Log("fired at " + RunClock + ": push " + d.PullSpeedPx(Level).ToString("F0") + " px/s, reach " +
                (reach * p.pixelsPerUnit).ToString("F0") + " px (" +
                (whole ? "last range level: the whole screen" : radii.ToString("0.##") + " gravity radii") +
                "), ring out in " + d.ringSec.ToString("0.##") + "s");
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
                Log("ring done: " + pushed + " dust pushed, " + root.Dust.Count + " on screen now");
            // The ring is at the end of its range now and only fades (24 §16.1); the next one waits for that.
            if (age >= d.ringSec + Math.Max(0.0, d.ringFadeSec)) active = false;
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
    }
}
