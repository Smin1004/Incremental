using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Incremental
{
    /// <summary>
    /// The button of the new style (26 §4): a panel-shader shape that lights up while hovered and shrinks a little while
    /// pressed, a label, and a key hint chip beside it ("Space"). Light and movement follow the effect intensity; at 0
    /// the button is still, and only the label color shows the hover. Runs on unscaled time (the screens between runs).
    /// </summary>
    public sealed class UIButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        IPointerClickHandler
    {
        GameRoot root;
        UIShape shape;
        TMP_Text label, chipText;
        Action onClick;
        bool over, down;
        float hover;

        public static UIButton Create(GameRoot root, string name, Transform parent, string label, string keyHint,
            Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Action onClick)
        {
            var ui = root.Ui;
            var th = ui.Theme;
            var shape = UIBuilder.CreateShape(name, parent, ui, UIRole.Button, anchor, pivot, pos, size);
            shape.raycastTarget = true;
            var b = shape.gameObject.AddComponent<UIButton>();
            b.root = root;
            b.shape = shape;
            b.onClick = onClick;

            var c = new Vector2(0.5f, 0.5f);
            b.label = UIBuilder.CreateText("Label", shape.transform, ui.FontBold, th.sizeLarge, TextAnchor.MiddleCenter, c, c, Vector2.zero, size);
            b.label.text = label;
            if (!string.IsNullOrEmpty(keyHint))
            {
                // Label and chip sit side by side, centered together.
                float labelW = b.label.GetPreferredValues(label).x;
                var chip = UIBuilder.CreateShape("KeyHint", shape.transform, ui, UIRole.Chip, c, c, Vector2.zero, Vector2.zero);
                chip.Pad = 0f;
                b.chipText = UIBuilder.CreateText("Key", chip.transform, ui.Font, th.sizeTiny, TextAnchor.MiddleCenter, c, c, Vector2.zero, Vector2.zero);
                b.chipText.text = keyHint;
                Vector2 keySize = b.chipText.GetPreferredValues(keyHint);
                var chipSize = new Vector2(keySize.x + th.chipPaddingPx.x * 2f, keySize.y + th.chipPaddingPx.y * 2f);
                float group = labelW + th.buttonChipGapPx + chipSize.x;
                ((RectTransform)b.label.transform).sizeDelta = new Vector2(labelW, size.y);
                ((RectTransform)b.label.transform).anchoredPosition = new Vector2(-group * 0.5f + labelW * 0.5f, 0f);
                chip.rectTransform.sizeDelta = chipSize;
                chip.rectTransform.anchoredPosition = new Vector2(group * 0.5f - chipSize.x * 0.5f, 0f);
                ((RectTransform)b.chipText.transform).sizeDelta = chipSize;
            }
            ui.Changed += b.ApplyStyle;
            b.ApplyStyle();
            return b;
        }

        void OnDestroy()
        {
            if (root != null && root.Ui != null) root.Ui.Changed -= ApplyStyle;
        }

        void OnEnable()
        {
            over = down = false;
            hover = 0f;
            transform.localScale = Vector3.one;
            if (shape != null) shape.State = 0f;
        }

        void ApplyStyle()
        {
            var d = root.Ui.Direction;
            if (chipText != null) chipText.color = d.textDim;
            label.color = Color.Lerp(d.text, d.accent, hover);
        }

        void Update()
        {
            var th = root.Ui.Theme;
            float target = over ? 1f : 0f;
            hover = th.buttonHoverSec > 0f ? Mathf.MoveTowards(hover, target, Time.unscaledDeltaTime / th.buttonHoverSec) : target;
            shape.State = hover;
            label.color = Color.Lerp(root.Ui.Direction.text, root.Ui.Direction.accent, hover);
            float scale = down && over ? Mathf.Lerp(1f, th.buttonPressScale, root.EffectIntensity) : 1f;
            transform.localScale = new Vector3(scale, scale, 1f);
        }

        public void OnPointerEnter(PointerEventData e) => over = true;
        public void OnPointerExit(PointerEventData e) => over = false;
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) down = true; }
        public void OnPointerUp(PointerEventData e) => down = false;
        public void OnPointerClick(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) onClick?.Invoke(); }
    }
}
