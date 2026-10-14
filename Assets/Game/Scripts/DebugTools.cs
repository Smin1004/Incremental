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
    /// Insert: the gravity source follow (22 §3), off → the effective speed × each GameParams.followDebugMults → off.
    /// Number row: force abilities on whatever the tree says (24 §7), 1..n = that ability alone, n + 1 = all, 0 = off.
    /// Minus / equals: the upgrade level of the forced abilities down / up. PageUp and PageDown cycle the same two
    /// things; the number row was added because those two never reached the game on the developer's keyboard. The
    /// state is shown in the corner of the screen; nothing is saved.
    /// U: the visual direction of the UI, A → B → C (26 §3). G: the stamina display, (a) the bar at the top ↔ (b) the
    /// gravity ring as the gauge (26 §5.1). Both work on every screen, are shown in the F1 overlay and are not saved.
    /// X: the overexposure measures on / off (26 §5.5), to compare before and after; shown in the overlay, not saved.
    /// T: the twist of the skill tree's spiral arms, through UITheme.tree.armTwistChoices (26 §10.1); not saved.
    /// With the F1 overlay on, every key the game receives is also written to the console, which is how to find out
    /// what a keyboard really sends.
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

        // ---------------- ring entry income measurement (20 §8.3) ----------------

        int measureRing;
        int measureRunsLeft;
        readonly System.Collections.Generic.List<double> measureIncomes = new System.Collections.Generic.List<double>();
        readonly System.Collections.Generic.List<double> measureDurations = new System.Collections.Generic.List<double>();
        bool measureHooked;

        /// <summary>"I_k = mean income" of the last finished ring measurement, or null while one is running.</summary>
        public string RingMeasureResult { get; private set; }

        /// <summary>Mean income of the last finished ring measurement (20 §8.3 step 1), or 0.</summary>
        public double RingMeasureIncome { get; private set; }

        /// <summary>
        /// 20 §8.3 step 1: puts the save into the state a player is in when ring <paramref name="ring"/> opens and has
        /// the bot play <paramref name="runs"/> runs from it, without letting it spend anything, so the runs measure the
        /// income of that ring's entry and nothing else. The mean is I_k, which every node cost of the ring is built from.
        /// </summary>
        public void BeginRingMeasure(int ring, int runs)
        {
            measureRing = ring;
            measureRunsLeft = Mathf.Max(1, runs);
            measureIncomes.Clear();
            measureDurations.Clear();
            RingMeasureResult = null;
            if (!measureHooked)
            {
                root.RunEnded += OnMeasureRunEnded;
                measureHooked = true;
            }
            ApplyRingStartState(ring);
            root.Bot.stopAfterRuns = 0;
            root.Bot.SetEnabled(false);
            root.Bot.SetEnabled(true);
        }

        /// <summary>
        /// The save as it looks the moment ring <paramref name="ring"/> opens: every gate up to it bought and every node
        /// of the rings below it at full level, with no currency. An approximation of a real run of play, but a
        /// reproducible one, so two measurements of the same ring can be compared.
        /// </summary>
        public void ApplyRingStartState(int ring)
        {
            var m = root.Meta;
            m.upgradeLevels.Clear();
            for (int tier = 2; tier <= ring; tier++)
            {
                var gate = root.nodeTable.Gate(tier);
                if (gate != null) m.SetLevel(gate.id, 1);
            }
            foreach (var n in root.nodeTable.nodes)
            {
                if (n == null || n.IsGate || !n.enabled || n.minTier >= ring) continue;
                m.SetLevel(n.id, n.maxLevel > 0 ? n.maxLevel : 1);
            }
            Shop.RecomputeUnlockedMaxTier(root.nodeTable, m);
            m.currency = 0;
        }

        void OnMeasureRunEnded(RunRecord rec)
        {
            if (measureRunsLeft <= 0) return;
            measureIncomes.Add(rec.income);
            measureDurations.Add(rec.durationSec);
            measureRunsLeft--;
            // The bot has already shopped by now; putting the ring start state back undoes it, so every run of the
            // measurement starts from the same place.
            ApplyRingStartState(measureRing);
            if (measureRunsLeft > 0) return;
            root.Bot.SetEnabled(false);
            double income = 0, secs = 0;
            foreach (var v in measureIncomes) income += v;
            foreach (var v in measureDurations) secs += v;
            RingMeasureIncome = income / measureIncomes.Count;
            RingMeasureResult = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "ring {0}: I = {1:F0} over {2} runs ({3}), {4:F1}s each",
                measureRing, RingMeasureIncome, measureIncomes.Count, string.Join("/", measureIncomes.ConvertAll(v => v.ToString("F0"))),
                secs / measureDurations.Count);
            Debug.Log("[Measure] " + RingMeasureResult);
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
            var g = root.GravityWorld;
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
                if (kb.insertKey.wasPressedThisFrame) CycleFollow();
                if (kb.uKey.wasPressedThisFrame) CycleUiDirection();
                if (kb.gKey.wasPressedThisFrame) ToggleStaminaDisplay();
                if (kb.xKey.wasPressedThisFrame) ToggleExposureFix();
                if (kb.vKey.wasPressedThisFrame) ToggleDustLook();
                if (kb.tKey.wasPressedThisFrame && root.Tree != null) root.Tree.CycleTwist();
                if (kb.kKey.wasPressedThisFrame) CycleGravityRange();
                if (kb.lKey.wasPressedThisFrame) CycleStaminaScale();
                if (root.Abilities != null) AbilityKeys(kb);
                if (overlay) LogKeys(kb);
            }
#endif

            root.Hud.SetDebug(overlay
                ? string.Format(UIStrings.DebugOverlay, fps, root.Dust.Count, root.LastTickMs, root.MaxTickMs,
                    perfMode ? UIStrings.On : UIStrings.Off, BotEnabled ? UIStrings.On : UIStrings.Off, root.EffectIntensity,
                    root.Dust.OutsideCount, root.Ui.Direction != null ? root.Ui.Direction.id : string.Empty,
                    root.Ui.StaminaMode == 1 ? UIStrings.StaminaDisplayRingShort : UIStrings.StaminaDisplayBarShort,
                    root.ExposureFix ? UIStrings.On : UIStrings.Off, root.Dust.SpawnBoost)
                : string.Empty);
            root.Hud.SetAbilityDebug(root.Abilities != null ? root.Abilities.DebugLabel(overlay) : string.Empty);
        }

        /// <summary>The number row, in the order of the digits: index = the digit.</summary>
        static readonly Key[] DigitKeys =
        {
            Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
        };

        /// <summary>
        /// 24 §7: the ability debug keys. A digit says outright what is forced (0 nothing, 1..n that ability alone,
        /// n + 1 all), minus and equals move the level of the forced ones, and PageUp / PageDown cycle the same two
        /// things. The digits are on every keyboard; PageUp and PageDown sit on a function layer on some.
        /// </summary>
        void AbilityKeys(Keyboard kb)
        {
            var abilities = root.Abilities;
            for (int digit = 0; digit < DigitKeys.Length; digit++)
                if (kb[DigitKeys[digit]].wasPressedThisFrame) abilities.SetForced(digit);
            if (kb.minusKey.wasPressedThisFrame) abilities.StepForcedLevel(-1);
            if (kb.equalsKey.wasPressedThisFrame) abilities.StepForcedLevel(1);
            if (kb.pageUpKey.wasPressedThisFrame) abilities.CycleForced();
            if (kb.pageDownKey.wasPressedThisFrame) abilities.CycleForcedLevel();
        }

        /// <summary>
        /// With the overlay on, every key press the game receives is written to the console. A debug key that does
        /// nothing is either not reaching the game or arriving as another key, and this shows which.
        /// </summary>
        void LogKeys(Keyboard kb)
        {
            var keys = kb.allKeys;
            for (int i = 0; i < keys.Count; i++)
                if (keys[i] != null && keys[i].wasPressedThisFrame) Debug.Log("[Debug] key pressed: " + keys[i].keyCode);
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

        /// <summary>
        /// K (24 §23.2): the next gravity range preset (radius node scale, crystal radius x at unlock and per level). The
        /// running run feels it on its next tick. Runtime only: the asset and the save are untouched. The toast shows the
        /// gravity ring's diameter at ring 10 (every radius node to ring 10 bought) as a share of the screen height, plain
        /// and with the crystal at Lv3.
        /// </summary>
        public void CycleGravityRange()
        {
            var p = root.gameParams;
            var presets = p.gravityRangePresets;
            if (presets == null || presets.Length == 0) return;
            var next = presets[(p.GravityRangePresetIndex(root.GrowthScaleNow) + 1) % presets.Length];
            root.GravityRange = new GravityRangeOverride
            {
                set = true, id = next.id, growthScale = next.growthScale,
                crystalRadiusMult = next.crystalRadiusMult, crystalRadiusPerPower = next.crystalRadiusPerPower,
            };
            double plain = RingShare(10, next.growthScale, 1.0);
            double lv3 = CrystalLv3Mult(next.crystalRadiusMult, next.crystalRadiusPerPower);
            double buffed = RingShare(10, next.growthScale, lv3);
            double resonance = RingShare(10, next.growthScale, 1.0 + (lv3 - 1.0) * 2);
            root.Hud.ShowToast(string.Format(UIStrings.GravityRangeToast, UIStrings.Get(UIStrings.GravityPresetKey(next.id)),
                next.growthScale, next.crystalRadiusMult, next.crystalRadiusPerPower * 0.1, plain * 100, buffed * 100));
            Debug.Log($"[Debug] gravity range: {next.id} growth x{next.growthScale} crystal x{next.crystalRadiusMult} +{next.crystalRadiusPerPower}/power; " +
                $"ring 1 {RingShare(1, next.growthScale, 1.0):P1}, ring 10 {plain:P1}, crystal Lv3 x{lv3:0.00} {buffed:P1}, resonance 2 stacks {resonance:P1} of the screen height");
        }

        /// <summary>
        /// The gravity ring's diameter at <paramref name="ring"/>, every gravity_radius node up to that ring bought, x
        /// <paramref name="mult"/>, as a share of the screen height that ring's world scale shows.
        /// </summary>
        public double RingShare(int ring, double growthScale, double mult)
        {
            var p = root.gameParams;
            double nodes = 0;
            foreach (var n in root.nodeTable.nodes)
                if (n != null && n.enabled && n.statId == StatIds.GravityRadius && n.minTier <= ring) nodes += n.effectPerLevel * n.maxLevel;
            double radiusPx = p.gravityRadius * (1.0 + nodes * growthScale) * mult;
            return root.RingShareOfScreen(radiusPx, Stats.WorldScale(p, ring));
        }

        /// <summary>The crystal's radius multiplier with every ability_power node of it bought (Lv3 in the seed).</summary>
        double CrystalLv3Mult(double atUnlock, double perPower)
        {
            double power = 0;
            foreach (var n in root.nodeTable.nodes)
                if (n != null && n.enabled && n.statId == StatIds.AbilityPower && n.abilityId == "crystal") power += n.effectPerLevel * n.maxLevel;
            return CrystalDef.RadiusMult(new AbilityLevel { power = power }, atUnlock, perPower);
        }

        /// <summary>
        /// L (24 §23.3): the next stamina_max node scale. A run keeps the scale it started with, so it shows from the next
        /// run. The toast says how long the next run lasts held throughout, with the save's nodes. Runtime only.
        /// </summary>
        public void CycleStaminaScale()
        {
            var p = root.gameParams;
            var steps = p.staminaNodeScaleSteps;
            if (steps == null || steps.Length == 0) return;
            double now = root.StaminaScaleNext;
            int at = -1;
            for (int i = 0; i < steps.Length; i++) if (Math.Abs(steps[i] - now) < 1e-9) { at = i; break; }
            double next = steps[(at + 1) % steps.Length];
            root.StaminaScaleDebug = next;
            double hold = root.StaminaDrain.enabled ? root.StaminaDrain.holdDrain : p.staminaHoldDrain;
            double idle = root.StaminaDrain.enabled ? root.StaminaDrain.idleDrain : p.staminaIdleDrain;
            var s = Stats.Compute(p, root.nodeTable, root.Meta, hold, idle, root.GrowthScaleNow, next);
            double longest = s.staminaMax / Math.Max(1e-9, s.staminaHoldDrain + s.staminaIdleDrain);
            root.Hud.ShowToast(string.Format(UIStrings.StaminaScaleToast, next, s.staminaMax, longest));
            Debug.Log($"[Debug] stamina node scale: x{next} from the next run; stamina {s.staminaMax:0.#}, drain {s.staminaHoldDrain:0.###} + {s.staminaIdleDrain:0.###}/s, " +
                $"a run held throughout {longest:0} s");
        }

        /// <summary>
        /// U (26 §3): the next visual direction of the UI, A → B → C → A. Every screen redraws in it at once. Runtime
        /// only: the direction a fresh launch starts in is UITheme.direction.
        /// </summary>
        public void CycleUiDirection()
        {
            root.Ui.SetDirection(root.Ui.DirectionIndex + 1);
            string name = UIStrings.UiDirectionName(root.Ui.Direction.id);
            root.Hud.ShowToast(string.Format(UIStrings.UiDirectionToast, name));
            Debug.Log("[UI] direction: " + name);
        }

        /// <summary>
        /// G (26 §5.1): the stamina display, (a) the bar at the top ↔ (b) the gravity ring as the gauge. Runtime only:
        /// a fresh launch starts with UITheme.staminaMode.
        /// </summary>
        public void ToggleStaminaDisplay()
        {
            root.Ui.SetStaminaMode(root.Ui.StaminaMode == 1 ? 0 : 1);
            string name = root.Ui.StaminaMode == 1 ? UIStrings.StaminaDisplayRing : UIStrings.StaminaDisplayBar;
            root.Hud.ShowToast(string.Format(UIStrings.StaminaDisplayToast, name));
            Debug.Log("[UI] stamina display: " + name);
        }

        /// <summary>
        /// X (26 §5.5): the overexposure measures on or off, in effect on the next frame, so before and after can be
        /// compared on the same scene. Runtime only: a fresh launch starts with GameParams.exposureFix.
        /// </summary>
        public void ToggleExposureFix()
        {
            root.ExposureFix = !root.ExposureFix;
            string state = root.ExposureFix ? UIStrings.On : UIStrings.Off;
            root.Hud.ShowToast(string.Format(UIStrings.ExposureFixToast, state));
            Debug.Log("[Debug] overexposure measures: " + state);
        }

        /// <summary>
        /// V (26 §11): the dust's head and tail look ↔ the look before it, on the next frame, for comparing on the same scene.
        /// Runtime only: a fresh launch starts with GameParams.dustLookNew.
        /// </summary>
        public void ToggleDustLook()
        {
            root.DustLookNew = !root.DustLookNew;
            var p = root.gameParams;
            string name = root.DustLookNew ? UIStrings.DustLookNew : UIStrings.DustLookOld;
            root.Hud.ShowToast(string.Format(UIStrings.DustLookToast, name));
            Debug.Log(root.DustLookNew
                ? "[Dust] look: ribbon cleared out of its head over " + p.trailHeadClearShare.ToString("0.##") + " of the dust's half-size"
                : "[Dust] look: as before (the ribbon's bright line runs across the head)");
        }

        /// <summary>
        /// Index into GameParams.followDebugMults the Insert key is on, or −1 for "off" (22 §3). The cycle is relative to
        /// the effective speed, which the follow nodes raise during a playthrough, so it is kept as an index.
        /// </summary>
        int followStep = -1;

        /// <summary>
        /// Insert (22 §3): off → the effective speed → × the next multiple → … → off. The running hold feels it at once,
        /// because the gravity source is moved on the fixed tick. Runtime only: neither the asset nor the save is touched.
        /// </summary>
        public void CycleFollow()
        {
            var mults = root.gameParams.followDebugMults;
            if (mults == null || mults.Length == 0) return;
            followStep = root.FollowEnabled ? followStep + 1 : 0;
            if (followStep >= mults.Length || followStep < 0)
            {
                followStep = -1;
                root.Follow = new FollowOverride { set = true, enabled = false, speedPx = root.Effective.followSpeedPx };
                root.Hud.ShowToast(UIStrings.FollowOff);
                Debug.Log("[Debug] gravity source follow: off");
                return;
            }
            double speed = root.Effective.followSpeedPx * mults[followStep];
            root.Follow = new FollowOverride { set = true, enabled = true, speedPx = speed };
            root.Hud.ShowToast(string.Format(UIStrings.FollowOn, speed));
            Debug.Log("[Debug] gravity source follow: " + speed + " px/s (x" + mults[followStep] + ")");
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
