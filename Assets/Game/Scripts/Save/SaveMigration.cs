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
        /// <summary>1: flat shop (week 2). 2: skill tree nodes (12 §3). 3: the dust kind axis (20 §4).</summary>
        public const int CurrentVersion = 3;

        /// <summary>
        /// Versions below this are replaced by a new game instead of being converted: v1 levels were per stat, v2 levels
        /// are per node, and v2 node ids on the 60° axis (the compound dust mass nodes) no longer exist, so a v2 save
        /// would keep paying for growth the tree no longer has (20 §4). Both only ever existed during development
        /// (12 §3, "세이브"). Settings are separate and stay.
        /// </summary>
        public const int FirstKeptVersion = 3;

        public static bool ResetsProgress(int version) => version < FirstKeptVersion;

        public static SaveData Migrate(SaveData d)
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

            // Future steps: if (d.version == 3) { ...; d.version = 4; }

            if (d.version > CurrentVersion)
                Debug.LogWarning($"[Save] save version {d.version} is newer than this build ({CurrentVersion}); loading what is known.");

            Normalize(d);
            return d;
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
            if (m.runHistory == null) m.runHistory = new List<RunRecord>();
            if (m.unlockedMaxTier < 1) m.unlockedMaxTier = 1;
            if (m.runCount < 0) m.runCount = 0;
            if (d.pending.tierCounts == null) d.pending.tierCounts = new int[0];
            if (d.pending.startLevels == null) d.pending.startLevels = new List<UpgradeLevel>();
        }
    }
}
