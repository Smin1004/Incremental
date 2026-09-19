using System;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// Result screen (11 §4, 13 §3, 14 §7): a panel on the left with the run income (counting up during the entry animation),
    /// the ratio vs the last full run, planets per tier made this run with the increase against the comparison run (the
    /// ratio's run; decreases are left blank, 15 §3) and a "new body" tag for tiers made for the first time, the best
    /// income and a "skill tree" button. Space or the button
    /// opens the tree (12 §9 flow: result → Space → tree → Space → next run). A click anywhere skips the entry animation.
    /// </summary>
    public sealed class ResultView : MonoBehaviour
    {
        const float PanelWidth = 600f;
        const float PanelHeight = 820f;
        const int PlanetFontSize = 21;
        const float PlanetLineSpacing = 0.88f;

        static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.1f, 0.88f);
        static readonly Color LabelColor = new Color(0.7f, 0.75f, 0.85f, 1f);

        GameRoot root;
        Font font;
        Canvas canvas;
        RectTransform panel;
        Text title, incomeValue, planetNames, planetCounts, planetDeltas, ratioLabel, ratioValue, bestValue;
        readonly StringBuilder sbNames = new StringBuilder();
        readonly StringBuilder sbCounts = new StringBuilder();
        readonly StringBuilder sbDeltas = new StringBuilder();
        RunRecord record;
        RunRecord comparison;
        bool fresh;
        float entryAge;
        bool entryDone;

        public bool IsVisible => canvas != null && canvas.gameObject.activeSelf;

        /// <summary>Raised when the entry animation is skipped (click) so other result visuals can jump to their final state.</summary>
        public event Action EntrySkipped;

        public void Init(GameRoot gameRoot)
        {
            root = gameRoot;
            font = root.Hud.Font;
            canvas = UIBuilder.CreateCanvas("Result", transform);
            canvas.sortingOrder = 10;
            var canvasRect = (RectTransform)canvas.transform;

            var catcher = UIBuilder.CreateStretch("ClickCatcher", canvasRect).gameObject;
            var img = catcher.AddComponent<Image>();
            img.color = Color.clear;
            img.raycastTarget = true;
            catcher.AddComponent<ClickCatcher>().onClick = SkipEntry;

            panel = UIBuilder.CreateRect("Panel", canvasRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(48f, 0f),
                new Vector2(PanelWidth, PanelHeight));
            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = PanelColor;
            bg.raycastTarget = true;
            panel.gameObject.AddComponent<ClickCatcher>().onClick = SkipEntry;

            title = Label("Title", 44, TextAnchor.MiddleCenter, 0f, -28f, PanelWidth, 56f);
            Label("IncomeLabel", 26, TextAnchor.MiddleLeft, 40f, -110f, 520f, 34f, LabelColor).text = UIStrings.ResultIncome;
            incomeValue = Label("IncomeValue", 64, TextAnchor.MiddleLeft, 40f, -146f, 520f, 76f);

            ratioLabel = Label("RatioLabel", 28, TextAnchor.MiddleLeft, 40f, -236f, 300f, 48f, LabelColor);
            ratioLabel.text = UIStrings.ResultRatio;
            ratioValue = Label("RatioValue", 40, TextAnchor.MiddleRight, PanelWidth - 40f - 280f, -236f, 280f, 48f);

            Label("PlanetsLabel", 26, TextAnchor.MiddleLeft, 40f, -306f, 520f, 34f, LabelColor).text = UIStrings.ResultPlanets;
            // One row per tier in three aligned columns: name, count, change / new tag (up to 11 rows).
            planetNames = Label("PlanetNames", PlanetFontSize, TextAnchor.UpperLeft, 56f, -346f, 190f, 290f);
            planetCounts = Label("PlanetCounts", PlanetFontSize, TextAnchor.UpperRight, 246f, -346f, 110f, 290f);
            planetDeltas = Label("PlanetDeltas", PlanetFontSize, TextAnchor.UpperLeft, 376f, -346f, 200f, 290f);
            planetDeltas.supportRichText = true;
            planetNames.lineSpacing = planetCounts.lineSpacing = planetDeltas.lineSpacing = PlanetLineSpacing;

            Label("BestLabel", 28, TextAnchor.MiddleLeft, 40f, -640f, 300f, 48f, LabelColor).text = UIStrings.ResultBest;
            bestValue = Label("BestValue", 32, TextAnchor.MiddleRight, PanelWidth - 40f - 280f, -640f, 280f, 48f);

            UIBuilder.CreateButton("OpenTree", panel, font, 30, UIStrings.OpenTree,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(420f, 80f), OpenTree, out _);

            root.RunEnded += rec => Show(rec, true);
            root.RunStarted += Hide;
            canvas.gameObject.SetActive(false);
        }

        /// <summary>
        /// Opens the screen with this run as the result (null: no run yet). <paramref name="freshRun"/>: right after the
        /// run ended (the "new body" highlight is shown only then, 14 §7); false when reopening a save.
        /// </summary>
        public void Show(RunRecord rec, bool freshRun)
        {
            record = rec;
            fresh = freshRun;
            comparison = ResultMath.ComparisonRun(root.Meta.runHistory, rec);
            entryAge = 0f;
            entryDone = rec == null;
            canvas.gameObject.SetActive(true);
            if (root.Solar != null) root.Solar.Show(rec, freshRun);
            Refresh();
        }

        public void Hide()
        {
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        void Update()
        {
            // The debug gallery hides this canvas without deactivating it; keys must not reach a hidden screen.
            if (!IsVisible || (root.Gallery != null && root.Gallery.IsOpen)) return;
            var kb = Keyboard.current;
            if (kb != null && kb.spaceKey.wasPressedThisFrame)
            {
                OpenTree();
                return;
            }
            if (!entryDone)
            {
                entryAge += Time.unscaledDeltaTime;
                if (entryAge >= EntrySec) entryDone = true;
            }
            Refresh();
        }

        float EntrySec => Mathf.Max(0.01f, (float)root.gameParams.resultEntrySec);

        void OpenTree()
        {
            Hide();
            root.Tree.Show();
        }

        void SkipEntry()
        {
            if (entryDone) return;
            entryDone = true;
            EntrySkipped?.Invoke();
        }

        void Refresh()
        {
            var m = root.Meta;
            if (record != null)
            {
                float k = entryDone ? 1f : Mathf.Clamp01(entryAge / EntrySec);
                float eased = 1f - (1f - k) * (1f - k) * (1f - k);
                title.text = string.Format(UIStrings.ResultTitle, record.run);
                incomeValue.text = Fmt.Num(Math.Floor(record.income * eased));
                FillPlanets(record);
            }
            else
            {
                title.text = string.Empty;
                incomeValue.text = UIStrings.NoPlanets;
                planetNames.text = UIStrings.NoPlanets;
                planetCounts.text = string.Empty;
                planetDeltas.text = string.Empty;
            }
            bool showRatio = record != null && record.HasRatio;
            SetActive(ratioLabel, showRatio);
            SetActive(ratioValue, showRatio);
            if (showRatio) ratioValue.text = Fmt.Mult(record.ratioVsLast);
            bestValue.text = Fmt.Num(m.bestRunIncome);
        }

        /// <summary>
        /// Planets per tier made this run, with the increase against the comparison run (▲3) and a "new body" tag for tiers
        /// made for the first time ever (fresh results only). A decrease leaves the change column blank: it is mostly the
        /// result of moving up to a higher tier and should not read as a penalty (15 §3); the ratio shows overall growth.
        /// </summary>
        void FillPlanets(RunRecord rec)
        {
            sbNames.Length = 0;
            sbCounts.Length = 0;
            sbDeltas.Length = 0;
            var p = root.gameParams;
            string up = ColorUtility.ToHtmlStringRGB(p.deltaUpColor);
            string newColor = ColorUtility.ToHtmlStringRGB(p.newTierColor);
            int n = 0;
            var tiers = root.celestialTable.tiers;
            for (int i = 0; i < tiers.Count; i++)
            {
                int t = tiers[i].tier;
                int count = rec.TierCount(t);
                if (count <= 0) continue;
                if (n > 0)
                {
                    sbNames.Append('\n');
                    sbCounts.Append('\n');
                    sbDeltas.Append('\n');
                }
                sbNames.Append(UIStrings.TierName(t));
                sbCounts.Append(Fmt.Num(count));
                int delta = ResultMath.ShownIncrease(rec, comparison, t);
                if (delta > 0) sbDeltas.Append("<color=#").Append(up).Append('>').AppendFormat(UIStrings.DeltaUp, Fmt.Num(delta)).Append("</color>");
                if (fresh && ResultMath.IsNewTier(root.Meta, rec, t))
                {
                    if (delta > 0) sbDeltas.Append("  ");
                    sbDeltas.Append("<color=#").Append(newColor).Append('>').Append(UIStrings.NewBody).Append("</color>");
                }
                n++;
            }
            planetNames.text = n == 0 ? UIStrings.NoPlanets : sbNames.ToString();
            planetCounts.text = sbCounts.ToString();
            planetDeltas.text = sbDeltas.ToString();
        }

        Text Label(string name, int size, TextAnchor align, float x, float y, float w, float h) =>
            UIBuilder.CreateText(name, panel, font, size, align, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(w, h));

        Text Label(string name, int size, TextAnchor align, float x, float y, float w, float h, Color color)
        {
            var t = Label(name, size, align, x, y, w, h);
            t.color = color;
            return t;
        }

        static void SetActive(Component c, bool on)
        {
            if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }
    }

    /// <summary>Forwards a click on this graphic to a callback.</summary>
    public sealed class ClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        public Action onClick;
        public void OnPointerClick(PointerEventData eventData) => onClick?.Invoke();
    }
}
