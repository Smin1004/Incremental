using System;
using UnityEngine;

namespace Incremental
{
    public struct Dust
    {
        public Vector2 pos;
        public Vector2 vel;
        /// <summary>0..1 position between the two dust colors of the palette (render only).</summary>
        public float tint;
        /// <summary>Size and brightness multipliers, 1 ± dustSizeJitter / dustBrightJitter (render only, 14 §5).</summary>
        public float size;
        public float bright;
        /// <summary>Index into GameParams.dustKinds (20 §4); 0 is the base dust. Decided once, when the dust is spawned.</summary>
        public int kind;
        /// <summary>
        /// 22 §5: how many of this dust's trail slots hold a position, and where the next one goes. The positions
        /// themselves live in <see cref="DustField.Trail"/>, one block of GameParams.trailPoints per dust, because a
        /// struct cannot hold an array of its own. Render only.
        /// </summary>
        public int trailFill;
        public int trailHead;
    }

    /// <summary>
    /// Dust simulation. One struct array, no GameObjects. Advanced only from GameRoot's fixed tick.
    /// Positions and speeds are in world units (px / pixelsPerUnit).
    /// </summary>
    public sealed class DustField : MonoBehaviour
    {
        Dust[] items = new Dust[512];
        int count;
        /// <summary>22 §5: trailLen positions per dust, in the ring buffer that dust owns.</summary>
        Vector2[] trail = System.Array.Empty<Vector2>();
        int trailLen;
        double trailClock;
        double[] kindMass = { 1.0 };
        float[] kindAccel = { 1f };
        double spawnAcc;
        System.Random rng = new System.Random();
        // Render-only variation draws from its own RNG so the simulation sequence is the same as before (bot measurements).
        System.Random visualRng = new System.Random();
        GameParams p;

        public Dust[] Items => items;
        public int Count => count;
        /// <summary>22 §5: the shared trail buffer; dust i owns [i x <see cref="TrailLen"/>, +TrailLen). Render only.</summary>
        public Vector2[] Trail => trail;
        public int TrailLen => trailLen;

        /// <summary>
        /// 22 §5: the <paramref name="age"/>-th most recent kept position of dust <paramref name="i"/> (0 = the newest),
        /// or its current position when that slot is empty.
        /// </summary>
        public Vector2 TrailPoint(int i, int age)
        {
            if (trailLen <= 0 || age >= items[i].trailFill) return items[i].pos;
            int slot = (items[i].trailHead - 1 - age) % trailLen;
            if (slot < 0) slot += trailLen;
            return trail[i * trailLen + slot];
        }
        /// <summary>Dust captured since the last <see cref="Reset"/>: the number and the mass. Measurement only (20 §3).</summary>
        public int CapturedCount { get; private set; }
        public double CapturedMass { get; private set; }
        /// <summary>Visible area the dust is confined to (camera rect, world units).</summary>
        public Rect Area { get; private set; }

        public void Init(GameParams gameParams)
        {
            p = gameParams;
            trailLen = Mathf.Max(0, p.trailPoints);
            EnsureTrail();
            ReadKinds();
        }

        /// <summary>
        /// 22 §7: the mass and the pull multiplier of every dust kind, read out of the data once instead of once per dust
        /// per tick. The values are fixed data (20 §4), so one read at startup is enough.
        /// </summary>
        void ReadKinds()
        {
            int kinds = Mathf.Max(1, p.DustKindCount);
            kindMass = new double[kinds];
            kindAccel = new float[kinds];
            for (int k = 0; k < kinds; k++)
            {
                var def = p.DustKind(k);
                kindMass[k] = def != null ? def.mass : 1.0;
                kindAccel[k] = def != null ? (float)def.accelMult : 1f;
            }
        }

        void EnsureTrail()
        {
            int need = items.Length * trailLen;
            if (trail.Length != need) trail = new Vector2[need];
        }

        /// <summary>22 §5: pushes the current position of every trailed dust, once every trailSampleSec of game time.</summary>
        void TickTrails(double dt)
        {
            if (trailLen <= 0) return;
            trailClock += dt;
            double step = Math.Max(1e-4, p.trailSampleSec);
            if (trailClock < step) return;
            // One push per call even after a long stall: a trail is a picture of the recent path, not a log.
            trailClock = 0;
            for (int i = 0; i < count; i++)
            {
                ref Dust d = ref items[i];
                if (d.kind <= 0) continue;
                trail[i * trailLen + d.trailHead] = d.pos;
                d.trailHead = (d.trailHead + 1) % trailLen;
                if (d.trailFill < trailLen) d.trailFill++;
            }
        }

        public void Reseed(int seed) { rng = new System.Random(seed); }

        /// <summary>Clears the field and places the initial dust outside the (effective) gravity radius of the cursor.</summary>
        public void Reset(int initial, Rect area, Vector2 cursor, in EffectiveStats s)
        {
            count = 0;
            spawnAcc = 0;
            trailClock = 0;
            CapturedCount = 0;
            CapturedMass = 0;
            Area = area;
            float gravR = Px(s.gravityRadius);
            for (int i = 0; i < initial; i++) SpawnRandom(cursor, gravR, s);
        }

        /// <summary>
        /// Pull strength at distance <paramref name="dist"/> as a share of the full acceleration (20 §3). Full within
        /// <paramref name="fullRatio"/> × the radius, smoothstep down to <paramref name="edgeShare"/> at the radius itself,
        /// then linearly to 0 at <paramref name="outerRatio"/> × the radius. A pure function so the tests can read the curve.
        /// </summary>
        public static double PullFactor(double dist, double radius, double fullRatio, double edgeShare, double outerRatio)
        {
            if (!(radius > 0)) return 0;
            double full = radius * fullRatio;
            if (dist <= full) return 1;
            if (dist <= radius)
            {
                // smoothstep 1 → edgeShare over full..radius
                double x = radius > full ? (dist - full) / (radius - full) : 1.0;
                double sm = x * x * (3.0 - 2.0 * x);
                return 1.0 + (edgeShare - 1.0) * sm;
            }
            double outer = radius * outerRatio;
            if (dist >= outer) return 0;
            return edgeShare * (1.0 - (dist - radius) / (outer - radius));
        }

        /// <summary>
        /// True when the segment a → b comes within <paramref name="r"/> of <paramref name="c"/> (20 §3 "통과 방지"):
        /// a fast dust cannot step across the capture circle without being caught.
        /// </summary>
        public static bool SegmentHitsCircle(Vector2 a, Vector2 b, Vector2 c, float r)
        {
            Vector2 ab = b - a;
            Vector2 ac = c - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-12f ? Mathf.Clamp01(Vector2.Dot(ac, ab) / len2) : 0f;
            return (a + ab * t - c).sqrMagnitude <= r * r;
        }

        /// <summary>
        /// Advances one fixed tick. Returns the mass captured this tick;
        /// <paramref name="gravity"/> is the gravity source, which is the cursor unless the follow experiment is on (20 §7).
        /// </summary>
        public double Tick(double dt, Rect area, Vector2 gravity, bool holding, in EffectiveStats s)
        {
            Area = area;
            float fdt = (float)dt;
            float drift = Px(p.driftSpeed);
            float gravR = Px(s.gravityRadius);
            float capR = Px(p.captureRadius);
            float maxSpeed = Px(p.dustMaxSpeed);
            float accel = Px(s.pullAccel);
            float outerR = gravR * (float)p.pullOuterRatio;
            float outerR2 = outerR * outerR;
            float assistR = capR * (float)p.captureAssistRatio;
            float assistR2 = assistR * assistR;
            float tangentKeep = Mathf.Pow(Mathf.Clamp01((float)p.tangentialKeepPerSec), fdt);
            float vortex = (float)p.vortexShare;
            float maxSpeed2 = maxSpeed * maxSpeed;
            float noise = (float)p.driftNoise;
            float relax = Mathf.Clamp01((float)p.driftRelaxRate * fdt);
            double captured = 0;

            for (int i = 0; i < count; i++)
            {
                ref Dust d = ref items[i];
                Vector2 toCursor = gravity - d.pos;
                float dist2 = toCursor.sqrMagnitude;

                if (holding && dist2 < outerR2)
                {
                    float dist = Mathf.Sqrt(dist2);
                    float f = (float)PullFactor(dist, gravR, p.pullFullRatio, p.pullEdgeShare, p.pullOuterRatio);
                    if (f > 0f && dist > 1e-6f)
                    {
                        // 22 §5: part of the pull is turned sideways (counter-clockwise), so the dust spirals in.
                        Vector2 inward = toCursor / dist;
                        Vector2 swirl = new Vector2(inward.y, -inward.x);
                        d.vel += (inward + swirl * vortex) * (accel * KindAccel(d.kind) * f * fdt);
                    }
                    // Near the capture circle the sideways speed is damped, so dust falls in instead of orbiting.
                    if (dist2 < assistR2 && dist > 1e-6f)
                    {
                        Vector2 dir = toCursor / dist;
                        Vector2 radial = dir * Vector2.Dot(d.vel, dir);
                        d.vel = radial + (d.vel - radial) * tangentKeep;
                    }
                    float sp2 = d.vel.sqrMagnitude;
                    if (sp2 > maxSpeed2) d.vel *= maxSpeed / Mathf.Sqrt(sp2);
                }
                else
                {
                    // Drift: small random heading change; speed relaxes toward driftSpeed.
                    float speed = d.vel.magnitude;
                    float turn = (float)(rng.NextDouble() * 2.0 - 1.0) * noise * fdt;
                    float cos = Mathf.Cos(turn), sin = Mathf.Sin(turn);
                    Vector2 dir = speed > 1e-6f ? d.vel / speed : RandomDir();
                    dir = new Vector2(dir.x * cos - dir.y * sin, dir.x * sin + dir.y * cos);
                    speed = Mathf.Lerp(speed, drift, relax);
                    d.vel = dir * speed;
                }

                Vector2 from = d.pos;
                Vector2 to = from + d.vel * fdt;
                // Capture on the swept segment, not on the end point: a fast dust cannot jump over the capture circle.
                if (holding && SegmentHitsCircle(from, to, gravity, capR))
                {
                    captured += KindMass(d.kind);
                    CapturedCount++;
                    SwapRemove(i);
                    i--;
                    continue;
                }
                d.pos = to;

                // Reflect at the visible bounds.
                if (d.pos.x < area.xMin) { d.pos.x = area.xMin; d.vel.x = Mathf.Abs(d.vel.x); }
                else if (d.pos.x > area.xMax) { d.pos.x = area.xMax; d.vel.x = -Mathf.Abs(d.vel.x); }
                if (d.pos.y < area.yMin) { d.pos.y = area.yMin; d.vel.y = Mathf.Abs(d.vel.y); }
                else if (d.pos.y > area.yMax) { d.pos.y = area.yMax; d.vel.y = -Mathf.Abs(d.vel.y); }
            }

            // Spawn. At the cap the spawn is skipped, not banked.
            spawnAcc += s.spawnRate * dt;
            int cap = (int)s.dustCap;
            while (spawnAcc >= 1.0)
            {
                spawnAcc -= 1.0;
                if (count < cap) SpawnRandom(gravity, gravR, s);
            }

            TickTrails(dt);
            CapturedMass += captured;
            return captured;
        }

        /// <summary>
        /// Removes dust <paramref name="i"/> by moving the last one into its place. The trail block moves with it, so
        /// the two arrays stay lined up (22 §5).
        /// </summary>
        void SwapRemove(int i)
        {
            int last = count - 1;
            items[i] = items[last];
            if (trailLen > 0 && i != last)
                Array.Copy(trail, last * trailLen, trail, i * trailLen, trailLen);
            count--;
        }

        /// <summary>
        /// Ending (15 §4.2): every dust falls toward <paramref name="center"/> with no radius limit; the sideways speed is
        /// damped so it falls in instead of orbiting. Dust within <paramref name="captureR"/> is absorbed. No spawn, no bounds.
        /// Fixed tick only. Returns the number absorbed.
        /// </summary>
        public int TickPull(double dt, Vector2 center, float accel, float maxSpeed, float damping, float captureR)
        {
            float fdt = (float)dt;
            float capR2 = captureR * captureR;
            float maxSpeed2 = maxSpeed * maxSpeed;
            float keep = Mathf.Exp(-damping * fdt);
            int absorbed = 0;
            for (int i = 0; i < count; i++)
            {
                ref Dust d = ref items[i];
                Vector2 to = center - d.pos;
                float dist2 = to.sqrMagnitude;
                if (dist2 < capR2)
                {
                    SwapRemove(i);
                    i--;
                    absorbed++;
                    continue;
                }
                Vector2 dir = to / Mathf.Sqrt(dist2);
                d.vel += dir * (accel * fdt);
                Vector2 radial = dir * Vector2.Dot(d.vel, dir);
                d.vel = radial + (d.vel - radial) * keep;
                float sp2 = d.vel.sqrMagnitude;
                if (sp2 > maxSpeed2) d.vel *= maxSpeed / Mathf.Sqrt(sp2);
                // Never step past the center: a fast dust lands instead of overshooting.
                Vector2 step = d.vel * fdt;
                if (step.sqrMagnitude >= dist2) d.pos = center;
                else d.pos += step;
            }
            return absorbed;
        }

        /// <summary>Removes every dust.</summary>
        public void Clear()
        {
            count = 0;
            spawnAcc = 0;
            trailClock = 0;
        }

        /// <summary>
        /// Adds a dust outside the game rules (the supernova remnant, 15 §4.3). Every random value comes from the render-only
        /// RNG so the simulation sequence of later runs is unchanged.
        /// </summary>
        public void AddFree(Vector2 pos, Vector2 vel)
        {
            if (count >= items.Length) Grow();
            items[count++] = new Dust
            {
                pos = pos,
                vel = vel,
                tint = (float)visualRng.NextDouble(),
                size = 1f + (float)((visualRng.NextDouble() * 2.0 - 1.0) * p.dustSizeJitter),
                bright = 1f + (float)((visualRng.NextDouble() * 2.0 - 1.0) * p.dustBrightJitter),
            };
        }

        /// <summary>
        /// Remnant drift (render time, not the game tick): speed decays with <paramref name="drag"/> (1/s) down to
        /// <paramref name="driftSpeed"/> (world units/s) and the heading wanders slowly. Dust bounces off the edges of
        /// <paramref name="area"/> (dust that starts outside comes back in). No spawn, no capture.
        /// </summary>
        public void TickFree(float dt, float drag, float driftSpeed, Rect area)
        {
            Area = area;
            float keep = Mathf.Exp(-drag * dt);
            float noise = (float)p.driftNoise * 0.5f;
            for (int i = 0; i < count; i++)
            {
                ref Dust d = ref items[i];
                float speed = d.vel.magnitude;
                if (speed > driftSpeed) speed = Mathf.Max(driftSpeed, speed * keep);
                float turn = (float)(visualRng.NextDouble() * 2.0 - 1.0) * noise * dt;
                Vector2 dir = speed > 1e-6f ? d.vel / Mathf.Max(d.vel.magnitude, 1e-6f) : new Vector2(1f, 0f);
                float cos = Mathf.Cos(turn), sin = Mathf.Sin(turn);
                dir = new Vector2(dir.x * cos - dir.y * sin, dir.x * sin + dir.y * cos);
                d.vel = dir * speed;
                d.pos += d.vel * dt;
                if (d.pos.x < area.xMin) { d.pos.x = area.xMin; d.vel.x = Mathf.Abs(d.vel.x); }
                else if (d.pos.x > area.xMax) { d.pos.x = area.xMax; d.vel.x = -Mathf.Abs(d.vel.x); }
                if (d.pos.y < area.yMin) { d.pos.y = area.yMin; d.vel.y = Mathf.Abs(d.vel.y); }
                else if (d.pos.y > area.yMax) { d.pos.y = area.yMax; d.vel.y = -Mathf.Abs(d.vel.y); }
            }
        }

        /// <summary>Random unit vector from the render-only RNG (remnant dust).</summary>
        public Vector2 VisualDir()
        {
            float a = (float)(visualRng.NextDouble() * Math.PI * 2.0);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        /// <summary>0..1 from the render-only RNG.</summary>
        public float VisualValue() => (float)visualRng.NextDouble();

        /// <summary>Puts n dust back around the cursor with outward velocity (release below tier 1).</summary>
        public void Scatter(int n, Vector2 center)
        {
            float capR = Px(p.captureRadius);
            float speed = Px(p.scatterSpeedPx);
            for (int i = 0; i < n; i++)
            {
                Vector2 dir = RandomDir();
                float r = capR * (0.3f + 0.7f * (float)rng.NextDouble());
                Add(center + dir * r, dir * (speed * (0.6f + 0.4f * (float)rng.NextDouble())));
            }
        }

        /// <summary>Mass of a dust of that kind (20 §4); the base dust is 1.</summary>
        public double KindMass(int kind) => kind >= 0 && kind < kindMass.Length ? kindMass[kind] : 1.0;

        /// <summary>Pull acceleration of that kind relative to the base dust: heavier dust is drawn in more slowly.</summary>
        public float KindAccel(int kind) => kind >= 0 && kind < kindAccel.Length ? kindAccel[kind] : 1f;

        /// <summary>
        /// One roll over the effective chances picks the kind of a new dust (20 §4). The shares do not overlap and
        /// whatever they leave is base dust, so a locked kind can never come out.
        /// </summary>
        int RollKind(in EffectiveStats s)
        {
            var chance = s.dustKindChance;
            if (chance == null || chance.Length < 2) return 0;
            double u = rng.NextDouble();
            double acc = 0;
            for (int k = 1; k < chance.Length; k++)
            {
                acc += chance[k];
                if (u < acc) return k;
            }
            return 0;
        }

        /// <summary>Random position inside the area, outside gravityRadius of the cursor (best effort).</summary>
        void SpawnRandom(Vector2 cursor, float gravR, in EffectiveStats s)
        {
            Rect a = Area;
            float gravR2 = gravR * gravR;
            Vector2 pos = default;
            for (int tries = 0; tries < 16; tries++)
            {
                pos = new Vector2(a.xMin + (float)rng.NextDouble() * a.width, a.yMin + (float)rng.NextDouble() * a.height);
                if ((pos - cursor).sqrMagnitude >= gravR2) break;
            }
            Add(pos, RandomDir() * Px(p.driftSpeed), RollKind(s));
        }

        void Add(Vector2 pos, Vector2 vel, int kind = 0)
        {
            if (count >= items.Length) Grow();
            items[count++] = new Dust
            {
                pos = pos,
                vel = vel,
                kind = kind,
                tint = (float)rng.NextDouble(),
                size = 1f + (float)((visualRng.NextDouble() * 2.0 - 1.0) * p.dustSizeJitter),
                bright = 1f + (float)((visualRng.NextDouble() * 2.0 - 1.0) * p.dustBrightJitter),
            };
        }

        /// <summary>Doubles both arrays together so a dust and its trail block keep the same index (22 §5).</summary>
        void Grow()
        {
            Array.Resize(ref items, items.Length * 2);
            EnsureTrail();
        }

        Vector2 RandomDir()
        {
            float a = (float)(rng.NextDouble() * Math.PI * 2.0);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        float Px(double px) => (float)(px / p.pixelsPerUnit);
    }
}
