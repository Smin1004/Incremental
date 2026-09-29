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
    /// 22 §7: the per-dust loop writes one interleaved vertex array that goes to the GPU in a single copy
    /// (<see cref="DynamicMesh"/>), the per-kind data is read into flat arrays once a frame instead of once a dust, and
    /// nothing is built at all while the mesh is not being drawn.
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
            int kinds = kindSize.Length;

            var items = field.Items;
            for (int i = 0; i < n; i++)
            {
                ref Dust d = ref items[i];
                int kind = d.kind >= 0 && d.kind < kinds ? d.kind : 0;
                Vector2 c = d.pos;
                float speed = d.vel.magnitude;
                Vector2 axis = speed > 1e-5f ? d.vel / speed : new Vector2(1f, 0f);
                float stretch = Mathf.Clamp01((speed - drift) / (full - drift));
                float s = 1f + (stretchMax - 1f) * stretch;
                float h = halo * d.size * kindSize[kind];
                Vector2 along = axis * (h * s);
                Vector2 across = new Vector2(-axis.y, axis.x) * (h * Mathf.Lerp(1f, widthAtFull, stretch));

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
                float b = d.bright * kindBright[kind] * Mathf.Lerp(dim, 1f, inField) * (1f + boost) * remnantBright;
                // Brightness 0..2 is stored halved in the 8-bit vertex color; the shader doubles it back.
                // The alpha channel carries the dust kind's shape id (20 §4), which the shader reads back as 0..255.
                float k = Mathf.Min(b, 2f) * 0.5f;
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

            Rect a = field.Area;
            var bounds = new Bounds(new Vector3(a.center.x, a.center.y, 0f), new Vector3(a.width + 1f, a.height + 1f, 1f));
            mesh.Upload(verts, n * 4, n * 6, bounds);
        }

        /// <summary>0..1 to a colour byte, saturating. Color32's own conversion does the same, through a Color first.</summary>
        static byte Byte01(float v) => (byte)(Mathf.Clamp01(v) * 255f + 0.5f);

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
