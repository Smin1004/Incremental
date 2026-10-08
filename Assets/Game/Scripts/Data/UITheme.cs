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
        [Tooltip("0..1: the fill fades out toward the bottom edge (1 = fully transparent there). The HUD's top veil.")]
        public float fillFade;
    }

    /// <summary>26 §9.2: the charge arc of one ability with a cooldown, drawn just outside the gravity ring.</summary>
    [Serializable]
    public sealed class UIAbilityArc
    {
        [Tooltip("AbilityTable row id. An ability without a cooldown gets no arc.")]
        public string abilityId = string.Empty;
        public Color color = Color.white;
    }

    /// <summary>
    /// 26 §9.2: the skill tree screen. Sizes are canvas units at the 1080p reference; the colors that TreeView used to
    /// keep as constants live here.
    /// </summary>
    [Serializable]
    public sealed class UITreeStyle
    {
        [Header("Nodes")]
        public float statSize = 18f;
        public float gateSize = 32f;
        [Tooltip("Dust kind and ability unlocks: drawn larger than stat nodes (26 §5.3).")]
        public float unlockSize = 26f;
        public float centerSize = 44f;
        [Tooltip("The soft glow behind a node, as a multiple of its size.")]
        public float glowScale = 2.6f;
        [Tooltip("Cost and level text under a node, and its distance below the node.")]
        public float labelSize = 18f;
        public float labelGapPx = 4f;
        public Color locked = new Color(0.45f, 0.5f, 0.62f, 0.55f);
        [Tooltip("Available but not affordable.")]
        public Color shortOfCurrency = new Color(0.75f, 0.8f, 0.95f, 0.9f);
        public Color owned = new Color(1f, 0.93f, 0.72f, 1f);
        public Color maxed = new Color(1f, 0.84f, 0.45f, 1f);
        public Color centerGlow = new Color(1f, 0.85f, 0.6f, 0.55f);
        public Color labelDim = new Color(0.6f, 0.68f, 0.82f, 0.85f);
        public Color labelOk = new Color(0.96f, 0.97f, 1f, 1f);
        public Color labelShort = new Color(1f, 0.55f, 0.48f, 1f);
        [Tooltip("Purchasable nodes breathe: beats per second, how much glow and how much size it adds (× effect intensity).")]
        public float pulseHz = 0.6f;
        public float pulseGlow = 0.45f;
        public float pulseScale = 0.08f;

        [Header("Edges and rings")]
        public float edgeWidth = 2f;
        public Color edgeDim = new Color(0.5f, 0.55f, 0.72f, 0.18f);
        public Color edgeOwned = new Color(0.95f, 0.88f, 0.62f, 0.6f);
        public Color edgeMaxed = new Color(1f, 0.9f, 0.55f, 0.95f);
        public float ringWidth = 2f;
        [Tooltip("Ring circles: the rings bought so far, and the next one.")]
        public Color ringOpen = new Color(0.6f, 0.68f, 0.9f, 0.22f);
        public Color ringNext = new Color(0.6f, 0.68f, 0.9f, 0.07f);

        [Header("Arms (26 §10.1)")]
        [Tooltip("The spiral arms: the seven axes of 12 §5 are regrouped into seven arms 360/7 degrees apart (gate; dust and dust " +
                 "kinds; planets and fragments; stamina; sale; gravity and follow; abilities), and every arm turns with the " +
                 "ring. Off = the radial layout of 12 §5, each node at its own angle.")]
        public bool arms = true;
        [Tooltip("Degrees an arm turns per ring: node angle = arm angle + this × ring. 0 = straight spokes; 16 turns ring 11 by 176° (the developer chose 16 from the build captures, 24 §20).")]
        public float armTwistDeg = 16f;
        [Tooltip("Degrees a sub-axis sits beside its arm (dust kinds beside dust, fragments beside planets, follow beside gravity).")]
        public float armSubOffsetDeg = 8f;
        [Tooltip("The twists the T key cycles through, to compare them while playing.")]
        public float[] armTwistChoices = { 0f, 8f, 12f, 16f };
        [Tooltip("Straight pieces a curved connection is drawn with.")]
        public int edgeSegments = 10;

        [Header("Purchase (26 §5.3)")]
        [Tooltip("The node lights up: a soft flash that grows to flashScale × the node and fades over flashSec.")]
        public Color flash = new Color(1f, 0.95f, 0.8f, 0.9f);
        public float flashSec = 0.4f;
        public float flashScale = 2.5f;
        [Tooltip("A light runs along each connection from an owned prerequisite to the bought node.")]
        public float travelSec = 0.3f;
        public float travelDotPx = 14f;
        [Tooltip("The connections stay lit this long afterwards.")]
        public float edgeLightSec = 0.6f;
        [Tooltip("Small sparks fly out of the node: how many, how long, how far and how large.")]
        public int sparkCount = 6;
        public float sparkSec = 0.45f;
        public float sparkDistPx = 42f;
        public float sparkPx = 8f;
        [Tooltip("A gate purchase brightens its ring circle to this alpha, fading over this long.")]
        public float gateRingAlpha = 0.7f;
        public float gateRingSec = 0.8f;

        [Header("Bars and tooltip")]
        public float topBarHeight = 80f;
        public float bottomBarHeight = 110f;
        [Tooltip("Side margin of the bar contents, and the room FocusFrontier keeps around the purchasable nodes.")]
        public float barPaddingPx = 32f;
        public float viewMargin = 90f;
        public Vector2 nextRunButtonSize = new Vector2(360f, 76f);
        public Vector2 tooltipSize = new Vector2(380f, 160f);
        public float tooltipPaddingPx = 16f;
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
        [Tooltip("Result list: the increase against the comparison run (▲3) and the \"new body\" tag (26 §9.2).")]
        public Color deltaUp = new Color(0.62f, 0.82f, 1f);
        public Color newTag = new Color(1f, 0.84f, 0.45f);
        [Tooltip("Shadow under every glyph: color with alpha (0 = none), offset and softness 0..1.")]
        public Color textShadow = new Color(0f, 0f, 0f, 0.5f);
        public Vector2 textShadowOffset = new Vector2(0f, -0.6f);
        public float textShadowSoftness = 0.3f;
        public UIShapeStyle panel = new UIShapeStyle();
        public UIShapeStyle button = new UIShapeStyle();
        public UIShapeStyle chip = new UIShapeStyle();
        public UIShapeStyle bar = new UIShapeStyle();
        public UIShapeStyle badge = new UIShapeStyle();
        [Tooltip("The dark band along the top of the run screen that keeps the HUD readable over bright dust (26 §9.2).")]
        public UIShapeStyle veil = new UIShapeStyle();
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

        [Header("HUD (26 §9.2)")]
        public Color hudDebugText = new Color(1f, 1f, 0.6f, 1f);
        public Color hudToastText = new Color(1f, 1f, 0.75f, 1f);
        [Tooltip("Margins of the HUD texts from the screen edge and the top, and the height of the top veil.")]
        public float hudEdgePx = 24f;
        public float hudTopPx = 12f;
        public float hudVeilHeightPx = 150f;
        [Tooltip("The run income counts up to a new value over this long and bumps once when it changes (× effect intensity).")]
        public float hudIncomeRollSec = 0.35f;
        public float hudIncomePopScale = 0.06f;
        public float hudIncomePopSec = 0.18f;

        [Header("Ability charge arcs (26 §9.2)")]
        [Tooltip("One arc per listed ability, just outside the gravity ring, filled with the charge toward the next trigger. " +
                 "An ability without a cooldown is skipped.")]
        public UIAbilityArc[] abilityArcs =
        {
            new UIAbilityArc { abilityId = "crystal", color = new Color(1f, 0.86f, 0.45f) },
            new UIAbilityArc { abilityId = "implode", color = new Color(0.72f, 0.88f, 1f) },
            new UIAbilityArc { abilityId = "meteor", color = new Color(1f, 0.8f, 0.5f) },
            new UIAbilityArc { abilityId = "blackhole", color = new Color(0.86f, 0.6f, 1f) },
        };
        [Tooltip("Distance of the first arc outside the gravity ring, the spacing between arcs and their width, px.")]
        public float abilityArcGapPx = 7f;
        public float abilityArcSpacingPx = 7f;
        public float abilityArcWidthPx = 3f;
        public float abilityArcAlpha = 0.9f;
        [Tooltip("The faint full circle behind each arc; alpha 0 = none.")]
        public float abilityArcTrackAlpha = 0.12f;
        public int abilityArcSegments = 64;
        [Tooltip("When the ability fires the whole circle lights up for this long, this much wider (× effect intensity).")]
        public float abilityArcFlashSec = 0.35f;
        public float abilityArcFlashWidth = 1.5f;

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

        [Header("Skill tree (26 §9.2)")]
        public UITreeStyle tree = new UITreeStyle();

        [Header("Screen transition (26 §5.4)")]
        [Tooltip("Run → result → tree → run: the veil fades in and out over this long in all (× effect intensity), to this color and alpha, " +
                 "while the camera zooms in by this share.")]
        public float transitionSec = 0.3f;
        public Color transitionColor = new Color(0f, 0.005f, 0.02f, 0.9f);
        public float transitionZoom = 0.04f;

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
                deltaUp = new Color(0.62f, 0.82f, 1f),
                newTag = new Color(1f, 0.82f, 0.42f),
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
                veil = new UIShapeStyle { fill = new Color(0f, 0.01f, 0.03f, 0.6f), fillFade = 1f },
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
                deltaUp = new Color(0.75f, 0.9f, 1f),
                newTag = new Color(1f, 0.9f, 0.62f),
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
                veil = new UIShapeStyle { fill = new Color(0.02f, 0.03f, 0.1f, 0.55f), fillFade = 1f },
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
                deltaUp = new Color(0.62f, 0.82f, 1f),
                newTag = new Color(1f, 0.84f, 0.45f),
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
                veil = new UIShapeStyle { fill = new Color(0f, 0f, 0f, 0.5f), fillFade = 1f },
            };
        }
    }
}
