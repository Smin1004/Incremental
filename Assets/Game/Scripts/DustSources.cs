using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// 29 §10.7, 15th pass: the three dust sources that are rule nodes. S1 먼지 무리 (a clump to chase), S2 잔해 (a
    /// burst when a body is completed) and S3 혜성 (a slow comet that sheds dust). GameRoot calls <see cref="Tick"/> on the
    /// fixed tick before the dust tick and <see cref="OnBody"/> when a body is completed; the drawing is in Update. None of
    /// them passes the dust cap. Every number is GameParams.rules; the looks follow the effect intensity.
    ///
    /// The comet is drawn so it cannot be taken for a meteor: a meteor is fast, hot and has a short fire tail; the comet is
    /// slow, cold, with a big faint coma and two long tails, a warm dust tail that bends away and a straight blue ion tail.
    /// Its path is picked as the meteor's is (CrossingPath).
    /// </summary>
    public sealed class DustSources : MonoBehaviour
    {
        struct Cluster
        {
            public double age;
            public Vector2 at;
        }

        struct Burst
        {
            public float age;
            public Vector2 at;
        }

        struct Comet
        {
            public bool active;
            public double age, duration, leaveSec;
            public Vector2 start, end, pos, dir;
            public float speed;
            public double shedRate, shedAcc;
            public int shedCount, shed, notShed;
            /// <summary>Which side the dust tail bends to (±1).</summary>
            public float bend;
        }

        GameRoot root;
        GameParams p;
        /// <summary>Paths, places and shed positions. Apart from the dust and visual RNGs, like the meteor's.</summary>
        readonly System.Random rng = new System.Random();
        readonly Cluster[] clusters = new Cluster[8];
        int nextCluster;
        readonly Burst[] bursts = new Burst[16];
        int nextBurst;
        Comet comet;
        Vector2[] points = new Vector2[24];

        RuleParams Q => p.rules;

        /// <summary>Seconds since the newest clump / burst appeared, or −1 (captures). The comet's age, or −1.</summary>
        public float ClusterAge { get; private set; } = -1f;
        public float BurstAge { get; private set; } = -1f;
        public float CometAge => comet.active ? (float)comet.age : -1f;
        /// <summary>Where the comet's head is (captures).</summary>
        public Vector2 CometPos => comet.pos;

        public void Init(GameRoot gameRoot)
        {
            root = gameRoot;
            p = root.gameParams;
            Clear();
        }

        /// <summary>Run start and end: nothing in flight.</summary>
        public void Clear()
        {
            for (int i = 0; i < clusters.Length; i++) clusters[i].age = -1;
            for (int i = 0; i < bursts.Length; i++) bursts[i].age = -1f;
            comet.active = false;
            ClusterAge = BurstAge = -1f;
        }

        float Px(double px) => (float)(px / p.pixelsPerUnit);

        /// <summary>Fixed tick, before the dust tick: S1 clumps and the S3 comet.</summary>
        public void Tick(double dt, in EffectiveStats s, RunState run)
        {
            var rules = root.Rules;
            if (rules.ClusterDue(run)) SpawnCluster(s, run);
            if (rules.CometDue(run) && !comet.active) LaunchComet(s, run);
            for (int i = 0; i < clusters.Length; i++)
                if (clusters[i].age >= 0) clusters[i].age += dt;
            if (comet.active) StepComet(dt, s);
        }

        /// <summary>Captures and checks: a clump / a comet now, whatever the rule levels say (run screen only).</summary>
        public void DebugCluster()
        {
            if (root.Phase == GamePhase.Run && root.Run != null) SpawnCluster(root.Effective, root.Run);
        }

        public void DebugComet()
        {
            if (root.Phase == GamePhase.Run && root.Run != null && !comet.active) LaunchComet(root.Effective, root.Run);
        }

        // ---------------- S1 먼지 무리 ----------------

        void SpawnCluster(in EffectiveStats s, RunState run)
        {
            Rect area = root.CameraArea();
            Vector2 gravity = root.GravityWorld;
            float radius = Px(Q.clusterRadiusPx);
            float keepOut = (float)(s.gravityRadius / p.pixelsPerUnit * Q.clusterMinRadii);
            // Inside the screen with the whole clump, and far enough from the gravity source (best effort, as the spawn).
            Rect inner = new Rect(area.xMin + radius, area.yMin + radius, Mathf.Max(0f, area.width - radius * 2f), Mathf.Max(0f, area.height - radius * 2f));
            Vector2 at = inner.center;
            float best = -1f;
            for (int tries = 0; tries < 24; tries++)
            {
                var c = new Vector2(inner.xMin + (float)rng.NextDouble() * inner.width, inner.yMin + (float)rng.NextDouble() * inner.height);
                float d = (c - gravity).magnitude;
                if (d >= keepOut)
                {
                    at = c;
                    break;
                }
                // Nowhere far enough (a huge radius on a small screen): the farthest place tried.
                if (d > best)
                {
                    best = d;
                    at = c;
                }
            }
            int added = root.Dust.SpawnCluster(at, Mathf.Max(0, Q.clusterCount), radius, Px(Q.clusterSpreadPx), (float)Q.clusterSpreadSec, s);
            clusters[nextCluster] = new Cluster { age = 0, at = at };
            nextCluster = (nextCluster + 1) % clusters.Length;
            ClusterAge = 0f;
            root.Rules.LogCluster(run, added, at * (float)p.pixelsPerUnit);
        }

        // ---------------- S2 잔해 ----------------

        /// <summary>
        /// A body (or a bundle) of <paramref name="mass"/> was completed at <paramref name="at"/>: S2 throws a share of it
        /// back out as new dust. The income is not touched.
        /// </summary>
        public void OnBody(Vector2 at, double mass, double bodyPx, in EffectiveStats s)
        {
            double share = root.Rules.DebrisShare;
            if (!(share > 0) || !(mass > 0)) return;
            double per = s.dustMassAvg > 0 ? s.dustMassAvg : Math.Max(1e-9, s.dustMass);
            int n = (int)Math.Min(Math.Max(0, Q.debrisMax), Math.Round(mass * share / per));
            if (n <= 0) return;
            // From just outside the capture circle and the body, so the burst is not caught on the tick it is made.
            float start = Mathf.Max(Px(p.captureRadius) * 1.6f, Px(bodyPx) * 0.5f);
            int added = root.Dust.SpawnBurst(at, n, start, Px(Q.debrisSpeedPx), s);
            if (added <= 0) return;
            bursts[nextBurst] = new Burst { age = 0f, at = at };
            nextBurst = (nextBurst + 1) % bursts.Length;
            BurstAge = 0f;
            root.Rules.OnDebris(added, mass);
        }

        // ---------------- S3 혜성 ----------------

        void LaunchComet(in EffectiveStats s, RunState run)
        {
            Rect area = root.CameraArea();
            Vector2 gravity = root.GravityWorld;
            float gravR = (float)(s.gravityRadius / p.pixelsPerUnit);
            float margin = Px(root.RunBodyPx(Q.cometComaPx)) * 0.5f;
            CrossingPath.Pick(rng, area, margin, gravity, gravR, Q.cometPassMinRadii, Q.cometPassMaxRadii, 16, false, 0, out comet.start, out comet.end);
            Vector2 path = comet.end - comet.start;
            float length = Mathf.Max(1e-4f, path.magnitude);
            comet.dir = path / length;
            comet.pos = comet.start;
            comet.duration = Math.Max(0.1, Q.cometCrossSec);
            comet.speed = (float)(length / comet.duration);
            // After the head has gone, the comet keeps going until its longest tail is off screen too.
            float tail = Px(Math.Max(Q.cometIonTailPx, Q.cometDustTailPx));
            comet.leaveSec = comet.speed > 1e-6f ? tail / comet.speed : 0;
            comet.shedCount = (int)Math.Round(Math.Max(0.0, Q.cometDustShare) * s.dustCap);
            double onScreen = CrossingPath.OnScreenSec(comet.start, comet.end, area, comet.duration);
            comet.shedRate = onScreen > 0 ? comet.shedCount / onScreen : 0;
            comet.shedAcc = 0;
            comet.shed = comet.notShed = 0;
            comet.age = 0;
            comet.bend = rng.Next(2) == 0 ? 1f : -1f;
            comet.active = true;
            Vector2 nearest = comet.start + comet.dir * Vector2.Dot(gravity - comet.start, comet.dir);
            float passPx = (nearest - gravity).magnitude * (float)p.pixelsPerUnit;
            root.Rules.LogComet(run, comet.shedCount, passPx, gravR > 0 ? passPx / (float)s.gravityRadius : 0f);
        }

        void StepComet(double dt, in EffectiveStats s)
        {
            Rect area = root.CameraArea();
            Vector2 from = comet.pos;
            comet.pos += comet.dir * (comet.speed * (float)dt);
            bool onScreen = area.Contains(comet.pos);
            // Shed evenly along the on-screen part of the path, as the meteor does.
            if (onScreen && comet.shed + comet.notShed < comet.shedCount)
            {
                comet.shedAcc += comet.shedRate * dt;
                var side = new Vector2(-comet.dir.y, comet.dir.x);
                Vector2 vel = comet.dir * (comet.speed * (float)Q.cometDustSpeedShare);
                float spread = Px(Q.cometShedSpreadPx);
                while (comet.shedAcc >= 1.0 && comet.shed + comet.notShed < comet.shedCount)
                {
                    comet.shedAcc -= 1.0;
                    Vector2 at = Vector2.LerpUnclamped(from, comet.pos, (float)rng.NextDouble()) + side * (spread * (float)(rng.NextDouble() * 2.0 - 1.0));
                    if (root.Dust.Shed(at, vel, s, 1.0)) comet.shed++;
                    else comet.notShed++;
                }
            }
            comet.age += dt;
            if (comet.age < comet.duration + comet.leaveSec) return;
            comet.active = false;
            root.Rules.LogCometGone(comet.shed, comet.notShed);
        }

        // ---------------- drawing ----------------

        void Update()
        {
            bool run = root.Phase == GamePhase.Run && root.Run != null;
            float intensity = root.EffectIntensity;
            float dt = Time.deltaTime;
            if (ClusterAge >= 0f) ClusterAge += dt;
            if (BurstAge >= 0f) BurstAge += dt;
            if (!run) return;
            // The comet's core stays at intensity 0, as the meteor's body does; the glows and the tails go.
            if (comet.active) DrawComet(intensity);
            if (intensity <= 0f) return;
            DrawClusters(intensity);
            DrawBursts(dt, intensity);
        }

        void DrawClusters(float intensity)
        {
            float sec = (float)Q.clusterGlowSec;
            if (sec <= 0f || root.Glow == null) return;
            for (int i = 0; i < clusters.Length; i++)
            {
                float age = (float)clusters[i].age;
                if (age < 0f || age >= sec) continue;
                float k = age / sec;
                // Up fast, down slowly, widening with the clump.
                float a = Mathf.Min(1f, k * 6f) * (1f - k) * (1f - k);
                float d = Px(Q.clusterGlowPx + Q.clusterSpreadPx * 2.0 * age / Math.Max(0.1, Q.clusterSpreadSec));
                var c = Q.clusterGlowColor.linear;
                root.Glow.Draw(clusters[i].at, d, new Color(c.r, c.g, c.b, (float)Q.clusterGlowStrength * a * intensity), 0f, 0.55f, 0.6f);
            }
        }

        void DrawBursts(float dt, float intensity)
        {
            float sec = (float)Q.debrisFlashSec;
            for (int i = 0; i < bursts.Length; i++)
            {
                if (bursts[i].age < 0f) continue;
                bursts[i].age += dt;
                float k = bursts[i].age / Mathf.Max(0.02f, sec);
                if (k >= 1f)
                {
                    bursts[i].age = -1f;
                    continue;
                }
                if (root.Glow == null) continue;
                // A ring that opens with the dust and fades.
                float d = Px(Q.debrisFlashPx) * (0.4f + 0.6f * k);
                var c = Q.clusterGlowColor.linear;
                root.Glow.Draw(bursts[i].at, d, new Color(c.r, c.g, c.b, (float)Q.debrisFlashStrength * (1f - k) * (1f - k) * intensity), 0.7f, 0.08f, 0.3f * (1f - k));
            }
        }

        void DrawComet(float intensity)
        {
            float lead = root.Abilities != null ? root.Abilities.Lead : 0f;
            Vector2 head = comet.pos + comet.dir * (comet.speed * lead);
            Vector2 back = -comet.dir;
            Vector2 side = new Vector2(-comet.dir.y, comet.dir.x) * comet.bend;
            float t = Time.time;
            root.Glow.Draw(head, Px(root.RunBodyPx(Q.cometCorePx)), new Color(1f, 1f, 1f, (float)Q.cometCoreStrength * Mathf.Max(0.5f, intensity)), 0f, 0.3f, 1f);
            if (intensity <= 0f) return;

            // The ion tail first (under), straight back, narrow and blue, a slight shimmer along it.
            float ionL = Px(Q.cometIonTailPx), ionW = Px(Q.cometIonTailWidthPx);
            var ion = Q.cometIonTailColor.linear;
            float ionB = (float)Q.cometIonTailBright * intensity;
            int n = points.Length;
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1);
                points[i] = head + back * (ionL * u) + side * (ionW * 0.15f * Mathf.Sin(u * 9f - t * 3f) * u);
            }
            root.Lines.Ribbon(points, n,
                i => { float u = i / (float)(n - 1); return ionW * (0.5f + 0.7f * u); },
                i => { float u = i / (float)(n - 1); float b = ionB * Mathf.Pow(1f - u, 1.3f) * (0.85f + 0.15f * Mathf.Sin(u * 23f - t * 7f)); return new Color(ion.r * b, ion.g * b, ion.b * b); });

            // The dust tail, warm, fanning out and bending away to one side.
            float dustL = Px(Q.cometDustTailPx), dustW = Px(Q.cometDustTailWidthPx);
            var warm = Q.cometDustTailColor.linear;
            float dustB = (float)Q.cometDustTailBright * intensity;
            float curve = (float)Q.cometDustTailCurve;
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1);
                points[i] = head + back * (dustL * u) + side * (dustL * curve * u * u);
            }
            root.Lines.Ribbon(points, n,
                i => { float u = i / (float)(n - 1); return dustW * (0.35f + 0.9f * Mathf.Sqrt(u)); },
                i => { float u = i / (float)(n - 1); float b = dustB * Mathf.Pow(1f - u, 1.6f); return new Color(warm.r * b, warm.g * b, warm.b * b); });

            // The coma: big and faint, round a small bright core.
            var coma = Q.cometComaColor.linear;
            float comaD = Px(root.RunBodyPx(Q.cometComaPx));
            float breathe = 1f + 0.06f * Mathf.Sin(t * 1.7f);
            root.Glow.Draw(head, comaD * breathe, new Color(coma.r, coma.g, coma.b, (float)Q.cometComaStrength * intensity), 0f, 0.5f, 0.5f);
        }
    }
}
