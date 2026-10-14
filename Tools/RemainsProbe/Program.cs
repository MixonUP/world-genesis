using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using WorldGenesis.Simulation;
using WorldGenesis.Tests;
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--showcase") { RemainsShowcase.Generate(); return 0; }
        var json = new JsonSerializerOptions { IncludeFields = true, IgnoreReadOnlyProperties = true };
        RemainsScenarios.Clone = w => JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w, json), json);
        RemainsScenarios.Snapshot = w => JsonSerializer.Serialize(w, json);
        var results = new List<object>(); int failures = 0;
        foreach (MethodInfo method in typeof(RemainsScenarios).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0) continue;
            try { method.Invoke(null, null); results.Add(new { name = method.Name, passed = true }); Console.WriteLine("PASS " + method.Name); }
            catch (Exception e) { failures++; string error = (e.InnerException ?? e).ToString(); results.Add(new { name = method.Name, passed = false, error }); Console.WriteLine("FAIL " + method.Name + ": " + error); }
        }
        Directory.CreateDirectory("Evidence/Simulation"); File.WriteAllText("Evidence/Simulation/remains-probe.json", JsonSerializer.Serialize(new { utc = DateTime.UtcNow, tests = results.Count, failures, results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
