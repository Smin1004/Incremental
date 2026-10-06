using System;
using UnityEngine;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// 26 §5.4: the change between the run, the result and the tree. A dark veil fades in over the whole screen while
    /// the camera zooms in a little, the screens switch at the midpoint, and the veil fades out again, transitionSec
    /// in all. The run view (dust, HUD) is kept up until the switch when the run has just ended, so it does not vanish
    /// a frame before the fade. Space during a transition skips to the switch at once, which keeps "Space twice is the
    /// next run" fast (R7). Time and depth follow the effect intensity; at 0 the screens simply switch. Unscaled time.
    /// </summary>
    public sealed class ScreenTransition : MonoBehaviour
    {
        GameRoot root;
        UITheme theme;
        Canvas canvas;
        Image veil;
        Action pending;
        bool holdRun;
        float age = -1f;
        float sec;

        public bool Running => age >= 0f;
        /// <summary>The first half of a transition out of a run: the run screen is still the one to show.</summary>
        public bool HoldingRunView => Running && pending != null && holdRun;
        /// <summary>Camera zoom multiplier right now; 1 outside a transition.</summary>
        public float Zoom { get; private set; } = 1f;

        public void Init(GameRoot gameRoot)
        {
            root = gameRoot;
            theme = root.Ui.Theme;
            canvas = UIBuilder.CreateCanvas("Transition", transform);
            canvas.sortingOrder = 90;
            veil = UIBuilder.CreateStretch("Veil", canvas.transform).gameObject.AddComponent<Image>();
            veil.color = Color.clear;
            veil.raycastTarget = false;
            canvas.gameObject.SetActive(false);
        }

        /// <summary>
        /// Starts a transition whose <paramref name="switchScreens"/> runs at the midpoint. <paramref name="fromRun"/>: the
        /// run view stays up until then. A transition asked for while one is running skips straight to the switch.
        /// </summary>
        public void Play(Action switchScreens, bool fromRun = false)
        {
            float fx = Mathf.Clamp01(root.EffectIntensity);
            float want = theme.transitionSec * fx;
            if (Running)
            {
                pending?.Invoke();
                pending = null;
                switchScreens?.Invoke();
                age = Mathf.Max(age, sec * 0.5f);
                return;
            }
            if (want <= 0.01f)
            {
                switchScreens?.Invoke();
                return;
            }
            sec = want;
            pending = switchScreens;
            holdRun = fromRun;
            age = 0f;
            canvas.gameObject.SetActive(true);
            Apply(0f);
        }

        void Update()
        {
            if (!Running) return;
            age += Time.unscaledDeltaTime;
            float half = sec * 0.5f;
            if (pending != null && age >= half)
            {
                var action = pending;
                pending = null;
                action.Invoke();
            }
            if (age >= sec)
            {
                age = -1f;
                Apply(0f);
                canvas.gameObject.SetActive(false);
                return;
            }
            float k = age < half ? age / half : 1f - (age - half) / half;
            Apply(Mathf.Clamp01(k));
        }

        /// <summary><paramref name="k"/> 0..1: how deep into the fade (1 at the switch).</summary>
        void Apply(float k)
        {
            float s = k * k * (3f - 2f * k);
            var c = theme.transitionColor;
            c.a *= s;
            veil.color = c;
            Zoom = 1f - theme.transitionZoom * s * Mathf.Clamp01(root.EffectIntensity);
        }
    }
}
