using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// 24 §4 위성. While the player holds, small gravity sources circle the gravity source, evenly spaced and
    /// counter-clockwise like the vortex. Each pulls the dust inside its own small reach toward itself and captures
    /// nothing, so the dust it drags along is left to the gravity source: one more hand for the same body. They move
    /// with the gravity source and are gone the moment the hold is released. They are always there while holding, so
    /// this ability has no cooldown and nothing to trigger.
    ///
    /// The pull goes to the dust tick as one <see cref="DustForce"/> per satellite. A dust outside a satellite's reach
    /// costs that tick one distance test and nothing more, which is the condition 24 §4 sets for having four of them.
    /// </summary>
    public sealed class SatelliteAbility : Ability
    {
        /// <summary>Over the gravity ring and the dust, under the bodies.</summary>
        const int SortingOrder = 6;

        struct Visual
        {
            public Transform root;
            public SpriteRenderer core;
            public SpriteRenderer glow;
            public TrailRenderer trail;
            public bool shown;
        }

        Transform holder;
        Visual[] visuals = new Visual[0];
        float coreSpriteSize = 1f;
        double phase;
        float angularSpeed;
        int count;
        int loggedCount;
        Vector2 center;
        float orbitRadius;

        SatelliteDef D => root.abilityTable.satellite;

        protected override void Build(Transform parent)
        {
            holder = new GameObject("Satellites").transform;
            holder.SetParent(parent, false);
            if (root.circleSprite != null) coreSpriteSize = Mathf.Max(1e-3f, root.circleSprite.bounds.size.x);
        }

        protected override void Step(double dt, bool holding, in EffectiveStats s)
        {
            var d = D;
            count = holding ? d.Count(Level) : 0;
            if (count == 0) return;

            float ppu = (float)p.pixelsPerUnit;
            angularSpeed = (float)(Math.PI * 2.0 / Math.Max(1e-3, d.orbitPeriodSec));
            phase = (phase + angularSpeed * dt) % (Math.PI * 2.0);
            center = root.GravityWorld;
            orbitRadius = (float)(s.gravityRadius * d.orbitRadiusShare) / ppu;
            float reach = (float)(s.gravityRadius * d.pullRadiusShare) / ppu;
            float accel = (float)(s.pullAccel * d.PullShare(Level)) / ppu;
            for (int i = 0; i < count; i++)
                system.AddForce(DustForce.Pull(Position(i, 0f), reach, accel));

            // They come and go with every hold, so the log only speaks up when their number changes.
            if (count == loggedCount) return;
            loggedCount = count;
            Log(count + " orbiting while holding, from " + RunClock + ": orbit " + (orbitRadius * ppu).ToString("F0") + " px, reach " +
                (reach * ppu).ToString("F0") + " px, pull " + (accel * ppu).ToString("F0") + " px/s²");
        }

        /// <summary>Where satellite <paramref name="i"/> is, <paramref name="lead"/> seconds after the last tick.</summary>
        Vector2 Position(int i, float lead)
        {
            double a = phase + angularSpeed * lead + Math.PI * 2.0 * i / Math.Max(1, count);
            return center + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * orbitRadius;
        }

        protected override void OnClear()
        {
            count = 0;
            loggedCount = 0;
        }

        public override void Render(bool show, float lead)
        {
            // Hidden while paused, like the gravity ring they circle.
            int n = show && !root.Paused ? count : 0;
            if (n > visuals.Length) Grow(n);
            if (visuals.Length == 0) return;

            var d = D;
            float ppu = (float)p.pixelsPerUnit;
            float intensity = root.EffectIntensity;
            // 22 §6: a dot this small would thin out to nothing as the camera pulls back, so it keeps the floor the
            // bodies keep; the glow and the ribbon follow it.
            float dot = (float)(root.RunBodyPx(d.dotPx) / ppu);
            float scale = dot / Mathf.Max(1e-6f, (float)d.dotPx / ppu);
            float glow = (float)(d.glowPx / ppu) * scale;
            float trailWidth = (float)(d.trailWidthPx / ppu) * scale;
            Color core = d.color;
            Color halo = new Color(core.r, core.g, core.b, Mathf.Clamp01((float)d.glowAlpha) * intensity);
            Color ribbon = new Color(core.r, core.g, core.b, core.a * intensity);
            Color ribbonEnd = new Color(core.r, core.g, core.b, 0f);
            bool ribboned = d.trailSec > 0 && d.trailWidthPx > 0 && intensity > 0f;

            for (int i = 0; i < visuals.Length; i++)
            {
                ref Visual v = ref visuals[i];
                if (i >= n)
                {
                    if (v.shown)
                    {
                        v.shown = false;
                        v.root.gameObject.SetActive(false);
                    }
                    continue;
                }
                Vector2 pos = Position(i, lead);
                v.root.position = new Vector3(pos.x, pos.y, 0f);
                v.core.transform.localScale = Vector3.one * (dot / coreSpriteSize);
                v.core.color = core;
                v.glow.transform.localScale = Vector3.one * glow;
                v.glow.color = halo;
                v.trail.time = (float)d.trailSec;
                v.trail.widthMultiplier = trailWidth;
                v.trail.startColor = ribbon;
                v.trail.endColor = ribbonEnd;
                v.trail.emitting = ribboned;
                if (!v.shown)
                {
                    v.shown = true;
                    v.root.gameObject.SetActive(true);
                    // It appears where it is now; without this the ribbon would be drawn from where it last was.
                    v.trail.Clear();
                }
            }
        }

        void Grow(int n)
        {
            int had = visuals.Length;
            Array.Resize(ref visuals, n);
            for (int i = had; i < n; i++) visuals[i] = MakeVisual(i);
        }

        Visual MakeVisual(int i)
        {
            var go = new GameObject("Satellite" + i);
            go.transform.SetParent(holder, false);

            var glow = MakeSprite("Glow", go.transform, UIBuilder.SoftDotSprite(), SortingOrder);
            var core = MakeSprite("Core", go.transform, root.circleSprite != null ? root.circleSprite : UIBuilder.SoftDotSprite(), SortingOrder + 1);

            var trail = go.AddComponent<TrailRenderer>();
            if (root.unlitMaterial != null) trail.sharedMaterial = root.unlitMaterial;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.alignment = LineAlignment.TransformZ;
            trail.textureMode = LineTextureMode.Stretch;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.sortingOrder = SortingOrder;
            trail.autodestruct = false;
            UIBuilder.UseWhiteTexture(trail);

            go.SetActive(false);
            return new Visual { root = go.transform, core = core, glow = glow, trail = trail, shown = false };
        }

        SpriteRenderer MakeSprite(string name, Transform parent, Sprite sprite, int order)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(parent, false);
            sr.sprite = sprite;
            if (root.unlitMaterial != null) sr.sharedMaterial = root.unlitMaterial;
            sr.sortingOrder = order;
            return sr;
        }
    }
}
