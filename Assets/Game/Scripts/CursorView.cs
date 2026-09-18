using UnityEngine;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// Gravity-radius ring (LineRenderer circle, effective radius) and mass gauge (radial-fill uGUI Image) that follow the cursor
    /// while holding. Gauge = mass / threshold of the target tier. Render only.
    /// </summary>
    public sealed class CursorView : MonoBehaviour
    {
        const int RingSegments = 64;

        GameRoot root;
        LineRenderer ring;
        double ringRadiusPx = -1;
        RectTransform canvasRect;
        RectTransform gaugeRoot;
        Image gaugeFill;

        public void Init(GameRoot gameRoot, Material material, RectTransform canvas, Sprite circle)
        {
            root = gameRoot;
            canvasRect = canvas;
            var p = root.gameParams;

            ring = gameObject.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.alignment = LineAlignment.TransformZ;
            ring.textureMode = LineTextureMode.Stretch;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            if (material != null) ring.sharedMaterial = material;
            ring.widthMultiplier = (float)(2.0 / p.pixelsPerUnit);
            ring.startColor = ring.endColor = new Color(1f, 1f, 1f, 0.35f);
            ring.sortingOrder = 5;
            ring.positionCount = RingSegments;
            SetRingRadius(p.gravityRadius);

            var size = new Vector2(72f, 72f);
            var center = new Vector2(0.5f, 0.5f);
            gaugeRoot = UIBuilder.CreateRect("MassGauge", canvasRect, center, center, Vector2.zero, size);
            UIBuilder.CreateImage("Bg", gaugeRoot, new Color(0f, 0f, 0f, 0.35f), center, center, Vector2.zero, size, circle);
            gaugeFill = UIBuilder.CreateImage("Fill", gaugeRoot, new Color(1f, 1f, 1f, 0.6f), center, center, Vector2.zero, size, circle);
            gaugeFill.type = Image.Type.Filled;
            gaugeFill.fillMethod = Image.FillMethod.Radial360;
            gaugeFill.fillOrigin = (int)Image.Origin360.Top;
            gaugeFill.fillClockwise = true;
            gaugeFill.fillAmount = 0f;
            SetVisible(false);
        }

        void LateUpdate()
        {
            bool show = root.Phase == GamePhase.Run && root.Run != null && root.Run.holding;
            SetVisible(show);
            if (!show) return;

            if (root.Effective.gravityRadius != ringRadiusPx) SetRingRadius(root.Effective.gravityRadius);
            var ps = root.ReadPointer();
            Vector2 w = root.ToWorld(ps.screenPos);
            transform.position = new Vector3(w.x, w.y, 0f);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, ps.screenPos, null, out var local))
                gaugeRoot.anchoredPosition = local;
            gaugeFill.fillAmount = (float)root.GaugeFill();
        }

        /// <summary>Rebuilds the ring for the effective gravity radius (the gravity_radius upgrade changes it between runs).</summary>
        void SetRingRadius(double radiusPx)
        {
            ringRadiusPx = radiusPx;
            float r = (float)(radiusPx / root.gameParams.pixelsPerUnit);
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i / (float)RingSegments * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
        }

        void SetVisible(bool v)
        {
            if (ring.enabled != v) ring.enabled = v;
            if (gaugeRoot.gameObject.activeSelf != v) gaugeRoot.gameObject.SetActive(v);
        }
    }
}
