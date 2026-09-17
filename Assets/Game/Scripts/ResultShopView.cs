using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// Result (spec §4) and shop (spec §5) on one panel, shown between runs.
    /// Result: run income, planets per tier, ratio vs last run (hidden on the first run), best run income.
    /// Shop: 5 upgrades + 2 unlocks with name, effect, level, cost and a buy button; "next run" button.
    /// Built from code; refreshed every frame while visible so debug currency changes show immediately.
    /// </summary>
    public sealed class ResultShopView : MonoBehaviour
    {
        sealed class Row
        {
            public bool isUnlock;
            public UpgradeId id;
            public int tier;
            public Text level;
            public Text cost;
            public Text buttonLabel;
            public Button button;
        }

        const float PanelWidth = 1000f;
        const float PanelHeight = 860f;
        const float RowHeight = 54f;

        GameRoot root;
        Font font;
        Canvas canvas;
        RectTransform panel;
        Text title, incomeValue, planetsValue, ratioLabel, ratioValue, bestValue, currencyText;
        readonly List<Row> rows = new List<Row>();
        readonly StringBuilder sb = new StringBuilder();
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

            var center = new Vector2(0.5f, 0.5f);
            panel = UIBuilder.CreateRect("Panel", canvasRect, center, center, Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            var panelImg = panel.gameObject.AddComponent<Image>();
            panelImg.color = new Color(0.08f, 0.09f, 0.13f, 0.97f);
            panelImg.raycastTarget = true;

            float y = -24f;
            title = Label("Title", 40, TextAnchor.UpperCenter, 0f, y, PanelWidth, 50f);
            y -= 72f;

            incomeValue = ResultRow("Income", UIStrings.ResultIncome, ref y, out _);
            planetsValue = ResultRow("Planets", UIStrings.ResultPlanets, ref y, out _);
            ratioValue = ResultRow("Ratio", UIStrings.ResultRatio, ref y, out ratioLabel);
            bestValue = ResultRow("Best", UIStrings.ResultBest, ref y, out _);
            y -= 24f;

            Label("ShopTitle", 32, TextAnchor.UpperLeft, 40f, y, 300f, 40f).text = UIStrings.ShopTitle;
            currencyText = Label("ShopCurrency", 26, TextAnchor.UpperRight, PanelWidth - 40f - 400f, y - 4f, 400f, 40f);
            y -= 50f;

            HeaderCell(UIStrings.ColumnName, 40f, y, 280f, TextAnchor.MiddleLeft);
            HeaderCell(UIStrings.ColumnEffect, 330f, y, 160f, TextAnchor.MiddleLeft);
            HeaderCell(UIStrings.ColumnLevel, 500f, y, 140f, TextAnchor.MiddleLeft);
            HeaderCell(UIStrings.ColumnCost, 650f, y, 140f, TextAnchor.MiddleRight);
            y -= 36f;

            var table = root.upgradeTable;
            foreach (var def in table.upgrades)
            {
                int idx = (int)def.id;
                string name = idx < UIStrings.UpgradeNames.Length ? UIStrings.UpgradeNames[idx] : def.id.ToString();
                string effect = idx < UIStrings.UpgradeEffects.Length ? string.Format(UIStrings.UpgradeEffects[idx], EffectNumber(def)) : string.Empty;
                var row = MakeRow(def.id.ToString(), name, effect, ref y);
                row.isUnlock = false;
                row.id = def.id;
                UpgradeId id = def.id;
                row.button.onClick.AddListener(() => { if (root.TryBuyUpgrade(id)) Refresh(); });
                rows.Add(row);
            }
            foreach (var unlock in table.unlocks)
            {
                var tierDef = root.celestialTable.Get(unlock.tier);
                string tierName = tierDef != null ? tierDef.name : unlock.tier.ToString();
                var row = MakeRow("Unlock" + unlock.tier, string.Format(UIStrings.UnlockName, tierName), string.Format(UIStrings.UnlockEffect, unlock.tier), ref y);
                row.isUnlock = true;
                row.tier = unlock.tier;
                int tier = unlock.tier;
                row.button.onClick.AddListener(() => { if (root.TryUnlock(tier)) Refresh(); });
                rows.Add(row);
            }

            UIBuilder.CreateButton("NextRun", panel, font, 30, UIStrings.NextRun,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(320f, 64f),
                () => root.RequestStartRun(), out _);

            root.RunEnded += OnRunEnded;
            root.RunStarted += OnRunStarted;
            canvas.gameObject.SetActive(false);
        }

        void Update()
        {
            if (IsVisible) Refresh();
        }

        void OnRunEnded(RunRecord rec)
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
            var t = root.upgradeTable;

            if (lastRecord != null)
            {
                title.text = string.Format(UIStrings.ResultTitle, lastRecord.run);
                incomeValue.text = Fmt.Int(lastRecord.income);
                planetsValue.text = PlanetsText(lastRecord);
                bool showRatio = !double.IsNaN(lastRecord.ratioVsLast) && !double.IsInfinity(lastRecord.ratioVsLast);
                if (ratioLabel.gameObject.activeSelf != showRatio) ratioLabel.gameObject.SetActive(showRatio);
                if (ratioValue.gameObject.activeSelf != showRatio) ratioValue.gameObject.SetActive(showRatio);
                if (showRatio) ratioValue.text = Fmt.Mult(lastRecord.ratioVsLast);
            }
            bestValue.text = Fmt.Int(m.bestRunIncome);
            currencyText.text = string.Format(UIStrings.ShopCurrency, Fmt.Int(m.currency));

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (!row.isUnlock)
                {
                    int lvl = m.GetLevel(row.id);
                    bool maxed = Shop.IsMaxed(t, m, row.id);
                    row.level.text = string.Format(maxed ? UIStrings.LevelMax : UIStrings.Level, lvl);
                    row.cost.text = maxed ? UIStrings.NoCost : Fmt.Int(Shop.UpgradeCost(t, m, row.id));
                    row.button.interactable = Shop.CanBuyUpgrade(t, m, row.id);
                    row.buttonLabel.text = maxed ? UIStrings.Max : UIStrings.Buy;
                }
                else
                {
                    bool unlocked = Shop.IsUnlocked(m, row.tier);
                    row.level.text = unlocked ? UIStrings.Unlocked : UIStrings.Locked;
                    row.cost.text = unlocked ? UIStrings.NoCost : Fmt.Int(Shop.UnlockCost(t, row.tier));
                    row.button.interactable = Shop.CanUnlock(t, m, row.tier);
                    row.buttonLabel.text = unlocked ? UIStrings.Unlocked : UIStrings.Buy;
                }
            }
        }

        string PlanetsText(RunRecord rec)
        {
            sb.Length = 0;
            var tiers = root.celestialTable.tiers;
            for (int i = 0; i < tiers.Count; i++)
            {
                int idx = tiers[i].tier - 1;
                int count = rec.tierCounts != null && idx >= 0 && idx < rec.tierCounts.Length ? rec.tierCounts[idx] : 0;
                if (i > 0) sb.Append(UIStrings.PlanetSeparator);
                sb.AppendFormat(UIStrings.PlanetCount, tiers[i].name, count);
            }
            return sb.ToString();
        }

        /// <summary>Effect number for display: fraction effects as percent, others as-is.</summary>
        static string EffectNumber(UpgradeDef def)
        {
            bool fraction = def.id == UpgradeId.PullAccel || def.id == UpgradeId.SaleMult || def.id == UpgradeId.ThresholdMult;
            double v = fraction ? def.effectPerLevel * 100.0 : def.effectPerLevel;
            return Fmt.Int(Math.Round(v));
        }

        // ---------------- layout helpers ----------------

        Text Label(string name, int size, TextAnchor align, float x, float y, float w, float h)
        {
            var t = UIBuilder.CreateText(name, panel, font, size, align, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(w, h));
            return t;
        }

        Text ResultRow(string name, string label, ref float y, out Text labelText)
        {
            labelText = Label(name + "Label", 28, TextAnchor.MiddleLeft, 60f, y, 400f, 40f);
            labelText.text = label;
            var value = Label(name + "Value", 28, TextAnchor.MiddleRight, PanelWidth - 60f - 600f, y, 600f, 40f);
            y -= 44f;
            return value;
        }

        void HeaderCell(string text, float x, float y, float w, TextAnchor align)
        {
            var t = Label("Header_" + text, 22, align, x, y, w, 30f);
            t.text = text;
            t.color = new Color(0.7f, 0.75f, 0.85f, 1f);
        }

        Row MakeRow(string name, string displayName, string effect, ref float y)
        {
            var row = new Row();
            Label(name + "Name", 26, TextAnchor.MiddleLeft, 40f, y, 280f, RowHeight).text = displayName;
            Label(name + "Effect", 22, TextAnchor.MiddleLeft, 330f, y, 160f, RowHeight).text = effect;
            row.level = Label(name + "Level", 24, TextAnchor.MiddleLeft, 500f, y, 140f, RowHeight);
            row.cost = Label(name + "Cost", 24, TextAnchor.MiddleRight, 650f, y, 140f, RowHeight);
            row.button = UIBuilder.CreateButton(name + "Buy", panel, font, 24, UIStrings.Buy,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(810f, y - 5f), new Vector2(150f, RowHeight - 10f),
                null, out row.buttonLabel);
            y -= RowHeight;
            return row;
        }
    }
}
