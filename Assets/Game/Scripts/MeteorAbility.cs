using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// 24 §5 유성. When its cooldown fills, a meteor comes in from outside one edge of the screen and leaves past the
    /// other, crossSec from end to end. The path is a straight line picked at random each time, but always one that
    /// passes the gravity source at a distance inside the pass range: near enough for what it sheds to be pulled in,
    /// never through the middle. Gravity does not act on it and it cannot be caught.
    ///
    /// The path is drawn from where the gravity source is when the meteor fires. If the player moves after that, the
    /// meteor bends to follow: while it is on screen it turns, by homingDegPerSec at most, toward the point that keeps
    /// the same distance and side from the gravity source as it has now. A player who stays put gets the straight line.
    ///
    /// While it is on screen it sheds dust evenly along its path, a share of the dust cap in all and never past the cap.
    /// The shed dust is born moving the meteor's way and leans toward the heavier kinds. It also drags the dust that was
    /// already there along with it (the wake), and that dust lights up (24 §8): the meteor stirs the field as it goes by.
    ///
    /// It wears the look of the comet tier with a longer tail. The planet shader points a tail away from the light, so
    /// the light is put straight ahead of the meteor.
    ///
    /// 15th pass (29 §10.7 M1-M5, "정적인 행성 스프라이트가 쭉 날아가는 기분"): the look is now a burning rock (the asteroid
    /// tier, turning, M4) with a hot glow leaning forward over it (M1), a short fire tail of the trail ribbon's kind (M2),
    /// shed dust that flies off as sparks (M3) and a streak of light where it comes in over the edge (M5). Fast, hot and
    /// short, so it is not taken for the S3 comet, which is slow, cold and long-tailed. Everything but the body follows
    /// the effect intensity.
    ///
    /// 24 §21.2 유성우, the evolved form as decided on 10/8: the same meteor at a shorter cooldown (MeteorDef.showerCooldownSec),
    /// one at a time. The five-at-once shower of the 7th pass was rejected as too much; the array of meteors below is
    /// what is left of it and holds one.
    /// </summary>
    public sealed class MeteorAbility : Ability
    {
        /// <summary>Over the dust, the rings and the satellites; under the bodies the player makes.</summary>
        const int SortingOrder = 9;

        struct Meteor
        {
            public bool active;
            public double age, duration;
            public Vector2 start, end, pos, dir;
            public float speed;
            /// <summary>How far outside the screen the meteor starts and ends, so that its tail is off screen too.</summary>
            public float margin;
            /// <summary>From the gravity source to the point of the planned path nearest to it: what the homing keeps.</summary>
            public Vector2 passOffset;
            public bool entered;
            /// <summary>Dust per second while on screen, and how many dust one crossing sheds in all.</summary>
            public double shedRate, shedAcc;
            public int shedCount, shed, notShed;
            public float closestPx;
            /// <summary>M5: where and when (seconds of its age) it came in over the edge; −1 = not yet.</summary>
            public Vector2 entryAt;
            public float entryAge;
        }

        /// <summary>Path and shed positions. Apart from the dust and visual RNGs, like the fragment rolls.</summary>
        readonly System.Random rng = new System.Random();
        Transform holder;
        Meteor[] meteors = new Meteor[0];
        PlanetVisual[] bodies = new PlanetVisual[0];
        /// <summary>Launches still to come from this trigger (one), and the heading the next one takes.</summary>
        int pending, launched;
        double fanBase;
        int triggerCount = 1;

        MeteorDef D => root.abilityTable.meteor;

        public override float EffectAge
        {
            get
            {
                float oldest = -1f;
                for (int i = 0; i < meteors.Length; i++)
                    if (meteors[i].active && (float)meteors[i].age > oldest) oldest = (float)meteors[i].age;
                return oldest;
            }
        }

        /// <summary>How many meteors are crossing right now (captures and checks).</summary>
        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < meteors.Length; i++) if (meteors[i].active) n++;
                return n;
            }
        }

        /// <summary>One trigger at a time: a charge that fills while meteors are still crossing waits for them.</summary>
        protected override bool Ready => pending == 0 && ActiveCount == 0;

        protected override void Build(Transform parent)
        {
            holder = new GameObject("Meteors").transform;
            holder.SetParent(parent, false);
            Grow(1);
        }

        void Grow(int n)
        {
            if (meteors.Length >= n) return;
            int had = meteors.Length;
            Array.Resize(ref meteors, n);
            Array.Resize(ref bodies, n);
            for (int i = had; i < n; i++)
            {
                bodies[i] = PlanetVisual.Create("Meteor" + i, holder, root.planetMaterial, SortingOrder);
                bodies[i].gameObject.SetActive(false);
            }
        }

        protected override void Trigger(in EffectiveStats s)
        {
            // One meteor per trigger; evolved, the triggers just come faster (10/8, MeteorDef.showerCooldownSec).
            triggerCount = 1;
            Grow(triggerCount);
            pending = triggerCount;
            launched = 0;
            fanBase = rng.NextDouble() * Math.PI * 2.0;
        }

        /// <summary>One meteor off: its path, its shedding plan and its look.</summary>
        void Launch(in EffectiveStats s)
        {
            var d = D;
            int slot = -1;
            for (int i = 0; i < meteors.Length; i++) if (!meteors[i].active) { slot = i; break; }
            if (slot < 0) return;
            ref Meteor m = ref meteors[slot];
            float ppu = (float)p.pixelsPerUnit;
            Rect area = root.CameraArea();
            Vector2 gravity = root.GravityWorld;

            var tier = root.celestialTable.Get(d.lookTier);
            var look = tier != null && tier.look != null ? tier.look.Clone() : new PlanetLook();
            look.tailLength = (float)d.tailLength;
            // M4: it turns as it goes.
            look.spinDegPerSec = (float)d.spinDegPerSec;
            float diameter = (float)(root.RunBodyPx(d.sizePx) / ppu);
            m.margin = diameter * 0.5f * look.Extent();

            // A single meteor takes any heading (the fanned headings of the shower went with it, 10/8).
            bool fanned = false;
            double heading = fanBase;
            // The path picking is shared with the S3 comet since the 15th pass (CrossingPath).
            CrossingPath.Pick(rng, area, m.margin, gravity, (float)(s.gravityRadius / ppu), d.passMinRadii, d.passMaxRadii, d.pathTries,
                fanned, heading, out m.start, out m.end);
            Vector2 path = m.end - m.start;
            float length = Mathf.Max(1e-4f, path.magnitude);
            m.pos = m.start;
            m.dir = path / length;
            m.duration = Math.Max(1e-3, d.crossSec);
            m.speed = (float)(length / m.duration);
            m.passOffset = m.start + m.dir * Vector2.Dot(gravity - m.start, m.dir) - gravity;
            m.entered = false;
            m.entryAge = -1f;

            // Dust is shed at an even rate over the part of the planned path that is on screen.
            double share = d.DustShare(Level);
            m.shedCount = (int)Math.Round(share * s.dustCap);
            double onScreenSec = CrossingPath.OnScreenSec(m.start, m.end, area, m.duration);
            m.shedRate = onScreenSec > 0 ? m.shedCount / onScreenSec : 0.0;
            m.shedAcc = 0;
            m.shed = m.notShed = 0;
            m.closestPx = float.MaxValue;
            m.age = 0;
            m.active = true;
            launched++;

            float pass = m.passOffset.magnitude * ppu;
            Log((fanned ? "#" + launched + " " : "") + "fired at " + RunClock + ": crossing in " + m.duration.ToString("0.##") + "s at " +
                (m.speed * ppu).ToString("F0") + " px/s, planned to pass the gravity source at " + pass.ToString("F0") + " px (" +
                Radii(pass, s) + " radii), to shed " + m.shedCount + " dust (" + (share * 100.0).ToString("0.#") + "% of the cap " +
                s.dustCap.ToString("F0") + ")");

            var body = bodies[slot];
            body.SetLook(look, root.NextVisualSeed(), p.planetAxisPitchDeg);
            body.SetLightDirection(m.dir);
            body.SetDiameter(diameter);
            body.SetFade(1f);
        }

        static string Radii(float px, in EffectiveStats s) => (px / Math.Max(1e-6, s.gravityRadius)).ToString("0.##");

        static Rect Expand(Rect r, float by) => CrossingPath.Expand(r, by);

        protected override void Step(double dt, bool holding, in EffectiveStats s)
        {
            var d = D;
            // The launch goes the tick after the trigger fired (one per trigger since 10/8).
            while (pending > 0)
            {
                Launch(s);
                pending--;
            }
            for (int i = 0; i < meteors.Length; i++)
                if (meteors[i].active) StepMeteor(ref meteors[i], dt, s, d);
        }

        void StepMeteor(ref Meteor m, double dt, in EffectiveStats s, MeteorDef d)
        {
            float ppu = (float)p.pixelsPerUnit;
            float fdt = (float)dt;
            Rect area = root.CameraArea();
            Vector2 gravity = root.GravityWorld;
            bool onScreen = area.Contains(m.pos);
            if (onScreen && !m.entered)
            {
                // M5: the edge it came in over.
                m.entryAt = m.pos;
                m.entryAge = (float)m.age;
            }
            m.entered |= onScreen;

            // Homing: the planned path passed the gravity source at passOffset. If the gravity source has moved since,
            // the meteor turns toward where that point is now, until it has gone by it. It only does so for the time
            // the crossing was planned to take, so it always leaves the screen in the end.
            if (onScreen && m.age < m.duration && d.homingDegPerSec > 0)
            {
                Vector2 to = gravity + m.passOffset - m.pos;
                if (Vector2.Dot(to, m.dir) > 0f)
                {
                    float most = (float)d.homingDegPerSec * fdt;
                    float turn = Mathf.Clamp(Vector2.SignedAngle(m.dir, to), -most, most) * Mathf.Deg2Rad;
                    float cos = Mathf.Cos(turn), sin = Mathf.Sin(turn);
                    m.dir = new Vector2(m.dir.x * cos - m.dir.y * sin, m.dir.x * sin + m.dir.y * cos);
                }
            }

            Vector2 from = m.pos;
            m.pos += m.dir * (m.speed * fdt);

            // The dust of this tick goes somewhere on the stretch the meteor covered in it, so the crossing sheds evenly
            // along the on-screen part of the path.
            if (onScreen && m.shed + m.notShed < m.shedCount)
            {
                m.shedAcc += m.shedRate * dt;
                var side = new Vector2(-m.dir.y, m.dir.x);
                Vector2 vel = m.dir * (m.speed * (float)d.dustSpeedShare);
                float spread = (float)(d.shedSpreadPx / ppu);
                float kick = (float)(d.sparkKickPx / ppu);
                while (m.shedAcc >= 1.0 && m.shed + m.notShed < m.shedCount)
                {
                    m.shedAcc -= 1.0;
                    Vector2 at = Vector2.LerpUnclamped(from, m.pos, (float)rng.NextDouble()) +
                                 side * (spread * (float)(rng.NextDouble() * 2.0 - 1.0));
                    // At the cap this returns false and the dust is simply not shed (24 §5).
                    // M3: thrown off sideways and a little back, flaring, so they read as sparks.
                    float sideways = (float)(rng.NextDouble() * 2.0 - 1.0);
                    Vector2 sparkVel = vel + side * (kick * sideways) - m.dir * (kick * 0.4f * (float)rng.NextDouble());
                    if (root.Dust.Shed(at, sparkVel, s, d.heavyWeight, (float)d.sparkSec, (float)Math.Max(0.0, d.sparkBright - 1.0))) m.shed++;
                    else m.notShed++;
                }
            }

            // The wake: the dust around the meteor is dragged along its way and lights up.
            if (d.wakeAccelPx > 0 && d.wakeRadiusShare > 0)
            {
                float radius = (float)(s.gravityRadius * d.wakeRadiusShare) / ppu;
                system.AddForce(DustForce.Drag(m.pos, radius, (float)(d.wakeAccelPx / ppu), m.dir));
            }

            m.closestPx = Mathf.Min(m.closestPx, (gravity - m.pos).magnitude * ppu);
            m.age += dt;
            // Done once it is off screen, tail and all: after having been on screen, or after the planned crossing time
            // for a path that never came on screen.
            // 15th pass: the fire tail (M2) has to be off screen too before it is done.
            if (Expand(area, m.margin + m.speed * (float)d.fireTailSec).Contains(m.pos) || !(m.entered || m.age >= m.duration)) return;
            m.active = false;
            Log("gone after " + m.age.ToString("0.##") + "s: came within " + m.closestPx.ToString("F0") + " px (" + Radii(m.closestPx, s) +
                " radii) of the gravity source, shed " + m.shed + " dust" +
                (m.notShed > 0 ? ", " + m.notShed + " not shed (at the dust cap, or the spot was off screen)" : string.Empty) +
                ", " + (root.Dust.Count - root.Dust.OutsideCount) + " on screen and " + root.Dust.OutsideCount + " outside it now");
        }

        protected override void OnClear()
        {
            for (int i = 0; i < meteors.Length; i++)
            {
                meteors[i].active = false;
                meteors[i].age = 0;
            }
            pending = 0;
            launched = 0;
        }

        public override void Render(bool show, float lead)
        {
            for (int i = 0; i < meteors.Length; i++)
            {
                ref Meteor m = ref meteors[i];
                var body = bodies[i];
                bool on = show && m.active;
                if (body.gameObject.activeSelf != on) body.gameObject.SetActive(on);
                if (!on) continue;
                Vector2 at = m.pos + m.dir * (m.speed * lead);
                body.transform.position = new Vector3(at.x, at.y, 0f);
                // The tail trails behind, so the light stays straight ahead as the meteor turns.
                body.SetLightDirection(m.dir);
                // The coma is a glow like any other and follows the effect intensity; the head and the tail stay.
                body.SetGlowMul(root.EffectIntensity);
                DrawFire(ref m, at, root.EffectIntensity);
            }
        }

        /// <summary>Seconds of the effect clock, for the flicker and the wobble (render only).</summary>
        float FxTime => Time.time;

        /// <summary>15th pass M1, M2, M5 for one meteor whose head is drawn at <paramref name="at"/>.</summary>
        void DrawFire(ref Meteor m, Vector2 at, float intensity)
        {
            if (intensity <= 0f || root.Glow == null || root.Lines == null) return;
            var d = D;
            float ppu = (float)p.pixelsPerUnit;
            float head = (float)(root.RunBodyPx(d.sizePx) / ppu);
            float t = FxTime;
            float flicker = 1f + (float)d.headFlicker * (Mathf.PerlinNoise(t * (float)d.headFlickerHz, m.start.x) * 2f - 1f);

            // M2 the fire tail: as long as the meteor travels in fireTailSec, under the head.
            float length = m.speed * (float)d.fireTailSec;
            float width = (float)(d.fireTailWidthPx / ppu);
            float wobble = (float)(d.fireTailWobblePx / ppu);
            int n = tail.Length;
            Vector2 side = new Vector2(-m.dir.y, m.dir.x);
            float bright = (float)d.fireTailBright * intensity * flicker;
            Color c0 = d.fireHeadColor.linear, c1 = d.fireMidColor.linear, c2 = d.fireEndColor.linear;
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1);
                float w = wobble * u * (Mathf.PerlinNoise(u * 4f - t * 6f, m.start.y) * 2f - 1f);
                tail[i] = at - m.dir * (length * u) + side * w;
                tailWidths[i] = width * Mathf.Pow(1f - u, 0.8f) * (1f + 0.25f * Mathf.Sin(u * 17f - t * 23f) * u);
                Color c = u < 0.35f ? Color.Lerp(c0, c1, u / 0.35f) : Color.Lerp(c1, c2, (u - 0.35f) / 0.65f);
                float b = bright * (1f - u) * (1f - u * 0.5f);
                tailColors[i] = new Color(c.r * b, c.g * b, c.b * b);
            }
            root.Lines.Ribbon(tail, tailWidths, tailColors, n);

            // M1 the hot head: an orange glow leaning forward, a white core at its front.
            Vector2 forward = at + m.dir * (head * (float)d.headGlowForward);
            var g = d.headGlowColor.linear;
            var w0 = d.headCoreColor.linear;
            root.Glow.Draw(forward, (float)(root.RunBodyPx(d.headGlowPx) / ppu),
                new Color(g.r, g.g, g.b, (float)d.headGlowStrength * intensity * flicker), 0f, 0.5f, 0.5f, m.dir, (float)d.headGlowStretch);
            root.Glow.Draw(forward + m.dir * (head * 0.15f), head * 1.1f,
                new Color(w0.r, w0.g, w0.b, (float)d.headCoreStrength * intensity * flicker), 0f, 0.35f, 1f, m.dir, 1.3f);

            // M5 the entry flash: a streak of light inward from the edge it came in over, and a flash at that point.
            float since = m.entryAge >= 0f ? (float)m.age - m.entryAge : -1f;
            float sec = (float)d.entryFlashSec;
            if (since >= 0f && since < sec && sec > 0f)
            {
                float k = since / sec;
                float fade = (1f - k) * (1f - k);
                float len = (float)(d.entryFlashPx / ppu) * (0.4f + 0.6f * k);
                float eb = (float)d.entryFlashBright * fade * intensity;
                var hot = new Color(w0.r * eb, w0.g * eb, w0.b * eb);
                var warm = new Color(g.r * eb * 0.3f, g.g * eb * 0.3f, g.b * eb * 0.3f);
                float ew = (float)(d.entryFlashWidthPx / ppu);
                root.Lines.Segment(m.entryAt - m.dir * (len * 0.3f), m.entryAt + m.dir * len, ew * 0.4f, ew, warm, hot);
                root.Glow.Draw(m.entryAt, (float)(d.entryGlowPx / ppu) * (0.6f + 0.4f * k),
                    new Color(w0.r, w0.g, w0.b, 0.6f * fade * intensity), 0f, 0.45f, 0.8f, m.dir, 1.8f);
            }
        }

        readonly Vector2[] tail = new Vector2[16];
        readonly float[] tailWidths = new float[16];
        readonly Color[] tailColors = new Color[16];
    }
}
