using System;
using System.Collections.Generic;
using UnityEngine;

namespace Incremental
{
    /// <summary>How the bot handles the next gate between runs.</summary>
    public enum GateSaving
    {
        /// <summary>Cheapest affordable node first, gate included (week-1 behaviour).</summary>
        Off,
        /// <summary>12 §3: gate first when affordable; buy nothing while gate ≤ currency + last run income.</summary>
        Stop,
        /// <summary>Like Stop, but while saving still spends what exceeds the reserve (gate − last run income).</summary>
        KeepReserve,
    }

    /// <summary>
    /// F4 autoplay bot for unattended measurement runs. Simple rules only:
    /// during a run, move the cursor toward the densest dust cell and hold; release once the goal tier
    /// (highest unlocked tier) threshold is reached; between runs buy purchasable skill tree nodes, cheapest first,
    /// until nothing is affordable, then start the next run. Drives the game through GameRoot.InputProvider.
    /// <see cref="gateSaving"/> (12 §3): the next gate is bought first when affordable, and the bot saves for it once the
    /// gate costs no more than currency + last run income, instead of spending everything on cheap nodes
    /// (week 1 report: the bot never unlocked tier 2).
    /// </summary>
    public sealed class AutoplayBot : MonoBehaviour
    {
        [Tooltip("Cursor speed in screen pixels per second.")]
        public double moveSpeedPx = 250;
        public int gridX = 12;
        public int gridY = 7;
        [Tooltip("Stop the bot after this many completed runs (0 = keep going).")]
        public int stopAfterRuns = 0;
        [Tooltip("Stop: buy the next gate first and buy nothing else once currency + last run income would pay for it (12 §3). " +
                 "KeepReserve: while saving, still spend what exceeds gate − last run income.")]
        public GateSaving gateSaving = GateSaving.Stop;

        GameRoot root;
        Vector2 cursor;
        bool pressed;
        int[] counts = new int[0];
        readonly List<NodeDef> scratch = new List<NodeDef>();
        int runsCompleted;
        Func<PointerState> provider;

        public bool Enabled { get; private set; }
        public int RunsCompleted => runsCompleted;

        public void Init(GameRoot gameRoot)
        {
            root = gameRoot;
            provider = Provide;
            root.RunEnded += OnRunEnded;
        }

        public void SetEnabled(bool on)
        {
            if (Enabled == on) return;
            Enabled = on;
            if (on)
            {
                runsCompleted = 0;
                cursor = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                pressed = false;
                root.InputProvider = provider;
            }
            else if (root.InputProvider == provider)
            {
                root.InputProvider = null;
            }
            if (root.Tools != null) root.Tools.BotEnabled = on;
            Debug.Log(on ? "[Bot] enabled" : "[Bot] disabled");

            // Enabled while the shop is open: shop and start the next run right away.
            if (on && root.Phase == GamePhase.Result)
            {
                BuyCheapestUntilBroke();
                root.RequestStartRun();
            }
        }

        PointerState Provide() => new PointerState { screenPos = cursor, pressed = pressed };

        void FixedUpdate()
        {
            if (!Enabled || root.Phase != GamePhase.Run || root.Run == null)
            {
                pressed = false;
                return;
            }

            float step = (float)moveSpeedPx * Time.fixedDeltaTime;
            Vector2 target = DensestCellScreen();
            Vector2 d = target - cursor;
            if (d.magnitude > step) cursor += d.normalized * step; else cursor = target;

            // Hold; release once the goal tier's threshold is reached. The game already creates the top
            // unlocked tier by itself, so this only matters if that rule changes.
            var goal = root.celestialTable.Get(root.Meta.unlockedMaxTier);
            bool reached = goal != null && root.Run.mass >= root.Threshold(goal);
            pressed = !reached;
        }

        /// <summary>Screen position of the center of the grid cell holding the most dust.</summary>
        Vector2 DensestCellScreen()
        {
            int gx = Mathf.Max(1, gridX), gy = Mathf.Max(1, gridY);
            if (counts.Length != gx * gy) counts = new int[gx * gy];
            else Array.Clear(counts, 0, counts.Length);

            var items = root.Dust.Items;
            int n = root.Dust.Count;
            Rect area = root.Dust.Area;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = items[i].pos;
                int cx = Mathf.Clamp((int)((p.x - area.xMin) / area.width * gx), 0, gx - 1);
                int cy = Mathf.Clamp((int)((p.y - area.yMin) / area.height * gy), 0, gy - 1);
                counts[cy * gx + cx]++;
            }
            int best = 0;
            for (int i = 1; i < counts.Length; i++)
                if (counts[i] > counts[best]) best = i;

            float wx = area.xMin + ((best % gx) + 0.5f) * area.width / gx;
            float wy = area.yMin + ((best / gx) + 0.5f) * area.height / gy;
            Vector3 sp = root.Cam.WorldToScreenPoint(new Vector3(wx, wy, 0f));
            return new Vector2(sp.x, sp.y);
        }

        void OnRunEnded(RunRecord rec)
        {
            if (!Enabled) return;
            runsCompleted++;
            int bought = BuyCheapestUntilBroke();
            var m = root.Meta;
            Debug.Log($"[Bot] run {rec.run} income={Fmt.Num(rec.income)} bought={bought} currency={Fmt.Num(m.currency)} levels=[{RunLogger.LevelsText(m.upgradeLevels)}] maxTier={m.unlockedMaxTier}");
            if (stopAfterRuns > 0 && runsCompleted >= stopAfterRuns)
            {
                SetEnabled(false);
                return;
            }
            root.RequestStartRun();
        }

        /// <summary>Buys <see cref="ChooseNext"/> until it returns nothing. Returns the number of levels bought.</summary>
        int BuyCheapestUntilBroke()
        {
            int bought = 0;
            for (int guard = 0; guard < 200; guard++)
            {
                var n = ChooseNext(root.nodeTable, root.Meta, gateSaving, scratch);
                if (n == null || !root.TryBuy(n.id)) break;
                bought++;
            }
            return bought;
        }

        /// <summary>
        /// The bot's next purchase, or null to stop buying. Off: the cheapest affordable purchasable node, gate included.
        /// Stop / KeepReserve: the next gate if affordable; else, once the gate costs no more than currency + last run
        /// income, nothing (Stop) or the cheapest node that leaves currency ≥ gate − last run income (KeepReserve);
        /// else the cheapest affordable stat node.
        /// </summary>
        public static NodeDef ChooseNext(NodeTable t, MetaState m, GateSaving saving, List<NodeDef> scratch)
        {
            var gate = Shop.NextGate(t, m);
            if (gate != null && !Shop.IsPurchasable(t, m, gate)) gate = null;
            double gateCost = gate != null ? Shop.Cost(t, m, gate) : double.PositiveInfinity;
            double budget = m.currency;

            if (saving != GateSaving.Off && gate != null)
            {
                if (m.currency >= gateCost) return gate;
                if (gateCost <= m.currency + m.lastRunIncome)
                {
                    if (saving == GateSaving.Stop) return null;
                    budget = m.currency - (gateCost - m.lastRunIncome);
                }
            }

            Shop.PurchasableStatNodes(t, m, scratch);
            NodeDef best = scratch.Count > 0 && budget >= Shop.Cost(t, m, scratch[0]) ? scratch[0] : null;
            if (saving == GateSaving.Off && gate != null && m.currency >= gateCost && (best == null || gateCost < Shop.Cost(t, m, best)))
                best = gate;
            return best;
        }
    }
}
