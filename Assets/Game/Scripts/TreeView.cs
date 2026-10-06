using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// Skill tree screen (12 §9, 26 §9.2), between runs after the result screen. Constellation style: nodes are glowing
    /// points on rings (radius = treeRingR0Px + minTier × treeRingStepPx, angle = angleDeg, 12 §5), edges are thin lines,
    /// owned paths are bright, unlock nodes (gates, dust kinds, abilities) are drawn larger. Node states follow 12 §4;
    /// a purchasable node breathes slowly. Click buys one level, holding repeats, drag pans, the wheel zooms, hovering
    /// shows a tooltip. A purchase lights the node, runs a light along the connections from its owned prerequisites,
    /// throws a few sparks and, for a gate, brightens that ring's circle. Opening moves the view to the frontier (the
    /// purchasable nodes). The top and bottom bars are dark veils so the header and the help line read over the nodes;
    /// "next run" and Space always work. Colors, sizes and times are UITheme.tree. Built from code in its own canvas.
    /// </summary>
    public sealed class TreeView : MonoBehaviour
    {
        sealed class Edge
        {
            public Image image;
            public TreeNodeWidget from;
            public TreeNodeWidget to;
            /// <summary>Seconds since a purchase lit this connection, or −1.</summary>
            public float light = -1f;
        }

        /// <summary>A purchase effect dot: a light travelling along a connection, or a spark flying off the node.</summary>
        sealed class Spark
        {
            public Image img;
            public Vector2 from, to;
            public float age = -1f;
            public float sec;
            public float size;
            public Color color;
            public bool shrinks;
        }

        const int FlashPool = 8;
        const int SparkPool = 48;

        GameRoot root;
        GameParams p;
        NodeTable t;
        UITheme theme;
        UITreeStyle st;
        TMP_FontAsset font;
        Canvas canvas;
        RectTransform area, content, ringsLayer, edgesLayer, flashLayer, nodesLayer, tooltip;
        UIShape topVeil, bottomVeil, tooltipShape;
        TMP_Text title, currencyLabel, currencyValue, hint, tooltipTitle, tooltipBody;
        string currencyShown;
        readonly List<TreeNodeWidget> widgets = new List<TreeNodeWidget>();
        readonly Dictionary<string, TreeNodeWidget> byId = new Dictionary<string, TreeNodeWidget>();
        readonly List<Edge> edges = new List<Edge>();
        readonly List<UIRing> rings = new List<UIRing>();
        float[] ringLight;
        readonly Dictionary<NodeDef, string> names = new Dictionary<NodeDef, string>();
        TreeNodeWidget center;

        Vector2 pan;
        float zoom = 1f;
        Vector2 panFrom, panTo;
        float zoomFrom, zoomTo;
        float focusT = 1f;
        bool dragging;

        TreeNodeWidget pressed;
        float pressTime, nextRepeat;
        bool repeated;
        TreeNodeWidget hovered;
        int openedFrame = -1;

        readonly Image[] flashes = new Image[FlashPool];
        readonly float[] flashAge = new float[FlashPool];
        int nextFlash;
        readonly Spark[] sparks = new Spark[SparkPool];
        int nextSpark;
        readonly System.Random fxRng = new System.Random(7);

        public bool IsVisible => canvas != null && canvas.gameObject.activeSelf;

        public void Init(GameRoot gameRoot)
        {
            root = gameRoot;
            p = root.gameParams;
            t = root.nodeTable;
            var ui = root.Ui;
            theme = ui.Theme;
            st = theme.tree;
            font = ui.Font;

            canvas = UIBuilder.CreateCanvas("SkillTree", transform);
            canvas.sortingOrder = 20;
            var canvasRect = (RectTransform)canvas.transform;
            var c = new Vector2(0.5f, 0.5f);

            area = UIBuilder.CreateStretch("Area", canvasRect);
            // Transparent: the shared procedural background (BackdropView) shows through; the image only catches drags.
            var bg = area.gameObject.AddComponent<Image>();
            bg.color = Color.clear;
            bg.raycastTarget = true;
            area.gameObject.AddComponent<TreeDragArea>().view = this;

            content = UIBuilder.CreateRect("Content", area, c, c, Vector2.zero, Vector2.zero);
            ringsLayer = UIBuilder.CreateRect("Rings", content, c, c, Vector2.zero, Vector2.zero);
            edgesLayer = UIBuilder.CreateRect("Edges", content, c, c, Vector2.zero, Vector2.zero);
            flashLayer = UIBuilder.CreateRect("Flashes", content, c, c, Vector2.zero, Vector2.zero);
            nodesLayer = UIBuilder.CreateRect("Nodes", content, c, c, Vector2.zero, Vector2.zero);

            int ringCount = root.celestialTable.MaxTier;
            ringLight = new float[ringCount];
            for (int k = 1; k <= ringCount; k++)
            {
                var rt = UIBuilder.CreateRect("Ring" + k, ringsLayer, c, c, Vector2.zero, Vector2.zero);
                var ring = rt.gameObject.AddComponent<UIRing>();
                ring.raycastTarget = false;
                ring.Set(RingRadius(k), st.ringWidth, 128);
                rings.Add(ring);
                ringLight[k - 1] = -1f;
            }

            center = MakeWidget(null, Vector2.zero, st.centerSize);
            foreach (var n in t.nodes)
            {
                var w = MakeWidget(n, NodePosition(n), NodeSize(n));
                widgets.Add(w);
                byId[n.id] = w;
            }
            foreach (var w in widgets)
            {
                var n = w.node;
                if (n.prereqs == null || n.prereqs.Count == 0) AddEdge(center, w);
                else
                    foreach (var pre in n.prereqs)
                        if (byId.TryGetValue(pre, out var from)) AddEdge(from, w);
            }

            for (int i = 0; i < FlashPool; i++)
            {
                flashes[i] = UIBuilder.CreateImage("Flash" + i, flashLayer, Color.clear, c, c, Vector2.zero, Vector2.one, UIBuilder.SoftDotSprite());
                flashes[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < SparkPool; i++)
            {
                var img = UIBuilder.CreateImage("Spark" + i, flashLayer, Color.clear, c, c, Vector2.zero, Vector2.one, UIBuilder.SoftDotSprite());
                img.gameObject.SetActive(false);
                sparks[i] = new Spark { img = img };
            }

            BuildBars(canvasRect, ui);
            BuildTooltip(canvasRect, ui);

            ui.Changed += ApplyStyle;
            ApplyStyle();
            root.RunStarted += Hide;
            canvas.gameObject.SetActive(false);
        }

        /// <summary>26 §5.3: gates and unlock nodes (dust kinds, abilities) are drawn larger than stat nodes.</summary>
        float NodeSize(NodeDef n) => n.IsGate ? st.gateSize : (n.IsDustUnlock || n.IsAbilityUnlock ? st.unlockSize : st.statSize);

        /// <summary>Colors of the visual direction for the header, the help line and the tooltip.</summary>
        void ApplyStyle()
        {
            var d = root.Ui.Direction;
            title.color = d.text;
            currencyLabel.color = hint.color = d.textDim;
            currencyValue.color = d.accent;
            tooltipTitle.color = d.text;
            tooltipBody.color = d.text;
        }

        // ---------------- open / close ----------------

        public void Show()
        {
            if (root.Solar != null) root.Solar.Hide();
            canvas.gameObject.SetActive(true);
            openedFrame = Time.frameCount;
            pressed = null;
            hovered = null;
            dragging = false;
            tooltip.gameObject.SetActive(false);
            Refresh();
            FocusFrontier(false);
        }

        public void Hide()
        {
            if (canvas != null) canvas.gameObject.SetActive(false);
            pressed = null;
            hovered = null;
        }

        void Update()
        {
            // The debug gallery hides this canvas without deactivating it; keys must not reach a hidden screen.
            if (!IsVisible || (root.Gallery != null && root.Gallery.IsOpen)) return;

            var kb = Keyboard.current;
            if (kb != null && kb.spaceKey.wasPressedThisFrame && Time.frameCount != openedFrame)
            {
                StartNextRun();
                return;
            }

            float now = Time.unscaledTime;
            if (pressed != null)
            {
                var mouse = Mouse.current;
                if (dragging || mouse == null || !mouse.leftButton.isPressed) pressed = null;
                else if (now - pressTime >= (float)p.treeHoldDelaySec && now >= nextRepeat)
                {
                    TryBuy(pressed);
                    repeated = true;
                    nextRepeat = now + (float)p.treeHoldRepeatSec;
                }
            }

            if (focusT < 1f)
            {
                focusT = Mathf.Min(1f, focusT + Time.unscaledDeltaTime / Mathf.Max(0.01f, (float)p.treeFocusSec));
                float e = 1f - (1f - focusT) * (1f - focusT);
                pan = Vector2.Lerp(panFrom, panTo, e);
                zoom = Mathf.Lerp(zoomFrom, zoomTo, e);
            }
            content.anchoredPosition = pan;
            content.localScale = new Vector3(zoom, zoom, 1f);

            Refresh();
            UpdateEffects(Time.unscaledDeltaTime);
            if (hovered != null) UpdateTooltip(hovered);
        }

        void StartNextRun() => root.RequestStartRun();

        // ---------------- node visuals ----------------

        void Refresh()
        {
            var m = root.Meta;
            SetCurrency(m.currency);
            float fx = root.EffectIntensity;
            // 26 §5.3: purchasable nodes breathe slowly; nothing moves at effect intensity 0.
            float pulse = (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * st.pulseHz * Mathf.PI * 2f)) * fx;

            center.core.color = st.owned;
            center.glow.color = st.centerGlow;
            center.label.text = string.Empty;

            foreach (var w in widgets)
            {
                var n = w.node;
                var state = Shop.State(t, m, n);
                bool visible = state != NodeState.Hidden && n.enabled;
                SetActive(w, visible);
                if (!visible) continue;

                int level = m.GetLevel(n.id);
                bool maxed = state == NodeState.Maxed;
                bool purchasable = state == NodeState.Available || state == NodeState.Owned;
                double cost = maxed ? 0 : Shop.Cost(t, m, n);
                bool affordable = purchasable && m.currency >= cost;
                Color tint = n.IsGate ? TierColor(n.tier) : Color.white;

                Color core;
                float glowA;
                switch (state)
                {
                    case NodeState.Maxed: core = n.IsGate ? tint : st.maxed; glowA = 0.8f; break;
                    case NodeState.Owned: core = n.IsGate ? tint : st.owned; glowA = 0.35f; break;
                    case NodeState.Available: core = affordable ? Color.Lerp(tint, Color.white, 0.4f) : st.shortOfCurrency * tint; glowA = affordable ? 0.3f : 0.12f; break;
                    default: core = st.locked * new Color(tint.r, tint.g, tint.b, 1f); glowA = 0f; break;
                }
                float breath = affordable ? pulse : 0f;
                w.core.color = core;
                w.glow.color = new Color(core.r, core.g, core.b, Mathf.Clamp01(glowA + st.pulseGlow * breath));
                float s = 1f + st.pulseScale * breath;
                w.core.transform.localScale = new Vector3(s, s, 1f);

                string levelText = n.maxLevel > 0 ? string.Format(UIStrings.LevelShort, level, n.maxLevel) : string.Format(UIStrings.Level, level);
                if (maxed) w.label.text = levelText;
                else if (level > 0) w.label.text = levelText + "\n" + Fmt.Num(cost, Rounding.Up);
                else w.label.text = Fmt.Num(cost, Rounding.Up);
                w.label.color = state == NodeState.Locked ? st.labelDim : (maxed || affordable ? st.labelOk : st.labelShort);
            }

            foreach (var e in edges)
            {
                bool visible = e.from.gameObject.activeSelf && e.to.gameObject.activeSelf;
                SetActive(e.image, visible);
                if (!visible) continue;
                bool fromOwned = e.from.node == null || Shop.IsOwned(m, e.from.node);
                bool toOwned = Shop.IsOwned(m, e.to.node);
                var col = fromOwned && toOwned ? (Shop.IsMaxed(m, e.to.node) ? st.edgeMaxed : st.edgeOwned) : st.edgeDim;
                // A connection a purchase just ran along stays lit for a moment.
                if (e.light >= 0f && st.edgeLightSec > 0f)
                    col = Color.Lerp(col, st.edgeMaxed, Mathf.Clamp01(1f - e.light / st.edgeLightSec) * fx);
                e.image.color = col;
            }

            for (int i = 0; i < rings.Count; i++)
            {
                int k = i + 1;
                bool visible = k <= m.unlockedMaxTier + 1;
                SetActive(rings[i], visible);
                if (!visible) continue;
                var col = k <= m.unlockedMaxTier ? st.ringOpen : st.ringNext;
                // 26 §5.3: a gate purchase brightens its ring's circle, which then settles back.
                if (ringLight[i] >= 0f && st.gateRingSec > 0f)
                    col.a = Mathf.Lerp(col.a, st.gateRingAlpha, Mathf.Clamp01(1f - ringLight[i] / st.gateRingSec) * fx);
                rings[i].color = col;
            }
        }

        void SetCurrency(double currency)
        {
            string value = Fmt.Num(currency);
            if (value == currencyShown) return;
            currencyShown = value;
            currencyValue.text = value;
            // The label sits right before the value, so the pair stays centered as the number changes width.
            float w = currencyValue.GetPreferredValues(value).x;
            var rt = (RectTransform)currencyLabel.transform;
            rt.anchoredPosition = new Vector2(-w * 0.5f - st.barPaddingPx * 0.4f, rt.anchoredPosition.y);
        }

        Color TierColor(int tier)
        {
            var def = root.celestialTable.Get(tier);
            return def != null ? def.color : Color.white;
        }

        // ---------------- buying ----------------

        bool TryBuy(TreeNodeWidget w)
        {
            if (w == null || w.node == null) return false;
            if (!root.TryBuy(w.node.id)) return false;
            OnBought(w);
            return true;
        }

        /// <summary>Buys one level of a node by id, with the purchase effect, as a click would (captures and checks).</summary>
        public bool DebugBuy(string nodeId) => byId.TryGetValue(nodeId, out var w) && TryBuy(w);

        /// <summary>
        /// 26 §5.3, the purchase: the node lights up, a light runs along every connection from an owned prerequisite,
        /// sparks fly off, and a gate brightens its ring. All of it follows the effect intensity; at 0 nothing is added.
        /// </summary>
        void OnBought(TreeNodeWidget w)
        {
            float fx = root.EffectIntensity;
            if (fx <= 0f) return;
            Flash(w);
            var m = root.Meta;
            foreach (var e in edges)
            {
                if (e.to != w) continue;
                bool fromOwned = e.from.node == null || Shop.IsOwned(m, e.from.node);
                if (!fromOwned) continue;
                e.light = 0f;
                Spawn(e.from.rt.anchoredPosition, w.rt.anchoredPosition, st.travelSec, st.travelDotPx, st.edgeMaxed, false);
            }
            for (int i = 0; i < st.sparkCount; i++)
            {
                float ang = (float)(fxRng.NextDouble() * Mathf.PI * 2.0);
                float dist = st.sparkDistPx * (0.55f + 0.45f * (float)fxRng.NextDouble());
                var to = w.rt.anchoredPosition + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                Spawn(w.rt.anchoredPosition, to, st.sparkSec, st.sparkPx, st.flash, true);
            }
            if (w.node.IsGate && w.node.tier >= 1 && w.node.tier <= ringLight.Length) ringLight[w.node.tier - 1] = 0f;
        }

        void Spawn(Vector2 from, Vector2 to, float sec, float size, Color color, bool shrinks)
        {
            if (sec <= 0f) return;
            var s = sparks[nextSpark];
            nextSpark = (nextSpark + 1) % SparkPool;
            s.from = from;
            s.to = to;
            s.sec = sec;
            s.size = size;
            s.color = color;
            s.shrinks = shrinks;
            s.age = 0f;
            s.img.rectTransform.sizeDelta = Vector2.one * size;
            s.img.rectTransform.anchoredPosition = from;
            s.img.color = color;
            s.img.gameObject.SetActive(true);
        }

        void Flash(TreeNodeWidget w)
        {
            int i = nextFlash;
            nextFlash = (nextFlash + 1) % FlashPool;
            var f = flashes[i];
            var rt = (RectTransform)f.transform;
            rt.anchoredPosition = w.rt.anchoredPosition;
            rt.sizeDelta = Vector2.one * w.size * st.glowScale;
            flashAge[i] = 0f;
            f.gameObject.SetActive(true);
        }

        void UpdateEffects(float dt)
        {
            float intensity = root.EffectIntensity;
            float flashSec = Mathf.Max(0.01f, st.flashSec);
            for (int i = 0; i < FlashPool; i++)
            {
                var f = flashes[i];
                if (!f.gameObject.activeSelf) continue;
                flashAge[i] += dt;
                float k = flashAge[i] / flashSec;
                if (k >= 1f)
                {
                    f.gameObject.SetActive(false);
                    continue;
                }
                f.transform.localScale = Vector3.one * (1f + st.flashScale * k);
                var c = st.flash;
                c.a *= intensity * (1f - k);
                f.color = c;
            }
            foreach (var s in sparks)
            {
                if (s.age < 0f) continue;
                s.age += dt;
                float k = s.age / s.sec;
                if (k >= 1f)
                {
                    s.age = -1f;
                    s.img.gameObject.SetActive(false);
                    continue;
                }
                float e = 1f - (1f - k) * (1f - k);
                s.img.rectTransform.anchoredPosition = Vector2.Lerp(s.from, s.to, e);
                float size = s.shrinks ? s.size * (1f - k) : s.size;
                s.img.rectTransform.sizeDelta = Vector2.one * size;
                var c = s.color;
                c.a *= intensity * (s.shrinks ? 1f - k : 1f);
                s.img.color = c;
            }
            foreach (var e in edges)
                if (e.light >= 0f && (e.light += dt) >= st.edgeLightSec) e.light = -1f;
            for (int i = 0; i < ringLight.Length; i++)
                if (ringLight[i] >= 0f && (ringLight[i] += dt) >= st.gateRingSec) ringLight[i] = -1f;
        }

        // ---------------- input callbacks ----------------

        internal void OnNodeDown(TreeNodeWidget w)
        {
            pressed = w;
            repeated = false;
            pressTime = Time.unscaledTime;
            nextRepeat = pressTime + (float)p.treeHoldDelaySec;
        }

        internal void OnNodeUp(TreeNodeWidget w)
        {
            if (pressed == w) pressed = null;
        }

        internal void OnNodeClick(TreeNodeWidget w)
        {
            if (!repeated && !dragging) TryBuy(w);
            repeated = false;
        }

        internal void OnNodeEnter(TreeNodeWidget w)
        {
            hovered = w;
            tooltip.gameObject.SetActive(true);
            UpdateTooltip(w);
        }

        internal void OnNodeExit(TreeNodeWidget w)
        {
            if (hovered != w) return;
            hovered = null;
            tooltip.gameObject.SetActive(false);
        }

        internal void OnPanBegin()
        {
            dragging = true;
            pressed = null;
            focusT = 1f;
        }

        internal void OnPan(Vector2 screenDelta)
        {
            pan += screenDelta / Mathf.Max(0.01f, canvas.scaleFactor);
        }

        internal void OnPanEnd() => dragging = false;

        /// <summary>One wheel step zooms by 10 % around the pointer.</summary>
        internal void OnZoom(float scroll, Vector2 screenPos)
        {
            if (Mathf.Approximately(scroll, 0f)) return;
            focusT = 1f;
            float z1 = zoom;
            float z2 = Mathf.Clamp(z1 * (scroll > 0 ? 1.1f : 1f / 1.1f), (float)p.treeZoomMin, (float)p.treeZoomMax);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screenPos, null, out var local)) local = Vector2.zero;
            pan = local - (local - pan) * (z2 / z1);
            zoom = z2;
        }

        // ---------------- camera ----------------

        /// <summary>Moves (and zooms out if needed) so the purchasable nodes are in view (12 §9).</summary>
        void FocusFrontier(bool instant)
        {
            var m = root.Meta;
            Vector2 sum = Vector2.zero;
            int count = 0;
            foreach (var w in widgets)
            {
                if (!Shop.IsPurchasable(t, m, w.node)) continue;
                sum += w.rt.anchoredPosition;
                count++;
            }
            Vector2 focus = count > 0 ? sum / count : Vector2.zero;
            float extent = 0f;
            foreach (var w in widgets)
                if (Shop.IsPurchasable(t, m, w.node)) extent = Mathf.Max(extent, (w.rt.anchoredPosition - focus).magnitude);

            Rect r = area.rect;
            if (r.width < 100f || r.height < 100f) r = new Rect(-960f, -540f, 1920f, 1080f); // layout not built yet
            float halfW = r.width * 0.5f - st.viewMargin;
            float halfH = (r.height - st.topBarHeight - st.bottomBarHeight) * 0.5f - st.viewMargin * 0.5f;
            float fit = extent > 1f ? Mathf.Min(halfW, halfH) / extent : 1f;
            float z = Mathf.Clamp(Mathf.Min(1f, fit), (float)p.treeZoomMin, (float)p.treeZoomMax);

            panFrom = pan;
            zoomFrom = zoom;
            panTo = -focus * z + new Vector2(0f, (st.bottomBarHeight - st.topBarHeight) * 0.5f);
            zoomTo = z;
            focusT = instant ? 1f : 0f;
            if (instant)
            {
                pan = panTo;
                zoom = zoomTo;
            }
        }

        // ---------------- tooltip ----------------

        void UpdateTooltip(TreeNodeWidget w)
        {
            var m = root.Meta;
            if (w.node == null)
            {
                tooltipTitle.text = UIStrings.CenterName;
                tooltipBody.text = UIStrings.CenterDesc;
            }
            else
            {
                var n = w.node;
                tooltipTitle.text = NodeName(n);
                var sb = new System.Text.StringBuilder();
                if (n.IsGate)
                {
                    var tier = root.celestialTable.Get(n.tier);
                    var s = Stats.Compute(p, t, m);
                    double price = tier != null ? Stats.SaleIncome(tier, s) : 0;
                    double mass = tier != null ? Stats.Threshold(tier, s) : 0;
                    sb.Append(string.Format(UIStrings.GateEffect, n.tier, Fmt.Price(price), Fmt.Num(mass, Rounding.Up)));
                }
                else if (n.IsDustUnlock || n.IsAbilityUnlock)
                {
                    // 20 §4, 24 §6: an unlock has one level, so "per level" would read oddly.
                    sb.Append(UIStrings.NodeEffect(n, p));
                }
                else
                {
                    sb.Append(string.Format(UIStrings.EffectPerLevel, UIStrings.NodeEffect(n, p)));
                }
                int level = m.GetLevel(n.id);
                sb.Append('\n').Append(n.maxLevel > 0 ? string.Format(UIStrings.LevelOf, level, n.maxLevel) : string.Format(UIStrings.Level, level));
                var state = Shop.State(t, m, n);
                if (state == NodeState.Maxed) sb.Append('\n').Append(UIStrings.Maxed);
                else
                {
                    double cost = Shop.Cost(t, m, n);
                    bool enough = m.currency >= cost;
                    var costColor = enough ? st.labelOk : st.labelShort;
                    sb.Append('\n').Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(costColor)).Append('>')
                      .Append(string.Format(UIStrings.Cost, Fmt.Num(cost, Rounding.Up))).Append("</color>");
                    if (state == NodeState.Locked)
                    {
                        sb.Append('\n');
                        if (m.unlockedMaxTier < n.minTier)
                            sb.Append(string.Format(UIStrings.LockedRing, UIStrings.TierName(n.minTier)));
                        else sb.Append(UIStrings.LockedPrereq);
                    }
                }
                tooltipBody.text = sb.ToString();
            }

            var mouse = Mouse.current;
            Vector2 screen = mouse != null ? mouse.position.ReadValue() : (Vector2)RectTransformUtility.WorldToScreenPoint(null, w.rt.position);
            var canvasRect = (RectTransform)canvas.transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local))
            {
                Rect cr = canvasRect.rect;
                Vector2 size = tooltip.sizeDelta;
                float pad = st.tooltipPaddingPx;
                float x = Mathf.Clamp(local.x + pad * 1.5f, cr.xMin + pad * 0.5f, cr.xMax - size.x - pad * 0.5f);
                float y = Mathf.Clamp(local.y - pad * 1.5f, cr.yMin + size.y + pad * 0.5f, cr.yMax - pad * 0.5f);
                tooltip.anchoredPosition = new Vector2(x, y);
            }
        }

        string NodeName(NodeDef n)
        {
            if (!names.TryGetValue(n, out var s))
            {
                s = UIStrings.NodeName(t, n);
                names[n] = s;
            }
            return s;
        }

        // ---------------- building ----------------

        float RingRadius(int ring) => (float)(p.treeRingR0Px + ring * p.treeRingStepPx);

        Vector2 NodePosition(NodeDef n)
        {
            float a = n.angleDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * RingRadius(n.minTier);
        }

        TreeNodeWidget MakeWidget(NodeDef n, Vector2 pos, float size)
        {
            var c = new Vector2(0.5f, 0.5f);
            var rt = UIBuilder.CreateRect(n != null ? "Node_" + n.id : "Center", nodesLayer, c, c, pos, new Vector2(size, size));
            var w = rt.gameObject.AddComponent<TreeNodeWidget>();
            w.view = this;
            w.node = n;
            w.rt = rt;
            w.size = size;
            w.glow = UIBuilder.CreateImage("Glow", rt, Color.clear, c, c, Vector2.zero, Vector2.one * size * st.glowScale, UIBuilder.SoftDotSprite());
            w.core = UIBuilder.CreateImage("Core", rt, Color.white, c, c, Vector2.zero, Vector2.one * size, root.circleSprite);
            w.core.raycastTarget = true;
            w.label = UIBuilder.CreateText("Label", rt, font, st.labelSize, TextAnchor.UpperCenter, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(0f, -st.labelGapPx), new Vector2(160f, st.labelSize * theme.lineHeight * 2f));
            return w;
        }

        void AddEdge(TreeNodeWidget from, TreeNodeWidget to)
        {
            var img = UIBuilder.CreateImage("Edge_" + (from.node != null ? from.node.id : "center") + "_" + to.node.id, edgesLayer,
                st.edgeDim, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one, UIBuilder.SolidSprite());
            UIBuilder.PlaceLine((RectTransform)img.transform, from.rt.anchoredPosition, to.rt.anchoredPosition, st.edgeWidth);
            edges.Add(new Edge { image = img, from = from, to = to });
        }

        /// <summary>
        /// Top bar: title left, currency (label + value) center. Bottom bar: the help line left, the "next run" button
        /// right. Both bars are veils that fade toward the middle of the screen, so what is under them stays visible
        /// but the text reads (26 §5.3: the help line used to collide with node costs).
        /// </summary>
        void BuildBars(RectTransform canvasRect, UIStyle ui)
        {
            float pad = st.barPaddingPx;
            topVeil = UIBuilder.CreateShape("TopVeil", canvasRect, ui, UIRole.Veil, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(4000f, st.topBarHeight * 1.6f));
            topVeil.Pad = 0f;
            topVeil.raycastTarget = true;
            // The same veil turned upside down fades upward from the bottom edge.
            bottomVeil = UIBuilder.CreateShape("BottomVeil", canvasRect, ui, UIRole.Veil, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(4000f, st.bottomBarHeight * 1.5f));
            bottomVeil.Pad = 0f;
            bottomVeil.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            bottomVeil.raycastTarget = true;

            var left = new Vector2(0f, 1f);
            title = UIBuilder.CreateText("Title", canvasRect, ui.FontBold, theme.sizeTitle, TextAnchor.MiddleLeft, left, left,
                new Vector2(pad, -st.topBarHeight * 0.5f + theme.sizeTitle * theme.lineHeight * 0.5f), new Vector2(400f, theme.sizeTitle * theme.lineHeight));
            title.text = UIStrings.TreeTitle;
            var top = new Vector2(0.5f, 1f);
            float midY = -st.topBarHeight * 0.5f;
            currencyValue = UIBuilder.CreateText("Currency", canvasRect, ui.FontBold, theme.sizeLarge, TextAnchor.MiddleCenter, top, new Vector2(0.5f, 0.5f),
                new Vector2(0f, midY), new Vector2(600f, theme.sizeLarge * theme.lineHeight));
            currencyLabel = UIBuilder.CreateText("CurrencyLabel", canvasRect, font, theme.sizeSmall, TextAnchor.MiddleRight, top, new Vector2(1f, 0.5f),
                new Vector2(0f, midY), new Vector2(300f, theme.sizeSmall * theme.lineHeight));
            currencyLabel.text = UIStrings.TreeCurrencyLabel;

            var bottomLeft = new Vector2(0f, 0f);
            hint = UIBuilder.CreateText("Hint", canvasRect, font, theme.sizeSmall, TextAnchor.MiddleLeft, bottomLeft, bottomLeft,
                new Vector2(pad, st.bottomBarHeight * 0.5f - theme.sizeSmall * theme.lineHeight * 0.5f), new Vector2(1200f, theme.sizeSmall * theme.lineHeight));
            hint.text = UIStrings.TreeHint;
            UIButton.Create(root, "NextRun", canvasRect, UIStrings.NextRunLabel, UIStrings.KeySpace, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-pad, (st.bottomBarHeight - st.nextRunButtonSize.y) * 0.5f), st.nextRunButtonSize, StartNextRun);
        }

        void BuildTooltip(RectTransform canvasRect, UIStyle ui)
        {
            tooltipShape = UIBuilder.CreateShape("Tooltip", canvasRect, ui, UIRole.Panel, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f),
                Vector2.zero, st.tooltipSize);
            tooltip = tooltipShape.rectTransform;
            float pad = st.tooltipPaddingPx;
            float w = st.tooltipSize.x - pad * 2f;
            float titleH = theme.sizeBody * theme.lineHeight;
            tooltipTitle = UIBuilder.CreateText("Title", tooltip, ui.FontBold, theme.sizeBody, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(pad, -pad * 0.75f), new Vector2(w, titleH));
            tooltipBody = UIBuilder.CreateText("Body", tooltip, font, theme.sizeSmall, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(pad, -pad * 0.75f - titleH - pad * 0.25f), new Vector2(w, st.tooltipSize.y - titleH - pad * 2f));
            // TextMeshPro: extra line space in hundredths of an em.
            tooltipBody.lineSpacing = 12f;
            tooltip.gameObject.SetActive(false);
        }

        static void SetActive(Component c, bool on)
        {
            if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }
    }
}
