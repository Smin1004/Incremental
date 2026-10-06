using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>Small helpers to build uGUI (TextMeshPro text, 26 §4) from code so nothing has to be hand-assembled in the scene.</summary>
    public static class UIBuilder
    {
        static Sprite solidSprite;

        /// <summary>
        /// Plain white sprite generated at runtime. uGUI Image ignores Type.Filled when it has no sprite,
        /// so bars and gauges that use fill need one.
        /// </summary>
        public static Sprite SolidSprite()
        {
            if (solidSprite != null) return solidSprite;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "SolidWhite" };
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            solidSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
            solidSprite.name = "SolidWhite";
            return solidSprite;
        }

        static Sprite softDotSprite;

        /// <summary>White radial glow (alpha falls off smoothly to 0 at the edge). Used for node glows, stars and flashes.</summary>
        public static Sprite SoftDotSprite()
        {
            if (softDotSprite != null) return softDotSprite;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "SoftDot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            softDotSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            softDotSprite.name = "SoftDot";
            return softDotSprite;
        }

        /// <summary>
        /// Binds a white _MainTex to a renderer that shares the sprite material but has no sprite of its own (dust mesh,
        /// line renderers). Without it the batcher can reuse the texture of the sprite drawn just before (e.g. a soft glow
        /// whose corner is transparent), which makes the dust invisible.
        /// </summary>
        public static void UseWhiteTexture(Renderer r)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetTexture("_MainTex", Texture2D.whiteTexture);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>Stretches a solid Image between two points of its parent (a line segment of the given width).</summary>
        public static void PlaceLine(RectTransform rt, Vector2 a, Vector2 b, float width)
        {
            Vector2 d = b - a;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = (a + b) * 0.5f;
            rt.sizeDelta = new Vector2(d.magnitude, width);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        public static Canvas CreateCanvas(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // UIShape passes the rect size and state of a shape in the second UV set.
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static void EnsureEventSystem(Transform parent)
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            go.transform.SetParent(parent, false);
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        public static RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform CreateStretch(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// A TextMeshPro label that behaves like the Legacy Text it replaced: no wrapping, overflow allowed, rich text
        /// on. Kerning is off so that digits keep the one width UIStyle gave them.
        /// </summary>
        public static TMP_Text CreateText(string name, Transform parent, TMP_FontAsset font, float fontSize, TextAnchor align, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = CreateRect(name, parent, anchor, pivot, pos, size);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = fontSize;
            t.alignment = Alignment(align);
            t.color = Color.white;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.richText = true;
            var features = t.fontFeatures;
            features.Clear();
            t.fontFeatures = features;
            t.raycastTarget = false;
            return t;
        }

        static TextAlignmentOptions Alignment(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }

        /// <summary>A shape of the panel shader (26 §4) in the look of its role. Not a raycast target unless asked.</summary>
        public static UIShape CreateShape(string name, Transform parent, UIStyle style, UIRole role, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = CreateRect(name, parent, anchor, pivot, pos, size);
            var shape = rt.gameObject.AddComponent<UIShape>();
            shape.material = style.MaterialFor(role);
            shape.Pad = style.Theme.shapePadPx;
            shape.raycastTarget = false;
            return shape;
        }

        public static Image CreateImage(string name, Transform parent, Color color, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Sprite sprite = null)
        {
            var rt = CreateRect(name, parent, anchor, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }
    }
}
