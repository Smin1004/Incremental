using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// What follows the cursor while holding: the gravity-radius ring (LineRenderer circle, effective radius), the forming
    /// body (14 §4) and the mass gauge as a thin ring around it. The body wears the look of the tier a release would make
    /// now (a dark clump below tier 1) and grows from that tier's display size toward the next one with the mass; crossing a
    /// threshold pops it into the next look. On creation the body is handed to the planet pool (same surface, same size)
    /// and a new clump starts. Render only.
    /// </summary>
    public sealed class CursorView : MonoBehaviour
    {
        const int RingSegments = 64;

        GameRoot root;
        GameParams p;
        LineRenderer ring;
        double ringRadiusPx = -1;
        RectTransform canvasRect;
        RectTransform gaugeRoot;
        UIRing gaugeBg, gaugeFill;
        PlanetVisual body;
        bool shown;
        int shownTier = -1;      // 0 = clump, else the tier whose look is on
        float seed;
        float sizePx;
        float popAge = float.MaxValue;

        /// <summary>Diameter the forming body is drawn at now (reference px), before the pop.</summary>
        public double FormingDiameterPx => sizePx;

        public void Init(GameRoot gameRoot, Material lineMaterial, RectTransform canvas)
        {
            root = gameRoot;
            p = root.gameParams;
            canvasRect = canvas;

            ring = gameObject.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.alignment = LineAlignment.TransformZ;
            ring.textureMode = LineTextureMode.Stretch;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            if (lineMaterial != null) ring.sharedMaterial = lineMaterial;
            ring.widthMultiplier = (float)(2.0 / p.pixelsPerUnit);
            ring.startColor = ring.endColor = new Color(1f, 1f, 1f, 0.35f);
            ring.sortingOrder = 5;
            UIBuilder.UseWhiteTexture(ring);
            ring.positionCount = RingSegments;
            SetRingRadius(p.gravityRadius);

            body = PlanetVisual.Create("FormingBody", transform.parent, root.planetMaterial, 11);

            var center = new Vector2(0.5f, 0.5f);
            gaugeRoot = UIBuilder.CreateRect("MassGauge", canvasRect, center, center, Vector2.zero, Vector2.zero);
            gaugeBg = MakeGaugeRing("Bg", (float)p.gaugeBgAlpha);
            gaugeFill = MakeGaugeRing("Fill", (float)p.gaugeFillAlpha);
            SetVisible(false);
        }

        UIRing MakeGaugeRing(string name, float alpha)
        {
            var c = new Vector2(0.5f, 0.5f);
            var rt = UIBuilder.CreateRect(name, gaugeRoot, c, c, Vector2.zero, Vector2.zero);
            var r = rt.gameObject.AddComponent<UIRing>();
            r.raycastTarget = false;
            r.color = new Color(1f, 1f, 1f, alpha);
            return r;
        }

        /// <summary>
        /// Target diameter of the forming body (14 §4): from the current look's size toward the next one as the mass goes
        /// from the current threshold to the next. Below tier 1 the "current" is the clump (threshold 0) and the next is
        /// the clump's full size at the tier 1 threshold. With no next tier the size stays.
        /// </summary>
        public static double FormingSizePx(double mass, double thCur, double sizeCur, double thNext, double sizeNext)
        {
            if (!(thNext > thCur)) return sizeCur;
            double f = (mass - thCur) / (thNext - thCur);
            if (f < 0) f = 0;
            if (f > 1) f = 1;
            return sizeCur + (sizeNext - sizeCur) * f;
        }

        /// <summary>
        /// Called when a planet is created from the held mass: returns the body's surface seed and drawn size so the
        /// planet continues it, and starts a new clump (the mass is 0 again).
        /// </summary>
        public void TakeFormingBody(out float bodySeed, out double diameterPx)
        {
            bodySeed = seed;
            diameterPx = shown ? sizePx : 0;
            seed = root.NextVisualSeed();
            sizePx = (float)p.formingClumpMinPx;
            shownTier = -1;
            popAge = float.MaxValue;
        }

        void LateUpdate()
        {
            bool show = root.Phase == GamePhase.Run && root.Run != null && root.Run.holding && !root.Paused;
            SetVisible(show);
            if (!show)
            {
                shown = false;
                return;
            }

            if (root.Effective.gravityRadius != ringRadiusPx) SetRingRadius(root.Effective.gravityRadius);
            var ps = root.ReadPointer();
            Vector2 w = root.ToWorld(ps.screenPos);
            transform.position = new Vector3(w.x, w.y, 0f);
            body.transform.position = new Vector3(w.x, w.y, 0f);

            // Current look and target size.
            var reached = root.HighestReachedTier();
            int tier = reached != null ? reached.tier : 0;
            double target;
            if (reached == null)
            {
                var t1 = root.celestialTable.Get(1);
                double th1 = t1 != null ? root.Threshold(t1) : 1;
                target = FormingSizePx(root.Run.mass, 0, p.formingClumpMinPx, th1, p.formingClumpMaxPx);
            }
            else
            {
                var next = tier < root.Meta.unlockedMaxTier ? root.celestialTable.Get(tier + 1) : null;
                target = next != null
                    ? FormingSizePx(root.Run.mass, root.Threshold(reached), reached.sizePx, root.Threshold(next), next.sizePx)
                    : reached.sizePx;
            }

            float dt = Time.deltaTime;
            if (!shown)
            {
                shown = true;
                seed = root.NextVisualSeed();
                sizePx = (float)target;
                shownTier = -1;
            }
            if (tier != shownTier)
            {
                if (shownTier >= 0 && tier > shownTier) popAge = 0f;
                shownTier = tier;
                body.SetLook(reached != null ? reached.look : p.formingClumpLook, seed, p.planetAxisPitchDeg);
                body.SetLightPoint(root.Planets.VanishPoint());
            }
            sizePx += (float)(target - sizePx) * (1f - Mathf.Exp(-(float)p.formingSizeLerpRate * dt));

            float pop = 0f;
            float popSec = Mathf.Max(0.01f, (float)p.formingPopSec);
            if (popAge < popSec)
            {
                pop = Mathf.Sin(popAge / popSec * Mathf.PI) * (float)p.formingPopScale * root.EffectIntensity;
                popAge += dt;
            }
            float drawnPx = sizePx * (1f + pop);
            body.SetDiameter(drawnPx / (float)p.pixelsPerUnit);

            // Gauge: a thin ring just outside the body (and its ring, if any).
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, ps.screenPos, null, out var local))
                gaugeRoot.anchoredPosition = local;
            float canvasPerRefPx = Screen.height / (2f * root.Cam.orthographicSize * (float)p.pixelsPerUnit) / Mathf.Max(root.Hud.Canvas.scaleFactor, 1e-3f);
            var look = body.Look;
            float reach = look != null && look.HasRing ? look.ringOuter : 1f;   // outside a planet ring
            float radius = (drawnPx * 0.5f * reach + (float)p.gaugeGapPx) * canvasPerRefPx;
            float width = (float)p.gaugeWidthPx * canvasPerRefPx;
            if (!Mathf.Approximately(gaugeBg.Radius, radius))
            {
                gaugeBg.Set(radius, width, 64);
                gaugeFill.Set(radius, width, 64);
            }
            gaugeFill.Fill = (float)root.GaugeFill();
        }

        /// <summary>Rebuilds the ring for the effective gravity radius (the gravity_radius upgrade changes it between runs).</summary>
        void SetRingRadius(double radiusPx)
        {
            ringRadiusPx = radiusPx;
            float r = (float)(radiusPx / p.pixelsPerUnit);
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i / (float)RingSegments * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
        }

        void SetVisible(bool v)
        {
            if (ring.enabled != v) ring.enabled = v;
            if (body.gameObject.activeSelf != v) body.gameObject.SetActive(v);
            if (gaugeRoot.gameObject.activeSelf != v) gaugeRoot.gameObject.SetActive(v);
        }
    }
}
