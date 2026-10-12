using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// 24 §4, §16.2 궤도석 (위성 until 24 §23.5). While the player holds, small gravity sources orbit the gravity source, counter-clockwise
    /// like the vortex. Each pulls the dust inside its own small reach toward itself, lights it up (24 §8) and captures
    /// nothing, so the dust it drags along is left to the gravity source: one more hand for the same body. They move
    /// with the gravity source and are gone the moment the hold is released. They are always there while holding, so
    /// this ability has no cooldown and nothing to trigger.
    ///
    /// The orbit is an ellipse with the gravity source at one focus, r(θ) = p / (1 + e·cos θ), and the satellite
    /// sweeps equal areas in equal times, dθ/dt = h / r². So it lingers far out, beyond the gravity ring, where it
    /// gathers dust, and whips round close in, dragging what it gathered into the gravity source's reach. With n
    /// satellites the long axes are 360°/n apart and all of them are at the same point of their own orbit, which makes
    /// the petals; the whole figure turns slowly, so the far points do not keep raking the same directions.
    ///
    /// The pull goes to the dust tick as one <see cref="DustForce"/> per satellite. A dust outside a satellite's reach
    /// costs that tick one distance test and nothing more, which is the condition 24 §4 sets for having four of them.
    ///
    /// 24 §18.1 the tow: a satellite near its nearest point moves at about 1,280 px/s and dust can manage 400, so the
    /// clump it gathered used to be left behind at the gravity ring. Now the dust in its core (towRadiusShare of the
    /// gravity radius) has its velocity drawn to the satellite's (towRate per second) and may go faster than the field's
    /// top speed while it is excited (AbilityTable.exciteSpeedCapMult), so the clump comes round with the satellite and
    /// is carried into the gravity source's reach.
    /// </summary>
    public sealed class SatelliteAbility : Ability
    {
        /// <summary>Over the gravity ring and the dust, under the bodies.</summary>
        const int SortingOrder = 6;
        const double TwoPi = Math.PI * 2.0;

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
        int count;
        int loggedCount;
        Vector2 center;
        /// <summary>One gravity radius in world units; the orbit below is in gravity radii.</summary>
        float unit;
        /// <summary>Angle along the orbit from its nearest point (θ), the same for every satellite.</summary>
        double theta;
        /// <summary>How far the whole figure has turned.</summary>
        double precession;
        double precessionRate;
        /// <summary>The ellipse: e, p, and h (twice the area swept per second), as in 24 §16.2.</summary>
        double eccentricity, semiLatus, areaRate;
        double clock;
        /// <summary>Where each satellite was at the last tick, for its velocity (the tow, 24 §18.1).</summary>
        Vector2[] lastPos = new Vector2[0];
        bool haveLast;

        SatelliteDef D => root.abilityTable.satellite;

        /// <summary>
        /// Seconds the satellites have been orbiting in this run. They start at their nearest point, so they are there
        /// again at every whole orbitPeriodSec and at their farthest point half a period after that.
        /// </summary>
        public override float EffectAge => count > 0 ? (float)clock : -1f;

        protected override void Build(Transform parent)
        {
            holder = new GameObject("Satellites").transform;
            holder.SetParent(parent, false);
            if (root.circleSprite != null) coreSpriteSize = Mathf.Max(1e-3f, root.circleSprite.bounds.size.x);
        }

        protected override void Step(double dt, bool holding, in EffectiveStats s)
        {
            var d = D;
            int had = count;
            count = holding ? d.Count(Level) : 0;
            if (count != had) haveLast = false;
            if (count == 0) return;

            // The ellipse from its nearest and farthest points (24 §16.2), in gravity radii.
            double near = Math.Max(1e-3, d.periapsisShare);
            double far = Math.Max(near, d.apoapsisShare);
            eccentricity = (far - near) / (far + near);
            semiLatus = near * (1.0 + eccentricity);
            double a = (near + far) * 0.5;
            double b = a * Math.Sqrt(1.0 - eccentricity * eccentricity);
            areaRate = TwoPi * a * b / Math.Max(1e-3, d.orbitPeriodSec);
            precessionRate = d.precessionDegPerSec * Math.PI / 180.0;

            // Equal areas in equal times. The angular speed changes many times over along the orbit (near² : far²), so
            // the step is taken at the speed of its middle, which keeps a turn at orbitPeriodSec.
            double mid = theta + AngularSpeed(theta) * dt * 0.5;
            theta = (theta + AngularSpeed(mid) * dt) % TwoPi;
            precession = (precession + precessionRate * dt) % TwoPi;
            clock += dt;

            float ppu = (float)p.pixelsPerUnit;
            center = root.GravityWorld;
            unit = (float)(s.gravityRadius / ppu);
            float reach = (float)(s.gravityRadius * d.pullRadiusShare) / ppu;
            float accel = (float)(s.pullAccel * d.PullShare(Level)) / ppu;
            // 24 §18.1: the core tows the dust in it along at the satellite's own velocity, which is where it was at the
            // last tick to where it is now. The first tick of a hold has no last tick and tows nothing.
            bool tow = d.Tows;
            float towRadius = tow ? (float)(s.gravityRadius * d.towRadiusShare) / ppu : 0f;
            if (lastPos.Length < count) System.Array.Resize(ref lastPos, count);
            for (int i = 0; i < count; i++)
            {
                Vector2 pos = Position(i, 0f);
                Vector2 vel = haveLast && dt > 0 ? (pos - lastPos[i]) / (float)dt : Vector2.zero;
                lastPos[i] = pos;
                if (tow && haveLast) system.AddForce(DustForce.PullTow(pos, reach, accel, towRadius, (float)d.towRate, vel));
                else system.AddForce(DustForce.Pull(pos, reach, accel));
            }
            haveLast = true;

            // They come and go with every hold, so the log only speaks up when their number changes.
            if (count == loggedCount) return;
            loggedCount = count;
            Log(count + " orbiting while holding, from " + RunClock + ": orbit " + (near * s.gravityRadius).ToString("F0") + " to " +
                (far * s.gravityRadius).ToString("F0") + " px from the gravity source, one turn in " + d.orbitPeriodSec.ToString("0.##") +
                "s, reach " + (reach * ppu).ToString("F0") + " px, pull " + (accel * ppu).ToString("F0") + " px/s²" +
                (tow ? ", core " + (towRadius * ppu).ToString("F0") + " px towing at " + d.towRate.ToString("0.##") + "/s" : ", no tow"));
        }

        /// <summary>Distance from the gravity source at angle <paramref name="angle"/> of the orbit, in gravity radii.</summary>
        double Radius(double angle) => semiLatus / (1.0 + eccentricity * Math.Cos(angle));

        /// <summary>dθ/dt at that angle: the nearer, the faster.</summary>
        double AngularSpeed(double angle)
        {
            double r = Radius(angle);
            return areaRate / (r * r);
        }

        /// <summary>Where satellite <paramref name="i"/> is, <paramref name="lead"/> seconds after the last tick.</summary>
        Vector2 Position(int i, float lead)
        {
            double angle = theta + AngularSpeed(theta) * lead;
            // The long axis of this satellite's ellipse: its share of the full turn, plus how far the figure has turned.
            double axis = precession + precessionRate * lead + TwoPi * i / Math.Max(1, count);
            float r = (float)Radius(angle) * unit;
            return center + new Vector2((float)Math.Cos(axis + angle), (float)Math.Sin(axis + angle)) * r;
        }

        protected override void OnClear()
        {
            count = 0;
            loggedCount = 0;
            theta = 0;
            precession = 0;
            clock = 0;
            haveLast = false;
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
