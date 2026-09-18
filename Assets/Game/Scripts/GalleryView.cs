using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// Debug tier gallery (14 §3): tiers 1..N in one row at their display size, no labels, over a plain backdrop.
    /// The light comes from the mouse direction (relative to the row center); the wheel zooms. Opening it hides the
    /// other screens' canvases and pauses a run. For acceptance captures and store assets.
    /// </summary>
    public sealed class GalleryView : MonoBehaviour
    {
        GameRoot root;
        GameParams p;
        Transform content;
        SpriteRenderer backdrop;
        Canvas canvas;
        readonly List<PlanetVisual> bodies = new List<PlanetVisual>();
        readonly List<Canvas> hidden = new List<Canvas>();
        Vector2 lightDir = new Vector2(1f, 1f).normalized;
        Vector2 lastMouse = new Vector2(float.NaN, float.NaN);
        float zoom = 1f;

        public bool IsOpen { get; private set; }

        float Ppu => (float)p.pixelsPerUnit;

        public void Init(GameRoot gameRoot)
        {
            root = gameRoot;
            p = root.gameParams;
            content = new GameObject("Content").transform;
            content.SetParent(transform, false);

            backdrop = new GameObject("Backdrop").AddComponent<SpriteRenderer>();
            backdrop.transform.SetParent(content, false);
            backdrop.sprite = UIBuilder.SolidSprite();
            if (root.unlitMaterial != null) backdrop.sharedMaterial = root.unlitMaterial;
            backdrop.sortingOrder = 900;

            var tiers = root.celestialTable.tiers;
            for (int i = 0; i < tiers.Count; i++)
            {
                var v = PlanetVisual.Create("Tier" + tiers[i].tier, content, root.planetMaterial, 910);
                v.SetLook(tiers[i].look, 11f + i * 7.3f, p.planetAxisPitchDeg);
                bodies.Add(v);
            }

            canvas = UIBuilder.CreateCanvas("GalleryHint", transform);
            canvas.sortingOrder = 100;
            var hint = UIBuilder.CreateText("Hint", canvas.transform, root.Hud.Font, 20, TextAnchor.LowerCenter,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(1400f, 30f));
            hint.text = UIStrings.GalleryHint;
            hint.color = new Color(1f, 1f, 1f, 0.45f);

            content.gameObject.SetActive(false);
            canvas.gameObject.SetActive(false);
        }

        public void Toggle() => SetOpen(!IsOpen);

        public void SetOpen(bool open)
        {
            if (IsOpen == open) return;
            IsOpen = open;
            if (open)
            {
                hidden.Clear();
                foreach (var c in root.GetComponentsInChildren<Canvas>(false))
                {
                    if (!c.isRootCanvas || c == canvas || !c.enabled) continue;
                    c.enabled = false;
                    hidden.Add(c);
                }
            }
            else
            {
                foreach (var c in hidden)
                    if (c != null) c.enabled = true;
                hidden.Clear();
            }
            content.gameObject.SetActive(open);
            canvas.gameObject.SetActive(open);
            if (open) Layout();
        }

        void Update()
        {
            if (!IsOpen) return;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f)
                {
                    zoom = Mathf.Clamp(zoom * (1f + Mathf.Sign(scroll) * (float)p.galleryZoomStep), (float)p.galleryZoomMin, (float)p.galleryZoomMax);
                }
                // The light follows the mouse only when it moves, so a direction set from code stays for captures.
                Vector2 screen = mouse.position.ReadValue();
                if (screen != lastMouse)
                {
                    bool first = float.IsNaN(lastMouse.x);
                    lastMouse = screen;
                    Vector2 d = root.ToWorld(screen) - (Vector2)root.Cam.transform.position;
                    if (!first && d.sqrMagnitude > 0.04f) lightDir = d.normalized;
                }
            }
            Layout();
        }

        /// <summary>Light direction in the screen plane (also set by the mouse).</summary>
        public Vector2 LightDirection
        {
            get => lightDir;
            set { if (value.sqrMagnitude > 1e-6f) lightDir = value.normalized; }
        }

        public float Zoom
        {
            get => zoom;
            set => zoom = Mathf.Clamp(value, (float)p.galleryZoomMin, (float)p.galleryZoomMax);
        }

        /// <summary>One row centered on the camera, bodies at sizePx × zoom with a fixed gap (rings get their width); shrinks to fit.</summary>
        void Layout()
        {
            var cam = root.Cam;
            var center = cam.transform.position;
            float halfW = cam.orthographicSize * cam.aspect, halfH = cam.orthographicSize;
            content.position = new Vector3(center.x, center.y, 0f);
            backdrop.transform.localScale = new Vector3(halfW * 2f / backdrop.sprite.bounds.size.x, halfH * 2f / backdrop.sprite.bounds.size.y, 1f);
            backdrop.color = root.Cam.backgroundColor;

            var tiers = root.celestialTable.tiers;
            float gap = (float)p.galleryGapPx / Ppu;
            float sum = 0f;
            for (int i = 0; i < tiers.Count; i++) sum += Slot(tiers[i]);
            float width = sum * zoom + gap * Mathf.Max(0, tiers.Count - 1);
            float fit = Mathf.Min(1f, (halfW * 2f * 0.95f) / Mathf.Max(width, 1e-3f));
            float x = -width * fit * 0.5f;
            for (int i = 0; i < tiers.Count; i++)
            {
                float slot = Slot(tiers[i]) * zoom * fit;
                var v = bodies[i];
                v.transform.localPosition = new Vector3(x + slot * 0.5f, 0f, 0f);
                v.SetDiameter((float)tiers[i].sizePx / Ppu * zoom * fit);
                v.SetLightDirection(lightDir);
                x += slot + gap * fit;
            }
        }

        /// <summary>Width a body takes in the row at zoom 1: the disc, or the ring when it has one.</summary>
        float Slot(CelestialTier t) => (float)t.sizePx / Ppu * (t.look != null && t.look.HasRing ? t.look.ringOuter : 1f);
    }
}
