using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// F4 autoplay bot for unattended measurement runs. Simple rules only:
    /// during a run, move the cursor toward the densest dust cell and hold; release once the goal tier
    /// (highest unlocked tier) threshold is reached; between runs buy the cheapest affordable item until
    /// nothing is affordable, then start the next run. Drives the game through GameRoot.InputProvider.
    /// </summary>
    public sealed class AutoplayBot : MonoBehaviour
    {
        [Tooltip("Cursor speed in screen pixels per second.")]
        public double moveSpeedPx = 250;
        public int gridX = 12;
        public int gridY = 7;
        [Tooltip("Stop the bot after this many completed runs (0 = keep going).")]
        public int stopAfterRuns = 0;

        GameRoot root;
        Vector2 cursor;
        bool pressed;
        int[] counts = new int[0];
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
            Debug.Log($"[Bot] run {rec.run} income={Fmt.Int(rec.income)} bought={bought} currency={Fmt.Int(m.currency)} levels=[{string.Join(",", m.upgradeLevels)}] maxTier={m.unlockedMaxTier}");
            if (stopAfterRuns > 0 && runsCompleted >= stopAfterRuns)
            {
                SetEnabled(false);
                return;
            }
            root.RequestStartRun();
        }

        /// <summary>Cheapest affordable item first (upgrades and unlocks together), repeated until nothing is affordable.</summary>
        int BuyCheapestUntilBroke()
        {
            var t = root.upgradeTable;
            var m = root.Meta;
            int bought = 0;
            for (int guard = 0; guard < 100; guard++)
            {
                double best = double.PositiveInfinity;
                bool isUnlock = false;
                UpgradeId bestId = UpgradeId.SpawnRate;
                int bestTier = 0;
                for (int i = 0; i < UpgradeTable.UpgradeCount; i++)
                {
                    var id = (UpgradeId)i;
                    if (!Shop.CanBuyUpgrade(t, m, id)) continue;
                    double c = Shop.UpgradeCost(t, m, id);
                    if (c < best) { best = c; isUnlock = false; bestId = id; }
                }
                foreach (var u in t.unlocks)
                {
                    if (!Shop.CanUnlock(t, m, u.tier)) continue;
                    double c = Shop.UnlockCost(t, u.tier);
                    if (c < best) { best = c; isUnlock = true; bestTier = u.tier; }
                }
                if (double.IsPositiveInfinity(best)) break;
                bool ok = isUnlock ? root.TryUnlock(bestTier) : root.TryBuyUpgrade(bestId);
                if (!ok) break;
                bought++;
            }
            return bought;
        }
    }
}
