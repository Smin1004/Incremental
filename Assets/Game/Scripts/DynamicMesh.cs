using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Incremental
{
    /// <summary>
    /// One vertex of the dust and trail meshes: position, colour and a three-component uv, interleaved the way the GPU
    /// wants them. 22 §7: writing one array of these is a single copy into the vertex buffer, where the
    /// SetVertices / SetUVs / SetColors triple of 14 §5 was three copies plus a Mesh.Clear every frame.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DustVertex
    {
        public Vector3 pos;
        public Color32 col;
        public Vector3 uv;
    }

    /// <summary>
    /// A dynamic triangle mesh filled from a managed <see cref="DustVertex"/> array once a frame (22 §7).
    ///
    /// The buffers are sized to a capacity that only ever grows, and the index buffer for whole quads is written once
    /// when it does; a frame then costs one <c>SetVertexBufferData</c> and one <c>SetSubMesh</c>, and vertices past the
    /// live count are simply not referenced by any index. That is what the 14 §5 renderer paid most of its time for:
    /// <c>Mesh.Clear</c> plus four separate native copies, every frame, for every dust on screen.
    /// </summary>
    public sealed class DynamicMesh
    {
        const MeshUpdateFlags Flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices |
                                      MeshUpdateFlags.DontResetBoneBounds | MeshUpdateFlags.DontNotifyMeshUsers;

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 3),
        };

        readonly Mesh mesh;
        int vertexCapacity;
        int indexCapacity;
        int[] indices = new int[0];
        bool empty = true;

        public Mesh Mesh => mesh;

        public DynamicMesh(string name)
        {
            mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
        }

        /// <summary>
        /// Makes room for <paramref name="quads"/> quads and fills the index buffer with two triangles each, in the
        /// vertex order (0, 1, 2) (0, 2, 3). Only does anything when the capacity has to grow.
        /// </summary>
        public void EnsureQuads(int quads)
        {
            if (quads * 4 <= vertexCapacity) return;
            int cap = Mathf.Max(256, vertexCapacity / 4);
            while (cap < quads) cap *= 2;
            SetCapacity(cap * 4, cap * 6);
            for (int i = 0; i < cap; i++)
            {
                int v = i * 4, t = i * 6;
                indices[t] = v; indices[t + 1] = v + 1; indices[t + 2] = v + 2;
                indices[t + 3] = v; indices[t + 4] = v + 2; indices[t + 5] = v + 3;
            }
            mesh.SetIndexBufferData(indices, 0, 0, indexCapacity, Flags);
        }

        /// <summary>
        /// Makes room for <paramref name="vertexCount"/> vertices and <paramref name="indexCount"/> indices whose pattern
        /// the caller writes itself (the trail ribbons, whose strips are not whole quads). Returns the index array to
        /// fill, which has to be handed back to <see cref="UploadIndices"/>.
        /// </summary>
        public int[] EnsureIndexed(int vertexCount, int indexCount)
        {
            if (vertexCount > vertexCapacity || indexCount > indexCapacity)
            {
                int vc = Mathf.Max(512, vertexCapacity);
                while (vc < vertexCount) vc *= 2;
                int ic = Mathf.Max(768, indexCapacity);
                while (ic < indexCount) ic *= 2;
                SetCapacity(vc, ic);
            }
            return indices;
        }

        void SetCapacity(int verts, int indexCount)
        {
            vertexCapacity = verts;
            indexCapacity = indexCount;
            if (indices.Length < indexCapacity) indices = new int[indexCapacity];
            mesh.SetVertexBufferParams(vertexCapacity, Layout);
            mesh.SetIndexBufferParams(indexCapacity, IndexFormat.UInt32);
        }

        /// <summary>Uploads a caller-built index pattern (after <see cref="EnsureIndexed"/>).</summary>
        public void UploadIndices(int count) => mesh.SetIndexBufferData(indices, 0, 0, Mathf.Min(count, indexCapacity), Flags);

        /// <summary>
        /// Uploads <paramref name="vertexCount"/> vertices and draws <paramref name="indexCount"/> indices of them.
        /// <paramref name="bounds"/> is set by hand, because recalculating it would walk every vertex again.
        /// </summary>
        public void Upload(DustVertex[] verts, int vertexCount, int indexCount, Bounds bounds)
        {
            if (vertexCount <= 0 || indexCount <= 0)
            {
                Clear();
                return;
            }
            empty = false;
            mesh.SetVertexBufferData(verts, 0, 0, Mathf.Min(vertexCount, vertexCapacity), 0, Flags);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, Mathf.Min(indexCount, indexCapacity)) { bounds = bounds }, Flags);
            mesh.bounds = bounds;
        }

        /// <summary>Draws nothing. Cheap to call every frame: an empty submesh, not a buffer teardown.</summary>
        public void Clear()
        {
            if (empty) return;
            empty = true;
            if (vertexCapacity == 0) return;
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, 0), Flags);
        }
    }
}
