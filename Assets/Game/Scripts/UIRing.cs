using UnityEngine;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>uGUI circle outline (ring of the skill tree). Radius and thickness are in the RectTransform's local units.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UIRing : MaskableGraphic
    {
        [SerializeField] float radius = 100f;
        [SerializeField] float thickness = 2f;
        [SerializeField] int segments = 96;

        public void Set(float r, float width, int segs)
        {
            radius = r;
            thickness = width;
            segments = Mathf.Max(8, segs);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float inner = Mathf.Max(0f, radius - thickness * 0.5f);
            float outer = radius + thickness * 0.5f;
            Color32 c = color;
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
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
