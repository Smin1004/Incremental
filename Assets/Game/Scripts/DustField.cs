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
    }

    /// <summary>
    /// Dust simulation. One struct array, no GameObjects. Advanced only from GameRoot's fixed tick.
    /// Positions and speeds are in world units (px / pixelsPerUnit).
    /// </summary>
    public sealed class DustField : MonoBehaviour
    {
        Dust[] items = new Dust[512];
        int count;
        double spawnAcc;
        System.Random rng = new System.Random();
        // Render-only variation draws from its own RNG so the simulation sequence is the same as before (bot measurements).
        System.Random visualRng = new System.Random();
        GameParams p;

        public Dust[] Items => items;
        public int Count => count;
        /// <summary>Visible area the dust is confined to (camera rect, world units).</summary>
        public Rect Area { get; private set; }

        public void Init(GameParams gameParams) { p = gameParams; }

        public void Reseed(int seed) { rng = new System.Random(seed); }

        /// <summary>Clears the field and places the initial dust outside the (effective) gravity radius of the cursor.</summary>
        public void Reset(int initial, Rect area, Vector2 cursor, in EffectiveStats s)
        {
            count = 0;
            spawnAcc = 0;
            Area = area;
            float gravR = Px(s.gravityRadius);
            for (int i = 0; i < initial; i++) SpawnRandom(cursor, gravR);
        }

        /// <summary>Advances one fixed tick. Returns the number of dust captured this tick.</summary>
        public int Tick(double dt, Rect area, Vector2 cursor, bool holding, in EffectiveStats s)
        {
            Area = area;
            float fdt = (float)dt;
            float drift = Px(p.driftSpeed);
            float gravR = Px(s.gravityRadius);
            float capR = Px(p.captureRadius);
            float maxSpeed = Px(p.dustMaxSpeed);
            float accel = Px(s.pullAccel);
            float gravR2 = gravR * gravR;
            float capR2 = capR * capR;
            float maxSpeed2 = maxSpeed * maxSpeed;
            float noise = (float)p.driftNoise;
            float relax = Mathf.Clamp01((float)p.driftRelaxRate * fdt);
            int captured = 0;

            for (int i = 0; i < count; i++)
            {
                ref Dust d = ref items[i];
                Vector2 toCursor = cursor - d.pos;
                float dist2 = toCursor.sqrMagnitude;

                if (holding && dist2 < gravR2)
                {
                    if (dist2 < capR2)
                    {
                        items[i] = items[count - 1];
                        count--;
                        i--;
                        captured++;
                        continue;
                    }
                    float dist = Mathf.Sqrt(dist2);
                    d.vel += toCursor * (accel * fdt / dist);
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

                d.pos += d.vel * fdt;

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
                if (count < cap) SpawnRandom(cursor, gravR);
            }

            return captured;
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
                    items[i] = items[count - 1];
                    count--;
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
        }

        /// <summary>
        /// Adds a dust outside the game rules (the supernova remnant, 15 §4.3). Every random value comes from the render-only
        /// RNG so the simulation sequence of later runs is unchanged.
        /// </summary>
        public void AddFree(Vector2 pos, Vector2 vel)
        {
            if (count >= items.Length) Array.Resize(ref items, items.Length * 2);
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

        /// <summary>Random position inside the area, outside gravityRadius of the cursor (best effort).</summary>
        void SpawnRandom(Vector2 cursor, float gravR)
        {
            Rect a = Area;
            float gravR2 = gravR * gravR;
            Vector2 pos = default;
            for (int tries = 0; tries < 16; tries++)
            {
                pos = new Vector2(a.xMin + (float)rng.NextDouble() * a.width, a.yMin + (float)rng.NextDouble() * a.height);
                if ((pos - cursor).sqrMagnitude >= gravR2) break;
            }
            Add(pos, RandomDir() * Px(p.driftSpeed));
        }

        void Add(Vector2 pos, Vector2 vel)
        {
            if (count >= items.Length) Array.Resize(ref items, items.Length * 2);
            items[count++] = new Dust
            {
                pos = pos,
                vel = vel,
                tint = (float)rng.NextDouble(),
                size = 1f + (float)((visualRng.NextDouble() * 2.0 - 1.0) * p.dustSizeJitter),
                bright = 1f + (float)((visualRng.NextDouble() * 2.0 - 1.0) * p.dustBrightJitter),
            };
        }

        Vector2 RandomDir()
        {
            float a = (float)(rng.NextDouble() * Math.PI * 2.0);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        float Px(double px) => (float)(px / p.pixelsPerUnit);
    }
}
