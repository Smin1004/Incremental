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
        Mesh mesh;
        MeshRenderer mr;
        Vector3[] verts = new Vector3[0];
        Vector3[] uvs = new Vector3[0];
        Color32[] cols = new Color32[0];
        int[] tris = new int[0];
        float holdGlow;

        public void Init(GameRoot gameRoot, DustField dustField, Material material)
        {
            root = gameRoot;
            field = dustField;
            p = root.gameParams;
            mesh = new Mesh { name = "DustMesh", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
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
        }

        /// <summary>The dust is drawn during runs only; between runs the solar system and the tree take the screen.</summary>
        public void SetVisible(bool visible)
        {
            if (mr.enabled != visible) mr.enabled = visible;
        }

        void LateUpdate()
        {
            if (field == null || mesh == null) return;
            int n = field.Count;
            if (n == 0)
            {
                mesh.Clear(true);
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
            Vector2 cursor = endingPull ? root.EndingCenter : root.CursorWorld;
            float gravR = endingPull ? (float)(p.endingDustGlowRadiusPx / ppu) : (float)(root.Effective.gravityRadius / ppu);
            float edgeR = gravR + Mathf.Max(1e-3f, (float)(p.dustDimEdgePx / ppu));
            float edgeR2 = edgeR * edgeR;
            Color colA = root.Backdrop.DustColorA.linear;
            Color colB = root.Backdrop.DustColorB.linear;

            var items = field.Items;
            for (int i = 0; i < n; i++)
            {
                ref Dust d = ref items[i];
                var kindDef = p.DustKind(d.kind);
                float kindSize = kindDef != null ? (float)kindDef.sizeMult : 1f;
                float kindBright = kindDef != null ? (float)kindDef.brightMult : 1f;
                float kindShape = kindDef != null ? (float)(int)kindDef.shape : 0f;
                Vector2 c = d.pos;
                float speed = d.vel.magnitude;
                Vector2 axis = speed > 1e-5f ? d.vel / speed : new Vector2(1f, 0f);
                float stretch = Mathf.Clamp01((speed - drift) / (full - drift));
                float s = 1f + (stretchMax - 1f) * stretch;
                float h = halo * d.size * kindSize;
                Vector2 along = axis * (h * s);
                Vector2 across = new Vector2(-axis.y, axis.x) * (h * Mathf.Lerp(1f, widthAtFull, stretch));

                int v = i * 4;
                verts[v] = c - along - across;
                verts[v + 1] = c - along + across;
                verts[v + 2] = c + along + across;
                verts[v + 3] = c + along - across;
                uvs[v] = new Vector3(-s, -1f, s);
                uvs[v + 1] = new Vector3(-s, 1f, s);
                uvs[v + 2] = new Vector3(s, 1f, s);
                uvs[v + 3] = new Vector3(s, -1f, s);

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
                float b = d.bright * kindBright * Mathf.Lerp(dim, 1f, inField) * (1f + boost);
                if (remnant) b *= (float)p.remnantDustBrightness;
                Color col = Color.Lerp(colA, colB, d.tint);
                // Brightness 0..2 is stored halved in the 8-bit vertex color; the shader doubles it back.
                // The alpha channel carries the dust kind's shape id (20 §4), which the shader reads back as 0..255.
                float k = Mathf.Min(b, 2f) * 0.5f;
                Color32 c32 = new Color(col.r * k, col.g * k, col.b * k, kindShape / 255f);
                cols[v] = c32; cols[v + 1] = c32; cols[v + 2] = c32; cols[v + 3] = c32;
            }

            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontResetBoneBounds;
            mesh.Clear(true);
            mesh.SetVertices(verts, 0, n * 4, flags);
            mesh.SetUVs(0, uvs, 0, n * 4, flags);
            mesh.SetColors(cols, 0, n * 4, flags);
            mesh.SetIndices(tris, 0, n * 6, MeshTopology.Triangles, 0, false);
            Rect a = field.Area;
            mesh.bounds = new Bounds(new Vector3(a.center.x, a.center.y, 0f), new Vector3(a.width + 1f, a.height + 1f, 1f));
        }

        void EnsureCapacity(int n)
        {
            if (verts.Length >= n * 4) return;
            int cap = Mathf.Max(256, verts.Length / 4);
            while (cap < n) cap *= 2;
            verts = new Vector3[cap * 4];
            uvs = new Vector3[cap * 4];
            cols = new Color32[cap * 4];
            tris = new int[cap * 6];
            for (int i = 0; i < cap; i++)
            {
                int v = i * 4, t = i * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
        }
    }
}
