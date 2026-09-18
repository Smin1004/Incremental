using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// Skill tree screen (12 §9), between runs after the result screen. Constellation style: nodes are glowing points on
    /// rings (radius = treeRingR0Px + minTier × treeRingStepPx, angle = angleDeg, 12 §5), edges are thin lines, owned
    /// paths are bright. Node states follow 12 §4. Click buys one level, holding repeats, drag pans, the wheel zooms,
    /// hovering shows a tooltip. Opening moves the view to the frontier (the purchasable nodes). Currency is pinned to the
    /// top; "next run" and Space always work. Built from code in its own canvas.
    /// </summary>
    public sealed class TreeView : MonoBehaviour
    {
        struct Edge
        {
            public Image image;
            public TreeNodeWidget from;
            public TreeNodeWidget to;
        }

        // Presentation (canvas units at the 1080p reference).
        const float StatSize = 18f, GateSize = 30f, CenterSize = 44f, GlowScale = 2.6f;
        const float EdgeWidth = 2f, RingWidth = 2f;
        const int FlashPool = 8;
        const float FlashSec = 0.4f;
        const float TopBarHeight = 80f, BottomBarHeight = 110f, ViewMargin = 90f;

        static readonly Color BgColor = new Color(0.012f, 0.016f, 0.04f, 0.97f);
        static readonly Color LockedColor = new Color(0.45f, 0.5f, 0.62f, 0.55f);
        static readonly Color ShortColor = new Color(0.75f, 0.8f, 0.95f, 0.9f);
        static readonly Color OwnedColor = new Color(1f, 0.93f, 0.72f, 1f);
        static readonly Color MaxedColor = new Color(1f, 0.84f, 0.45f, 1f);
        static readonly Color EdgeDim = new Color(0.5f, 0.55f, 0.72f, 0.18f);
        static readonly Color EdgeOwned = new Color(0.95f, 0.88f, 0.62f, 0.6f);
        static readonly Color EdgeMaxed = new Color(1f, 0.9f, 0.55f, 0.95f);
        static readonly Color LabelDim = new Color(0.65f, 0.7f, 0.8f, 0.7f);
        static readonly Color LabelOk = new Color(0.95f, 0.95f, 1f, 1f);
        static readonly Color LabelShort = new Color(1f, 0.45f, 0.42f, 1f);

        GameRoot root;
        GameParams p;
        NodeTable t;
        Font font;
        Canvas canvas;
        RectTransform area, content, ringsLayer, edgesLayer, nodesLayer, flashLayer, tooltip;
        Text currencyText, tooltipTitle, tooltipBody;
        readonly List<TreeNodeWidget> widgets = new List<TreeNodeWidget>();
        readonly Dictionary<string, TreeNodeWidget> byId = new Dictionary<string, TreeNodeWidget>();
        readonly List<Edge> edges = new List<Edge>();
        readonly List<UIRing> rings = new List<UIRing>();
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

        public bool IsVisible => canvas != null && canvas.gameObject.activeSelf;

        public void Init(GameRoot gameRoot)
        {
            root = gameRoot;
            p = root.gameParams;
            t = root.nodeTable;
            font = root.Hud.Font;

            canvas = UIBuilder.CreateCanvas("SkillTree", transform);
            canvas.sortingOrder = 20;
            var canvasRect = (RectTransform)canvas.transform;
            var c = new Vector2(0.5f, 0.5f);

            area = UIBuilder.CreateStretch("Area", canvasRect);
            var bg = area.gameObject.AddComponent<Image>();
            bg.color = BgColor;
            bg.raycastTarget = true;
            area.gameObject.AddComponent<TreeDragArea>().view = this;
            BuildStarfield();

            content = UIBuilder.CreateRect("Content", area, c, c, Vector2.zero, Vector2.zero);
            ringsLayer = UIBuilder.CreateRect("Rings", content, c, c, Vector2.zero, Vector2.zero);
            edgesLayer = UIBuilder.CreateRect("Edges", content, c, c, Vector2.zero, Vector2.zero);
            flashLayer = UIBuilder.CreateRect("Flashes", content, c, c, Vector2.zero, Vector2.zero);
            nodesLayer = UIBuilder.CreateRect("Nodes", content, c, c, Vector2.zero, Vector2.zero);

            int ringCount = root.celestialTable.MaxTier;
            for (int k = 1; k <= ringCount; k++)
            {
                var rt = UIBuilder.CreateRect("Ring" + k, ringsLayer, c, c, Vector2.zero, Vector2.zero);
                var ring = rt.gameObject.AddComponent<UIRing>();
                ring.raycastTarget = false;
                ring.Set(RingRadius(k), RingWidth, 128);
                rings.Add(ring);
            }

            center = MakeWidget(null, Vector2.zero, CenterSize);
            foreach (var n in t.nodes)
            {
                var w = MakeWidget(n, NodePosition(n), n.IsGate ? GateSize : StatSize);
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

            BuildBars(canvasRect);
            BuildTooltip(canvasRect);

            root.RunStarted += Hide;
            canvas.gameObject.SetActive(false);
        }

        // ---------------- open / close ----------------

        public void Show()
        {
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
            if (!IsVisible) return;

            var kb = Keyboard.current;
            if (kb != null && kb.spaceKey.wasPressedThisFrame && Time.frameCount != openedFrame)
            {
                root.RequestStartRun();
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
            UpdateFlashes(Time.unscaledDeltaTime);
            if (hovered != null) UpdateTooltip(hovered);
        }

        // ---------------- node visuals ----------------

        void Refresh()
        {
            var m = root.Meta;
            currencyText.text = string.Format(UIStrings.TreeCurrency, Fmt.Num(m.currency));
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);

            center.core.color = OwnedColor;
            center.glow.color = new Color(1f, 0.85f, 0.6f, 0.55f);
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
                    case NodeState.Maxed: core = n.IsGate ? tint : MaxedColor; glowA = 0.8f; break;
                    case NodeState.Owned: core = n.IsGate ? tint : OwnedColor; glowA = affordable ? 0.35f + 0.35f * pulse : 0.35f; break;
                    case NodeState.Available: core = affordable ? Color.Lerp(tint, Color.white, 0.4f) : ShortColor * tint; glowA = affordable ? 0.25f + 0.5f * pulse : 0.12f; break;
                    default: core = LockedColor * new Color(tint.r, tint.g, tint.b, 1f); glowA = 0f; break;
                }
                w.core.color = core;
                w.glow.color = new Color(core.r, core.g, core.b, glowA);

                string levelText = n.maxLevel > 0 ? string.Format(UIStrings.LevelShort, level, n.maxLevel) : string.Format(UIStrings.Level, level);
                if (maxed) w.label.text = levelText;
                else if (level > 0) w.label.text = levelText + "\n" + Fmt.Num(cost, Rounding.Up);
                else w.label.text = Fmt.Num(cost, Rounding.Up);
                w.label.color = state == NodeState.Locked ? LabelDim : (maxed || affordable ? LabelOk : LabelShort);
            }

            foreach (var e in edges)
            {
                bool visible = e.from.gameObject.activeSelf && e.to.gameObject.activeSelf;
                SetActive(e.image, visible);
                if (!visible) continue;
                bool fromOwned = e.from.node == null || Shop.IsOwned(m, e.from.node);
                bool toOwned = Shop.IsOwned(m, e.to.node);
                e.image.color = fromOwned && toOwned ? (Shop.IsMaxed(m, e.to.node) ? EdgeMaxed : EdgeOwned) : EdgeDim;
            }

            for (int i = 0; i < rings.Count; i++)
            {
                int k = i + 1;
                bool visible = k <= m.unlockedMaxTier + 1;
                SetActive(rings[i], visible);
                if (visible) rings[i].color = new Color(0.6f, 0.68f, 0.9f, k <= m.unlockedMaxTier ? 0.22f : 0.07f);
            }
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
            Flash(w);
            return true;
        }

        void Flash(TreeNodeWidget w)
        {
            float intensity = Mathf.Clamp01((float)p.effectIntensity);
            if (intensity <= 0f) return;
            int i = nextFlash;
            nextFlash = (nextFlash + 1) % FlashPool;
            var f = flashes[i];
            var rt = (RectTransform)f.transform;
            rt.anchoredPosition = w.rt.anchoredPosition;
            rt.sizeDelta = Vector2.one * w.size * GlowScale;
            flashAge[i] = 0f;
            f.gameObject.SetActive(true);
        }

        void UpdateFlashes(float dt)
        {
            float intensity = Mathf.Clamp01((float)p.effectIntensity);
            for (int i = 0; i < FlashPool; i++)
            {
                var f = flashes[i];
                if (!f.gameObject.activeSelf) continue;
                flashAge[i] += dt;
                float k = flashAge[i] / FlashSec;
                if (k >= 1f)
                {
                    f.gameObject.SetActive(false);
                    continue;
                }
                f.transform.localScale = Vector3.one * (1f + 2.5f * k);
                f.color = new Color(1f, 0.95f, 0.8f, 0.9f * intensity * (1f - k));
            }
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
            float halfW = r.width * 0.5f - ViewMargin;
            float halfH = (r.height - TopBarHeight - BottomBarHeight) * 0.5f - ViewMargin * 0.5f;
            float fit = extent > 1f ? Mathf.Min(halfW, halfH) / extent : 1f;
            float z = Mathf.Clamp(Mathf.Min(1f, fit), (float)p.treeZoomMin, (float)p.treeZoomMax);

            panFrom = pan;
            zoomFrom = zoom;
            panTo = -focus * z + new Vector2(0f, (BottomBarHeight - TopBarHeight) * 0.5f);
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
                    sb.Append(string.Format(UIStrings.GateEffect, n.tier, Fmt.Num(price), Fmt.Num(mass, Rounding.Up)));
                }
                else
                {
                    sb.Append(string.Format(UIStrings.EffectPerLevel, UIStrings.NodeEffect(n)));
                }
                int level = m.GetLevel(n.id);
                sb.Append('\n').Append(n.maxLevel > 0 ? string.Format(UIStrings.LevelOf, level, n.maxLevel) : string.Format(UIStrings.Level, level));
                var state = Shop.State(t, m, n);
                if (state == NodeState.Maxed) sb.Append('\n').Append(UIStrings.Maxed);
                else
                {
                    double cost = Shop.Cost(t, m, n);
                    bool enough = m.currency >= cost;
                    sb.Append('\n').Append("<color=").Append(enough ? "#FFFFFF" : "#FF7266").Append('>')
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
                float x = Mathf.Clamp(local.x + 24f, cr.xMin + 8f, cr.xMax - size.x - 8f);
                float y = Mathf.Clamp(local.y - 24f, cr.yMin + size.y + 8f, cr.yMax - 8f);
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
            w.glow = UIBuilder.CreateImage("Glow", rt, Color.clear, c, c, Vector2.zero, Vector2.one * size * GlowScale, UIBuilder.SoftDotSprite());
            w.core = UIBuilder.CreateImage("Core", rt, Color.white, c, c, Vector2.zero, Vector2.one * size, root.circleSprite);
            w.core.raycastTarget = true;
            w.label = UIBuilder.CreateText("Label", rt, font, 15, TextAnchor.UpperCenter, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(0f, -4f), new Vector2(140f, 40f));
            return w;
        }

        void AddEdge(TreeNodeWidget from, TreeNodeWidget to)
        {
            var img = UIBuilder.CreateImage("Edge_" + (from.node != null ? from.node.id : "center") + "_" + to.node.id, edgesLayer,
                EdgeDim, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one, UIBuilder.SolidSprite());
            UIBuilder.PlaceLine((RectTransform)img.transform, from.rt.anchoredPosition, to.rt.anchoredPosition, EdgeWidth);
            edges.Add(new Edge { image = img, from = from, to = to });
        }

        void BuildStarfield()
        {
            var rng = new System.Random(7);
            var c = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < 140; i++)
            {
                float x = (float)(rng.NextDouble() - 0.5) * 1920f;
                float y = (float)(rng.NextDouble() - 0.5) * 1080f;
                float s = 2f + (float)rng.NextDouble() * 4f;
                float a = 0.15f + (float)rng.NextDouble() * 0.45f;
                UIBuilder.CreateImage("Star", area, new Color(0.8f, 0.85f, 1f, a), c, c, new Vector2(x, y), new Vector2(s, s), UIBuilder.SoftDotSprite());
            }
        }

        void BuildBars(RectTransform canvasRect)
        {
            var top = UIBuilder.CreateImage("TopBar", canvasRect, new Color(0f, 0f, 0f, 0.45f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(4000f, TopBarHeight));
            top.raycastTarget = true;
            UIBuilder.CreateText("Title", canvasRect, font, 34, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(32f, -12f), new Vector2(400f, 56f)).text = UIStrings.TreeTitle;
            currencyText = UIBuilder.CreateText("Currency", canvasRect, font, 34, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(800f, 56f));
            var hint = UIBuilder.CreateText("Hint", canvasRect, font, 20, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(32f, 28f), new Vector2(1200f, 40f));
            hint.text = UIStrings.TreeHint;
            hint.color = LabelDim;
            UIBuilder.CreateButton("NextRun", canvasRect, font, 30, UIStrings.NextRun, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-32f, 24f), new Vector2(360f, 76f), () => root.RequestStartRun(), out _);
        }

        void BuildTooltip(RectTransform canvasRect)
        {
            var img = UIBuilder.CreateImage("Tooltip", canvasRect, new Color(0.05f, 0.06f, 0.1f, 0.95f), new Vector2(0.5f, 0.5f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(380f, 150f), UIBuilder.SolidSprite());
            tooltip = (RectTransform)img.transform;
            tooltipTitle = UIBuilder.CreateText("Title", tooltip, font, 24, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -12f), new Vector2(350f, 32f));
            tooltipBody = UIBuilder.CreateText("Body", tooltip, font, 19, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -48f), new Vector2(350f, 100f));
            tooltipBody.supportRichText = true;
            tooltipBody.lineSpacing = 1.1f;
            tooltip.gameObject.SetActive(false);
        }

        static void SetActive(Component c, bool on)
        {
            if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }
    }
}
