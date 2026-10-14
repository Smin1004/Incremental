using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// What follows the cursor while holding: the gravity-radius ring (LineRenderer circle, effective radius), the forming
    /// body (14 §4) and the mass gauge as a thin ring around it. The body wears the look of the tier a release would make
    /// now (a dark clump below tier 1) and grows from that tier's display size toward the next one with the mass; crossing a
    /// threshold pops it into the next look. On creation the body is handed to the planet pool (same surface, same size)
    /// and a new clump starts. All of it sits on the gravity source, which is the cursor unless the follow experiment is
    /// on (20 §7); then a thin line joins the two. With the stamina display on (b) the gravity ring is also the
    /// stamina gauge (26 §5.1): it is drawn only as far round as there is stamina left. Just outside the ring, one thin
    /// arc per ability with a cooldown shows its charge toward the next trigger and lights up the moment it fires
    /// (26 §9.2); the abilities are only read. Render only.
    /// </summary>
    public sealed class CursorView : MonoBehaviour
    {
        const int RingSegments = 64;
        const float RingWidthPx = 2f;
        static readonly Color RingColor = new Color(1f, 1f, 1f, 0.35f);

        GameRoot root;
        GameParams p;
        LineRenderer ring;
        /// <summary>26 §5.1 (b): the faint full circle behind the stamina arc, so the gravity radius still reads.</summary>
        LineRenderer track;
        Vector3[] arc;
        bool gauge;

        /// <summary>26 §9.2: the charge arc of one ability with a cooldown, and the faint full circle behind it.</summary>
        sealed class ChargeArc
        {
            public string id;
            public Color color;
            public LineRenderer arc, track;
        }
        readonly System.Collections.Generic.List<ChargeArc> arcs = new System.Collections.Generic.List<ChargeArc>();
        /// <summary>20 §7: the thin line from the cursor to the gravity source while the follow experiment is on.</summary>
        LineRenderer leash;
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

        PlanetLook lookOverride;
        double lookOverridePx;
        bool overrideShown;

        /// <summary>
        /// 24 §19.1: while set, the forming body wears this look at this diameter (reference px) instead of the tier's:
        /// the black hole. Cleared, the body goes back to the tier's look on the next frame; the forming size has kept
        /// moving meanwhile.
        /// </summary>
        public void SetLookOverride(PlanetLook look, double diameterPx)
        {
            lookOverride = look;
            lookOverridePx = diameterPx;
        }

        public void ClearLookOverride() => lookOverride = null;

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
            ring.widthMultiplier = (float)(RingWidthPx / p.pixelsPerUnit);
            ring.startColor = ring.endColor = RingColor;
            ring.sortingOrder = 5;
            UIBuilder.UseWhiteTexture(ring);
            ring.positionCount = RingSegments;

            track = MakeLine("StaminaTrack", lineMaterial, true, 4);
            track.positionCount = RingSegments;
            SetRingRadius(p.gravityRadius);

            var th = root.Ui.Theme;
            if (th.abilityArcs != null)
                foreach (var row in th.abilityArcs)
                {
                    if (row == null || string.IsNullOrEmpty(row.abilityId)) continue;
                    arcs.Add(new ChargeArc
                    {
                        id = row.abilityId,
                        color = row.color,
                        arc = MakeLine("Charge_" + row.abilityId, lineMaterial, false, 6),
                        track = MakeLine("ChargeTrack_" + row.abilityId, lineMaterial, true, 4),
                    });
                }

            leash = new GameObject("FollowLine").AddComponent<LineRenderer>();
            leash.transform.SetParent(transform.parent, false);
            leash.useWorldSpace = true;
            leash.positionCount = 2;
            leash.alignment = LineAlignment.TransformZ;
            leash.textureMode = LineTextureMode.Stretch;
            leash.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            leash.receiveShadows = false;
            if (lineMaterial != null) leash.sharedMaterial = lineMaterial;
            leash.widthMultiplier = (float)(p.followLineWidthPx / p.pixelsPerUnit);
            leash.startColor = leash.endColor = p.followLineColor;
            leash.sortingOrder = 5;
            UIBuilder.UseWhiteTexture(leash);
            leash.enabled = false;

            body = PlanetVisual.Create("FormingBody", transform.parent, root.planetMaterial, 11);

            var center = new Vector2(0.5f, 0.5f);
            gaugeRoot = UIBuilder.CreateRect("MassGauge", canvasRect, center, center, Vector2.zero, Vector2.zero);
            gaugeBg = MakeGaugeRing("Bg", (float)p.gaugeBgAlpha);
            gaugeFill = MakeGaugeRing("Fill", (float)p.gaugeFillAlpha);
            SetVisible(false);
        }

        /// <summary>A circle or arc around the gravity source, in local units, drawn with the unlit line material; off until used.</summary>
        LineRenderer MakeLine(string name, Material lineMaterial, bool loop, int order)
        {
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.transform.SetParent(transform, false);
            lr.useWorldSpace = false;
            lr.loop = loop;
            lr.alignment = LineAlignment.TransformZ;
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (lineMaterial != null) lr.sharedMaterial = lineMaterial;
            lr.widthMultiplier = (float)(RingWidthPx / p.pixelsPerUnit);
            lr.sortingOrder = order;
            UIBuilder.UseWhiteTexture(lr);
            lr.enabled = false;
            return lr;
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
                overrideShown = false;
                return;
            }

            if (root.Effective.gravityRadius != ringRadiusPx) SetRingRadius(root.Effective.gravityRadius);
            UpdateRing();
            TintRing();
            UpdateChargeArcs();
            // 22 §6: the lines are drawn in world units, so a zoomed-out camera would thin them to nothing. Their
            // width follows the scale, which keeps them the same thickness on screen.
            SetLineWidths((float)root.WorldScale, gauge ? root.Ui.Theme.staminaRingWidthPx : RingWidthPx);
            var ps = root.ReadPointer();
            // 20 §7: everything about the hold sits on the gravity source, which is the cursor unless it is trailing it.
            Vector2 w = root.GravityWorld;
            Vector2 cursor = root.ToWorld(ps.screenPos);
            transform.position = new Vector3(w.x, w.y, 0f);
            body.transform.position = new Vector3(w.x, w.y, 0f);
            bool leashOn = root.FollowEnabled && (cursor - w).sqrMagnitude > 1e-6f;
            if (leash.enabled != leashOn) leash.enabled = leashOn;
            if (leashOn)
            {
                leash.SetPosition(0, new Vector3(cursor.x, cursor.y, 0f));
                leash.SetPosition(1, new Vector3(w.x, w.y, 0f));
            }

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
                // 24 §19.1: while the black hole wears the body, a tier crossed meanwhile must not put the tier's look back.
                if (lookOverride == null)
                {
                    body.SetLook(reached != null ? reached.look : p.formingClumpLook, seed, p.planetAxisPitchDeg);
                    body.SetLightPoint(root.Planets.VanishPoint());
                }
            }
            sizePx += (float)(target - sizePx) * (1f - Mathf.Exp(-(float)p.formingSizeLerpRate * dt));

            float pop = 0f;
            float popSec = Mathf.Max(0.01f, (float)p.formingPopSec);
            if (popAge < popSec)
            {
                pop = Mathf.Sin(popAge / popSec * Mathf.PI) * (float)p.formingPopScale * root.EffectIntensity;
                popAge += dt;
            }
            // 24 §24.2-1: smaller while the black hole is on (the inhale and the release; while open it wears the hole).
            float drawnPx = (float)root.GravityBodyPx(root.RunBodyPx(sizePx * (1f + pop)));
            // 24 §19.1: the black hole in place of the forming body, at its own size, until the override is cleared.
            if (lookOverride != null)
            {
                if (!overrideShown)
                {
                    overrideShown = true;
                    body.SetLook(lookOverride, seed, p.planetAxisPitchDeg);
                    body.SetLightPoint(root.Planets.VanishPoint());
                }
                drawnPx = (float)root.RunBodyPx(lookOverridePx);
            }
            else if (overrideShown)
            {
                overrideShown = false;
                // The tier's look again on this frame (the tier may have changed meanwhile), without the pop.
                shownTier = tier;
                body.SetLook(reached != null ? reached.look : p.formingClumpLook, seed, p.planetAxisPitchDeg);
                body.SetLightPoint(root.Planets.VanishPoint());
            }
            body.SetDiameter(drawnPx / (float)p.pixelsPerUnit);

            // Gauge: a thin ring just outside the body (and its ring, if any), around the gravity source.
            var bodyScreen = (Vector2)root.Cam.WorldToScreenPoint(new Vector3(w.x, w.y, 0f));
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, bodyScreen, null, out var local))
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

        /// <summary>
        /// 26 §5.1: the gravity ring as it is drawn now. (a): the plain full circle. (b): the ring is the stamina gauge,
        /// an arc from the top, clockwise, as long as the share of stamina left, in the stamina color (pulsing near the
        /// end), over a faint full circle.
        /// </summary>
        void UpdateRing()
        {
            var th = root.Ui.Theme;
            bool want = root.Ui.StaminaMode == 1;
            if (!want)
            {
                if (!gauge) return;
                gauge = false;
                ring.loop = true;
                ring.startColor = ring.endColor = RingColor;
                track.enabled = false;
                SetRingRadius(ringRadiusPx);
                return;
            }
            gauge = true;
            float max = (float)root.Effective.staminaMax;
            float share = max > 0f ? Mathf.Clamp01((float)root.Run.stamina / max) : 0f;
            int segs = Mathf.Max(8, th.staminaRingSegments);
            int n = Mathf.Max(1, Mathf.CeilToInt(segs * share));
            if (arc == null || arc.Length < segs + 1) arc = new Vector3[segs + 1];
            float r = (float)(ringRadiusPx / p.pixelsPerUnit);
            float sweep = Mathf.PI * 2f * share;
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.PI * 0.5f - sweep * i / n;
                arc[i] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
            }
            ring.loop = false;
            ring.positionCount = n + 1;
            ring.SetPositions(arc);
            float pulse = share <= th.staminaLowAt ? th.StaminaPulse(Time.time, root.EffectIntensity) : 0f;
            Color c = th.StaminaColor(share, pulse);
            c.a *= th.staminaRingAlpha;
            ring.startColor = ring.endColor = c;
            bool trackOn = th.staminaRingTrackAlpha > 0f;
            if (track.enabled != trackOn) track.enabled = trackOn;
            if (trackOn) track.startColor = track.endColor = new Color(th.staminaFull.r, th.staminaFull.g, th.staminaFull.b, th.staminaRingTrackAlpha);
        }

        /// <summary>
        /// 26 §9.2: one arc per listed ability that is unlocked and has a cooldown, concentric just outside the gravity
        /// ring: from the top, clockwise, as full as the charge toward the next trigger, in the ability's color over a
        /// faint full circle. The moment the ability fires the whole circle lights up and widens, then the gauge starts
        /// over. The abilities are only read.
        /// </summary>
        void UpdateChargeArcs()
        {
            var th = root.Ui.Theme;
            float scale = (float)root.WorldScale;
            float ppu = (float)p.pixelsPerUnit;
            float baseR = (float)(ringRadiusPx / ppu);
            int segs = Mathf.Max(8, th.abilityArcSegments);
            if (arc == null || arc.Length < segs + 1) arc = new Vector3[segs + 1];
            int shown = 0;
            for (int i = 0; i < arcs.Count; i++)
            {
                var a = arcs[i];
                var ability = root.Abilities != null ? root.Abilities.Get(a.id) : null;
                bool on = ability != null && ability.Level.unlocked && ability.Def.cooldownSec > 0;
                if (!on)
                {
                    if (a.arc.enabled) a.arc.enabled = false;
                    if (a.track.enabled) a.track.enabled = false;
                    continue;
                }
                double cooldown = ability.Def.Cooldown(ability.Level, root.abilityTable.maxCooldownReduction);
                // 24 §20.2: the amplifier's arc runs down with its buff instead of up with its charge.
                float share = ability.ArcShare(cooldown);
                float r = baseR + (th.abilityArcGapPx + shown * th.abilityArcSpacingPx) * scale / ppu;
                shown++;
                float age = ability.EffectAge;
                float flash = age >= 0f && th.abilityArcFlashSec > 0f && age < th.abilityArcFlashSec
                    ? (1f - age / th.abilityArcFlashSec) * root.EffectIntensity
                    : 0f;
                float width = th.abilityArcWidthPx * scale / ppu;

                bool trackOn = th.abilityArcTrackAlpha > 0f;
                if (a.track.enabled != trackOn) a.track.enabled = trackOn;
                if (trackOn)
                {
                    for (int k = 0; k < segs; k++)
                    {
                        float ang = k / (float)segs * Mathf.PI * 2f;
                        arc[k] = new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, 0f);
                    }
                    a.track.positionCount = segs;
                    a.track.SetPositions(arc);
                    a.track.widthMultiplier = width;
                    a.track.startColor = a.track.endColor = new Color(a.color.r, a.color.g, a.color.b, th.abilityArcTrackAlpha);
                }

                // While it flashes the whole circle is drawn; otherwise the arc of the charge.
                float drawn = flash > 0f ? 1f : share;
                bool arcOn = drawn > 0.002f;
                if (a.arc.enabled != arcOn) a.arc.enabled = arcOn;
                if (!arcOn) continue;
                int n = Mathf.Max(1, Mathf.CeilToInt(segs * drawn));
                float sweep = Mathf.PI * 2f * drawn;
                for (int k = 0; k <= n; k++)
                {
                    float ang = Mathf.PI * 0.5f - sweep * k / n;
                    arc[k] = new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, 0f);
                }
                a.arc.positionCount = n + 1;
                a.arc.SetPositions(arc);
                a.arc.widthMultiplier = width * (1f + flash * th.abilityArcFlashWidth);
                Color col = a.color;
                col.a = th.abilityArcAlpha * (flash > 0f ? Mathf.Max(flash, 0.35f) : 1f);
                a.arc.startColor = a.arc.endColor = col;
            }
        }

        bool tinted;

        /// <summary>
        /// 24 §20.2: the gravity ring takes the abilities' tint (the amplifier's gold while its buff is on, brighter at
        /// the flash of the moment a crystal is taken), blended over whatever color the ring has this frame.
        /// </summary>
        void TintRing()
        {
            float amount = 0f;
            Color tint = root.Abilities != null ? root.Abilities.RingTint(out amount) : Color.white;
            if (amount > 0f)
            {
                Color baseColor = gauge ? ring.startColor : RingColor;
                ring.startColor = ring.endColor = Color.Lerp(baseColor, tint, Mathf.Clamp01(amount));
                tinted = true;
            }
            else if (tinted)
            {
                tinted = false;
                if (!gauge) ring.startColor = ring.endColor = RingColor;
            }
        }

        /// <summary>22 §6: keeps the ring and the leash the same thickness on screen however far the camera has pulled back.</summary>
        void SetLineWidths(float worldScale, float ringWidthPx)
        {
            if (Mathf.Approximately(lineScale, worldScale) && Mathf.Approximately(lineRingPx, ringWidthPx)) return;
            lineScale = worldScale;
            lineRingPx = ringWidthPx;
            ring.widthMultiplier = (float)(ringWidthPx * worldScale / p.pixelsPerUnit);
            track.widthMultiplier = (float)(RingWidthPx * worldScale / p.pixelsPerUnit);
            leash.widthMultiplier = (float)(p.followLineWidthPx * worldScale / p.pixelsPerUnit);
        }

        float lineScale = -1f;
        float lineRingPx = -1f;

        /// <summary>Rebuilds the ring for the effective gravity radius (the gravity_radius upgrade changes it between runs).</summary>
        void SetRingRadius(double radiusPx)
        {
            ringRadiusPx = radiusPx;
            float r = (float)(radiusPx / p.pixelsPerUnit);
            // As the stamina gauge the ring's points are an arc, rewritten every frame (UpdateRing).
            if (!gauge) ring.positionCount = RingSegments;
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i / (float)RingSegments * Mathf.PI * 2f;
                var point = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
                if (!gauge) ring.SetPosition(i, point);
                track.SetPosition(i, point);
            }
        }

        void SetVisible(bool v)
        {
            if (ring.enabled != v) ring.enabled = v;
            if (!v && track.enabled) track.enabled = false;
            if (!v)
                foreach (var a in arcs)
                {
                    if (a.arc.enabled) a.arc.enabled = false;
                    if (a.track.enabled) a.track.enabled = false;
                }
            if (!v && leash.enabled) leash.enabled = false;
            if (body.gameObject.activeSelf != v) body.gameObject.SetActive(v);
            if (gaugeRoot.gameObject.activeSelf != v) gaugeRoot.gameObject.SetActive(v);
        }
    }
}
