using UnityEngine;
using UnityEngine.Rendering;

namespace Incremental
{
    /// <summary>
    /// 15th pass: soft additive lines and ribbons with the trail ribbon's material (Incremental/DustTrail), no texture.
    /// Whoever draws one adds it every frame (in Update or an ability's Render) and this uploads them all in one mesh in
    /// its LateUpdate, which runs after every other script's. Used by the D2 crack lines, the meteor's fire tail (M2), its
    /// entry streak (M5) and the comet's two tails (S3). The colors go in linear, already × the effect intensity; a
    /// brightness above 2 is clipped by the halved vertex color encoding the dust meshes use.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class FxLines : MonoBehaviour
    {
        DynamicMesh mesh;
        MeshRenderer mr;
        DustVertex[] verts = new DustVertex[256];
        int quads;
        Vector2 min, max;

        /// <summary>Quads drawn in the last frame (checks).</summary>
        public int LastQuads { get; private set; }

        public void Init(Material trailMaterial, int sortingOrder)
        {
            mesh = new DynamicMesh("FxLines");
            GetComponent<MeshFilter>().sharedMesh = mesh.Mesh;
            mr = GetComponent<MeshRenderer>();
            if (trailMaterial != null) mr.sharedMaterial = trailMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.sortingOrder = sortingOrder;
            ResetBounds();
        }

        void ResetBounds()
        {
            quads = 0;
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
        }

        static Color32 Encode(Color c) => new Color32(Byte01(c.r * 0.5f), Byte01(c.g * 0.5f), Byte01(c.b * 0.5f), 255);
        static byte Byte01(float v) => (byte)(Mathf.Clamp01(v) * 255f + 0.5f);

        /// <summary>
        /// One straight piece from <paramref name="a"/> to <paramref name="b"/>, <paramref name="widthA"/> wide at a and
        /// <paramref name="widthB"/> at b (world units, full width), color <paramref name="colA"/> → <paramref name="colB"/>.
        /// </summary>
        public void Segment(Vector2 a, Vector2 b, float widthA, float widthB, Color colA, Color colB)
        {
            Vector2 ab = b - a;
            float len = ab.magnitude;
            if (len < 1e-6f || (widthA <= 0f && widthB <= 0f)) return;
            Vector2 side = new Vector2(-ab.y, ab.x) / len;
            Quad(a + side * (widthA * 0.5f), a - side * (widthA * 0.5f), b - side * (widthB * 0.5f), b + side * (widthB * 0.5f), Encode(colA), Encode(colB));
        }

        /// <summary>
        /// A ribbon through <paramref name="count"/> points, head first: width and color are asked per point (0 = head,
        /// 1 = tail end) from <paramref name="width"/> and <paramref name="color"/>. Neighbouring pieces share their edge
        /// so the ribbon has no seams.
        /// </summary>
        public void Ribbon(Vector2[] points, int count, System.Func<int, float> width, System.Func<int, Color> color)
        {
            if (points == null || count < 2) return;
            Vector2 prevL = default, prevR = default;
            Color32 prevC = default;
            for (int i = 0; i < count; i++)
            {
                Vector2 dir = i < count - 1 ? points[i + 1] - points[i] : points[i] - points[i - 1];
                if (i > 0 && i < count - 1) dir = points[i + 1] - points[i - 1];
                float len = dir.magnitude;
                Vector2 side = len > 1e-6f ? new Vector2(-dir.y, dir.x) / len : Vector2.up;
                float w = Mathf.Max(0f, width(i)) * 0.5f;
                Vector2 l = points[i] + side * w, r = points[i] - side * w;
                var c = Encode(color(i));
                if (i > 0) Quad(prevL, prevR, r, l, prevC, c);
                prevL = l;
                prevR = r;
                prevC = c;
            }
        }

        void Quad(Vector2 a0, Vector2 a1, Vector2 b1, Vector2 b0, Color32 ca, Color32 cb)
        {
            if ((quads + 1) * 4 > verts.Length) System.Array.Resize(ref verts, verts.Length * 2);
            int v = quads * 4;
            // UV.x runs −1..1 across the ribbon (the shader fades the edges).
            verts[v] = new DustVertex { pos = a0, col = ca, uv = new Vector3(-1f, 0f, 0f) };
            verts[v + 1] = new DustVertex { pos = a1, col = ca, uv = new Vector3(1f, 0f, 0f) };
            verts[v + 2] = new DustVertex { pos = b1, col = cb, uv = new Vector3(1f, 1f, 0f) };
            verts[v + 3] = new DustVertex { pos = b0, col = cb, uv = new Vector3(-1f, 1f, 0f) };
            quads++;
            Grow(a0); Grow(a1); Grow(b0); Grow(b1);
        }

        void Grow(Vector2 p)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        void LateUpdate()
        {
            if (mesh == null) return;
            LastQuads = quads;
            if (quads == 0)
            {
                mesh.Clear();
                ResetBounds();
                return;
            }
            mesh.EnsureQuads(quads);
            Vector2 c = (min + max) * 0.5f, size = max - min;
            mesh.Upload(verts, quads * 4, quads * 6, new Bounds(new Vector3(c.x, c.y, 0f), new Vector3(size.x + 1f, size.y + 1f, 1f)));
            ResetBounds();
        }
    }
}
