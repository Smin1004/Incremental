using System.Text;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// The special abilities of a run (24). Holds one <see cref="Ability"/> per row of the AbilityTable, in the table's
    /// order, and on every fixed tick hands each of them what the skill tree gives it: whether it is unlocked and how
    /// far it is upgraded. GameRoot ticks this right before the dust, so what an ability does to the dust in a tick is
    /// part of that same dust tick.
    ///
    /// The debug keys (24 §7) replace what the tree says without touching the save. The number row picks what is
    /// forced on (0 = nothing, the tree decides; 1..n = that ability alone; n + 1 = all of them) and the minus and
    /// equals keys move the upgrade level of the forced ones. PageUp and PageDown cycle through the same two things.
    ///
    /// With AbilityTable.logEvents on, what the abilities do is also written to the console as "[Ability] …" lines:
    /// which are on when a run starts, every trigger and what it did, and every debug key.
    /// </summary>
    public sealed class AbilitySystem : MonoBehaviour
    {
        GameRoot root;
        Ability[] abilities = new Ability[0];
        bool[] wasOn = new bool[0];
        bool runAnnounced;
        DustForce[] forces = new DustForce[8];
        int forceCount;
        float lastTickAt;
        readonly StringBuilder label = new StringBuilder();

        /// <summary>
        /// 24 §7: 0 = the tree decides; 1..n = only the ability of that row is on, whatever the tree says; n + 1 = all.
        /// </summary>
        public int ForcedMode { get; private set; }
        /// <summary>24 §7: the level every stat node of a forced ability is taken to be at.</summary>
        public int ForcedLevel { get; private set; }

        /// <summary>
        /// What the abilities put on the dust in the tick that was just run (24 §4, §5): the first
        /// <see cref="ForceCount"/> entries. GameRoot hands them to the dust tick that follows.
        /// </summary>
        public DustForce[] Forces => forces;
        public int ForceCount => forceCount;

        /// <summary>An ability adds a force to this tick's dust tick. Only valid from inside an ability's tick.</summary>
        public void AddForce(in DustForce force)
        {
            if (forceCount >= forces.Length) System.Array.Resize(ref forces, forces.Length * 2);
            forces[forceCount++] = force;
        }

        /// <summary>Rows of the table, which is also how many abilities there are. 0 without a table.</summary>
        public int RowCount => root.abilityTable != null ? root.abilityTable.Count : 0;

        public void Init(GameRoot gameRoot)
        {
            root = gameRoot;
            var table = root.abilityTable;
            if (table == null) return;
            var rows = table.Rows;
            abilities = new Ability[rows.Length];
            wasOn = new bool[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                var a = rows[i] != null ? rows[i].CreateRuntime() : null;
                if (a != null) a.Init(root, this, i, transform);
                abilities[i] = a;
            }
            root.RunStarted += ResetRun;
        }

        /// <summary>A new run: nothing is charged and nothing is in flight.</summary>
        public void ResetRun()
        {
            forceCount = 0;
            runAnnounced = false;
            for (int i = 0; i < abilities.Length; i++)
            {
                wasOn[i] = false;
                if (abilities[i] != null) abilities[i].Clear();
            }
        }

        /// <summary>The ability of the table row with this id, or null (captures and checks).</summary>
        public Ability Get(string id)
        {
            int i = root.abilityTable != null ? root.abilityTable.IndexOf(id) : -1;
            return i >= 0 && i < abilities.Length ? abilities[i] : null;
        }

        /// <summary>One fixed tick of a run, called right before the dust tick.</summary>
        public void Tick(double dt, bool holding, in EffectiveStats s)
        {
            lastTickAt = Time.time;
            forceCount = 0;
            if (abilities.Length == 0) return;
            // 24 §8: how long an excited dust stays excited is table data, so the dust is told every tick.
            root.Dust.ExciteSec = root.abilityTable.exciteSec;
            for (int i = 0; i < abilities.Length; i++)
            {
                var level = LevelOf(i);
                // After the first tick of a run, an ability switching on or off can only be the debug keys (or the
                // free tier unlock), and is worth a line of its own. The first tick is covered by the run's summary.
                if (level.unlocked != wasOn[i])
                {
                    wasOn[i] = level.unlocked;
                    if (runAnnounced) Log(level.unlocked ? Describe(i, level) + " is now on" : root.abilityTable.Rows[i].id + " is now off");
                }
                if (abilities[i] != null) abilities[i].Tick(dt, holding, s, level);
            }
            if (!runAnnounced)
            {
                runAnnounced = true;
                Log("run " + root.Meta.runCount + " starts with " + Summary());
            }
        }

        /// <summary>What row <paramref name="i"/> is given now: by the tree, or by the debug key when it is forcing (24 §7).</summary>
        AbilityLevel LevelOf(int i)
        {
            string id = root.abilityTable.Rows[i].id;
            if (ForcedMode == 0) return Stats.AbilityFromTree(root.nodeTable, root.Meta, id);
            return IsForced(i) ? Stats.AbilityAt(root.nodeTable, id, ForcedLevel) : default;
        }

        bool IsForced(int i) => ForcedMode == RowCount + 1 || ForcedMode == i + 1;

        void LateUpdate()
        {
            bool run = root.Phase == GamePhase.Run && root.Run != null && !root.EndingActive;
            // The abilities move on the fixed tick; drawing them a fraction of a tick ahead is what makes a fast ring or a
            // meteor glide instead of stepping. While paused no tick comes, so there is nothing to run ahead of.
            float lead = run && !root.Paused ? Mathf.Clamp(Time.time - lastTickAt, 0f, Time.fixedDeltaTime) : 0f;
            for (int i = 0; i < abilities.Length; i++)
            {
                var a = abilities[i];
                if (a != null) a.Render(run && a.Level.unlocked, lead);
            }
        }

        // ---------------- console log ----------------

        /// <summary>
        /// One "[Ability] …" line in the console, when AbilityTable.logEvents is on. The prototype is checked by eye,
        /// and the log says what the eye may have missed: that an ability fired at all, and what it did.
        /// </summary>
        public void Log(string message)
        {
            if (root.abilityTable != null && root.abilityTable.logEvents) Debug.Log("[Ability] " + message);
        }

        /// <summary>"implode Lv1 (tree)" / "implode (locked)": one ability, its level and who turned it on.</summary>
        string Describe(int i, in AbilityLevel level)
        {
            string id = root.abilityTable.Rows[i].id;
            if (!level.unlocked) return id + " (locked)";
            return id + " Lv" + level.level + (ForcedMode == 0 ? " (tree)" : " (debug key)");
        }

        /// <summary>Every ability in table order, as <see cref="Describe"/> writes them.</summary>
        string Summary()
        {
            label.Length = 0;
            for (int i = 0; i < RowCount; i++)
            {
                if (i > 0) label.Append(", ");
                label.Append(Describe(i, LevelOf(i)));
            }
            return label.ToString();
        }

        // ---------------- debug keys (24 §7) ----------------

        /// <summary>
        /// The number row: 0 = nothing is forced and the tree decides, 1..n = only the ability of that row, n + 1 = all
        /// of them. A number past that does nothing. A forced ability is charged at once, so holding shows it without
        /// waiting out its cooldown. Nothing is saved.
        /// </summary>
        public void SetForced(int mode)
        {
            int n = RowCount;
            if (n == 0 || mode < 0 || mode > n + 1) return;
            ForcedMode = mode;
            ForcedLevel = Mathf.Min(ForcedLevel, ForcedMaxLevel());
            AnnounceForced();
        }

        /// <summary>PageUp: the same choices in a cycle, off → each ability alone → all → off.</summary>
        public void CycleForced()
        {
            int n = RowCount;
            if (n > 0) SetForced((ForcedMode + 1) % (n + 2));
        }

        /// <summary>
        /// The minus and equals keys: the upgrade level of the forced abilities one down or one up, between 0 and the
        /// most levels their nodes have.
        /// </summary>
        public void StepForcedLevel(int delta)
        {
            if (!CanSetLevel()) return;
            ForcedLevel = Mathf.Clamp(ForcedLevel + delta, 0, ForcedMaxLevel());
            AnnounceForced();
        }

        /// <summary>PageDown: the same level in a cycle, 0 → 1 → … → the most the nodes have → 0.</summary>
        public void CycleForcedLevel()
        {
            if (!CanSetLevel()) return;
            ForcedLevel = ForcedLevel >= ForcedMaxLevel() ? 0 : ForcedLevel + 1;
            AnnounceForced();
        }

        /// <summary>The level keys only mean something while an ability is forced; otherwise they say how to force one.</summary>
        bool CanSetLevel()
        {
            if (RowCount == 0) return false;
            if (ForcedMode != 0) return true;
            root.Hud.ShowToast(string.Format(UIStrings.AbilityForceFirst, RowCount + 1));
            return false;
        }

        /// <summary>The most levels a stat node of any forced ability has: where the level keys stop.</summary>
        int ForcedMaxLevel()
        {
            int max = 0;
            var rows = root.abilityTable.Rows;
            for (int i = 0; i < rows.Length; i++)
                if (IsForced(i)) max = Mathf.Max(max, Stats.AbilityMaxLevel(root.nodeTable, rows[i].id));
            return max;
        }

        /// <summary>After a debug key: charge what is forced, and say what is forced on screen and in the console.</summary>
        void AnnounceForced()
        {
            for (int i = 0; i < abilities.Length; i++)
                if (abilities[i] != null && ForcedMode != 0 && IsForced(i)) abilities[i].FillCharge();
            root.Hud.ShowToast(ForcedToast());
            Log("debug key: " + (ForcedMode == 0 ? "nothing forced, the tree decides: " : "forced, level " + ForcedLevel + ": ") + Summary());
        }

        string ForcedToast()
        {
            if (ForcedMode == 0) return UIStrings.AbilityForceOff;
            if (ForcedMode == RowCount + 1) return string.Format(UIStrings.AbilityForceAll, ForcedLevel);
            return string.Format(UIStrings.AbilityForceOne, UIStrings.AbilityName(root.abilityTable.Rows[ForcedMode - 1].id), ForcedLevel);
        }

        /// <summary>
        /// The small status line in the corner of the screen (24 §7): the abilities that are on, each with its level and
        /// the seconds of holding left to its next trigger. Shown while the debug keys are forcing, and with the F1
        /// overlay also for what the tree gives, followed by which keys do what; empty otherwise.
        /// </summary>
        public string DebugLabel(bool overlay)
        {
            if (RowCount == 0 || (ForcedMode == 0 && !overlay)) return string.Empty;
            label.Length = 0;
            var rows = root.abilityTable.Rows;
            for (int i = 0; i < rows.Length; i++)
            {
                var level = LevelOf(i);
                if (!level.unlocked) continue;
                if (label.Length > 0) label.Append(UIStrings.AbilityDebugSeparator);
                label.AppendFormat(UIStrings.AbilityDebugEntry, UIStrings.AbilityName(rows[i].id), level.level);
                if (i < abilities.Length && abilities[i] != null && rows[i].cooldownSec > 0)
                    label.AppendFormat(UIStrings.AbilityDebugCharge, abilities[i].SecondsToTrigger(level));
            }
            string list = label.Length > 0 ? label.ToString() : UIStrings.AbilityDebugNone;
            string line = string.Format(ForcedMode == 0 ? UIStrings.AbilityDebugTree : UIStrings.AbilityDebugForced, list);
            return overlay ? line + string.Format(UIStrings.AbilityDebugHint, rows.Length, rows.Length + 1) : line;
        }
    }
}
