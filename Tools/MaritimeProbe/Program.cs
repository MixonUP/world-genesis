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
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    private static string Hash(string path) { return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))); }
    private static Dictionary<string, string> Sources()
    { return Directory.GetFiles("WorldGenesis/Assets/_Project/Scripts/Simulation", "*.cs", SearchOption.AllDirectories).OrderBy(p => p).ToDictionary(p => p.Replace('\\', '/'), p => Hash(p)); }
    private static int Main(string[] args)
    {
        MaritimeScenarios.Clone = w => JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w, Json), Json);
        MaritimeScenarios.Snapshot = w => JsonSerializer.Serialize(w, Json);
        Dictionary<string, string> before = Sources(); List<object> results = new List<object>(); int failed = 0;
        foreach (MethodInfo method in typeof(MaritimeScenarios).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0) continue;
            if (args.Length > 1 && !method.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase)) continue;
            Stopwatch timer = Stopwatch.StartNew();
            try { method.Invoke(null, null); results.Add(new { name = method.Name, passed = true, milliseconds = timer.Elapsed.TotalMilliseconds }); Console.WriteLine("PASS " + method.Name); }
            catch (Exception ex) { failed++; string error = (ex.InnerException ?? ex).ToString(); results.Add(new { name = method.Name, passed = false, error }); Console.WriteLine("FAIL " + method.Name + ": " + error); }
        }
        Dictionary<string, string> after = Sources(); string[] changed = before.Keys.Union(after.Keys).Where(p => !before.TryGetValue(p, out string a) || !after.TryGetValue(p, out string b) || a != b).ToArray();
        string path = args.Length > 0 ? args[0] : "Evidence/Simulation/maritime-probe.json";
        string directory = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, tests = results.Count, failures = failed, sourceUnchanged = changed.Length == 0, sourceChangedDuringRun = changed, sourceSHA256 = before, assemblySHA256 = Hash(Assembly.GetExecutingAssembly().Location), results,
            assistance = "Controlled coastal worlds and explicit inputs/research; real construction, port supply, crew approach, sea movement, trade/capture/provisions and save. Prepared ship fixture explicitly supplies some hulls/crew; construction separately tested. Not natural progression or native rendering." }, Json));
        Console.WriteLine((results.Count - failed) + "/" + results.Count + " PASS; sourceUnchanged=" + (changed.Length == 0));
        return failed == 0 && changed.Length == 0 && results.Count > 0 ? 0 : 1;
    }
}
