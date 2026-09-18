using System.Collections.Generic;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// Upgrades a loaded SaveData to <see cref="CurrentVersion"/>, one step per version, then fills in anything the
    /// JSON did not have. Version 1 is the first format, so the chain has no steps yet.
    /// Upgrade ids are never removed: ids unknown to the table are kept and ignored, ids new to the table read as level 0.
    /// </summary>
    public static class SaveMigration
    {
        public const int CurrentVersion = 1;

        public static SaveData Migrate(SaveData d)
        {
            if (d == null) return null;
            if (d.version < 1) d.version = 1;

            // Add steps here when the format changes, e.g.
            // if (d.version == 1) { MigrateV1ToV2(d); d.version = 2; }

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
