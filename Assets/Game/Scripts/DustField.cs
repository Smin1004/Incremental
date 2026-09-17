using System;
using UnityEngine;

namespace Incremental
{
    public struct Dust
    {
        public Vector2 pos;
        public Vector2 vel;
        public Color32 color;
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
        GameParams p;

        public Dust[] Items => items;
        public int Count => count;
        /// <summary>Visible area the dust is confined to (camera rect, world units).</summary>
        public Rect Area { get; private set; }

        public void Init(GameParams gameParams) { p = gameParams; }

        public void Reseed(int seed) { rng = new System.Random(seed); }

        public void Reset(int initial, Rect area, Vector2 cursor)
        {
            count = 0;
            spawnAcc = 0;
            Area = area;
            float gravR = Px(p.gravityRadius);
            for (int i = 0; i < initial; i++) SpawnRandom(cursor, gravR);
        }

        /// <summary>Advances one fixed tick. Returns the number of dust captured this tick.</summary>
        public int Tick(double dt, Rect area, Vector2 cursor, bool holding, in EffectiveStats s)
        {
            Area = area;
            float fdt = (float)dt;
            float drift = Px(p.driftSpeed);
            float gravR = Px(p.gravityRadius);
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
                color = Color32.Lerp(p.dustColorA, p.dustColorB, (float)rng.NextDouble()),
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
