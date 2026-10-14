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
        /// <summary>
        /// 24 §8: 0..1, how excited the dust is. An ability that hits it sets it to 1 and it calms down over
        /// <see cref="DustField.ExciteSec"/>; the renderer draws an excited dust brighter. Render only.
        /// </summary>
        public float excite;
        /// <summary>
        /// 0..1: how far a newly created dust has faded in. It starts at 0 and reaches 1 over GameParams.dustSpawnFadeSec,
        /// so a dust that is created in plain view grows in instead of popping in. Render only.
        /// </summary>
        public float born;
    }

    /// <summary>
    /// 24 §4, §5: a force an ability puts on the dust for one tick, around a point that moves (a satellite, a meteor).
    /// A dust farther away than <see cref="radius"/> costs one distance test and nothing else, which is what keeps
    /// four satellites from multiplying the dust tick (24 §4). Inside the reach the force falls off the way the gravity
    /// pull does (20 §3), and heavier dust answers it more slowly, as it does gravity. World units.
    /// </summary>
    public struct DustForce
    {
        public Vector2 at;
        public float radius;
        /// <summary>The square of <see cref="radius"/>: what the distance test compares against.</summary>
        public float radius2;
        /// <summary>Acceleration at the point itself, world units per second squared.</summary>
        public float accel;
        /// <summary>Zero: the dust is pulled toward <see cref="at"/>. Else the unit direction it is pushed along.</summary>
        public Vector2 dir;
        /// <summary>The dust in reach is excited (24 §8).</summary>
        public bool excite;
        /// <summary>
        /// 24 §18.1 the tow: inside <see cref="towRadius2"/> a dust's velocity is drawn toward <see cref="vel"/> at
        /// this rate per second (0 = no tow). A satellite's core, so that what it gathered comes round with it.
        /// </summary>
        public float tow;
        public float towRadius2;
        public Vector2 vel;
        /// <summary>
        /// 24 §19.1: the sideways share of the pull (counter-clockwise), as GameParams.vortexShare is for the gravity
        /// source, capped the same way by vortexTerminalPx. 0 = a straight pull.
        /// </summary>
        public float vortex;
        /// <summary>
        /// 10차 회신: while holding, a dust closer to the gravity source than this (squared, world) is left alone by this
        /// force and held to the field's ordinary top speed: the gravity source owns it. 0 = the force acts everywhere.
        /// </summary>
        public float yieldRadius2;

        /// <summary>A pull toward a point, which also excites the dust: a satellite (24 §4, §16.2).</summary>
        public static DustForce Pull(Vector2 at, float radius, float accel) =>
            new DustForce { at = at, radius = radius, radius2 = radius * radius, accel = accel, excite = true };

        /// <summary>
        /// 24 §18.1: the pull of a satellite with its core: inside <paramref name="towRadius"/> the dust is also towed
        /// toward the satellite's velocity <paramref name="velocity"/> at <paramref name="towRate"/> per second.
        /// </summary>
        public static DustForce PullTow(Vector2 at, float radius, float accel, float towRadius, float towRate, Vector2 velocity) =>
            new DustForce
            {
                at = at, radius = radius, radius2 = radius * radius, accel = accel, excite = true,
                tow = towRate, towRadius2 = towRadius * towRadius, vel = velocity,
            };

        /// <summary>
        /// 24 §19.1: the black hole's pull over everything dust can be in, with a swirl: a pull like a satellite's,
        /// plus <paramref name="vortex"/> of it turned sideways.
        /// </summary>
        public static DustForce Global(Vector2 at, float radius, float accel, float vortex) =>
            new DustForce { at = at, radius = radius, radius2 = radius * radius, accel = accel, excite = true, vortex = vortex };

        /// <summary>A push along a direction, which also excites the dust: a meteor's wake (24 §5).</summary>
        public static DustForce Drag(Vector2 at, float radius, float accel, Vector2 dir) =>
            new DustForce { at = at, radius = radius, radius2 = radius * radius, accel = accel, dir = dir, excite = true };
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
        double marginAcc;
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
        /// 22 §5: how far the field is into the current trail sample, 0..1. The renderer needs it to age the ribbon
        /// continuously: without it the tail would step by a whole sample every trailSampleSec and blink.
        /// </summary>
        public float TrailPhase => trailLen <= 0 ? 0f : Mathf.Clamp01((float)(trailClock / Math.Max(1e-4, p.trailSampleSec)));

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
        /// <summary>
        /// The visible area (camera rect, world units). A dust that is in it stays in it; see <see cref="Bounds"/> for
        /// what lies beyond.
        /// </summary>
        public Rect Area { get; private set; }
        /// <summary>
        /// Everything a dust may be in: the visible area and, around it, a band GameParams.dustMarginPx wide that is
        /// out of sight. The band holds a thin, even layer of dust for whatever reaches past the screen to pull in:
        /// the gravity source at an edge, 수축 at its last level, a satellite far out, a meteor's wake. Dust goes
        /// from the band to the screen and never the other way.
        /// </summary>
        public Rect Bounds { get; private set; }
        /// <summary>Dust outside the visible area as of the last tick. It counts toward the dust cap like any other.</summary>
        public int OutsideCount { get; private set; }
        /// <summary>10/8: the spawn rate multiplier of the last tick: 1 at the dust cap, up to 1 + GameParams.dustRefillBoost on an empty screen.</summary>
        public double SpawnBoost { get; private set; } = 1.0;
        /// <summary>
        /// 24 §8: seconds an excited dust takes to calm down. The ability system hands it over from the AbilityTable every
        /// tick; at 0, which is what it is when there are no abilities, a dust calms down at once.
        /// </summary>
        public double ExciteSec { get; set; }
        /// <summary>
        /// 24 §16.1: while a dust is excited, how fast it settles back to its drifting speed is multiplied by this, so
        /// a dust an ability pushed keeps going. Handed over with <see cref="ExciteSec"/>; 1 changes nothing.
        /// </summary>
        public double ExciteDriftRelaxMult { get; set; } = 1.0;

        /// <summary>
        /// 24 §18.1: while a dust is excited its top speed is dustMaxSpeed x this, so what a satellite tows keeps up
        /// with it. Handed over with <see cref="ExciteSec"/>; 1 changes nothing.
        /// </summary>
        public double ExciteSpeedCapMult { get; set; } = 1.0;

        /// <summary>
        /// 24 §20.2: the dust's top speed is GameParams.dustMaxSpeed x this (the amplifier crystal's buff). The ability
        /// system hands it over every tick; 1 changes nothing.
        /// </summary>
        public double MaxSpeedMult { get; set; } = 1.0;

        /// <summary>
        /// 24 §19.1 the inhale: every dust's velocity is multiplied by this once per tick while it is below 1 (the
        /// black hole sets it for the ticks of its inhale and puts it back to 1). Render has nothing to do with it.
        /// </summary>
        public float SpeedMul { get; set; } = 1f;

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

        /// <summary>
        /// Keeps the trail buffer as long as the dust array. It is resized, never replaced: the dust that are already
        /// there keep the positions their ribbons are drawn from. Replacing it left every one of those positions at
        /// (0, 0) while the dust still counted them, so at the moment the array grew past 512, 1024, 2048 or 4096 dust
        /// every ribbon on screen drew a line to the middle of the screen until its buffer had filled up again.
        /// </summary>
        void EnsureTrail()
        {
            int need = items.Length * trailLen;
            if (trail.Length != need) Array.Resize(ref trail, need);
        }

        /// <summary>
        /// A new dust has no path yet: every slot of its trail block is where it is now, so whatever reads the block
        /// finds a ribbon of length 0 that can only grow from there.
        /// </summary>
        void ClearTrail(int index, Vector2 pos)
        {
            int at = index * trailLen;
            for (int j = 0; j < trailLen; j++) trail[at + j] = pos;
        }

        /// <summary>How much of its fade-in a new dust gets through in <paramref name="fdt"/> seconds (GameParams.dustSpawnFadeSec).</summary>
        float BornStep(float fdt) => p.dustSpawnFadeSec > 0 ? (float)(fdt / p.dustSpawnFadeSec) : 1f;

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

        /// <summary>
        /// Clears the field and places the initial dust, outside GameParams.initialDustClearShare of the (effective) gravity
        /// radius around the gravity source (0 = evenly over the area). The band
        /// outside the screen starts as full as the screen does: its share of the cap, scaled by how much of the cap
        /// <paramref name="initial"/> is.
        /// </summary>
        public void Reset(int initial, Rect area, Vector2 cursor, in EffectiveStats s)
        {
            count = 0;
            spawnAcc = 0;
            marginAcc = 0;
            trailClock = 0;
            CapturedCount = 0;
            CapturedMass = 0;
            Area = area;
            Bounds = BoundsOf(area);
            float gravR = Px(s.gravityRadius);
            float margin = Margin;
            int cap = (int)s.dustCap;
            int outside = 0;
            // A field asked to start above its cap is a test or a tool filling it by hand: it gets what it asked for.
            if (margin > 0f && cap > 0 && initial <= cap)
            {
                outside = (int)Math.Round(MarginBudget(cap) * (initial / (double)cap));
                // No room for both under the cap (a scene staged at the cap): the band's part comes out of the screen's.
                if (initial + outside > cap) initial = cap - outside;
            }
            // 24 §23.1.1: the starting dust keeps out of initialDustClearShare of the radius (0 = evenly everywhere).
            float clearR = gravR * (float)Math.Max(0.0, p.initialDustClearShare);
            // The screen first, so the dust on it is where the same seed put it before there was a band.
            for (int i = 0; i < initial; i++) SpawnRandom(cursor, clearR, s);
            for (int i = 0; i < outside; i++) SpawnOutside(cursor, clearR, margin, s);
            OutsideCount = outside;
        }

        /// <summary>Width of the band outside the screen, world units (GameParams.dustMarginPx). 0 = there is none.</summary>
        float Margin => p.dustMarginPx > 0 ? Px(p.dustMarginPx) : 0f;

        /// <summary>The most dust the band is filled with: its share of the dust cap (GameParams.dustMarginCapShare).</summary>
        int MarginBudget(int cap) => (int)(cap * Math.Min(1.0, Math.Max(0.0, p.dustMarginCapShare)));

        /// <summary><see cref="Bounds"/> for a visible area: that area and the band around it.</summary>
        public Rect BoundsOf(Rect area)
        {
            float m = Margin;
            return new Rect(area.xMin - m, area.yMin - m, area.width + m * 2f, area.height + m * 2f);
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
        /// The first <paramref name="forceCount"/> of <paramref name="forces"/> are what the abilities add this tick
        /// (24): they act whether or not the player is holding, and they never capture.
        /// </summary>
        public double Tick(double dt, Rect area, Vector2 gravity, bool holding, in EffectiveStats s,
            DustForce[] forces = null, int forceCount = 0)
        {
            Area = area;
            // The band outside the screen, and the speed above which a dust of the band comes in over the edge.
            float margin = Margin;
            bool banded = margin > 0f;
            Rect outer = BoundsOf(area);
            Bounds = outer;
            float passSpeed = Px(p.dustEdgePassSpeedPx);
            float passSpeed2 = passSpeed * passSpeed;
            int outside = 0;
            float fdt = (float)dt;
            float drift = Px(p.driftSpeed);
            float gravR = Px(s.gravityRadius);
            float capR = Px(p.captureRadius);
            float maxSpeed = Px(p.dustMaxSpeed) * (float)Math.Max(0.01, MaxSpeedMult);
            float accel = Px(s.pullAccel);
            float outerR = gravR * (float)p.pullOuterRatio;
            float outerR2 = outerR * outerR;
            float assistR = capR * (float)p.captureAssistRatio;
            float assistR2 = assistR * assistR;
            float tangentKeep = Mathf.Pow(Mathf.Clamp01((float)p.tangentialKeepPerSec), fdt);
            float vortex = (float)p.vortexShare;
            float vortexTerm = Px(p.vortexTerminalPx);
            float maxSpeed2 = maxSpeed * maxSpeed;
            // 24 §18.1: an excited dust may go this much faster, so a tow or a push is not capped at the drift field's top speed.
            float maxSpeedExcited = maxSpeed * (float)Math.Max(1.0, ExciteSpeedCapMult);
            float maxSpeedExcited2 = maxSpeedExcited * maxSpeedExcited;
            float noise = (float)p.driftNoise;
            float relax = Mathf.Clamp01((float)p.driftRelaxRate * fdt);
            float relaxExcited = Mathf.Clamp01(relax * (float)Math.Max(0.0, ExciteDriftRelaxMult));
            float calm = ExciteStep(fdt);
            float grow = BornStep(fdt);
            float speedMul = Mathf.Clamp01(SpeedMul);
            double captured = 0;

            for (int i = 0; i < count; i++)
            {
                ref Dust d = ref items[i];
                if (d.excite > 0f) d.excite = Mathf.Max(0f, d.excite - calm);
                if (d.born < 1f) d.born = Mathf.Min(1f, d.born + grow);
                Vector2 toCursor = gravity - d.pos;
                float dist2 = toCursor.sqrMagnitude;

                bool pulled = holding && dist2 < outerR2;
                if (pulled)
                {
                    float dist = Mathf.Sqrt(dist2);
                    float f = (float)PullFactor(dist, gravR, p.pullFullRatio, p.pullEdgeShare, p.pullOuterRatio);
                    if (f > 0f && dist > 1e-6f)
                    {
                        // 22 §5: part of the pull is turned sideways (counter-clockwise), so the dust spirals in.
                        Vector2 inward = toCursor / dist;
                        Vector2 swirl = new Vector2(inward.y, -inward.x);
                        float pull = accel * KindAccel(d.kind) * f;
                        // The sideways push fades out as the dust reaches vortexTerminalPx and reverses above it. Without
                        // that ceiling the swirl kept feeding sideways speed until the pull and the centrifugal need
                        // balanced, and the dust circled the gravity source for ever instead of falling in.
                        float sideways = Vector2.Dot(d.vel, swirl);
                        float swirlAccel = pull * vortex * (1f - sideways / vortexTerm);
                        d.vel += (inward * pull + swirl * swirlAccel) * fdt;
                    }
                    // Near the capture circle the sideways speed is damped, so dust falls in instead of orbiting.
                    if (dist2 < assistR2 && dist > 1e-6f)
                    {
                        Vector2 dir = toCursor / dist;
                        Vector2 radial = dir * Vector2.Dot(d.vel, dir);
                        d.vel = radial + (d.vel - radial) * tangentKeep;
                    }
                }
                // 24: what the abilities put on the dust. The distance test is all a dust out of reach pays.
                for (int k = 0; k < forceCount; k++)
                {
                    ref DustForce force = ref forces[k];
                    Vector2 toForce = force.at - d.pos;
                    float reach2 = toForce.sqrMagnitude;
                    if (reach2 >= force.radius2) continue;
                    if (holding && dist2 < force.yieldRadius2)
                    {
                        // 10차 회신: inside the gravity radius the satellite lets go of what it brought, and what it towed
                        // loses the speed the tow gave it, so it falls in instead of being carried back out.
                        float held2 = d.vel.sqrMagnitude;
                        if (held2 > maxSpeed2) d.vel *= Mathf.Sqrt(maxSpeed2 / held2);
                        continue;
                    }
                    float reach = Mathf.Sqrt(reach2);
                    float share = (float)PullFactor(reach, force.radius, p.pullFullRatio, p.pullEdgeShare, p.pullOuterRatio);
                    float push = force.accel * KindAccel(d.kind) * share * fdt;
                    if (force.dir.x != 0f || force.dir.y != 0f) d.vel += force.dir * push;
                    else if (reach > 1e-6f)
                    {
                        Vector2 inward = toForce / reach;
                        d.vel += inward * push;
                        // 24 §19.1: part of the pull turned sideways, with the same ceiling as the gravity source's swirl.
                        if (force.vortex > 0f)
                        {
                            Vector2 swirl = new Vector2(inward.y, -inward.x);
                            float sideways = Vector2.Dot(d.vel, swirl);
                            d.vel += swirl * (push * force.vortex * (1f - sideways / vortexTerm));
                        }
                    }
                    // 24 §18.1: in the core the dust is carried along: its velocity is drawn to the satellite's.
                    if (force.tow > 0f && reach2 < force.towRadius2) d.vel += (force.vel - d.vel) * Mathf.Min(1f, force.tow * fdt);
                    if (force.excite) d.excite = 1f;
                    pulled = true;
                }
                if (pulled)
                {
                    float sp2 = d.vel.sqrMagnitude;
                    float cap2 = d.excite > 0f ? maxSpeedExcited2 : maxSpeed2;
                    if (sp2 > cap2) d.vel *= Mathf.Sqrt(cap2 / sp2);
                }
                else
                {
                    // Drift: small random heading change; speed relaxes toward driftSpeed, more slowly while the
                    // dust is still excited by an ability (24 §16.1).
                    float speed = d.vel.magnitude;
                    float turn = (float)(rng.NextDouble() * 2.0 - 1.0) * noise * fdt;
                    float cos = Mathf.Cos(turn), sin = Mathf.Sin(turn);
                    Vector2 dir = speed > 1e-6f ? d.vel / speed : RandomDir();
                    dir = new Vector2(dir.x * cos - dir.y * sin, dir.x * sin + dir.y * cos);
                    speed = Mathf.Lerp(speed, drift, d.excite > 0f ? relaxExcited : relax);
                    d.vel = dir * speed;
                }

                // 24 §19.1: the inhale slows the whole field.
                if (speedMul < 1f) d.vel *= speedMul;

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

                // The edge of the screen. A dust on screen never leaves it: it is turned back there, as it always was,
                // so nothing the player can see is lost to the band and the band never holds more than it was given.
                // A dust of the band comes in only when something has set it moving (a pull, a push, a wake); while
                // it is merely drifting it is turned back from the outside, so the band does not leak into the screen.
                bool wasIn = from.x >= area.xMin && from.x <= area.xMax && from.y >= area.yMin && from.y <= area.yMax;
                if (wasIn || !banded)
                {
                    // Reflect at the visible bounds.
                    if (d.pos.x < area.xMin) { d.pos.x = area.xMin; d.vel.x = Mathf.Abs(d.vel.x); }
                    else if (d.pos.x > area.xMax) { d.pos.x = area.xMax; d.vel.x = -Mathf.Abs(d.vel.x); }
                    if (d.pos.y < area.yMin) { d.pos.y = area.yMin; d.vel.y = Mathf.Abs(d.vel.y); }
                    else if (d.pos.y > area.yMax) { d.pos.y = area.yMax; d.vel.y = -Mathf.Abs(d.vel.y); }
                    continue;
                }
                if (d.vel.sqrMagnitude <= passSpeed2 &&
                    d.pos.x >= area.xMin && d.pos.x <= area.xMax && d.pos.y >= area.yMin && d.pos.y <= area.yMax)
                {
                    // It drifted in from the band: back out, on the axis (at a corner, both) it was outside on.
                    if (from.x < area.xMin || from.x > area.xMax) { d.pos.x = from.x; d.vel.x = -d.vel.x; }
                    if (from.y < area.yMin || from.y > area.yMax) { d.pos.y = from.y; d.vel.y = -d.vel.y; }
                }
                if (d.pos.x < outer.xMin) { d.pos.x = outer.xMin; d.vel.x = Mathf.Abs(d.vel.x); }
                else if (d.pos.x > outer.xMax) { d.pos.x = outer.xMax; d.vel.x = -Mathf.Abs(d.vel.x); }
                if (d.pos.y < outer.yMin) { d.pos.y = outer.yMin; d.vel.y = Mathf.Abs(d.vel.y); }
                else if (d.pos.y > outer.yMax) { d.pos.y = outer.yMax; d.vel.y = -Mathf.Abs(d.vel.y); }
                if (d.pos.x < area.xMin || d.pos.x > area.xMax || d.pos.y < area.yMin || d.pos.y > area.yMax) outside++;
            }

            // Spawn. At the cap the spawn is skipped, not banked.
            // 24 §23.1.1 (10/12): new dust keeps out of the gravity radius only while holding, so it does not land in the
            // pull. Kept out while idle too, the dust around a still gravity source thinned to half in 13 s at ring 11.
            float avoidR = holding ? gravR : 0f;
            // 10/8: the emptier the field, the faster it refills (x1 at the cap, x(1 + dustRefillBoost) when empty), so an
            // ability that sweeps the screen clean does not leave it bare for most of the run.
            int cap = (int)s.dustCap;
            float fill = cap > 0 ? Mathf.Clamp01(count / (float)cap) : 1f;
            SpawnBoost = 1.0 + Math.Max(0.0, p.dustRefillBoost) * (1f - fill);
            spawnAcc += s.spawnRate * SpawnBoost * dt;
            while (spawnAcc >= 1.0)
            {
                spawnAcc -= 1.0;
                if (count < cap) SpawnRandom(gravity, avoidR, s);
            }

            // The band outside the screen has a trickle of its own, which stops at the band's share of the cap: it is
            // topped up after something pulled dust out of it and never piles up. The spawn on screen above is as it
            // was; the cap is the cap of both together.
            if (banded)
            {
                int budget = MarginBudget(cap);
                marginAcc += s.spawnRate * SpawnBoost * Math.Max(0.0, p.dustMarginSpawnShare) * dt;
                while (marginAcc >= 1.0)
                {
                    marginAcc -= 1.0;
                    if (count >= cap || outside >= budget) continue;
                    SpawnOutside(gravity, avoidR, margin, s);
                    outside++;
                }
            }
            OutsideCount = outside;

            TickTrails(dt);
            CapturedMass += captured;
            return captured;
        }

        /// <summary>How much calmer an excited dust gets in <paramref name="fdt"/> seconds (24 §8): 1 → 0 over <see cref="ExciteSec"/>.</summary>
        float ExciteStep(float fdt) => ExciteSec > 0 ? (float)(fdt / ExciteSec) : 1f;

        /// <summary>
        /// 24 §3: one push for every dust the ring of 수축 passes this tick, which is the dust between
        /// <paramref name="innerR"/> and <paramref name="outerR"/> of <paramref name="origin"/>. The push is
        /// <paramref name="speed"/> toward <paramref name="toward"/>, and it is the same for every dust whatever its
        /// distance or kind, so the whole field turns as one. The dust it hits is excited (24 §8). World units.
        /// Returns how many were hit.
        /// </summary>
        public int Impulse(Vector2 origin, float innerR, float outerR, Vector2 toward, float speed)
        {
            float inner2 = innerR * innerR, outer2 = outerR * outerR;
            int hit = 0;
            for (int i = 0; i < count; i++)
            {
                ref Dust d = ref items[i];
                float dist2 = (d.pos - origin).sqrMagnitude;
                if (dist2 < inner2 || dist2 >= outer2) continue;
                Vector2 to = toward - d.pos;
                float len = to.magnitude;
                if (len > 1e-6f) d.vel += to * (speed / len);
                d.excite = 1f;
                hit++;
            }
            return hit;
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
            float calm = ExciteStep(fdt);
            float grow = BornStep(fdt);
            int absorbed = 0;
            for (int i = 0; i < count; i++)
            {
                ref Dust d = ref items[i];
                if (d.excite > 0f) d.excite = Mathf.Max(0f, d.excite - calm);
                if (d.born < 1f) d.born = Mathf.Min(1f, d.born + grow);
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

        /// <summary>24 §20.2: every dust lights up at once (24 §8), the field's answer to a crystal being taken.</summary>
        public void ExciteAll()
        {
            for (int i = 0; i < count; i++) items[i].excite = 1f;
        }

        /// <summary>Removes every dust.</summary>
        public void Clear()
        {
            count = 0;
            spawnAcc = 0;
            marginAcc = 0;
            trailClock = 0;
            OutsideCount = 0;
        }

        /// <summary>
        /// Adds a dust outside the game rules (the supernova remnant, 15 §4.3). Every random value comes from the render-only
        /// RNG so the simulation sequence of later runs is unchanged.
        /// </summary>
        public void AddFree(Vector2 pos, Vector2 vel)
        {
            if (count >= items.Length) Grow();
            int index = count++;
            items[index] = new Dust
            {
                pos = pos,
                vel = vel,
                tint = (float)visualRng.NextDouble(),
                size = 1f + (float)((visualRng.NextDouble() * 2.0 - 1.0) * p.dustSizeJitter),
                bright = 1f + (float)((visualRng.NextDouble() * 2.0 - 1.0) * p.dustBrightJitter),
                // The remnant is thrown out by the blast and is there at once: no fade-in.
                born = 1f,
            };
            ClearTrail(index, pos);
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
        /// 24 §5: a meteor's dust multiplies the share of every kind above the base dust by
        /// <paramref name="heavyWeight"/>. If that takes the shares past 1 they are scaled down together, so the order
        /// between the kinds stays what it is.
        /// </summary>
        int RollKind(in EffectiveStats s, double heavyWeight = 1.0)
        {
            var chance = s.dustKindChance;
            if (chance == null || chance.Length < 2) return 0;
            double scale = heavyWeight;
            if (heavyWeight != 1.0)
            {
                double total = 0;
                for (int k = 1; k < chance.Length; k++) total += chance[k] * heavyWeight;
                if (total > 1) scale = heavyWeight / total;
            }
            double u = rng.NextDouble();
            double acc = 0;
            for (int k = 1; k < chance.Length; k++)
            {
                acc += chance[k] * scale;
                if (u < acc) return k;
            }
            return 0;
        }

        /// <summary>
        /// 24 §5: one dust shed by a meteor, at <paramref name="pos"/> with <paramref name="vel"/>. Its kind is rolled
        /// with the heavier kinds weighted by <paramref name="heavyWeight"/>, and it is born excited (24 §8). Nothing is
        /// added at the dust cap or outside the visible area: the meteor then sheds that much less. Returns whether the
        /// dust was added.
        /// </summary>
        public bool Shed(Vector2 pos, Vector2 vel, in EffectiveStats s, double heavyWeight)
        {
            if (count >= (int)s.dustCap || !Area.Contains(pos)) return false;
            Add(pos, vel, RollKind(s, heavyWeight));
            items[count - 1].excite = 1f;
            return true;
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

        /// <summary>
        /// Random position in the band outside the visible area, even over the whole band so that no part of it
        /// gathers more than another, and outside gravityRadius of the cursor (best effort) as on screen: a gravity
        /// source held at the edge is not fed dust that was created inside its own reach.
        /// </summary>
        void SpawnOutside(Vector2 cursor, float gravR, float margin, in EffectiveStats s)
        {
            Rect a = Area;
            float gravR2 = gravR * gravR;
            // The band as four strips that do not overlap: one above and one below as wide as the band is, and the
            // two beside the screen. A strip is picked by its area, then a point in it.
            float wide = a.width + margin * 2f;
            float across = wide * margin;
            float beside = a.height * margin;
            float total = (across + beside) * 2f;
            Vector2 pos = default;
            for (int tries = 0; tries < 16; tries++)
            {
                float pick = (float)rng.NextDouble() * total;
                float u = (float)rng.NextDouble(), v = (float)rng.NextDouble();
                if (pick < across) pos = new Vector2(a.xMin - margin + u * wide, a.yMax + v * margin);
                else if (pick < across * 2f) pos = new Vector2(a.xMin - margin + u * wide, a.yMin - margin + v * margin);
                else if (pick < across * 2f + beside) pos = new Vector2(a.xMin - margin + u * margin, a.yMin + v * a.height);
                else pos = new Vector2(a.xMax + u * margin, a.yMin + v * a.height);
                if ((pos - cursor).sqrMagnitude >= gravR2) break;
            }
            Add(pos, RandomDir() * Px(p.driftSpeed), RollKind(s));
        }

        /// <summary>
        /// A new dust of the game. It starts with no path behind it and unseen: its ribbon grows from nothing and its
        /// brightness fades in over GameParams.dustSpawnFadeSec, so a dust created in plain view, inside the gravity
        /// field included, does not pop in. Where a dust is created is not this method's business and has not changed.
        /// </summary>
        void Add(Vector2 pos, Vector2 vel, int kind = 0)
        {
            if (count >= items.Length) Grow();
            int index = count++;
            items[index] = new Dust
            {
                pos = pos,
                vel = vel,
                kind = kind,
                tint = (float)rng.NextDouble(),
                size = 1f + (float)((visualRng.NextDouble() * 2.0 - 1.0) * p.dustSizeJitter),
                bright = 1f + (float)((visualRng.NextDouble() * 2.0 - 1.0) * p.dustBrightJitter),
            };
            ClearTrail(index, pos);
        }

        /// <summary>
        /// Doubles both arrays together so a dust and its trail block keep the same index (22 §5). Both keep what they
        /// hold: see <see cref="EnsureTrail"/>.
        /// </summary>
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
