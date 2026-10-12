using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using WorldGenesis.Simulation;
using WorldGenesis.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
        var json = new JsonSerializerOptions { IncludeFields = true };
        BiomeScenarios.Clone = w => JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w, json), json);
        BiomeScenarios.Snapshot = w => JsonSerializer.Serialize(w, json);
        var results = new List<object>(); int failures = 0;
        foreach (MethodInfo method in typeof(BiomeScenarios).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0) continue;
            var timer = Stopwatch.StartNew();
            try { method.Invoke(null, null); results.Add(new { name = method.Name, passed = true, milliseconds = timer.Elapsed.TotalMilliseconds }); Console.WriteLine("PASS " + method.Name); }
            catch (Exception e) { failures++; string error = (e.InnerException ?? e).ToString(); results.Add(new { name = method.Name, passed = false, error, milliseconds = timer.Elapsed.TotalMilliseconds }); Console.WriteLine("FAIL " + method.Name + ": " + error); }
        }
        string path = args.Length > 0 ? args[0] : "Evidence/Simulation/biome-probe.json";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var distribution = new List<object>();
        foreach (WorldPreset preset in Enum.GetValues(typeof(WorldPreset)))
        {
            int[] counts = new int[BiomeCatalog.Count]; int matchingEdges = 0, edges = 0;
            for (int seed = 1; seed <= 8; seed++)
            {
                WorldState w = WorldGenerator.Generate(seed * 7907, 96, 96, preset); BiomeCatalog.Generate(w);
                foreach (TileData cell in w.Cells) counts[cell.Biome]++;
                for (int y = 0; y < w.Height; y++) for (int x = 1; x < w.Width; x++) { edges++; if (w.Cells[w.Index(x,y)].Biome == w.Cells[w.Index(x-1,y)].Biome) matchingEdges++; }
            }
            distribution.Add(new { preset = preset.ToString(), seeds = 8, width = 96, height = 96, countsByBiomeId = counts, equalHorizontalNeighborFraction = matchingEdges / (double)edges });
        }
        File.WriteAllText(path, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, tests = results.Count, failures, runtime = Environment.Version.ToString(), results, distribution }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine((results.Count - failures) + "/" + results.Count + " PASS"); return failures == 0 ? 0 : 1;
    }
}
