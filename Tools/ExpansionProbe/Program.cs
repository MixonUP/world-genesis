using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using WorldGenesis.Simulation;
using WorldGenesis.Tests;
var json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
string source = "WorldGenesis/Assets/_Project/Scripts/Simulation";
string Hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
var hashes = Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories).ToDictionary(p => p, Hash);
SettlementExpansionScenarios.Clone = w => JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w, json), json);
SettlementExpansionScenarios.Snapshot = w => JsonSerializer.Serialize(w, json);
var results = new List<object>(); int passed = 0, failed = 0;
foreach (var test in typeof(SettlementExpansionScenarios).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0))
{
    try { test.Invoke(null, null); passed++; results.Add(new { test = test.Name, passed = true }); Console.WriteLine("PASS " + test.Name); }
    catch (Exception e) { failed++; string error = (e.InnerException ?? e).ToString(); results.Add(new { test = test.Name, passed = false, error }); Console.WriteLine("FAIL " + test.Name + ": " + error); }
}
if (failed == 0)
{
    var e = SettlementExpansionScenarios.Fixture(); var w = e.World; var home = w.Settlements[0];
    int target = w.Index(home.X + 32, home.Y); e.TryStartSettlementExpedition(home.Id, target);
    var expedition = w.SettlementExpeditions[0];
    SettlementExpansionScenarios.AdvanceUntil(e, () => expedition.Phase == SettlementExpeditionPhase.Travelling);
    for (int i = 0; i < 100; i++) e.Step(.1f);
    Directory.CreateDirectory("Builds/ExpansionShowcase");
    Export("Builds/ExpansionShowcase/expedition-march.save.json", w);
    SettlementExpansionScenarios.AdvanceUntil(e, () => !ExpansionCatalog.Active(expedition));
    Export("Builds/ExpansionShowcase/expedition-founded.save.json", w);
}
void Export(string path, WorldState world)
{
    File.WriteAllText(path, JsonSerializer.Serialize(new { Schema = 1, Format = "WorldGenesis", World = world }, json));
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var restored = new SimulationEngine(JsonSerializer.Deserialize<WorldState>(document.RootElement.GetProperty("World").GetRawText(), json));
    for (int tick = 0; tick < 120; tick++) restored.Step(.1f);
    if (!restored.World.Validate(out string error)) throw new InvalidOperationException("Export continuation: " + error);
}
bool unchanged = hashes.All(kv => File.Exists(kv.Key) && Hash(kv.Key) == kv.Value);
string output = args.Length > 0 ? args[0] : "Evidence/Simulation/expansion-probe.json";
Directory.CreateDirectory(Path.GetDirectoryName(output));
File.WriteAllText(output, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, passed, failed, sourceUnchanged = unchanged, sourceSHA256 = hashes, assemblySHA256 = Hash(Assembly.GetExecutingAssembly().Location), assistance = "Prepared flat96x96 town,24 existing citizens,4 real planks,30food/10water. Births,aging,natural wildlife/weather disabled to isolate migration. Commands and ordinary Step(.1), physical routes and actual goods. Three seeded autonomous site/founding scenarios. Not proof of unassisted long-term balance.", results }, json));
return failed == 0 && unchanged ? 0 : 1;
