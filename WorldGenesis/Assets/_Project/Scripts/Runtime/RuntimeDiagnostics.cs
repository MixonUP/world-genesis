using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    /// <summary>Opt-in native Player measurements. No overhead in ordinary play.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class RuntimeDiagnostics : MonoBehaviour
    {
        private string reportPath;
        private float started, seconds;
        private int initialPopulation, requestedPopulation, startCollections;
        private bool initialized;
        private readonly List<float> frameTimes = new List<float>(12000);
        private readonly List<float> simulationTimes = new List<float>(12000);
        private long previousTick, measuredStartTick;
        private int measuredStartPopulation, peakPopulation, budgetLimitedFrames;
        private double maximumBacklog, lastLimitedRequested, measuredLimitedRequested;
        private float previousSpeed;
        private bool previousPaused;
        private int configurationChanges;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallWhenRequested()
        {
            string[] args = Environment.GetCommandLineArgs();
            int path = Array.IndexOf(args, "--profile-report");
            if (path < 0 || path + 1 >= args.Length) return;
            GameObject target = new GameObject("Opt-in Player measurement");
            RuntimeDiagnostics diagnostics = target.AddComponent<RuntimeDiagnostics>();
            diagnostics.reportPath = args[path + 1];
            diagnostics.seconds = 30;
            int duration = Array.IndexOf(args, "--profile-seconds");
            if (duration >= 0 && duration + 1 < args.Length && int.TryParse(args[duration + 1], out int requested)) diagnostics.seconds = Mathf.Clamp(requested, 5, 1800);
            int population = Array.IndexOf(args, "--stress-population");
            if (population >= 0 && population + 1 < args.Length && int.TryParse(args[population + 1], out int units)) diagnostics.requestedPopulation = Mathf.Clamp(units, 0, 10000);
        }

        private void Update()
        {
            GameBootstrap game = GameBootstrap.Instance;
            if (game == null || game.World == null) return;
            if (!initialized)
            {
                if (requestedPopulation > game.World.Units.Count && game.World.Settlements.Count > 0)
                {
                    SettlementData town = game.World.Settlements[0];
                    int missing = requestedPopulation - game.World.Units.Count;
                    while (missing > 0)
                    {
                        int batch = Math.Min(1000, missing);
                        game.Engine.Spawn(town.Species, town.X, town.Y, batch); missing -= batch;
                    }
                }
                initialPopulation = game.World.Units.Count; started = Time.realtimeSinceStartup; initialized = true;
                startCollections = GC.CollectionCount(0); previousTick = game.World.Tick;
            }
            float elapsed = Time.realtimeSinceStartup - started;
            if (elapsed < 3) { previousTick = game.World.Tick; return; } // Exclude loading and shader warm-up explicitly.
            if (frameTimes.Count == 0) { measuredStartTick = previousTick; measuredStartPopulation = game.World.Units.Count; lastLimitedRequested = game.LimitedRequestedSeconds; previousSpeed = game.Speed; previousPaused = game.Paused; startCollections = GC.CollectionCount(0); }
            double currentLimited = game.LimitedRequestedSeconds;
            measuredLimitedRequested += currentLimited >= lastLimitedRequested ? currentLimited - lastLimitedRequested : currentLimited;
            lastLimitedRequested = currentLimited;
            if (previousSpeed != game.Speed || previousPaused != game.Paused) { configurationChanges++; previousSpeed = game.Speed; previousPaused = game.Paused; }
            frameTimes.Add(Time.unscaledDeltaTime * 1000);
            simulationTimes.Add((float)game.SimulationWorkMilliseconds);
            peakPopulation = Math.Max(peakPopulation, game.World.Units.Count);
            maximumBacklog = Math.Max(maximumBacklog, game.SimulationBacklogSeconds);
            if (game.SimulationBudgetLimited) budgetLimitedFrames++;
            if (elapsed < seconds + 3) return;
            double total = 0;
            foreach (float time in frameTimes) total += time;
            frameTimes.Sort();
            simulationTimes.Sort();
            BackgroundMusic music = game.GetComponent<BackgroundMusic>();
            Report report = new Report
            {
                Utc = DateTime.UtcNow.ToString("o"), Kind = "native Windows Player frame sample",
                Unity = Application.unityVersion, Cpu = SystemInfo.processorType,
                Gpu = SystemInfo.graphicsDeviceName, RamMB = SystemInfo.systemMemorySize,
                Width = Screen.width, Height = Screen.height, MapWidth = game.World.Width, MapHeight = game.World.Height,
                Seed = game.World.Seed, RequestedStressPopulation = requestedPopulation,
                InitialPopulation = initialPopulation, FinalPopulation = game.World.Units.Count,
                WarmupSeconds = 3, MeasuredFrames = frameTimes.Count, MeasuredSeconds = total / 1000,
                MeanFrameMS = total / frameTimes.Count, P50FrameMS = Percentile(.50f),
                P95FrameMS = Percentile(.95f), P99FrameMS = Percentile(.99f),
                MaxFrameMS = frameTimes[frameTimes.Count - 1], MeanFPS = frameTimes.Count * 1000 / total,
                AllocatedMemoryBytes = Profiler.GetTotalAllocatedMemoryLong(), ReservedMemoryBytes = Profiler.GetTotalReservedMemoryLong(),
                Gen0Collections = GC.CollectionCount(0) - startCollections,
                Tick = game.World.Tick, Speed = game.Speed, Paused = game.Paused,
                MeasuredStartTick = measuredStartTick, MeasuredSimulationSeconds = (game.World.Tick - measuredStartTick) * (double)SimulationEngine.TickSeconds,
                ActualSpeed = (game.World.Tick - measuredStartTick) * (double)SimulationEngine.TickSeconds / (total / 1000),
                MeasuredStartPopulation = measuredStartPopulation, PeakPopulation = peakPopulation, BudgetLimitedFrames = budgetLimitedFrames,
                MaximumBacklogSeconds = maximumBacklog, LimitedRequestedSeconds = measuredLimitedRequested, SpeedOrPauseChanges = configurationChanges,
                SimulationP95MS = simulationTimes[Mathf.Clamp((int)Math.Ceiling(simulationTimes.Count * .95) - 1, 0, simulationTimes.Count - 1)],
                SimulationMaxMS = simulationTimes[simulationTimes.Count - 1]
            };
            if (music != null)
            {
                report.MusicTracks = music.TrackCount; report.MusicTrackIndex = music.CurrentTrackIndex;
                report.MusicPlaybackActive = music.IsPlaybackActive; report.MusicMuted = music.Muted;
                report.MusicTransitions = music.CompletedTransitions;
                foreach (AudioSource source in music.GetComponentsInChildren<AudioSource>())
                {
                    if (!source.name.StartsWith("Background music ") || !source.isPlaying) continue;
                    if (source.time > report.MusicPlaybackSeconds) { report.MusicPlaybackSeconds = source.time; report.MusicPitch = source.pitch; }
                }
            }
            try
            {
                string path = Path.GetFullPath(reportPath); Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                Debug.Log("PLAYER_PROFILE_WRITTEN " + path);
            }
            catch (Exception exception) { Debug.LogError("Player profile could not be written: " + exception.Message); }
            enabled = false;
        }
        private float Percentile(float p) { return frameTimes[Mathf.Clamp((int)Math.Ceiling(frameTimes.Count * p) - 1, 0, frameTimes.Count - 1)]; }
        [Serializable]
        private sealed class Report
        {
            public string Utc, Kind, Unity, Cpu, Gpu;
            public int RamMB, Width, Height, MapWidth, MapHeight, Seed, RequestedStressPopulation, InitialPopulation, FinalPopulation, WarmupSeconds, MeasuredFrames, Gen0Collections;
            public double MeasuredSeconds, MeanFrameMS, MeanFPS;
            public float P50FrameMS, P95FrameMS, P99FrameMS, MaxFrameMS, Speed;
            public long AllocatedMemoryBytes, ReservedMemoryBytes, Tick;
            public bool Paused;
            public int MeasuredStartPopulation, PeakPopulation, BudgetLimitedFrames, SpeedOrPauseChanges;
            public long MeasuredStartTick;
            public double MeasuredSimulationSeconds, ActualSpeed, MaximumBacklogSeconds, LimitedRequestedSeconds;
            public float SimulationP95MS, SimulationMaxMS;
            public int MusicTracks, MusicTrackIndex, MusicTransitions;
            public bool MusicPlaybackActive, MusicMuted;
            public float MusicPlaybackSeconds, MusicPitch;
        }
    }
}
