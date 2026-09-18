using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>Small helpers to build uGUI (Legacy Text) from code so nothing has to be hand-assembled in the scene.</summary>
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
        static Sprite shadowSprite;

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
        /// Sphere shading as a black overlay for a circle of the same size: transparent on the lit side (−x), dark on the
        /// far side (+x), so a planet reads as a lit ball with a crescent shadow (13 §4). Rotate it so +x points away from the light.
        /// </summary>
        public static Sprite ShadowSprite()
        {
            if (shadowSprite != null) return shadowSprite;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "PlanetShadow", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            var light = new Vector3(-0.8f, 0f, 0.6f).normalized;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float nx = (x + 0.5f) / n * 2f - 1f, ny = (y + 0.5f) / n * 2f - 1f;
                float r2 = nx * nx + ny * ny;
                float a = 0f;
                if (r2 < 1f)
                {
                    float nz = Mathf.Sqrt(1f - r2);
                    float lit = Mathf.Clamp01(nx * light.x + ny * light.y + nz * light.z);
                    a = 1f - Mathf.SmoothStep(0f, 1f, lit * 1.4f);
                    a *= Mathf.Clamp01((1f - Mathf.Sqrt(r2)) * n * 0.5f); // soft rim, no hard edge outside the disc
                }
                px[y * n + x] = new Color32(0, 0, 0, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            shadowSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            shadowSprite.name = "PlanetShadow";
            return shadowSprite;
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

        /// <summary>Korean-capable dynamic font: OS "Malgun Gothic" on Windows, otherwise the built-in legacy font.</summary>
        public static Font LoadFont()
        {
            Font font = null;
            try
            {
                string[] os = Font.GetOSInstalledFontNames();
                if (Array.IndexOf(os, "Malgun Gothic") >= 0) font = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 32);
            }
            catch (Exception) { font = null; }
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }

        public static Canvas CreateCanvas(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
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

        public static Text CreateText(string name, Transform parent, Font font, int fontSize, TextAnchor align, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = CreateRect(name, parent, anchor, pivot, pos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
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

        /// <summary>Plain uGUI Button (solid background + Legacy Text label). Disabled state is clearly greyed out.</summary>
        public static Button CreateButton(string name, Transform parent, Font font, int fontSize, string label,
            Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick, out Text labelText)
        {
            var img = CreateImage(name, parent, new Color(0.25f, 0.45f, 0.8f, 1f), anchor, pivot, pos, size, SolidSprite());
            img.raycastTarget = true;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            colors.disabledColor = new Color(0.35f, 0.35f, 0.4f, 0.6f);
            btn.colors = colors;
            labelText = CreateText("Label", img.transform, font, fontSize, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            labelText.text = label;
            if (onClick != null) btn.onClick.AddListener(onClick);
            return btn;
        }
    }
}
