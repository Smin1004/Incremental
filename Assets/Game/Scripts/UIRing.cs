using UnityEngine;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// uGUI circle outline (rings of the skill tree, the mass gauge). Radius and thickness are in the RectTransform's local
    /// units. <see cref="Fill"/> below 1 draws an arc from the top, clockwise.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UIRing : MaskableGraphic
    {
        [SerializeField] float radius = 100f;
        [SerializeField] float thickness = 2f;
        [SerializeField] int segments = 96;
        [SerializeField] float fill = 1f;

        public float Radius => radius;

        public void Set(float r, float width, int segs)
        {
            radius = r;
            thickness = width;
            segments = Mathf.Max(8, segs);
            SetVerticesDirty();
        }

        /// <summary>0..1 share of the circle drawn, from the top, clockwise.</summary>
        public float Fill
        {
            get => fill;
            set
            {
                float v = Mathf.Clamp01(value);
                if (Mathf.Approximately(v, fill)) return;
                fill = v;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (fill <= 0f) return;
            float inner = Mathf.Max(0f, radius - thickness * 0.5f);
            float outer = radius + thickness * 0.5f;
            Color32 c = color;
            int segs = Mathf.Max(1, Mathf.CeilToInt(segments * fill));
            float sweep = Mathf.PI * 2f * fill;
            for (int i = 0; i <= segs; i++)
            {
                // Full circle: counter-clockwise from +x (as before). Arc: clockwise from the top.
                float a = fill >= 1f ? i / (float)segs * Mathf.PI * 2f : Mathf.PI * 0.5f - i / (float)segs * sweep;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                vh.AddVert(dir * inner, c, Vector2.zero);
                vh.AddVert(dir * outer, c, Vector2.zero);
                if (i == 0) continue;
                int v = i * 2;
                vh.AddTriangle(v - 2, v - 1, v + 1);
                vh.AddTriangle(v - 2, v + 1, v);
            }
        }
    }
}
