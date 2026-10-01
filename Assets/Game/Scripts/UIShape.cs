using UnityEngine;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// One shape drawn by the Incremental/UIPanel shader (26 §4): a quad the size of the RectTransform plus
    /// <see cref="Pad"/> on every side for the shadow and outer light. The mesh tells the shader where each pixel is
    /// (px from the rect center), how large the rect is, how much of a bar is filled and a 0..1 state for hover and
    /// flashes; the look itself is in the material (<see cref="UIStyle.MaterialFor"/>). The canvas has to pass
    /// TexCoord1 (UIBuilder.CreateCanvas does).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UIShape : MaskableGraphic
    {
        [SerializeField] float pad;
        [SerializeField] float fill = 1f;
        [SerializeField] float state;

        /// <summary>Extra room around the rect, px.</summary>
        public float Pad
        {
            get => pad;
            set { if (!Mathf.Approximately(pad, value)) { pad = value; SetVerticesDirty(); } }
        }

        /// <summary>0..1 share of a bar that is filled (bars only).</summary>
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

        /// <summary>0..1: hover, a flash. The shader turns it into light (× effect intensity).</summary>
        public float State
        {
            get => state;
            set
            {
                float v = Mathf.Clamp01(value);
                if (Mathf.Approximately(v, state)) return;
                state = v;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Vector2 half = r.size * 0.5f;
            Vector2 c = r.center;
            Vector2 ext = half + new Vector2(pad, pad);
            var box = new Vector4(half.x, half.y, fill, state);
            Color32 col = color;
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2((i == 0 || i == 1) ? -ext.x : ext.x, (i == 0 || i == 3) ? -ext.y : ext.y);
                vh.AddVert(new Vector3(c.x + corner.x, c.y + corner.y, 0f), col, new Vector4(corner.x, corner.y, 0f, 0f), box,
                    Vector3.back, new Vector4(1f, 0f, 0f, -1f));
            }
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
        }
    }
}
