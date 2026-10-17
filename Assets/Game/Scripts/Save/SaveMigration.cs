using System.Collections.Generic;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// Upgrades a loaded SaveData to <see cref="CurrentVersion"/>, one step per version, then fills in anything the
    /// JSON did not have. Node ids are never removed: ids unknown to the table are kept and ignored, new ids read as level 0.
    /// </summary>
    public static class SaveMigration
    {
        /// <summary>
        /// 1: flat shop (week 2). 2: skill tree nodes (12 §3). 3: the dust kind axis (20 §4). 4: time-based runs (28 §2):
        /// the stamina fields are gone from the run stats and a run that ran out ends as "time" instead of "stamina".
        /// 5: skill tree v2 (29, Q9): the tree is reset and what was spent on it is given back.
        /// </summary>
        public const int CurrentVersion = 5;

        /// <summary>29 Q9: saves below this hold tree v1 levels, which v5 resets (and refunds).</summary>
        public const int FirstTreeV2Version = 5;

        /// <summary>True when migrating this version resets the skill tree (SaveStore keeps a copy of the file first).</summary>
        public static bool ResetsTree(int version) => version >= FirstKeptVersion && version < FirstTreeV2Version;

        /// <summary>
        /// Versions below this are replaced by a new game instead of being converted: v1 levels were per stat, v2 levels
        /// are per node, and v2 node ids on the 60° axis (the compound dust mass nodes) no longer exist, so a v2 save
        /// would keep paying for growth the tree no longer has (20 §4). Both only ever existed during development
        /// (12 §3, "세이브"). Settings are separate and stay.
        /// </summary>
        public const int FirstKeptVersion = 3;

        public static bool ResetsProgress(int version) => version < FirstKeptVersion;

        /// <param name="treeV1">
        /// The v1 skill tree (Data/NodeTable_v1.asset): what the v4 node levels cost, for the v5 refund. Without it the
        /// tree is still reset, and nothing is given back.
        /// </param>
        public static SaveData Migrate(SaveData d, NodeTable treeV1 = null)
        {
            if (d == null) return null;
            if (d.version < 1) d.version = 1;

            // v1 (flat shop) and v2 (node ids of the pre-20 §4 tree) cannot be mapped onto the current tree, so they are
            // replaced by a new game, statistics included.
            if (ResetsProgress(d.version))
            {
                Debug.LogWarning($"[Save] version {d.version} save (upgrade ids the current tree no longer has) " +
                                 $"replaced by a new game, version {CurrentVersion}.");
                d = new SaveData { version = CurrentVersion };
            }

            if (d.version == 3)
            {
                ToV4(d);
                d.version = 4;
            }
            if (d.version == 4)
            {
                ToV5(d, treeV1);
                d.version = 5;
            }
            // Future steps: if (d.version == 5) { ...; d.version = 6; }

            if (d.version > CurrentVersion)
                Debug.LogWarning($"[Save] save version {d.version} is newer than this build ({CurrentVersion}); loading what is known.");

            Normalize(d);
            return d;
        }

        /// <summary>
        /// v3 → v4 (28 §2). The node levels stay as they are: the stamina_max and stamina_drain nodes kept their ids and
        /// now add run time. Runs in the history that ended on stamina are renamed to the time ending, so they still count
        /// as full runs (ratio base, best income). The stamina numbers in their run start stats are simply not read any
        /// more. A run left pending is closed the usual way on load (a crash with its income kept), under the new rules.
        /// </summary>
        static void ToV4(SaveData d)
        {
            int renamed = 0;
            if (d.meta != null && d.meta.runHistory != null)
                foreach (var r in d.meta.runHistory)
                    if (r != null && r.endReason == EndReason.LegacyStamina)
                    {
                        r.endReason = EndReason.TimeUp;
                        renamed++;
                    }
            // 28 §4: the play clock did not exist; the run time is the closest thing the save has.
            if (d.meta != null && d.meta.playClockSec <= 0) d.meta.playClockSec = d.meta.totalPlayTimeSec;
            Debug.Log($"[Save] v3 -> v4 (time-based runs, 28 §2): {renamed} stamina-ended runs in the history renamed to \"{EndReason.TimeUp}\"");
        }

        /// <summary>
        /// v4 → v5 (29 Q9): skill tree v2. Every node level of the v1 tree is given back at what it cost, gates included,
        /// priced with the v1 table (its ring cost multipliers too), and the tree starts empty: the tiers unlocked
        /// follow the gate levels, so the player is back to ring 1 with the money to buy the new tree up again. Records,
        /// history, totals and the ending stay. Ids the v1 table does not know (none in a real save) refund nothing.
        /// </summary>
        public static double RefundV1(MetaState m, NodeTable treeV1, out int levels)
        {
            levels = 0;
            double refund = 0;
            if (m == null || m.upgradeLevels == null || treeV1 == null) return 0;
            foreach (var l in m.upgradeLevels)
            {
                if (l == null || l.level <= 0) continue;
                var n = treeV1.Get(l.id);
                if (n == null) continue;
                int owned = n.maxLevel > 0 ? System.Math.Min(l.level, n.maxLevel) : l.level;
                for (int k = 0; k < owned; k++) refund += Stats.CostCeil(treeV1, n, k);
                levels += owned;
            }
            return refund;
        }

        static void ToV5(SaveData d, NodeTable treeV1)
        {
            var m = d.meta;
            if (m == null) return;
            double before = m.currency;
            double refund = RefundV1(m, treeV1, out int levels);
            int nodes = m.upgradeLevels != null ? m.upgradeLevels.Count : 0;
            m.currency += refund;
            if (m.upgradeLevels != null) m.upgradeLevels.Clear();
            m.unlockedMaxTier = 1;
            // A run left pending started under the old tree; its start levels name v1 nodes and are only a record.
            if (treeV1 == null)
                Debug.LogWarning("[Save] v4 -> v5 (skill tree v2, 29): no v1 tree table to price the refund with; the tree is reset and nothing is given back.");
            Debug.Log($"[Save] v4 -> v5 (skill tree v2, 29 Q9): tree reset, {nodes} nodes / {levels} levels refunded for " +
                      $"{refund:F0} ({Fmt.Num(refund)}); currency {Fmt.Num(before)} -> {Fmt.Num(m.currency)}");
        }

        /// <summary>Replaces missing collections with empty ones and clamps impossible values.</summary>
        static void Normalize(SaveData d)
        {
            if (d.meta == null) d.meta = new MetaState();
            if (d.pending == null) d.pending = new PendingRun();
            var m = d.meta;
            if (m.upgradeLevels == null) m.upgradeLevels = new List<UpgradeLevel>();
            m.upgradeLevels.RemoveAll(l => l == null || string.IsNullOrEmpty(l.id));
            if (m.totalPlanets == null) m.totalPlanets = new int[0];
            if (m.totalFragments == null) m.totalFragments = new int[0];
            if (m.runHistory == null) m.runHistory = new List<RunRecord>();
            if (m.unlockedMaxTier < 1) m.unlockedMaxTier = 1;
            if (m.runCount < 0) m.runCount = 0;
            if (d.pending.tierCounts == null) d.pending.tierCounts = new int[0];
            if (d.pending.fragmentCounts == null) d.pending.fragmentCounts = new int[0];
            if (d.pending.startLevels == null) d.pending.startLevels = new List<UpgradeLevel>();
        }
    }
}
