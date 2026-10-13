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
        FishingScenarios.Clone = w => JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w, json), json);
        FishingScenarios.Snapshot = w => JsonSerializer.Serialize(w, json);
        var results = new List<object>(); int failed = 0;
        foreach (MethodInfo method in typeof(FishingScenarios).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0) continue;
            var timer = Stopwatch.StartNew();
            try { method.Invoke(null, null); results.Add(new { name = method.Name, passed = true, milliseconds = timer.Elapsed.TotalMilliseconds }); Console.WriteLine("PASS " + method.Name); }
            catch (Exception ex) { failed++; string error = (ex.InnerException ?? ex).ToString(); results.Add(new { name = method.Name, passed = false, error }); Console.WriteLine("FAIL " + method.Name + ": " + error); }
        }
        string path = args.Length > 0 ? args[0] : "Evidence/Simulation/fishing-probe.json"; Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, tests = results.Count, failures = failed, results, progression = FishingScenarios.Progression,
            assistance = "Controlled flat map, workshop and known initial recipe inputs; progression uses actual resident movement and local ingredient consumption. Not natural settlement progression or native rendering." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine((results.Count - failed) + "/" + results.Count + " PASS"); return failed == 0 ? 0 : 1;
    }
}
