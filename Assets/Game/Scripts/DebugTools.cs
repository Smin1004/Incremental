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
    /// F8: tier gallery (14 §3). Shift+F9: delete the save and start again from run 1.
    /// F10: next stamina drain preset, in effect at once, never saved (18 §A1). F11: effect intensity 1 → 0.5 → 0 (14 §6).
    /// F12: screenshot to persistentDataPath. End: replay the ending sequence between runs without touching the save (15 §4.6).
    /// Home: drain the stamina, ending the run at once.
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

        /// <summary>
        /// Reproducible run screen for before / after captures (15 §3): tiers unlocked up to <paramref name="unlockTier"/>,
        /// <paramref name="dust"/> dust from a fixed seed, the pointer held at <paramref name="viewport"/> (0..1), the held
        /// mass at <paramref name="massShare"/> of the way from the second-highest to the highest unlocked threshold, and
        /// the run clock reset to 0 (capture with ScheduleCaptures on <c>() =&gt; (float)root.Run.elapsed</c>). The pointer
        /// moves at <paramref name="velocityPx"/> (screen px per second of run time) so it sweeps into dust as a player does.
        /// Call <see cref="EndStaging"/> afterwards. Run screen only.
        /// </summary>
        public void StageRunScene(int seed, int dust, Vector2 viewport, Vector2 velocityPx, int unlockTier, double massShare)
        {
            if (root.Phase != GamePhase.Run || root.Run == null) return;
            while (root.Meta.unlockedMaxTier < unlockTier && Shop.UnlockNextFree(root.nodeTable, root.Meta) > 0) { }
            root.Override = new StatsOverride { enabled = true, dustCap = dust, spawnRate = perfSpawnRate };
            var s = Stats.Compute(root.gameParams, root.nodeTable, root.Meta);
            s.dustCap = dust;
            var screen = new Vector2(viewport.x * Screen.width, viewport.y * Screen.height);
            var run = root.Run;
            root.InputProvider = () => new PointerState { screenPos = screen + velocityPx * (float)run.elapsed, pressed = true };
            root.Dust.Reseed(seed);
            root.Dust.Reset(dust, root.CameraArea(), root.ToWorld(screen), s);
            var lo = root.celestialTable.Get(Mathf.Max(1, root.Meta.unlockedMaxTier - 1));
            var hi = root.celestialTable.Get(root.Meta.unlockedMaxTier);
            double thLo = Stats.Threshold(lo, s), thHi = Stats.Threshold(hi, s);
            root.Run.mass = thLo + (thHi - thLo) * massShare;
            root.Run.stamina = s.staminaMax;
            root.Run.elapsed = 0;
        }

        // ---------------- still-hold capture measurement (20 §3) ----------------

        string measureLabel;
        double measureUntil = -1;
        int measureFrom;
        double measureStart;

        /// <summary>
        /// 20 §3 acceptance: pins the pointer to the middle of the screen, holds, and after <paramref name="runSeconds"/>
        /// of run time logs how much dust was captured per second. The dust is reseeded and refilled to
        /// <paramref name="dust"/> (0 = the run's starting dust) so two measurements start from the same field, and the
        /// stamina is topped up so the run cannot end mid-measurement. Run screen only; the pointer goes back to the mouse
        /// afterwards.
        /// </summary>
        public void BeginStillHoldMeasure(string label, float runSeconds, int seed, int dust = 0)
        {
            if (root.Phase != GamePhase.Run || root.Run == null)
            {
                Debug.LogWarning("[Measure] still hold needs a run in progress");
                return;
            }
            var screen = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var s = root.Effective;
            root.InputProvider = () => new PointerState { screenPos = screen, pressed = true };
            root.Dust.Reseed(seed);
            root.Dust.Reset(dust > 0 ? dust : (int)s.dustInitial, root.CameraArea(), root.ToWorld(screen), s);
            root.Run.stamina = Math.Max(s.staminaMax, (s.staminaHoldDrain + s.staminaIdleDrain) * runSeconds * 1.2);
            measureLabel = label;
            measureFrom = root.Dust.CapturedCount;
            measureStart = root.Run.elapsed;
            measureUntil = root.Run.elapsed + runSeconds;
        }

        /// <summary>Dust within <paramref name="radiusPx"/> of the gravity source right now (20 §3 diagnostics).</summary>
        public int DustWithin(double radiusPx)
        {
            float r = (float)(radiusPx / root.gameParams.pixelsPerUnit);
            var g = root.CursorWorld;
            var items = root.Dust.Items;
            int n = 0;
            for (int i = 0; i < root.Dust.Count; i++)
                if ((items[i].pos - g).sqrMagnitude <= r * r) n++;
            return n;
        }

        /// <summary>Result of the last finished measurement: "captured/second", or null while one is running.</summary>
        public string MeasureResult { get; private set; }

        void TickMeasure()
        {
            if (measureUntil < 0) return;
            if (root.Phase != GamePhase.Run || root.Run == null)
            {
                measureUntil = -1;
                return;
            }
            if (root.Run.elapsed < measureUntil) return;
            double secs = root.Run.elapsed - measureStart;
            int captured = root.Dust.CapturedCount - measureFrom;
            MeasureResult = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}: {1} dust in {2:F1}s = {3:F2}/s (field {4}, inside the radius {5})",
                measureLabel, captured, secs, captured / System.Math.Max(secs, 1e-6), root.Dust.Count, DustWithin(root.Effective.gravityRadius));
            measureUntil = -1;
            EndStaging();
            Debug.Log("[Measure] " + MeasureResult);
        }

        readonly System.Collections.Generic.List<DelayedAction> delayed = new System.Collections.Generic.List<DelayedAction>();

        struct DelayedAction
        {
            public float at;
            public System.Action action;
        }

        /// <summary>
        /// Runs <paramref name="action"/> after <paramref name="seconds"/> of unscaled frame time. A tool call stalls the editor
        /// and the next frame's delta time jumps, so an effect started from the call itself cannot be captured at a precise
        /// moment; start it a second later with this instead.
        /// </summary>
        public void After(float seconds, System.Action action)
        {
            delayed.Add(new DelayedAction { at = Time.unscaledTime + seconds, action = action });
        }

        /// <summary>Undoes <see cref="StageRunScene"/>: mouse input and the normal stats again.</summary>
        public void EndStaging()
        {
            root.InputProvider = root.Bot != null && root.Bot.Enabled ? root.InputProvider : null;
            SetPerfMode(perfMode);
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
            TickMeasure();
            for (int i = delayed.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < delayed[i].at) continue;
                var a = delayed[i].action;
                delayed.RemoveAt(i);
                a?.Invoke();
            }
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
                if (kb.f10Key.wasPressedThisFrame) CycleStaminaPreset();
                if (kb.f11Key.wasPressedThisFrame) CycleEffectIntensity();
                if (kb.f12Key.wasPressedThisFrame) Screenshot();
                if (kb.endKey.wasPressedThisFrame) root.DebugReplayEnding();
                if (kb.homeKey.wasPressedThisFrame) root.DebugDrainStamina();
            }
#endif

            root.Hud.SetDebug(overlay
                ? string.Format(UIStrings.DebugOverlay, fps, root.Dust.Count, root.LastTickMs, root.MaxTickMs,
                    perfMode ? UIStrings.On : UIStrings.Off, BotEnabled ? UIStrings.On : UIStrings.Off, root.EffectIntensity)
                : string.Empty);
        }

        /// <summary>
        /// F10: the next stamina drain preset (18 §A1). A run in progress feels it on its next tick, because the effective
        /// stats are recomputed every tick. Runtime only: nothing is written to the asset or the save.
        /// </summary>
        public void CycleStaminaPreset()
        {
            var p = root.gameParams;
            double hold = root.StaminaDrain.enabled ? root.StaminaDrain.holdDrain : p.staminaHoldDrain;
            double idle = root.StaminaDrain.enabled ? root.StaminaDrain.idleDrain : p.staminaIdleDrain;
            var next = p.NextStaminaPreset(hold, idle);
            if (next == null) return;
            root.StaminaDrain = new StaminaOverride { enabled = true, holdDrain = next.holdDrain, idleDrain = next.idleDrain };
            root.Hud.ShowToast(string.Format(UIStrings.StaminaPreset, UIStrings.StaminaPresetName(next.id), next.holdDrain, next.idleDrain));
            Debug.Log($"[Debug] stamina preset: {next.id} hold={next.holdDrain} idle={next.idleDrain}");
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
