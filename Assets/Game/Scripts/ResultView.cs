using System;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// Result screen (11 §4, 13 §3): a panel on the left with the run income (counting up during the entry animation),
    /// the ratio vs the last full run, planets per tier, the best income and a "skill tree" button. Space or the button
    /// opens the tree (12 §9 flow: result → Space → tree → Space → next run). A click anywhere skips the entry animation.
    /// </summary>
    public sealed class ResultView : MonoBehaviour
    {
        const float PanelWidth = 600f;
        const float PanelHeight = 820f;
        const int PlanetLinesPerColumn = 6;

        static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.1f, 0.88f);
        static readonly Color LabelColor = new Color(0.7f, 0.75f, 0.85f, 1f);

        GameRoot root;
        Font font;
        Canvas canvas;
        RectTransform panel;
        Text title, incomeValue, planetsColA, planetsColB, ratioLabel, ratioValue, bestValue;
        readonly StringBuilder sbA = new StringBuilder();
        readonly StringBuilder sbB = new StringBuilder();
        RunRecord record;
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
            planetsColA = Label("PlanetsA", 26, TextAnchor.UpperLeft, 56f, -348f, 250f, 230f);
            planetsColB = Label("PlanetsB", 26, TextAnchor.UpperLeft, 316f, -348f, 250f, 230f);

            Label("BestLabel", 28, TextAnchor.MiddleLeft, 40f, -598f, 300f, 48f, LabelColor).text = UIStrings.ResultBest;
            bestValue = Label("BestValue", 32, TextAnchor.MiddleRight, PanelWidth - 40f - 280f, -598f, 280f, 48f);

            UIBuilder.CreateButton("OpenTree", panel, font, 30, UIStrings.OpenTree,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(420f, 80f), OpenTree, out _);

            root.RunEnded += Show;
            root.RunStarted += Hide;
            canvas.gameObject.SetActive(false);
        }

        /// <summary>Opens the screen with this run as the result (null: no run yet).</summary>
        public void Show(RunRecord rec)
        {
            record = rec;
            entryAge = 0f;
            entryDone = rec == null;
            canvas.gameObject.SetActive(true);
            if (root.Solar != null) root.Solar.Show(rec);
            Refresh();
        }

        public void Hide()
        {
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        void Update()
        {
            if (!IsVisible) return;
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
                planetsColA.text = UIStrings.NoPlanets;
                planetsColB.text = string.Empty;
            }
            bool showRatio = record != null && record.HasRatio;
            SetActive(ratioLabel, showRatio);
            SetActive(ratioValue, showRatio);
            if (showRatio) ratioValue.text = Fmt.Mult(record.ratioVsLast);
            bestValue.text = Fmt.Num(m.bestRunIncome);
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
                sb.AppendFormat(UIStrings.PlanetCount, UIStrings.TierName(tiers[i].tier), Fmt.Num(count));
                n++;
            }
            planetsColA.text = n == 0 ? UIStrings.NoPlanets : sbA.ToString();
            planetsColB.text = sbB.ToString();
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
