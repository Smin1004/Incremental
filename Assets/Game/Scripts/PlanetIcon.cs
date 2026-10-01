using UnityEngine;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// A body of the planet shader drawn inside a canvas (26 §5.2): the mini icons of the result list, so the list
    /// reads as the same bodies that orbit on the right. uGUI cannot use property blocks, so each icon owns a copy of
    /// the planet material and <see cref="PlanetVisual.ApplyLook"/> writes the look into it. The rect is the disc
    /// times the look's extent, so rings, tails and glows have their room as they do in the world.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PlanetIcon : MaskableGraphic
    {
        Material instance;
        PlanetLook look;
        float fade = 1f;

        public static PlanetIcon Create(string name, Transform parent, Material planetMaterial)
        {
            var c = new Vector2(0.5f, 0.5f);
            var rt = UIBuilder.CreateRect(name, parent, c, c, Vector2.zero, Vector2.zero);
            var icon = rt.gameObject.AddComponent<PlanetIcon>();
            icon.raycastTarget = false;
            if (planetMaterial != null)
            {
                icon.instance = new Material(planetMaterial) { name = "PlanetIcon", hideFlags = HideFlags.DontSave };
                icon.material = icon.instance;
            }
            return icon;
        }

        /// <summary>Shows this look with a disc of <paramref name="discPx"/> (canvas px), lit from a screen direction.</summary>
        public void Set(PlanetLook l, float seed, double axisPitchDeg, Vector2 lightDir, float discPx)
        {
            look = l;
            if (l == null || instance == null) return;
            PlanetVisual.ApplyLook(instance, l, seed, axisPitchDeg, lightDir.sqrMagnitude > 1e-6f ? lightDir.normalized : Vector2.right, fade);
            float size = discPx * l.Extent();
            rectTransform.sizeDelta = new Vector2(size, size);
        }

        /// <summary>Overall opacity 0..1 (the shader ignores vertex colors, so a CanvasGroup does not reach it).</summary>
        public void SetFade(float a)
        {
            if (Mathf.Approximately(a, fade)) return;
            fade = a;
            PlanetVisual.ApplyFade(instance, look, fade);
        }

        protected override void OnDestroy()
        {
            if (instance != null) Destroy(instance);
            base.OnDestroy();
        }
    }
}
