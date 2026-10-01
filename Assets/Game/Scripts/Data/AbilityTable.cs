using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// What the skill tree gives one ability (24 §8): whether its unlock node is owned, and the sums of its stat nodes
    /// (effect × level, as for every other stat). The debug key builds the same thing for a level of its own (24 §7).
    /// An ability reads the sums that mean something to it and ignores the rest.
    /// </summary>
    public struct AbilityLevel
    {
        public bool unlocked;
        /// <summary>Levels counted over the ability's stat nodes. For the debug label only.</summary>
        public int level;
        /// <summary>ability_power: a share of the ability's strength, 0.25 = +25%.</summary>
        public double power;
        /// <summary>ability_cooldown: the share taken off its cooldown, 0.1 = −10%.</summary>
        public double cooldown;
        /// <summary>ability_count: whole units of whatever it has a number of.</summary>
        public double count;
        /// <summary>ability_amount: percentage points as a fraction, 0.03 = +3%p.</summary>
        public double amount;
        /// <summary>ability_range: steps of reach (24 §16.1).</summary>
        public double range;
        /// <summary>
        /// 24 §16.1: every ability_range node of the ability is at its last level. The rule "the last level is the whole
        /// screen" hangs on this and not on a level number, so it still holds when the nodes are given more levels.
        /// </summary>
        public bool rangeMaxed;
        /// <summary>
        /// 24 §17.1: how far along its range nodes the ability is, 0..1: the steps it has over the steps the tree can
        /// give in all. 0 with only the unlock, 1 at the last level. The reach is spread over this, so giving the nodes
        /// more levels divides the same span into more, smaller steps.
        /// </summary>
        public double rangeShare;
    }

    /// <summary>
    /// One row of the ability table (24 §8): the columns every ability has. The numbers only one ability needs are in
    /// its own class below, so the inspector shows each ability with exactly its own values.
    /// </summary>
    [Serializable]
    public abstract class AbilityDef
    {
        [Tooltip("Stable key: NodeDef.abilityId of its tree nodes, and the string keys ability.<id> and ability.<id>.effect.")]
        public string id = string.Empty;
        [Tooltip("The ring holding the unlock node. Validate Data checks the tree against it.")]
        public int unlockRing;
        [Tooltip("Seconds of holding between two triggers. The clock only runs while holding (24 §1). " +
                 "0 = nothing to trigger: the ability is simply there while holding.")]
        public double cooldownSec;

        /// <summary>The cooldown after the ability_cooldown nodes, which can take off <paramref name="maxReduction"/> at most.</summary>
        public double Cooldown(in AbilityLevel lv, double maxReduction) =>
            cooldownSec * (1.0 - Math.Min(Math.Max(lv.cooldown, 0.0), maxReduction));

        /// <summary>True when this ability reads that ability stat. Validate Data warns about a node raising one it does not.</summary>
        public abstract bool Uses(string statId);

        /// <summary>
        /// The ability's own rules for Validate Data. <paramref name="full"/> is the ability with every one of its stat
        /// nodes at full level, which is what the ceilings are checked against.
        /// </summary>
        public abstract void Check(in AbilityLevel full, CelestialTable c, ValidationReport r);

        /// <summary>
        /// The object that runs this ability during a run, or null for a row that does nothing yet. The row names its
        /// own runtime so that nothing else has to know the list of abilities.
        /// </summary>
        public virtual Ability CreateRuntime() => null;

        protected string Where => "AbilityTable: '" + id + "'";
    }

    /// <summary>
    /// 24 §3, §16.1, §17.1 수축. Every cooldown a ring spreads from the gravity source, and each dust it passes gets
    /// one push toward the gravity source, whether the gravity reaches that dust or not. How far the ring goes is what
    /// the upgrade raises: a share of the screen at first, more with every level, and the whole screen at the last.
    /// The reach is measured against the screen and not the gravity radius, which grows much faster than the screen
    /// does over the game and used to swallow the steps.
    /// </summary>
    [Serializable]
    public sealed class ImplodeDef : AbilityDef
    {
        [Tooltip("Speed added to a dust, toward the gravity source, the moment the ring passes it (px/s). The same at every " +
                 "level since 24 §16.1; an ability_power node would multiply it, but the seed has none.")]
        public double pullSpeedPx = 260;
        [Tooltip("How far the ring goes with only the unlock owned, as a share of the screen's half diagonal (the middle of " +
                 "the screen to a corner) at the world scale of the moment (24 §17.1). Every ability_range level adds an " +
                 "equal step from here toward 1, and the last level is the whole screen instead: 0.4 → 0.6 → 0.8 → whole " +
                 "screen with three levels, 0.4 → 0.52 → 0.64 → 0.76 → 0.88 → whole screen with five.")]
        public double rangeScreenStart = 0.4;
        [Tooltip("The ring always goes at least this many gravity radii. Late in the game the gravity radius has outgrown the " +
                 "screen share above, and even the unlock level should reach past the gravity ring.")]
        public double rangeMinRadii = 1.5;
        [Tooltip("Seconds the ring takes to reach the end of its range, whatever the range is.")]
        public double ringSec = 0.5;

        [Header("Look")]
        [Tooltip("Width of the ring line, px on screen.")]
        public double ringWidthPx = 4;
        [Tooltip("Ring color, at the alpha it has while it spreads.")]
        public Color ringColor = new Color(0.72f, 0.88f, 1f, 0.85f);
        [Tooltip("Seconds the ring stays at the end of its range, fading out, so that how far it reached can be seen. " +
                 "0 = it is gone the moment it arrives.")]
        public double ringFadeSec = 0.25;
        [Tooltip("Corners of the circle the ring is drawn as.")]
        public int ringSegments = 128;

        public ImplodeDef()
        {
            id = "implode";
            unlockRing = 4;
            cooldownSec = 14;
        }

        /// <summary>The push after the ability_power nodes, px/s.</summary>
        public double PullSpeedPx(in AbilityLevel lv) => pullSpeedPx * (1.0 + lv.power);

        /// <summary>24 §16.1: at the last level of the range nodes the ring covers the screen and <see cref="RangeScreenShare"/> is not asked.</summary>
        public bool WholeScreen(in AbilityLevel lv) => lv.rangeMaxed;

        /// <summary>
        /// 24 §17.1: how far the ring goes below the last level, as a share of the screen's half diagonal:
        /// start + (1 − start) × levels / all levels.
        /// </summary>
        public double RangeScreenShare(in AbilityLevel lv) =>
            rangeScreenStart + (1.0 - rangeScreenStart) * Math.Min(1.0, Math.Max(0.0, lv.rangeShare));

        public override Ability CreateRuntime() => new ImplodeAbility();

        public override bool Uses(string statId) =>
            statId == StatIds.AbilityRange || statId == StatIds.AbilityPower || statId == StatIds.AbilityCooldown;

        public override void Check(in AbilityLevel full, CelestialTable c, ValidationReport r)
        {
            if (!(cooldownSec > 0)) r.Errors.Add(Where + " cooldownSec must be > 0 (24 §8).");
            if (!(pullSpeedPx > 0)) r.Errors.Add(Where + " pullSpeedPx must be > 0, or the ring does nothing.");
            if (!(rangeScreenStart > 0) || rangeScreenStart > 1)
                r.Errors.Add(Where + " rangeScreenStart must be within 0..1 (0 excluded); it is a share of the screen's half diagonal (24 §17.1).");
            if (rangeMinRadii < 0) r.Errors.Add(Where + " rangeMinRadii must be >= 0; 0 leaves the reach to the screen share alone.");
            if (!full.rangeMaxed)
                r.Warnings.Add(Where + " has no ability_range node with a last level, so it never reaches the whole screen (24 §16.1).");
            if (!(ringSec > 0)) r.Errors.Add(Where + " ringSec must be > 0.");
            if (!(ringWidthPx > 0)) r.Errors.Add(Where + " ringWidthPx must be > 0.");
            if (ringFadeSec < 0) r.Errors.Add(Where + " ringFadeSec must be >= 0.");
            if (ringSegments < 8) r.Errors.Add(Where + " ringSegments must be >= 8.");
        }
    }

    /// <summary>
    /// 24 §4, §16.2 위성. Small gravity sources that orbit the gravity source while holding, on an ellipse that takes
    /// them out past the gravity ring and back in close. They pull and never capture: the dust they drag along ends up
    /// in the gravity source's own field.
    /// </summary>
    [Serializable]
    public sealed class SatelliteDef : AbilityDef
    {
        [Tooltip("Satellites with only the unlock owned. ability_count adds to it: +1 per level in the seed, 1 → 4.")]
        public int count = 1;
        [Tooltip("The most satellites there can be. Each one costs every dust one more distance test per tick and nothing " +
                 "above this was measured (24 §4), so Validate Data fails when the tree can go past it.")]
        public int maxCount = 4;
        [Tooltip("The orbit is an ellipse with the gravity source at one focus (24 §16.2). Its nearest point, as a share of " +
                 "the gravity radius: inside the gravity ring, where the satellite whips round fast.")]
        public double periapsisShare = 0.5;
        [Tooltip("Its farthest point, as a share of the gravity radius: outside the ring, where the gravity source does not " +
                 "reach and the satellite lingers. Equal to periapsisShare = a circle.")]
        public double apoapsisShare = 2.2;
        [Tooltip("Seconds per orbit. Counter-clockwise, like the vortex. The satellite sweeps equal areas in equal times, " +
                 "so most of these seconds are spent far out.")]
        public double orbitPeriodSec = 5;
        [Tooltip("The whole orbit turns this many degrees per second, counter-clockwise, so the far point does not keep " +
                 "raking the same direction. With n satellites the long axes are 360/n degrees apart. 0 = it stays put.")]
        public double precessionDegPerSec = 15;
        [Tooltip("Reach of a satellite's own pull as a share of the gravity radius. Dust inside it is pulled and excited; " +
                 "dust outside it costs one distance test and is skipped.")]
        public double pullRadiusShare = 0.45;
        [Tooltip("A satellite's pull as a share of the gravity source's pull_accel. ability_power multiplies it.")]
        public double pullShare = 1.0;

        [Header("Look")]
        [Tooltip("Diameter of the bright dot and of the soft glow around it, px.")]
        public double dotPx = 10;
        public double glowPx = 44;
        [Tooltip("Dot color, and the alpha of its glow (the glow follows the effect intensity).")]
        public Color color = new Color(0.82f, 0.93f, 1f, 1f);
        public double glowAlpha = 0.45;
        [Tooltip("The ribbon a satellite leaves behind, which is what draws the orbit: how long it lasts (seconds; 0 = none) " +
                 "and its width at the head, px.")]
        public double trailSec = 0.8;
        public double trailWidthPx = 5;

        public SatelliteDef()
        {
            id = "satellite";
            unlockRing = 6;
            cooldownSec = 0;
        }

        /// <summary>Satellites after the ability_count nodes, never above <see cref="maxCount"/>.</summary>
        public int Count(in AbilityLevel lv) => Math.Max(0, Math.Min(maxCount, count + (int)Math.Round(lv.count)));

        /// <summary>The pull share after the ability_power nodes.</summary>
        public double PullShare(in AbilityLevel lv) => pullShare * (1.0 + lv.power);

        public override Ability CreateRuntime() => new SatelliteAbility();

        public override bool Uses(string statId) => statId == StatIds.AbilityCount || statId == StatIds.AbilityPower;

        public override void Check(in AbilityLevel full, CelestialTable c, ValidationReport r)
        {
            if (cooldownSec < 0) r.Errors.Add(Where + " cooldownSec must be >= 0.");
            if (count < 1) r.Errors.Add(Where + " count must be >= 1, or unlocking it shows nothing.");
            if (count + full.count > maxCount)
                r.Errors.Add(Where + $" reaches {count + full.count} satellites with every node bought, past maxCount {maxCount} (24 §8). " +
                             "Either raise the ceiling after measuring, or lower the count nodes.");
            if (!(periapsisShare > 0) || apoapsisShare < periapsisShare)
                r.Errors.Add(Where + " needs 0 < periapsisShare <= apoapsisShare (24 §16.2); at 0 the orbit goes through the gravity source.");
            if (!(orbitPeriodSec > 0)) r.Errors.Add(Where + " orbitPeriodSec must be > 0.");
            if (!(pullRadiusShare > 0)) r.Errors.Add(Where + " pullRadiusShare must be > 0.");
            if (pullShare < 0) r.Errors.Add(Where + " pullShare must be >= 0.");
            if (!(dotPx > 0) || glowPx < 0) r.Errors.Add(Where + " dotPx must be > 0 and glowPx >= 0.");
            if (glowAlpha < 0 || glowAlpha > 1) r.Errors.Add(Where + " glowAlpha must be within 0..1.");
            if (trailSec < 0 || trailWidthPx < 0) r.Errors.Add(Where + " trailSec and trailWidthPx must be >= 0.");
        }
    }

    /// <summary>
    /// 24 §5 유성. Every cooldown a meteor crosses the screen in a straight line, grazing the gravity source, and sheds
    /// dust along its path. Gravity does not act on it and it cannot be caught.
    /// </summary>
    [Serializable]
    public sealed class MeteorDef : AbilityDef
    {
        [Tooltip("Seconds from coming in past one edge of the screen to leaving past the other.")]
        public double crossSec = 2.5;
        [Tooltip("The path passes the gravity source at a random distance between these, in gravity radii: near enough for " +
                 "the shed dust to be pulled in, never through the middle.")]
        public double passMinRadii = 1;
        public double passMaxRadii = 1.5;
        [Tooltip("The path is drawn from where the gravity source is when the meteor fires. If the gravity source moves after " +
                 "that, the meteor turns to keep its planned distance from it, by this many degrees per second at most and " +
                 "only while it is on screen. 0 = a straight line whatever the player does.")]
        public double homingDegPerSec = 12;
        [Tooltip("Dust shed over one crossing, as a share of the dust cap at that moment. ability_amount adds to it: " +
                 "+3%p per level in the seed, 6% → 15%. The cap is never passed: at the cap it sheds that much less.")]
        public double dustShare = 0.06;
        [Tooltip("The largest share the upgrades may reach. Validate Data fails when the tree can go past it (24 §8).")]
        public double maxDustShare = 0.30;
        [Tooltip("Shed dust is born moving the meteor's way, at this share of the meteor's speed.")]
        public double dustSpeedShare = 0.2;
        [Tooltip("Shed dust rolls its kind as any dust does, with the share of every unlocked kind above the base dust multiplied by this.")]
        public double heavyWeight = 2;
        [Tooltip("Shed dust lands up to this far to either side of the path (px), so it reads as a band and not as a drawn line.")]
        public double shedSpreadPx = 30;
        [Tooltip("The wake: dust within this share of the gravity radius of the meteor is dragged along its direction and excited.")]
        public double wakeRadiusShare = 0.8;
        [Tooltip("How hard the wake drags, px/s² at the meteor itself; it falls off toward the edge of the wake. 0 = no wake.")]
        public double wakeAccelPx = 700;

        [Header("Look")]
        [Tooltip("The celestial tier whose look the meteor wears (2 = the comet).")]
        public int lookTier = 2;
        [Tooltip("Diameter of the head, px, and the length of the tail in head radii (the comet's own tail is 3.5).")]
        public double sizePx = 40;
        public double tailLength = 9;

        [Header("Internals")]
        [Tooltip("Random paths tried for one that passes the gravity source inside the screen, before settling for any path.")]
        public int pathTries = 16;

        public MeteorDef()
        {
            id = "meteor";
            unlockRing = 8;
            cooldownSec = 10;
        }

        /// <summary>The share of the dust cap one crossing sheds after the ability_amount nodes, never above <see cref="maxDustShare"/>.</summary>
        public double DustShare(in AbilityLevel lv) => Math.Max(0.0, Math.Min(maxDustShare, dustShare + lv.amount));

        public override Ability CreateRuntime() => new MeteorAbility();

        public override bool Uses(string statId) => statId == StatIds.AbilityAmount || statId == StatIds.AbilityCooldown;

        public override void Check(in AbilityLevel full, CelestialTable c, ValidationReport r)
        {
            if (!(cooldownSec > 0)) r.Errors.Add(Where + " cooldownSec must be > 0 (24 §8).");
            if (!(crossSec > 0)) r.Errors.Add(Where + " crossSec must be > 0.");
            if (!(passMinRadii > 0) || passMaxRadii < passMinRadii)
                r.Errors.Add(Where + " needs 0 < passMinRadii <= passMaxRadii; at 0 the meteor goes through the gravity source (24 §5).");
            if (homingDegPerSec < 0) r.Errors.Add(Where + " homingDegPerSec must be >= 0; 0 is a straight line.");
            if (!(dustShare > 0)) r.Errors.Add(Where + " dustShare must be > 0, or the meteor sheds nothing.");
            if (!(maxDustShare > 0) || maxDustShare > 1) r.Errors.Add(Where + " maxDustShare must be within 0..1 (0 excluded); it is a share of the dust cap.");
            if (dustShare + full.amount > maxDustShare + 1e-9)
                r.Errors.Add(Where + $" sheds {(dustShare + full.amount) * 100:F0}% of the cap with every node bought, past maxDustShare {maxDustShare * 100:F0}% (24 §8).");
            if (dustSpeedShare < 0) r.Errors.Add(Where + " dustSpeedShare must be >= 0.");
            if (heavyWeight < 0) r.Errors.Add(Where + " heavyWeight must be >= 0.");
            if (shedSpreadPx < 0) r.Errors.Add(Where + " shedSpreadPx must be >= 0.");
            if (wakeRadiusShare < 0 || wakeAccelPx < 0) r.Errors.Add(Where + " wakeRadiusShare and wakeAccelPx must be >= 0.");
            if (c != null && c.Get(lookTier) == null) r.Errors.Add(Where + $" lookTier {lookTier} is not a tier of the CelestialTable.");
            if (!(sizePx > 0) || tailLength < 0) r.Errors.Add(Where + " sizePx must be > 0 and tailLength >= 0.");
            if (pathTries < 1) r.Errors.Add(Where + " pathTries must be >= 1.");
        }
    }

    /// <summary>
    /// Special abilities (24). One row per ability, and every number of an ability is in its row: nothing about them is
    /// a constant in code. The field initializers are the seed (PrototypeSetup creates the asset from them, and
    /// Validate Data warns where the asset holds another value), and the values are meant to be changed in the
    /// inspector while playing: the game reads them every tick.
    ///
    /// An ability is unlocked and raised in the skill tree by nodes that name its <see cref="AbilityDef.id"/>
    /// (NodeKind.AbilityUnlock and the ability_* stats), the way the dust kinds are. So a new ability is a row here
    /// (its Def class, which also names the <see cref="Ability"/> that runs it) and two nodes in the tree seed; the
    /// stats, the debug keys, the tree screen and the checks already cover it.
    /// </summary>
    [CreateAssetMenu(fileName = "AbilityTable", menuName = "Incremental/Ability Table")]
    public sealed class AbilityTable : ScriptableObject
    {
        public ImplodeDef implode = new ImplodeDef();
        public SatelliteDef satellite = new SatelliteDef();
        public MeteorDef meteor = new MeteorDef();

        [Header("Shared")]
        [Tooltip("Excited dust (24 §8): a dust an ability hits lights up and takes this long to calm down again (seconds).")]
        public double exciteSec = 0.4;
        [Tooltip("How much brighter a freshly excited dust is drawn: its brightness x (1 + this), fading with the excitement. " +
                 "Follows the effect intensity.")]
        public double exciteBrightness = 2;
        [Tooltip("While a dust is excited, how fast it settles back to its drifting speed is multiplied by this (24 §16.1), " +
                 "so a dust an ability pushed keeps going instead of stopping in half a second. 1 = as the first prototype; " +
                 "0 = it keeps all of its speed until it calms down. It lasts as long as exciteSec.")]
        public double exciteDriftRelaxMult = 0.3;
        [Tooltip("The most the ability_cooldown nodes can take off a cooldown (0.8 = down to a fifth), so none ever reaches 0.")]
        public double maxCooldownReduction = 0.8;
        [Tooltip("Writes what the abilities do to the Console as \"[Ability] ...\" lines: which are on when a run starts, every " +
                 "trigger and what it did, every debug key. For checking the prototype by eye; switch it off for bot runs.")]
        public bool logEvents = true;

        [NonSerialized] AbilityDef[] rows;

        /// <summary>
        /// Every row, in the order the debug key cycles through them (24 §7). A new ability is one more field above and
        /// one more entry here.
        /// </summary>
        public AbilityDef[] Rows => rows ?? (rows = new AbilityDef[] { implode, satellite, meteor });

        public int Count => Rows.Length;

        /// <summary>Index of the row with this id, or -1.</summary>
        public int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            var all = Rows;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].id == id) return i;
            return -1;
        }

        /// <summary>The row with this id, or null.</summary>
        public AbilityDef Get(string id)
        {
            int i = IndexOf(id);
            return i >= 0 ? Rows[i] : null;
        }

        // The rows are plain serialized objects, which the editor may replace when the asset is reloaded or edited.
        void OnEnable() => rows = null;
        void OnValidate() => rows = null;
    }
}
