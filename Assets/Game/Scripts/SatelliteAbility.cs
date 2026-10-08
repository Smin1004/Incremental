using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// 24 §4, §16.2 위성. While the player holds, small gravity sources orbit the gravity source, counter-clockwise
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
    ///
    /// 24 §21.2 번개 사슬, the evolved form: consecutive satellites are joined by lightning (two by one chain, three or
    /// more in a ring). Dust within chainReachPx of a chain is pulled onto it and excited, so the chains carry dust
    /// round with them, one more <see cref="DustForce"/> per chain; and when the satellites pass their nearest point,
    /// which they do together once an orbit, the chains flash and every dust within snapRadiusShare gravity radii is
    /// pushed to the gravity source at once (24 §3's push), with a ring closing on it.
    /// </summary>
    public sealed class SatelliteAbility : Ability
    {
        /// <summary>Over the gravity ring and the dust, under the bodies.</summary>
        const int SortingOrder = 6;
        const double TwoPi = Math.PI * 2.0;
        static readonly int IdColor = Shader.PropertyToID("_Color");
        static readonly int IdRing = Shader.PropertyToID("_Ring");

        struct Visual
        {
            public Transform root;
            public SpriteRenderer core;
            public SpriteRenderer glow;
            public TrailRenderer trail;
            public bool shown;
        }

        struct Chain
        {
            public LineRenderer core;
            public LineRenderer glow;
            public bool shown;
        }

        readonly System.Random rng = new System.Random();
        Transform holder;
        Visual[] visuals = new Visual[0];
        Chain[] chains = new Chain[0];
        Vector3[] chainPoints = new Vector3[0];
        MeshRenderer snapRing;
        MaterialPropertyBlock snapMpb;
        float coreSpriteSize = 1f;
        int count;
        int loggedCount;
        bool loggedChained;
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
        /// <summary>24 §21.2: seconds since the last snap while its flash lasts (−1 otherwise), how many this run, and the radius it pushed, world units.</summary>
        double snapAge = -1;
        int snaps;
        float snapRadius;

        SatelliteDef D => root.abilityTable.satellite;

        /// <summary>
        /// Seconds the satellites have been orbiting in this run. They start at their nearest point, so they are there
        /// again at every whole orbitPeriodSec and at their farthest point half a period after that.
        /// </summary>
        public override float EffectAge => count > 0 ? (float)clock : -1f;

        /// <summary>24 §21.2: true while the lightning is on: evolved, holding, two satellites or more.</summary>
        public bool Chained => Level.evolved && count >= 2;
        /// <summary>Chains right now: one between two satellites, a ring of them from three up, none otherwise.</summary>
        public int ChainCount => Chained ? Links(count) : 0;
        /// <summary>24 §21.2: snaps this run, and seconds since the last one while its flash lasts (−1 otherwise).</summary>
        public int Snaps => snaps;
        public float SnapAge => (float)snapAge;

        static int Links(int n) => n >= 3 ? n : n == 2 ? 1 : 0;

        protected override void Build(Transform parent)
        {
            holder = new GameObject("Satellites").transform;
            holder.SetParent(parent, false);
            if (root.circleSprite != null) coreSpriteSize = Mathf.Max(1e-3f, root.circleSprite.bounds.size.x);

            // 24 §21.2: the ring of the snap, an FxRing quad on the gravity source.
            var go = new GameObject("SnapRing", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(holder, false);
            go.GetComponent<MeshFilter>().sharedMesh = PlanetVisual.Quad();
            snapRing = go.GetComponent<MeshRenderer>();
            if (root.fxMaterial != null) snapRing.sharedMaterial = root.fxMaterial;
            snapRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            snapRing.receiveShadows = false;
            snapRing.sortingOrder = SortingOrder;
            snapMpb = new MaterialPropertyBlock();
            snapRing.enabled = false;
        }

        protected override void Step(double dt, bool holding, in EffectiveStats s)
        {
            var d = D;
            if (snapAge >= 0)
            {
                snapAge += dt;
                if (snapAge >= Math.Max(0.0, d.snapFlashSec)) snapAge = -1;
            }
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
            double before = theta;
            theta = (theta + AngularSpeed(mid) * dt) % TwoPi;
            // θ = 0 is the nearest point: the step that wraps past 2π passed it (24 §21.2 the snap).
            bool passedNear = theta < before;
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

            // 24 §21.2: the lightning between consecutive satellites pulls the dust near it onto it, one line force each.
            bool chained = Chained;
            float chainReach = (float)(Math.Max(0.0, d.chainReachPx) / ppu);
            float chainAccel = (float)(Math.Max(0.0, d.chainPullPx) / ppu);
            if (chained && chainReach > 0f && chainAccel > 0f)
            {
                int links = Links(count);
                for (int i = 0; i < links; i++)
                    system.AddForce(DustForce.Line(lastPos[i], lastPos[(i + 1) % count], chainReach, chainAccel));
            }
            if (chained && passedNear) Snap(s, ppu);

            // They come and go with every hold, so the log only speaks up when their number changes.
            if (count == loggedCount && chained == loggedChained) return;
            loggedCount = count;
            loggedChained = chained;
            Log(count + " orbiting while holding, from " + RunClock + ": orbit " + (near * s.gravityRadius).ToString("F0") + " to " +
                (far * s.gravityRadius).ToString("F0") + " px from the gravity source, one turn in " + d.orbitPeriodSec.ToString("0.##") +
                "s, reach " + (reach * ppu).ToString("F0") + " px, pull " + (accel * ppu).ToString("F0") + " px/s²" +
                (tow ? ", core " + (towRadius * ppu).ToString("F0") + " px towing at " + d.towRate.ToString("0.##") + "/s" : ", no tow") +
                (chained ? "; lightning: " + Links(count) + " chain(s), dust within " + d.chainReachPx.ToString("F0") + " px pulled onto them at " +
                    d.chainPullPx.ToString("F0") + " px/s², a snap every orbit over " + (d.snapRadiusShare * s.gravityRadius).ToString("F0") +
                    " px at " + d.snapSpeedPx.ToString("F0") + " px/s" : ""));
        }

        /// <summary>
        /// 24 §21.2: the satellites are at their nearest point together. The chains flash and every dust within
        /// snapRadiusShare gravity radii gets one push to the gravity source, the same push 수축 gives.
        /// </summary>
        void Snap(in EffectiveStats s, float ppu)
        {
            var d = D;
            snapRadius = (float)(s.gravityRadius * Math.Max(0.0, d.snapRadiusShare) / ppu);
            int hit = snapRadius > 0f && d.snapSpeedPx > 0 ? root.Dust.Impulse(center, 0f, snapRadius, center, (float)(d.snapSpeedPx / ppu)) : 0;
            snapAge = 0;
            snaps++;
            Log("snap at " + RunClock + ": " + count + " at their nearest point, " + hit + " dust within " + (snapRadius * ppu).ToString("F0") +
                " px pushed to the gravity source at " + d.snapSpeedPx.ToString("F0") + " px/s (snap " + snaps + " this run)");
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
            loggedChained = false;
            theta = 0;
            precession = 0;
            clock = 0;
            haveLast = false;
            snapAge = -1;
            snaps = 0;
        }

        public override void Render(bool show, float lead)
        {
            // Hidden while paused, like the gravity ring they circle.
            int n = show && !root.Paused ? count : 0;
            if (n > visuals.Length) Grow(n);
            var d = D;
            float ppu = (float)p.pixelsPerUnit;
            float intensity = root.EffectIntensity;
            RenderChains(n, lead, d, ppu, intensity);
            RenderSnap(n, lead, d, ppu, intensity);
            if (visuals.Length == 0) return;

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

        /// <summary>
        /// 24 §21.2: the lightning. Each chain is a core line and a glow line through chainSegments points between two
        /// satellites; the ends stay on them and the points between jump sideways every frame, so the line trembles.
        /// The core is the chain itself and stays at effect intensity 0; the glow, the trembling and the flash follow it.
        /// </summary>
        void RenderChains(int n, float lead, SatelliteDef d, float ppu, float intensity)
        {
            int links = Level.evolved ? Links(n) : 0;
            if (links > chains.Length) GrowChains(links);
            if (chains.Length == 0) return;
            int segs = Mathf.Max(2, d.chainSegments);
            if (chainPoints.Length != segs) chainPoints = new Vector3[segs];
            float flash = snapAge >= 0 && d.snapFlashSec > 0 ? (1f - (float)(snapAge / d.snapFlashSec)) * intensity : 0f;
            // Like the gravity ring (22 §6), the widths follow the world scale so they are the same on screen.
            float width = (float)(d.chainWidthPx * root.WorldScale / ppu) * (1f + flash);
            float glowWidth = (float)(d.chainGlowWidthPx * root.WorldScale / ppu) * (1f + flash);
            float jitter = (float)(d.chainJitterPx * root.WorldScale / ppu) * intensity;
            Color core = d.chainColor;
            core.a = Mathf.Clamp01(core.a * (1f + flash));
            Color glow = new Color(core.r, core.g, core.b, Mathf.Clamp01((float)d.chainGlowAlpha * intensity * (1f + flash)));
            bool glowOn = d.chainGlowWidthPx > 0 && glow.a > 0f;
            for (int i = 0; i < chains.Length; i++)
            {
                ref Chain c = ref chains[i];
                bool on = i < links;
                if (on != c.shown)
                {
                    c.shown = on;
                    c.core.enabled = on;
                }
                if (c.glow.enabled != (on && glowOn)) c.glow.enabled = on && glowOn;
                if (!on) continue;
                Vector2 a = Position(i, lead), b = Position((i + 1) % n, lead);
                Vector2 ab = b - a;
                float len = ab.magnitude;
                Vector2 perp = len > 1e-6f ? new Vector2(-ab.y, ab.x) / len : Vector2.zero;
                for (int k = 0; k < segs; k++)
                {
                    float u = k / (float)(segs - 1);
                    float amp = jitter * Mathf.Sin(u * Mathf.PI) * (float)(rng.NextDouble() * 2.0 - 1.0);
                    Vector2 at = a + ab * u + perp * amp;
                    chainPoints[k] = new Vector3(at.x, at.y, 0f);
                }
                if (c.core.positionCount != segs) c.core.positionCount = segs;
                c.core.SetPositions(chainPoints);
                c.core.widthMultiplier = width;
                c.core.startColor = c.core.endColor = core;
                if (!glowOn) continue;
                if (c.glow.positionCount != segs) c.glow.positionCount = segs;
                c.glow.SetPositions(chainPoints);
                c.glow.widthMultiplier = glowWidth;
                c.glow.startColor = c.glow.endColor = glow;
            }
        }

        /// <summary>24 §21.2: the ring of the snap closes on the gravity source with the dust it pushed, over snapFlashSec (× effect intensity).</summary>
        void RenderSnap(int n, float lead, SatelliteDef d, float ppu, float intensity)
        {
            bool on = n > 0 && snapAge >= 0 && d.snapFlashSec > 0 && intensity > 0f && snapRadius > 0f;
            if (snapRing.enabled != on) snapRing.enabled = on;
            if (!on) return;
            float k = Mathf.Clamp01((float)((snapAge + lead) / d.snapFlashSec));
            float radius = snapRadius * (1f - k);
            float width = (float)(Math.Max(1.0, d.chainGlowWidthPx) * root.WorldScale / ppu);
            float diameter = radius * 2f;
            float quad = diameter + width * 2f;
            snapRing.transform.position = new Vector3(center.x, center.y, 0f);
            snapRing.transform.localScale = new Vector3(quad, quad, 1f);
            float strength = (1f - k) * intensity;
            Color c = d.chainColor;
            snapMpb.SetColor(IdColor, new Color(c.r * strength, c.g * strength, c.b * strength, 1f));
            snapMpb.SetVector(IdRing, new Vector4(diameter / quad, width / quad, 0f, 0f));
            snapRing.SetPropertyBlock(snapMpb);
        }

        void Grow(int n)
        {
            int had = visuals.Length;
            Array.Resize(ref visuals, n);
            for (int i = had; i < n; i++) visuals[i] = MakeVisual(i);
        }

        void GrowChains(int n)
        {
            int had = chains.Length;
            Array.Resize(ref chains, n);
            for (int i = had; i < n; i++)
            {
                chains[i].glow = MakeLine("ChainGlow" + i, SortingOrder);
                chains[i].core = MakeLine("Chain" + i, SortingOrder + 1);
                chains[i].shown = false;
            }
        }

        LineRenderer MakeLine(string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(holder, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.alignment = LineAlignment.TransformZ;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (root.unlitMaterial != null) line.sharedMaterial = root.unlitMaterial;
            line.sortingOrder = order;
            UIBuilder.UseWhiteTexture(line);
            line.enabled = false;
            return line;
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
