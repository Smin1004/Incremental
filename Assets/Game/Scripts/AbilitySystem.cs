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
    /// The debug keys (24 §7) replace what the tree says without touching the save: PageUp forces abilities on, one at a
    /// time and then all of them, and PageDown cycles the upgrade level of the forced ones.
    /// </summary>
    public sealed class AbilitySystem : MonoBehaviour
    {
        GameRoot root;
        Ability[] abilities = new Ability[0];
        float lastTickAt;
        readonly StringBuilder label = new StringBuilder();

        /// <summary>
        /// 24 §7: 0 = the tree decides; 1..n = only the ability of that row is on, whatever the tree says; n + 1 = all.
        /// </summary>
        public int ForcedMode { get; private set; }
        /// <summary>24 §7: the level every stat node of a forced ability is taken to be at.</summary>
        public int ForcedLevel { get; private set; }

        int RowCount => root.abilityTable != null ? root.abilityTable.Count : 0;

        public void Init(GameRoot gameRoot)
        {
            root = gameRoot;
            var table = root.abilityTable;
            if (table == null) return;
            var rows = table.Rows;
            abilities = new Ability[rows.Length];
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
            for (int i = 0; i < abilities.Length; i++)
                if (abilities[i] != null) abilities[i].Clear();
        }

        /// <summary>One fixed tick of a run, called right before the dust tick.</summary>
        public void Tick(double dt, bool holding, in EffectiveStats s)
        {
            lastTickAt = Time.time;
            for (int i = 0; i < abilities.Length; i++)
                if (abilities[i] != null) abilities[i].Tick(dt, holding, s, LevelOf(i));
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

        // ---------------- debug keys (24 §7) ----------------

        /// <summary>
        /// PageUp: off (the tree decides) → the first ability only → … → the last only → all of them → off. A forced
        /// ability is charged at once, so holding shows it without waiting for its cooldown. Nothing is saved.
        /// </summary>
        public void CycleForced()
        {
            int n = RowCount;
            if (n == 0) return;
            ForcedMode = (ForcedMode + 1) % (n + 2);
            ForcedLevel = Mathf.Min(ForcedLevel, ForcedMaxLevel());
            PrimeForced();
            root.Hud.ShowToast(ForcedToast());
            Debug.Log("[Debug] abilities forced: mode " + ForcedMode + ", level " + ForcedLevel);
        }

        /// <summary>PageDown: the upgrade level of the forced abilities, 0 → 1 → … → the most their nodes have → 0.</summary>
        public void CycleForcedLevel()
        {
            if (RowCount == 0) return;
            if (ForcedMode == 0)
            {
                root.Hud.ShowToast(UIStrings.AbilityForceFirst);
                return;
            }
            ForcedLevel = ForcedLevel >= ForcedMaxLevel() ? 0 : ForcedLevel + 1;
            PrimeForced();
            root.Hud.ShowToast(ForcedToast());
            Debug.Log("[Debug] abilities forced: mode " + ForcedMode + ", level " + ForcedLevel);
        }

        /// <summary>The most levels a stat node of any forced ability has: where PageDown wraps around.</summary>
        int ForcedMaxLevel()
        {
            int max = 0;
            var rows = root.abilityTable.Rows;
            for (int i = 0; i < rows.Length; i++)
                if (IsForced(i)) max = Mathf.Max(max, Stats.AbilityMaxLevel(root.nodeTable, rows[i].id));
            return max;
        }

        void PrimeForced()
        {
            for (int i = 0; i < abilities.Length; i++)
                if (abilities[i] != null && ForcedMode != 0 && IsForced(i)) abilities[i].FillCharge();
        }

        string ForcedToast()
        {
            if (ForcedMode == 0) return UIStrings.AbilityForceOff;
            if (ForcedMode == RowCount + 1) return string.Format(UIStrings.AbilityForceAll, ForcedLevel);
            return string.Format(UIStrings.AbilityForceOne, UIStrings.AbilityName(root.abilityTable.Rows[ForcedMode - 1].id), ForcedLevel);
        }

        /// <summary>
        /// The small status line in the corner of the screen (24 §7): the abilities that are on, each with its level and
        /// the seconds of holding left to its next trigger. Shown while the debug key is forcing, and with the F1 overlay
        /// also for what the tree gives; empty otherwise.
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
            return string.Format(ForcedMode == 0 ? UIStrings.AbilityDebugTree : UIStrings.AbilityDebugForced, list);
        }
    }
}
