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
