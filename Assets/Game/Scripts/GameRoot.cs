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
    /// Runtime stamina drain used by the debug tools (F10 preset cycle, 18 §A1). The two values replace the GameParams
    /// bases; node reductions still apply. Nothing is saved, and it takes effect on the next tick of the running run.
    /// </summary>
    public struct StaminaOverride
    {
        public bool enabled;
        public double holdDrain;
        public double idleDrain;
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
        [Tooltip("Incremental/Planet: every planet, star and forming body (14 §3).")]
        public Material planetMaterial;
        [Tooltip("Incremental/Dust: glowing dust points (14 §5).")]
        public Material dustMaterial;
        [Tooltip("Incremental/Starfield: the shared procedural background (14 §6).")]
        public Material backgroundMaterial;
        [Tooltip("Incremental/FxRing: light rings of star ignition (14 §6).")]
        public Material fxMaterial;
        [Tooltip("Incremental/Flash: radial flashes (15 §3 flash rule).")]
        public Material flashMaterial;
        public Sprite circleSprite;
        [Tooltip("Bodies on screen at once. A hold can drop several fragments on top of the body it makes (20 §5), so this " +
                 "is above the 8 that the bodies alone needed.")]
        public int planetPoolSize = 16;

        public MetaState Meta { get; private set; } = new MetaState();
        public Settings Settings { get; private set; } = new Settings();
        public SaveStore Store { get; private set; }
        /// <summary>The run is frozen while the window is unfocused (settings.pauseOnFocusLoss), except when the bot plays.</summary>
        public bool Paused => Phase == GamePhase.Run &&
                              ((!hasFocus && Settings.pauseOnFocusLoss && !(Bot != null && Bot.Enabled)) || (Gallery != null && Gallery.IsOpen));
        public RunState Run { get; private set; }
        public EffectiveStats Effective { get; private set; }
        public GamePhase Phase { get; private set; } = GamePhase.Result;
        public Camera Cam { get; private set; }
        public BackdropView Backdrop { get; private set; }
        public IgnitionFx Ignition { get; private set; }
        public DustField Dust { get; private set; }
        public DustMeshRenderer DustRenderer { get; private set; }
        public PlanetPool Planets { get; private set; }
        public HudView Hud { get; private set; }
        public CursorView Cursor { get; private set; }
        public ResultView Result { get; private set; }
        public SolarSystemView Solar { get; private set; }
        public TreeView Tree { get; private set; }
        public DebugTools Tools { get; private set; }
        public GalleryView Gallery { get; private set; }
        public SupernovaRunFx SupernovaRun { get; private set; }
        public EndingView Ending { get; private set; }
        public AutoplayBot Bot { get; private set; }
        public Vector2 CursorWorld { get; private set; }
        public PointerState LastPointer { get; private set; }
        public double LastTickMs { get; private set; }
        public double MaxTickMs { get; private set; }
        public StatsOverride Override;
        /// <summary>Runtime stamina drain from the debug preset key (18 §A1); disabled = the GameParams values.</summary>
        public StaminaOverride StaminaDrain;
        /// <summary>
        /// 0..1, multiplies every flash, light bleed and pop (10 §8). Starts at GameParams.effectIntensity; the debug key
        /// cycles it at runtime (the options screen is week 7), so the asset is never modified.
        /// </summary>
        public float EffectIntensity { get; set; } = 1f;
        /// <summary>Replace to drive the game without a mouse (autoplay bot).</summary>
        public Func<PointerState> InputProvider;
        /// <summary>True from the first blue giant ever until the ending run ends (15 §4.2): input is ignored and the dust falls into the star.</summary>
        public bool EndingActive { get; private set; }
        /// <summary>Logic seconds since the ending started (fixed tick).</summary>
        public double EndingAge { get; private set; }
        /// <summary>World position of the blue giant that started the ending.</summary>
        public Vector2 EndingCenter { get; private set; }
        public event Action<RunRecord> RunEnded;
        public event Action RunStarted;
        /// <summary>Raised on the fixed tick right after a planet is created and sold.</summary>
        public event Action<CelestialTier> PlanetCreated;
        /// <summary>persistentDataPath, captured on the main thread in Awake (run_log.csv, screenshots).</summary>
        public string DataDir { get; private set; }

        readonly Stopwatch tickWatch = new Stopwatch();
        List<UpgradeLevel> runStartLevels = new List<UpgradeLevel>();
        EffectiveStats runStartStats;
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
            EffectIntensity = Mathf.Clamp01((float)gameParams.effectIntensity);
            PlanetVisual.ApplyGlobals(gameParams);

            Cam = Camera.main;
            if (Cam == null) Cam = FindFirstObjectByType<Camera>();

            var planetPoolGo = new GameObject("PlanetPool");
            planetPoolGo.transform.SetParent(transform, false);
            Planets = planetPoolGo.AddComponent<PlanetPool>();
            Planets.Init(this, planetPoolSize);

            var backdropGo = new GameObject("Backdrop", typeof(MeshFilter), typeof(MeshRenderer));
            backdropGo.transform.SetParent(transform, false);
            Backdrop = backdropGo.AddComponent<BackdropView>();
            Backdrop.Init(this, backgroundMaterial);

            var dustGo = new GameObject("DustField", typeof(MeshFilter), typeof(MeshRenderer));
            dustGo.transform.SetParent(transform, false);
            Dust = dustGo.AddComponent<DustField>();
            Dust.Init(gameParams);
            DustRenderer = dustGo.AddComponent<DustMeshRenderer>();
            DustRenderer.Init(this, Dust, dustMaterial);


            var hudGo = new GameObject("HudView");
            hudGo.transform.SetParent(transform, false);
            Hud = hudGo.AddComponent<HudView>();
            Hud.Build();
            Hud.SetCountLifetime(gameParams.planetPopDurationSec + gameParams.planetHoldSec + gameParams.planetDepartSec * 0.5);
            UIBuilder.EnsureEventSystem(transform);

            var cursorGo = new GameObject("CursorView");
            cursorGo.transform.SetParent(transform, false);
            Cursor = cursorGo.AddComponent<CursorView>();
            Cursor.Init(this, unlitMaterial, Hud.CanvasRect);

            var resultGo = new GameObject("ResultView");
            resultGo.transform.SetParent(transform, false);
            Result = resultGo.AddComponent<ResultView>();
            Result.Init(this);

            var solarGo = new GameObject("SolarSystem");
            solarGo.transform.SetParent(transform, false);
            Solar = solarGo.AddComponent<SolarSystemView>();
            Solar.Init(this);

            var treeGo = new GameObject("TreeView");
            treeGo.transform.SetParent(transform, false);
            Tree = treeGo.AddComponent<TreeView>();
            Tree.Init(this);

            var toolsGo = new GameObject("DebugTools");
            toolsGo.transform.SetParent(transform, false);
            Tools = toolsGo.AddComponent<DebugTools>();
            Tools.Init(this);

            var ignitionGo = new GameObject("Ignition");
            ignitionGo.transform.SetParent(transform, false);
            Ignition = ignitionGo.AddComponent<IgnitionFx>();
            Ignition.Init(this, fxMaterial);

            var supernovaGo = new GameObject("SupernovaRun");
            supernovaGo.transform.SetParent(transform, false);
            SupernovaRun = supernovaGo.AddComponent<SupernovaRunFx>();
            SupernovaRun.Init(this);

            var endingGo = new GameObject("Ending");
            endingGo.transform.SetParent(transform, false);
            Ending = endingGo.AddComponent<EndingView>();
            Ending.Init(this);

            var galleryGo = new GameObject("Gallery");
            galleryGo.transform.SetParent(transform, false);
            Gallery = galleryGo.AddComponent<GalleryView>();
            Gallery.Init(this);

            var botGo = new GameObject("AutoplayBot");
            botGo.transform.SetParent(transform, false);
            Bot = botGo.AddComponent<AutoplayBot>();
            Bot.Init(this);
        }

        /// <summary>
        /// No save: run 1 starts right away. With a save: a run left pending by a killed game is closed as a crash (or as
        /// the ending run when it died during its ending), then the game opens on the result / shop screen showing the last
        /// run, or replays the ending sequence when it was not seen to the end (15 §4.5).
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
            OpenLoaded(data, status == LoadStatus.RestoredFromBackup);
            UnityEngine.Debug.Log($"[Save] loaded ({status}): run {Meta.runCount}, currency {Fmt.Num(Meta.currency)}, max tier {Meta.unlockedMaxTier}" +
                                  (Meta.endingRun > 0 ? $", ending run {Meta.endingRun} (seen {Meta.endingSeen})" : string.Empty));
        }

        /// <summary>Takes a loaded save: closes a pending run, then shows the ending sequence if it is unseen, else the result screen.</summary>
        void OpenLoaded(SaveData data, bool saveNow)
        {
            Meta = data.meta;
            Run = null;
            EndingActive = false;
            Shop.RecomputeUnlockedMaxTier(nodeTable, Meta);
            var crashed = Session.FinalizePending(data, EndReason.Crash);
            if (crashed != null) LogRun(crashed);
            if (crashed != null || saveNow) Save();
            Phase = GamePhase.Result;
            ShowIdleField();
            Tree.Hide();
            if (EndingView.IsUnseen(Meta)) Ending.Play(Meta.EndingRecord, false);
            else Result.Show(Meta.LastRun, false);
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
                // Quitting during the ending closes the ending run, so the next launch plays the sequence (15 §4.5).
                if (EndingActive)
                {
                    EndRun(EndReason.Ending, false);
                    return;
                }
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
            // Between runs the result screen and the tree show their own numbers (Space is handled there too). The dust is
            // drawn during runs, and on the result screen only as the supernova remnant (15 §4.3).
            Hud.SetRunHudVisible(Phase == GamePhase.Run);
            DustRenderer.SetVisible(Phase == GamePhase.Run || Ending.ShowsDust);
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
            EndingActive = false;
            EndingAge = 0;
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
            if (EndingActive)
            {
                TickEnding(dt);
                return;
            }

            var ps = ReadPointer();
            LastPointer = ps;
            CursorWorld = ToWorld(ps.screenPos);

            bool wasHolding = Run.holding;
            bool holding = ps.pressed;
            Run.holding = holding;
            Run.elapsed += dt;
            Run.stamina -= s.staminaIdleDrain * dt;
            if (holding) Run.stamina -= s.staminaHoldDrain * dt;

            double captured = Dust.Tick(dt, CameraArea(), CursorWorld, holding, s);
            if (captured > 0) Run.mass += captured * s.dustMass;

            // 20 §5: the moment the held mass passes a tier threshold, that tier may drop a fragment. No mass is spent.
            if (holding && captured > 0) DropFragments(s);

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

        /// <summary>
        /// The ending on the run screen (15 §4.2): input is ignored, nothing spawns, stamina and the run clock stand still,
        /// every dust falls into the blue giant for endingPullSec, the screen darkens for endingFadeSec, then the run ends as
        /// an ending run (income, record, ratio and best as for stamina; the stamina left is dropped).
        /// </summary>
        void TickEnding(double dt)
        {
            EndingAge += dt;
            var ps = ReadPointer();
            LastPointer = ps;
            CursorWorld = ToWorld(ps.screenPos);
            Run.holding = false;
            var p = gameParams;
            float ppu = (float)p.pixelsPerUnit;
            var star = celestialTable.Get(celestialTable.MaxTier);
            float captureR = (float)((star != null ? star.sizePx : 100.0) * 0.5 / ppu);
            Dust.TickPull(dt, EndingCenter, (float)(p.endingPullAccelPx / ppu), (float)(p.endingPullMaxSpeedPx / ppu), (float)p.endingPullDamping, captureR);
            if (EndingAge >= p.endingPullSec + p.endingFadeSec)
            {
                EndRun(EndReason.Ending, true);
                return;
            }
            pendingSaveTimer += dt;
            if (pendingSaveTimer >= p.pendingSaveIntervalSec)
            {
                pendingSaveTimer = 0;
                Save();
            }
        }

        /// <summary>True when making this tier starts the ending: the last tier, and the ending was never reached (15 §4.1).</summary>
        public static bool StartsEnding(MetaState meta, int tier, int maxTier) => tier == maxTier && meta.endingRun == 0;

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
            Run.crossedTier = 0;
        }

        /// <summary>Randomness of the fragment rolls (20 §5). Apart from the dust and visual RNGs.</summary>
        readonly System.Random fragRng = new System.Random();

        /// <summary>
        /// 20 §5: walks the thresholds the held mass has newly crossed and rolls once for each. A hit drops a fragment of
        /// that tier, which pays a share of the tier's price and flies off to the vanishing point. The mass is untouched,
        /// so the body being formed is not slowed down, and the top tiers are out of reach through the offset.
        /// </summary>
        void DropFragments(in EffectiveStats s)
        {
            int cap = Stats.FragmentMaxTier(Meta.unlockedMaxTier, s);
            if (cap <= 0) return;
            for (int t = Run.crossedTier + 1; t <= celestialTable.MaxTier; t++)
            {
                var def = celestialTable.Get(t);
                if (def == null || Run.mass < Stats.Threshold(def, s)) break;
                Run.crossedTier = t;
                if (t <= cap && fragRng.NextDouble() < s.fragChance) DropFragment(def, s);
            }
        }

        void DropFragment(CelestialTier tier, in EffectiveStats s)
        {
            double income = Stats.FragmentIncome(gameParams, tier, s);
            Run.runIncome += income;
            Run.fragmentIncome += income;
            int idx = tier.tier - 1;
            if (idx >= 0 && idx < Run.fragmentCounts.Length) Run.fragmentCounts[idx]++;
            // It chips off the edge of the body being formed, so it starts beside the cursor rather than on it.
            double radius = Cursor.FormingDiameterPx * 0.5 * gameParams.fragmentSpawnRadii / gameParams.pixelsPerUnit;
            double angle = fragRng.NextDouble() * Math.PI * 2.0;
            var at = CursorWorld + new Vector2((float)(Math.Cos(angle) * radius), (float)(Math.Sin(angle) * radius));
            Planets.Show(at, tier, NextVisualSeed(), 0, gameParams.fragmentScale);
        }

        /// <summary>
        /// Creates and sells planets of this tier from the current mass. At the highest unlocked tier the mass makes
        /// floor(mass / threshold) planets, so dust mass beyond one threshold still pays (12 §10-2); below it, one planet.
        /// Each planet pays sale price × sale multiplier with fractions; the run total is rounded up once at the end (14 §8).
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
            // The forming body under the cursor becomes this planet (same surface, grows from its drawn size, 14 §4). The
            // first blue giant stays where it formed and starts the ending (15 §4.2); the others leave for the vanishing point.
            Cursor.TakeFormingBody(out float bodySeed, out double bodyPx);
            bool ending = !EndingActive && StartsEnding(Meta, tier.tier, celestialTable.MaxTier);
            if (ending)
            {
                EndingActive = true;
                EndingAge = 0;
                EndingCenter = CursorWorld;
                Run.holding = false;
                SupernovaRun.Begin(CursorWorld, tier, bodySeed, bodyPx);
                UnityEngine.Debug.Log($"[Ending] tier {tier.tier} created in run {Meta.runCount}: the ending starts");
            }
            else Planets.Show(CursorWorld, tier, bodySeed, bodyPx);
            if (count > 1) Hud.ShowPlanetCount(Cam.WorldToScreenPoint(CursorWorld), tier.sizePx, count);
            Run.mass = 0;
            Run.crossedTier = 0;
            PlanetCreated?.Invoke(tier);
        }

        /// <summary>
        /// Ends the run: income into currency, statistics and history (Session.ApplyRunEnd), save, run_log.csv.
        /// <paramref name="showResult"/> is false when quitting: no result screen and no bot shopping.
        /// </summary>
        void EndRun(string reason, bool showResult)
        {
            var rec = Session.MakeRecord(Meta, Meta.runCount, Run.elapsed, Run.runIncome, Run.planetCounts, runStartLevels,
                runStartStats, reason, Run.fragmentCounts, Run.fragmentIncome);
            Session.ApplyRunEnd(Meta, rec);
            Phase = GamePhase.Result;
            EndingActive = false;
            Save();
            LogRun(rec);
            if (showResult) RunEnded?.Invoke(rec);
        }

        void LogRun(RunRecord rec)
        {
            RunLogger.Append(DataDir, rec, celestialTable, gameParams);
            UnityEngine.Debug.Log(RunLogger.Summary(rec));
        }

        // ---------------- save ----------------

        /// <summary>Writes save.json. During a run the run in flight goes into pending.</summary>
        public void Save()
        {
            var data = new SaveData { meta = Meta };
            if (Phase == GamePhase.Run && Run != null)
                data.pending = Session.MakePending(Meta.runCount, Run.elapsed, Run.runIncome, Run.planetCounts, runStartLevels,
                    runStartStats, EndingActive, Run.fragmentCounts, Run.fragmentIncome);
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
            EndingActive = false;
            Phase = GamePhase.Result;
            Ending.Stop();
            pendingStart = true;
            UnityEngine.Debug.Log("[Save] save deleted; starting again from run 1");
        }

        // ---------------- ring snapshots (15 §6.2) ----------------

        public const string SnapshotDir = "snapshots";

        public static string SnapshotPath(string dataDir, int ring) => System.IO.Path.Combine(dataDir, SnapshotDir, "ring_" + ring + ".json");

        /// <summary>Copies the current save.json to snapshots/ring_k.json (the bot calls it right after buying the gate of ring k).</summary>
        public void WriteRingSnapshot(int ring)
        {
            try
            {
                string path = SnapshotPath(DataDir, ring);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.Copy(Store.SavePath, path, true);
                UnityEngine.Debug.Log($"[Snapshot] ring {ring}: {path}");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[Snapshot] write failed: " + e.Message);
            }
        }

        /// <summary>
        /// Replaces the game state with a save file (a ring snapshot) while playing: the run in flight is dropped unrecorded,
        /// the file becomes save.json and the game opens on its result screen. Returns false when the file cannot be read.
        /// </summary>
        public bool LoadSaveFile(string path)
        {
            var data = SaveStore.ReadFile(path);
            if (data == null) return false;
            Ending.Stop();
            Phase = GamePhase.Result;
            pendingStart = false;
            OpenLoaded(data, true);
            UnityEngine.Debug.Log($"[Save] loaded {path}: run {Meta.runCount}, max tier {Meta.unlockedMaxTier}");
            return true;
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

        /// <summary>
        /// Home key: drains the stamina at once, so the run ends on the next tick the normal way (held mass is released,
        /// income and the result screen as usual). Ignored between runs and during the ending.
        /// </summary>
        public bool DebugDrainStamina()
        {
            if (Phase != GamePhase.Run || Run == null || EndingActive)
            {
                Hud.ShowToast(UIStrings.DebugDuringRunOnly);
                return false;
            }
            Run.stamina = 0;
            Hud.ShowToast(UIStrings.DebugStaminaDrained);
            UnityEngine.Debug.Log("[Debug] stamina drained: the run ends now");
            return true;
        }

        /// <summary>
        /// End key (15 §4.6): replays the ending sequence with the last run's system, between runs only. The save is not
        /// touched (endingRun / endingSeen stay); "continue" leads to the tree as usual. Also for trailer captures.
        /// </summary>
        public bool DebugReplayEnding()
        {
            if (Phase != GamePhase.Result || Ending.IsPlaying)
            {
                Hud.ShowToast(UIStrings.EndingReplayBetweenRuns);
                return false;
            }
            Ending.Play(Meta.LastRun, true);
            Hud.ShowToast(UIStrings.EndingReplay);
            return true;
        }

        /// <summary>F6: raise the next gate node to level 1 for free (also mid-run). Returns the unlocked tier, or 0.</summary>
        public int DebugUnlockNext()
        {
            int tier = Shop.UnlockNextFree(nodeTable, Meta);
            UnityEngine.Debug.Log(tier > 0 ? $"[Debug] tier {tier} unlocked for free" : "[Debug] every tier is already unlocked");
            if (tier > 0) Save();
            return tier;
        }

        // ---------------- visuals ----------------

        readonly System.Random visualRng = new System.Random();

        /// <summary>Noise seed for a new body's surface. Separate from the logic RNGs so visuals never change the simulation.</summary>
        public float NextVisualSeed() => (float)(visualRng.NextDouble() * 100.0);

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

        /// <summary>
        /// Highest tier ever made, this run included (MetaState.highestTierCreated is updated only when a run ends).
        /// Drives the central star stage and the background palette (14 §6) without a new save field.
        /// </summary>
        public int HighestTierEver
        {
            get
            {
                int h = Meta.highestTierCreated;
                if (Phase == GamePhase.Run && Run != null)
                    for (int i = Run.planetCounts.Length - 1; i >= 0; i--)
                        if (Run.planetCounts[i] > 0)
                        {
                            if (i + 1 > h) h = i + 1;
                            break;
                        }
                return h;
            }
        }

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
            var s = StaminaDrain.enabled
                ? Stats.Compute(gameParams, nodeTable, Meta, StaminaDrain.holdDrain, StaminaDrain.idleDrain)
                : Stats.Compute(gameParams, nodeTable, Meta);
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
