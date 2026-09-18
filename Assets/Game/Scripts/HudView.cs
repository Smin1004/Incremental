using UnityEngine;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// Run HUD: currency + run number (top-left), run income (top-right), stamina bar (top-center),
    /// a center message, the debug overlay line and the "×N" label over a multi-planet creation. Built from code; render only.
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        public Canvas Canvas { get; private set; }
        public RectTransform CanvasRect { get; private set; }
        public Font Font { get; private set; }

        const int CountLabelPool = 8;

        Text currencyText, incomeText, staminaText, centerText, debugText;
        Image staminaFill;
        readonly Text[] countLabels = new Text[CountLabelPool];
        readonly float[] countAges = new float[CountLabelPool];
        int nextCountLabel;
        float countLifetime = 1.5f;

        public void Build()
        {
            Font = UIBuilder.LoadFont();
            Canvas = UIBuilder.CreateCanvas("HUD", transform);
            CanvasRect = (RectTransform)Canvas.transform;
            var root = CanvasRect;

            currencyText = UIBuilder.CreateText("Currency", root, Font, 30, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -16f), new Vector2(600f, 40f));
            incomeText = UIBuilder.CreateText("Income", root, Font, 30, TextAnchor.UpperRight,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -16f), new Vector2(600f, 40f));

            var barBg = UIBuilder.CreateImage("StaminaBg", root, new Color(0.15f, 0.15f, 0.2f, 0.85f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(600f, 28f));
            staminaFill = UIBuilder.CreateImage("StaminaFill", barBg.transform, new Color(0.45f, 0.9f, 0.55f, 1f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(596f, 24f), UIBuilder.SolidSprite());
            staminaFill.type = Image.Type.Filled;
            staminaFill.fillMethod = Image.FillMethod.Horizontal;
            staminaFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            staminaText = UIBuilder.CreateText("StaminaText", barBg.transform, Font, 20, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 28f));

            centerText = UIBuilder.CreateText("Center", root, Font, 40, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 200f));
            debugText = UIBuilder.CreateText("Debug", root, Font, 20, TextAnchor.LowerLeft,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 12f), new Vector2(1400f, 30f));
            debugText.color = new Color(1f, 1f, 0.6f, 1f);
            SetDebug(string.Empty);

            for (int i = 0; i < CountLabelPool; i++)
            {
                countLabels[i] = UIBuilder.CreateText("PlanetCount" + i, root, Font, 30, TextAnchor.LowerCenter,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(300f, 40f));
                countLabels[i].gameObject.SetActive(false);
            }
        }

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

        void Update()
        {
            float dt = Time.deltaTime;
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

        public void SetCurrency(double currency, int run) =>
            currencyText.text = string.Format(UIStrings.CurrencyAndRun, Fmt.Num(currency), run);

        public void SetIncome(double income) =>
            incomeText.text = string.Format(UIStrings.RunIncome, Fmt.Num(income));

        public void SetStamina(double current, double max)
        {
            staminaFill.fillAmount = max > 0 ? Mathf.Clamp01((float)(current / max)) : 0f;
            staminaText.text = string.Format(UIStrings.Stamina, Fmt.Int(System.Math.Ceiling(current)), Fmt.Int(max));
        }

        public void SetCenter(string text) => centerText.text = text ?? string.Empty;
        public void SetDebug(string text) => debugText.text = text ?? string.Empty;
    }
}
