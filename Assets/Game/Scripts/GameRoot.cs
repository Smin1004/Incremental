using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Incremental
{
    public enum GamePhase { Run, Result }

    public struct PointerState
    {
        public Vector2 screenPos;
        public bool pressed;
    }

    /// <summary>Runtime override used by the debug tools (F2 performance mode). Assets are untouched.</summary>
    public struct StatsOverride
    {
        public bool enabled;
        public double dustCap;
        public double spawnRate;
    }

    /// <summary>
    /// Owns the two state objects and the fixed-tick game logic. Builds every other runtime object from code in Awake,
    /// so the scene only needs a camera, a light and this component with its asset references.
    /// Game logic runs in FixedUpdate only; Update / LateUpdate is rendering and input-edge handling.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        [Header("Data")]
        public GameParams gameParams;
        public CelestialTable celestialTable;
        public NodeTable nodeTable;

        [Header("Render")]
        public Material unlitMaterial;
        public Sprite circleSprite;
        public int planetPoolSize = 8;

        public MetaState Meta { get; private set; } = new MetaState();
        public Settings Settings { get; private set; } = new Settings();
        public SaveStore Store { get; private set; }
        /// <summary>The run is frozen while the window is unfocused (settings.pauseOnFocusLoss), except when the bot plays.</summary>
        public bool Paused => Phase == GamePhase.Run && !hasFocus && Settings.pauseOnFocusLoss && !(Bot != null && Bot.Enabled);
        public RunState Run { get; private set; }
        public EffectiveStats Effective { get; private set; }
        public GamePhase Phase { get; private set; } = GamePhase.Result;
        public Camera Cam { get; private set; }
        public DustField Dust { get; private set; }
        public DustMeshRenderer DustRenderer { get; private set; }
        public PlanetPool Planets { get; private set; }
        public HudView Hud { get; private set; }
        public CursorView Cursor { get; private set; }
        public ResultShopView ShopView { get; private set; }
        public DebugTools Tools { get; private set; }
        public AutoplayBot Bot { get; private set; }
        public Vector2 CursorWorld { get; private set; }
        public PointerState LastPointer { get; private set; }
        public double LastTickMs { get; private set; }
        public double MaxTickMs { get; private set; }
        public StatsOverride Override;
        /// <summary>Replace to drive the game without a mouse (autoplay bot).</summary>
        public Func<PointerState> InputProvider;
        public event Action<RunRecord> RunEnded;
        public event Action RunStarted;
        /// <summary>Raised on the fixed tick right after a planet is created and sold.</summary>
        public event Action<CelestialTier> PlanetCreated;
        /// <summary>persistentDataPath, captured on the main thread in Awake (run_log.csv, screenshots).</summary>
        public string DataDir { get; private set; }

        readonly Stopwatch tickWatch = new Stopwatch();
        List<UpgradeLevel> runStartLevels = new List<UpgradeLevel>();
        EffectiveStats runStartStats;
        bool endingLoggedThisRun;
        bool pendingStart;
        bool hasFocus = true;
        bool shownPaused;
        double pendingSaveTimer;
        double maxTickWindow;
        double maxTickAcc;

        void Awake()
        {
            // The Unity 6 editor honours this while the editor is not the active application; without it the player
            // loop stalls as soon as another window is in front, which breaks unattended bot runs.
            Application.runInBackground = true;
            DataDir = Application.persistentDataPath;
            Store = new SaveStore(DataDir);
            Settings = Store.LoadSettings();
            Fmt.Mode = Settings.notation;
            hasFocus = Application.isFocused;

            Cam = Camera.main;
            if (Cam == null) Cam = FindFirstObjectByType<Camera>();

            var dustGo = new GameObject("DustField", typeof(MeshFilter), typeof(MeshRenderer));
            dustGo.transform.SetParent(transform, false);
            Dust = dustGo.AddComponent<DustField>();
            Dust.Init(gameParams);
            DustRenderer = dustGo.AddComponent<DustMeshRenderer>();
            DustRenderer.Init(Dust, gameParams, unlitMaterial);

            var poolGo = new GameObject("PlanetPool");
            poolGo.transform.SetParent(transform, false);
            Planets = poolGo.AddComponent<PlanetPool>();
            Planets.Init(gameParams, circleSprite, unlitMaterial, planetPoolSize);

            var hudGo = new GameObject("HudView");
            hudGo.transform.SetParent(transform, false);
            Hud = hudGo.AddComponent<HudView>();
            Hud.Build();
            Hud.SetCountLifetime(gameParams.planetLifetimeSec);
            UIBuilder.EnsureEventSystem(transform);

            var cursorGo = new GameObject("CursorView");
            cursorGo.transform.SetParent(transform, false);
            Cursor = cursorGo.AddComponent<CursorView>();
            Cursor.Init(this, unlitMaterial, Hud.CanvasRect, circleSprite);

            var shopGo = new GameObject("ResultShopView");
            shopGo.transform.SetParent(transform, false);
            ShopView = shopGo.AddComponent<ResultShopView>();
            ShopView.Init(this);

            var toolsGo = new GameObject("DebugTools");
            toolsGo.transform.SetParent(transform, false);
            Tools = toolsGo.AddComponent<DebugTools>();
            Tools.Init(this);

            var botGo = new GameObject("AutoplayBot");
            botGo.transform.SetParent(transform, false);
            Bot = botGo.AddComponent<AutoplayBot>();
            Bot.Init(this);
        }

        /// <summary>
        /// No save: run 1 starts right away. With a save: a run left pending by a killed game is closed as a crash,
        /// then the game opens on the result / shop screen showing the last run.
        /// </summary>
        void Start()
        {
            var data = Store.Load(out var status);
            if (data == null)
            {
                Meta = new MetaState();
                StartRun();
                return;
            }

            Meta = data.meta;
            Shop.RecomputeUnlockedMaxTier(nodeTable, Meta);
            var crashed = Session.FinalizePending(data, EndReason.Crash);
            if (crashed != null) LogRun(crashed);
            if (crashed != null || status == LoadStatus.RestoredFromBackup) Save();
            Phase = GamePhase.Result;
            ShowIdleField();
            ShopView.Show(Meta.LastRun);
            UnityEngine.Debug.Log($"[Save] loaded ({status}): run {Meta.runCount}, currency {Fmt.Num(Meta.currency)}, max tier {Meta.unlockedMaxTier}");
        }

        /// <summary>
        /// Opening on the shop from a save: the HUD and dust field look like a finished run (stamina 0, last run income,
        /// the run-start dust as a still background), as they do after a run ends.
        /// </summary>
        void ShowIdleField()
        {
            Effective = ComputeStats();
            var last = Meta.LastRun;
            Hud.SetIncome(last != null ? last.income : 0);
            Hud.SetStamina(0, Effective.staminaMax);
            var ps = ReadPointer();
            LastPointer = ps;
            CursorWorld = ToWorld(ps.screenPos);
            Dust.Reset((int)Effective.dustInitial, CameraArea(), CursorWorld, Effective);
        }

        /// <summary>
        /// Quitting mid-run ends the run without the result screen (income kept). Held mass is released first, as when
        /// stamina runs out (12 §10-4). Between runs it just saves.
        /// </summary>
        void OnApplicationQuit()
        {
            if (Phase == GamePhase.Run && Run != null)
            {
                if (Run.holding)
                {
                    Release();
                    Run.holding = false;
                }
                EndRun(EndReason.Quit, false);
            }
            else Save();
        }

        void OnApplicationFocus(bool focus)
        {
            hasFocus = focus;
        }

        void FixedUpdate()
        {
            tickWatch.Restart();
            if (pendingStart)
            {
                pendingStart = false;
                StartRun();
            }
            if (Phase == GamePhase.Run && !Paused) TickRun(Time.fixedDeltaTime);
            tickWatch.Stop();

            LastTickMs = tickWatch.Elapsed.TotalMilliseconds;
            maxTickAcc = Math.Max(maxTickAcc, LastTickMs);
            maxTickWindow += Time.fixedDeltaTime;
            if (maxTickWindow >= 1.0)
            {
                MaxTickMs = maxTickAcc;
                maxTickAcc = 0;
                maxTickWindow = 0;
            }
        }

        void Update()
        {
            if (Phase == GamePhase.Result)
            {
                var kb = Keyboard.current;
                if (kb != null && kb.spaceKey.wasPressedThisFrame) RequestStartRun();
            }

            Hud.SetCurrency(Meta.currency, Meta.runCount);
            if (Run != null)
            {
                Hud.SetIncome(Run.runIncome);
                Hud.SetStamina(Run.stamina, Effective.staminaMax);
            }
            bool paused = Paused;
            if (paused != shownPaused)
            {
                shownPaused = paused;
                Hud.SetCenter(paused ? UIStrings.Paused : string.Empty);
            }
        }

        // ---------------- run lifecycle ----------------

        /// <summary>Starts the next run on the next fixed tick (safe to call from UI / Update).</summary>
        public void RequestStartRun()
        {
            if (Phase == GamePhase.Result) pendingStart = true;
        }

        public void StartRun()
        {
            Meta.runCount++;
            runStartLevels = Meta.CopyLevels();
            endingLoggedThisRun = false;
            Effective = ComputeStats();
            runStartStats = Effective;
            Run = new RunState(celestialTable.MaxTier) { stamina = Effective.staminaMax };

            var ps = ReadPointer();
            LastPointer = ps;
            CursorWorld = ToWorld(ps.screenPos);
            Dust.Reset((int)Effective.dustInitial, CameraArea(), CursorWorld, Effective);
            Planets.Clear();
            Phase = GamePhase.Run;
            pendingSaveTimer = 0;
            Save();
            RunStarted?.Invoke();
        }

        void TickRun(double dt)
        {
            Effective = ComputeStats();
            var s = Effective;

            var ps = ReadPointer();
            LastPointer = ps;
            CursorWorld = ToWorld(ps.screenPos);

            bool wasHolding = Run.holding;
            bool holding = ps.pressed;
            Run.holding = holding;
            Run.elapsed += dt;
            Run.stamina -= s.staminaIdleDrain * dt;
            if (holding) Run.stamina -= s.staminaHoldDrain * dt;

            int captured = Dust.Tick(dt, CameraArea(), CursorWorld, holding, s);
            if (captured > 0) Run.mass += captured * s.dustMass;

            // Reaching the highest unlocked tier creates immediately, without releasing.
            if (holding)
            {
                var top = celestialTable.Get(Meta.unlockedMaxTier);
                if (top != null && Run.mass >= Stats.Threshold(top, s)) CreatePlanet(top);
            }

            if (wasHolding && !holding) Release();

            if (Run.stamina <= 0)
            {
                Run.stamina = 0;
                if (Run.holding)
                {
                    Release();
                    Run.holding = false;
                }
                EndRun(EndReason.Stamina, true);
                return;
            }

            pendingSaveTimer += dt;
            if (pendingSaveTimer >= gameParams.pendingSaveIntervalSec)
            {
                pendingSaveTimer = 0;
                Save();
            }
        }

        /// <summary>Release: create the highest reached tier (excess mass is lost below the top tier), or scatter the dust below tier 1.</summary>
        void Release()
        {
            var tier = HighestReachedTier();
            if (tier != null)
            {
                CreatePlanet(tier);
                return;
            }
            double dustMass = Effective.dustMass;
            int n = dustMass > 0 ? (int)Math.Round(Run.mass / dustMass) : 0;
            if (n > 0) Dust.Scatter(n, CursorWorld);
            Run.mass = 0;
        }

        /// <summary>
        /// Creates and sells planets of this tier from the current mass. At the highest unlocked tier the mass makes
        /// floor(mass / threshold) planets, so dust mass beyond one threshold still pays (12 §10-2); below it, one planet.
        /// Each planet pays ceil(sale price × sale multiplier), so currency stays an integer (12 §10-9).
        /// </summary>
        void CreatePlanet(CelestialTier tier)
        {
            double count = 1;
            if (tier.tier == Meta.unlockedMaxTier)
            {
                double th = Threshold(tier);
                if (th > 0) count = Math.Max(1.0, Math.Floor(Run.mass / th));
            }
            Run.runIncome += count * Stats.SaleIncome(tier, Effective);
            int idx = tier.tier - 1;
            if (idx >= 0 && idx < Run.planetCounts.Length)
                Run.planetCounts[idx] = (int)Math.Min((double)int.MaxValue, Run.planetCounts[idx] + count);
            Planets.Show(CursorWorld, tier.sizePx, tier.color);
            if (count > 1) Hud.ShowPlanetCount(Cam.WorldToScreenPoint(CursorWorld), tier.sizePx, count);
            Run.mass = 0;

            // The last tier ends the game in week 4; for now it only sells, and the log marks it once per run.
            if (tier.tier == celestialTable.MaxTier && !endingLoggedThisRun)
            {
                endingLoggedThisRun = true;
                UnityEngine.Debug.Log($"[Ending] tier {tier.tier} created (ending: week 4)");
            }
            PlanetCreated?.Invoke(tier);
        }

        /// <summary>
        /// Ends the run: income into currency, statistics and history (Session.ApplyRunEnd), save, run_log.csv.
        /// <paramref name="showResult"/> is false when quitting: no result screen and no bot shopping.
        /// </summary>
        void EndRun(string reason, bool showResult)
        {
            var rec = Session.MakeRecord(Meta, Meta.runCount, Run.elapsed, Run.runIncome, Run.planetCounts, runStartLevels, runStartStats, reason);
            Session.ApplyRunEnd(Meta, rec);
            Phase = GamePhase.Result;
            Save();
            LogRun(rec);
            if (showResult) RunEnded?.Invoke(rec);
        }

        void LogRun(RunRecord rec)
        {
            RunLogger.Append(DataDir, rec, celestialTable);
            UnityEngine.Debug.Log(RunLogger.Summary(rec));
        }

        // ---------------- save ----------------

        /// <summary>Writes save.json. During a run the run in flight goes into pending.</summary>
        public void Save()
        {
            var data = new SaveData { meta = Meta };
            if (Phase == GamePhase.Run && Run != null)
                data.pending = Session.MakePending(Meta.runCount, Run.elapsed, Run.runIncome, Run.planetCounts, runStartLevels, runStartStats);
            try
            {
                Store.Write(data);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[Save] write failed: " + e.Message);
            }
        }

        /// <summary>Writes settings.json with the current notation (F7).</summary>
        public void SaveSettings()
        {
            Settings.notation = Fmt.Mode;
            try
            {
                Store.WriteSettings(Settings);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[Save] settings write failed: " + e.Message);
            }
        }

        /// <summary>Shift+F9: delete the save (settings stay) and start again from run 1. The current run is dropped unrecorded.</summary>
        public void ResetProgress()
        {
            Store.DeleteSave();
            Meta = new MetaState();
            Run = null;
            Phase = GamePhase.Result;
            pendingStart = true;
            UnityEngine.Debug.Log("[Save] save deleted; starting again from run 1");
        }

        // ---------------- shop (between runs only) ----------------

        /// <summary>Buys one level of a node (stat or gate). Between runs only; saves after every purchase.</summary>
        public bool TryBuy(string nodeId)
        {
            if (Phase != GamePhase.Result || !Shop.Buy(nodeTable, Meta, nodeId)) return false;
            Save();
            return true;
        }

        // ---------------- debug (DebugTools) ----------------

        /// <summary>F3: currency = max(currency × mult, min).</summary>
        public void DebugBoostCurrency(double mult, double min) => Meta.currency = Math.Max(Meta.currency * mult, min);

        /// <summary>F6: raise the next gate node to level 1 for free (also mid-run). Returns the unlocked tier, or 0.</summary>
        public int DebugUnlockNext()
        {
            int tier = Shop.UnlockNextFree(nodeTable, Meta);
            UnityEngine.Debug.Log(tier > 0 ? $"[Debug] tier {tier} unlocked for free" : "[Debug] every tier is already unlocked");
            if (tier > 0) Save();
            return tier;
        }

        // ---------------- queries (views, bot) ----------------

        /// <summary>Highest unlocked tier whose threshold the current mass has reached, or null.</summary>
        public CelestialTier HighestReachedTier()
        {
            CelestialTier best = null;
            for (int t = 1; t <= Meta.unlockedMaxTier; t++)
            {
                var def = celestialTable.Get(t);
                if (def != null && Run.mass >= Threshold(def)) best = def;
            }
            return best;
        }

        /// <summary>Lowest unlocked tier not yet reached; the top unlocked tier if all are reached.</summary>
        public CelestialTier TargetTier()
        {
            for (int t = 1; t <= Meta.unlockedMaxTier; t++)
            {
                var def = celestialTable.Get(t);
                if (def != null && Threshold(def) > Run.mass) return def;
            }
            return celestialTable.Get(Meta.unlockedMaxTier);
        }

        public double Threshold(CelestialTier tier) => Stats.Threshold(tier, Effective);

        /// <summary>0..1 fill of the cursor gauge: mass over the target tier's threshold.</summary>
        public double GaugeFill()
        {
            if (Run == null) return 0;
            var t = TargetTier();
            if (t == null) return 0;
            double th = Threshold(t);
            return th > 0 ? Math.Clamp(Run.mass / th, 0.0, 1.0) : 1.0;
        }

        public PointerState ReadPointer()
        {
            if (InputProvider != null) return InputProvider();
            var m = Mouse.current;
            if (m == null) return LastPointer;
            return new PointerState { screenPos = m.position.ReadValue(), pressed = m.leftButton.isPressed };
        }

        public Vector2 ToWorld(Vector2 screen)
        {
            var w = Cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
            return new Vector2(w.x, w.y);
        }

        /// <summary>Visible camera rect in world units, computed at runtime.</summary>
        public Rect CameraArea()
        {
            float h = Cam.orthographicSize;
            float w = h * Cam.aspect;
            var c = Cam.transform.position;
            return new Rect(c.x - w, c.y - h, w * 2f, h * 2f);
        }

        EffectiveStats ComputeStats()
        {
            var s = Stats.Compute(gameParams, nodeTable, Meta);
            if (Override.enabled)
            {
                s.dustCap = Override.dustCap;
                s.spawnRate = Override.spawnRate;
                s.dustInitial = Math.Min(s.dustInitial, s.dustCap);
            }
            return s;
        }
    }
}
