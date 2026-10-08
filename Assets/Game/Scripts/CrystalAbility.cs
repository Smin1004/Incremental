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
    /// The buff goes on the run's effective stats through <see cref="Amplify"/>, which GameRoot applies before anything
    /// reads them each tick, so the gravity ring, the dust and the other abilities all see the amplified radius. The
    /// crystal itself stays visible at effect intensity 0 (it is a thing to pick up); its glow, its halo and the flash
    /// of the gravity ring follow the intensity.
    /// </summary>
    public sealed class CrystalAbility : Ability
    {
        /// <summary>Over the dust and the satellites, under the meteor and the bodies.</summary>
        const int SortingOrder = 8;
        static readonly int IdColor = Shader.PropertyToID("_Color");
        static readonly int IdRing = Shader.PropertyToID("_Ring");

        readonly System.Random rng = new System.Random();

        bool alive;
        Vector2 pos, vel;
        double age;
        bool pulled;
        double buffLeft;
        float strength;
        double flashAge = -1;
        double clock = -1;
        int taken, missed;

        Transform holder;
        MeshRenderer diamond;
        Mesh diamondMesh;
        readonly Color[] diamondColors = new Color[4];
        SpriteRenderer glow;
        float glowSpriteSize = 1f;
        MeshRenderer halo;
        MaterialPropertyBlock mpb;
        bool shown;

        CrystalDef D => root.abilityTable.crystal;

        /// <summary>Seconds since the crystal that is waiting now appeared, or −1.</summary>
        public override float EffectAge => alive ? (float)age : -1f;

        /// <summary>
        /// Seconds since the crystal of the current cycle appeared, running on through its buff until the buff has
        /// faded, or −1 when nothing is going on (captures and checks).
        /// </summary>
        public float Clock => (float)clock;
        public bool Alive => alive;
        /// <summary>Where the waiting crystal is, world units (the test harness steers the pointer to it).</summary>
        public Vector2 Position => pos;
        /// <summary>Seconds of amplification left, 0 when none.</summary>
        public double BuffLeft => buffLeft;
        /// <summary>How far the buff is in, 0..1: 1 while amplified, ramping over rampSec at both ends.</summary>
        public float BuffStrength => strength;
        /// <summary>Seconds since the last crystal was absorbed, while the flash lasts; −1 otherwise.</summary>
        public float FlashAge => (float)flashAge;

        /// <summary>One crystal at a time: a charge that fills while one is waiting waits for it to be taken or missed.</summary>
        protected override bool Ready => !alive;

        protected override void Build(Transform parent)
        {
            holder = new GameObject("Crystal").transform;
            holder.SetParent(parent, false);

            // The halo: an FxRing quad behind everything else of the crystal.
            var haloGo = new GameObject("Halo", typeof(MeshFilter), typeof(MeshRenderer));
            haloGo.transform.SetParent(holder, false);
            haloGo.GetComponent<MeshFilter>().sharedMesh = PlanetVisual.Quad();
            halo = haloGo.GetComponent<MeshRenderer>();
            if (root.fxMaterial != null) halo.sharedMaterial = root.fxMaterial;
            halo.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            halo.receiveShadows = false;
            halo.sortingOrder = SortingOrder;
            mpb = new MaterialPropertyBlock();

            // The soft glow, as the satellites have.
            glow = new GameObject("Glow").AddComponent<SpriteRenderer>();
            glow.transform.SetParent(holder, false);
            glow.sprite = UIBuilder.SoftDotSprite();
            if (root.unlitMaterial != null) glow.sharedMaterial = root.unlitMaterial;
            glow.sortingOrder = SortingOrder + 1;
            glowSpriteSize = Mathf.Max(1e-3f, glow.sprite.bounds.size.x);

            // The diamond: four vertices with the color on them, drawn with the unlit material and a white texture.
            var go = new GameObject("Diamond", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(holder, false);
            diamondMesh = new Mesh { name = "CrystalDiamond" };
            diamondMesh.vertices = new[] { new Vector3(0f, 0.5f, 0f), new Vector3(0.36f, 0f, 0f), new Vector3(0f, -0.5f, 0f), new Vector3(-0.36f, 0f, 0f) };
            diamondMesh.uv = new[] { new Vector2(0.5f, 1f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0f), new Vector2(0f, 0.5f) };
            diamondMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            for (int i = 0; i < 4; i++) diamondColors[i] = Color.white;
            diamondMesh.colors = diamondColors;
            diamondMesh.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh = diamondMesh;
            diamond = go.GetComponent<MeshRenderer>();
            if (root.unlitMaterial != null) diamond.sharedMaterial = root.unlitMaterial;
            diamond.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            diamond.receiveShadows = false;
            diamond.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            diamond.sortingOrder = SortingOrder + 2;
            UIBuilder.UseWhiteTexture(diamond);

            holder.gameObject.SetActive(false);
        }

        /// <summary>The crystal appears: somewhere inside the edge margin, at least minDistanceRadii gravity radii away.</summary>
        protected override void Trigger(in EffectiveStats s)
        {
            var d = D;
            float ppu = (float)p.pixelsPerUnit;
            Rect inner = Inner(root.CameraArea());
            Vector2 g = root.GravityWorld;
            float least = (float)(s.gravityRadius * Math.Max(0.0, d.minDistanceRadii) / ppu);
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
            pos = best;
            vel = RandomDir() * (float)(d.driftSpeedPx / ppu);
            age = 0;
            alive = true;
            pulled = false;
            clock = 0;
            float px = bestDist * ppu;
            Log("appeared at " + RunClock + ": " + px.ToString("F0") + " px from the gravity source (" +
                (px / Math.Max(1e-6, s.gravityRadius)).ToString("0.##") + " radii), gone in " + d.lifeSec.ToString("0.##") + "s unless taken");
        }

        protected override void Step(double dt, bool holding, in EffectiveStats s)
        {
            var d = D;
            float ppu = (float)p.pixelsPerUnit;

            // The buff: its clock, the ramp of its strength, and the flash of the moment it began.
            if (buffLeft > 0)
            {
                buffLeft -= dt;
                if (buffLeft <= 0)
                {
                    buffLeft = 0;
                    Log("buff ended at " + RunClock + " (" + taken + " taken, " + missed + " missed this run)");
                }
            }
            float ramp = d.rampSec > 0 ? (float)(dt / d.rampSec) : 1f;
            strength = Mathf.MoveTowards(strength, buffLeft > 0 ? 1f : 0f, ramp);
            if (flashAge >= 0)
            {
                flashAge += dt;
                if (flashAge >= Math.Max(0.0, d.flashSec)) flashAge = -1;
            }
            if (clock >= 0)
            {
                clock += dt;
                if (!alive && buffLeft <= 0 && strength <= 0f && flashAge < 0) clock = -1;
            }
            if (!alive) return;

            age += dt;
            Vector2 g = root.GravityWorld;
            Vector2 to = g - pos;
            float dist = to.magnitude;
            // The radius in s is already amplified when a buff is on, so a second crystal is a little easier to reach.
            float radius = (float)(s.gravityRadius / ppu);
            if (holding && dist <= radius) pulled = true;
            else if (!holding) pulled = false;

            if (pulled)
            {
                float step = (float)(Math.Max(1e-3, d.pullInSpeedPx) / ppu) * (float)dt;
                float capture = (float)(p.captureRadius / ppu);
                if (dist <= capture + step)
                {
                    Absorb(s);
                    return;
                }
                pos += to / Mathf.Max(1e-6f, dist) * step;
                return;
            }

            // Waiting: a slow drift that turns back at the edge margin, and a life that runs out.
            Rect inner = Inner(root.CameraArea());
            pos += vel * (float)dt;
            if (pos.x < inner.xMin) { pos.x = inner.xMin; vel.x = Mathf.Abs(vel.x); }
            else if (pos.x > inner.xMax) { pos.x = inner.xMax; vel.x = -Mathf.Abs(vel.x); }
            if (pos.y < inner.yMin) { pos.y = inner.yMin; vel.y = Mathf.Abs(vel.y); }
            else if (pos.y > inner.yMax) { pos.y = inner.yMax; vel.y = -Mathf.Abs(vel.y); }
            if (age >= d.lifeSec)
            {
                alive = false;
                missed++;
                Log("missed at " + RunClock + ": gone after " + d.lifeSec.ToString("0.##") + "s, " + (dist * ppu).ToString("F0") + " px from the gravity source");
            }
        }

        void Absorb(in EffectiveStats s)
        {
            var d = D;
            bool again = buffLeft > 0;
            alive = false;
            taken++;
            buffLeft = Math.Max(0.0, d.buffSec);
            flashAge = 0;
            // 25 §3: the moment it is taken, the whole field answers.
            root.Dust.ExciteAll();
            Log("absorbed at " + RunClock + ": amplified for " + d.buffSec.ToString("0.##") + "s, gravity radius x" +
                d.RadiusMult(Level).ToString("0.##") + " (" + (s.gravityRadius * d.RadiusMult(Level)).ToString("F0") + " px), pull x" +
                d.PullMult(Level).ToString("0.##") + ", dust speed x" + d.speedMult.ToString("0.##") + (again ? " (the clock started over)" : ""));
        }

        /// <summary>24 §20.2: the amplification on this tick's stats, as far as the ramp has come.</summary>
        public override void Amplify(ref EffectiveStats s)
        {
            if (strength <= 0f) return;
            var d = D;
            s.gravityRadius *= 1.0 + (d.RadiusMult(Level) - 1.0) * strength;
            s.pullAccel *= 1.0 + (d.PullMult(Level) - 1.0) * strength;
        }

        public override double DustSpeedMult => strength > 0f ? 1.0 + (D.speedMult - 1.0) * strength : 1.0;

        /// <summary>While amplified the charge arc runs down with the buff instead of up with the charge.</summary>
        public override float ArcShare(double cooldown) =>
            buffLeft > 0 ? Mathf.Clamp01((float)(buffLeft / Math.Max(1e-3, D.buffSec))) : base.ArcShare(cooldown);

        /// <summary>The gravity ring: gold while amplified, and a gold flash the moment a crystal is taken (× effect intensity).</summary>
        public override void TintRing(ref Color color, ref float amount)
        {
            var d = D;
            float flash = flashAge >= 0 && d.flashSec > 0 ? (1f - (float)(flashAge / d.flashSec)) * root.EffectIntensity : 0f;
            float a = Mathf.Max(strength * 0.8f, flash);
            if (a <= amount) return;
            amount = a;
            color = d.ringColor;
        }

        protected override void OnClear()
        {
            alive = false;
            pulled = false;
            buffLeft = 0;
            strength = 0f;
            flashAge = -1;
            clock = -1;
            age = 0;
            taken = missed = 0;
        }

        public override void Render(bool show, float lead)
        {
            bool on = show && alive && !root.Paused;
            if (on != shown)
            {
                shown = on;
                holder.gameObject.SetActive(on);
            }
            if (!on) return;

            var d = D;
            float ppu = (float)p.pixelsPerUnit;
            float intensity = root.EffectIntensity;
            float t = (float)age + lead;
            // Where it is now: a tick's lead along the drift, or along the pull-in.
            Vector2 at = pos;
            if (pulled)
            {
                Vector2 to = root.GravityWorld - pos;
                float dist = to.magnitude;
                if (dist > 1e-4f) at += to / dist * Mathf.Min(dist, (float)(d.pullInSpeedPx / ppu) * lead);
            }
            else at += vel * lead;
            holder.position = new Vector3(at.x, at.y, 0f);

            // The pulse, and the blink over the last seconds.
            float pulse = 1f + (float)d.pulseScale * (0.5f + 0.5f * Mathf.Sin(t / Mathf.Max(0.05f, (float)d.pulseSec) * Mathf.PI * 2f));
            float blink = 1f;
            if (d.blinkSec > 0 && t >= d.lifeSec - d.blinkSec && !pulled)
                blink = Mathf.Sin(t * (float)d.blinkHz * Mathf.PI * 2f) >= 0f ? 1f : 0.25f;

            // 22 §6: a thing this small keeps the floor the bodies keep as the camera pulls back.
            float size = (float)(root.RunBodyPx(d.sizePx) / ppu) * pulse;
            float scale = size / Mathf.Max(1e-6f, (float)d.sizePx / ppu);
            diamond.transform.localScale = new Vector3(size, size, 1f);
            Color c = d.color;
            c.a *= blink;
            for (int i = 0; i < 4; i++) diamondColors[i] = c;
            diamondMesh.colors = diamondColors;

            float glowSize = (float)(d.glowPx / ppu) * scale;
            bool glowOn = d.glowPx > 0 && d.glowAlpha > 0 && intensity > 0f;
            if (glow.enabled != glowOn) glow.enabled = glowOn;
            if (glowOn)
            {
                glow.transform.localScale = Vector3.one * (glowSize / glowSpriteSize);
                glow.color = new Color(c.r, c.g, c.b, Mathf.Clamp01((float)d.glowAlpha) * intensity * blink);
            }

            bool haloOn = d.haloPx > 0 && d.haloStrength > 0 && intensity > 0f;
            if (halo.enabled != haloOn) halo.enabled = haloOn;
            if (haloOn)
            {
                float diameter = (float)d.haloPx * scale;
                float width = (float)d.haloWidthPx * scale;
                float quad = diameter + width * 2f;
                float world = quad / ppu;
                halo.transform.localScale = new Vector3(world, world, 1f);
                float k = (float)d.haloStrength * intensity * blink;
                mpb.SetColor(IdColor, new Color(c.r * k, c.g * k, c.b * k, 1f));
                mpb.SetVector(IdRing, new Vector4(diameter / quad, width / quad, 0f, 0f));
                halo.SetPropertyBlock(mpb);
            }
        }

        /// <summary>The part of the view the crystal keeps to: the view less the edge margin on every side.</summary>
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
