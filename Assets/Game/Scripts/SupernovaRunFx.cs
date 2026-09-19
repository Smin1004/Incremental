using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// The ending on the run screen (15 §4.2), render only. The first blue giant stays where it formed (it grows from the
    /// forming body's size with the usual pop and its glow swells while the dust falls in), and after endingPullSec the
    /// screen darkens over endingFadeSec. Timing follows GameRoot.EndingAge (logic time); the ignition flash and ring
    /// come from IgnitionFx. Hidden whenever the ending is not running.
    /// </summary>
    public sealed class SupernovaRunFx : MonoBehaviour
    {
        GameRoot root;
        GameParams p;
        PlanetVisual star;
        SpriteRenderer shade;
        float fromPx, toPx;

        public void Init(GameRoot gameRoot)
        {
            root = gameRoot;
            p = root.gameParams;
            star = PlanetVisual.Create("EndingStar", transform, root.planetMaterial, 12);
            star.gameObject.SetActive(false);
            shade = new GameObject("Darken").AddComponent<SpriteRenderer>();
            shade.transform.SetParent(transform, false);
            shade.sprite = UIBuilder.SolidSprite();
            if (root.unlitMaterial != null) shade.sharedMaterial = root.unlitMaterial;
            shade.sortingOrder = 80;
            shade.enabled = false;
        }

        /// <summary>The blue giant was made at <paramref name="pos"/>: it keeps the forming body's surface and grows from its size.</summary>
        public void Begin(Vector2 pos, CelestialTier tier, float seed, double fromDiameterPx)
        {
            star.transform.position = new Vector3(pos.x, pos.y, 0f);
            star.SetLook(tier.look, seed, p.planetAxisPitchDeg);
            star.SetGlowMul(1f);
            star.SetFade(1f);
            toPx = (float)tier.sizePx;
            fromPx = fromDiameterPx > 0 ? (float)fromDiameterPx : toPx;
            star.gameObject.SetActive(true);
            LateUpdate();
        }

        void LateUpdate()
        {
            bool on = root.EndingActive && root.Phase == GamePhase.Run;
            if (star.gameObject.activeSelf != on) star.gameObject.SetActive(on);
            if (!on)
            {
                if (shade.enabled) shade.enabled = false;
                return;
            }

            float age = (float)root.EndingAge;
            float ppu = (float)p.pixelsPerUnit;
            float popDur = Mathf.Max(0.01f, (float)p.planetPopDurationSec);
            float tPop = Mathf.Clamp01(age / popDur);
            float d = Mathf.Lerp(fromPx, toPx, tPop) * (1f + (float)p.planetPopScale * root.EffectIntensity * Mathf.Sin(tPop * Mathf.PI));
            star.SetDiameter(d / ppu);
            float pull = Mathf.Clamp01(age / Mathf.Max(0.01f, (float)p.endingPullSec));
            star.SetGlowMul(1f + (float)p.endingStarGlowGain * pull);

            float k = Mathf.Clamp01((age - (float)p.endingPullSec) / Mathf.Max(0.01f, (float)p.endingFadeSec));
            float a = k * k * (3f - 2f * k) * (float)p.endingFadeAlpha;
            shade.enabled = a > 0f;
            if (!shade.enabled) return;
            Cover(shade, root.Cam);
            shade.color = new Color(0f, 0f, 0f, a);
        }

        /// <summary>Scales a solid sprite to cover the camera view.</summary>
        public static void Cover(SpriteRenderer sr, Camera cam)
        {
            float h = cam.orthographicSize * 2f, w = h * cam.aspect;
            var c = cam.transform.position;
            sr.transform.position = new Vector3(c.x, c.y, 0f);
            var b = sr.sprite.bounds.size;
            sr.transform.localScale = new Vector3(w * 1.05f / b.x, h * 1.05f / b.y, 1f);
        }
    }
}
