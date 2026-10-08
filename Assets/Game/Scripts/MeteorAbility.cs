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
            float diameter = (float)(root.RunBodyPx(d.sizePx) / ppu);
            m.margin = diameter * 0.5f * look.Extent();

            // A single meteor takes any heading (the fanned headings of the shower went with it, 10/8).
            bool fanned = false;
            double heading = fanBase;
            PickPath(ref m, d, area, gravity, (float)(s.gravityRadius / ppu), fanned, heading);
            Vector2 path = m.end - m.start;
            float length = Mathf.Max(1e-4f, path.magnitude);
            m.pos = m.start;
            m.dir = path / length;
            m.duration = Math.Max(1e-3, d.crossSec);
            m.speed = (float)(length / m.duration);
            m.passOffset = m.start + m.dir * Vector2.Dot(gravity - m.start, m.dir) - gravity;
            m.entered = false;

            // Dust is shed at an even rate over the part of the planned path that is on screen.
            double share = d.DustShare(Level);
            m.shedCount = (int)Math.Round(share * s.dustCap);
            double onScreenSec = Clip(m.start, m.dir, area, out float t0, out float t1)
                ? (Mathf.Clamp(t1, 0f, length) - Mathf.Clamp(t0, 0f, length)) / length * m.duration
                : 0.0;
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

        /// <summary>
        /// A straight line across the screen. Each try takes a direction (a random one, or the shower's heading) and puts
        /// the line at a random distance within the pass range to one side of the gravity source; the first one whose
        /// nearest point to the gravity source is on screen is taken. When none is (the gravity source sits in a corner),
        /// the last try that crossed the screen at all is kept, and when there is none of those either, the line goes
        /// through the middle.
        /// </summary>
        void PickPath(ref Meteor m, MeteorDef d, Rect area, Vector2 gravity, float gravityRadius, bool fixedHeading, double heading)
        {
            Rect outer = Expand(area, m.margin);
            bool crossed = false;
            Vector2 u = Vector2.right;
            int tries = Math.Max(1, d.pathTries);
            for (int i = 0; i < tries; i++)
            {
                double angle = fixedHeading ? heading : rng.NextDouble() * Math.PI * 2.0;
                u = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                var side = new Vector2(-u.y, u.x);
                double radii = d.passMinRadii + (d.passMaxRadii - d.passMinRadii) * rng.NextDouble();
                float pass = gravityRadius * (float)radii * (rng.Next(2) == 0 ? 1f : -1f);
                Vector2 nearest = gravity + side * pass;
                if (!Clip(nearest, u, outer, out float t0, out float t1)) continue;
                m.start = nearest + u * t0;
                m.end = nearest + u * t1;
                crossed = true;
                if (area.Contains(nearest)) return;
            }
            if (crossed) return;
            Clip(area.center, u, outer, out float c0, out float c1);
            m.start = area.center + u * c0;
            m.end = area.center + u * c1;
        }

        static Rect Expand(Rect r, float by) => new Rect(r.xMin - by, r.yMin - by, r.width + by * 2f, r.height + by * 2f);

        /// <summary>Where the line point + u × t is inside the rect: from t0 to t1. False when it misses the rect.</summary>
        static bool Clip(Vector2 point, Vector2 u, Rect r, out float t0, out float t1)
        {
            t0 = float.NegativeInfinity;
            t1 = float.PositiveInfinity;
            return Slab(point.x, u.x, r.xMin, r.xMax, ref t0, ref t1) && Slab(point.y, u.y, r.yMin, r.yMax, ref t0, ref t1);
        }

        static bool Slab(float point, float u, float min, float max, ref float t0, ref float t1)
        {
            if (Mathf.Abs(u) < 1e-6f) return point >= min && point <= max;
            float a = (min - point) / u, b = (max - point) / u;
            if (a > b)
            {
                float swap = a;
                a = b;
                b = swap;
            }
            t0 = Mathf.Max(t0, a);
            t1 = Mathf.Min(t1, b);
            return t1 > t0;
        }

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
                while (m.shedAcc >= 1.0 && m.shed + m.notShed < m.shedCount)
                {
                    m.shedAcc -= 1.0;
                    Vector2 at = Vector2.LerpUnclamped(from, m.pos, (float)rng.NextDouble()) +
                                 side * (spread * (float)(rng.NextDouble() * 2.0 - 1.0));
                    // At the cap this returns false and the dust is simply not shed (24 §5).
                    if (root.Dust.Shed(at, vel, s, d.heavyWeight)) m.shed++;
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
            if (Expand(area, m.margin).Contains(m.pos) || !(m.entered || m.age >= m.duration)) return;
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
            }
        }
    }
}
