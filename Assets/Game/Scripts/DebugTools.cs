using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Incremental
{
    /// <summary>
    /// Debug keys (editor and development builds only). F1: overlay (FPS, dust count, tick ms). F2: performance mode
    /// (runtime override of dust_cap / spawn_rate, assets untouched). F3: currency = max(currency × 10, 1,000).
    /// F4: autoplay bot toggle. F5: save now. F6: unlock the next tier for free. F7: toggle number notation (settings.json).
    /// F8: tier gallery (14 §3). Shift+F9: delete the save and start again from run 1. F11: effect intensity 1 → 0.5 → 0 (14 §6).
    /// F12: screenshot to persistentDataPath.
    /// </summary>
    public sealed class DebugTools : MonoBehaviour
    {
        [Tooltip("F2 performance mode: the dust cap with every node bought (14 §5: 3,700).")]
        public double perfDustCap = 3700;
        public double perfSpawnRate = 100;
        [Tooltip("F3: currency = max(currency × cheatCurrencyMult, cheatCurrencyMin).")]
        public double cheatCurrencyMult = 10;
        public double cheatCurrencyMin = 1000;
        [Tooltip("F11 cycles the runtime effect intensity through these values (14 §6; the options screen is week 7).")]
        public float[] effectSteps = { 1f, 0.5f, 0f };

        GameRoot root;
        readonly System.Collections.Generic.List<PendingCapture> pendingCaptures = new System.Collections.Generic.List<PendingCapture>();
        float captureClock;
        bool overlay;
        bool perfMode;
        float fpsTimer;
        int fpsFrames;
        float fps;

        public bool Overlay => overlay;
        public bool PerfMode => perfMode;
        public bool BotEnabled { get; set; }
        public float Fps => fps;

        public void Init(GameRoot gameRoot) { root = gameRoot; }

        struct PendingCapture
        {
            public float at;
            public string file;
        }

        /// <summary>
        /// Screenshots into <paramref name="dir"/> (prefix_&lt;ms&gt;.png) when <paramref name="clock"/> reaches each delay,
        /// earliest first, at most one per frame. With a null clock the delays count from now in frame time. Pass the
        /// effect's own clock (IgnitionFx.Age) for short moments a tool call cannot hit by hand: the editor stalls during
        /// tool calls, and the effect's delta time is capped while a wall clock is not.
        /// </summary>
        public void ScheduleCaptures(string dir, string prefix, System.Func<float> clock, params float[] delaysSec)
        {
            Directory.CreateDirectory(dir);
            captureClockSource = clock;
            captureClock = 0f;
            pendingCaptures.Clear();
            var sorted = (float[])delaysSec.Clone();
            System.Array.Sort(sorted);
            foreach (var d in sorted)
                pendingCaptures.Add(new PendingCapture { at = d, file = Path.Combine(dir, prefix + "_" + Mathf.RoundToInt(d * 1000f) + "ms.png") });
        }

        System.Func<float> captureClockSource;

        readonly System.Collections.Generic.List<float> sampleFrames = new System.Collections.Generic.List<float>();
        float sampleLeft = -1f;
        float sampleDelay;

        /// <summary>
        /// Frame-time sample for the 14 §9 performance checks: waits <paramref name="delaySec"/> (so the stall of the
        /// tool call that started it is not counted), then records unscaled frame times for <paramref name="seconds"/>.
        /// Read the result with <see cref="SampleResult"/>.
        /// </summary>
        public void BeginSample(float delaySec, float seconds)
        {
            sampleFrames.Clear();
            sampleDelay = delaySec;
            sampleLeft = seconds;
        }

        /// <summary>"frames, avg fps, fps at the 99th percentile frame time, max frame ms", or the state while sampling.</summary>
        public string SampleResult()
        {
            if (sampleLeft > 0f || sampleDelay > 0f) return "sampling";
            if (sampleFrames.Count == 0) return "no sample";
            var sorted = new System.Collections.Generic.List<float>(sampleFrames);
            sorted.Sort();
            float sum = 0f;
            foreach (var f in sorted) sum += f;
            float p99 = sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * 0.99f))];
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "frames={0} avgFps={1:F1} p99Fps={2:F1} maxMs={3:F1}", sorted.Count, sorted.Count / sum, 1f / p99, sorted[sorted.Count - 1] * 1000f);
        }

        void Update()
        {
            if (sampleDelay > 0f) sampleDelay -= Time.unscaledDeltaTime;
            else if (sampleLeft > 0f)
            {
                sampleFrames.Add(Time.unscaledDeltaTime);
                sampleLeft -= Time.unscaledDeltaTime;
            }
            captureClock += Time.deltaTime;
            float now = captureClockSource != null ? captureClockSource() : captureClock;
            if (pendingCaptures.Count > 0 && now >= pendingCaptures[0].at)
            {
                ScreenCapture.CaptureScreenshot(pendingCaptures[0].file);
                pendingCaptures.RemoveAt(0);
            }
            fpsFrames++;
            fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer >= 0.5f)
            {
                fps = fpsFrames / fpsTimer;
                fpsFrames = 0;
                fpsTimer = 0f;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f1Key.wasPressedThisFrame) overlay = !overlay;
                if (kb.f2Key.wasPressedThisFrame) SetPerfMode(!perfMode);
                if (kb.f3Key.wasPressedThisFrame) root.DebugBoostCurrency(cheatCurrencyMult, cheatCurrencyMin);
                if (kb.f4Key.wasPressedThisFrame && root.Bot != null) root.Bot.SetEnabled(!root.Bot.Enabled);
                if (kb.f5Key.wasPressedThisFrame) SaveNow();
                if (kb.f6Key.wasPressedThisFrame) root.DebugUnlockNext();
                if (kb.f7Key.wasPressedThisFrame) ToggleNotation();
                if (kb.f8Key.wasPressedThisFrame && root.Gallery != null) root.Gallery.Toggle();
                if (kb.f9Key.wasPressedThisFrame && kb.shiftKey.isPressed) root.ResetProgress();
                if (kb.f11Key.wasPressedThisFrame) CycleEffectIntensity();
                if (kb.f12Key.wasPressedThisFrame) Screenshot();
            }
#endif

            root.Hud.SetDebug(overlay
                ? string.Format(UIStrings.DebugOverlay, fps, root.Dust.Count, root.LastTickMs, root.MaxTickMs,
                    perfMode ? UIStrings.On : UIStrings.Off, BotEnabled ? UIStrings.On : UIStrings.Off, root.EffectIntensity)
                : string.Empty);
        }

        /// <summary>F11: next value of <see cref="effectSteps"/> after the current intensity.</summary>
        public void CycleEffectIntensity()
        {
            if (effectSteps == null || effectSteps.Length == 0) return;
            int cur = 0;
            float best = float.MaxValue;
            for (int i = 0; i < effectSteps.Length; i++)
            {
                float d = Mathf.Abs(effectSteps[i] - root.EffectIntensity);
                if (d < best) { best = d; cur = i; }
            }
            root.EffectIntensity = Mathf.Clamp01(effectSteps[(cur + 1) % effectSteps.Length]);
            root.Hud.ShowToast(string.Format(UIStrings.EffectIntensity, root.EffectIntensity));
            Debug.Log("[Debug] effect intensity: " + root.EffectIntensity);
        }

        public void SetPerfMode(bool on)
        {
            perfMode = on;
            root.Override = new StatsOverride { enabled = on, dustCap = perfDustCap, spawnRate = perfSpawnRate };
        }

        public void ToggleNotation()
        {
            Fmt.Mode = Fmt.Mode == Notation.Letters ? Notation.Scientific : Notation.Letters;
            root.SaveSettings();
            Debug.Log("[Debug] notation: " + Fmt.Mode);
        }

        public void SaveNow()
        {
            root.Save();
            Debug.Log("[Debug] saved: " + root.Store.SavePath);
        }

        public string Screenshot()
        {
            string file = Path.Combine(root.DataDir, "screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            ScreenCapture.CaptureScreenshot(file);
            Debug.Log(string.Format(UIStrings.ScreenshotSaved, file));
            return file;
        }
    }
}
