using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using WorldGenesis.Simulation;
using WorldGenesis.Tests;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    internal static string Hash(string path) { return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))); }
    internal static Dictionary<string, string> Sources()
    { return Directory.GetFiles("WorldGenesis/Assets/_Project/Scripts/Simulation", "*.cs", SearchOption.AllDirectories).OrderBy(p => p).ToDictionary(p => p.Replace('\\', '/'), p => Hash(p)); }
    internal static void Save(string path, object value)
    { string folder = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder); File.WriteAllText(path, JsonSerializer.Serialize(value, Json)); }
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--natural") return NaturalRun.Run(args);
        ProductionScenarios.Clone = w => JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w, Json), Json);
        ProductionScenarios.Snapshot = w => JsonSerializer.Serialize(w, Json);
        Dictionary<string, string> before = Sources(); List<object> results = new List<object>(); int failed = 0;
        foreach (MethodInfo method in typeof(ProductionScenarios).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0) continue;
            if (args.Length > 1 && !method.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase)) continue;
            Stopwatch timer = Stopwatch.StartNew();
            try { method.Invoke(null, null); results.Add(new { name = method.Name, passed = true, milliseconds = timer.Elapsed.TotalMilliseconds }); Console.WriteLine("PASS " + method.Name); }
            catch (Exception ex) { failed++; string error = (ex.InnerException ?? ex).ToString(); results.Add(new { name = method.Name, passed = false, error }); Console.WriteLine("FAIL " + method.Name + ": " + error); }
        }
        Dictionary<string, string> after = Sources(); string[] changed = before.Keys.Union(after.Keys).Where(p => !before.TryGetValue(p, out string a) || !after.TryGetValue(p, out string b) || a != b).ToArray();
        string path = args.Length > 0 ? args[0] : "Evidence/Simulation/production-probe.json";
        Save(path, new { utc = DateTime.UtcNow, tests = results.Count, failures = failed, sourceUnchanged = changed.Length == 0, sourceChangedDuringRun = changed, sourceSHA256 = before, assemblySHA256 = Hash(Assembly.GetExecutingAssembly().Location), results, batches = ProductionScenarios.Batches,
            assistance = "Controlled flat worlds, explicit research/workshop/material fixtures; real production worker movement and local recipe debit. Separate --natural mode gives no technology/material grants. Not native performance or artistic acceptance." });
        Console.WriteLine((results.Count - failed) + "/" + results.Count + " PASS; sourceUnchanged=" + (changed.Length == 0)); return failed == 0 && changed.Length == 0 && results.Count > 0 ? 0 : 1;
    }
}
