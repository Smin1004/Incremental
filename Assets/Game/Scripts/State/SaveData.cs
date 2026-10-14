using System;
using System.Collections.Generic;

namespace Incremental
{
    /// <summary>
    /// Contents of save.json (JsonUtility). <see cref="version"/> drives <see cref="SaveMigration"/>.
    /// No NaN anywhere: JsonUtility would write it as invalid JSON. Values that do not exist yet use -1.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        public int version = SaveMigration.CurrentVersion;
        public MetaState meta = new MetaState();
        public PendingRun pending = new PendingRun();
    }

    /// <summary>
    /// Progress of the run in flight, saved every few seconds while a run is going. If the game is killed during a run
    /// this is still active on the next launch, and the run is closed as a crash with its income kept (02 §6).
    /// JsonUtility never deserializes a class field as null, so <see cref="active"/> marks whether it holds a run.
    /// </summary>
    [Serializable]
    public sealed class PendingRun
    {
        public bool active;
        public int run;
        public double elapsed;
        public double income;
        /// <summary>Index = tier - 1.</summary>
        public int[] tierCounts = new int[0];
        /// <summary>Fragments so far, index = tier - 1, and their share of the income (20 §5).</summary>
        public int[] fragmentCounts = new int[0];
        public double fragmentIncome;
        public List<UpgradeLevel> startLevels = new List<UpgradeLevel>();
        public EffectiveStats startStats;
        /// <summary>The run is in its ending (the first blue giant was made, 15 §4.2): if the game dies now, it closes as an ending run.</summary>
        public bool ending;
    }

    /// <summary>Contents of settings.json. Kept apart from save.json so a save reset leaves settings alone.</summary>
    [Serializable]
    public sealed class Settings
    {
        public int version = 1;
        public Notation notation = Notation.Letters;
        /// <summary>Pause the run while the game window is not focused (never while the autoplay bot runs).</summary>
        public bool pauseOnFocusLoss = true;
        /// <summary>
        /// 28 §2.1: one click starts holding and the next click lets go, instead of keeping the button down (for the
        /// wrist). Off by default. The debug key H flips it until the options screen has it.
        /// </summary>
        public bool holdToggle;
    }
}
