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
        /// <summary>1: flat shop (week 2). 2: skill tree nodes (12 §3).</summary>
        public const int CurrentVersion = 2;

        /// <summary>
        /// Versions below this are replaced by a new game instead of being converted: v1 levels were per stat, v2 levels
        /// are per node, and v1 saves only ever existed during development (12 §3, "세이브"). Settings are separate and stay.
        /// </summary>
        public const int FirstKeptVersion = 2;

        public static bool ResetsProgress(int version) => version < FirstKeptVersion;

        public static SaveData Migrate(SaveData d)
        {
            if (d == null) return null;
            if (d.version < 1) d.version = 1;

            // v1 → v2: flat-shop levels cannot be mapped onto nodes; start a new game (statistics included).
            if (d.version == 1)
            {
                Debug.LogWarning("[Save] version 1 save (flat shop) replaced by a new game for the skill tree (version 2).");
                d = new SaveData { version = 2 };
            }

            // Future steps: if (d.version == 2) { ...; d.version = 3; }

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
