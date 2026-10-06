using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// Result screen (11 §4, 13 §3, 14 §7, 26 §5.2): a panel on the left in the UI theme's style with the run income
    /// counting up, a "best record" badge that lights up once when the run beat the best, the ratio vs the last full
    /// run (accent color at 1 or above, dim below), one row per tier made this run with a mini planet icon, the count,
    /// the increase against the comparison run (the ratio's run; decreases are left blank, 15 §3) and a "new body" tag
    /// for tiers made for the first time, the best income and a "skill tree" button with its key hint.
    /// The rows come in one after another from the top. Space or the button opens the tree at once, whatever the
    /// entry is doing (12 §9 flow: result → Space → tree → Space → next run); a click anywhere skips the entry.
    /// Times and movement are scaled by the effect intensity: at 0 everything is simply there.
    /// </summary>
    public sealed class ResultView : MonoBehaviour
    {
        sealed class Row
        {
            public RectTransform rt;
            public CanvasGroup group;
            public PlanetIcon icon;
            public TMP_Text name, count, delta;
            public float y;
        }

        GameRoot root;
        UITheme theme;
        Canvas canvas;
        UIShape panel, badge;
        CanvasGroup badgeGroup;
        TMP_Text title, incomeLabel, incomeValue, planetsLabel, ratioLabel, ratioValue, bestLabel, bestValue, badgeText;
        Image[] dividers;
        Row[] rows;
        int rowCount;
        RunRecord record;
        RunRecord comparison;
        bool fresh;
        bool newBest;
        float entryAge;
        bool entryDone;

        public bool IsVisible => canvas != null && canvas.gameObject.activeSelf;

        /// <summary>Raised when the entry animation is skipped (click) so other result visuals can jump to their final state.</summary>
        public event Action EntrySkipped;

        public void Init(GameRoot gameRoot)
        {
            root = gameRoot;
            var ui = root.Ui;
            theme = ui.Theme;
            canvas = UIBuilder.CreateCanvas("Result", transform);
            canvas.sortingOrder = 10;
            var canvasRect = (RectTransform)canvas.transform;

            var catcher = UIBuilder.CreateStretch("ClickCatcher", canvasRect).gameObject;
            var img = catcher.AddComponent<Image>();
            img.color = Color.clear;
            img.raycastTarget = true;
            catcher.AddComponent<ClickCatcher>().onClick = SkipEntry;

            Vector2 size = theme.resultPanelSize;
            panel = UIBuilder.CreateShape("Panel", canvasRect, ui, UIRole.Panel, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(theme.resultPanelMarginPx, 0f), size);
            panel.raycastTarget = true;
            panel.gameObject.AddComponent<ClickCatcher>().onClick = SkipEntry;

            // Stacked from the top: title, income, ratio, the planet list, best income; the button sits at the bottom.
            float pad = theme.resultPaddingPx, gap = theme.resultGapPx, lh = theme.lineHeight;
            float w = size.x - pad * 2f;
            float valueW = w * 0.55f;
            float y = -pad * 0.7f;
            dividers = new Image[3];

            title = Text("Title", ui.FontBold, theme.sizeTitle, TextAnchor.MiddleLeft, pad, ref y, w, theme.sizeTitle * lh);
            y -= gap;
            dividers[0] = Divider(0, pad, y, w);
            y -= gap;

            float labelY = y;
            incomeLabel = Text("IncomeLabel", ui.Font, theme.sizeBody, TextAnchor.MiddleLeft, pad, ref y, w, theme.sizeBody * lh);
            incomeLabel.text = UIStrings.ResultIncome;
            BuildBadge(ui, size.x - pad, labelY, theme.sizeBody * lh);
            incomeValue = Text("IncomeValue", ui.FontBold, theme.sizeDisplay, TextAnchor.MiddleLeft, pad, ref y, w, theme.sizeDisplay * lh);
            // The count's bump grows from the left end of the number.
            incomeValue.rectTransform.pivot = new Vector2(0f, 0.5f);
            incomeValue.rectTransform.anchoredPosition += new Vector2(0f, -theme.sizeDisplay * lh * 0.5f);

            float ratioY = y;
            ratioLabel = Text("RatioLabel", ui.Font, theme.sizeBody, TextAnchor.MiddleLeft, pad, ref y, w, theme.sizeLarge * lh);
            ratioLabel.text = UIStrings.ResultRatio;
            ratioValue = Text("RatioValue", ui.FontBold, theme.sizeLarge, TextAnchor.MiddleRight, size.x - pad - valueW, ref ratioY, valueW, theme.sizeLarge * lh);
            y -= gap;
            dividers[1] = Divider(1, pad, y, w);
            y -= gap;

            planetsLabel = Text("PlanetsLabel", ui.Font, theme.sizeBody, TextAnchor.MiddleLeft, pad, ref y, w, theme.sizeBody * lh);
            planetsLabel.text = UIStrings.ResultPlanets;
            y -= gap * 0.4f;
            int n = Mathf.Max(1, root.celestialTable.tiers.Count);
            rows = new Row[n];
            for (int i = 0; i < n; i++) rows[i] = BuildRow(ui, i, y - i * theme.resultRowHeightPx, size.x, pad);
            y -= n * theme.resultRowHeightPx + gap;
            dividers[2] = Divider(2, pad, y, w);
            y -= gap;

            float bestY = y;
            bestLabel = Text("BestLabel", ui.Font, theme.sizeBody, TextAnchor.MiddleLeft, pad, ref y, w, theme.sizeLarge * lh);
            bestLabel.text = UIStrings.ResultBest;
            bestValue = Text("BestValue", ui.FontBold, theme.sizeLarge, TextAnchor.MiddleRight, size.x - pad - valueW, ref bestY, valueW, theme.sizeLarge * lh);

            UIButton.Create(root, "OpenTree", panel.transform, UIStrings.OpenTreeLabel, UIStrings.KeySpace,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, pad), theme.resultButtonSize, OpenTree);

            ui.Changed += ApplyStyle;
            ApplyStyle();

            // The ending run opens the supernova sequence instead of this panel (15 §4.3).
            // 26 §5.4: the run screen fades into the result.
            root.RunEnded += rec =>
            {
                if (EndingView.PlaysFor(root.Meta, rec)) root.Ending.Play(rec, false);
                else root.Transition.Play(() => Show(rec, true), true);
            };
            root.RunStarted += Hide;
            canvas.gameObject.SetActive(false);
        }

        // ---------------- building ----------------

        /// <summary>A label at the stack position <paramref name="y"/> (from the panel top); moves the position down by its height.</summary>
        TMP_Text Text(string name, TMP_FontAsset font, float fontSize, TextAnchor align, float x, ref float y, float w, float h)
        {
            var t = UIBuilder.CreateText(name, panel.transform, font, fontSize, align, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(w, h));
            y -= h;
            return t;
        }

        Image Divider(int index, float x, float y, float w) =>
            UIBuilder.CreateImage("Divider" + index, panel.transform, Color.clear, new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(x, y), new Vector2(w, theme.resultDividerPx), UIBuilder.SolidSprite());

        /// <summary>"Best record" badge at the right end of the income label's line, sized to its text.</summary>
        void BuildBadge(UIStyle ui, float right, float y, float lineHeight)
        {
            var c = new Vector2(0.5f, 0.5f);
            badge = UIBuilder.CreateShape("BestBadge", panel.transform, ui, UIRole.Badge, new Vector2(0f, 1f), new Vector2(1f, 0.5f),
                new Vector2(right, y - lineHeight * 0.5f), Vector2.zero);
            badgeGroup = badge.gameObject.AddComponent<CanvasGroup>();
            badgeGroup.blocksRaycasts = false;
            badgeText = UIBuilder.CreateText("Text", badge.transform, ui.FontBold, theme.sizeTiny, TextAnchor.MiddleCenter, c, c, Vector2.zero, Vector2.zero);
            badgeText.text = UIStrings.ResultNewBest;
            Vector2 text = badgeText.GetPreferredValues(UIStrings.ResultNewBest);
            var size = new Vector2(text.x + theme.chipPaddingPx.x * 2f, text.y + theme.chipPaddingPx.y * 2f);
            badge.rectTransform.sizeDelta = size;
            ((RectTransform)badgeText.transform).sizeDelta = size;
        }

        /// <summary>One row of the planet list: icon, name, count (right aligned), change / new tag.</summary>
        Row BuildRow(UIStyle ui, int index, float y, float panelWidth, float pad)
        {
            float h = theme.resultRowHeightPx;
            var topLeft = new Vector2(0f, 1f);
            var row = new Row { y = y };
            row.rt = UIBuilder.CreateRect("Row" + index, panel.transform, topLeft, topLeft, new Vector2(0f, y), new Vector2(panelWidth, h));
            row.group = row.rt.gameObject.AddComponent<CanvasGroup>();
            row.group.blocksRaycasts = false;
            row.icon = PlanetIcon.Create("Icon", row.rt, root.planetMaterial);
            var iconRt = row.icon.rectTransform;
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 0.5f);
            iconRt.anchoredPosition = new Vector2(pad + theme.resultIconColumnPx * 0.5f, 0f);
            float nameX = pad + theme.resultIconColumnPx;
            float w = panelWidth - pad - nameX;
            var left = new Vector2(0f, 0.5f);
            row.name = UIBuilder.CreateText("Name", row.rt, ui.Font, theme.sizeSmall, TextAnchor.MiddleLeft, left, left,
                new Vector2(nameX, 0f), new Vector2(w * 0.42f, h));
            row.count = UIBuilder.CreateText("Count", row.rt, ui.Font, theme.sizeSmall, TextAnchor.MiddleRight, left, left,
                new Vector2(nameX + w * 0.42f, 0f), new Vector2(w * 0.2f, h));
            row.delta = UIBuilder.CreateText("Delta", row.rt, ui.Font, theme.sizeSmall, TextAnchor.MiddleLeft, left, left,
                new Vector2(nameX + w * 0.62f + theme.resultGapPx * 2f, 0f), new Vector2(w * 0.38f - theme.resultGapPx * 2f, h));
            return row;
        }

        /// <summary>Colors of the current visual direction (the debug key switches it while the screen is open).</summary>
        void ApplyStyle()
        {
            var d = root.Ui.Direction;
            title.color = d.text;
            incomeLabel.color = ratioLabel.color = planetsLabel.color = bestLabel.color = d.textDim;
            incomeValue.color = d.accent;
            bestValue.color = d.text;
            badgeText.color = d.accent;
            foreach (var line in dividers) line.color = d.line;
            foreach (var row in rows)
            {
                row.name.color = d.text;
                row.count.color = d.text;
                row.delta.color = d.text;
            }
        }

        // ---------------- show / hide ----------------

        /// <summary>
        /// Opens the screen with this run as the result (null: no run yet). <paramref name="freshRun"/>: right after the
        /// run ended (the "new body" highlight and the badge's flash are shown only then, 14 §7); false when reopening a save.
        /// </summary>
        public void Show(RunRecord rec, bool freshRun)
        {
            record = rec;
            fresh = freshRun;
            comparison = ResultMath.ComparisonRun(root.Meta.runHistory, rec);
            newBest = ResultMath.IsNewBest(root.Meta, rec);
            entryAge = 0f;
            entryDone = rec == null;
            canvas.gameObject.SetActive(true);
            if (root.Solar != null) root.Solar.Show(rec, freshRun);
            FillRows();
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

        /// <summary>0..1: the UI's movement follows the effect intensity (26 §4); at 0 the entry is instant.</summary>
        float Fx => Mathf.Clamp01(root.EffectIntensity);

        /// <summary>
        /// How long the entry takes: the panel's own animation (count, pop or badge flash, the last row) and the solar
        /// system's fly-in, whichever ends later, so a click during either still skips it.
        /// </summary>
        float EntrySec
        {
            get
            {
                float count = theme.incomeRollSec + Mathf.Max(theme.valuePopSec, theme.badgeFlashSec);
                float list = theme.rowStartSec + Mathf.Max(0, rowCount - 1) * theme.rowStaggerSec + theme.rowFadeSec;
                return Mathf.Max(0.01f, (float)root.gameParams.resultEntrySec, Mathf.Max(count, list) * Fx);
            }
        }

        /// <summary>26 §5.4: fades to the tree. Asked again during the fade, the switch happens at once (R7).</summary>
        void OpenTree()
        {
            root.Transition.Play(() =>
            {
                Hide();
                root.Tree.Show();
            });
        }

        void SkipEntry()
        {
            if (entryDone) return;
            entryDone = true;
            EntrySkipped?.Invoke();
        }

        // ---------------- contents ----------------

        void Refresh()
        {
            var m = root.Meta;
            var d = root.Ui.Direction;
            float fx = Fx;
            float age = entryDone ? float.MaxValue : entryAge;
            float roll = theme.incomeRollSec * fx;
            float sinceRoll = age - roll;

            if (record != null)
            {
                float k = roll > 0f ? Mathf.Clamp01(age / roll) : 1f;
                float eased = 1f - (1f - k) * (1f - k) * (1f - k);
                title.text = string.Format(UIStrings.ResultTitle, record.run);
                incomeValue.text = Fmt.Num(Math.Floor(record.income * eased));
                // The number bumps once when the count lands.
                float pop = theme.valuePopSec > 0f && sinceRoll >= 0f && sinceRoll < theme.valuePopSec
                    ? Mathf.Sin(sinceRoll / theme.valuePopSec * Mathf.PI) * theme.valuePopScale * fx
                    : 0f;
                incomeValue.rectTransform.localScale = new Vector3(1f + pop, 1f + pop, 1f);
                FillRows();
            }
            else
            {
                title.text = string.Empty;
                incomeValue.text = UIStrings.NoPlanets;
                incomeValue.rectTransform.localScale = Vector3.one;
            }

            // The ratio, and the badge with its one flash, arrive when the count lands.
            float tail = sinceRoll >= 0f ? 1f : 0f;
            if (fx > 0f && theme.rowFadeSec > 0f) tail = Mathf.Clamp01(sinceRoll / (theme.rowFadeSec * fx));
            bool showRatio = record != null && record.HasRatio;
            SetActive(ratioLabel, showRatio);
            SetActive(ratioValue, showRatio);
            if (showRatio)
            {
                ratioValue.text = Fmt.Mult(record.ratioVsLast);
                var c = record.ratioVsLast >= 1.0 ? d.accent : d.textDim;
                c.a *= tail;
                ratioValue.color = c;
            }
            bool showBadge = record != null && newBest;
            SetActive(badge, showBadge);
            if (showBadge)
            {
                badgeGroup.alpha = tail;
                float flash = fresh && theme.badgeFlashSec > 0f && sinceRoll >= 0f ? Mathf.Clamp01(1f - sinceRoll / theme.badgeFlashSec) : 0f;
                badge.State = flash;
                float s = 1f + flash * theme.valuePopScale * fx;
                badge.rectTransform.localScale = new Vector3(s, s, 1f);
            }
            bestValue.text = Fmt.Num(m.bestRunIncome);

            // Rows come in from the top, one after another.
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                bool on = i < rowCount;
                SetActive(row.rt, on);
                if (!on) continue;
                float a = 1f;
                if (fx > 0f && !entryDone)
                {
                    float start = (theme.rowStartSec + i * theme.rowStaggerSec) * fx;
                    a = Mathf.Clamp01((age - start) / Mathf.Max(1e-3f, theme.rowFadeSec * fx));
                    a = 1f - (1f - a) * (1f - a);
                }
                row.group.alpha = a;
                row.icon.SetFade(a);
                row.rt.anchoredPosition = new Vector2(-theme.rowSlidePx * fx * (1f - a), row.y);
            }
        }

        /// <summary>
        /// One row per tier made this run: the icon, the name, the count, the increase against the comparison run (▲3)
        /// and a "new body" tag for tiers made for the first time ever (fresh results only). A decrease leaves the change
        /// column blank: it is mostly the result of moving up to a higher tier and should not read as a penalty (15 §3);
        /// the ratio shows overall growth. With nothing made the first row is a dash.
        /// </summary>
        void FillRows()
        {
            int n = 0;
            if (record != null)
            {
                var p = root.gameParams;
                string up = ColorUtility.ToHtmlStringRGB(p.deltaUpColor);
                string newColor = ColorUtility.ToHtmlStringRGB(p.newTierColor);
                var tiers = root.celestialTable.tiers;
                for (int i = 0; i < tiers.Count && n < rows.Length; i++)
                {
                    int t = tiers[i].tier;
                    int count = record.TierCount(t);
                    if (count <= 0) continue;
                    var row = rows[n];
                    if (!row.icon.gameObject.activeSelf) row.icon.gameObject.SetActive(true);
                    // The same seed every time, so a tier keeps its face from one result to the next.
                    row.icon.Set(tiers[i].look, 11f + i * 7.3f, p.planetAxisPitchDeg, theme.resultIconLight, theme.resultIconPx);
                    row.name.text = UIStrings.TierName(t);
                    row.count.text = Fmt.Num(count);
                    int delta = ResultMath.ShownIncrease(record, comparison, t);
                    string change = delta > 0 ? "<color=#" + up + ">" + string.Format(UIStrings.DeltaUp, Fmt.Num(delta)) + "</color>" : string.Empty;
                    if (fresh && ResultMath.IsNewTier(root.Meta, record, t))
                        change += (delta > 0 ? "  " : string.Empty) + "<color=#" + newColor + ">" + UIStrings.NewBody + "</color>";
                    row.delta.text = change;
                    n++;
                }
            }
            if (n == 0)
            {
                var row = rows[0];
                row.icon.gameObject.SetActive(false);
                row.name.text = UIStrings.NoPlanets;
                row.count.text = string.Empty;
                row.delta.text = string.Empty;
                n = 1;
            }
            rowCount = n;
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
