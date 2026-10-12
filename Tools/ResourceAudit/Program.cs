using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorldGenesis.Simulation;

// Same authored opening commands as GameBootstrap.SeedLife. No stock grants, map
// edits, profession assignments, accelerated births or workshop creation.
WorldState world = WorldGenerator.Generate(1618033, 128, 128);
SimulationEngine engine = new SimulationEngine(world);
int[] ax = { 42, 85, 42, 85 }, ay = { 42, 85, 85, 42 };
for (int s = 0; s < 4; s++)
{
    int bx = -1, by = -1; float best = float.MaxValue;
    for (int y = 4; y < 124; y++) for (int x = 4; x < 124; x++)
    {
        if (!world.IsWalkable(x, y) || world.Cells[world.Index(x, y)].Terrain == TerrainType.Snow) continue;
        float score = (x - ax[s]) * (x - ax[s]) + (y - ay[s]) * (y - ay[s]);
        if (score < best) { best = score; bx = x; by = y; }
    }
    if (bx >= 0) { engine.Spawn((SpeciesType)s, bx, by, 9); engine.Spawn(SpeciesType.Sheep, bx + 3, by + 2, 3); }
}
var originalRegions = world.Settlements.Select(s => new { settlement = s.Id, s.Name,
    depositsWithin18Cells = world.ResourceNodes.Select((n,i) => new { n, x=i%128, y=i/128 })
    .Where(p => p.n.Amount > 0 && Math.Abs(p.x-s.X)<=18 && Math.Abs(p.y-s.Y)<=18)
    .GroupBy(p => p.n.Type).ToDictionary(g => g.Key.ToString(), g => g.Count()) }).ToArray();
var timeline = new List<object>();
var firstSawmill = new Dictionary<int,long>();
var exchanges = new Dictionary<string,int>();
int singleLoadChecks = 0;
Stopwatch timer = Stopwatch.StartNew();
for (int tick = 1; tick <= 4500; tick++)
{
    engine.Step(.1f);
    foreach (TradeRecord trade in world.Trades)
        if (trade.Tick == world.Tick)
        {
            string kind = trade.Kind.ToString();
            exchanges[kind] = exchanges.TryGetValue(kind, out int count) ? count + 1 : 1;
        }
    foreach (UnitData u in world.Units)
    {
        if (u.CargoAmount + u.CarriedFood + u.CarriedWood > 1.0001f) throw new Exception("Overloaded unit " + u.Id + " at " + world.Tick);
        singleLoadChecks++;
    }
    foreach (BuildingData b in world.Buildings)
        if (b.Type == BuildingType.Sawmill && !firstSawmill.ContainsKey(b.SettlementId)) firstSawmill.Add(b.SettlementId, world.Tick);
    if (tick % 100 == 0 && !world.Validate(out string error)) throw new Exception("tick " + tick + ": " + error);
    if (tick % 500 != 0) continue;
    timeline.Add(new { world.Tick, population = world.Units.Count, towns = world.Settlements.Select(s => new { s.Id, s.Population, s.Food, s.Wood,
        buildings = world.Buildings.Where(b=>b.SettlementId==s.Id).GroupBy(b=>b.Type).ToDictionary(g=>g.Key.ToString(),g=>g.Count()),
        demographics = new { men = world.Units.Count(u=>u.SettlementId==s.Id && u.Age>=16 && u.Sex==Sex.Male), women=world.Units.Count(u=>u.SettlementId==s.Id && u.Age>=16 && u.Sex==Sex.Female), children=world.Units.Count(u=>u.SettlementId==s.Id && u.Age<16) },
        policy = new { exchange=s.EconomyPolicy.ToString(), childLabor=s.ChildLabor.ToString(), resources=s.ResourceFocus.ToString(), s.TaxRate, s.LastDecreeReason },
        stocks = Enum.GetValues<ItemType>().Where(i=>i!=ItemType.None).ToDictionary(i=>i.ToString(),i=>world.GetSettlementStock(s.Id,i)),
        naturalTax = Enum.GetValues<ItemType>().Where(i=>i!=ItemType.None).ToDictionary(i=>i.ToString(),i=>world.GetTreasuryStock(s.Id,i)),
        deficits = Enum.GetValues<ItemType>().Where(i=>i!=ItemType.None).ToDictionary(i=>i.ToString(),i=>world.GetResourceBalance(s.Id,i).Deficit)
    }).ToArray() });
    Console.WriteLine("tick="+tick+" population="+world.Units.Count+" buildings="+world.Buildings.Count);
}
var receipt = new { utc = DateTime.UtcNow, scope = "Offline production C# core, original authored 128x128 opening; no Unity performance or visual claim", seed = world.Seed,
    ticks = world.Tick, validations = 45, singleLoadChecks, exchanges, passed = true, wallSeconds = timer.Elapsed.TotalSeconds, firstSawmill, originalRegions, timeline };
File.WriteAllText(args[0], JsonSerializer.Serialize(receipt, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
Console.WriteLine("Natural opening audit completed: "+args[0]);
