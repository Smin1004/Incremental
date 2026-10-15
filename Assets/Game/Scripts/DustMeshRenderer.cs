using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace Incremental
{
    /// <summary>
    /// Draws every dust as one quad in a single dynamic mesh, rebuilt each frame (14 §5): a soft additive glowing point
    /// (Incremental/Dust), per-dust size and brightness variation, stretched along the velocity when fast (up to
    /// dustStretchMax) into a thinner streak whose tail narrows to a point. Only dust in the active gravity field keeps full
    /// brightness (brighter toward the center); the rest is dimmed to dustOutsideDim so the eye goes to the cursor (15 §3).
    /// Colors are the warm dust hue of the background palette, converted to linear. During the ending pull (15 §4.2) every
    /// dust is being pulled, so none is dimmed and dust near the blue giant glows; the supernova remnant (§4.3) is drawn at
    /// remnantDustBrightness. Render only.
    ///
    /// 26 §5.5: with GameRoot.ExposureFix on, the brightness is held down where it used to pile up into white: every dust
    /// dims a little when there is a lot of dust on screen (dustDensityRef / Floor), dust near the forming body dims
    /// toward the center (dustCenterDimRadii / Dim) so the body's surface shows, the excite flash is weaker on large
    /// dust (exciteSizeFalloff), and one dust is capped below the point where every channel clips (dustBrightCap). In
    /// rings 1-2 the dust is drawn larger and brighter (earlyDustSizeMult / BrightMult); count and mass are untouched.
    ///
    /// 22 §7: the per-dust loop writes one interleaved vertex array that goes to the GPU in a single copy
    /// (<see cref="DynamicMesh"/>), the per-kind data is read into flat arrays once a frame instead of once a dust, and
    /// nothing is built at all while the mesh is not being drawn.
    ///
    /// 11th pass F: the per-dust loop is one Burst job (<see cref="QuadJob"/>) spread over the worker threads; the
    /// arithmetic is the same, so the picture is the same. GameParams.dustMeshJobs off runs the same Execute on the
    /// main thread, one dust after another, for comparison.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class DustMeshRenderer : MonoBehaviour
    {
        static readonly int IdCore = Shader.PropertyToID("_Core");
        static readonly int IdHaloAlpha = Shader.PropertyToID("_HaloAlpha");
        static readonly int IdStreakDim = Shader.PropertyToID("_StreakDim");

        GameRoot root;
        DustField field;
        GameParams p;
        DynamicMesh mesh;
        MeshRenderer mr;
        DustVertex[] verts = new DustVertex[0];
        float holdGlow;
        // 22 §7: one entry per dust kind, read out of the data once a frame. The old code asked GameParams for the kind of
        // every single dust, three times each.
        float[] kindSize = new float[0];
        float[] kindBright = new float[0];
        float[] kindShape = new float[0];

        readonly System.Diagnostics.Stopwatch rebuildWatch = new System.Diagnostics.Stopwatch();
        double rebuildMsAcc;

        /// <summary>
        /// 22 §4: milliseconds spent rebuilding the dust mesh since the last call, and zero after it. Read once a frame
        /// by the performance measurement.
        /// </summary>
        public double TakeRebuildMs()
        {
            double v = rebuildMsAcc;
            rebuildMsAcc = 0;
            return v;
        }

        public void Init(GameRoot gameRoot, DustField dustField, Material material)
        {
            root = gameRoot;
            field = dustField;
            p = root.gameParams;
            mesh = new DynamicMesh("DustMesh");
            GetComponent<MeshFilter>().sharedMesh = mesh.Mesh;
            mr = GetComponent<MeshRenderer>();
            if (material != null) mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.sortingOrder = 0;
            var mpb = new MaterialPropertyBlock();
            mpb.SetFloat(IdCore, (float)(p.dustCorePx / System.Math.Max(p.dustHaloPx, 1e-3)));
            mpb.SetFloat(IdHaloAlpha, (float)p.dustHaloAlpha);
            mpb.SetFloat(IdStreakDim, (float)p.dustStreakDim);
            mr.SetPropertyBlock(mpb);
            ReadKinds();
            // 22 §7: size the buffers once for the most dust the game can ask for. Growing them mid-run means a new
            // vertex buffer and a stall, which showed up in the 22 §4 measurement as a 376 ms frame.
            EnsureCapacity((int)p.dustCapCeiling);
        }

        void ReadKinds()
        {
            int kinds = Mathf.Max(1, p.DustKindCount);
            if (kindSize.Length == kinds) return;
            kindSize = new float[kinds];
            kindBright = new float[kinds];
            kindShape = new float[kinds];
            for (int k = 0; k < kinds; k++)
            {
                var def = p.DustKind(k);
                kindSize[k] = def != null ? (float)def.sizeMult : 1f;
                kindBright[k] = def != null ? (float)def.brightMult : 1f;
                kindShape[k] = def != null ? (int)def.shape : 0;
            }
        }

        /// <summary>The dust is drawn during runs only; between runs the solar system and the tree take the screen.</summary>
        public void SetVisible(bool visible)
        {
            if (mr.enabled != visible) mr.enabled = visible;
        }

        void LateUpdate()
        {
            if (field == null || mesh == null) return;
            // 22 §7: nothing to build while the mesh is not drawn. Between runs the field still holds a run's worth of
            // dust as a still background, and rebuilding it cost a full frame's worth of work for nothing.
            if (!mr.enabled)
            {
                mesh.Clear();
                return;
            }
            rebuildWatch.Restart();
            Rebuild();
            rebuildWatch.Stop();
            rebuildMsAcc += rebuildWatch.Elapsed.TotalMilliseconds;
        }

        void Rebuild()
        {
            int n = field.Count;
            if (n == 0)
            {
                mesh.Clear();
                return;
            }
            EnsureCapacity(n);

            float ppu = (float)p.pixelsPerUnit;
            float halo = (float)(p.dustHaloPx / ppu) * 0.5f;
            float drift = (float)(p.driftSpeed / ppu);
            float full = Mathf.Max(drift + 1e-3f, (float)(p.dustStretchFullSpeedPx / ppu));
            float stretchMax = Mathf.Max(1f, (float)p.dustStretchMax);
            float exposure = (float)p.dustStreakExposureSec;
            // 22 §5: a kind that draws a trail ribbon does not also smear its own quad. Two pictures of the same motion
            // left a round head sticking out of the front of the ribbon and a hard shoulder where the ribbon met the
            // middle of the dust; the bigger the kind, the more it showed. The ribbon carries the motion, the quad is
            // just the dust, silhouette and all.
            bool trailsOn = p.trailBudget > 0 && p.trailPoints >= 2;
            float brighten = (float)p.dustGravityBrighten;
            float dim = (float)p.dustOutsideDim;
            float widthAtFull = (float)p.dustStreakWidth;
            bool holding = root.Phase == GamePhase.Run && root.Run != null && root.Run.holding && !root.Paused;
            float fade = Mathf.Max(1e-3f, (float)p.dustHoldFadeSec);
            holdGlow = Mathf.MoveTowards(holdGlow, holding ? 1f : 0f, Time.unscaledDeltaTime / fade);
            bool endingPull = root.EndingActive && root.Phase == GamePhase.Run;
            bool remnant = !endingPull && root.Ending != null && root.Ending.ShowsDust;
            if (endingPull) holdGlow = 1f;
            Vector2 cursor = endingPull ? root.EndingCenter : root.GravityWorld;
            float gravR = endingPull ? (float)(p.endingDustGlowRadiusPx / ppu) : (float)(root.Effective.gravityRadius / ppu);
            float edgeR = gravR + Mathf.Max(1e-3f, (float)(p.dustDimEdgePx / ppu));
            float edgeR2 = edgeR * edgeR;
            Color colA = root.Backdrop.DustColorA.linear;
            Color colB = root.Backdrop.DustColorB.linear;
            float remnantBright = remnant ? (float)p.remnantDustBrightness : 1f;
            // 24 §8: a dust an ability has just hit is drawn brighter while it calms down. It is a flash like any other,
            // so it follows the effect intensity.
            float exciteBright = root.abilityTable != null ? (float)root.abilityTable.exciteBrightness * root.EffectIntensity : 0f;
            int kinds = kindSize.Length;

            // 26 §5.5: the overexposure measures, and the early-ring look.
            bool fix = root.ExposureFix;
            float density = fix ? DensityDim(p, n) : 1f;
            float centerR = 0f, centerDim = 1f;
            if (fix && holding && p.dustCenterDimRadii > 0)
            {
                centerR = (float)(root.RunBodyPx(root.Cursor.FormingDiameterPx) * 0.5 * p.dustCenterDimRadii / ppu);
                centerDim = (float)p.dustCenterDim;
            }
            float centerR2 = centerR * centerR;
            float exciteFalloff = fix ? (float)p.exciteSizeFalloff : 0f;
            float cap = fix ? Mathf.Clamp((float)p.dustBrightCap, 0.01f, 2f) : 2f;
            int ring = root.Meta.unlockedMaxTier;
            float earlySize = EarlyMult(p.earlyDustSizeMult, ring);
            float earlyBright = EarlyMult(p.earlyDustBrightMult, ring) * density;

            var job = new QuadJob
            {
                kinds = kinds, halo = halo, drift = drift, full = full, stretchMax = stretchMax, exposure = exposure,
                trailsOn = trailsOn, brighten = brighten, dim = dim, widthAtFull = widthAtFull, holdGlow = holdGlow,
                endingPull = endingPull, remnant = remnant, cursor = cursor, gravR = gravR, edgeR = edgeR, edgeR2 = edgeR2,
                colA = colA, colB = colB, remnantBright = remnantBright, exciteBright = exciteBright, centerR = centerR,
                centerR2 = centerR2, centerDim = centerDim, exciteFalloff = exciteFalloff, cap = cap, earlySize = earlySize,
                earlyBright = earlyBright,
            };
            unsafe
            {
                fixed (Dust* items = field.Items)
                fixed (DustVertex* vp = verts)
                fixed (float* ks = kindSize, kb = kindBright, kh = kindShape)
                {
                    job.items = items;
                    job.verts = vp;
                    job.kindSize = ks;
                    job.kindBright = kb;
                    job.kindShape = kh;
                    if (p.dustMeshJobs) job.Schedule(n, Mathf.Max(16, p.dustMeshJobBatch)).Complete();
                    else for (int i = 0; i < n; i++) job.Execute(i);
                }
            }

            Rect a = field.Area;
            var bounds = new Bounds(new Vector3(a.center.x, a.center.y, 0f), new Vector3(a.width + 1f, a.height + 1f, 1f));
            mesh.Upload(verts, n * 4, n * 6, bounds);
        }

        /// <summary>
        /// 26 §5.5: how much every dust dims for the amount of dust on screen: 1 up to dustDensityRef, then sqrt(ref / count),
        /// never below dustDensityFloor. A pure function so the trail renderer shares it.
        /// </summary>
        public static float DensityDim(GameParams p, int count)
        {
            if (p.dustDensityRef <= 0 || count <= p.dustDensityRef) return 1f;
            return Mathf.Max((float)p.dustDensityFloor, Mathf.Sqrt((float)(p.dustDensityRef / count)));
        }

        /// <summary>26 §5.5: the early-ring multiplier for the highest unlocked ring; 1 past the end of the array.</summary>
        public static float EarlyMult(double[] mults, int ring) =>
            mults != null && ring >= 1 && ring <= mults.Length ? (float)mults[ring - 1] : 1f;

        /// <summary>0..1 to a colour byte, saturating. Color32's own conversion does the same, through a Color first.</summary>
        static byte Byte01(float v) => (byte)(Mathf.Clamp01(v) * 255f + 0.5f);

        /// <summary>
        /// 11th pass F: one dust's quad (four vertices), exactly what the old loop wrote. The frame's values are copied in
        /// as fields; the dust, the vertices and the per-kind tables are pinned arrays, read and written through pointers
        /// (each index touches only its own dust and its own four vertices).
        /// </summary>
        [BurstCompile]
        unsafe struct QuadJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction] public Dust* items;
            [NativeDisableUnsafePtrRestriction] public DustVertex* verts;
            [NativeDisableUnsafePtrRestriction] public float* kindSize;
            [NativeDisableUnsafePtrRestriction] public float* kindBright;
            [NativeDisableUnsafePtrRestriction] public float* kindShape;
            public int kinds;
            public float halo, drift, full, stretchMax, exposure, brighten, dim, widthAtFull, holdGlow;
            public bool trailsOn, endingPull, remnant;
            public Vector2 cursor;
            public float gravR, edgeR, edgeR2;
            public Color colA, colB;
            public float remnantBright, exciteBright, centerR, centerR2, centerDim, exciteFalloff, cap, earlySize, earlyBright;

            public void Execute(int i)
            {
                ref Dust d = ref items[i];
                int kind = d.kind >= 0 && d.kind < kinds ? d.kind : 0;
                Vector2 c = d.pos;
                float speed = d.vel.magnitude;
                Vector2 axis = speed > 1e-5f ? d.vel / speed : new Vector2(1f, 0f);
                float h = halo * d.size * kindSize[kind] * earlySize;
                // The smear is how far the dust travels in one "shutter", which is the same distance for every kind.
                // Multiplying the dust's own size by the speed (14 §5) drew the heavy kinds, up to 8.5x wider than base
                // dust, as long bars. A kind that has a ribbon does not smear at all: its quad stays the round dust.
                bool ribboned = trailsOn && kind > 0;
                float s = ribboned ? 1f : Mathf.Min(1f + speed * exposure / Mathf.Max(h, 1e-6f), stretchMax);
                // The streak also narrows, but by how much it actually stretched, not by the raw speed.
                float narrow = stretchMax > 1f ? (s - 1f) / (stretchMax - 1f) : 0f;
                Vector2 along = axis * (h * s);
                Vector2 across = new Vector2(-axis.y, axis.x) * (h * Mathf.Lerp(1f, widthAtFull, narrow));

                // Dust in the gravity field (held) keeps full brightness and gets brighter toward the center; the rest dims.
                float inField = endingPull ? 1f : 0f;
                float boost = 0f;
                if (remnant) inField = 1f;
                else if (holdGlow > 0f)
                {
                    float dist2 = (c - cursor).sqrMagnitude;
                    if (dist2 < edgeR2)
                    {
                        float dist = Mathf.Sqrt(dist2);
                        if (!endingPull) inField = dist <= gravR ? 1f : 1f - (dist - gravR) / (edgeR - gravR);
                        if (dist < gravR) boost = brighten * (1f - dist / gravR);
                        inField *= holdGlow;
                        boost *= holdGlow;
                    }
                }
                float b = d.bright * kindBright[kind] * Mathf.Lerp(dim, 1f, inField) * (1f + boost) * remnantBright * earlyBright;
                if (d.excite > 0f) b *= 1f + d.excite * exciteBright / (1f + (kindSize[kind] - 1f) * exciteFalloff);
                // 26 §5.5: dust right over the forming body dims toward its center so the body is not drowned.
                if (centerR > 0f)
                {
                    float dc2 = (c - cursor).sqrMagnitude;
                    if (dc2 < centerR2) b *= Mathf.Lerp(centerDim, 1f, Mathf.Sqrt(dc2) / centerR);
                }
                // A dust that was just created fades in (GameParams.dustSpawnFadeSec).
                if (d.born < 1f) b *= d.born;
                // Brightness 0..2 is stored halved in the 8-bit vertex color; the shader doubles it back.
                // The alpha channel carries the dust kind's shape id (20 §4), which the shader reads back as 0..255.
                float k = Mathf.Min(b, cap) * 0.5f;
                float t = d.tint;
                var c32 = new Color32(
                    Byte01(Mathf.LerpUnclamped(colA.r, colB.r, t) * k),
                    Byte01(Mathf.LerpUnclamped(colA.g, colB.g, t) * k),
                    Byte01(Mathf.LerpUnclamped(colA.b, colB.b, t) * k),
                    (byte)kindShape[kind]);

                int v = i * 4;
                verts[v].pos = new Vector3(c.x - along.x - across.x, c.y - along.y - across.y, 0f);
                verts[v].uv = new Vector3(-s, -1f, s);
                verts[v].col = c32;
                verts[v + 1].pos = new Vector3(c.x - along.x + across.x, c.y - along.y + across.y, 0f);
                verts[v + 1].uv = new Vector3(-s, 1f, s);
                verts[v + 1].col = c32;
                verts[v + 2].pos = new Vector3(c.x + along.x + across.x, c.y + along.y + across.y, 0f);
                verts[v + 2].uv = new Vector3(s, 1f, s);
                verts[v + 2].col = c32;
                verts[v + 3].pos = new Vector3(c.x + along.x - across.x, c.y + along.y - across.y, 0f);
                verts[v + 3].uv = new Vector3(s, -1f, s);
                verts[v + 3].col = c32;
            }
        }

        void EnsureCapacity(int n)
        {
            mesh.EnsureQuads(n);
            if (verts.Length >= n * 4) return;
            int cap = Mathf.Max(256, verts.Length / 4);
            while (cap < n) cap *= 2;
            verts = new DustVertex[cap * 4];
        }
    }
}
