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
    /// which is where the eye is. The last places of the budget are drawn fainter (trailBudgetFadeShare), so a ribbon
    /// fades in as its dust moves up that order instead of appearing at full length; and a dust that was just created
    /// has no ribbon to show yet and fades in with it. Everything goes into one dynamic mesh, so the whole thing is one
    /// draw call.
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
        /// <summary>How bright each of <see cref="picked"/> is drawn, 0..1: below 1 near the end of the budget.</summary>
        readonly List<float> pickedFade = new List<float>();
        int[] perKind = new int[0];
        /// <summary>Per kind, this frame: how many ribbons of heavier kinds come before it, and what is done with it.</summary>
        int[] kindStart = new int[0];
        byte[] kindPlan = new byte[0];
        /// <summary>Scratch per kind, for the kinds that have to be put in order of distance this frame.</summary>
        List<int>[] ordered = new List<int>[0];

        const byte PlanSkip = 0, PlanFull = 1, PlanOrdered = 2;
        Vector2[] points = new Vector2[0];
        /// <summary>Age of each kept point, in sample intervals, so a skipped duplicate does not shift the taper.</summary>
        int[] ages = new int[0];
        Vector2 gravity;
        float phase;
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
            EnsureKinds();
            nearestFirst = CompareByDistance;
            // 22 §7: as with the dust mesh, size for the worst case once instead of growing during a run.
            points = new Vector2[Mathf.Max(2, p.trailPoints + 1)];
            ages = new int[points.Length];
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

        /// <summary>One entry per dust kind in the per-kind scratch arrays.</summary>
        void EnsureKinds()
        {
            int kinds = Mathf.Max(1, p.DustKindCount);
            if (perKind.Length == kinds) return;
            perKind = new int[kinds];
            kindStart = new int[kinds];
            kindPlan = new byte[kinds];
            ordered = new List<int>[kinds];
            for (int k = 0; k < kinds; k++) ordered[k] = new List<int>();
        }

        /// <summary>
        /// 22 §5 budget rule without a sort over everything. The order the budget is handed out in is the heavier kinds
        /// first and, within a kind, the dust nearer to the gravity source first. The candidates of each kind are
        /// counted, which gives every kind the place in that order where it starts; a kind that ends before the budget
        /// does is taken whole without being ordered, a kind that starts after it is left out, and only a kind the
        /// budget (or its faded end) runs out in is put in order of distance.
        ///
        /// The last <paramref name="fadeCount"/> places of the budget are drawn fainter, down to nothing at the very
        /// last. A ribbon that had its full length the moment its dust got a place used to appear all at once; now it
        /// comes in as the dust moves up the order. With a fadeCount of 0 this is the hard cut of 22 §5.
        /// </summary>
        void PickCandidates(int budget, int fadeCount)
        {
            picked.Clear();
            pickedFade.Clear();
            var items = field.Items;
            int n = field.Count;
            int kinds = perKind.Length;
            for (int k = 0; k < kinds; k++) perKind[k] = 0;
            for (int i = 0; i < n; i++)
            {
                int kind = items[i].kind;
                if (kind > 0 && kind < kinds && items[i].trailFill > 0) perKind[kind]++;
            }
            int start = 0;
            for (int k = kinds - 1; k >= 1; k--)
            {
                kindStart[k] = start;
                if (perKind[k] == 0 || start >= budget) kindPlan[k] = PlanSkip;
                else if (start + perKind[k] <= budget - fadeCount) kindPlan[k] = PlanFull;
                else
                {
                    kindPlan[k] = PlanOrdered;
                    ordered[k].Clear();
                }
                start += perKind[k];
            }
            for (int i = 0; i < n; i++)
            {
                int kind = items[i].kind;
                if (kind <= 0 || kind >= kinds || items[i].trailFill <= 0) continue;
                if (kindPlan[kind] == PlanFull)
                {
                    picked.Add(i);
                    pickedFade.Add(1f);
                }
                else if (kindPlan[kind] == PlanOrdered) ordered[kind].Add(i);
            }
            for (int k = kinds - 1; k >= 1; k--)
            {
                if (kindPlan[k] != PlanOrdered) continue;
                var list = ordered[k];
                list.Sort(nearestFirst);
                int take = Mathf.Min(list.Count, budget - kindStart[k]);
                for (int j = 0; j < take; j++)
                {
                    int place = kindStart[k] + j;
                    picked.Add(list[j]);
                    pickedFade.Add(fadeCount > 0 ? Mathf.Clamp01((budget - place) / (float)fadeCount) : 1f);
                }
            }
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
            EnsureKinds();
            if (points.Length < trailLen + 1)
            {
                points = new Vector2[trailLen + 1];
                ages = new int[points.Length];
            }

            gravity = root.EndingActive && root.Phase == GamePhase.Run ? root.EndingCenter : root.GravityWorld;
            PickCandidates(budget, Mathf.RoundToInt(budget * Mathf.Clamp01((float)p.trailBudgetFadeShare)));
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
            // 22 §5 "그 먼지 표시 크기의 60%": a moving dust is drawn as a streak that narrows across its travel, so the
            // ribbon follows that thickness and not the round halo, or the trail comes out fatter than the dust leaving it.
            float stretchMax = Mathf.Max(1f, (float)p.dustStretchMax);
            float exposure = (float)p.dustStreakExposureSec;
            float widthAtFull = (float)p.dustStreakWidth;
            phase = field.TrailPhase;
            Color colA = root.Backdrop.DustColorA.linear;
            Color colB = root.Backdrop.DustColorB.linear;
            var items = field.Items;
            // 26 §5.5: the ribbons dim with the dust they belong to, by density and near the forming body.
            bool fix = root.ExposureFix;
            float density = fix ? DustMeshRenderer.DensityDim(p, field.Count) : 1f;
            bool holding = root.Phase == GamePhase.Run && root.Run != null && root.Run.holding;
            float centerR = fix && holding && p.dustCenterDimRadii > 0
                ? (float)(root.RunBodyPx(root.Cursor.FormingDiameterPx) * 0.5 * p.dustCenterDimRadii / ppu)
                : 0f;
            float centerDim = (float)p.dustCenterDim;

            for (int c = 0; c < picked.Count; c++)
            {
                int i = picked[c];
                ref Dust d = ref items[i];
                var kindDef = p.DustKind(d.kind);
                // A ribboned dust is drawn as a round dot of half-size h, so a head of share x h starts inside that
                // dot and the ribbon slides out from under it with no shoulder to see.
                float h = halo * d.size * (kindDef != null ? (float)kindDef.sizeMult : 1f);
                float headWidth = h * share;
                // Fainter near the end of the budget, and while the dust itself is still fading in.
                float bright = d.bright * (kindDef != null ? (float)kindDef.brightMult : 1f) * pickedFade[c] * d.born * density;
                // 15th pass: a flaring dust (a D2 piece) leaves a brighter ribbon while it flares.
                if (d.flare > 0f) bright *= 1f + d.flareGain * root.EffectIntensity;
                if (centerR > 0f)
                {
                    float dc = (d.pos - gravity).magnitude;
                    if (dc < centerR) bright *= Mathf.Lerp(centerDim, 1f, dc / centerR);
                }
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
        /// 22 §5: how much of the ribbon is left at a point of the given age, where age is measured in sample intervals.
        /// The newest kept position is <paramref name="phase"/> intervals old and the oldest is fill − 1 + phase, so the
        /// oldest always sits at exactly 0 and fades out as the phase runs, instead of being dropped while still visible.
        /// That step is what made the tail blink. A pure function so the test can read the rule.
        /// </summary>
        public static float Taper(int index, int fill, float phase)
        {
            if (index <= 0) return 1f;
            if (fill <= 0) return 0f;
            return Mathf.Clamp01(1f - (phase + index - 1) / fill);
        }

        /// <summary>
        /// One ribbon: the dust's position now, then the positions kept for it, newest first. Width and brightness go
        /// from full at the head to 0 at the tail. Returns false when the path is too short to draw.
        /// </summary>
        bool AppendRibbon(int index, int fill, float headWidth, float cr, float cg, float cb, float bright)
        {
            if (fill < 1 || headWidth <= 0f) return false;
            // Read the path once: the ribbon needs each point's neighbours, and TrailPoint walks the ring buffer.
            // A point on top of the one before it would give a zero-length segment and a NaN normal, so it is skipped —
            // never bailed out of. The frame right after a sample has the dust exactly on its newest kept position, and
            // bailing out there made the whole ribbon vanish for that frame, once every trailSampleSec.
            points[0] = field.Items[index].pos;
            ages[0] = 0;
            int count = 1;
            for (int j = 0; j < fill; j++)
            {
                Vector2 here = field.TrailPoint(index, j);
                if ((here - points[count - 1]).sqrMagnitude < 1e-10f) continue;
                points[count] = here;
                ages[count] = j + 1;
                count++;
            }
            if (count < 2) return false;

            int firstVert = vertCount;
            for (int j = 0; j < count; j++)
            {
                Vector2 here = points[j];
                Vector2 ahead = points[j > 0 ? j - 1 : 0];
                Vector2 behind = points[j + 1 < count ? j + 1 : j];
                Vector2 dir = ahead - behind;
                if (dir.sqrMagnitude < 1e-12f) dir = new Vector2(1f, 0f);
                dir.Normalize();
                float px = -dir.y, py = dir.x;

                float taper = Taper(ages[j], fill, phase);
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
