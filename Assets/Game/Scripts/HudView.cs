using TMPro;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// Run HUD (26 §9.2): currency and run number top-left, run income top-right, the time bar top-center, all over a
    /// dark veil that fades down from the top edge so the numbers read on bright dust. Also a center message, the debug
    /// overlay lines and the "×N" label over a multi-planet creation. Built from code; render only. Fonts, sizes and
    /// colors come from the UI theme (26 §4).
    /// The run income counts up to each new value and bumps once when it changes (the result screen's device). The
    /// time bar (26 §5.1, 28 §2) is a panel-shader bar whose color goes from the full color through the mid color to the
    /// low color as the run's time runs out, and pulses near the end; under it "남은 시간 N초".
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        public Canvas Canvas { get; private set; }
        public RectTransform CanvasRect { get; private set; }
        /// <summary>The regular UI font (UIStyle.Font), kept here for the views that were built around the HUD's font.</summary>
        public TMP_FontAsset Font { get; private set; }

        const int CountLabelPool = 8;

        GameRoot game;
        UIStyle ui;
        UITheme theme;
        UIShape veil, timeBar;
        bool runHudVisible = true;
        TMP_Text currencyLabel, currencyValue, runText, incomeLabel, incomeValue, timeText, centerText, debugText, abilityText, toastText;
        string currencyShown;
        double incomeTarget = double.NaN, incomeFrom, incomeShown;
        float incomeAge = float.MaxValue;
        float toastAge = float.MaxValue;
        const float ToastSec = 1.5f;
        readonly TMP_Text[] countLabels = new TMP_Text[CountLabelPool];
        readonly float[] countAges = new float[CountLabelPool];
        /// <summary>
        /// 24 §23.6: per label, the age it is gone at and the age its fade starts at. Normally the shared lifetime and 60%
        /// of it; a newer label cuts both short.
        /// </summary>
        readonly float[] countEnds = new float[CountLabelPool];
        readonly float[] countFades = new float[CountLabelPool];
        float countYield = 0.15f;
        /// <summary>
        /// 26 §12.3 E2: per label, its scale before the pop, its total, the number it rolls up from, how long it has been
        /// rolling, and whether it pops (a bundle label is one).
        /// </summary>
        readonly float[] countBase = new float[CountLabelPool];
        readonly double[] countTotal = new double[CountLabelPool];
        readonly double[] countFrom = new double[CountLabelPool];
        readonly float[] countRoll = new float[CountLabelPool];
        readonly float[] countPop = new float[CountLabelPool];
        readonly bool[] countPops = new bool[CountLabelPool];
        int lastCountLabel = -1;
        int nextCountLabel;
        float countLifetime = 1.5f;

        public void Build(GameRoot gameRoot)
        {
            game = gameRoot;
            ui = gameRoot.Ui;
            theme = ui.Theme;
            Font = ui.Font;
            Canvas = UIBuilder.CreateCanvas("HUD", transform);
            CanvasRect = (RectTransform)Canvas.transform;
            var root = CanvasRect;
            var topLeft = new Vector2(0f, 1f);
            var topRight = new Vector2(1f, 1f);
            float edge = theme.hudEdgePx, top = theme.hudTopPx, lh = theme.lineHeight;
            float labelH = theme.sizeSmall * lh, valueH = theme.sizeLarge * lh;

            // The veil is wider than any screen so it has no visible side edges.
            veil = UIBuilder.CreateShape("Veil", root, ui, UIRole.Veil, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero,
                new Vector2(4000f, theme.hudVeilHeightPx));
            veil.Pad = 0f;

            currencyLabel = UIBuilder.CreateText("CurrencyLabel", root, Font, theme.sizeSmall, TextAnchor.UpperLeft, topLeft, topLeft,
                new Vector2(edge, -top), new Vector2(400f, labelH));
            currencyLabel.text = UIStrings.HudCurrency;
            currencyValue = UIBuilder.CreateText("Currency", root, ui.FontBold, theme.sizeLarge, TextAnchor.UpperLeft, topLeft, topLeft,
                new Vector2(edge, -top - labelH), new Vector2(600f, valueH));
            runText = UIBuilder.CreateText("Run", root, Font, theme.sizeSmall, TextAnchor.UpperLeft, topLeft, topLeft,
                new Vector2(edge, -top - labelH), new Vector2(300f, valueH));
            runText.alignment = TextAlignmentOptions.BottomLeft;
            ((RectTransform)runText.transform).sizeDelta = new Vector2(300f, valueH - (valueH - theme.sizeLarge) * 0.5f);

            incomeLabel = UIBuilder.CreateText("IncomeLabel", root, Font, theme.sizeSmall, TextAnchor.UpperRight, topRight, topRight,
                new Vector2(-edge, -top), new Vector2(400f, labelH));
            incomeLabel.text = UIStrings.HudIncome;
            incomeValue = UIBuilder.CreateText("Income", root, ui.FontBold, theme.sizeLarge, TextAnchor.MiddleRight, topRight, new Vector2(1f, 0.5f),
                new Vector2(-edge, -top - labelH - valueH * 0.5f), new Vector2(600f, valueH));

            timeBar = UIBuilder.CreateShape("TimeBar", root, ui, UIRole.Bar, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -theme.timeBarTopPx), theme.timeBarSize);
            timeText = UIBuilder.CreateText("TimeText", timeBar.transform, Font, theme.sizeTiny, TextAnchor.UpperCenter,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -theme.timeTextGapPx),
                new Vector2(theme.timeBarSize.x, theme.sizeTiny * theme.lineHeight));

            centerText = UIBuilder.CreateText("Center", root, Font, theme.sizeTitle, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 200f));
            debugText = UIBuilder.CreateText("Debug", root, Font, theme.sizeSmall, TextAnchor.LowerLeft,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 12f), new Vector2(1400f, 30f));
            debugText.color = theme.hudDebugText;
            SetDebug(string.Empty);
            // 24 §7: the ability status of the debug keys, above the overlay line and clear of the tree screen's hint,
            // so all three can be read at once.
            abilityText = UIBuilder.CreateText("AbilityDebug", root, Font, theme.sizeTiny, TextAnchor.LowerLeft,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 66f), new Vector2(1400f, 26f));
            abilityText.color = debugText.color;
            SetAbilityDebug(string.Empty);
            toastText = UIBuilder.CreateText("Toast", root, Font, theme.sizeBody, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 80f), new Vector2(900f, 40f));
            toastText.gameObject.SetActive(false);

            for (int i = 0; i < CountLabelPool; i++)
            {
                countLabels[i] = UIBuilder.CreateText("PlanetCount" + i, root, Font, theme.sizeLarge, TextAnchor.LowerCenter,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(300f, 40f));
                countLabels[i].gameObject.SetActive(false);
            }

            ui.Changed += ApplyStyle;
            ApplyStyle();
        }

        /// <summary>Colors of the visual direction, and the time bar's size (26 §5.1).</summary>
        void ApplyStyle()
        {
            var d = ui.Direction;
            timeBar.rectTransform.sizeDelta = theme.timeBarSize;
            timeText.color = d.text;
            currencyLabel.color = incomeLabel.color = runText.color = d.textDim;
            currencyValue.color = centerText.color = d.text;
            incomeValue.color = d.accent;
            timeBar.gameObject.SetActive(runHudVisible && BarShown);
        }

        bool BarShown => timeBar.rectTransform.sizeDelta.y > 0f;

        /// <summary>How long a "×N" label stays (the planet's display time).</summary>
        public void SetCountLifetime(double seconds) => countLifetime = Mathf.Max(0.1f, (float)seconds);

        /// <summary>24 §23.6: seconds the labels still showing have left once a new one appears (GameParams.bundleLabelYieldSec).</summary>
        public void SetCountYield(double seconds) => countYield = Mathf.Max(0f, (float)seconds);

        /// <summary>"×N" above a planet created N at once (12 §10-2). screenPos in pixels, sizePx = planet diameter.</summary>
        public void ShowPlanetCount(Vector3 screenPos, double sizePx, double count) => ShowPlanetCount(screenPos, sizePx, count, 1.0);

        /// <summary>Same, with the label x <paramref name="scale"/>: a bundle's (24 §21.3) is big.</summary>
        public void ShowPlanetCount(Vector3 screenPos, double sizePx, double count, double scale)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(CanvasRect, screenPos, null, out var local)) return;
            var gp = game.gameParams;
            bool pop = game.Visual.bundlePop;
            // 26 §12.3 E2: a bundle soon after the last one adds to its label, the number rolling up, instead of a new label.
            if (pop && lastCountLabel >= 0 && countPops[lastCountLabel] && countLabels[lastCountLabel].gameObject.activeSelf &&
                countAges[lastCountLabel] < gp.bundleRollSec)
            {
                int j = lastCountLabel;
                countFrom[j] = Shown(j);
                countTotal[j] += count;
                countRoll[j] = 0f;
                countPop[j] = 0f;
                countAges[j] = 0f;
                countEnds[j] = countLifetime;
                countFades[j] = countLifetime * 0.6f;
                countBase[j] = BundleScale(scale, countTotal[j]);
                ((RectTransform)countLabels[j].transform).anchoredPosition = local + new Vector2(0f, (float)sizePx * 0.5f + 8f);
                Debug.Log("[Visual] bundle label rolls up: x" + countFrom[j].ToString("0") + " -> x" + countTotal[j].ToString("0"));
                return;
            }
            int i = nextCountLabel;
            nextCountLabel = (nextCountLabel + 1) % CountLabelPool;
            // 24 §23.6: back-to-back bundles put their labels on the same spot; the older ones make way within countYield,
            // fading from where they are so none of them blinks.
            for (int j = 0; j < CountLabelPool; j++)
            {
                if (j == i || countLabels[j] == null || !countLabels[j].gameObject.activeSelf) continue;
                float age = countAges[j];
                float end = Mathf.Min(countEnds[j], age + countYield);
                if (end >= countEnds[j]) continue;
                float a = CountAlpha(j);
                countEnds[j] = end;
                // The fade restarts now at alpha a: a straight line from a to 0 over what is left.
                countFades[j] = a > 0.01f ? age - (end - age) * (1f - a) / a : age;
            }
            var t = countLabels[i];
            t.text = string.Format(UIStrings.PlanetMulti, Fmt.Num(count));
            countPops[i] = pop;
            countTotal[i] = countFrom[i] = count;
            countRoll[i] = float.MaxValue;
            countPop[i] = 0f;
            countBase[i] = pop ? BundleScale(scale, count) : Mathf.Max(0.1f, (float)scale);
            t.transform.localScale = Vector3.one * countBase[i];
            ((RectTransform)t.transform).anchoredPosition = local + new Vector2(0f, (float)sizePx * 0.5f + 8f);
            t.color = pop ? BundleColor(count) : ui.Direction.text;
            if (!pop) SetGlow(t, Color.clear);
            lastCountLabel = i;
            countAges[i] = 0f;
            countEnds[i] = countLifetime;
            countFades[i] = countLifetime * 0.6f;
            t.gameObject.SetActive(true);
        }

        /// <summary>26 §12.3 E2: the label's size grows with log N.</summary>
        float BundleScale(double scale, double count) =>
            Mathf.Max(0.1f, (float)(scale * (1.0 + game.gameParams.bundlePopLogScale * System.Math.Log10(System.Math.Max(1.0, count)))));

        /// <summary>26 §12.3 E2: white below x3, to gold at x10, to orange at x30.</summary>
        Color BundleColor(double count)
        {
            var gp = game.gameParams;
            Color white = ui.Direction.text;
            if (count < 3) return white;
            if (count < 10) return Color.Lerp(white, gp.bundleColorGold, (float)((count - 3) / 7.0));
            if (count < 30) return Color.Lerp(gp.bundleColorGold, gp.bundleColorOrange, (float)((count - 10) / 20.0));
            return gp.bundleColorOrange;
        }

        /// <summary>The number label <paramref name="i"/> shows now, rolling from its last total to its new one.</summary>
        double Shown(int i)
        {
            float sec = (float)game.gameParams.bundleRollCountSec * Mathf.Clamp01(game.EffectIntensity);
            if (sec <= 0f || countRoll[i] >= sec) return countTotal[i];
            float k = countRoll[i] / sec;
            return countFrom[i] + (countTotal[i] - countFrom[i]) * (1f - (1f - k) * (1f - k));
        }

        /// <summary>26 §12.3 E2: the pop (from bundlePopFrom to 1, eased out) and the rolling number of a bundle label.</summary>
        void TickBundleLabel(int i, TMP_Text t, float dt)
        {
            var gp = game.gameParams;
            float fx = Mathf.Clamp01(game.EffectIntensity);
            countPop[i] += dt;
            float popSec = Mathf.Max(0.01f, (float)gp.bundlePopSec);
            float k = Mathf.Clamp01(countPop[i] / popSec);
            float pop = 1f + ((float)gp.bundlePopFrom - 1f) * (1f - k) * (1f - k) * fx;
            t.transform.localScale = Vector3.one * countBase[i] * pop;
            if (countRoll[i] < float.MaxValue)
            {
                countRoll[i] += dt;
                double shown = System.Math.Round(Shown(i));
                t.text = string.Format(UIStrings.PlanetMulti, Fmt.Num(shown));
                var c = BundleColor(shown);
                c.a = t.color.a;
                t.color = c;
                if (countRoll[i] >= gp.bundleRollCountSec) countRoll[i] = float.MaxValue;
            }
        }

        /// <summary>
        /// 26 §12.3 E2 (12차 회신): a soft glow behind a bundle label, the TextMeshPro underlay of the label's own material
        /// (its own copy, so the other texts keep theirs). A clear color turns it off.
        /// </summary>
        void SetGlow(TMP_Text t, Color color)
        {
            var m = t.fontMaterial;
            if (m == null) return;
            bool on = color.a > 0.002f;
            if (on != m.IsKeywordEnabled(ShaderUtilities.Keyword_Underlay))
            {
                if (on) m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                else m.DisableKeyword(ShaderUtilities.Keyword_Underlay);
            }
            if (!on) return;
            float spread = Mathf.Clamp01((float)game.gameParams.bundleGlowSpread);
            m.SetColor(ShaderUtilities.ID_UnderlayColor, color);
            m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, spread);
            m.SetFloat(ShaderUtilities.ID_UnderlayDilate, spread * 0.6f);
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
        }

        float CountAlpha(int i)
        {
            float age = countAges[i], fade = countFades[i], end = countEnds[i];
            if (age <= fade) return 1f;
            return end > fade ? Mathf.Clamp01(1f - (age - fade) / (end - fade)) : 0f;
        }

        /// <summary>Short message at the bottom center that fades out (debug keys).</summary>
        public void ShowToast(string text)
        {
            toastText.text = text;
            toastText.color = theme.hudToastText;
            toastAge = 0f;
            toastText.gameObject.SetActive(true);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            TickIncome(dt);
            if (toastAge < ToastSec)
            {
                toastAge += Time.unscaledDeltaTime;
                var tc = toastText.color;
                tc.a = Mathf.Clamp01((ToastSec - toastAge) / (ToastSec * 0.4f));
                toastText.color = tc;
                if (toastAge >= ToastSec) toastText.gameObject.SetActive(false);
            }
            for (int i = 0; i < CountLabelPool; i++)
            {
                var t = countLabels[i];
                if (t == null || !t.gameObject.activeSelf) continue;
                countAges[i] += dt;
                if (countAges[i] >= countEnds[i])
                {
                    t.gameObject.SetActive(false);
                    continue;
                }
                if (countPops[i]) TickBundleLabel(i, t, dt);
                var c = t.color;
                c.a = CountAlpha(i);
                t.color = c;
                if (countPops[i])
                {
                    var g = c;
                    g.a = (float)game.gameParams.bundleGlowAlpha * c.a * Mathf.Clamp01(game.EffectIntensity);
                    SetGlow(t, g);
                }
            }
        }

        /// <summary>
        /// 26 §9.2: the shown income counts from where it was to the new value over hudIncomeRollSec and bumps once as
        /// it starts. Both follow the effect intensity; at 0 the new value is simply there.
        /// </summary>
        void TickIncome(float dt)
        {
            if (double.IsNaN(incomeTarget)) return;
            float fx = Mathf.Clamp01(game.EffectIntensity);
            float roll = theme.hudIncomeRollSec * fx;
            incomeAge += dt;
            if (roll <= 0f || incomeAge >= roll) incomeShown = incomeTarget;
            else
            {
                float k = incomeAge / roll;
                incomeShown = incomeFrom + (incomeTarget - incomeFrom) * (1.0 - (1.0 - k) * (1.0 - k));
            }
            float pop = theme.hudIncomePopSec > 0f && incomeAge < theme.hudIncomePopSec
                ? Mathf.Sin(incomeAge / theme.hudIncomePopSec * Mathf.PI) * theme.hudIncomePopScale * fx
                : 0f;
            incomeValue.rectTransform.localScale = new Vector3(1f + pop, 1f + pop, 1f);
            incomeValue.text = Fmt.Num(System.Math.Floor(incomeShown));
        }

        /// <summary>Currency, run income and time bar: shown during a run only.</summary>
        public void SetRunHudVisible(bool visible)
        {
            if (runHudVisible == visible) return;
            runHudVisible = visible;
            veil.gameObject.SetActive(visible);
            currencyLabel.gameObject.SetActive(visible);
            currencyValue.gameObject.SetActive(visible);
            runText.gameObject.SetActive(visible);
            incomeLabel.gameObject.SetActive(visible);
            incomeValue.gameObject.SetActive(visible);
            timeBar.gameObject.SetActive(visible && BarShown);
        }

        public void SetCurrency(double currency, int run)
        {
            string value = Fmt.Num(currency);
            if (value == currencyShown) return;
            currencyShown = value;
            currencyValue.text = value;
            // The run number sits right after the currency, so it moves with the number's width.
            float w = currencyValue.GetPreferredValues(value).x;
            ((RectTransform)runText.transform).anchoredPosition = new Vector2(theme.hudEdgePx + w + theme.hudEdgePx * 0.6f, -theme.hudTopPx - theme.sizeSmall * theme.lineHeight);
            runText.text = string.Format(UIStrings.HudRun, run);
        }

        /// <summary>Run income as it will be paid: rounded up once, the same value the result screen shows (14 §8).</summary>
        public void SetIncome(double income)
        {
            double target = Stats.RunIncome(income);
            if (!double.IsNaN(incomeTarget) && target == incomeTarget) return;
            // A run start drops the income to 0: no count-down, the number simply resets.
            if (double.IsNaN(incomeTarget) || target < incomeTarget)
            {
                incomeFrom = incomeShown = target;
                incomeAge = float.MaxValue;
            }
            else
            {
                incomeFrom = incomeShown;
                incomeAge = 0f;
            }
            incomeTarget = target;
            TickIncome(0f);
        }

        /// <summary>28 §2: the time bar, <paramref name="left"/> of <paramref name="total"/> seconds, and "남은 시간 N초" under it.</summary>
        public void SetTimeLeft(double left, double total)
        {
            float share = total > 0 ? Mathf.Clamp01((float)(left / total)) : 0f;
            float pulse = share <= theme.timeLowAt ? theme.TimePulse(Time.time, game.EffectIntensity) : 0f;
            timeBar.Fill = share;
            timeBar.color = theme.TimeColor(share, pulse);
            timeBar.State = pulse;
            int secs = (int)System.Math.Ceiling(System.Math.Max(0.0, left));
            if (secs != timeShown)
            {
                timeShown = secs;
                timeText.text = string.Format(UIStrings.TimeLeft, secs);
            }
        }

        int timeShown = -1;

        public void SetCenter(string text) => centerText.text = text ?? string.Empty;
        public void SetDebug(string text) => debugText.text = text ?? string.Empty;
        /// <summary>24 §7: which abilities the debug keys have on, and at what level.</summary>
        public void SetAbilityDebug(string text) => abilityText.text = text ?? string.Empty;
    }
}
