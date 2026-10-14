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
    /// during a run, move the cursor toward the densest dust cell and hold (27 §1.1 P1: or toward an amplifier crystal
    /// on screen that it can still reach before it runs out, then back to the dust once it is drawn in); release once the goal tier
    /// (highest unlocked tier) threshold is reached; between runs buy purchasable skill tree nodes, cheapest first,
    /// until nothing is affordable, then start the next run. Drives the game through GameRoot.InputProvider.
    /// <see cref="gateSaving"/> (12 §3): the next gate is bought first when affordable, and the bot saves for it once the
    /// gate costs no more than currency + last run income, instead of spending everything on cheap nodes
    /// (week 1 report: the bot never unlocked tier 2).
    /// Completion (15 §6): right after buying a gate the save is copied to snapshots/ring_k.json. When the ending run ends
    /// the bot skips the sequence as soon as it can (after the blast), continues, plays stopAfterEndlessRuns endless runs
    /// and stops.
    /// </summary>
    public sealed class AutoplayBot : MonoBehaviour
    {
        [Tooltip("Cursor speed in screen pixels per second.")]
        public double moveSpeedPx = 250;
        public int gridX = 12;
        public int gridY = 7;
        [Tooltip("Stop the bot after this many completed runs (0 = keep going).")]
        public int stopAfterRuns = 0;
        [Tooltip("After the ending: play this many endless runs, then stop (0 = keep going), 15 §6.1.")]
        public int stopAfterEndlessRuns = 1;
        [Tooltip("Copy the save to snapshots/ring_k.json right after buying the gate of ring k (15 §6.2).")]
        public bool writeRingSnapshots = true;
        [Tooltip("Stop: buy the next gate first and buy nothing else once currency + last run income would pay for it (12 §3). " +
                 "KeepReserve (default, 14 §8): while saving, still spend what exceeds gate − last run income.")]
        public GateSaving gateSaving = GateSaving.KeepReserve;
        [Tooltip("27 §1.1 P1: go for an amplifier crystal when it can be reached before it runs out (crystal resonance too).")]
        public bool chaseCrystals = true;
        [Tooltip("Seconds of a crystal's life the bot keeps in hand when deciding whether it can still reach it.")]
        public double chaseMarginSec = 0.3;

        GameRoot root;
        Vector2 cursor;
        bool pressed;
        int[] counts = new int[0];
        readonly List<NodeDef> scratch = new List<NodeDef>();
        int runsCompleted;
        int endlessLeft = -1;
        bool waitingForEnding;
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
                endlessLeft = -1;
                waitingForEnding = false;
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

        /// <summary>The ending sequence: skip after the blast, then continue and go on with the next (endless) run.</summary>
        void Update()
        {
            if (!Enabled || !waitingForEnding) return;
            var ending = root.Ending;
            if (ending == null || !ending.IsPlaying)
            {
                waitingForEnding = false;
                return;
            }
            if (ending.CanSkip) ending.Skip();
            if (!ending.AtEnd) return;
            ending.Continue();
            waitingForEnding = false;
            endlessLeft = stopAfterEndlessRuns;
            Debug.Log("[Bot] ending skipped; continuing into endless play");
            BuyCheapestUntilBroke();
            root.RequestStartRun();
        }

        void FixedUpdate()
        {
            if (!Enabled || root.Phase != GamePhase.Run || root.Run == null)
            {
                pressed = false;
                return;
            }

            float step = (float)moveSpeedPx * Time.fixedDeltaTime;
            Vector2 target;
            if (!(chaseCrystals && CrystalTarget(out target))) target = DensestCellScreen();
            Vector2 d = target - cursor;
            if (d.magnitude > step) cursor += d.normalized * step; else cursor = target;

            // Hold; release once the goal tier's threshold is reached. The game already creates the top
            // unlocked tier by itself, so this only matters if that rule changes.
            var goal = root.celestialTable.Get(root.Meta.unlockedMaxTier);
            bool reached = goal != null && root.Run.mass >= root.Threshold(goal);
            pressed = !reached;
        }

        CrystalAbility crystal;
        bool chasing;

        /// <summary>
        /// 27 §1.1 P1: the screen position of the waiting crystal nearest the gravity source, when the gravity source can
        /// get it inside its pull radius before the crystal runs out: the distance left over the slower of the bot's
        /// cursor and the gravity source's follow speed, plus chaseMarginSec. False otherwise (then the dust as before).
        /// </summary>
        bool CrystalTarget(out Vector2 screen)
        {
            screen = Vector2.zero;
            if (crystal == null && root.Abilities != null) crystal = root.Abilities.Get("crystal") as CrystalAbility;
            if (crystal == null) return Chasing(false);
            Vector2 g = root.GravityWorld;
            if (!crystal.NearestWaiting(g, out var pos, out double lifeLeft, out float pullRadius)) return Chasing(false);
            // World units per screen pixel, to compare the bot's cursor speed with the follow speed.
            float worldPerPx = 2f * root.Cam.orthographicSize / Mathf.Max(1, root.Cam.pixelHeight);
            double speed = moveSpeedPx * worldPerPx;
            if (root.FollowEnabled) speed = Math.Min(speed, root.FollowSpeedPx / root.gameParams.pixelsPerUnit);
            double gap = Math.Max(0.0, (pos - g).magnitude - pullRadius);
            if (gap / Math.Max(1e-6, speed) + chaseMarginSec > lifeLeft) return Chasing(false);
            Vector3 sp = root.Cam.WorldToScreenPoint(new Vector3(pos.x, pos.y, 0f));
            screen = new Vector2(sp.x, sp.y);
            return Chasing(true);
        }

        bool Chasing(bool on)
        {
            if (on != chasing && root.abilityTable != null && root.abilityTable.logEvents)
                Debug.Log(on ? "[Bot] going for the crystal" : "[Bot] back to the dust");
            chasing = on;
            return on;
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
                // Dust in the band outside the screen is not somewhere the pointer can go.
                if (!area.Contains(p)) continue;
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
            if (rec.endReason == EndReason.Ending)
            {
                Debug.Log($"[Bot] run {rec.run} reached the ending (income={Fmt.Num(rec.income)}); waiting for the sequence");
                waitingForEnding = true;
                return;
            }
            int bought = BuyCheapestUntilBroke();
            var m = root.Meta;
            Debug.Log($"[Bot] run {rec.run} income={Fmt.Num(rec.income)} bought={bought} currency={Fmt.Num(m.currency)} levels=[{RunLogger.LevelsText(m.upgradeLevels)}] maxTier={m.unlockedMaxTier}");
            if (endlessLeft > 0 && --endlessLeft == 0 && stopAfterEndlessRuns > 0)
            {
                Debug.Log("[Bot] endless run done; stopping (15 §6.1)");
                SetEnabled(false);
                return;
            }
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
                if (n.IsGate && writeRingSnapshots) root.WriteRingSnapshot(root.Meta.unlockedMaxTier);
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
