using System;
using System.Collections.Generic;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// 29 §5: the rule nodes while a run plays. The tree cannot change during a run, so the owned rules are read once at
    /// run start (<see cref="BeginRun"/>); GameRoot, the dust, the HUD and the abilities then ask here. Every number is
    /// GameParams.rules (temporary values of 29 §5; the balance pass sets them). Each effect logs one "[Rule] …" line when
    /// it acts, GameParams.rules.log, and GameParams.rules.enabled off makes every rule node do nothing.
    /// </summary>
    public sealed class Rules
    {
        readonly GameParams p;

        /// <summary>C1: the HUD's chain label (GameRoot hooks HudView.ShowChain here).</summary>
        public Action<int, double> ChainShown;
        readonly Dictionary<string, int> levels = new Dictionary<string, int>();

        int completions;
        int chain;
        double lastCompleteAt = -1;
        double timeAdded;
        double crystalTimeAdded;
        bool deepLogged;
        double holdSec;
        double lingerLeft;
        Vector2 lastGravity;
        bool haveGravity;
        bool still;
        bool graceLogged, rushLogged, startRushLogged;
        int pulsesLogged;
        double aftermathUntil = -1;
        int splits;

        public Rules(GameParams gameParams) { p = gameParams; }

        /// <summary>15th pass, debug key R: every rule node does nothing from the next run on, whatever rules.enabled says. Runtime only.</summary>
        public bool DebugOff { get; set; }

        /// <summary>Whether the rule nodes act in the runs that start now (rules.enabled and not switched off by R).</summary>
        public bool On => P != null && P.enabled && !DebugOff;

        RuleParams P => p.rules;

        /// <summary>Level of a rule node owned at run start (0 = not owned or rules off).</summary>
        public int Level(string ruleId) => ruleId != null && levels.TryGetValue(ruleId, out int l) ? l : 0;

        /// <summary>C1: the completions in a row so far (0 = none yet), and what they add to income.</summary>
        public int Chain => chain;
        public double ChainBonus => Level("C1") > 0 ? Math.Min(P.chainMax, P.chainStep * chain) : 0;
        /// <summary>T2: seconds the completions have added to this run.</summary>
        public double TimeAdded => timeAdded;
        /// <summary>Completions in this run (C2).</summary>
        public int Completions => completions;

        /// <summary>Reads the owned rule nodes and resets everything a run counts.</summary>
        public void BeginRun(MetaState m, NodeTable t)
        {
            levels.Clear();
            completions = 0;
            chain = 0;
            lastCompleteAt = -1;
            timeAdded = 0;
            crystalTimeAdded = 0;
            deepLogged = false;
            holdSec = 0;
            lingerLeft = 0;
            haveGravity = false;
            still = false;
            graceLogged = rushLogged = startRushLogged = false;
            pulsesLogged = 0;
            aftermathUntil = -1;
            splits = 0;
            if (!On || t == null || m == null) return;
            var owned = new List<string>();
            foreach (var n in t.nodes)
            {
                if (n == null || !n.enabled || !n.IsRule || string.IsNullOrEmpty(n.ruleId)) continue;
                int l = m.GetLevel(n.id);
                if (l <= 0) continue;
                if (n.maxLevel > 0) l = Math.Min(l, n.maxLevel);
                levels[n.ruleId] = l;
                owned.Add(n.ruleId + (n.maxLevel > 1 ? " Lv" + l : string.Empty));
            }
            if (owned.Count > 0) Log("run " + m.runCount + " with " + string.Join(", ", owned));
        }

        void Log(string message)
        {
            if (P != null && P.log) Debug.Log("[Rule] " + message);
        }

        // ---------------- time (T1, T2, T3, T5) ----------------

        /// <summary>T1 유예: seconds at the start of the run during which the clock does not run.</summary>
        public double GraceSec => P.graceSecPerLevel * Level("T1");

        /// <summary>T1: true while the clock is held.</summary>
        public bool InGrace(RunState run)
        {
            double g = GraceSec;
            if (g <= 0 || run == null || run.elapsed > g) return false;
            if (!graceLogged)
            {
                graceLogged = true;
                Log("T1 유예: the clock holds for the first " + g.ToString("0.#") + "s");
            }
            return true;
        }

        /// <summary>T3 막판 러시: true in the last rushLastSec of the run (income × rushIncomeMult).</summary>
        public bool InRush(RunState run) => Level("T3") > 0 && run != null && run.timeLeft <= P.rushLastSec && run.timeLeft > 0;

        /// <summary>C3 시작 러시 (14th pass): true in the first startRushSec of the run (income × startRushMult).</summary>
        public bool InStartRush(RunState run) => Level("C3") > 0 && run != null && run.elapsed < P.startRushSec;

        /// <summary>T3 and C3: what the run's income is multiplied by right now.</summary>
        public double RushMult(RunState run)
        {
            double mult = 1;
            if (InStartRush(run))
            {
                if (!startRushLogged)
                {
                    startRushLogged = true;
                    Log("C3 시작 러시: first " + P.startRushSec.ToString("0.#") + "s, income x" + P.startRushMult.ToString("0.##"));
                }
                mult *= P.startRushMult;
            }
            if (InRush(run))
            {
                if (!rushLogged)
                {
                    rushLogged = true;
                    Log("T3 막판 러시: last " + P.rushLastSec.ToString("0.#") + "s, income x" + P.rushIncomeMult.ToString("0.##"));
                }
                mult *= P.rushIncomeMult;
            }
            return mult;
        }

        /// <summary>The time bar's look: 0 as usual, 1 held (T1), 2 rush (T3 at the end, C3 at the start: the same gold pulse).</summary>
        public int TimeBarMode(RunState run)
        {
            if (run == null) return 0;
            double g = GraceSec;
            if (g > 0 && run.elapsed <= g) return 1;
            if (InStartRush(run)) return 2;
            return Level("T3") > 0 && run.timeLeft <= P.rushLastSec && run.timeLeft > 0 ? 2 : 0;
        }

        void AddTime(RunState run, double sec, string why)
        {
            if (run == null || sec <= 0) return;
            run.timeLeft += sec;
            Log(why + ": +" + sec.ToString("0.##") + "s (" + run.timeLeft.ToString("F1") + "s left)");
        }

        /// <summary>T5 결정의 시간: a crystal was eaten.</summary>
        public void OnCrystalTaken(RunState run)
        {
            if (Level("T5") <= 0 || crystalTimeAdded >= P.crystalTimeMaxSec) return;
            double add = Math.Min(P.crystalTimeSec, P.crystalTimeMaxSec - crystalTimeAdded);
            crystalTimeAdded += add;
            AddTime(run, add, "T5 결정의 시간 (" + crystalTimeAdded.ToString("0.#") + "/" + P.crystalTimeMaxSec.ToString("0") + "s this run)");
        }

        // ---------------- dust (D1, D2, D3, D4, D5) ----------------

        /// <summary>D1 가득 찬 시작: the dust a run starts with, at least the level's share of the cap.</summary>
        public double StartDust(double initial, double cap)
        {
            int l = Level("D1");
            var shares = P.fullStartShare;
            if (l <= 0 || shares == null || shares.Length == 0) return initial;
            double share = shares[Math.Min(l, shares.Length) - 1];
            double full = Math.Min(cap, cap * share);
            if (full <= initial) return initial;
            Log("D1 가득 찬 시작: the run starts with " + full.ToString("F0") + " dust (" + (share * 100).ToString("F0") + "% of the cap) instead of " + initial.ToString("F0"));
            return full;
        }

        /// <summary>D3 빠른 보충: added to GameParams.dustRefillBoost.</summary>
        public double RefillBoostAdd => P.refillBoostPerLevel * Level("D3");

        /// <summary>D4 황금 먼지: chance that a new dust is golden.</summary>
        public double GoldChance => P.goldChancePerLevel * Level("D4");

        /// <summary>D2 갈라지는 먼지: chance that a large dust splits the first time it comes within the gravity range.</summary>
        public double SplitChance
        {
            get
            {
                int l = Level("D2");
                var c = P.splitChancePerLevel;
                if (l <= 0 || c == null || c.Length == 0) return 0;
                return Math.Max(0.0, Math.Min(1.0, c[Math.Min(l, c.Length) - 1]));
            }
        }

        /// <summary>D2: a large dust split (the dust field reports it through GameRoot). Logs the first three and every 1000th (a ring 11 run splits ~18,000).</summary>
        public void OnSplit()
        {
            splits++;
            if (splits <= 3 || splits % 1000 == 0)
                Log("D2 갈라지는 먼지: split " + splits + " this run, " + P.splitPieces + " pieces of " + (P.splitPieceMassShare * 100).ToString("0") + "% mass");
        }

        /// <summary>D2: large dust split so far this run.</summary>
        public int Splits => splits;

        /// <summary>D5 먼지 맥동: seconds between pulses at the owned level (0 = not owned).</summary>
        public double PulseEvery
        {
            get
            {
                int l = Level("D5");
                var e = P.pulseEverySec;
                if (l <= 0 || e == null || e.Length == 0) return 0;
                return e[Math.Min(l, e.Length) - 1];
            }
        }

        /// <summary>D5: 0..1 how far into the current pulse (−1 = no pulse now). Pulses start at every, 2 × every, … of the run clock.</summary>
        public double PulsePhase(RunState run)
        {
            double every = PulseEvery;
            if (every <= 0 || run == null || run.elapsed < every || P.pulseSec <= 0) return -1;
            int index = (int)Math.Floor(run.elapsed / every);
            double into = run.elapsed - index * every;
            if (into >= P.pulseSec) return -1;
            if (index > pulsesLogged)
            {
                pulsesLogged = index;
                Log("D5 먼지 맥동: pulse " + index + " at " + run.elapsed.ToString("F1") + "s, spawn x" + P.pulseSpawnMult.ToString("0.##") +
                    " for " + P.pulseSec.ToString("0.#") + "s (every " + every.ToString("0.#") + "s)");
            }
            return into / P.pulseSec;
        }

        /// <summary>
        /// D5 and A5: what the dust spawn is multiplied by on this tick (both at once multiply). The dust cap still
        /// holds: at the cap nothing spawns, whatever this says.
        /// </summary>
        public double SpawnMult(RunState run)
        {
            double m = 1;
            if (PulsePhase(run) >= 0) m *= P.pulseSpawnMult;
            if (InAftermath(run)) m *= P.aftermathSpawnMult;
            return m;
        }

        // ---------------- gravity (G1, G2, G3) ----------------

        /// <summary>
        /// Once a tick, after the gravity source moved: how long the hold has lasted (G1) and whether the source stood
        /// still this tick (G3).
        /// </summary>
        public void Track(double dt, bool holding, Vector2 gravity)
        {
            holdSec = holding ? holdSec + dt : 0;
            if (!holding) deepLogged = false;
            else if (!deepLogged && Level("G1") > 0 && holdSec >= P.deepHoldStartSec + P.deepHoldRampSec)
            {
                deepLogged = true;
                Log("G1 깊어지는 중력: held " + holdSec.ToString("0.#") + "s, gravity radius +" + (P.deepHoldMax * 100).ToString("0") + "%");
            }
            if (haveGravity && dt > 0)
            {
                double speedPx = (gravity - lastGravity).magnitude * p.pixelsPerUnit / dt;
                bool now = holding && speedPx < P.stillSpeedPx;
                if (now && !still && Level("G3") > 0) Log("G3 고요한 중심: the gravity source is still, pull x" + P.stillPullMult.ToString("0.##"));
                still = now;
            }
            lastGravity = gravity;
            haveGravity = true;
        }

        /// <summary>G1 깊어지는 중력: the radius share added by the hold so far, 0..deepHoldMax.</summary>
        public double DeepHoldShare
        {
            get
            {
                if (Level("G1") <= 0 || holdSec <= P.deepHoldStartSec) return 0;
                double k = P.deepHoldRampSec > 0 ? Math.Min(1.0, (holdSec - P.deepHoldStartSec) / P.deepHoldRampSec) : 1.0;
                return P.deepHoldMax * k;
            }
        }

        /// <summary>G1 and G3 on this tick's stats (after the abilities' buffs).</summary>
        public void Amplify(ref EffectiveStats s)
        {
            double deep = DeepHoldShare;
            if (deep > 0) s.gravityRadius *= 1.0 + deep;
            if (still && Level("G3") > 0) s.pullAccel *= P.stillPullMult;
        }

        /// <summary>
        /// G2 잔류 중력: whether the dust is pulled this tick. A release keeps the gravity on for lingerSec; what it
        /// brings in then is held for the next body.
        /// </summary>
        public bool DustHolding(double dt, bool wasHolding, bool holding)
        {
            if (holding)
            {
                lingerLeft = 0;
                return true;
            }
            if (wasHolding && Level("G2") > 0)
            {
                lingerLeft = P.lingerSec;
                Log("G2 잔류 중력: the pull stays " + P.lingerSec.ToString("0.##") + "s after the release");
            }
            if (lingerLeft <= 0) return false;
            lingerLeft -= dt;
            return true;
        }

        /// <summary>G2: seconds of lingering gravity left (the gravity ring stays drawn meanwhile).</summary>
        public double LingerLeft => Math.Max(0, lingerLeft);

        // ---------------- bodies (C1, C2, C4, T2; C3 and T3 through RushMult) ----------------

        /// <summary>C4 쉬운 묶음: the bundle threshold after the rule.</summary>
        public double BundleAt(double baseAt)
        {
            int l = Level("C4");
            return l > 0 ? Math.Max(1.5, baseAt - P.bundleAtCutPerLevel * l) : baseAt;
        }

        /// <summary>
        /// A body (or a bundle) was completed. Returns what its income is multiplied by; <paramref name="count"/> is
        /// doubled by C2 (<paramref name="nth"/> true then). Also adds T2's time.
        /// </summary>
        public double OnComplete(RunState run, int tier, ref double count, out bool nth)
        {
            nth = false;
            completions++;
            double mult = 1;
            double now = run != null ? run.elapsed : 0;

            int c2 = Level("C2");
            if (c2 > 0 && P.everyNth != null && P.everyNth.Length > 0)
            {
                int n = P.everyNth[Math.Min(c2, P.everyNth.Length) - 1];
                if (n > 0 && completions % n == 0)
                {
                    count *= P.nthMult;
                    nth = true;
                    Log("C2 열 번째: completion " + completions + " (every " + n + "th) becomes a x" + count.ToString("0") + " bundle");
                }
            }

            if (Level("C1") > 0)
            {
                chain = lastCompleteAt >= 0 && now - lastCompleteAt <= P.chainWindowSec ? chain + 1 : 0;
                if (chain > 0)
                {
                    mult *= 1.0 + ChainBonus;
                    Log("C1 연쇄 완성: chain " + chain + ", income +" + (ChainBonus * 100).ToString("0") + "%");
                    ChainShown?.Invoke(chain, ChainBonus);
                }
            }
            lastCompleteAt = now;

            if (Level("T2") > 0 && timeAdded < P.completeTimeMaxSec)
            {
                double add = Math.Min(P.completeTimeSec, P.completeTimeMaxSec - timeAdded);
                timeAdded += add;
                AddTime(run, add, "T2 완성의 시간 (" + timeAdded.ToString("0.#") + "/" + P.completeTimeMaxSec.ToString("0") + "s this run)");
            }

            mult *= RushMult(run);
            return mult;
        }

        /// <summary>D4: one line at the end of the run.</summary>
        public void EndRun(int goldCaptured)
        {
            if (Level("D2") > 0) Log("D2 갈라지는 먼지: " + splits + " large dust split this run");
            if (Level("D4") > 0) Log("D4 황금 먼지: " + goldCaptured + " golden dust taken this run (x" + P.goldMassMult.ToString("0.#") + " mass each)");
        }

        // ---------------- abilities (A1, A2) ----------------

        /// <summary>A2 버프 가속: how fast the abilities other than the crystal charge (× buffChargeMult while it is on).</summary>
        public double ChargeRate(Ability a, bool crystalBuffed)
        {
            if (!crystalBuffed || Level("A2") <= 0 || a == null || a.Def.id == "crystal") return 1;
            return P.buffChargeMult;
        }

        /// <summary>A1 공명하는 능력: share of another ability's cooldown its charge gains when one ability fires.</summary>
        public double ResonanceShare => Level("A1") > 0 ? P.resonanceChargeShare : 0;

        /// <summary>A5 블랙홀 여파: the black hole has just closed.</summary>
        public void OnBlackHoleClosed(RunState run)
        {
            if (Level("A5") <= 0 || run == null || P.aftermathSec <= 0) return;
            aftermathUntil = run.elapsed + P.aftermathSec;
            Log("A5 블랙홀 여파: the black hole closed at " + run.elapsed.ToString("F1") + "s, spawn x" + P.aftermathSpawnMult.ToString("0.##") +
                " for " + P.aftermathSec.ToString("0.#") + "s");
        }

        /// <summary>A5: true while the spawn after a black hole runs faster.</summary>
        public bool InAftermath(RunState run) => run != null && aftermathUntil >= 0 && run.elapsed < aftermathUntil;

        /// <summary>A1 logs once per trigger.</summary>
        public void LogResonance(Ability from, int others)
        {
            if (others > 0) Log("A1 공명하는 능력: " + from.Def.id + " fired, " + others + " other abilities charged +" + (ResonanceShare * 100).ToString("0") + "%");
        }
    }
}
