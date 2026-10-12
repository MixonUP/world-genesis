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
        if (args.Length > 0 && args[0] == "--natural") return NaturalDiagnostics.Run(args);
        var json = new JsonSerializerOptions { IncludeFields = true };
        SocialScenarios.Clone = w => JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w, json), json);
        SocialScenarios.Snapshot = w => JsonSerializer.Serialize(w, json);
        var results = new List<object>(); int failures = 0;
        foreach (MethodInfo method in typeof(SocialScenarios).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0) continue;
            var timer = Stopwatch.StartNew();
            try { method.Invoke(null, null); results.Add(new { name = method.Name, passed = true, milliseconds = timer.Elapsed.TotalMilliseconds }); Console.WriteLine("PASS " + method.Name); }
            catch (Exception e) { failures++; string error = (e.InnerException ?? e).ToString(); results.Add(new { name = method.Name, passed = false, error, milliseconds = timer.Elapsed.TotalMilliseconds }); Console.WriteLine("FAIL " + method.Name + ": " + error); }
        }
        string path = args.Length > 0 ? args[0] : "Evidence/Simulation/social-probe.json";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, tests = results.Count, failures, runtime = Environment.Version.ToString(), results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine((results.Count - failures) + "/" + results.Count + " PASS"); return failures == 0 ? 0 : 1;
    }
}
