using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// A straight crossing of the screen that passes the gravity source at a chosen distance: the path of a meteor
    /// (24 §5) and, since the 15th pass, of the S3 comet (29 §10.7). Pulled out of MeteorAbility unchanged.
    /// </summary>
    public static class CrossingPath
    {
        /// <summary>
        /// A straight line across the screen. Each try takes a direction (a random one, or <paramref name="heading"/>) and
        /// puts the line at a random distance between <paramref name="minRadii"/> and <paramref name="maxRadii"/> gravity
        /// radii to one side of the gravity source; the first one whose nearest point to the gravity source is on screen is
        /// taken. When none is (the gravity source sits in a corner), the last try that crossed the screen at all is kept,
        /// and when there is none of those either, the line goes through the middle. Start and end lie
        /// <paramref name="margin"/> outside the screen.
        /// </summary>
        public static void Pick(System.Random rng, Rect area, float margin, Vector2 gravity, float gravityRadius,
            double minRadii, double maxRadii, int tries, bool fixedHeading, double heading, out Vector2 start, out Vector2 end)
        {
            Rect outer = Expand(area, margin);
            bool crossed = false;
            Vector2 u = Vector2.right;
            start = end = area.center;
            for (int i = 0; i < Math.Max(1, tries); i++)
            {
                double angle = fixedHeading ? heading : rng.NextDouble() * Math.PI * 2.0;
                u = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                var side = new Vector2(-u.y, u.x);
                double radii = minRadii + (maxRadii - minRadii) * rng.NextDouble();
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

        public static Rect Expand(Rect r, float by) => new Rect(r.xMin - by, r.yMin - by, r.width + by * 2f, r.height + by * 2f);

        /// <summary>Where the line point + u × t is inside the rect: from t0 to t1. False when it misses the rect.</summary>
        public static bool Clip(Vector2 point, Vector2 u, Rect r, out float t0, out float t1)
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

        /// <summary>Seconds of a crossing from <paramref name="start"/> to <paramref name="end"/> in <paramref name="duration"/> that are on screen.</summary>
        public static double OnScreenSec(Vector2 start, Vector2 end, Rect area, double duration)
        {
            Vector2 path = end - start;
            float length = Mathf.Max(1e-4f, path.magnitude);
            return Clip(start, path / length, area, out float t0, out float t1)
                ? (Mathf.Clamp(t1, 0f, length) - Mathf.Clamp(t0, 0f, length)) / length * duration
                : 0.0;
        }
    }
}
