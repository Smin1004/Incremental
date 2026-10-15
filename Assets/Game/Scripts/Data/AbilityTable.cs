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
        /// <summary>ability_duration: seconds added to how long the ability lasts (24 §19.1).</summary>
        public double duration;
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
        /// <summary>
        /// 24 §21.2: the ability's evolve node is owned (or the debug level is past every stat node), so the ability
        /// takes its evolved form.
        /// </summary>
        public bool evolved;
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

        /// <summary>The cooldown before the ability_cooldown nodes: cooldownSec, unless the evolved form has one of its own (24 §21.2).</summary>
        public virtual double BaseCooldown(in AbilityLevel lv) => cooldownSec;

        /// <summary>The cooldown after the ability_cooldown nodes, which can take off <paramref name="maxReduction"/> at most.</summary>
        public double Cooldown(in AbilityLevel lv, double maxReduction) =>
            BaseCooldown(lv) * (1.0 - Math.Min(Math.Max(lv.cooldown, 0.0), maxReduction));

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
            // 10/8 (developer, after the 7th pass): 14 -> 20, so the field has time to refill between waves.
            cooldownSec = 20;
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
    /// 24 §4, §16.2 궤도석 (위성 until 24 §23.5). Small gravity sources that orbit the gravity source while holding, on an ellipse that takes
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
        [Tooltip("A satellite's pull as a share of the gravity source's pull_accel. ability_power multiplies it. 24 §18.1 " +
                 "raised it from 1.0.")]
        public double pullShare = 1.4;
        [Tooltip("24 §18.1 the tow: the satellite's core, as a share of the gravity radius. Dust inside it is not only pulled " +
                 "but has its velocity drawn toward the satellite's own, so the clump it gathered comes round with it " +
                 "instead of being left behind at the speed dust can manage. 0 = no core.")]
        public double towRadiusShare = 0.15;
        [Tooltip("How fast the velocity of a dust in the core is drawn to the satellite's, per second (6 = most of the way " +
                 "in a sixth of a second). 0 = no tow, which is the 2차 satellite.")]
        public double towRate = 6;
        [Tooltip("Inside this share of the gravity radius (while holding) a satellite lets go: its pull and its tow stop and the dust " +
                 "is held to the field's ordinary top speed, so the gravity source keeps what it has instead of the satellite " +
                 "dragging it back out (10차 회신). 0 = the satellite pulls everywhere, as before.")]
        public double yieldRadiusShare = 1;

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
        [Tooltip("24 §24.5: the ribbon is the arc of the orbit behind the satellite, worked out every frame around where the " +
                 "gravity source is drawn now, so moving the gravity source does not drag it. Off = the world-space trail of " +
                 "before, which stretches and bends while the gravity source moves.")]
        public bool trailInOrbitFrame = true;
        [Tooltip("Points along that arc. More is smoother where the satellite whips round its nearest point.")]
        public int trailSegments = 48;

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

        /// <summary>24 §18.1: true when the core tows at all.</summary>
        public bool Tows => towRadiusShare > 0 && towRate > 0;

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
            if (towRadiusShare < 0 || towRate < 0) r.Errors.Add(Where + " towRadiusShare and towRate must be >= 0 (24 §18.1); 0 is no tow.");
            if (yieldRadiusShare < 0 || yieldRadiusShare > 1.5) r.Errors.Add(Where + " yieldRadiusShare must be within 0..1.5; 0 never lets go.");
            if (towRadiusShare > pullRadiusShare)
                r.Warnings.Add(Where + " towRadiusShare is wider than pullRadiusShare: the core reaches past the pull, which 24 §18.1 did not intend.");
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

        [Header("Evolved: 유성우 (24 §21.2; 10/8: 간격 단축)")]
        [Tooltip("Evolved, meteors come every this many seconds of holding instead of cooldownSec, still one at a time, each on " +
                 "its own random path. (Five at once, 0.3 s apart, was tried in the 7th pass and rejected on 10/8 as too much.)")]
        public double showerCooldownSec = 5;

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

        /// <summary>24 §21.2 (10/8): evolved, the cooldown is showerCooldownSec.</summary>
        public override double BaseCooldown(in AbilityLevel lv) => lv.evolved ? showerCooldownSec : cooldownSec;

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
            if (!(showerCooldownSec > 0)) r.Errors.Add(Where + " showerCooldownSec must be > 0: it is the evolved form's cooldown (24 §21.2).");
            if (full.evolved && showerCooldownSec < crossSec)
                r.Warnings.Add(Where + " evolved, the next meteor is due while the last is still crossing; it waits for it.");
        }
    }

    /// <summary>
    /// 24 §19.1 블랙홀. The big, rare one (40 seconds of holding): an inhale that slows the field and darkens the edges,
    /// then the gravity source is the ending's black hole for a few seconds and pulls everything dust can be in, band
    /// outside the screen included, harder and with more swirl; a flash lets go. Ring 10 unlocks it, ring 11 adds seconds.
    /// </summary>
    [Serializable]
    public sealed class BlackHoleDef : AbilityDef
    {
        [Tooltip("Seconds of the inhale before the black hole opens: the dust slows to slowFactor of its speed and the edges of " +
                 "the screen darken. The warning that something big is coming.")]
        public double inhaleSec = 1;
        [Tooltip("What the dust's speed is multiplied down to over the inhale (0.25 = a quarter).")]
        public double slowFactor = 0.25;
        [Tooltip("Seconds the black hole is open with only the unlock owned. ability_duration adds seconds: +1 per level in the seed, 4 -> 7.")]
        public double durationSec = 4;
        [Tooltip("While it is open, a pull from the gravity source over everything dust can be in (the band outside the screen " +
                 "included): pull_accel x this, on top of the ordinary pull inside the gravity radius.")]
        public double pullMult = 2.5;
        [Tooltip("The sideways share of that pull: GameParams.vortexShare x this, so the whole screen winds in instead of falling straight.")]
        public double vortexMult = 2;
        [Tooltip("Seconds of the release: the flash, the darkness fading, the gravity source as it was.")]
        public double releaseSec = 0.4;

        [Header("Look")]
        [Tooltip("The gravity source wears GameParams.blackHoleLook (the ending's black hole, accretion disc and all) at this diameter (px) while it is open.")]
        public double sizePx = 138;
        [Tooltip("How dark the edges of the screen get (alpha), rising over the inhale, held while open, gone with the release; x effect intensity.")]
        public double darkenAlpha = 0.45;
        [Tooltip("How far in from the edge the darkness reaches, as a share of the half screen (1 = to the middle).")]
        public double darkenReach = 0.7;
        [Tooltip("The release flash (the ending's ScreenFlash, flash rule 15 §3): peak alpha, rise seconds, color; x effect intensity.")]
        public double flashAlpha = 0.35;
        public double flashRiseSec = 0.1;
        public Color flashColor = new Color(0.9f, 0.95f, 1f);

        [Header("Bodies while open (24 §24.2-1)")]
        [Tooltip("From the inhale to the end of the release, bodies at the gravity source (the forming one and the ones just made) " +
                 "are drawn at this share of their size. 1 = off. Income, the x N label and the result screen are untouched.")]
        public double blackHoleBodyScale = 0.4;
        [Tooltip("A body drawn then (bundle growth included) is never wider on screen than this share of the black hole's diameter.")]
        public double blackHoleBodyMaxShare = 0.6;
        [Tooltip("Seconds over which the bodies grow back to their own size after the release.")]
        public double blackHoleBodyRestoreSec = 0.3;
        [Tooltip("24 §24.3: the glow (corona) of those bodies reaches this share of its own reach, on top of the body's " +
                 "shrink, so it does not lie round the disc as a thick ring. 1 = shrinks with the body only.")]
        public double blackHoleBodyGlowScale = 0.3;
        [Tooltip("24 §24.3: a star that ignites while the bodies are shrunk gets its ignition flash and light ring at this " +
                 "share (they are drawn over everything, so they washed the black disc grey). 1 = as before.")]
        public double blackHoleIgnitionScale = 0;

        [Header("Lens (24 §24.2-2)")]
        [Tooltip("How far the background stars, the dust and the trails are pushed outward around the black hole, in black hole " +
                 "radii at its edge (0 = no lens). Only where they are drawn moves; the simulation does not. x effect intensity; " +
                 "rises over the inhale, gone over the release.")]
        public double lensStrength = 1.0;
        [Tooltip("The push reaches out to this many black hole radii from its centre, fading to nothing there.")]
        public double lensReachRadii = 3.2;
        [Tooltip("The thin ring of light (Einstein ring) just outside the edge, drawn into the background: brightness (linear, " +
                 "additive; 0 = none) and width as a share of the black hole radius.")]
        public double lensRingAlpha = 0.05;
        public double lensRingWidth = 0.08;
        [Tooltip("24 §24.3: dust and trails inside the disc are drawn pushed out past its edge (radius x (1 + lensRingWidth) " +
                 "and on), so the disc stays black and the dust gathers into a bright ring round it. Off = the 10th pass lens.")]
        public bool lensClearDisc = true;
        [Tooltip("26 §12.2 D3: the lens also turns what it draws round the black hole, counter-clockwise like the dust vortex: " +
                 "this many degrees at the edge, nothing at lensReachRadii. Comes and goes with the push. 0 = no swirl.")]
        public double lensTwistDeg = 40;

        public BlackHoleDef()
        {
            id = "blackhole";
            unlockRing = 10;
            cooldownSec = 60;
        }

        /// <summary>Seconds the black hole is open after the ability_duration nodes.</summary>
        public double Duration(in AbilityLevel lv) => durationSec + Math.Max(0.0, lv.duration);

        public override Ability CreateRuntime() => new BlackHoleAbility();

        public override bool Uses(string statId) => statId == StatIds.AbilityDuration || statId == StatIds.AbilityCooldown;

        public override void Check(in AbilityLevel full, CelestialTable c, ValidationReport r)
        {
            if (!(cooldownSec > 0)) r.Errors.Add(Where + " cooldownSec must be > 0 (24 §8).");
            if (inhaleSec < 0) r.Errors.Add(Where + " inhaleSec must be >= 0; 0 opens the black hole at once.");
            if (!(slowFactor > 0) || slowFactor > 1) r.Errors.Add(Where + " slowFactor must be within 0..1 (0 excluded); 1 does not slow the dust.");
            if (!(durationSec > 0)) r.Errors.Add(Where + " durationSec must be > 0, or the black hole never opens.");
            if (pullMult < 0 || vortexMult < 0) r.Errors.Add(Where + " pullMult and vortexMult must be >= 0.");
            if (releaseSec < 0) r.Errors.Add(Where + " releaseSec must be >= 0.");
            if (!(sizePx > 0)) r.Errors.Add(Where + " sizePx must be > 0.");
            if (darkenAlpha < 0 || darkenAlpha > 1 || darkenReach < 0 || darkenReach > 1)
                r.Errors.Add(Where + " darkenAlpha and darkenReach must be within 0..1.");
            if (flashAlpha < 0 || flashAlpha > 1 || flashRiseSec < 0) r.Errors.Add(Where + " flashAlpha must be within 0..1 and flashRiseSec >= 0.");
            if (blackHoleIgnitionScale < 0 || blackHoleIgnitionScale > 1)
                r.Errors.Add(Where + " blackHoleIgnitionScale must be within 0..1.");
            if (!(blackHoleBodyGlowScale > 0) || blackHoleBodyGlowScale > 1)
                r.Errors.Add(Where + " blackHoleBodyGlowScale must be within 0..1 (0 excluded).");
            if (!(blackHoleBodyScale > 0) || blackHoleBodyScale > 1 || !(blackHoleBodyMaxShare > 0) || blackHoleBodyRestoreSec < 0)
                r.Errors.Add(Where + " blackHoleBodyScale must be within 0..1 (0 excluded), blackHoleBodyMaxShare > 0 and blackHoleBodyRestoreSec >= 0.");
            if (lensStrength < 0 || lensStrength > 1.5 || !(lensReachRadii > 1) || lensRingAlpha < 0 || !(lensRingWidth > 0))
                r.Errors.Add(Where + " lensStrength must be within 0..1.5, lensReachRadii > 1, lensRingAlpha >= 0 and lensRingWidth > 0.");
            if (full.duration > 0 && cooldownSec <= durationSec + full.duration + inhaleSec)
                r.Warnings.Add(Where + " is open longer than its cooldown with every node bought, so it would never close.");
        }
    }

    /// <summary>
    /// 24 §20.2 증폭 결정. Every cooldown of holding a gold crystal appears on screen, a few gravity radii from the gravity
    /// source, and waits there a while; brought inside the gravity radius it is drawn in and absorbed, and for buffSec
    /// the gravity source is amplified: wider radius, stronger pull, faster dust. The first ability (ring 2); ring 3
    /// raises the multipliers. The cooldownSec column is how often a crystal appears (24 §20.2 spawnEverySec).
    /// </summary>
    [Serializable]
    public sealed class CrystalDef : AbilityDef
    {
        [Tooltip("The crystal appears at least this many gravity radii from the gravity source, so there is something to go and get.")]
        public double minDistanceRadii = 2;
        [Tooltip("It appears and stays at least this far inside the edge of the screen, px.")]
        public double edgeMarginPx = 80;
        [Tooltip("Seconds it waits before it is gone. Over the last blinkSec of them it blinks, blinkHz times a second.")]
        public double lifeSec = 8;
        public double blinkSec = 2;
        public double blinkHz = 4;
        [Tooltip("How fast it drifts while it waits, px/s; it turns back at the edge margin.")]
        public double driftSpeedPx = 14;
        [Tooltip("Once inside the gravity radius (while holding) it is drawn to the gravity source at this speed, px/s, and absorbed at the capture radius.")]
        public double pullInSpeedPx = 700;

        [Header("Buff")]
        [Tooltip("Seconds the amplification lasts. Eating another crystal meanwhile starts these over; the multipliers do not stack " +
                 "unless the crystal is evolved (공명, 24 §21.2), when each one adds them once more up to resonanceMaxStacks.")]
        public double buffSec = 6;
        [Tooltip("The gravity radius x this while amplified, with only the unlock owned. ability_power x radiusPerPower adds to it " +
                 "(24 §23.2: +0.1 x 0.5 = +0.05 per level in the seed, 1.20 -> 1.35).")]
        public double radiusMult = 1.20;
        [Tooltip("24 §23.2: how much the radius multiplier rises per ability_power (0.5 = +0.05 per +0.1; 1 = as before 10/12).")]
        public double radiusPerPower = 0.5;
        [Tooltip("The pull (pull_accel) x this while amplified. ability_power x pullPerPower adds to it: +0.2 per level in the seed, 1.5 -> 2.1.")]
        public double pullMult = 1.5;
        [Tooltip("How much faster the pull multiplier rises with ability_power than the radius multiplier does (2 = +0.2 per +0.1).")]
        public double pullPerPower = 2;
        [Tooltip("The dust's top speed x this while amplified, so the stronger pull is not capped.")]
        public double speedMult = 1.4;
        [Tooltip("Seconds the multipliers take to come in when a crystal is absorbed, and to go when the buff ends.")]
        public double rampSec = 0.3;

        [Header("Evolved: 공명 (24 §21.2; 10/8: 간격 단축, 2중첩)")]
        [Tooltip("Evolved, a crystal appears every this many seconds of holding instead of cooldownSec (still one at a time), and " +
                 "absorptions stack while the buff is on, up to resonanceMaxStacks: each stack adds the multipliers once more, " +
                 "radius x(1 + (radiusMult - 1) x stacks); two stacks of ring 7's x1.65 are x2.3. (Three at once and three stacks " +
                 "were tried in the 7th pass and rejected on 10/8 as too much.)")]
        public double resonanceCooldownSec = 5;
        public int resonanceMaxStacks = 2;

        [Header("Look")]
        [Tooltip("The crystal: a diamond this tall (px), its color, and the soft glow around it (diameter px, alpha; the glow follows the effect intensity).")]
        public double sizePx = 26;
        public Color color = new Color(1f, 0.86f, 0.45f, 1f);
        public double glowPx = 90;
        public double glowAlpha = 0.5;
        [Tooltip("The ring pulsing around the crystal (FxRing): diameter px, line width px and strength; a pulse takes pulseSec and grows it by pulseScale.")]
        public double haloPx = 70;
        public double haloWidthPx = 2.5;
        public double haloStrength = 0.5;
        public double pulseSec = 1.2;
        public double pulseScale = 0.18;
        [Tooltip("The moment it is absorbed the gravity ring flashes gold for this long and every dust on screen is excited (24 §8).")]
        public double flashSec = 0.4;
        [Tooltip("The gravity ring's color while amplified (blended in with the ramp) and at the flash.")]
        public Color ringColor = new Color(1f, 0.84f, 0.4f, 0.9f);

        public CrystalDef()
        {
            id = "crystal";
            unlockRing = 2;
            cooldownSec = 12;
        }

        /// <summary>The gravity radius multiplier after the ability_power nodes.</summary>
        public double RadiusMult(in AbilityLevel lv) => RadiusMult(lv, radiusMult, radiusPerPower);

        /// <summary>Same, with the two values given (the debug key K, 24 §23.2).</summary>
        public static double RadiusMult(in AbilityLevel lv, double atUnlock, double perPower) =>
            atUnlock + Math.Max(0.0, lv.power) * perPower;

        /// <summary>The pull multiplier after the ability_power nodes.</summary>
        public double PullMult(in AbilityLevel lv) => pullMult + Math.Max(0.0, lv.power) * pullPerPower;

        /// <summary>24 §21.2 (10/8): evolved, a crystal comes every resonanceCooldownSec of holding instead of cooldownSec.</summary>
        public override double BaseCooldown(in AbilityLevel lv) => lv.evolved ? resonanceCooldownSec : cooldownSec;

        public override Ability CreateRuntime() => new CrystalAbility();

        public override bool Uses(string statId) => statId == StatIds.AbilityPower || statId == StatIds.AbilityCooldown;

        public override void Check(in AbilityLevel full, CelestialTable c, ValidationReport r)
        {
            if (!(cooldownSec > 0)) r.Errors.Add(Where + " cooldownSec must be > 0: it is how often a crystal appears (24 §20.2).");
            if (minDistanceRadii < 0 || edgeMarginPx < 0) r.Errors.Add(Where + " minDistanceRadii and edgeMarginPx must be >= 0.");
            if (!(lifeSec > 0)) r.Errors.Add(Where + " lifeSec must be > 0, or the crystal is gone before it is seen.");
            if (blinkSec < 0 || blinkSec > lifeSec) r.Errors.Add(Where + " blinkSec must be within 0..lifeSec.");
            if (blinkHz < 0 || driftSpeedPx < 0) r.Errors.Add(Where + " blinkHz and driftSpeedPx must be >= 0.");
            if (!(pullInSpeedPx > 0)) r.Errors.Add(Where + " pullInSpeedPx must be > 0, or the crystal is never absorbed.");
            if (!(buffSec > 0)) r.Errors.Add(Where + " buffSec must be > 0.");
            if (radiusMult < 1 || pullMult < 1 || speedMult < 1)
                r.Errors.Add(Where + " radiusMult, pullMult and speedMult must be >= 1; below 1 the buff would weaken the gravity source.");
            if (pullPerPower < 0) r.Errors.Add(Where + " pullPerPower must be >= 0.");
            if (rampSec < 0) r.Errors.Add(Where + " rampSec must be >= 0.");
            if (rampSec * 2 > buffSec) r.Warnings.Add(Where + " ramps in and out longer than the buff lasts; the full multipliers are never reached.");
            if (!(sizePx > 0) || glowPx < 0 || haloPx < 0 || haloWidthPx < 0) r.Errors.Add(Where + " sizePx must be > 0 and glowPx, haloPx, haloWidthPx >= 0.");
            if (glowAlpha < 0 || glowAlpha > 1) r.Errors.Add(Where + " glowAlpha must be within 0..1.");
            if (haloStrength < 0 || pulseScale < 0 || !(pulseSec > 0)) r.Errors.Add(Where + " haloStrength and pulseScale must be >= 0 and pulseSec > 0.");
            if (flashSec < 0) r.Errors.Add(Where + " flashSec must be >= 0.");
            if (!(resonanceCooldownSec > 0)) r.Errors.Add(Where + " resonanceCooldownSec must be > 0: it is the evolved form's cooldown (24 §21.2).");
            if (resonanceMaxStacks < 1) r.Errors.Add(Where + " resonanceMaxStacks must be >= 1 (24 §21.2).");
            if (full.evolved && resonanceCooldownSec >= buffSec)
                r.Warnings.Add(Where + " evolved, the next crystal comes only after the buff has ended, so the stacks never build.");
            if (lifeSec >= cooldownSec)
                r.Warnings.Add(Where + " waits longer than its cooldown, so a crystal can still be there when the next is due; the next waits for it.");
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
        public CrystalDef crystal = new CrystalDef();
        public ImplodeDef implode = new ImplodeDef();
        public SatelliteDef satellite = new SatelliteDef();
        public MeteorDef meteor = new MeteorDef();
        public BlackHoleDef blackHole = new BlackHoleDef();

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
        [Tooltip("24 §18.1: while a dust is excited its top speed is GameParams.dustMaxSpeed x this (1.8: 400 -> 720 px/s), " +
                 "so a dust a satellite tows can keep up with it; dust pushed by 수축 or a meteor's wake is faster too. " +
                 "1 = the cap of the field.")]
        public double exciteSpeedCapMult = 1.8;
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
        public AbilityDef[] Rows => rows ?? (rows = new AbilityDef[] { crystal, implode, satellite, meteor, blackHole });

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
