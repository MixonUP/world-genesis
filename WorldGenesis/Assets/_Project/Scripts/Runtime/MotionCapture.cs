using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    /// <summary>Opt-in native frame sequence, with wall time and simulation tick receipts.</summary>
    public sealed class MotionCapture : MonoBehaviour
    {
        [Serializable] private sealed class Frame { public int index, observedUnit; public double wallSeconds; public long tick; public string task, cargo; public float cargoAmount, x, y; }
        [Serializable] private sealed class Receipt
        {
            public string kind = "Native Unity Player screenshots sampled at 10 Hz; diagnostic capture affects performance";
            public int seed, width, height;
            public List<Frame> frames = new List<Frame>();
        }

        private IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--capture-motion");
            if (index < 0 || index + 1 >= args.Length) { Destroy(this); yield break; }
            string folder = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(folder);
            float seconds = 6;
            int durationIndex = Array.IndexOf(args, "--motion-seconds");
            if (durationIndex >= 0 && durationIndex + 1 < args.Length && float.TryParse(args[durationIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float requested))
                seconds = Mathf.Clamp(requested, 1, 20);
            float delay = 3;
            int delayIndex = Array.IndexOf(args, "--motion-delay");
            if (delayIndex >= 0 && delayIndex + 1 < args.Length && float.TryParse(args[delayIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float requestedDelay)) delay = Mathf.Clamp(requestedDelay, 0, 180);
            yield return new WaitForSecondsRealtime(delay);
            GameBootstrap game = GameBootstrap.Instance;
            bool observeWorker = Array.IndexOf(args, "--inspect-resource-worker") >= 0;
            bool observeCaravan = Array.IndexOf(args, "--inspect-caravan") >= 0;
            if (observeCaravan) game.InspectCaravan(9);
            bool logDelivery = Array.IndexOf(args, "--observe-log-delivery") >= 0;
            if (logDelivery)
            {
                double deadline = Time.realtimeSinceStartupAsDouble + 30;
                while (!game.InspectResourceWorker(7, true))
                {
                    if (Time.realtimeSinceStartupAsDouble >= deadline) { Debug.LogError("No real log delivery observed during capture window."); Application.Quit(2); yield break; }
                    yield return null;
                }
                observeWorker = true;
            }
            else if (observeWorker) game.InspectResourceWorker(7);
            Receipt receipt = new Receipt { seed = game.World.Seed, width = Screen.width, height = Screen.height };
            double start = Time.realtimeSinceStartupAsDouble;
            int frame = 0;
            while (Time.realtimeSinceStartupAsDouble - start < seconds)
            {
                yield return new WaitForEndOfFrame();
                UnitData observed = game.World.GetUnit(game.SelectedUnitId);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "frame-" + frame.ToString("D4") + ".png"));
                receipt.frames.Add(new Frame { index = frame++, wallSeconds = Time.realtimeSinceStartupAsDouble - start, tick = game.World.Tick, observedUnit = observed?.Id ?? 0, task = observed?.Task.ToString(), cargo = observed?.CargoKind.ToString(), cargoAmount = observed?.CargoAmount ?? 0, x = observed?.X ?? 0, y = observed?.Y ?? 0 });
                if ((observeWorker || observeCaravan) && observed != null) game.FocusWorldPoint(new Vector2(observed.X, observed.Y), observeCaravan ? 9 : 7);
                double next = start + frame * .1;
                while (Time.realtimeSinceStartupAsDouble < next) yield return null;
            }
            yield return new WaitForSecondsRealtime(.5f);
            File.WriteAllText(Path.Combine(folder, "motion.json"), JsonUtility.ToJson(receipt, true));
            if (Array.IndexOf(args, "--quit-after-motion") >= 0) Application.Quit();
            Destroy(this);
        }
    }
}
