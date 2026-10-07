using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// The 22 §4 / §7 performance measurement. Runs the scenes of the spec table one after another — the run screen at
    /// each dust count while holding, the result screen with a full solar system, the tree screen with every node — and
    /// writes one markdown table per scene to a file, then quits. Since the abilities (24) and the dust outside the
    /// screen (Abilities_Proto §7) there is one more scene at the end: the run screen at the dust ceiling with every
    /// ability forced to its top level and the band outside the screen on. The scenes before it are kept as 7주차
    /// measured them, without either, so the two tables compare line for line (Build_Check1).
    ///
    /// It exists so the numbers come from a Windows build and not from the editor: the build is started with
    /// <c>-perfbench</c> (and optionally <c>-perfout &lt;file&gt;</c>, <c>-perflabel &lt;name&gt;</c>), plays itself and
    /// leaves the table behind. The same thing runs from the editor through <see cref="DebugTools"/> for a quick look.
    ///
    /// Per frame it takes the frame time plus the two costs the spec asks to be split out: the dust simulation tick
    /// (<see cref="GameRoot.DustTickMs"/>, a fixed-tick cost, so several may land in one frame) and the dust mesh rebuild
    /// (<see cref="DustMeshRenderer.RebuildMs"/>). What is left of the frame is the rest (render and everything else).
    /// </summary>
    public sealed class PerfBench : MonoBehaviour
    {
        /// <summary>One line of the result table.</summary>
        public struct Scene
        {
            public string name;
            public int frames;
            public double avgFps;
            public double p99Fps;
            public double maxMs;
            public double simMs;
            public double meshMs;
            public double trailMs;
            public double restMs;
            public int trails;
        }

        GameRoot root;
        GameParams p;
        readonly List<Scene> results = new List<Scene>();
        readonly List<float> frames = new List<float>();
        double simAcc, meshAcc, trailAcc;
        int trailsSeen;
        string outFile;
        string label = "build";
        bool quitWhenDone;
        /// <summary>GameParams.dustMarginPx as the asset has it; the 7주차 scenes run with 0 and the last scene with this.</summary>
        double marginPx;

        /// <summary>The scenes that are left to run; the head is the one being measured.</summary>
        readonly Queue<Action> steps = new Queue<Action>();
        string stepName;
        float warmupLeft;
        float sampleLeft;
        float settleLeft;
        bool running;

        public bool Running => running;
        public IReadOnlyList<Scene> Results => results;

        public void Init(GameRoot gameRoot)
        {
            root = gameRoot;
            p = root.gameParams;
        }

        /// <summary>
        /// True when the process was started to run the benchmark (<c>-perfbench</c>). <see cref="OutFileArg"/> and
        /// <see cref="LabelArg"/> read the two optional arguments beside it.
        /// </summary>
        public static bool WantedOnCommandLine => HasArg("-perfbench");

        public static string OutFileArg => ArgValue("-perfout");
        public static string LabelArg => ArgValue("-perflabel");

        /// <summary>
        /// 22 §4 / §5: <c>-perfvortex &lt;share&gt;</c> and <c>-perftrails &lt;count&gt;</c> put the two 22 §5 values the
        /// measurement is about somewhere else than the asset, so one build gives both the baseline (0 and 0, the state
        /// before C) and the state after it. Nothing is written to the asset.
        /// </summary>
        public static double? VortexArg => Number("-perfvortex");
        public static double? TrailsArg => Number("-perftrails");
        /// <summary>22 §6: <c>-perfworld &lt;perRing&gt;</c> so the same build measures before and after the widening world.</summary>
        public static double? WorldArg => Number("-perfworld");

        static double? Number(string name)
        {
            string v = ArgValue(name);
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : (double?)null;
        }

        static bool HasArg(string name)
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        /// <summary>
        /// Queues every scene of the 22 §4 table and starts. The frame rate is uncapped first, or the numbers would be
        /// the monitor's refresh rate instead of what the game can do.
        /// </summary>
        public void Begin(string outPath, string runLabel, bool quitAfter)
        {
            if (running) return;
            outFile = string.IsNullOrEmpty(outPath) ? Path.Combine(root.DataDir, "perf_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".md") : outPath;
            if (!string.IsNullOrEmpty(runLabel)) label = runLabel;
            quitWhenDone = quitAfter;
            var vortex = VortexArg;
            var trails = TrailsArg;
            if (vortex.HasValue) p.vortexShare = vortex.Value;
            if (trails.HasValue) p.trailBudget = (int)trails.Value;
            var world = WorldArg;
            if (world.HasValue) p.worldScalePerRing = world.Value;
            marginPx = p.dustMarginPx;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            // A windowed player is held to the monitor's refresh rate by the desktop compositor whatever vSyncCount says,
            // and a measurement pinned to 60.0 says nothing about the headroom. Exclusive fullscreen gets past it; the
            // mode goes back when the process ends.
            if (!Application.isEditor)
                Screen.SetResolution(Screen.width, Screen.height, FullScreenMode.ExclusiveFullScreen);
            results.Clear();
            steps.Clear();
            var counts = p.perfBenchDustCounts;
            if (counts != null)
                foreach (double c in counts)
                {
                    int dust = (int)c;
                    steps.Enqueue(() => StageRun(dust, Abilities.None, false));
                }
            steps.Enqueue(StageResult);
            steps.Enqueue(StageTree);
            // The heaviest scenes the game can be asked for now: the dust ceiling (22 §7) with the band outside the
            // screen, which is part of the cap (so 15% of the dust sits out of sight), and every ability as the ring 11
            // tree gives them, first without the black hole and then with it open through the sample (24 §19.1). An
            // open black hole swallows the whole field in a couple of seconds, which would leave the sample measuring
            // an empty screen, so that scene refills the dust to the ceiling every tick: the worst case, not the game.
            steps.Enqueue(() => StageRun((int)p.dustCapCeiling, Abilities.TreeWithoutBlackHole, true));
            steps.Enqueue(() => StageRun((int)p.dustCapCeiling, Abilities.AllForced, true));
            running = true;
            // Nothing is measured until the process has settled: the window opens, the shader variants of the first frames
            // compile and the first save is written, and any of those is a one-off stall that would poison scene 1.
            settleLeft = (float)p.perfBenchSettleSec;
            if (steps.Count > 0) steps.Peek()();
        }

        /// <summary>Which abilities a run scene has (24): none, what the ring 11 tree gives minus the black hole, or everything forced to its top level.</summary>
        enum Abilities { None, TreeWithoutBlackHole, AllForced }

        /// <summary>
        /// Run screen, holding still in the middle, with the field filled to <paramref name="dust"/>. With
        /// <paramref name="abilities"/> None the ability nodes are taken back out of the ring start state, because 24 §6
        /// put them into rings 4-10 and the scenes of 7주차 had none; TreeWithoutBlackHole keeps what the ring 11 start
        /// state owns except the black hole's nodes; AllForced forces every ability to its top level (24 §7), and a
        /// forced black hole fires on the first held tick, so it is open through the sample. <paramref name="band"/> is
        /// the dust outside the screen, on or off the same way.
        /// </summary>
        void StageRun(int dust, Abilities abilities, bool band)
        {
            stepName = string.Format(CultureInfo.InvariantCulture,
                abilities == Abilities.AllForced ? "런 화면, 먼지 {0:N0}개 + 능력 전부 최대 레벨 (블랙홀 열림, 먼지 즉시 보충) + 화면 밖 먼지, 홀드 중"
                : abilities == Abilities.TreeWithoutBlackHole ? "런 화면, 먼지 {0:N0}개 + 능력 (블랙홀 제외, 트리대로) + 화면 밖 먼지, 홀드 중"
                : "런 화면, 먼지 {0:N0}개, 홀드 중", dust);
            root.Tree.Hide();
            root.Result.Hide();
            // Before the field is laid out: the run start and the staging below both fill the band from this value.
            p.dustMarginPx = band ? marginPx : 0;
            if (root.Phase != GamePhase.Run) root.StartRun();
            // The heaviest scene the game can reach: every node of rings 1-10 owned, so the dust kinds are unlocked at
            // full share. Without it the field would be base dust only and no trail would ever be drawn.
            root.Tools.ApplyRingStartState(root.celestialTable.MaxTier);
            if (abilities == Abilities.None) StripAbilityNodes(null);
            else if (abilities == Abilities.TreeWithoutBlackHole) StripAbilityNodes("blackhole");
            // Making the top tier would otherwise start the ending (15 §4.1), which stops the dust simulation and ends
            // the run in the middle of the sample. Marking the ending as already seen keeps the hold going.
            root.Meta.endingRun = 1;
            root.Meta.endingSeen = true;
            root.Tools.StageRunScene(20260929, dust, new Vector2(0.5f, 0.5f), Vector2.zero, root.celestialTable.MaxTier, 0.5);
            // The run must not end in the middle of the sample.
            root.Run.stamina = double.MaxValue / 4;
            if (root.Abilities != null)
            {
                // 24 §7: "all" forced and the level stepped up to the most any ability node has; forced abilities are
                // charged at once, so the first one fires in the warmup and they keep cycling through the sample.
                bool forced = abilities == Abilities.AllForced;
                root.Abilities.SetForced(forced ? root.Abilities.RowCount + 1 : 0);
                if (forced) root.Abilities.StepForcedLevel(1000);
                // Enough spawn to put every dust the black hole takes straight back, so the tick keeps its full count.
                if (forced) root.Override = new StatsOverride { enabled = true, dustCap = dust, spawnRate = dust / Math.Max(1e-3, Time.fixedDeltaTime) };
            }
        }

        /// <summary>
        /// The ability unlock and stat nodes back to level 0, so the tree gives no ability (the 7주차 scenes); or only
        /// those of <paramref name="abilityId"/>.
        /// </summary>
        void StripAbilityNodes(string abilityId)
        {
            foreach (var n in root.nodeTable.nodes)
            {
                if (n == null || !(n.kind == NodeKind.AbilityUnlock || !string.IsNullOrEmpty(n.abilityId))) continue;
                if (abilityId != null && n.abilityId != abilityId) continue;
                root.Meta.SetLevel(n.id, 0);
            }
        }

        /// <summary>
        /// Result screen with the 14 §9 measurement system: every orbit full (tiers 1-3 at the belt threshold, 4-8 at the
        /// individual maximum) and enough stars for a full sky. That is the "129 shader bodies" of the spec table.
        /// </summary>
        void StageResult()
        {
            stepName = "결과 화면, 셰이더 천체 129개";
            root.Tools.EndStaging();
            root.Tree.Hide();
            // The result screen is only the result screen once the run has actually ended: the phase is what stops the
            // dust simulation and hides the run's dust. Draining the stamina ends it the normal way on the next tick, and
            // the staged system goes up once that has happened (well inside the warmup).
            root.DebugDrainStamina();
            root.Tools.After(0.5f, ShowStagedSystem);
        }

        /// <summary>The 14 §9 measurement system, put up once the run has ended and the result screen is the screen.</summary>
        void ShowStagedSystem()
        {
            // Back to the blue giant the 14 §9 scene was measured with, not the black hole the run scene left behind.
            root.Meta.endingRun = 0;
            root.Meta.endingSeen = false;
            IdleDust();
            var c = root.celestialTable;
            var counts = new int[c.MaxTier];
            for (int t = 1; t <= c.MaxTier; t++)
            {
                if (t <= p.orbitBeltMaxTier) counts[t - 1] = p.orbitBeltThreshold;
                else if (t <= p.orbitMaxTier) counts[t - 1] = p.orbitIndividualMax;
                else counts[t - 1] = Mathf.CeilToInt(p.skyStarsPerRun / (float)Mathf.Max(1, c.MaxTier - p.orbitMaxTier));
            }
            var rec = new RunRecord
            {
                run = root.Meta.runCount, durationSec = 120, income = 1, tierCounts = counts,
                unlockedMaxTier = c.MaxTier, endReason = EndReason.Stamina,
            };
            root.Meta.highestTierCreated = c.MaxTier;
            root.Result.Show(rec, false);
            stagedSystem = rec;
        }

        RunRecord stagedSystem;

        /// <summary>Tree screen with every ring open, zoomed out until the outer ring is on screen.</summary>
        void StageTree()
        {
            stepName = "트리 화면, 노드 전부";
            root.Result.Hide();
            if (stagedSystem == null) root.DebugDrainStamina();
            IdleDust();
            while (Shop.UnlockNextFree(root.nodeTable, root.Meta) > 0) { }
            root.Tree.Show();
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            for (int i = 0; i < 30; i++) root.Tree.OnZoom(-1f, center);
        }

        /// <summary>
        /// Puts the dust field back to what the screens between runs really show (14 §5: the run-start dust as a still
        /// background). Without this the field would still hold the 8,000 dust the last run scene staged.
        /// </summary>
        void IdleDust()
        {
            var st = root.Effective;
            root.Dust.Reset((int)st.dustInitial, root.CameraArea(), root.CursorWorld, st);
        }

        void Next()
        {
            frames.Clear();
            simAcc = 0;
            meshAcc = 0;
            trailAcc = 0;
            trailsSeen = 0;
            if (steps.Count == 0)
            {
                Finish();
                return;
            }
            steps.Dequeue()();
            warmupLeft = (float)p.perfBenchWarmupSec;
            sampleLeft = (float)p.perfBenchSampleSec;
        }

        void Update()
        {
            if (!running) return;
            // Some display paths put vSync back; without holding it at 0 every frame the numbers are the refresh rate.
            if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
            if (settleLeft > 0f)
            {
                settleLeft -= Time.unscaledDeltaTime;
                if (settleLeft > 0f) return;
                Next();
                return;
            }
            // The dust tick and the mesh rebuild of the frame before this one; over a sample the two sums line up.
            simAcc += root.TakeDustTickMs();
            meshAcc += root.DustRenderer != null ? root.DustRenderer.TakeRebuildMs() : 0;
            if (root.TrailRenderer != null)
            {
                trailAcc += root.TrailRenderer.TakeRebuildMs();
                if (root.TrailRenderer.DrawnTrails > trailsSeen) trailsSeen = root.TrailRenderer.DrawnTrails;
            }
            if (warmupLeft > 0f)
            {
                warmupLeft -= Time.unscaledDeltaTime;
                simAcc = 0;
                meshAcc = 0;
                trailAcc = 0;
                trailsSeen = 0;
                return;
            }
            frames.Add(Time.unscaledDeltaTime);
            sampleLeft -= Time.unscaledDeltaTime;
            if (sampleLeft > 0f) return;
            Record();
            Next();
        }

        void Record()
        {
            if (frames.Count == 0) return;
            var sorted = new List<float>(frames);
            sorted.Sort();
            double sum = 0;
            foreach (float f in sorted) sum += f;
            float p99 = sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * 0.99f))];
            double sim = simAcc / frames.Count, mesh = meshAcc / frames.Count, trailMs = trailAcc / frames.Count;
            results.Add(new Scene
            {
                name = stepName,
                frames = sorted.Count,
                avgFps = sorted.Count / sum,
                p99Fps = 1.0 / p99,
                maxMs = sorted[sorted.Count - 1] * 1000.0,
                simMs = sim,
                meshMs = mesh,
                trailMs = trailMs,
                trails = trailsSeen,
                restMs = Math.Max(0, sum / sorted.Count * 1000.0 - sim - mesh - trailMs),
            });
        }

        void Finish()
        {
            running = false;
            p.dustMarginPx = marginPx;
            if (root.Abilities != null) root.Abilities.SetForced(0);
            root.Tools.EndStaging();
            string text = Report();
            try
            {
                File.WriteAllText(outFile, text, new UTF8Encoding(false));
                Debug.Log("[Perf] written to " + outFile);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Perf] could not write " + outFile + ": " + e.Message);
            }
            Debug.Log(text);
            if (quitWhenDone) Application.Quit();
        }

        /// <summary>The markdown table of 22 §4: one row per scene, frame cost split into dust tick / mesh / the rest.</summary>
        public string Report()
        {
            var sb = new StringBuilder();
            sb.Append("# 성능 측정 (").Append(label).Append(")\n\n");
            sb.Append("| 장면 | 프레임 | 평균 fps | 99퍼센타일 fps | 최대 프레임 ms | 먼지 틱 ms | 메시 재구성 ms | 궤적 메시 ms | 나머지 ms | 궤적 |\n");
            sb.Append("|---|---|---|---|---|---|---|---|---|---|\n");
            foreach (var s in results)
                sb.AppendFormat(CultureInfo.InvariantCulture, "| {0} | {1} | {2:F1} | {3:F1} | {4:F1} | {5:F2} | {6:F2} | {7:F2} | {8:F2} | {9} |\n",
                    s.name, s.frames, s.avgFps, s.p99Fps, s.maxMs, s.simMs, s.meshMs, s.trailMs, s.restMs, s.trails);
            sb.Append('\n').Append(Screen.width).Append('×').Append(Screen.height)
              .Append(Application.isEditor ? ", 에디터" : ", 빌드")
              .Append(", vSync ").Append(QualitySettings.vSyncCount)
              .Append(", ").Append(Screen.fullScreenMode)
              .Append(", vortexShare ").Append(p.vortexShare.ToString(CultureInfo.InvariantCulture))
              .Append(", trailBudget ").Append(p.trailBudget)
              .Append(", worldScalePerRing ").Append(p.worldScalePerRing.ToString(CultureInfo.InvariantCulture))
              .Append(", worldScale ").Append(root.WorldScale.ToString("F2", CultureInfo.InvariantCulture))
              .Append(", dustCap ").Append(root.Effective.dustCap.ToString("F0", CultureInfo.InvariantCulture))
              .Append(", dustMarginPx ").Append(marginPx.ToString(CultureInfo.InvariantCulture)).Append(" (마지막 장면만)")
              .Append(", 예열 ").Append(p.perfBenchWarmupSec).Append("초 / 표본 ").Append(p.perfBenchSampleSec).Append("초\n");
            return sb.ToString();
        }
    }
}
