using UnityEngine;
using UnityEngine.Rendering;

namespace Incremental
{
    /// <summary>Draws every dust as one quad in a single dynamic mesh (vertex colors). Render only; rebuilt each frame.</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class DustMeshRenderer : MonoBehaviour
    {
        DustField field;
        GameParams p;
        Mesh mesh;
        Vector3[] verts = new Vector3[0];
        Color32[] cols = new Color32[0];
        int[] tris = new int[0];

        public void Init(DustField dustField, GameParams gameParams, Material material)
        {
            field = dustField;
            p = gameParams;
            mesh = new Mesh { name = "DustMesh", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = GetComponent<MeshRenderer>();
            if (material != null) mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.sortingOrder = 0;
            UIBuilder.UseWhiteTexture(mr);
        }

        /// <summary>The dust is drawn during runs only; between runs the solar system and the tree take the screen.</summary>
        public void SetVisible(bool visible)
        {
            var mr = GetComponent<MeshRenderer>();
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

            float h = (float)(p.dustSizePx / p.pixelsPerUnit) * 0.5f;
            var items = field.Items;
            for (int i = 0; i < n; i++)
            {
                Vector2 c = items[i].pos;
                int v = i * 4;
                verts[v] = new Vector3(c.x - h, c.y - h, 0f);
                verts[v + 1] = new Vector3(c.x - h, c.y + h, 0f);
                verts[v + 2] = new Vector3(c.x + h, c.y + h, 0f);
                verts[v + 3] = new Vector3(c.x + h, c.y - h, 0f);
                Color32 col = items[i].color;
                cols[v] = col; cols[v + 1] = col; cols[v + 2] = col; cols[v + 3] = col;
            }

            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontResetBoneBounds;
            mesh.Clear(true);
            mesh.SetVertices(verts, 0, n * 4, flags);
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
