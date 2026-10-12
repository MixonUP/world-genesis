using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace WorldGenesis.Runtime
{
    /// <summary>Opt-in native frame sequence, with wall time and simulation tick receipts.</summary>
    public sealed class MotionCapture : MonoBehaviour
    {
        [Serializable] private sealed class Frame { public int index; public double wallSeconds; public long tick; }
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
            yield return new WaitForSecondsRealtime(3);
            GameBootstrap game = GameBootstrap.Instance;
            Receipt receipt = new Receipt { seed = game.World.Seed, width = Screen.width, height = Screen.height };
            double start = Time.realtimeSinceStartupAsDouble;
            int frame = 0;
            while (Time.realtimeSinceStartupAsDouble - start < seconds)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "frame-" + frame.ToString("D4") + ".png"));
                receipt.frames.Add(new Frame { index = frame++, wallSeconds = Time.realtimeSinceStartupAsDouble - start, tick = game.World.Tick });
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
