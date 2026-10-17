using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// 24 §20.2 증폭 결정. Every cooldown of holding a gold crystal appears on screen, a few gravity radii away from the
    /// gravity source, and waits there for lifeSec: it drifts slowly, pulses, and blinks over its last seconds. Brought
    /// inside the gravity radius it is drawn in and absorbed, and for buffSec the gravity source is amplified: the
    /// radius wider (ramping in over rampSec), the pull stronger, the dust faster. Eating another crystal while
    /// amplified restarts the clock; the multipliers do not stack. The first ability of the tree (ring 2), and the one
    /// reason to move the gravity source now that the ring is smaller (24 §20.3).
    ///
    /// 24 §21.2 공명, the evolved form as decided on 10/8: crystals come every resonanceCooldownSec of holding instead of
    /// cooldownSec, still one at a time, and the buff stacks: a crystal absorbed while the buff is on adds the
    /// multipliers once more (up to resonanceMaxStacks, 2) and starts the clock over. Two stacks of ring 7's x1.65
    /// radius are x2.3. Three at once and three stacks were tried in the 7th pass and rejected as too much; the shard
    /// array below is what is left of that and holds one.
    ///
    /// The buff goes on the run's effective stats through <see cref="Amplify"/>, which GameRoot applies before anything
    /// reads them each tick, so the gravity ring, the dust and the other abilities all see the amplified radius. The
    /// crystals themselves stay visible at effect intensity 0 (they are things to pick up); their glow, their halo and
    /// the flash of the gravity ring follow the intensity.
    /// </summary>
    public sealed class CrystalAbility : Ability
    {
        /// <summary>Over the dust and the satellites, under the meteor and the bodies.</summary>
        const int SortingOrder = 8;
        static readonly int IdColor = Shader.PropertyToID("_Color");
        static readonly int IdRing = Shader.PropertyToID("_Ring");

        struct Shard
        {
            public bool alive;
            public Vector2 pos, vel;
            public double age;
            public bool pulled;
        }

        struct Visual
        {
            public Transform root;
            public MeshRenderer diamond;
            public Mesh mesh;
            public Color[] colors;
            public SpriteRenderer glow;
            public MeshRenderer halo;
            public MaterialPropertyBlock mpb;
            public bool shown;
        }

        readonly System.Random rng = new System.Random();

        Shard[] shards = new Shard[0];
        Visual[] visuals = new Visual[0];
        Transform holder;
        float glowSpriteSize = 1f;

        double buffLeft;
        float strength;
        /// <summary>24 §21.2: how many times the multipliers are added while the buff is on (1 unless evolved).</summary>
        int stacks;
        double flashAge = -1;
        double clock = -1;
        int taken, missed;

        CrystalDef D => root.abilityTable.crystal;

        /// <summary>Seconds since the oldest crystal that is waiting now appeared, or −1.</summary>
        public override float EffectAge
        {
            get
            {
                float oldest = -1f;
                for (int i = 0; i < shards.Length; i++)
                    if (shards[i].alive && (float)shards[i].age > oldest) oldest = (float)shards[i].age;
                return oldest;
            }
        }

        /// <summary>
        /// Seconds since the crystals of the current cycle appeared, running on through the buff until it has faded,
        /// or −1 when nothing is going on (captures and checks).
        /// </summary>
        public float Clock => (float)clock;
        public bool Alive => AliveCount > 0;
        /// <summary>Crystals waiting right now.</summary>
        public int AliveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < shards.Length; i++) if (shards[i].alive) n++;
                return n;
            }
        }
        /// <summary>Where the waiting crystal nearest the gravity source is, world units (the test harness steers the pointer to it).</summary>
        public Vector2 Position
        {
            get
            {
                Vector2 g = root.GravityWorld;
                Vector2 best = shards.Length > 0 ? shards[0].pos : g;
                float bestDist = float.PositiveInfinity;
                for (int i = 0; i < shards.Length; i++)
                {
                    if (!shards[i].alive) continue;
                    float dist = (shards[i].pos - g).sqrMagnitude;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = shards[i].pos;
                    }
                }
                return best;
            }
        }
        /// <summary>
        /// 27 §1.1 P1 (the bot): the waiting crystal nearest <paramref name="from"/> that is not being drawn in yet, its
        /// position (world units) and the seconds it has left. False when none is waiting.
        /// </summary>
        public bool NearestWaiting(Vector2 from, out Vector2 pos, out double lifeLeft, out float pullRadiusWorld)
        {
            pos = from;
            lifeLeft = 0;
            pullRadiusWorld = (float)(root.Effective.gravityRadius / p.pixelsPerUnit);
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < shards.Length; i++)
            {
                if (!shards[i].alive || shards[i].pulled) continue;
                float dist = (shards[i].pos - from).sqrMagnitude;
                if (dist >= bestDist) continue;
                bestDist = dist;
                pos = shards[i].pos;
                lifeLeft = D.lifeSec - shards[i].age;
            }
            return !float.IsPositiveInfinity(bestDist);
        }

        /// <summary>11th pass C: crystals that appeared, were taken and ran out since <see cref="ResetCounts"/> (bot measurement).</summary>
        public int Appeared { get; private set; }
        public int Taken { get; private set; }
        public int Missed { get; private set; }
        public void ResetCounts() => Appeared = Taken = Missed = 0;

        /// <summary>Seconds of amplification left, 0 when none.</summary>
        public double BuffLeft => buffLeft;
        /// <summary>How far the buff is in, 0..1: 1 while amplified, ramping over rampSec at both ends.</summary>
        public float BuffStrength => strength;
        /// <summary>24 §21.2: stacks of the buff that is on, 0 when none.</summary>
        public int Stacks => stacks;
        /// <summary>Seconds since the last crystal was absorbed, while the flash lasts; −1 otherwise.</summary>
        public float FlashAge => (float)flashAge;

        /// <summary>One batch at a time: a charge that fills while crystals are waiting waits for them to be taken or missed.</summary>
        protected override bool Ready => AliveCount == 0;

        protected override void Build(Transform parent)
        {
            holder = new GameObject("Crystals").transform;
            holder.SetParent(parent, false);
            Grow(1);
        }

        void Grow(int n)
        {
            if (shards.Length >= n) return;
            int had = shards.Length;
            Array.Resize(ref shards, n);
            Array.Resize(ref visuals, n);
            for (int i = had; i < n; i++) visuals[i] = MakeVisual(i);
        }

        Visual MakeVisual(int index)
        {
            var v = new Visual();
            v.root = new GameObject("Crystal" + index).transform;
            v.root.SetParent(holder, false);

            // The halo: an FxRing quad behind everything else of the crystal.
            var haloGo = new GameObject("Halo", typeof(MeshFilter), typeof(MeshRenderer));
            haloGo.transform.SetParent(v.root, false);
            haloGo.GetComponent<MeshFilter>().sharedMesh = PlanetVisual.Quad();
            v.halo = haloGo.GetComponent<MeshRenderer>();
            if (root.fxMaterial != null) v.halo.sharedMaterial = root.fxMaterial;
            v.halo.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            v.halo.receiveShadows = false;
            v.halo.sortingOrder = SortingOrder;
            v.mpb = new MaterialPropertyBlock();

            // The soft glow, as the satellites have.
            v.glow = new GameObject("Glow").AddComponent<SpriteRenderer>();
            v.glow.transform.SetParent(v.root, false);
            v.glow.sprite = UIBuilder.SoftDotSprite();
            if (root.unlitMaterial != null) v.glow.sharedMaterial = root.unlitMaterial;
            v.glow.sortingOrder = SortingOrder + 1;
            glowSpriteSize = Mathf.Max(1e-3f, v.glow.sprite.bounds.size.x);

            // The diamond: four vertices with the color on them, drawn with the unlit material and a white texture.
            var go = new GameObject("Diamond", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(v.root, false);
            v.mesh = new Mesh { name = "CrystalDiamond" };
            v.mesh.vertices = new[] { new Vector3(0f, 0.5f, 0f), new Vector3(0.36f, 0f, 0f), new Vector3(0f, -0.5f, 0f), new Vector3(-0.36f, 0f, 0f) };
            v.mesh.uv = new[] { new Vector2(0.5f, 1f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0f), new Vector2(0f, 0.5f) };
            v.mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            v.colors = new Color[4];
            for (int i = 0; i < 4; i++) v.colors[i] = Color.white;
            v.mesh.colors = v.colors;
            v.mesh.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh = v.mesh;
            v.diamond = go.GetComponent<MeshRenderer>();
            if (root.unlitMaterial != null) v.diamond.sharedMaterial = root.unlitMaterial;
            v.diamond.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            v.diamond.receiveShadows = false;
            v.diamond.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            v.diamond.sortingOrder = SortingOrder + 2;
            UIBuilder.UseWhiteTexture(v.diamond);

            v.root.gameObject.SetActive(false);
            v.shown = false;
            return v;
        }

        /// <summary>
        /// The crystal appears: inside the edge margin, at least minDistanceRadii gravity radii from the gravity source
        /// (the radius as it is without the buff). Of 16 random points it takes the first that is far enough, else the farthest.
        /// </summary>
        protected override void Trigger(in EffectiveStats s)
        {
            var d = D;
            float ppu = (float)p.pixelsPerUnit;
            Rect inner = Inner(root.CameraArea());
            Vector2 g = root.GravityWorld;
            // During a buff the amplified radius would ask for a distance the screen has not got, and every crystal
            // would fall back to the farthest corner; so the distance is measured in the radius without the buff.
            double baseRadius = s.gravityRadius / Factor(RadiusMult());
            float least = (float)(baseRadius * Math.Max(0.0, d.minDistanceRadii) / ppu);
            Grow(1);
            Vector2 best = inner.center;
            float bestDist = -1f;
            for (int tries = 0; tries < 16; tries++)
            {
                var candidate = new Vector2(inner.xMin + (float)rng.NextDouble() * inner.width, inner.yMin + (float)rng.NextDouble() * inner.height);
                float dist = (candidate - g).magnitude;
                if (dist > bestDist)
                {
                    bestDist = dist;
                    best = candidate;
                }
                if (dist >= least) break;
            }
            ref Shard sh = ref shards[0];
            sh.alive = true;
            sh.pos = best;
            sh.vel = RandomDir() * (float)(d.driftSpeedPx / ppu);
            sh.age = 0;
            sh.pulled = false;
            for (int k = 1; k < shards.Length; k++) shards[k].alive = false;
            clock = 0;
            Appeared++;
            Log("appeared at " + RunClock + ": " + (bestDist * ppu).ToString("F0") + " px from the gravity source (" +
                (least * ppu).ToString("F0") + " px or more wanted), gone in " + d.lifeSec.ToString("0.##") + "s unless taken" +
                (Level.evolved ? ", the next " + d.resonanceCooldownSec.ToString("0.##") + "s of holding after it is gone" : ""));
        }

        protected override void Step(double dt, bool holding, in EffectiveStats s)
        {
            var d = D;
            float ppu = (float)p.pixelsPerUnit;

            // The buff: its clock, the ramp of its strength, and the flash of the moment it began or grew.
            if (buffLeft > 0)
            {
                buffLeft -= dt;
                if (buffLeft <= 0)
                {
                    buffLeft = 0;
                    Log("buff ended at " + RunClock + (stacks > 1 ? " after " + stacks + " stacks" : "") + " (" + taken + " taken, " + missed + " missed this run)");
                }
            }
            float ramp = d.rampSec > 0 ? (float)(dt / d.rampSec) : 1f;
            strength = Mathf.MoveTowards(strength, buffLeft > 0 ? 1f : 0f, ramp);
            // The stacks go with the last of the strength, so the ramp out is from where the buff was.
            if (buffLeft <= 0 && strength <= 0f) stacks = 0;
            if (flashAge >= 0)
            {
                flashAge += dt;
                if (flashAge >= Math.Max(0.0, d.flashSec)) flashAge = -1;
            }
            if (clock >= 0)
            {
                clock += dt;
                if (AliveCount == 0 && buffLeft <= 0 && strength <= 0f && flashAge < 0) clock = -1;
            }

            Vector2 g = root.GravityWorld;
            // The radius in s is already amplified when a buff is on, so the next crystal is a little easier to reach.
            float radius = (float)(s.gravityRadius / ppu);
            float step = (float)(Math.Max(1e-3, d.pullInSpeedPx) / ppu) * (float)dt;
            float capture = (float)(p.captureRadius / ppu);
            Rect inner = Inner(root.CameraArea());
            for (int i = 0; i < shards.Length; i++)
            {
                ref Shard sh = ref shards[i];
                if (!sh.alive) continue;
                sh.age += dt;
                Vector2 to = g - sh.pos;
                float dist = to.magnitude;
                if (holding && dist <= radius) sh.pulled = true;
                else if (!holding) sh.pulled = false;

                if (sh.pulled)
                {
                    if (dist <= capture + step)
                    {
                        sh.alive = false;
                        Absorb(s);
                        continue;
                    }
                    sh.pos += to / Mathf.Max(1e-6f, dist) * step;
                    continue;
                }

                // Waiting: a slow drift that turns back at the edge margin, and a life that runs out.
                sh.pos += sh.vel * (float)dt;
                if (sh.pos.x < inner.xMin) { sh.pos.x = inner.xMin; sh.vel.x = Mathf.Abs(sh.vel.x); }
                else if (sh.pos.x > inner.xMax) { sh.pos.x = inner.xMax; sh.vel.x = -Mathf.Abs(sh.vel.x); }
                if (sh.pos.y < inner.yMin) { sh.pos.y = inner.yMin; sh.vel.y = Mathf.Abs(sh.vel.y); }
                else if (sh.pos.y > inner.yMax) { sh.pos.y = inner.yMax; sh.vel.y = -Mathf.Abs(sh.vel.y); }
                if (sh.age >= d.lifeSec)
                {
                    sh.alive = false;
                    missed++;
                    Missed++;
                    Log("missed at " + RunClock + ": gone after " + d.lifeSec.ToString("0.##") + "s, " + (dist * ppu).ToString("F0") + " px from the gravity source");
                }
            }
        }

        void Absorb(in EffectiveStats s)
        {
            var d = D;
            bool again = buffLeft > 0;
            // The radius before this crystal, with whatever buff was already on it taken off again.
            double before = s.gravityRadius / Factor(RadiusMult());
            taken++;
            Taken++;
            // 24 §21.2: evolved, a crystal taken while the buff is on adds the multipliers once more.
            int most = Level.evolved ? Math.Max(1, d.resonanceMaxStacks) : 1;
            stacks = again ? Math.Min(most, stacks + 1) : 1;
            buffLeft = Math.Max(0.0, d.BuffSec(Level));
            // 29 §5 T5: eating a crystal can add run time.
            root.Rules.OnCrystalTaken(root.Run);
            flashAge = 0;
            // 25 §3: the moment it is taken, the whole field answers.
            root.Dust.ExciteAll();
            double radiusMult = 1.0 + (RadiusMult() - 1.0) * stacks;
            double pullMult = 1.0 + (d.PullMult(Level) - 1.0) * stacks;
            double speedMult = 1.0 + (d.speedMult - 1.0) * stacks;
            Log("absorbed at " + RunClock + ": amplified for " + d.BuffSec(Level).ToString("0.##") + "s" + (stacks > 1 ? " at " + stacks + " stacks" : "") +
                ", gravity radius x" + radiusMult.ToString("0.##") + " (" + (before * radiusMult).ToString("F0") + " px), pull x" +
                pullMult.ToString("0.##") + ", dust speed x" + speedMult.ToString("0.##") + (again ? " (the clock started over)" : ""));
        }

        /// <summary>29 §5 A2: the buff is on.</summary>
        public bool Buffed => buffLeft > 0;

        /// <summary>The radius multiplier at this level: the row's, or the debug key K's (24 §23.2).</summary>
        double RadiusMult()
        {
            var o = root.GravityRange;
            return o.set ? CrystalDef.RadiusMult(Level, o.crystalRadiusMult, o.crystalRadiusPerPower) : D.RadiusMult(Level);
        }

        /// <summary>A multiplier of the row, added once per stack, as far as the ramp has come: 1 when the buff is off.</summary>
        double Factor(double mult) => strength > 0f && stacks > 0 ? 1.0 + (mult - 1.0) * stacks * strength : 1.0;

        /// <summary>24 §20.2: the amplification on this tick's stats, as far as the ramp has come.</summary>
        public override void Amplify(ref EffectiveStats s)
        {
            if (strength <= 0f || stacks <= 0) return;
            var d = D;
            s.gravityRadius *= Factor(RadiusMult());
            s.pullAccel *= Factor(d.PullMult(Level));
        }

        public override double DustSpeedMult => Factor(D.speedMult);

        /// <summary>While amplified the charge arc runs down with the buff instead of up with the charge.</summary>
        public override float ArcShare(double cooldown) =>
            buffLeft > 0 ? Mathf.Clamp01((float)(buffLeft / Math.Max(1e-3, D.BuffSec(Level)))) : base.ArcShare(cooldown);

        /// <summary>
        /// The gravity ring: gold while amplified, deeper with every stack, and a gold flash the moment a crystal is
        /// taken (× effect intensity).
        /// </summary>
        public override void TintRing(ref Color color, ref float amount)
        {
            var d = D;
            float flash = flashAge >= 0 && d.flashSec > 0 ? (1f - (float)(flashAge / d.flashSec)) * root.EffectIntensity : 0f;
            float held = strength * Mathf.Min(1f, 0.8f + 0.1f * Math.Max(0, stacks - 1));
            float a = Mathf.Max(held, flash);
            if (a <= amount) return;
            amount = a;
            color = d.ringColor;
        }

        protected override void OnClear()
        {
            for (int i = 0; i < shards.Length; i++)
            {
                shards[i].alive = false;
                shards[i].pulled = false;
                shards[i].age = 0;
            }
            buffLeft = 0;
            strength = 0f;
            stacks = 0;
            flashAge = -1;
            clock = -1;
            taken = missed = 0;
        }

        public override void Render(bool show, float lead)
        {
            var d = D;
            float ppu = (float)p.pixelsPerUnit;
            float intensity = root.EffectIntensity;
            bool any = show && !root.Paused;
            for (int i = 0; i < visuals.Length; i++)
            {
                ref Visual v = ref visuals[i];
                ref Shard sh = ref shards[i];
                bool on = any && sh.alive;
                if (on != v.shown)
                {
                    v.shown = on;
                    v.root.gameObject.SetActive(on);
                }
                if (!on) continue;

                float t = (float)sh.age + lead;
                // Where it is now: a tick's lead along the drift, or along the pull-in.
                Vector2 at = sh.pos;
                if (sh.pulled)
                {
                    Vector2 to = root.GravityWorld - sh.pos;
                    float dist = to.magnitude;
                    if (dist > 1e-4f) at += to / dist * Mathf.Min(dist, (float)(d.pullInSpeedPx / ppu) * lead);
                }
                else at += sh.vel * lead;
                v.root.position = new Vector3(at.x, at.y, 0f);

                // The pulse (each crystal a little out of step with the others), and the blink over the last seconds.
                float pulse = 1f + (float)d.pulseScale * (0.5f + 0.5f * Mathf.Sin((t / Mathf.Max(0.05f, (float)d.pulseSec) + i * 0.37f) * Mathf.PI * 2f));
                float blink = 1f;
                if (d.blinkSec > 0 && t >= d.lifeSec - d.blinkSec && !sh.pulled)
                    blink = Mathf.Sin(t * (float)d.blinkHz * Mathf.PI * 2f) >= 0f ? 1f : 0.25f;

                // 22 §6: a thing this small keeps the floor the bodies keep as the camera pulls back.
                float size = (float)(root.RunBodyPx(d.sizePx) / ppu) * pulse;
                float scale = size / Mathf.Max(1e-6f, (float)d.sizePx / ppu);
                v.diamond.transform.localScale = new Vector3(size, size, 1f);
                Color c = d.color;
                c.a *= blink;
                for (int k = 0; k < 4; k++) v.colors[k] = c;
                v.mesh.colors = v.colors;

                float glowSize = (float)(d.glowPx / ppu) * scale;
                bool glowOn = d.glowPx > 0 && d.glowAlpha > 0 && intensity > 0f;
                if (v.glow.enabled != glowOn) v.glow.enabled = glowOn;
                if (glowOn)
                {
                    v.glow.transform.localScale = Vector3.one * (glowSize / glowSpriteSize);
                    v.glow.color = new Color(c.r, c.g, c.b, Mathf.Clamp01((float)d.glowAlpha) * intensity * blink);
                }

                bool haloOn = d.haloPx > 0 && d.haloStrength > 0 && intensity > 0f;
                if (v.halo.enabled != haloOn) v.halo.enabled = haloOn;
                if (haloOn)
                {
                    float diameter = (float)d.haloPx * scale;
                    float width = (float)d.haloWidthPx * scale;
                    float quad = diameter + width * 2f;
                    float world = quad / ppu;
                    v.halo.transform.localScale = new Vector3(world, world, 1f);
                    float k = (float)d.haloStrength * intensity * blink;
                    v.mpb.SetColor(IdColor, new Color(c.r * k, c.g * k, c.b * k, 1f));
                    v.mpb.SetVector(IdRing, new Vector4(diameter / quad, width / quad, 0f, 0f));
                    v.halo.SetPropertyBlock(v.mpb);
                }
            }
        }

        /// <summary>The part of the view the crystals keep to: the view less the edge margin on every side.</summary>
        Rect Inner(Rect area)
        {
            float m = (float)(Math.Max(0.0, D.edgeMarginPx) / p.pixelsPerUnit);
            if (m * 2f >= area.width || m * 2f >= area.height) return area;
            return new Rect(area.xMin + m, area.yMin + m, area.width - m * 2f, area.height - m * 2f);
        }

        Vector2 RandomDir()
        {
            float a = (float)(rng.NextDouble() * Math.PI * 2.0);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
    }
}
