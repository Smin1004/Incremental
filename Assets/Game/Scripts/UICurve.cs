using UnityEngine;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// uGUI polyline of a given width (the curved connections of the skill tree, 26 §10.1). Points are in the
    /// RectTransform's local units; the strip is one quad per segment, joined at the points by the mean direction.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UICurve : MaskableGraphic
    {
        Vector2[] points = new Vector2[0];
        float width = 2f;

        public void SetPoints(Vector2[] pts, float lineWidth)
        {
            points = pts ?? new Vector2[0];
            width = lineWidth;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int n = points.Length;
            if (n < 2 || width <= 0f) return;
            Color32 c = color;
            float half = width * 0.5f;
            for (int i = 0; i < n; i++)
            {
                Vector2 dir;
                if (i == 0) dir = points[1] - points[0];
                else if (i == n - 1) dir = points[n - 1] - points[n - 2];
                else dir = (points[i + 1] - points[i]).normalized + (points[i] - points[i - 1]).normalized;
                if (dir.sqrMagnitude < 1e-8f) dir = Vector2.right;
                dir.Normalize();
                var normal = new Vector2(-dir.y, dir.x) * half;
                vh.AddVert(points[i] - normal, c, Vector2.zero);
                vh.AddVert(points[i] + normal, c, Vector2.zero);
                if (i == 0) continue;
                int v = i * 2;
                vh.AddTriangle(v - 2, v - 1, v + 1);
                vh.AddTriangle(v - 2, v + 1, v);
            }
        }
    }
}
