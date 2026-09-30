using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// One ability while the game runs (24): what it does on the fixed tick and what it draws. Its numbers are its row
    /// of the AbilityTable, read through the table every time, so a value changed in the inspector is in effect on the
    /// next tick. The charging rule is the same for all of them and lives here: the cooldown only runs while holding,
    /// and the ability fires by itself when it is full (24 §1).
    /// </summary>
    public abstract class Ability
    {
        protected GameRoot root;
        protected GameParams p;
        protected AbilitySystem system;
        int index;

        /// <summary>This ability's row of the table.</summary>
        public AbilityDef Def => root.abilityTable.Rows[index];
        /// <summary>What the tree, or the debug key, gives it as of the last tick (24 §7, §8).</summary>
        public AbilityLevel Level { get; private set; }
        /// <summary>Seconds of holding since the last trigger.</summary>
        public double Charge { get; private set; }

        public void Init(GameRoot gameRoot, AbilitySystem abilitySystem, int rowIndex, Transform parent)
        {
            root = gameRoot;
            p = root.gameParams;
            system = abilitySystem;
            index = rowIndex;
            Build(parent);
        }

        /// <summary>
        /// One fixed tick of a run. A <paramref name="level"/> that is not unlocked switches the ability off, which ends
        /// whatever it was doing.
        /// </summary>
        public void Tick(double dt, bool holding, in EffectiveStats s, in AbilityLevel level)
        {
            bool was = Level.unlocked;
            Level = level;
            if (!level.unlocked)
            {
                if (was) Clear();
                return;
            }
            double cooldown = Def.Cooldown(level, root.abilityTable.maxCooldownReduction);
            if (cooldown > 0)
            {
                if (holding) Charge += dt;
                if (Charge >= cooldown && Ready)
                {
                    Charge = 0;
                    Trigger(s);
                }
            }
            Step(dt, holding, s);
        }

        /// <summary>Run start, or the ability was switched off: its effects end at once and the charge is lost.</summary>
        public void Clear()
        {
            Level = default;
            Charge = 0;
            OnClear();
        }

        /// <summary>The debug key (24 §7): the next held tick fires it, so there is no waiting out a cooldown to see it.</summary>
        public void FillCharge() => Charge = Math.Max(Charge, Def.cooldownSec);

        /// <summary>Seconds of holding left until it fires, or 0 for an ability without a cooldown.</summary>
        public double SecondsToTrigger(in AbilityLevel level) =>
            Math.Max(0.0, Def.Cooldown(level, root.abilityTable.maxCooldownReduction) - Charge);

        /// <summary>Seconds since the trigger that is playing out now started, or −1 when none is (captures and checks).</summary>
        public virtual float EffectAge => -1f;

        /// <summary>A console line about this ability, "[Ability] &lt;id&gt;: …", when AbilityTable.logEvents is on.</summary>
        protected void Log(string message) => system.Log(Def.id + ": " + message);

        /// <summary>Seconds into the run, for the log lines.</summary>
        protected string RunClock => root.Run != null ? root.Run.elapsed.ToString("F1") + "s" : "-";

        /// <summary>Creates what the ability draws, hidden, under <paramref name="parent"/>.</summary>
        protected abstract void Build(Transform parent);

        /// <summary>False while one trigger is still playing out; a full charge then waits for it.</summary>
        protected virtual bool Ready => true;

        /// <summary>The cooldown filled up while holding.</summary>
        protected virtual void Trigger(in EffectiveStats s) { }

        /// <summary>The ability's part of a fixed tick, after the charge. Game logic only.</summary>
        protected abstract void Step(double dt, bool holding, in EffectiveStats s);

        protected abstract void OnClear();

        /// <summary>
        /// Once a frame. <paramref name="show"/> is false whenever nothing of it may be on screen (between runs, during the
        /// ending, locked). <paramref name="lead"/> is the time since the last fixed tick, for drawing a moving thing
        /// where it is now rather than where the tick left it.
        /// </summary>
        public abstract void Render(bool show, float lead);
    }
}
