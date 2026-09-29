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
    /// which is where the eye is. Everything goes into one dynamic mesh, so the whole thing is one draw call.
    ///
    /// 22 §7: the budget is filled by counting the candidates per kind and only ordering the one kind the budget runs out
    /// in, instead of sorting every candidate; the vertices go to the GPU in one copy (<see cref="DynamicMesh"/>); and
    /// nothing is built while the mesh is not drawn. Render only.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class DustTrailRenderer : MonoBehaviour
    {
        GameRoot root;
        DustField field;
        GameParams p;
        DynamicMesh mesh;
        MeshRenderer mr;
        DustVertex[] verts = new DustVertex[0];
        int[] tris = new int[0];
        int vertCount, triCount;

        /// <summary>The dust indices whose ribbons are drawn this frame, filled by <see cref="PickCandidates"/>.</summary>
        readonly List<int> picked = new List<int>();
        /// <summary>Scratch for the one kind the budget runs out in.</summary>
        readonly List<int> boundary = new List<int>();
        int[] perKind = new int[0];
        Vector2[] points = new Vector2[0];
        Vector2 gravity;
        System.Comparison<int> nearestFirst;

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
            mesh = new DynamicMesh("DustTrailMesh");
            GetComponent<MeshFilter>().sharedMesh = mesh.Mesh;
            mr = GetComponent<MeshRenderer>();
            if (material != null) mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // Under the dust quads, so a bright dust head still reads as the head of its own trail.
            mr.sortingOrder = -1;
            perKind = new int[Mathf.Max(1, p.DustKindCount)];
            nearestFirst = CompareByDistance;
            // 22 §7: as with the dust mesh, size for the worst case once instead of growing during a run.
            points = new Vector2[Mathf.Max(2, p.trailPoints + 1)];
            EnsureCapacity(Mathf.Max(1, p.trailBudget) * (Mathf.Max(0, p.trailPoints) + 1));
        }

        public void SetVisible(bool visible)
        {
            if (mr.enabled != visible) mr.enabled = visible;
        }

        int CompareByDistance(int a, int b)
        {
            var items = field.Items;
            float da = (items[a].pos - gravity).sqrMagnitude, db = (items[b].pos - gravity).sqrMagnitude;
            return da.CompareTo(db);
        }

        void LateUpdate()
        {
            if (field == null || mesh == null) return;
            if (!mr.enabled)
            {
                DrawnTrails = 0;
                mesh.Clear();
                return;
            }
            rebuildWatch.Restart();
            Rebuild();
            rebuildWatch.Stop();
            rebuildMsAcc += rebuildWatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// 22 §5 budget rule without a sort over everything: count the candidates of each kind, walk the kinds from the
        /// heaviest down taking whole kinds while they fit, and order only the kind the budget runs out in, by distance to
        /// the gravity source. That kind is the only one whose members compete with each other.
        /// </summary>
        void PickCandidates(int budget)
        {
            picked.Clear();
            var items = field.Items;
            int n = field.Count;
            int kinds = perKind.Length;
            for (int k = 0; k < kinds; k++) perKind[k] = 0;
            for (int i = 0; i < n; i++)
            {
                int kind = items[i].kind;
                if (kind > 0 && kind < kinds && items[i].trailFill > 0) perKind[kind]++;
            }
            // Which kind does the budget run out in, and how much room is left for it?
            int cutKind = 0, room = budget;
            for (int k = kinds - 1; k >= 1; k--)
            {
                if (perKind[k] <= room)
                {
                    room -= perKind[k];
                    continue;
                }
                cutKind = k;
                break;
            }
            boundary.Clear();
            for (int i = 0; i < n; i++)
            {
                int kind = items[i].kind;
                if (kind <= 0 || kind >= kinds || items[i].trailFill <= 0) continue;
                if (kind > cutKind) picked.Add(i);
                else if (kind == cutKind) boundary.Add(i);
            }
            if (cutKind == 0 || room <= 0) return;
            if (boundary.Count > room) boundary.Sort(nearestFirst);
            int take = Mathf.Min(room, boundary.Count);
            for (int i = 0; i < take; i++) picked.Add(boundary[i]);
        }

        void Rebuild()
        {
            vertCount = 0;
            triCount = 0;
            DrawnTrails = 0;
            int trailLen = field.TrailLen;
            int budget = Mathf.Max(0, p.trailBudget);
            if (trailLen <= 0 || budget == 0 || field.Count == 0)
            {
                mesh.Clear();
                return;
            }
            if (perKind.Length != Mathf.Max(1, p.DustKindCount)) perKind = new int[Mathf.Max(1, p.DustKindCount)];
            if (points.Length < trailLen + 1) points = new Vector2[trailLen + 1];

            gravity = root.EndingActive && root.Phase == GamePhase.Run ? root.EndingCenter : root.GravityWorld;
            PickCandidates(budget);
            if (picked.Count == 0)
            {
                mesh.Clear();
                return;
            }

            // Worst case every ribbon is full length: one point for "now" plus every kept position, two vertices each.
            int maxPoints = picked.Count * (trailLen + 1);
            EnsureCapacity(maxPoints);

            float ppu = (float)p.pixelsPerUnit;
            float halo = (float)(p.dustHaloPx / ppu) * 0.5f;
            float share = (float)p.trailWidthShare;
            Color colA = root.Backdrop.DustColorA.linear;
            Color colB = root.Backdrop.DustColorB.linear;
            var items = field.Items;

            for (int c = 0; c < picked.Count; c++)
            {
                int i = picked[c];
                ref Dust d = ref items[i];
                var kindDef = p.DustKind(d.kind);
                float headWidth = halo * d.size * (kindDef != null ? (float)kindDef.sizeMult : 1f) * share;
                float bright = d.bright * (kindDef != null ? (float)kindDef.brightMult : 1f);
                float t = d.tint;
                float r = Mathf.LerpUnclamped(colA.r, colB.r, t);
                float g = Mathf.LerpUnclamped(colA.g, colB.g, t);
                float bl = Mathf.LerpUnclamped(colA.b, colB.b, t);
                if (AppendRibbon(i, d.trailFill, headWidth, r, g, bl, bright)) DrawnTrails++;
            }

            if (vertCount == 0 || triCount == 0)
            {
                mesh.Clear();
                return;
            }
            mesh.UploadIndices(triCount);
            Rect a = field.Area;
            var bounds = new Bounds(new Vector3(a.center.x, a.center.y, 0f), new Vector3(a.width + 1f, a.height + 1f, 1f));
            mesh.Upload(verts, vertCount, triCount, bounds);
        }

        /// <summary>
        /// One ribbon: the dust's position now, then the positions kept for it, newest first. Width and brightness go
        /// from full at the head to 0 at the tail. Returns false when the path is too short to draw.
        /// </summary>
        bool AppendRibbon(int index, int fill, float headWidth, float cr, float cg, float cb, float bright)
        {
            if (fill < 1 || headWidth <= 0f) return false;
            // Read the path once: the ribbon needs each point's neighbours, and TrailPoint walks the ring buffer.
            points[0] = field.Items[index].pos;
            int count = 1;
            for (int j = 0; j < fill; j++)
            {
                Vector2 here = field.TrailPoint(index, j);
                // A dust that has not moved since the last sample would give a zero-length segment and a NaN normal.
                if ((here - points[count - 1]).sqrMagnitude < 1e-10f) break;
                points[count++] = here;
            }
            if (count < 2) return false;

            int firstVert = vertCount;
            float last = count - 1;
            for (int j = 0; j < count; j++)
            {
                Vector2 here = points[j];
                Vector2 ahead = points[j > 0 ? j - 1 : 0];
                Vector2 behind = points[j + 1 < count ? j + 1 : j];
                Vector2 dir = ahead - behind;
                if (dir.sqrMagnitude < 1e-12f) dir = new Vector2(1f, 0f);
                dir.Normalize();
                float px = -dir.y, py = dir.x;

                float taper = 1f - j / last;
                float w = headWidth * taper;
                float k = Mathf.Min(bright * taper, 2f) * 0.5f;
                var c32 = new Color32(Byte01(cr * k), Byte01(cg * k), Byte01(cb * k), 255);

                verts[vertCount].pos = new Vector3(here.x - px * w, here.y - py * w, 0f);
                verts[vertCount].uv = new Vector3(-1f, taper, 0f);
                verts[vertCount].col = c32;
                vertCount++;
                verts[vertCount].pos = new Vector3(here.x + px * w, here.y + py * w, 0f);
                verts[vertCount].uv = new Vector3(1f, taper, 0f);
                verts[vertCount].col = c32;
                vertCount++;
            }
            for (int j = 0; j < count - 1; j++)
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

        static byte Byte01(float v) => (byte)(Mathf.Clamp01(v) * 255f + 0.5f);

        void EnsureCapacity(int points)
        {
            tris = mesh.EnsureIndexed(points * 2, points * 6);
            if (verts.Length >= points * 2) return;
            int cap = Mathf.Max(256, verts.Length / 2);
            while (cap < points) cap *= 2;
            verts = new DustVertex[cap * 2];
        }
    }
}
