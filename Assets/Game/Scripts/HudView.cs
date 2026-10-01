using TMPro;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// Run HUD: currency + run number (top-left), run income (top-right), stamina bar (top-center),
    /// a center message, the debug overlay line and the "×N" label over a multi-planet creation. Built from code; render only.
    /// Fonts, sizes and colors come from the UI theme (26 §4).
    /// The stamina bar (26 §5.1) is a panel-shader bar whose color goes from the full color through the mid color to
    /// the low color as the stamina runs out, and pulses near the end. With the stamina display on (b), where the
    /// gravity ring is the gauge (CursorView), the bar shrinks to a thin strip without numbers.
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
        UIShape staminaBar;
        bool runHudVisible = true;
        TMP_Text currencyText, incomeText, staminaText, centerText, debugText, abilityText, toastText;
        float toastAge = float.MaxValue;
        const float ToastSec = 1.5f;
        readonly TMP_Text[] countLabels = new TMP_Text[CountLabelPool];
        readonly float[] countAges = new float[CountLabelPool];
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

            currencyText = UIBuilder.CreateText("Currency", root, Font, theme.sizeLarge, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -16f), new Vector2(600f, 40f));
            incomeText = UIBuilder.CreateText("Income", root, Font, theme.sizeLarge, TextAnchor.UpperRight,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -16f), new Vector2(600f, 40f));

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
            currencyText.color = incomeText.color = centerText.color = d.text;
            staminaBar.gameObject.SetActive(runHudVisible && BarShown);
        }

        bool BarShown => staminaBar.rectTransform.sizeDelta.y > 0f;

        /// <summary>How long a "×N" label stays (the planet's display time).</summary>
        public void SetCountLifetime(double seconds) => countLifetime = Mathf.Max(0.1f, (float)seconds);

        /// <summary>"×N" above a planet created N at once (12 §10-2). screenPos in pixels, sizePx = planet diameter.</summary>
        public void ShowPlanetCount(Vector3 screenPos, double sizePx, double count)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(CanvasRect, screenPos, null, out var local)) return;
            int i = nextCountLabel;
            nextCountLabel = (nextCountLabel + 1) % CountLabelPool;
            var t = countLabels[i];
            t.text = string.Format(UIStrings.PlanetMulti, Fmt.Num(count));
            ((RectTransform)t.transform).anchoredPosition = local + new Vector2(0f, (float)sizePx * 0.5f + 8f);
            t.color = Color.white;
            countAges[i] = 0f;
            t.gameObject.SetActive(true);
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
                if (countAges[i] >= countLifetime)
                {
                    t.gameObject.SetActive(false);
                    continue;
                }
                float fadeStart = countLifetime * 0.6f;
                var c = t.color;
                c.a = countAges[i] <= fadeStart ? 1f : 1f - (countAges[i] - fadeStart) / (countLifetime - fadeStart);
                t.color = c;
            }
        }

        /// <summary>Currency, run income and stamina bar: shown during a run only.</summary>
        public void SetRunHudVisible(bool visible)
        {
            if (runHudVisible == visible) return;
            runHudVisible = visible;
            currencyText.gameObject.SetActive(visible);
            incomeText.gameObject.SetActive(visible);
            staminaBar.gameObject.SetActive(visible && BarShown);
        }

        public void SetCurrency(double currency, int run) =>
            currencyText.text = string.Format(UIStrings.CurrencyAndRun, Fmt.Num(currency), run);

        /// <summary>Run income as it will be paid: rounded up once, the same value the result screen shows (14 §8).</summary>
        public void SetIncome(double income) =>
            incomeText.text = string.Format(UIStrings.RunIncome, Fmt.Num(Stats.RunIncome(income)));

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
