using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Incremental
{
    /// <summary>
    /// The trail ribbons of 22 §5. Only dust of a kind above the base one leaves one, because there is little of it and
    /// a ribbon costs more than a quad; the base dust keeps the stretched quad of 14 §5.
    ///
    /// One dust's ribbon runs from where it is now back through the positions <see cref="DustField"/> kept for it, a
    /// fixed time apart. It starts at trailWidthShare of that dust's drawn size and narrows to nothing at the tail, with
    /// the brightness going the same way, so the eye reads the head as "here" and the tail as "was". The color is the
    /// dust's own.
    ///
    /// At most trailBudget ribbons are drawn at once: the heavier kinds first, then the ones nearest the gravity source,
    /// which is where the eye is. Everything goes into one dynamic mesh, so the whole thing is one draw call. Render only.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class DustTrailRenderer : MonoBehaviour
    {
        GameRoot root;
        DustField field;
        GameParams p;
        Mesh mesh;
        MeshRenderer mr;
        Vector3[] verts = new Vector3[0];
        Vector2[] uvs = new Vector2[0];
        Color32[] cols = new Color32[0];
        int[] tris = new int[0];
        int vertCount, triCount;

        readonly List<int> candidates = new List<int>();
        System.Comparison<int> order;
        Vector2 gravity;

        readonly System.Diagnostics.Stopwatch rebuildWatch = new System.Diagnostics.Stopwatch();
        double rebuildMsAcc;

        /// <summary>22 §4: milliseconds spent building the trail mesh since the last call, and zero after it.</summary>
        public double TakeRebuildMs()
        {
            double v = rebuildMsAcc;
            rebuildMsAcc = 0;
            return v;
        }

        /// <summary>Ribbons drawn in the last frame; the report compares it with trailBudget.</summary>
        public int DrawnTrails { get; private set; }

        public void Init(GameRoot gameRoot, DustField dustField, Material material)
        {
            root = gameRoot;
            field = dustField;
            p = root.gameParams;
            mesh = new Mesh { name = "DustTrailMesh", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            mr = GetComponent<MeshRenderer>();
            if (material != null) mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // Under the dust quads, so a bright dust head still reads as the head of its own trail.
            mr.sortingOrder = -1;
            order = CompareCandidates;
        }

        public void SetVisible(bool visible)
        {
            if (mr.enabled != visible) mr.enabled = visible;
        }

        /// <summary>Heavier kinds first, then the ones nearest the gravity source (22 §5 budget rule).</summary>
        int CompareCandidates(int a, int b)
        {
            var items = field.Items;
            if (items[a].kind != items[b].kind) return items[b].kind - items[a].kind;
            float da = (items[a].pos - gravity).sqrMagnitude, db = (items[b].pos - gravity).sqrMagnitude;
            return da.CompareTo(db);
        }

        void LateUpdate()
        {
            if (field == null || mesh == null) return;
            rebuildWatch.Restart();
            Rebuild();
            rebuildWatch.Stop();
            rebuildMsAcc += rebuildWatch.Elapsed.TotalMilliseconds;
        }

        void Rebuild()
        {
            vertCount = 0;
            triCount = 0;
            DrawnTrails = 0;
            int trailLen = field.TrailLen;
            int budget = Mathf.Max(0, p.trailBudget);
            if (!mr.enabled || trailLen <= 0 || budget == 0)
            {
                mesh.Clear(true);
                return;
            }

            gravity = root.EndingActive && root.Phase == GamePhase.Run ? root.EndingCenter : root.GravityWorld;
            var items = field.Items;
            int n = field.Count;
            candidates.Clear();
            for (int i = 0; i < n; i++)
                if (items[i].kind > 0 && items[i].trailFill > 0) candidates.Add(i);
            if (candidates.Count > budget) candidates.Sort(order);
            int drawn = Mathf.Min(candidates.Count, budget);
            if (drawn == 0)
            {
                mesh.Clear(true);
                return;
            }

            // Worst case every ribbon is full length: one point for "now" plus every kept position.
            EnsureCapacity(drawn * (trailLen + 1));

            float ppu = (float)p.pixelsPerUnit;
            float halo = (float)(p.dustHaloPx / ppu) * 0.5f;
            float share = (float)p.trailWidthShare;
            Color colA = root.Backdrop.DustColorA.linear;
            Color colB = root.Backdrop.DustColorB.linear;

            for (int c = 0; c < drawn; c++)
            {
                int i = candidates[c];
                ref Dust d = ref items[i];
                var kindDef = p.DustKind(d.kind);
                float headWidth = halo * d.size * (kindDef != null ? (float)kindDef.sizeMult : 1f) * share;
                float bright = d.bright * (kindDef != null ? (float)kindDef.brightMult : 1f);
                Color col = Color.Lerp(colA, colB, d.tint);
                if (AppendRibbon(i, d.trailFill, headWidth, col, bright)) DrawnTrails++;
            }

            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontResetBoneBounds;
            mesh.Clear(true);
            if (vertCount == 0) return;
            mesh.SetVertices(verts, 0, vertCount, flags);
            mesh.SetUVs(0, uvs, 0, vertCount, flags);
            mesh.SetColors(cols, 0, vertCount, flags);
            mesh.SetIndices(tris, 0, triCount, MeshTopology.Triangles, 0, false);
            Rect a = field.Area;
            mesh.bounds = new Bounds(new Vector3(a.center.x, a.center.y, 0f), new Vector3(a.width + 1f, a.height + 1f, 1f));
        }

        /// <summary>
        /// One ribbon: the dust's position now, then the positions kept for it, newest first. Width and brightness go
        /// from full at the head to 0 at the tail. Returns false when the path is too short to draw.
        /// </summary>
        bool AppendRibbon(int index, int fill, float headWidth, Color col, float bright)
        {
            int points = fill + 1;
            if (points < 2 || headWidth <= 0f) return false;
            int firstVert = vertCount;
            int written = 0;
            Vector2 prevPoint = Vector2.zero;
            for (int j = 0; j < points; j++)
            {
                Vector2 here = j == 0 ? field.Items[index].pos : field.TrailPoint(index, j - 1);
                // A dust that has not moved since the last sample would give a zero-length segment and a NaN normal.
                if (j > 0 && (here - prevPoint).sqrMagnitude < 1e-10f) break;
                Vector2 ahead = j == 0 ? here : (j == 1 ? field.Items[index].pos : field.TrailPoint(index, j - 2));
                Vector2 behind = j + 1 < points ? field.TrailPoint(index, j) : here;
                Vector2 dir = ahead - behind;
                if (dir.sqrMagnitude < 1e-12f) dir = new Vector2(1f, 0f);
                dir.Normalize();
                Vector2 perp = new Vector2(-dir.y, dir.x);

                float taper = 1f - j / (float)(points - 1);
                float w = headWidth * taper;
                float k = Mathf.Min(bright * taper, 2f) * 0.5f;
                var c32 = (Color32)new Color(col.r * k, col.g * k, col.b * k, 1f);

                verts[vertCount] = here - perp * w;
                uvs[vertCount] = new Vector2(-1f, taper);
                cols[vertCount] = c32;
                vertCount++;
                verts[vertCount] = here + perp * w;
                uvs[vertCount] = new Vector2(1f, taper);
                cols[vertCount] = c32;
                vertCount++;
                written++;
                prevPoint = here;
            }
            if (written < 2)
            {
                vertCount = firstVert;
                return false;
            }
            for (int j = 0; j < written - 1; j++)
            {
                int v = firstVert + j * 2;
                tris[triCount++] = v;
                tris[triCount++] = v + 1;
                tris[triCount++] = v + 3;
                tris[triCount++] = v;
                tris[triCount++] = v + 3;
                tris[triCount++] = v + 2;
            }
            return true;
        }

        void EnsureCapacity(int points)
        {
            if (verts.Length >= points * 2) return;
            int cap = Mathf.Max(256, verts.Length / 2);
            while (cap < points) cap *= 2;
            verts = new Vector3[cap * 2];
            uvs = new Vector2[cap * 2];
            cols = new Color32[cap * 2];
            tris = new int[cap * 6];
        }
    }
}
