using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// Result (11 §4) and shop on one screen, shown between runs, as two panels side by side at 1920x1080:
    /// result on the left (run income, planets per tier for tiers made at least once, ratio vs last run hidden when there
    /// is none, best run income, "next run" button), shop on the right.
    /// Shop (12 §8, step 1 of the skill tree): the purchasable nodes, cheapest first, at most <see cref="MaxNodeRows"/>,
    /// and the next gate as the last row. Locked nodes appear in the tree view (step 2).
    /// Built from code; refreshed every frame while visible so debug currency changes show immediately.
    /// </summary>
    public sealed class ResultShopView : MonoBehaviour
    {
        sealed class Row
        {
            /// <summary>The node shown in this row this frame (rows are reassigned as the list changes).</summary>
            public NodeDef node;
            public RectTransform rt;
            public Image bg;
            public Text name, effect, level, cost, buttonLabel;
            public Button button;
        }

        // Layout (reference 1920x1080).
        const float LeftWidth = 640f;
        const float RightWidth = 1120f;
        const float PanelHeight = 880f;
        const float PanelGap = 24f;
        const float RowHeight = 56f;
        const float RowsTop = -132f;
        const float ColName = 40f, ColEffect = 350f, ColLevel = 610f, ColCost = 780f, ColButton = 960f;
        const float WName = 300f, WEffect = 250f, WLevel = 160f, WCost = 160f, WButton = 130f;
        const int PlanetLinesPerColumn = 6;
        /// <summary>Stat node rows; with the gate row the list has 11 rows, what fits the panel at 1080p.</summary>
        const int MaxNodeRows = 10;

        static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.13f, 0.97f);
        static readonly Color LabelColor = new Color(0.7f, 0.75f, 0.85f, 1f);
        static readonly Color RowColorA = new Color(1f, 1f, 1f, 0.035f);
        static readonly Color RowColorB = new Color(1f, 1f, 1f, 0f);
        static readonly Color CostColor = Color.white;
        static readonly Color CostShortColor = new Color(1f, 0.45f, 0.42f, 1f);

        GameRoot root;
        Font font;
        Canvas canvas;
        RectTransform left, right;
        Text title, incomeValue, planetsColA, planetsColB, ratioLabel, ratioValue, bestValue, currencyText;
        readonly List<Row> rows = new List<Row>();
        Row gateRow;
        readonly List<NodeDef> purchasable = new List<NodeDef>();
        readonly Dictionary<NodeDef, string> nodeNames = new Dictionary<NodeDef, string>();
        readonly StringBuilder sbA = new StringBuilder();
        readonly StringBuilder sbB = new StringBuilder();
        RunRecord lastRecord;

        public bool IsVisible => canvas != null && canvas.gameObject.activeSelf;

        public void Init(GameRoot gameRoot)
        {
            root = gameRoot;
            font = root.Hud.Font;
            canvas = UIBuilder.CreateCanvas("ResultShop", transform);
            canvas.sortingOrder = 10;
            var canvasRect = (RectTransform)canvas.transform;

            var dim = UIBuilder.CreateStretch("Dim", canvasRect).gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            dim.raycastTarget = true;

            float total = LeftWidth + PanelGap + RightWidth;
            left = Panel("ResultPanel", canvasRect, -total * 0.5f + LeftWidth * 0.5f, LeftWidth);
            right = Panel("ShopPanel", canvasRect, total * 0.5f - RightWidth * 0.5f, RightWidth);

            BuildResult();
            BuildShop();

            root.RunEnded += OnRunEnded;
            root.RunStarted += OnRunStarted;
            canvas.gameObject.SetActive(false);
        }

        void BuildResult()
        {
            title = Label(left, "Title", 44, TextAnchor.MiddleCenter, 0f, -28f, LeftWidth, 56f);

            Label(left, "IncomeLabel", 26, TextAnchor.MiddleLeft, 40f, -110f, 560f, 34f, LabelColor).text = UIStrings.ResultIncome;
            incomeValue = Label(left, "IncomeValue", 64, TextAnchor.MiddleLeft, 40f, -146f, 560f, 76f);

            Label(left, "PlanetsLabel", 26, TextAnchor.MiddleLeft, 40f, -240f, 560f, 34f, LabelColor).text = UIStrings.ResultPlanets;
            planetsColA = Label(left, "PlanetsA", 26, TextAnchor.UpperLeft, 56f, -282f, 270f, 230f);
            planetsColB = Label(left, "PlanetsB", 26, TextAnchor.UpperLeft, 336f, -282f, 270f, 230f);

            ratioLabel = Label(left, "RatioLabel", 28, TextAnchor.MiddleLeft, 40f, -532f, 300f, 48f, LabelColor);
            ratioLabel.text = UIStrings.ResultRatio;
            ratioValue = Label(left, "RatioValue", 40, TextAnchor.MiddleRight, LeftWidth - 40f - 280f, -532f, 280f, 48f);

            Label(left, "BestLabel", 28, TextAnchor.MiddleLeft, 40f, -592f, 300f, 48f, LabelColor).text = UIStrings.ResultBest;
            bestValue = Label(left, "BestValue", 32, TextAnchor.MiddleRight, LeftWidth - 40f - 280f, -592f, 280f, 48f);

            UIBuilder.CreateButton("NextRun", left, font, 32, UIStrings.NextRun,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(400f, 80f),
                () => root.RequestStartRun(), out _);
        }

        void BuildShop()
        {
            Label(right, "ShopTitle", 36, TextAnchor.MiddleLeft, ColName, -28f, 300f, 48f).text = UIStrings.ShopTitle;
            currencyText = Label(right, "ShopCurrency", 30, TextAnchor.MiddleRight, RightWidth - 40f - 600f, -28f, 600f, 48f);

            HeaderCell(UIStrings.ColumnName, ColName, WName, TextAnchor.MiddleLeft);
            HeaderCell(UIStrings.ColumnEffect, ColEffect, WEffect, TextAnchor.MiddleLeft);
            HeaderCell(UIStrings.ColumnLevel, ColLevel, WLevel, TextAnchor.MiddleLeft);
            HeaderCell(UIStrings.ColumnCost, ColCost, WCost, TextAnchor.MiddleRight);

            for (int i = 0; i < MaxNodeRows; i++)
            {
                var row = MakeRow("Node" + i);
                row.button.onClick.AddListener(() => { if (row.node != null && root.TryBuy(row.node.id)) Refresh(); });
                rows.Add(row);
            }
            gateRow = MakeRow("Gate");
            gateRow.button.onClick.AddListener(() => { if (gateRow.node != null && root.TryBuy(gateRow.node.id)) Refresh(); });
        }

        void Update()
        {
            if (IsVisible) Refresh();
        }

        void OnRunEnded(RunRecord rec) => Show(rec);

        /// <summary>Opens the screen with this run as the result (null: no run yet, only the shop and best income).</summary>
        public void Show(RunRecord rec)
        {
            lastRecord = rec;
            canvas.gameObject.SetActive(true);
            Refresh();
        }

        void OnRunStarted()
        {
            canvas.gameObject.SetActive(false);
        }

        public void Refresh()
        {
            var m = root.Meta;
            var t = root.nodeTable;

            if (lastRecord != null)
            {
                title.text = string.Format(UIStrings.ResultTitle, lastRecord.run);
                incomeValue.text = Fmt.Num(lastRecord.income);
                FillPlanets(lastRecord);
            }
            else
            {
                title.text = string.Empty;
                incomeValue.text = UIStrings.NoPlanets;
                planetsColA.text = UIStrings.NoPlanets;
                planetsColB.text = string.Empty;
            }
            bool showRatio = lastRecord != null && lastRecord.HasRatio;
            SetActive(ratioLabel, showRatio);
            SetActive(ratioValue, showRatio);
            if (showRatio) ratioValue.text = Fmt.Mult(lastRecord.ratioVsLast);
            bestValue.text = Fmt.Num(m.bestRunIncome);
            currencyText.text = string.Format(UIStrings.ShopCurrency, Fmt.Num(m.currency));

            // Purchasable stat nodes, cheapest first.
            Shop.PurchasableStatNodes(t, m, purchasable);
            float y = RowsTop;
            int shown = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                row.node = i < purchasable.Count ? purchasable[i] : null;
                SetActive(row.rt, row.node != null);
                if (row.node == null) continue;
                Place(row, ref y, shown++);

                var n = row.node;
                int lvl = m.GetLevel(n.id);
                double cost = Shop.Cost(t, m, n);
                row.name.text = NodeName(n);
                row.effect.text = UIStrings.NodeEffect(n);
                row.level.text = n.maxLevel > 0 ? string.Format(UIStrings.LevelOf, lvl, n.maxLevel) : string.Format(UIStrings.Level, lvl);
                row.cost.text = Fmt.Num(cost, Rounding.Up);
                row.cost.color = m.currency >= cost ? CostColor : CostShortColor;
                row.button.interactable = m.currency >= cost;
            }

            // Last row: the next gate, or "all unlocked".
            Place(gateRow, ref y, shown);
            var gate = Shop.NextGate(t, m);
            gateRow.node = gate != null && Shop.IsPurchasable(t, m, gate) ? gate : null;
            SetActive(gateRow.button, gateRow.node != null);
            if (gateRow.node != null)
            {
                var tier = root.celestialTable.Get(gate.tier);
                double price = tier != null ? Stats.SaleIncome(tier, Stats.Compute(root.gameParams, t, m)) : 0.0;
                double cost = Shop.Cost(t, m, gate);
                gateRow.name.text = string.Format(UIStrings.UnlockName, UIStrings.TierName(gate.tier));
                gateRow.effect.text = string.Format(UIStrings.UnlockEffect, gate.tier, Fmt.Num(price));
                gateRow.level.text = string.Empty;
                gateRow.cost.text = Fmt.Num(cost, Rounding.Up);
                gateRow.cost.color = m.currency >= cost ? CostColor : CostShortColor;
                gateRow.button.interactable = m.currency >= cost;
            }
            else
            {
                gateRow.name.text = UIStrings.AllUnlocked;
                gateRow.effect.text = string.Empty;
                gateRow.level.text = string.Empty;
                gateRow.cost.text = string.Empty;
            }
        }

        string NodeName(NodeDef n)
        {
            if (!nodeNames.TryGetValue(n, out var name))
            {
                name = UIStrings.NodeName(root.nodeTable, n);
                nodeNames[n] = name;
            }
            return name;
        }

        /// <summary>Planets per tier, only tiers made at least once, split over two columns.</summary>
        void FillPlanets(RunRecord rec)
        {
            sbA.Length = 0;
            sbB.Length = 0;
            int n = 0;
            var tiers = root.celestialTable.tiers;
            for (int i = 0; i < tiers.Count; i++)
            {
                int count = rec.TierCount(tiers[i].tier);
                if (count <= 0) continue;
                var sb = n < PlanetLinesPerColumn ? sbA : sbB;
                if (sb.Length > 0) sb.Append('\n');
                sb.AppendFormat(UIStrings.PlanetCount, UIStrings.TierName(tiers[i].tier), count);
                n++;
            }
            planetsColA.text = n == 0 ? UIStrings.NoPlanets : sbA.ToString();
            planetsColB.text = sbB.ToString();
        }

        // ---------------- layout helpers ----------------

        RectTransform Panel(string name, RectTransform parent, float centerX, float width)
        {
            var center = new Vector2(0.5f, 0.5f);
            var rt = UIBuilder.CreateRect(name, parent, center, center, new Vector2(centerX, 0f), new Vector2(width, PanelHeight));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = PanelColor;
            img.raycastTarget = true;
            return rt;
        }

        Text Label(RectTransform parent, string name, int size, TextAnchor align, float x, float y, float w, float h) =>
            UIBuilder.CreateText(name, parent, font, size, align, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(w, h));

        Text Label(RectTransform parent, string name, int size, TextAnchor align, float x, float y, float w, float h, Color color)
        {
            var t = Label(parent, name, size, align, x, y, w, h);
            t.color = color;
            return t;
        }

        void HeaderCell(string text, float x, float w, TextAnchor align)
        {
            Label(right, "Header_" + text, 22, align, x, -96f, w, 30f, LabelColor).text = text;
        }

        Row MakeRow(string name)
        {
            var row = new Row();
            var top = new Vector2(0f, 1f);
            row.rt = UIBuilder.CreateRect(name, right, top, top, new Vector2(0f, RowsTop), new Vector2(RightWidth, RowHeight));
            row.bg = UIBuilder.CreateImage("Bg", row.rt, RowColorB, top, top, Vector2.zero, new Vector2(RightWidth, RowHeight), UIBuilder.SolidSprite());
            row.name = Label(row.rt, "Name", 26, TextAnchor.MiddleLeft, ColName, 0f, WName, RowHeight);
            row.effect = Label(row.rt, "Effect", 22, TextAnchor.MiddleLeft, ColEffect, 0f, WEffect, RowHeight);
            row.level = Label(row.rt, "Level", 24, TextAnchor.MiddleLeft, ColLevel, 0f, WLevel, RowHeight);
            row.cost = Label(row.rt, "Cost", 24, TextAnchor.MiddleRight, ColCost, 0f, WCost, RowHeight);
            row.button = UIBuilder.CreateButton("Buy", row.rt, font, 24, UIStrings.Buy,
                top, top, new Vector2(ColButton, -5f), new Vector2(WButton, RowHeight - 10f), null, out row.buttonLabel);
            return row;
        }

        static void Place(Row row, ref float y, int visibleIndex)
        {
            if (row.rt.anchoredPosition.y != y) row.rt.anchoredPosition = new Vector2(0f, y);
            row.bg.color = (visibleIndex & 1) == 0 ? RowColorA : RowColorB;
            y -= RowHeight;
        }

        static void SetActive(Component c, bool on)
        {
            if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }
    }
}
