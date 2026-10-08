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
    /// 24 §21.2 연쇄 붕괴, the evolved form: one trigger sends chainCount waves chainGapSec apart, each from where the
    /// gravity source is at that moment and each pushing and lighting the field again, and the cooldown is shorter
    /// (chainCooldownSec). The field is hit three times in a second: the whole screen keeps folding inward.
    /// </summary>
    public sealed class ImplodeAbility : Ability
    {
        /// <summary>With the gravity radius ring: over the dust, under the bodies.</summary>
        const int SortingOrder = 5;

        struct Wave
        {
            public bool active;
            public Vector2 origin;
            public double age;
            public float reach;
            public int pushed;
        }

        Wave[] waves = new Wave[0];
        LineRenderer[] rings = new LineRenderer[0];
        Transform holder;
        Vector3[] points = new Vector3[0];
        /// <summary>24 §21.2: waves still to come from this trigger and seconds to the next one.</summary>
        int pending, launched, triggerWaves = 1;
        double launchIn;
        /// <summary>The reach every wave of this trigger has, decided when it fired.</summary>
        float plannedReach;

        ImplodeDef D => root.abilityTable.implode;

        public override float EffectAge
        {
            get
            {
                float oldest = -1f;
                for (int i = 0; i < waves.Length; i++)
                    if (waves[i].active && (float)waves[i].age > oldest) oldest = (float)waves[i].age;
                return oldest;
            }
        }

        /// <summary>Waves on their way right now (captures and checks).</summary>
        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < waves.Length; i++) if (waves[i].active) n++;
                return n;
            }
        }

        /// <summary>One trigger at a time: a charge that fills while waves are still on their way waits for them.</summary>
        protected override bool Ready => pending == 0 && ActiveCount == 0;

        protected override void Build(Transform parent)
        {
            holder = new GameObject("ImplodeRings").transform;
            holder.SetParent(parent, false);
            Grow(1);
        }

        void Grow(int n)
        {
            if (waves.Length >= n) return;
            int had = waves.Length;
            Array.Resize(ref waves, n);
            Array.Resize(ref rings, n);
            for (int i = had; i < n; i++)
            {
                var go = new GameObject("ImplodeRing" + i);
                go.transform.SetParent(holder, false);
                var ring = go.AddComponent<LineRenderer>();
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
                rings[i] = ring;
            }
        }

        protected override void Trigger(in EffectiveStats s)
        {
            var d = D;
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
            plannedReach = whole ? farthest : Mathf.Min(farthest, Mathf.Max(byScreen, least));
            string why = whole ? "last range level: the whole screen"
                : least > byScreen ? "the least, " + d.rangeMinRadii.ToString("0.##") + " gravity radii; " + (share * 100).ToString("F0") + "% of the half diagonal would be " + (byScreen * p.pixelsPerUnit).ToString("F0") + " px"
                : (share * 100).ToString("F0") + "% of the screen's half diagonal " + (halfDiagonal * p.pixelsPerUnit).ToString("F0") + " px";

            // 24 §21.2: evolved, the trigger is a chain of waves; the first goes this tick, the rest follow.
            triggerWaves = Level.evolved ? Math.Max(1, d.chainCount) : 1;
            Grow(triggerWaves);
            pending = triggerWaves;
            launched = 0;
            launchIn = 0;
            Log("fired at " + RunClock + ": push " + d.PullSpeedPx(Level).ToString("F0") + " px/s, reach " +
                (plannedReach * p.pixelsPerUnit).ToString("F0") + " px (" + why + "), ring out in " + d.ringSec.ToString("0.##") + "s" +
                (triggerWaves > 1 ? ", " + triggerWaves + " waves " + d.chainGapSec.ToString("0.##") + "s apart" : ""));
        }

        void LaunchWave()
        {
            int slot = -1;
            for (int i = 0; i < waves.Length; i++) if (!waves[i].active) { slot = i; break; }
            if (slot < 0) return;
            ref Wave w = ref waves[slot];
            w.active = true;
            // The ring stays where it started; the gravity source may move on while it spreads.
            w.origin = root.GravityWorld;
            w.age = 0;
            w.reach = plannedReach;
            w.pushed = 0;
            launched++;
        }

        protected override void Step(double dt, bool holding, in EffectiveStats s)
        {
            var d = D;
            if (pending > 0)
            {
                launchIn -= dt;
                while (pending > 0 && launchIn <= 0)
                {
                    LaunchWave();
                    pending--;
                    launchIn += Math.Max(0.0, d.chainGapSec);
                }
            }
            float speed = (float)(d.PullSpeedPx(Level) / p.pixelsPerUnit);
            for (int i = 0; i < waves.Length; i++)
            {
                ref Wave w = ref waves[i];
                if (!w.active) continue;
                bool spreading = w.age < d.ringSec;
                // Each tick pushes the band the ring crossed in it, so every dust is hit once as the ring goes by.
                if (spreading) w.pushed += root.Dust.Impulse(w.origin, RadiusAt(w.reach, w.age), RadiusAt(w.reach, w.age + dt), root.GravityWorld, speed);
                w.age += dt;
                if (spreading && w.age >= d.ringSec)
                    Log((triggerWaves > 1 ? "wave " + (i + 1) + " done: " : "ring done: ") + w.pushed + " dust pushed, " +
                        (root.Dust.Count - root.Dust.OutsideCount) + " on screen and " + root.Dust.OutsideCount + " outside it now");
                // The ring is at the end of its range now and only fades (24 §16.1); the next trigger waits for that.
                if (w.age >= d.ringSec + Math.Max(0.0, d.ringFadeSec)) w.active = false;
            }
        }

        float RadiusAt(float reach, double seconds) => reach * Mathf.Clamp01((float)(seconds / Math.Max(1e-3, D.ringSec)));

        protected override void OnClear()
        {
            for (int i = 0; i < waves.Length; i++)
            {
                waves[i].active = false;
                waves[i].age = 0;
            }
            pending = 0;
            launched = 0;
        }

        public override void Render(bool show, float lead)
        {
            float intensity = root.EffectIntensity;
            var d = D;
            int n = Mathf.Max(3, d.ringSegments);
            if (points.Length != n) points = new Vector3[n];
            for (int k = 0; k < waves.Length; k++)
            {
                ref Wave w = ref waves[k];
                var ring = rings[k];
                bool on = show && w.active && intensity > 0f;
                if (ring.enabled != on) ring.enabled = on;
                if (!on) continue;
                float radius = RadiusAt(w.reach, w.age + lead);
                if (ring.positionCount != n) ring.positionCount = n;
                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f;
                    points[i] = new Vector3(w.origin.x + Mathf.Cos(a) * radius, w.origin.y + Mathf.Sin(a) * radius, 0f);
                }
                ring.SetPositions(points);
                // As with the gravity ring (22 §6), the width follows the world scale so it is the same on screen.
                ring.widthMultiplier = (float)(d.ringWidthPx * root.WorldScale / p.pixelsPerUnit);
                // Full while it spreads, then it fades where it stopped.
                float fade = d.ringFadeSec > 0 ? Mathf.Clamp01((float)((w.age + lead - d.ringSec) / d.ringFadeSec)) : 0f;
                Color c = d.ringColor;
                c.a *= (1f - fade) * intensity;
                ring.startColor = ring.endColor = c;
            }
        }
    }
}
