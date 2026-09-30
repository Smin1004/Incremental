using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// 24 §3 수축. When its cooldown fills, a ring leaves the gravity source and reaches the length of the screen
    /// diagonal in ringSec. The dust the ring passes in a tick gets one push toward the gravity source, the same for
    /// every dust whether the gravity reaches it or not, and lights up (24 §8); so the whole field turns inward as one,
    /// and the reaction spreads outward with the ring. Dust already captured is mass, not dust, and is not touched.
    /// The ring is drawn the way the gravity radius ring is, in blue-white, and fades as it goes.
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
            active = true;
            age = 0;
            // The ring stays where it started; the gravity source may move on while it spreads.
            origin = root.GravityWorld;
            Rect a = root.CameraArea();
            reach = Mathf.Sqrt(a.width * a.width + a.height * a.height);
            pushed = 0;
            Log("fired at " + RunClock + ": push " + D.PullSpeedPx(Level).ToString("F0") + " px/s, ring out in " + D.ringSec.ToString("0.##") + "s");
        }

        protected override void Step(double dt, bool holding, in EffectiveStats s)
        {
            if (!active) return;
            var d = D;
            // Each tick pushes the band the ring crossed in it, so every dust is hit once as the ring goes by.
            float speed = (float)(d.PullSpeedPx(Level) / p.pixelsPerUnit);
            pushed += root.Dust.Impulse(origin, RadiusAt(age), RadiusAt(age + dt), root.GravityWorld, speed);
            age += dt;
            if (age < d.ringSec) return;
            active = false;
            Log("ring done: " + pushed + " dust pushed, " + root.Dust.Count + " on screen now");
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
            float k = Mathf.Clamp01((float)((age + lead) / Math.Max(1e-3, d.ringSec)));
            float radius = reach * k;
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
            Color c = d.ringColor;
            c.a *= (1f - k) * intensity;
            ring.startColor = ring.endColor = c;
        }
    }
}
