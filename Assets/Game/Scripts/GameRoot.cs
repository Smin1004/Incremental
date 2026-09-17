using System;
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
        public UpgradeTable upgradeTable;

        [Header("Render")]
        public Material unlitMaterial;
        public Sprite circleSprite;
        public int planetPoolSize = 8;

        public MetaState Meta { get; private set; } = new MetaState();
        public RunState Run { get; private set; }
        public EffectiveStats Effective { get; private set; }
        public GamePhase Phase { get; private set; } = GamePhase.Result;
        public Camera Cam { get; private set; }
        public DustField Dust { get; private set; }
        public DustMeshRenderer DustRenderer { get; private set; }
        public PlanetPool Planets { get; private set; }
        public HudView Hud { get; private set; }
        public CursorView Cursor { get; private set; }
        public DebugTools Tools { get; private set; }
        public Vector2 CursorWorld { get; private set; }
        public PointerState LastPointer { get; private set; }
        public double LastTickMs { get; private set; }
        public double MaxTickMs { get; private set; }
        public StatsOverride Override;
        /// <summary>Replace to drive the game without a mouse (autoplay bot).</summary>
        public Func<PointerState> InputProvider;
        public event Action<RunRecord> RunEnded;
        public event Action RunStarted;

        readonly int[] startLevels = new int[UpgradeTable.UpgradeCount];
        readonly Stopwatch tickWatch = new Stopwatch();
        bool pendingStart;
        double maxTickWindow;
        double maxTickAcc;

        void Awake()
        {
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
            UIBuilder.EnsureEventSystem(transform);

            var cursorGo = new GameObject("CursorView");
            cursorGo.transform.SetParent(transform, false);
            Cursor = cursorGo.AddComponent<CursorView>();
            Cursor.Init(this, unlitMaterial, Hud.CanvasRect, circleSprite);

            var toolsGo = new GameObject("DebugTools");
            toolsGo.transform.SetParent(transform, false);
            Tools = toolsGo.AddComponent<DebugTools>();
            Tools.Init(this);
        }

        void Start()
        {
            StartRun();
        }

        void FixedUpdate()
        {
            tickWatch.Restart();
            if (pendingStart)
            {
                pendingStart = false;
                StartRun();
            }
            if (Phase == GamePhase.Run) TickRun(Time.fixedDeltaTime);
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
            Array.Copy(Meta.upgradeLevels, startLevels, startLevels.Length);
            Effective = ComputeStats();
            Run = new RunState(celestialTable.TierCount) { stamina = Effective.staminaMax };

            var ps = ReadPointer();
            LastPointer = ps;
            CursorWorld = ToWorld(ps.screenPos);
            Dust.Reset((int)gameParams.dustInitial, CameraArea(), CursorWorld);
            Planets.Clear();
            Phase = GamePhase.Run;
            Hud.SetCenter(string.Empty);
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
            Run.stamina -= gameParams.staminaIdleDrain * dt;
            if (holding) Run.stamina -= gameParams.staminaHoldDrain * dt;

            int captured = Dust.Tick(dt, CameraArea(), CursorWorld, holding, s);
            if (captured > 0) Run.mass += captured * gameParams.dustMass;

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
                EndRun();
            }
        }

        /// <summary>Release: create the highest reached tier, or scatter the collected dust if below tier 1.</summary>
        void Release()
        {
            var tier = HighestReachedTier();
            if (tier != null)
            {
                CreatePlanet(tier);
                return;
            }
            int n = gameParams.dustMass > 0 ? (int)Math.Round(Run.mass / gameParams.dustMass) : 0;
            if (n > 0) Dust.Scatter(n, CursorWorld);
            Run.mass = 0;
        }

        void CreatePlanet(CelestialTier tier)
        {
            Run.runIncome += tier.salePrice * Effective.saleMult;
            int idx = tier.tier - 1;
            if (idx >= 0 && idx < Run.planetCounts.Length) Run.planetCounts[idx]++;
            Planets.Show(CursorWorld, tier.sizePx, tier.color);
            Run.mass = 0;
        }

        void EndRun()
        {
            double ratio = (Meta.runCount > 1 && Meta.lastRunIncome > 0) ? Run.runIncome / Meta.lastRunIncome : double.NaN;
            var rec = new RunRecord
            {
                run = Meta.runCount,
                durationSec = Run.elapsed,
                income = Run.runIncome,
                tierCounts = (int[])Run.planetCounts.Clone(),
                ratioVsLast = ratio,
                startLevels = (int[])startLevels.Clone(),
            };

            Meta.currency += Run.runIncome;
            Meta.lastRunIncome = Run.runIncome;
            if (Run.runIncome > Meta.bestRunIncome) Meta.bestRunIncome = Run.runIncome;
            Phase = GamePhase.Result;

            RunLogger.Append(rec);
            UnityEngine.Debug.Log(RunLogger.Summary(rec));
            Hud.SetCenter(string.Format(UIStrings.RunEndedTemp, Fmt.Int(rec.income)));
            RunEnded?.Invoke(rec);
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
            var s = Stats.Compute(gameParams, upgradeTable, Meta);
            if (Override.enabled)
            {
                s.dustCap = Override.dustCap;
                s.spawnRate = Override.spawnRate;
            }
            return s;
        }
    }
}
