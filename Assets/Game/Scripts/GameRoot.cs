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
    /// 24 §23.2: the gravity range the debug key K picked (radius node scale and the crystal's radius multiplier).
    /// Not set = the GameParams and AbilityTable values. Nothing is saved.
    /// </summary>
    public struct GravityRangeOverride
    {
        public bool set;
        public string id;
        public double growthScale;
        public double crystalRadiusMult;
        public double crystalRadiusPerPower;
    }

    /// <summary>
    /// Runtime setting of the gravity source follow (22 §3). The debug key cycles it; nothing is saved, and the
    /// GameParams value plus the follow nodes decide what a fresh launch does.
    /// </summary>
    public struct FollowOverride
    {
        public bool set;
        public bool enabled;
        public double speedPx;
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
        [Tooltip("29 Q9: the v1 skill tree (92 nodes), kept only to price the refund when a v4 save is moved to tree v2.")]
        public NodeTable nodeTableV1;
        [Tooltip("29 §10.1: the 13th pass tree (141 nodes), kept only to price the refund of the rule nodes a v5 save loses.")]
        public NodeTable nodeTableV5;
        [Tooltip("Special abilities (24). Without it the game runs with none.")]
        public AbilityTable abilityTable;
        [Tooltip("The look of the UI: fonts, palette, shapes, animation times (26 §4).")]
        public UITheme uiTheme;

        [Header("Render")]
        public Material unlitMaterial;
        [Tooltip("Incremental/Planet: every planet, star and forming body (14 §3).")]
        public Material planetMaterial;
        [Tooltip("Incremental/Dust: glowing dust points (14 §5).")]
        public Material dustMaterial;
        [Tooltip("Incremental/DustTrail: the trail ribbons of heavier dust (22 §5).")]
        public Material dustTrailMaterial;
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
        /// <summary>26 §5.5: the light ring that spreads with the zoom-out after a gate purchase.</summary>
        public GateExpandFx GateExpand { get; private set; }
        /// <summary>29 §10.2 D2: the crack flash where a large dust splits.</summary>
        public SplitFlashFx SplitFlash { get; private set; }
        /// <summary>15th pass: soft lines and ribbons (D2 cracks, meteor and comet tails), drawn over the dust.</summary>
        public FxLines Lines { get; private set; }
        public DustField Dust { get; private set; }
        public DustMeshRenderer DustRenderer { get; private set; }
        /// <summary>22 §5: the trail ribbons of dust above the base kind.</summary>
        public DustTrailRenderer TrailRenderer { get; private set; }
        public PlanetPool Planets { get; private set; }
        /// <summary>24: the special abilities of the run, ticked right before the dust.</summary>
        public AbilitySystem Abilities { get; private set; }
        public PerfBench Perf { get; private set; }
        /// <summary>26 §4: fonts, shape materials and the visual direction the UI is drawn in.</summary>
        public UIStyle Ui { get; private set; }
        public HudView Hud { get; private set; }
        /// <summary>29 §5: the rule nodes owned this run.</summary>
        public Rules Rules { get; private set; }
        /// <summary>26 §5.4: the fade between the run, the result and the tree.</summary>
        public ScreenTransition Transition { get; private set; }
        public CursorView Cursor { get; private set; }
        public ResultView Result { get; private set; }
        public SolarSystemView Solar { get; private set; }
        public TreeView Tree { get; private set; }
        public DebugTools Tools { get; private set; }
        /// <summary>26 §12: the visual pass items that are on now (debug keys B, N, J change it at runtime).</summary>
        public VisualToggles Visual { get; } = new VisualToggles();
        /// <summary>26 §12: lenses, background motion, hit stop, shake and the buff frame.</summary>
        public VisualFx Fx { get; private set; }
        public GalleryView Gallery { get; private set; }
        public SupernovaRunFx SupernovaRun { get; private set; }
        public EndingView Ending { get; private set; }
        public AutoplayBot Bot { get; private set; }
        public Vector2 CursorWorld { get; private set; }

        /// <summary>
        /// 22 §6: how much wider the play world is than at ring 1, as it is being drawn now. It follows
        /// <see cref="Stats.WorldScale"/> for the rings bought so far, but takes worldExpandSec to get there so the world
        /// visibly opens up the moment a gate is bought.
        /// </summary>
        public double WorldScale { get; private set; } = 1;
        /// <summary>Where <see cref="WorldScale"/> is heading: the scale of the rings bought so far.</summary>
        public double WorldScaleTarget => Stats.WorldScale(gameParams, Meta.unlockedMaxTier);
        /// <summary>
        /// The screens the world scale applies to (22 §6): the run screen, and the tree screen, where the background
        /// pulling back is what makes a gate purchase read as growth. The result screen keeps its own scale, because the
        /// solar system there is drawn in world space and has nothing to do with the play area.
        /// </summary>
        public bool WorldScaleActive => Phase == GamePhase.Run || (Tree != null && Tree.IsVisible);
        double worldScaleFrom = 1, worldScaleTo = 1, worldScaleAge = double.MaxValue;
        float baseOrthoSize = 5f;

        /// <summary>
        /// 22 §6: a body's drawn size in reference px, never so small that the zoomed-out camera turns it into a dot.
        /// The world size of a body does not change with the scale, so its screen size is its size over the scale; the
        /// floor is bodyMinScreenPx on screen.
        /// </summary>
        public double RunBodyPx(double px) => Math.Max(px, gameParams.bodyMinScreenPx * WorldScale);

        BlackHoleAbility blackHole;
        /// <summary>
        /// 24 §24.2-1: the drawn diameter (px) of a body at the gravity source, shrunk while the black hole is on. Render only.
        /// </summary>
        public double GravityBodyPx(double px)
        {
            if (blackHole == null && Abilities != null) blackHole = Abilities.Get("blackhole") as BlackHoleAbility;
            return blackHole != null ? blackHole.BodyPx(px) : px;
        }

        /// <summary>24 §24.3: the glow reach multiplier of a body at the gravity source (1 unless the black hole shrinks it). Render only.</summary>
        public float GravityGlowSizeMul()
        {
            if (blackHole == null && Abilities != null) blackHole = Abilities.Get("blackhole") as BlackHoleAbility;
            return blackHole != null ? blackHole.GlowSizeMul() : 1f;
        }

        /// <summary>24 §24.2-1: true while bodies at the gravity source are drawn smaller (a body made now stays shrunk until the restore).</summary>
        public bool GravityBodiesShrunk
        {
            get
            {
                if (blackHole == null && Abilities != null) blackHole = Abilities.Get("blackhole") as BlackHoleAbility;
                return blackHole != null && blackHole.Shrinking;
            }
        }
        /// <summary>
        /// Where the gravity actually pulls from (20 §7). The cursor itself unless the follow experiment is on, in which
        /// case it trails the cursor at a top speed. Bodies form here too, so the whole hold follows this point.
        /// </summary>
        public Vector2 GravityWorld { get; private set; }
        /// <summary>Runtime state of the follow debug key (22 §3); unset = the asset and the tree decide.</summary>
        public FollowOverride Follow;
        /// <summary>True while the gravity source trails the cursor instead of sitting on it (22 §3).</summary>
        public bool FollowEnabled => Follow.set ? Follow.enabled : gameParams.followEnabled;
        /// <summary>Top speed of the gravity source, px per second: the follow nodes' value unless the debug key set one (22 §3).</summary>
        public double FollowSpeedPx => Follow.set ? Follow.speedPx : Effective.followSpeedPx;
        public PointerState LastPointer { get; private set; }
        public double LastTickMs { get; private set; }
        public double MaxTickMs { get; private set; }

        /// <summary>
        /// 22 §4: milliseconds spent in the dust simulation since the last call, and zero after it. The performance
        /// measurement reads it once a frame; nothing else does, so the counter cannot go stale.
        /// </summary>
        public double TakeDustTickMs()
        {
            double v = dustTickMsAcc;
            dustTickMsAcc = 0;
            return v;
        }

        /// <summary>
        /// 11th pass F: milliseconds of the abilities (the amplifier's buff on the stats and every ability's tick) since
        /// the last call. The tick part is also inside <see cref="TakeDustTickMs"/>; this splits it out.
        /// </summary>
        public double TakeAbilityTickMs()
        {
            double v = abilityTickMsAcc;
            abilityTickMsAcc = 0;
            return v;
        }
        public StatsOverride Override;
        /// <summary>24 §23.2: the debug key K's gravity range; not set = the asset values.</summary>
        public GravityRangeOverride GravityRange;
        /// <summary>The radius node scale in use: the debug key K's, else GameParams.gravityGrowthScale.</summary>
        public double GrowthScaleNow => GravityRange.set ? GravityRange.growthScale : gameParams.gravityGrowthScale;
        /// <summary>
        /// 0..1, multiplies every flash, light bleed and pop (10 §8). Starts at GameParams.effectIntensity; the debug key
        /// cycles it at runtime (the options screen is week 7), so the asset is never modified.
        /// </summary>
        public float EffectIntensity { get; set; } = 1f;
        /// <summary>
        /// 26 §5.5: the overexposure measures (density dimming, dimming near the forming body, excite falloff by dust
        /// size, the brightness cap) are on. Starts at GameParams.exposureFix; the debug key X flips it for comparison.
        /// </summary>
        public bool ExposureFix { get; set; } = true;
        /// <summary>22 §6, 26 §5.5: the drawn world scale starts moving to a new target (from, to). A gate was bought.</summary>
        public event Action<double, double> WorldExpanding;
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
        /// <summary>22 §4: the dust simulation tick alone, so the measurement can split it out of the frame.</summary>
        readonly Stopwatch dustWatch = new Stopwatch();
        double dustTickMsAcc;
        readonly Stopwatch abilityWatch = new Stopwatch();
        double abilityTickMsAcc;
        List<UpgradeLevel> runStartLevels = new List<UpgradeLevel>();
        EffectiveStats runStartStats;
        bool pendingStart;
        /// <summary>28 §2.1: the hold the toggle mode keeps on between clicks. Let go whenever a run starts or ends.</summary>
        bool toggleHeld;
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
            Store = new SaveStore(DataDir) { TreeV1 = nodeTableV1, TreeV5 = nodeTableV5 };
            Rules = new Rules(gameParams);
            Settings = Store.LoadSettings();
            Fmt.Mode = Settings.notation;
            hasFocus = Application.isFocused;
            EffectIntensity = Mathf.Clamp01((float)gameParams.effectIntensity);
            Visual.FromData(gameParams, abilityTable);
            ExposureFix = gameParams.exposureFix;
            PlanetVisual.ApplyGlobals(gameParams);

            Cam = Camera.main;
            if (Cam == null) Cam = FindFirstObjectByType<Camera>();
            if (Cam != null) baseOrthoSize = Cam.orthographicSize;
            WorldScale = worldScaleFrom = worldScaleTo = Stats.WorldScale(gameParams, Meta.unlockedMaxTier);

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

            var trailGo = new GameObject("DustTrails", typeof(MeshFilter), typeof(MeshRenderer));
            trailGo.transform.SetParent(transform, false);
            TrailRenderer = trailGo.AddComponent<DustTrailRenderer>();
            TrailRenderer.Init(this, Dust, dustTrailMaterial);

            var linesGo = new GameObject("FxLines", typeof(MeshFilter), typeof(MeshRenderer));
            linesGo.transform.SetParent(transform, false);
            Lines = linesGo.AddComponent<FxLines>();
            // Over the dust (0) and the rings (5), under the meteor's body (9) so its tail starts behind it.
            Lines.Init(dustTrailMaterial, 8);

            var hudGo = new GameObject("HudView");
            hudGo.transform.SetParent(transform, false);
            Ui = new UIStyle(uiTheme);
            UIStyle.SetEffect(EffectIntensity);
            Hud = hudGo.AddComponent<HudView>();
            Hud.Build(this);
            Rules.ChainShown = Hud.ShowChain;
            Hud.SetCountLifetime(gameParams.planetPopDurationSec + gameParams.planetHoldSec + gameParams.planetDepartSec * 0.5);
            Hud.SetCountYield(gameParams.bundleLabelYieldSec);
            UIBuilder.EnsureEventSystem(transform);

            var transitionGo = new GameObject("Transition");
            transitionGo.transform.SetParent(transform, false);
            Transition = transitionGo.AddComponent<ScreenTransition>();
            Transition.Init(this);

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

            var fxGo = new GameObject("VisualFx");
            fxGo.transform.SetParent(transform, false);
            Fx = fxGo.AddComponent<VisualFx>();
            Fx.Init(this);

            var ignitionGo = new GameObject("Ignition");
            ignitionGo.transform.SetParent(transform, false);
            Ignition = ignitionGo.AddComponent<IgnitionFx>();
            Ignition.Init(this, fxMaterial);

            var gateFxGo = new GameObject("GateExpand");
            gateFxGo.transform.SetParent(transform, false);
            GateExpand = gateFxGo.AddComponent<GateExpandFx>();
            GateExpand.Init(this, fxMaterial);

            var splitFxGo = new GameObject("SplitFlash");
            splitFxGo.transform.SetParent(transform, false);
            SplitFlash = splitFxGo.AddComponent<SplitFlashFx>();
            SplitFlash.Init(this, fxMaterial);
            Dust.Split = pos =>
            {
                Rules.OnSplit();
                SplitFlash.Show(pos);
            };

            var abilitiesGo = new GameObject("Abilities");
            abilitiesGo.transform.SetParent(transform, false);
            Abilities = abilitiesGo.AddComponent<AbilitySystem>();
            Abilities.Init(this);

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

            var perfGo = new GameObject("PerfBench");
            perfGo.transform.SetParent(transform, false);
            Perf = perfGo.AddComponent<PerfBench>();
            Perf.Init(this);
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
                StartPerfBenchIfAsked();
                return;
            }
            OpenLoaded(data, status == LoadStatus.RestoredFromBackup);
            UnityEngine.Debug.Log($"[Save] loaded ({status}): run {Meta.runCount}, currency {Fmt.Num(Meta.currency)}, max tier {Meta.unlockedMaxTier}" +
                                  (Meta.endingRun > 0 ? $", ending run {Meta.endingRun} (seen {Meta.endingSeen})" : string.Empty));
            StartPerfBenchIfAsked();
        }

        /// <summary>
        /// 22 §4: started with <c>-perfbench</c> the build measures itself instead of being played, writes the table and
        /// quits. It is the only way to get build numbers without a person at the keyboard.
        /// </summary>
        void StartPerfBenchIfAsked()
        {
            if (!PerfBench.WantedOnCommandLine) return;
            Perf.Begin(PerfBench.OutFileArg, PerfBench.LabelArg, true);
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
        /// Opening on the shop from a save: the HUD and dust field look like a finished run (no time left, last run income,
        /// the run-start dust as a still background), as they do after a run ends.
        /// </summary>
        void ShowIdleField()
        {
            Effective = ComputeStats();
            var last = Meta.LastRun;
            Hud.SetIncome(last != null ? last.income : 0);
            Hud.SetTimeLeft(0, Effective.runTimeSec);
            var ps = ReadPointer();
            LastPointer = ps;
            CursorWorld = ToWorld(ps.screenPos);
            GravityWorld = CursorWorld;
            Dust.Reset((int)Effective.dustInitial, CameraArea(), CursorWorld, Effective);
        }

        /// <summary>
        /// Quitting mid-run ends the run without the result screen (income kept). Held mass is released first, as when
        /// the time runs out (12 §10-4). Between runs it just saves.
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

        void OnDestroy()
        {
            if (Ui != null) Ui.Dispose();
        }

        void FixedUpdate()
        {
            TickWorldScale(Time.fixedDeltaTime);
            tickWatch.Restart();
            if (pendingStart)
            {
                pendingStart = false;
                StartRun();
            }
            // 26 §12.3 E1: a hit stop holds the run still for a few ticks (nothing it counts changes, only when).
            if (Phase == GamePhase.Run && !Paused && !(Fx != null && Fx.TakeHitStop(Time.fixedDeltaTime))) TickRun(Time.fixedDeltaTime);
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

        /// <summary>
        /// 22 §6: moves the drawn world scale toward the scale of the rings bought so far, over worldExpandSec, and puts
        /// it on the camera. A new target restarts the move from wherever the scale is now, so buying two gates in a row
        /// still looks like one continuous opening up.
        /// </summary>
        void TickWorldScale(double dt)
        {
            double target = WorldScaleTarget;
            if (target != worldScaleTo)
            {
                worldScaleFrom = WorldScale;
                worldScaleTo = target;
                worldScaleAge = 0;
                WorldExpanding?.Invoke(worldScaleFrom, worldScaleTo);
            }
            double sec = Math.Max(1e-4, gameParams.worldExpandSec);
            if (worldScaleAge < sec)
            {
                worldScaleAge += dt;
                double k = Math.Clamp(worldScaleAge / sec, 0, 1);
                WorldScale = worldScaleFrom + (worldScaleTo - worldScaleFrom) * (k * k * (3.0 - 2.0 * k));
            }
            else WorldScale = worldScaleTo;
            ApplyWorldScale();
        }

        /// <summary>Puts the drawn world scale on the camera. Everything that reads the camera follows from here.</summary>
        /// <summary>
        /// 24 §23.2: a gravity radius (px) as a share of the screen height, for the diameter, at world scale
        /// <paramref name="worldScale"/>. The camera shows baseOrthoSize x the world scale.
        /// </summary>
        public double RingShareOfScreen(double radiusPx, double worldScale) =>
            radiusPx * 2.0 / gameParams.pixelsPerUnit / (baseOrthoSize * 2.0 * worldScale);

        void ApplyWorldScale()
        {
            if (Cam == null) return;
            float want = baseOrthoSize * (float)(WorldScaleActive ? WorldScale : 1.0) * (Transition != null ? Transition.Zoom : 1f);
            if (!Mathf.Approximately(Cam.orthographicSize, want)) Cam.orthographicSize = want;
        }

        void Update()
        {
            TickHoldToggle();
            TickPlayClock();
            ApplyWorldScale();
            // 26 §4: the light of the UI follows the effect intensity like every other effect.
            UIStyle.SetEffect(EffectIntensity);
            Ui.Tick();
            // Between runs the result screen and the tree show their own numbers (Space is handled there too). The dust is
            // drawn during runs, and on the result screen only as the supernova remnant (15 §4.3).
            // 26 §5.4: when a run has just ended its screen stays up until the fade reaches its midpoint.
            bool runView = Phase == GamePhase.Run || Transition.HoldingRunView;
            Hud.SetRunHudVisible(runView);
            bool dustVisible = runView || Ending.ShowsDust;
            DustRenderer.SetVisible(dustVisible);
            TrailRenderer.SetVisible(dustVisible);
            Hud.SetCurrency(Meta.currency, Meta.runCount);
            if (Run != null)
            {
                Hud.SetIncome(Run.runIncome);
                Hud.SetTimeLeft(Run.timeLeft, runStartStats.runTimeSec, Phase == GamePhase.Run ? Rules.TimeBarMode(Run) : 0);
            }
            bool paused = Paused;
            if (paused != shownPaused)
            {
                shownPaused = paused;
                Hud.SetCenter(paused ? UIStrings.Paused : string.Empty);
            }
        }

        /// <summary>
        /// 28 §4: the play clock of the progress log runs on every screen (run, result, tree), in game time so a bot
        /// measurement at a raised time scale reads like a real play. It stops while the game is paused, and between
        /// runs while the window is unfocused under the same setting that pauses a run.
        /// </summary>
        void TickPlayClock()
        {
            if (Paused) return;
            bool away = !hasFocus && Settings.pauseOnFocusLoss && !(Bot != null && Bot.Enabled);
            if (away || (Gallery != null && Gallery.IsOpen)) return;
            Meta.playClockSec += Time.deltaTime;
        }

        /// <summary>
        /// 28 §2.1: with the hold toggle on, a click during a run flips the hold. Done here, once a frame, because the
        /// fixed tick can miss the frame a click lands on. Paused runs and the ending ignore clicks.
        /// </summary>
        void TickHoldToggle()
        {
            if (!Settings.holdToggle || Phase != GamePhase.Run || Run == null || EndingActive || Paused)
                return;
            var m = Mouse.current;
            if (m == null || !m.leftButton.wasPressedThisFrame) return;
            toggleHeld = !toggleHeld;
            UnityEngine.Debug.Log("[Feature] hold toggle: " + (toggleHeld ? "holding" : "let go"));
        }

        /// <summary>
        /// 28 §2.1, debug key H: the hold toggle on or off, written to settings.json. Turning it off lets go of a hold
        /// the toggle was keeping.
        /// </summary>
        public void SetHoldToggle(bool on)
        {
            Settings.holdToggle = on;
            if (!on) toggleHeld = false;
            SaveSettings();
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
            Run = new RunState(celestialTable.MaxTier) { timeLeft = Effective.runTimeSec };
            toggleHeld = false;
            bodyGapLeft = 0;
            LastBundleCount = 0;
            LastBundleAt = -1;
            // 29 §5: the rule nodes owned for this run, and what they do to the dust from the start.
            Rules.BeginRun(Meta, nodeTable);
            Dust.RefillBoostAdd = Rules.RefillBoostAdd;
            Dust.GoldChance = Rules.GoldChance;
            Dust.GoldMassMult = gameParams.rules.goldMassMult;
            Dust.GoldCaptured = 0;
            Dust.SplitChance = Rules.SplitChance;
            Dust.SpawnMult = 1;
            Dust.Inflow = false;
            SplitFlash.Clear();
            Backdrop.PulseGlow = 0f;

            // 22 §6: the run is drawn at the world scale, and coming from the result screen the camera is not there
            // yet. It goes on before anything is placed, so the pointer is read and the dust laid out for the area the
            // run really shows. Laid out for the smaller one, the band of dust outside the screen started inside it.
            Phase = GamePhase.Run;
            ApplyWorldScale();

            // 24 §23.1.1: the gravity source starts in the middle of the screen. The pointer is moved there too, unless the
            // flag is off (then the gravity source follows it from the middle) or something else drives the input (the bot).
            Vector2 middle = CameraArea().center;
            var ps = ReadPointer();
            bool warp = gameParams.warpPointerOnRunStart && InputProvider == null && Mouse.current != null;
            if (warp)
            {
                var screenMiddle = new Vector2(Cam.pixelWidth * 0.5f, Cam.pixelHeight * 0.5f);
                Mouse.current.WarpCursorPosition(screenMiddle);
                ps.screenPos = screenMiddle;
            }
            LastPointer = ps;
            CursorWorld = warp ? middle : ToWorld(ps.screenPos);
            GravityWorld = middle;
            UnityEngine.Debug.Log("[Run] start: gravity source in the middle of the screen" + (warp ? ", pointer moved there" : ", pointer left where it is") +
                "; starting dust " + (int)Effective.dustInitial + ", clear of " + gameParams.initialDustClearShare.ToString("0.##") + " gravity radius");
            Dust.Reset((int)Rules.StartDust(Effective.dustInitial, Effective.dustCap), CameraArea(), GravityWorld, Effective);
            Planets.Clear();
            pendingSaveTimer = 0;
            Save();
            RunStarted?.Invoke();
        }

        void TickRun(double dt)
        {
            var s = ComputeStats();
            // 24 §20.2: the amplifier's buff goes on the stats of this tick before anything reads them, the abilities,
            // the dust and the gravity ring alike.
            abilityWatch.Restart();
            Abilities.Amplify(ref s);
            abilityWatch.Stop();
            abilityTickMsAcc += abilityWatch.Elapsed.TotalMilliseconds;
            // 29 §5 G1, G3: the rules' share on top of the buffs.
            Rules.Amplify(ref s);
            Effective = s;
            if (EndingActive)
            {
                TickEnding(dt);
                return;
            }

            var ps = ReadPointer();
            LastPointer = ps;
            CursorWorld = ToWorld(ps.screenPos);
            MoveGravitySource(dt);

            bool wasHolding = Run.holding;
            bool holding = ps.pressed;
            Run.holding = holding;
            Run.elapsed += dt;
            Rules.Track(dt, holding, GravityWorld);
            // 28 §2: the clock runs down the same whether the player holds or not; 29 §5 T1 holds it at the start.
            bool timeUp = !Rules.InGrace(Run) && Session.TickClock(Run, dt);
            // 29 §5 G2: the gravity keeps pulling for a moment after a release.
            bool dustHolding = Rules.DustHolding(dt, wasHolding, holding);
            // 29 §10.3 D5, A5: the spawn multiplier of this tick; during a D5 pulse new dust comes in from the edge band.
            // 29 §10.7 S4: the spawn_ramp nodes speed the spawn up as the run goes on.
            Dust.SpawnMult = Rules.SpawnMult(Run) * Stats.SpawnRampMult(s.spawnRamp, Run.elapsed, gameParams.spawnRampSec);
            bool pulse = Rules.PulsePhase(Run) >= 0;
            Dust.Inflow = pulse;
            Backdrop.PulseTarget = pulse ? 1f : 0f;

            dustWatch.Restart();
            // 24: the abilities go first, so what they do to the dust this tick is part of this same dust tick, and
            // their cost is counted with it.
            abilityWatch.Restart();
            Abilities.Tick(dt, holding, s);
            abilityWatch.Stop();
            abilityTickMsAcc += abilityWatch.Elapsed.TotalMilliseconds;
            double captured = Dust.Tick(dt, CameraArea(), GravityWorld, dustHolding, s, Abilities.Forces, Abilities.ForceCount);
            dustWatch.Stop();
            dustTickMsAcc += dustWatch.Elapsed.TotalMilliseconds;
            if (captured > 0) Run.mass += captured * s.dustMass;

            // 20 §5: the moment the held mass passes a tier threshold, that tier may drop a fragment. No mass is spent.
            if (holding && captured > 0) DropFragments(s);

            // Reaching the highest unlocked tier creates immediately, without releasing. 24 §19.2: once a body is made
            // this way the next one waits out bodyMinIntervalSec, the mass meanwhile staying held.
            if (bodyGapLeft > 0) bodyGapLeft -= dt;
            if (holding && bodyGapLeft <= 0)
            {
                var top = celestialTable.Get(Meta.unlockedMaxTier);
                if (top != null && Run.mass >= Stats.Threshold(top, s)) CreatePlanet(top, gameParams.bodyMinIntervalSec > 0);
            }

            if (wasHolding && !holding) Release();

            if (timeUp)
            {
                if (Run.holding)
                {
                    Release();
                    Run.holding = false;
                }
                EndRun(EndReason.TimeUp, true);
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
        /// 22 §3: the gravity source trails the cursor at a top speed instead of sitting on it, so a fast sweep leaves it
        /// behind and letting go to grab again becomes worth it. Switched off, the source is the cursor exactly.
        /// </summary>
        void MoveGravitySource(double dt)
        {
            if (!FollowEnabled)
            {
                GravityWorld = CursorWorld;
                return;
            }
            float step = (float)(FollowSpeedPx / gameParams.pixelsPerUnit * dt);
            Vector2 to = CursorWorld - GravityWorld;
            float d = to.magnitude;
            GravityWorld = d <= step || d < 1e-6f ? CursorWorld : GravityWorld + to * (step / d);
        }

        /// <summary>
        /// The ending on the run screen (15 §4.2): input is ignored, nothing spawns, the time left and the run clock stand
        /// still, every dust falls into the blue giant for endingPullSec, the screen darkens for endingFadeSec, then the run
        /// ends as an ending run (income and record as for a run that ran out of time; the time left is dropped).
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
            if (n > 0) Dust.Scatter(n, GravityWorld);
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
            double income = Stats.FragmentIncome(gameParams, tier, s) * Rules.RushMult(Run);
            Run.runIncome += income;
            Run.fragmentIncome += income;
            int idx = tier.tier - 1;
            if (idx >= 0 && idx < Run.fragmentCounts.Length) Run.fragmentCounts[idx]++;
            // It chips off the edge of the body being formed, so it starts beside the cursor rather than on it.
            double radius = Cursor.FormingDiameterPx * 0.5 * gameParams.fragmentSpawnRadii / gameParams.pixelsPerUnit;
            double angle = fragRng.NextDouble() * Math.PI * 2.0;
            var at = GravityWorld + new Vector2((float)(Math.Cos(angle) * radius), (float)(Math.Sin(angle) * radius));
            Planets.Show(at, tier, NextVisualSeed(), 0, gameParams.fragmentScale);
        }

        /// <summary>
        /// 24 §19.2: seconds left before the next body may be completed while holding at the highest tier.
        /// </summary>
        double bodyGapLeft;

        /// <summary>24 §21.3: the last bundle of this run, for captures and checks: how many bodies it was worth, and when (−1 = none yet).</summary>
        public int LastBundleCount { get; private set; }
        public double LastBundleAt { get; private set; } = -1;

        /// <summary>
        /// Creates and sells planets of this tier from the current mass. At the highest unlocked tier the mass makes
        /// floor(mass / threshold) planets, so dust mass beyond one threshold still pays (12 §10-2); below it, one planet.
        /// Each planet pays sale price × sale multiplier with fractions; the run total is rounded up once at the end (14 §8).
        /// With <paramref name="oneAtATime"/> (24 §19.2, the hold at the highest tier) only one body is made and the mass
        /// past its threshold stays held for the next one, which waits out GameParams.bodyMinIntervalSec.
        /// </summary>
        void CreatePlanet(CelestialTier tier, bool oneAtATime = false)
        {
            double count = 1;
            double keep = 0;
            bool bundle = false;
            double th = tier.tier == Meta.unlockedMaxTier ? Threshold(tier) : 0;
            if (th > 0 && oneAtATime)
            {
                // 24 §21.3 묶음 완성: mass that has piled up to bundleAt thresholds becomes one body worth all of them.
                double bundleAt = Rules.BundleAt(gameParams.bundleAt);
                if (gameParams.bundleAt > 0 && Run.mass >= th * bundleAt)
                {
                    count = Math.Max(1.0, Math.Floor(Run.mass / th));
                    bundle = count > 1;
                }
                keep = Math.Max(0.0, Run.mass - th * count);
                bodyGapLeft = gameParams.bodyMinIntervalSec;
            }
            else if (th > 0) count = Math.Max(1.0, Math.Floor(Run.mass / th));
            // 29 §5: C1 chain, C2 every n-th (count x2, shown as a bundle), C3 first body, T2 time, T3 rush.
            double ruleMult = Rules.OnComplete(Run, tier.tier, ref count, out bool nth);
            if (nth) bundle = true;
            Run.runIncome += count * Stats.SaleIncome(tier, Effective) * ruleMult;
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
                EndingCenter = GravityWorld;
                Run.holding = false;
                SupernovaRun.Begin(GravityWorld, tier, bodySeed, bodyPx);
                UnityEngine.Debug.Log($"[Ending] tier {tier.tier} created in run {Meta.runCount}: the ending starts");
            }
            else if (bundle)
            {
                // 24 §21.3: the bundle is one body, bigger for every body in it, landing harder, with a big x N over it.
                double scale = Math.Min(gameParams.bundleSizeMax, 1.0 + Math.Max(0.0, gameParams.bundleSizeGrowth) * (count - 1));
                Planets.Show(GravityWorld, tier, bodySeed, bodyPx, scale, gameParams.bundlePopMult);
                Hud.ShowPlanetCount(Cam.WorldToScreenPoint(GravityWorld), tier.sizePx * scale, count, gameParams.bundleLabelScale);
                LastBundleCount = (int)count;
                Fx.OnBundle(GravityWorld, count);
                LastBundleAt = Run.elapsed;
                if (abilityTable == null || abilityTable.logEvents)
                    UnityEngine.Debug.Log($"[Bundle] x{count:0} of tier {tier.tier} at {Run.elapsed:F1}s: {count:0} thresholds of {th:F0} held, drawn x{scale:0.##}, {keep:F0} mass kept");
            }
            else Planets.Show(GravityWorld, tier, bodySeed, bodyPx);
            if (count > 1 && !bundle) Hud.ShowPlanetCount(Cam.WorldToScreenPoint(GravityWorld), tier.sizePx, count);
            Run.mass = keep;
            // The kept mass has crossed its thresholds once already (20 §5 rolled for them), so they are not crossed again.
            Run.crossedTier = 0;
            if (keep > 0)
                for (int t = 1; t <= celestialTable.MaxTier; t++)
                {
                    var def = celestialTable.Get(t);
                    if (def == null || keep < Threshold(def)) break;
                    Run.crossedTier = t;
                }
            PlanetCreated?.Invoke(tier);
        }

        /// <summary>
        /// Ends the run: income into currency, statistics and history (Session.ApplyRunEnd), save, run_log.csv.
        /// <paramref name="showResult"/> is false when quitting: no result screen and no bot shopping.
        /// </summary>
        void EndRun(string reason, bool showResult)
        {
            Rules.EndRun(Dust.GoldCaptured);
            Dust.SpawnMult = 1;
            Dust.Inflow = false;
            Backdrop.PulseTarget = 0f;
            var rec = Session.MakeRecord(Meta, Meta.runCount, Run.elapsed, Run.runIncome, Run.planetCounts, runStartLevels,
                runStartStats, reason, Run.fragmentCounts, Run.fragmentIncome);
            Session.ApplyRunEnd(Meta, rec);
            Phase = GamePhase.Result;
            EndingActive = false;
            // 28 §2.1: the toggle's hold ends with the run.
            toggleHeld = false;
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
            // 28 §4: a new game is where a measurement play starts; the play clock is 0 again (a new MetaState).
            ProgressLogger.Write(DataDir, "new_game", null, Meta, 0, 0);
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
            if (Phase != GamePhase.Result) return false;
            var node = nodeTable.Get(nodeId);
            double before = Meta.currency;
            double cost = node != null ? Shop.Cost(nodeTable, Meta, node) : 0;
            bool first = !AnyLevel(Meta);
            if (!Shop.Buy(nodeTable, Meta, nodeId)) return false;
            Save();
            // 28 §4: every gate, and the very first purchase of the game, goes into the progress log.
            if (node != null && node.IsGate) ProgressLogger.Write(DataDir, "gate", nodeId, Meta, before, cost);
            else if (first) ProgressLogger.Write(DataDir, "first_purchase", nodeId, Meta, before, cost);
            return true;
        }

        static bool AnyLevel(MetaState m)
        {
            foreach (var l in m.upgradeLevels) if (l != null && l.level > 0) return true;
            return false;
        }

        // ---------------- debug (DebugTools) ----------------

        /// <summary>F3: currency = max(currency × mult, min).</summary>
        public void DebugBoostCurrency(double mult, double min) => Meta.currency = Math.Max(Meta.currency * mult, min);

        /// <summary>
        /// Home key: no time left, so the run ends on the next tick the normal way (held mass is released, income and the
        /// result screen as usual). Ignored between runs and during the ending.
        /// </summary>
        public bool DebugTimeUp()
        {
            if (Phase != GamePhase.Run || Run == null || EndingActive)
            {
                Hud.ShowToast(UIStrings.DebugDuringRunOnly);
                return false;
            }
            Run.timeLeft = 0;
            Hud.ShowToast(UIStrings.DebugTimeUp);
            UnityEngine.Debug.Log("[Debug] time left 0: the run ends now");
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
            // 28 §2.1: in toggle mode the hold is what the clicks left it at, not the button.
            bool pressed = Settings.holdToggle ? toggleHeld : m.leftButton.isPressed;
            return new PointerState { screenPos = m.position.ReadValue(), pressed = pressed };
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
            var s = Stats.Compute(gameParams, nodeTable, Meta, GrowthScaleNow);
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
