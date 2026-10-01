using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// How one shape (panel, button, key chip, bar, badge) is drawn by the Incremental/UIPanel shader (26 §4).
    /// Lengths are reference px (1080p). A color with alpha 0 switches that part off.
    /// </summary>
    [Serializable]
    public sealed class UIShapeStyle
    {
        public Color fill = Color.clear;
        [Tooltip("0..1: how much darker the fill gets toward the bottom.")]
        public float fillShade;
        public Color border = Color.clear;
        public float borderPx = 1f;
        [Tooltip("Corner radius; 0 = square corners.")]
        public float cornerPx;
        [Tooltip("Corner brackets: an L of this length and width on each corner.")]
        public Color bracket = Color.clear;
        public float bracketLenPx;
        public float bracketPx = 2f;
        [Tooltip("Scale marks along the bottom edge: spacing (0 = none), length, alpha. Every fifth mark is longer.")]
        public float tickSpacingPx;
        public float tickLenPx = 5f;
        public float tickAlpha = 0.4f;
        [Tooltip("Light from the edge inward: reach and strength (× effect intensity).")]
        public float glowPx = 16f;
        public float glowStrength;
        [Tooltip("Glass highlight on the upper part, 0..1.")]
        public float sheen;
        [Tooltip("Soft shadow outside the shape: reach and alpha.")]
        public float shadowPx = 24f;
        public float shadowAlpha;
        [Tooltip("Light added while the shape is hovered or flashing (× effect intensity).")]
        public float stateGlow = 0.5f;
        [Tooltip("Bars: the filled part is cut into cells this wide (0 = solid) with this gap between them.")]
        public float segmentPx;
        public float segmentGapPx = 2f;
    }

    /// <summary>One visual direction of the UI (26 §3): the palette, the glyph shadow and the shapes.</summary>
    [Serializable]
    public sealed class UIDirection
    {
        [Tooltip("Shown by the debug key; the name is UIStrings 'ui.direction.<id>'.")]
        public string id = "A";
        public Color text = Color.white;
        [Tooltip("Labels and anything that should stay behind the values.")]
        public Color textDim = new Color(0.7f, 0.75f, 0.85f);
        [Tooltip("Values: income, the ratio at 1 or above, the best record.")]
        public Color accent = new Color(1f, 0.84f, 0.45f);
        [Tooltip("Divider lines; alpha 0 = none.")]
        public Color line = new Color(0.62f, 0.8f, 1f, 0.3f);
        [Tooltip("Shadow under every glyph: color with alpha (0 = none), offset and softness 0..1.")]
        public Color textShadow = new Color(0f, 0f, 0f, 0.5f);
        public Vector2 textShadowOffset = new Vector2(0f, -0.6f);
        public float textShadowSoftness = 0.3f;
        public UIShapeStyle panel = new UIShapeStyle();
        public UIShapeStyle button = new UIShapeStyle();
        public UIShapeStyle chip = new UIShapeStyle();
        public UIShapeStyle bar = new UIShapeStyle();
        public UIShapeStyle badge = new UIShapeStyle();
    }

    /// <summary>
    /// The look of the UI in one asset (26 §4): fonts, the palette and shapes of each visual direction, the type scale,
    /// animation times and the stamina display. The views hold no colors or numbers of their own; the field initializers
    /// here are the seed of the asset, as with GameParams.
    /// </summary>
    [CreateAssetMenu(menuName = "Incremental/UI Theme", fileName = "UITheme")]
    public sealed class UITheme : ScriptableObject
    {
        [Header("Font (26 §4)")]
        [Tooltip("Source fonts. The SDF font assets are made from them at startup (dynamic atlas), so swapping a font is " +
                 "assigning another file here.")]
        public Font fontRegular;
        public Font fontBold;
        [Tooltip("The TextMeshPro shader the font assets use. Referenced here so a build includes it.")]
        public Shader textShader;
        public int fontSamplingSize = 56;
        public int fontPaddingPx = 6;
        public int fontAtlasSize = 2048;
        [Tooltip("Every digit takes the width of the widest one, so a changing number does not shake.")]
        public bool tabularDigits = true;

        [Header("Type scale (reference px)")]
        public float sizeDisplay = 64f;
        public float sizeTitle = 40f;
        public float sizeLarge = 30f;
        public float sizeBody = 24f;
        public float sizeSmall = 20f;
        public float sizeTiny = 17f;
        [Tooltip("Height of a line of text as a multiple of its font size (stacked layouts).")]
        public float lineHeight = 1.3f;

        [Header("Visual direction (26 §3)")]
        [Tooltip("Index into directions the game starts with. The debug key cycles it at runtime.")]
        public int direction;
        public UIDirection[] directions = { DirectionA(), DirectionB(), DirectionC() };
        [Tooltip("Incremental/UIPanel material; each shape role gets its own copy at startup.")]
        public Material panelMaterial;
        [Tooltip("Room around a shape for its shadow and outer light, px.")]
        public float shapePadPx = 40f;

        [Header("HUD")]
        public Color hudDebugText = new Color(1f, 1f, 0.6f, 1f);
        public Color hudToastText = new Color(1f, 1f, 0.75f, 1f);

        [Header("Stamina (26 §5.1)")]
        [Tooltip("0 = (a) the bar at the top, 1 = (b) the gravity ring is the gauge. The debug key switches it at runtime.")]
        public int staminaMode;
        public Color staminaFull = new Color(0.72f, 0.88f, 1f, 1f);
        public Color staminaMid = new Color(1f, 0.62f, 0.2f, 1f);
        public Color staminaLow = new Color(1f, 0.27f, 0.22f, 1f);
        [Tooltip("Share left where the color starts to turn (full → mid → low) and where it is fully the low color. " +
                 "Below the second it also pulses.")]
        public float staminaWarnAt = 0.5f;
        public float staminaLowAt = 0.2f;
        [Tooltip("Pulse below staminaLowAt: beats per second and how deep it dims (× effect intensity).")]
        public float staminaPulseHz = 2.2f;
        public float staminaPulseDepth = 0.55f;
        public Vector2 staminaBarSize = new Vector2(600f, 22f);
        [Tooltip("Distance of the bar from the top of the screen, and of the numbers from the bar.")]
        public float staminaBarTopPx = 18f;
        public float staminaTextGapPx = 5f;
        [Tooltip("(b): the bar that stays at the top while the ring is the gauge. Height 0 = no bar at all.")]
        public Vector2 staminaBarSmallSize = new Vector2(260f, 6f);
        [Tooltip("(b): the arc on the gravity ring, the faint full ring behind it (alpha 0 = none) and its smoothness.")]
        public float staminaRingWidthPx = 4f;
        public float staminaRingAlpha = 0.95f;
        public float staminaRingTrackAlpha = 0.1f;
        public int staminaRingSegments = 96;

        [Header("Result screen (26 §5.2)")]
        public Vector2 resultPanelSize = new Vector2(600f, 820f);
        public float resultPanelMarginPx = 48f;
        [Tooltip("Space between the panel edge and its contents, and between the blocks inside.")]
        public float resultPaddingPx = 40f;
        public float resultGapPx = 10f;
        public float resultDividerPx = 1f;
        public float resultRowHeightPx = 26.5f;
        [Tooltip("Width of the icon column in front of the tier names.")]
        public float resultIconColumnPx = 48f;
        [Tooltip("Disc diameter of the planet icon in a row.")]
        public float resultIconPx = 19f;
        [Tooltip("Where the light of the icons comes from: the central star is to the right of the panel.")]
        public Vector2 resultIconLight = new Vector2(1f, 0.25f);
        public Vector2 resultButtonSize = new Vector2(420f, 76f);
        [Tooltip("The income counts up over this long (× effect intensity).")]
        public float incomeRollSec = 0.8f;
        [Tooltip("Scale bump of the income when the count ends, and how long it takes.")]
        public float valuePopScale = 0.08f;
        public float valuePopSec = 0.22f;
        [Tooltip("Rows of the planet list: the first one starts here, each next one this much later; fade time and slide.")]
        public float rowStartSec = 0.12f;
        public float rowStaggerSec = 0.05f;
        public float rowFadeSec = 0.2f;
        public float rowSlidePx = 16f;
        [Tooltip("The best record badge lights up once for this long when the count ends.")]
        public float badgeFlashSec = 0.7f;

        [Header("Buttons (26 §4)")]
        public float buttonHoverSec = 0.12f;
        [Tooltip("Scale while pressed (1 = none).")]
        public float buttonPressScale = 0.96f;
        [Tooltip("Key hint chip: space between the label and the chip, and the padding around the key name.")]
        public float buttonChipGapPx = 14f;
        public Vector2 chipPaddingPx = new Vector2(11f, 4f);

#if UNITY_EDITOR
        /// <summary>
        /// Raised when a value is changed in the inspector, so a running game redraws in the new values (UIStyle).
        /// Sizes that a screen reads once while it is built still need a new play session.
        /// </summary>
        public static event Action<UITheme> Edited;

        void OnValidate() => Edited?.Invoke(this);
#endif

        /// <summary>The direction at this index, clamped; never null as long as the asset has one.</summary>
        public UIDirection Direction(int index) =>
            directions != null && directions.Length > 0 ? directions[Mathf.Clamp(index, 0, directions.Length - 1)] : null;

        /// <summary>
        /// Stamina color for the share left (26 §5.1): the full color down to staminaWarnAt, then through the mid color
        /// to the low color at staminaLowAt. Below that it pulses; <paramref name="pulse01"/> is the pulse phase 0..1
        /// already scaled by the effect intensity (0 = steady).
        /// </summary>
        public Color StaminaColor(float share, float pulse01)
        {
            float warn = Mathf.Max(staminaWarnAt, staminaLowAt + 1e-3f);
            float half = (warn + staminaLowAt) * 0.5f;
            Color c;
            if (share >= warn) c = staminaFull;
            else if (share >= half) c = Color.Lerp(staminaMid, staminaFull, (share - half) / (warn - half));
            else if (share > staminaLowAt) c = Color.Lerp(staminaLow, staminaMid, (share - staminaLowAt) / (half - staminaLowAt));
            else c = staminaLow;
            if (share <= staminaLowAt) c.a *= 1f - staminaPulseDepth * Mathf.Clamp01(pulse01);
            return c;
        }

        /// <summary>Pulse phase 0..1 at this time for <see cref="StaminaColor"/>, already × the effect intensity.</summary>
        public float StaminaPulse(float time, float intensity) =>
            (0.5f - 0.5f * Mathf.Cos(time * staminaPulseHz * Mathf.PI * 2f)) * Mathf.Clamp01(intensity);

        // ---------------- seed of the three directions (26 §3) ----------------

        /// <summary>A, the observatory instrument: thin lines, corner brackets, scale marks, gold values on a cold line color.</summary>
        static UIDirection DirectionA()
        {
            var line = new Color(0.62f, 0.82f, 1f);
            return new UIDirection
            {
                id = "A",
                text = new Color(0.86f, 0.92f, 1f),
                textDim = new Color(0.52f, 0.64f, 0.8f),
                accent = new Color(1f, 0.82f, 0.42f),
                line = new Color(line.r, line.g, line.b, 0.28f),
                textShadow = new Color(0f, 0f, 0f, 0.55f),
                textShadowOffset = new Vector2(0f, -0.5f),
                textShadowSoftness = 0.25f,
                panel = new UIShapeStyle
                {
                    fill = new Color(0.015f, 0.03f, 0.065f, 0.84f), fillShade = 0.2f,
                    border = new Color(line.r, line.g, line.b, 0.3f), borderPx = 1f, cornerPx = 0f,
                    bracket = new Color(0.8f, 0.92f, 1f, 0.95f), bracketLenPx = 22f, bracketPx = 2f,
                    tickSpacingPx = 12f, tickLenPx = 5f, tickAlpha = 0.4f,
                    glowPx = 30f, glowStrength = 0.025f, shadowPx = 30f, shadowAlpha = 0.25f, stateGlow = 0.3f,
                },
                button = new UIShapeStyle
                {
                    fill = new Color(0.25f, 0.5f, 0.9f, 0.1f),
                    border = new Color(line.r, line.g, line.b, 0.7f), borderPx = 1f, cornerPx = 0f,
                    bracket = new Color(1f, 0.82f, 0.42f, 1f), bracketLenPx = 12f, bracketPx = 2f,
                    glowPx = 22f, glowStrength = 0.12f, stateGlow = 0.6f,
                },
                chip = new UIShapeStyle
                {
                    fill = new Color(0.62f, 0.82f, 1f, 0.08f),
                    border = new Color(line.r, line.g, line.b, 0.55f), borderPx = 1f, cornerPx = 3f,
                },
                bar = new UIShapeStyle
                {
                    fill = new Color(0.02f, 0.04f, 0.08f, 0.55f),
                    border = new Color(line.r, line.g, line.b, 0.45f), borderPx = 1f, cornerPx = 0f,
                    bracket = new Color(0.8f, 0.92f, 1f, 0.9f), bracketLenPx = 6f, bracketPx = 2f,
                    glowPx = 8f, glowStrength = 0f, stateGlow = 0.5f,
                    segmentPx = 12f, segmentGapPx = 2f,
                },
                badge = new UIShapeStyle
                {
                    fill = new Color(1f, 0.82f, 0.42f, 0.12f),
                    border = new Color(1f, 0.82f, 0.42f, 0.9f), borderPx = 1f, cornerPx = 0f,
                    glowPx = 10f, glowStrength = 0.1f, stateGlow = 1.2f,
                },
            };
        }

        /// <summary>B, glass: translucent navy, round corners, light along the edge.</summary>
        static UIDirection DirectionB()
        {
            var edge = new Color(0.78f, 0.86f, 1f);
            return new UIDirection
            {
                id = "B",
                text = new Color(0.96f, 0.97f, 1f),
                textDim = new Color(0.7f, 0.76f, 0.9f),
                accent = new Color(1f, 0.9f, 0.62f),
                line = new Color(edge.r, edge.g, edge.b, 0.16f),
                textShadow = new Color(0f, 0f, 0.05f, 0.45f),
                textShadowOffset = new Vector2(0f, -0.7f),
                textShadowSoftness = 0.45f,
                panel = new UIShapeStyle
                {
                    fill = new Color(0.06f, 0.09f, 0.26f, 0.72f), fillShade = 0.45f,
                    border = new Color(edge.r, edge.g, edge.b, 0.3f), borderPx = 1.5f, cornerPx = 26f,
                    glowPx = 34f, glowStrength = 0.1f, sheen = 0.05f,
                    shadowPx = 36f, shadowAlpha = 0.5f, stateGlow = 0.3f,
                },
                button = new UIShapeStyle
                {
                    fill = new Color(0.36f, 0.5f, 0.98f, 0.42f), fillShade = 0.35f,
                    border = new Color(edge.r, edge.g, edge.b, 0.55f), borderPx = 1.5f, cornerPx = 20f,
                    glowPx = 18f, glowStrength = 0.3f, sheen = 0.16f,
                    shadowPx = 18f, shadowAlpha = 0.35f, stateGlow = 0.7f,
                },
                chip = new UIShapeStyle
                {
                    fill = new Color(1f, 1f, 1f, 0.14f),
                    border = new Color(1f, 1f, 1f, 0.3f), borderPx = 1f, cornerPx = 8f,
                },
                bar = new UIShapeStyle
                {
                    fill = new Color(0.1f, 0.13f, 0.28f, 0.6f), fillShade = 0.3f,
                    border = new Color(edge.r, edge.g, edge.b, 0.35f), borderPx = 1.5f, cornerPx = 11f,
                    glowPx = 8f, glowStrength = 0.25f, sheen = 0.25f,
                    shadowPx = 12f, shadowAlpha = 0.35f, stateGlow = 0.6f,
                },
                badge = new UIShapeStyle
                {
                    fill = new Color(1f, 0.9f, 0.62f, 0.22f),
                    border = new Color(1f, 0.9f, 0.62f, 0.75f), borderPx = 1f, cornerPx = 12f,
                    glowPx = 10f, glowStrength = 0.25f, sheen = 0.15f, stateGlow = 1.2f,
                },
            };
        }

        /// <summary>C, the minimum: no panels at all, only glyphs and their shadow. The bar keeps a thin line.</summary>
        static UIDirection DirectionC()
        {
            return new UIDirection
            {
                id = "C",
                text = Color.white,
                textDim = new Color(0.74f, 0.8f, 0.92f),
                accent = new Color(1f, 0.84f, 0.45f),
                line = new Color(1f, 1f, 1f, 0f),
                textShadow = new Color(0f, 0f, 0f, 0.95f),
                textShadowOffset = new Vector2(0f, -0.9f),
                textShadowSoftness = 0.6f,
                panel = new UIShapeStyle(),
                button = new UIShapeStyle(),
                chip = new UIShapeStyle(),
                bar = new UIShapeStyle
                {
                    fill = new Color(1f, 1f, 1f, 0.14f), cornerPx = 2f,
                    shadowPx = 6f, shadowAlpha = 0.5f, stateGlow = 0.4f,
                },
                badge = new UIShapeStyle(),
            };
        }
    }
}
