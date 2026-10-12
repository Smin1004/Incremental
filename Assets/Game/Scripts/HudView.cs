using TMPro;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// Run HUD (26 §9.2): currency and run number top-left, run income top-right, the stamina bar top-center, all over a
    /// dark veil that fades down from the top edge so the numbers read on bright dust. Also a center message, the debug
    /// overlay lines and the "×N" label over a multi-planet creation. Built from code; render only. Fonts, sizes and
    /// colors come from the UI theme (26 §4).
    /// The run income counts up to each new value and bumps once when it changes (the result screen's device). The
    /// stamina bar (26 §5.1) is a panel-shader bar whose color goes from the full color through the mid color to the low
    /// color as the stamina runs out, and pulses near the end. With the stamina display on (b), where the gravity ring
    /// is the gauge (CursorView), the bar shrinks to a thin strip without numbers.
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
        UIShape veil, staminaBar;
        bool runHudVisible = true;
        TMP_Text currencyLabel, currencyValue, runText, incomeLabel, incomeValue, staminaText, centerText, debugText, abilityText, toastText;
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

            staminaBar = UIBuilder.CreateShape("Stamina", root, ui, UIRole.Bar, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -theme.staminaBarTopPx), theme.staminaBarSize);
            staminaText = UIBuilder.CreateText("StaminaText", staminaBar.transform, Font, theme.sizeTiny, TextAnchor.UpperCenter,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -theme.staminaTextGapPx),
                new Vector2(theme.staminaBarSize.x, theme.sizeTiny * theme.lineHeight));

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

        /// <summary>
        /// Colors of the visual direction, and the bar for the stamina display in use (26 §5.1): the full bar with its
        /// numbers for (a), a thin strip for (b), nothing when that strip's height is set to 0.
        /// </summary>
        void ApplyStyle()
        {
            var d = ui.Direction;
            bool ring = ui.StaminaMode == 1;
            staminaBar.rectTransform.sizeDelta = ring ? theme.staminaBarSmallSize : theme.staminaBarSize;
            staminaText.gameObject.SetActive(!ring);
            staminaText.color = d.text;
            currencyLabel.color = incomeLabel.color = runText.color = d.textDim;
            currencyValue.color = centerText.color = d.text;
            incomeValue.color = d.accent;
            staminaBar.gameObject.SetActive(runHudVisible && BarShown);
        }

        bool BarShown => staminaBar.rectTransform.sizeDelta.y > 0f;

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
            t.transform.localScale = Vector3.one * Mathf.Max(0.1f, (float)scale);
            ((RectTransform)t.transform).anchoredPosition = local + new Vector2(0f, (float)sizePx * 0.5f + 8f);
            t.color = ui.Direction.text;
            countAges[i] = 0f;
            countEnds[i] = countLifetime;
            countFades[i] = countLifetime * 0.6f;
            t.gameObject.SetActive(true);
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
                var c = t.color;
                c.a = CountAlpha(i);
                t.color = c;
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

        /// <summary>Currency, run income and stamina bar: shown during a run only.</summary>
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
            staminaBar.gameObject.SetActive(visible && BarShown);
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

        public void SetStamina(double current, double max)
        {
            float share = max > 0 ? Mathf.Clamp01((float)(current / max)) : 0f;
            float pulse = share <= theme.staminaLowAt ? theme.StaminaPulse(Time.time, game.EffectIntensity) : 0f;
            staminaBar.Fill = share;
            staminaBar.color = theme.StaminaColor(share, pulse);
            staminaBar.State = pulse;
            staminaText.text = string.Format(UIStrings.Stamina, Fmt.Int(System.Math.Ceiling(current)), Fmt.Int(max));
        }

        public void SetCenter(string text) => centerText.text = text ?? string.Empty;
        public void SetDebug(string text) => debugText.text = text ?? string.Empty;
        /// <summary>24 §7: which abilities the debug keys have on, and at what level.</summary>
        public void SetAbilityDebug(string text) => abilityText.text = text ?? string.Empty;
    }
}
