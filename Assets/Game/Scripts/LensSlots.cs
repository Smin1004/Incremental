using UnityEngine;
using UnityEngine.Rendering;

namespace Incremental
{
    /// <summary>
    /// 26 §12.2: the four lenses the Starfield, Dust and DustTrail shaders read (Lens.hlsl). 0 the black hole, 1 the
    /// gravity source, 2 the implode wave, 3 the bundle ripple. Whoever owns a slot sets it once a frame (or clears it);
    /// the values go to the shaders as global arrays just before the frame is drawn, so the order in which the owners
    /// run does not matter. Render only: nothing in the simulation reads them.
    /// </summary>
    public static class LensSlots
    {
        public const int BlackHole = 0, Gravity = 1, Implode = 2, Bundle = 3;
        public const int Count = 4;
        public const float KindHole = 0f, KindBulge = 1f, KindBand = 2f;

        static readonly int IdA = Shader.PropertyToID("_LensA");
        static readonly int IdB = Shader.PropertyToID("_LensB");
        static readonly int IdShape = Shader.PropertyToID("_BHLensShape");

        static readonly Vector4[] a = new Vector4[Count];
        static readonly Vector4[] b = new Vector4[Count];
        static Vector4 holeShape;
        static bool dirty = true;
        static bool hooked;

        /// <summary>
        /// Slot <paramref name="slot"/>: centre and radius (world), push (in radii, or band widths for a band), reach in
        /// radii, twist in radians at the edge, band width as a share of the radius, kind.
        /// </summary>
        public static void Set(int slot, Vector2 centre, float radius, float push, float reach, float twist, float bandWidth, float kind)
        {
            Hook();
            var na = new Vector4(centre.x, centre.y, radius, push);
            var nb = new Vector4(reach, twist, bandWidth, kind);
            if (a[slot] == na && b[slot] == nb) return;
            a[slot] = na;
            b[slot] = nb;
            dirty = true;
        }

        public static void Clear(int slot)
        {
            Hook();
            if (a[slot] == Vector4.zero && b[slot] == Vector4.zero) return;
            a[slot] = Vector4.zero;
            b[slot] = Vector4.zero;
            dirty = true;
        }

        /// <summary>The black hole's light ring and disc clearing (24 §24.3), read with slot 0.</summary>
        public static void SetHoleShape(Vector4 shape)
        {
            Hook();
            if (holeShape == shape) return;
            holeShape = shape;
            dirty = true;
        }

        /// <summary>True while slot <paramref name="slot"/> moves anything (captures and checks).</summary>
        public static bool IsOn(int slot) => a[slot].w > 0f || b[slot].y != 0f;

        public static Vector4 A(int slot) => a[slot];

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginContextRendering += (ctx, cams) => Flush();
            Flush();
        }

        /// <summary>Hands the slots to the shaders when one of them changed.</summary>
        public static void Flush()
        {
            if (!dirty) return;
            dirty = false;
            Shader.SetGlobalVectorArray(IdA, a);
            Shader.SetGlobalVectorArray(IdB, b);
            Shader.SetGlobalVector(IdShape, holeShape);
        }

        /// <summary>Every slot off (a run ends, the domain reloads).</summary>
        public static void ClearAll()
        {
            for (int i = 0; i < Count; i++) Clear(i);
            SetHoleShape(Vector4.zero);
        }
    }
}
