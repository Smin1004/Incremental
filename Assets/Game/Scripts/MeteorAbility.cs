using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// 24 §5 유성. When its cooldown fills, a meteor comes in from outside one edge of the screen and leaves past the
    /// other in a straight line, crossSec from end to end. The path is random each time but always passes the gravity
    /// source at 1 to 2.5 gravity radii: near enough for what it sheds to be pulled in, never through the middle.
    /// Gravity does not act on it and it cannot be caught.
    ///
    /// While it is on screen it sheds dust evenly along its path, a share of the dust cap in all and never past the cap.
    /// The shed dust is born moving the meteor's way and leans toward the heavier kinds. It also drags the dust that was
    /// already there along with it (the wake), and that dust lights up (24 §8): the meteor stirs the field as it goes by.
    ///
    /// It wears the look of the comet tier with a longer tail. The planet shader points a tail away from the light, so
    /// the light is put straight ahead of the meteor.
    /// </summary>
    public sealed class MeteorAbility : Ability
    {
        /// <summary>Over the dust, the rings and the satellites; under the bodies the player makes.</summary>
        const int SortingOrder = 9;

        /// <summary>Path and shed positions. Apart from the dust and visual RNGs, like the fragment rolls.</summary>
        readonly System.Random rng = new System.Random();
        PlanetVisual body;
        bool active;
        double age;
        double duration = 1;
        Vector2 start, end, dir;
        float speed;
        /// <summary>The part of the path, as 0..1 of it, that lies inside the screen: where dust is shed.</summary>
        double inFrom, inTo;
        double shedTotal, shedAcc;
        int shed, notShed;

        MeteorDef D => root.abilityTable.meteor;

        public override float EffectAge => active ? (float)age : -1f;

        /// <summary>One meteor at a time: a charge that fills while one is still crossing waits for it.</summary>
        protected override bool Ready => !active;

        protected override void Build(Transform parent)
        {
            body = PlanetVisual.Create("Meteor", parent, root.planetMaterial, SortingOrder);
            body.gameObject.SetActive(false);
        }

        protected override void Trigger(in EffectiveStats s)
        {
            var d = D;
            float ppu = (float)p.pixelsPerUnit;
            Rect area = root.CameraArea();

            var tier = root.celestialTable.Get(d.lookTier);
            var look = tier != null && tier.look != null ? tier.look.Clone() : new PlanetLook();
            look.tailLength = (float)d.tailLength;
            float diameter = (float)(root.RunBodyPx(d.sizePx) / ppu);
            // The path starts and ends this far outside the screen, so no part of the meteor, tail included, pops in or out.
            float margin = diameter * 0.5f * look.Extent();

            PickPath(d, area, margin, root.GravityWorld, (float)(s.gravityRadius / ppu));
            Vector2 path = end - start;
            float length = Mathf.Max(1e-4f, path.magnitude);
            dir = path / length;
            duration = Math.Max(1e-3, d.crossSec);
            speed = (float)(length / duration);
            if (Clip(start, dir, area, out float t0, out float t1))
            {
                inFrom = Mathf.Clamp01(t0 / length);
                inTo = Mathf.Clamp01(t1 / length);
            }
            else inFrom = inTo = 0;
            double share = d.DustShare(Level);
            shedTotal = share * s.dustCap;
            shedAcc = 0;
            shed = notShed = 0;
            age = 0;
            active = true;

            // How near the path comes to the gravity source: the distance from it to the line.
            Vector2 toGravity = root.GravityWorld - start;
            float pass = Mathf.Abs(toGravity.x * dir.y - toGravity.y * dir.x) * ppu;
            Log("fired at " + RunClock + ": crossing in " + duration.ToString("0.##") + "s at " + (speed * ppu).ToString("F0") +
                " px/s, passing the gravity source at " + pass.ToString("F0") + " px (" + (pass / Math.Max(1e-6, s.gravityRadius)).ToString("0.##") +
                " radii), to shed " + shedTotal.ToString("F0") + " dust (" + (share * 100.0).ToString("0.#") + "% of the cap " + s.dustCap.ToString("F0") + ")");

            body.SetLook(look, root.NextVisualSeed(), p.planetAxisPitchDeg);
            body.SetLightDirection(dir);
            body.SetDiameter(diameter);
            body.SetFade(1f);
        }

        /// <summary>
        /// A random straight line across the screen. Each try takes a random direction and puts the line at a random
        /// distance within the pass range to one side of the gravity source; the first one whose nearest point to the
        /// gravity source is on screen is taken. When none is (the gravity source sits in a corner), the last try that
        /// crossed the screen at all is kept, and when there is none of those either, the line goes through the middle.
        /// </summary>
        void PickPath(MeteorDef d, Rect area, float margin, Vector2 gravity, float gravityRadius)
        {
            var outer = new Rect(area.xMin - margin, area.yMin - margin, area.width + margin * 2f, area.height + margin * 2f);
            bool crossed = false;
            Vector2 u = Vector2.right;
            int tries = Math.Max(1, d.pathTries);
            for (int i = 0; i < tries; i++)
            {
                double angle = rng.NextDouble() * Math.PI * 2.0;
                u = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                var side = new Vector2(-u.y, u.x);
                double radii = d.passMinRadii + (d.passMaxRadii - d.passMinRadii) * rng.NextDouble();
                float pass = gravityRadius * (float)radii * (rng.Next(2) == 0 ? 1f : -1f);
                Vector2 nearest = gravity + side * pass;
                if (!Clip(nearest, u, outer, out float t0, out float t1)) continue;
                start = nearest + u * t0;
                end = nearest + u * t1;
                crossed = true;
                if (area.Contains(nearest)) return;
            }
            if (crossed) return;
            Clip(area.center, u, outer, out float c0, out float c1);
            start = area.center + u * c0;
            end = area.center + u * c1;
        }

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
            if (!active) return;
            var d = D;
            float ppu = (float)p.pixelsPerUnit;
            double k0 = age / duration;
            double k1 = Math.Min(1.0, (age + dt) / duration);

            // The dust of this tick goes somewhere on the stretch the meteor covered in it, so the whole crossing sheds
            // evenly along the on-screen part of the path.
            double from = Math.Max(k0, inFrom), to = Math.Min(k1, inTo);
            if (to > from)
            {
                shedAcc += shedTotal * (to - from) / (inTo - inFrom);
                var side = new Vector2(-dir.y, dir.x);
                Vector2 vel = dir * (speed * (float)d.dustSpeedShare);
                float spread = (float)(d.shedSpreadPx / ppu);
                while (shedAcc >= 1.0)
                {
                    shedAcc -= 1.0;
                    float k = (float)(from + (to - from) * rng.NextDouble());
                    Vector2 pos = Vector2.LerpUnclamped(start, end, k) + side * (spread * (float)(rng.NextDouble() * 2.0 - 1.0));
                    // At the cap this returns false and the dust is simply not shed (24 §5).
                    if (root.Dust.Shed(pos, vel, s, d.heavyWeight)) shed++;
                    else notShed++;
                }
            }

            // The wake: the dust around the meteor is dragged along its way and lights up.
            if (d.wakeAccelPx > 0 && d.wakeRadiusShare > 0)
            {
                Vector2 at = Vector2.LerpUnclamped(start, end, (float)k1);
                float radius = (float)(s.gravityRadius * d.wakeRadiusShare) / ppu;
                system.AddForce(DustForce.Drag(at, radius, (float)(d.wakeAccelPx / ppu), dir));
            }

            age += dt;
            if (age < duration) return;
            active = false;
            Log("gone: shed " + shed + " dust" + (notShed > 0 ? ", " + notShed + " not shed (at the dust cap, or the spot was off screen)" : string.Empty) +
                ", " + root.Dust.Count + " on screen now");
        }

        protected override void OnClear()
        {
            active = false;
            age = 0;
        }

        public override void Render(bool show, float lead)
        {
            bool on = show && active;
            if (body.gameObject.activeSelf != on) body.gameObject.SetActive(on);
            if (!on) return;
            float k = Mathf.Clamp01((float)((age + lead) / duration));
            Vector2 pos = Vector2.LerpUnclamped(start, end, k);
            body.transform.position = new Vector3(pos.x, pos.y, 0f);
            // The coma is a glow like any other and follows the effect intensity; the head and the tail stay.
            body.SetGlowMul(root.EffectIntensity);
        }
    }
}
