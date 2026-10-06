using System;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace Incremental
{
    /// <summary>What a shape is; each role has its own material (one look per visual direction).</summary>
    public enum UIRole { Panel, Button, Chip, Bar, Badge, Veil }

    /// <summary>
    /// The UI look at runtime (26 §4), built once from <see cref="UITheme"/>: the two SDF font assets (dynamic atlas,
    /// made from the theme's font files), one panel-shader material per shape role, and the two things the debug keys
    /// switch, the visual direction (A / B / C, 26 §3) and the stamina display (26 §5.1). Views read colors from
    /// <see cref="Direction"/> and re-read them on <see cref="Changed"/>. Nothing here is saved.
    /// </summary>
    public sealed class UIStyle
    {
        static readonly int IdEffect = Shader.PropertyToID("_UIEffect");
        static readonly int IdFill = Shader.PropertyToID("_Fill");
        static readonly int IdBorder = Shader.PropertyToID("_Border");
        static readonly int IdBracket = Shader.PropertyToID("_Bracket");
        static readonly int IdShape = Shader.PropertyToID("_Shape");
        static readonly int IdMarks = Shader.PropertyToID("_Marks");
        static readonly int IdGlow = Shader.PropertyToID("_Glow");
        static readonly int IdOuter = Shader.PropertyToID("_Outer");
        static readonly int IdSegment = Shader.PropertyToID("_Segment");

        const string Digits = "0123456789";

        readonly Material[] materials;
        bool edited;

        public UITheme Theme { get; }
        /// <summary>Regular weight: labels and body text.</summary>
        public TMP_FontAsset Font { get; }
        /// <summary>Heavier weight: titles and values.</summary>
        public TMP_FontAsset FontBold { get; }
        public int DirectionIndex { get; private set; }
        public UIDirection Direction => Theme.Direction(DirectionIndex);
        /// <summary>0 = (a) the bar at the top, 1 = (b) the gravity ring (26 §5.1).</summary>
        public int StaminaMode { get; private set; }
        /// <summary>The direction or the stamina display changed: re-read colors and layouts.</summary>
        public event Action Changed;

        public UIStyle(UITheme theme)
        {
            if (theme == null)
            {
                Debug.LogError("[UI] GameRoot has no UITheme (menu Incremental/Setup/1. Create Data Assets, then 3. Build Prototype Scene); using the code defaults without fonts");
                theme = ScriptableObject.CreateInstance<UITheme>();
            }
            Theme = theme;
            Font = MakeFont(theme.fontRegular, "UI Regular");
            FontBold = theme.fontBold != null ? MakeFont(theme.fontBold, "UI Bold") : Font;

            var roles = (UIRole[])Enum.GetValues(typeof(UIRole));
            materials = new Material[roles.Length];
            for (int i = 0; i < roles.Length; i++)
            {
                if (theme.panelMaterial == null) continue;
                materials[i] = new Material(theme.panelMaterial) { name = "UI " + roles[i], hideFlags = HideFlags.DontSave };
            }
            StaminaMode = Mathf.Clamp(theme.staminaMode, 0, 1);
            DirectionIndex = Mathf.Clamp(theme.direction, 0, Mathf.Max(0, (theme.directions != null ? theme.directions.Length : 1) - 1));
            Apply();
#if UNITY_EDITOR
            UITheme.Edited += OnThemeEdited;
#endif
        }

#if UNITY_EDITOR
        void OnThemeEdited(UITheme t)
        {
            if (t == Theme) edited = true;
        }
#endif

        /// <summary>
        /// Once a frame (GameRoot). In the editor a theme value changed in the inspector during play is picked up here:
        /// the shapes and the glyph shadow are rewritten and the views re-read their colors.
        /// </summary>
        public void Tick()
        {
            if (!edited) return;
            edited = false;
            DirectionIndex = Mathf.Clamp(DirectionIndex, 0, Mathf.Max(0, (Theme.directions != null ? Theme.directions.Length : 1) - 1));
            Apply();
            Changed?.Invoke();
        }

        /// <summary>Lets go of the theme's edit notification (GameRoot.OnDestroy).</summary>
        public void Dispose()
        {
#if UNITY_EDITOR
            UITheme.Edited -= OnThemeEdited;
#endif
        }

        /// <summary>The material of a shape role in the current direction (shared by every shape of that role).</summary>
        public Material MaterialFor(UIRole role) => materials[(int)role];

        /// <summary>Pushes the effect intensity to the UI shader: edge light, hover light and flashes follow it (26 §4).</summary>
        public static void SetEffect(float intensity) => Shader.SetGlobalFloat(IdEffect, Mathf.Clamp01(intensity));

        public void SetDirection(int index)
        {
            int n = Theme.directions != null ? Theme.directions.Length : 0;
            if (n == 0) return;
            DirectionIndex = ((index % n) + n) % n;
            Apply();
            Changed?.Invoke();
        }

        public void SetStaminaMode(int mode)
        {
            StaminaMode = Mathf.Clamp(mode, 0, 1);
            Changed?.Invoke();
        }

        /// <summary>Writes the current direction into the role materials and the glyph shadow into the font materials.</summary>
        void Apply()
        {
            var d = Direction;
            if (d == null) return;
            Write(UIRole.Panel, d.panel, false);
            Write(UIRole.Button, d.button, false);
            Write(UIRole.Chip, d.chip, false);
            Write(UIRole.Bar, d.bar, true);
            Write(UIRole.Badge, d.badge, false);
            Write(UIRole.Veil, d.veil, false);
            ApplyTextShadow(Font, d);
            if (FontBold != Font) ApplyTextShadow(FontBold, d);
            // The shadow changes how much room a glyph quad needs, which each text caches.
            foreach (var t in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.font != Font && t.font != FontBold) continue;
                t.UpdateMeshPadding();
                t.SetVerticesDirty();
            }
        }

        void Write(UIRole role, UIShapeStyle s, bool bar)
        {
            var m = materials[(int)role];
            if (m == null || s == null) return;
            m.SetColor(IdFill, s.fill);
            m.SetColor(IdBorder, s.border);
            m.SetColor(IdBracket, s.bracket);
            m.SetVector(IdShape, new Vector4(s.cornerPx, s.border.a > 0f ? s.borderPx : 0f, s.bracket.a > 0f ? s.bracketLenPx : 0f, s.bracketPx));
            m.SetVector(IdMarks, new Vector4(s.tickSpacingPx, s.tickLenPx, s.tickAlpha, bar ? 1f : 0f));
            m.SetVector(IdGlow, new Vector4(s.glowPx, s.glowStrength, s.sheen, s.fillShade));
            m.SetVector(IdOuter, new Vector4(s.shadowPx, s.shadowAlpha, s.stateGlow, 0f));
            m.SetVector(IdSegment, new Vector4(s.segmentPx, s.segmentGapPx, s.fillFade, 0f));
        }

        static void ApplyTextShadow(TMP_FontAsset font, UIDirection d)
        {
            if (font == null || font.material == null) return;
            var m = font.material;
            if (d.textShadow.a > 0f)
            {
                m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                m.SetColor(ShaderUtilities.ID_UnderlayColor, d.textShadow);
                m.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, d.textShadowOffset.x);
                m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, d.textShadowOffset.y);
                m.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0f);
                m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, Mathf.Clamp01(d.textShadowSoftness));
            }
            else m.DisableKeyword(ShaderUtilities.Keyword_Underlay);
        }

        /// <summary>
        /// An SDF font asset with a dynamic atlas from a font file: glyphs are rendered into the atlas the first time a
        /// text uses them, so the whole Hangul range is available without shipping it as a texture.
        /// </summary>
        TMP_FontAsset MakeFont(Font source, string name)
        {
            if (source == null)
            {
                Debug.LogError("[UI] UITheme has no font file for '" + name + "'; Korean text will not show");
                return TMP_Settings.defaultFontAsset;
            }
            var fa = TMP_FontAsset.CreateFontAsset(source, Theme.fontSamplingSize, Theme.fontPaddingPx, GlyphRenderMode.SDFAA,
                Theme.fontAtlasSize, Theme.fontAtlasSize, AtlasPopulationMode.Dynamic, true);
            if (fa == null)
            {
                Debug.LogError("[UI] could not make a font asset from " + source.name);
                return TMP_Settings.defaultFontAsset;
            }
            fa.name = name;
            fa.hideFlags = HideFlags.DontSave;
            if (Theme.textShader != null) fa.material.shader = Theme.textShader;
            if (Theme.tabularDigits) MakeDigitsTabular(fa);
            return fa;
        }

        /// <summary>
        /// 26 §4: every digit gets the advance of the widest one and is centered in it, so a number that changes keeps
        /// its width. Pretendard's default digits are proportional (the 1 is a quarter narrower).
        /// </summary>
        static void MakeDigitsTabular(TMP_FontAsset fa)
        {
            if (!fa.TryAddCharacters(Digits)) return;
            float max = 0f;
            foreach (char ch in Digits) max = Mathf.Max(max, fa.characterLookupTable[ch].glyph.metrics.horizontalAdvance);
            foreach (char ch in Digits)
            {
                var glyph = fa.characterLookupTable[ch].glyph;
                var m = glyph.metrics;
                float extra = max - m.horizontalAdvance;
                glyph.metrics = new GlyphMetrics(m.width, m.height, m.horizontalBearingX + extra * 0.5f, m.horizontalBearingY, max);
            }
        }
    }
}
