using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using WorldGenesis.Simulation;

internal static class Program
{
    private static readonly AgriculturalProduct[] Products = { AgriculturalProduct.Fish, AgriculturalProduct.DelicacyFish, AgriculturalProduct.RiverPearl, AgriculturalProduct.AncientScale };
    private sealed class Tracked
    {
        public UnitData Unit;
        public int LastCaught, MaximumRod;
    }
    private static int Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "Evidence/Simulation/fishing-natural.json";
        int seconds = args.Length > 1 ? int.Parse(args[1]) : 600;
        double budget = args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 110;
        if (seconds < 1 || seconds > 600 || !double.IsFinite(budget) || budget < 1 || budget > 110) throw new ArgumentException("Seconds 1..600 and wall budget 1..110 required.");
        string sourceRoot = "WorldGenesis/Assets/_Project/Scripts/Simulation";
        Dictionary<string, string> before = Sources(sourceRoot);
        List<object> results = new List<object>();
        bool allValid = true, allComplete = true;
        Stopwatch total = Stopwatch.StartNew();
        foreach (int seed in new[] { 1618033, 271828 })
        {
            try
            {
                object result = Run(seed, seconds, Math.Min(budget / 2, Math.Max(0, budget - total.Elapsed.TotalSeconds)), out bool valid, out bool complete);
                results.Add(result); allValid &= valid; allComplete &= complete;
            }
            catch (Exception exception)
            { allValid = false; allComplete = false; results.Add(new { seed, error = exception.ToString(), invariantsPassed = false }); }
        }
        Dictionary<string, string> after = Sources(sourceRoot);
        string[] changed = before.Keys.Union(after.Keys).Where(p => !before.TryGetValue(p, out string a) || !after.TryGetValue(p, out string b) || a != b).ToArray();
        string folder = Path.GetDirectoryName(output); if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            utc = DateTime.UtcNow, kind = "Offline .NET production simulation natural opening smoke; not native performance or visual acceptance",
            startup = "WorldGenerator Island128x128, nearest ordinary non-snow walkable positions to four GameBootstrap anchors; Spawn9 civilization residents plus Spawn3 sheep per anchor. All initial grants are only the production Spawn contract.",
            mutationsAfterStartup = "Only SimulationEngine.Step(.1); no TryStartFishing commands, map edits, stock grants, research grants, professions, equipment, laws, weather or needs overridden",
            requestedSimulatedSecondsPerSeed = seconds, wallBudgetSeconds = budget, elapsedWallSeconds = total.Elapsed.TotalSeconds,
            allInvariantsPassed = allValid, allRequestedDurationsCompleted = allComplete, sourceUnchanged = changed.Length == 0,
            naturalRodOneToFiveProven = false, notes = "Catch increments and last species observed every tick, including retained references to deceased residents. Stored product maxima sampled every tick; ending fish stock can be zero after real conversion to Food. Active-fishing counters are unit-tick observations. No forced rare habitats or catches.",
            assemblySHA256 = Hash(Assembly.GetExecutingAssembly().Location), sourceSHA256 = before, sourceChangedDuringRun = changed, results
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Console.WriteLine("Receipt=" + output + "; valid=" + allValid + "; completed=" + allComplete + "; sourceUnchanged=" + (changed.Length == 0));
        return !allValid || changed.Length != 0 ? 1 : allComplete ? 0 : 2;
    }
    private static object Run(int seed, int seconds, double budget, out bool valid, out bool complete)
    {
        Stopwatch timer = Stopwatch.StartNew();
        WorldState world = WorldGenerator.Generate(seed, 128, 128);
        SimulationEngine engine = new SimulationEngine(world);
        List<object> commands = new List<object>();
        int[] ax = { 42, 85, 42, 85 }, ay = { 42, 85, 85, 42 };
        for (int race = 0; race < 4; race++)
        {
            int bx = -1, by = -1; float best = float.MaxValue;
            for (int y = 4; y < world.Height - 4; y++) for (int x = 4; x < world.Width - 4; x++)
            {
                if (!world.IsWalkable(x, y) || world.Cells[world.Index(x, y)].Terrain == TerrainType.Snow) continue;
                float score = (x - ax[race]) * (x - ax[race]) + (y - ay[race]) * (y - ay[race]);
                if (score < best) { best = score; bx = x; by = y; }
            }
            if (bx < 0) continue;
            engine.Spawn((SpeciesType)race, bx, by, 9); engine.Spawn(SpeciesType.Sheep, bx + 3, by + 2, 3);
            commands.Add(new { civilization = ((SpeciesType)race).ToString(), x = bx, y = by, residentsRequested = 9, sheepRequested = 3 });
        }
        Dictionary<int, Tracked> observed = new Dictionary<int, Tracked>();
        Dictionary<string, long> catches = FishingCatalog.All.ToDictionary(f => f.Id.ToString(), f => 0L);
        Dictionary<string, float> maxStored = Products.ToDictionary(p => p.ToString(), p => 0f);
        HashSet<int> everFishing = new HashSet<int>(), everActive = new HashSet<int>();
        List<object> timeline = new List<object>();
        long loadChecks = 0, taskTicks = 0, activeTicks = 0, catchCount = 0;
        long? firstCatchTick = null, firstUpgradeTick = null;
        int validations = 0, peakPopulation = world.Units.Count;
        string failure = null;
        valid = world.Validate(out failure); validations++;
        timeline.Add(Snapshot(world));
        foreach (UnitData unit in world.Units) observed[unit.Id] = new Tracked { Unit = unit, LastCaught = unit.FishCaughtCount, MaximumRod = FishingCatalog.RodLevel(unit) };
        int requestedTicks = seconds * 10;
        while (valid && world.Tick < requestedTicks)
        {
            if (timer.Elapsed.TotalSeconds >= budget) break;
            engine.Step(SimulationEngine.TickSeconds);
            peakPopulation = Math.Max(peakPopulation, world.Units.Count);
            foreach (UnitData unit in world.Units)
            {
                float load = SimulationEngine.CarriedLoad(unit), capacity = SimulationEngine.CarryCapacityFor(unit);
                loadChecks++;
                if (!float.IsFinite(load) || !float.IsFinite(capacity) || load < 0 || load > capacity + .0001f)
                { failure = "Load invariant failed at tick " + world.Tick + " unit " + unit.Id + ": " + load + "/" + capacity; valid = false; break; }
                if (!observed.ContainsKey(unit.Id)) observed[unit.Id] = new Tracked { Unit = unit, LastCaught = 0, MaximumRod = FishingCatalog.RodLevel(unit) };
                if (unit.Task == UnitTask.Fish) { taskTicks++; everFishing.Add(unit.Id); }
                if (FishingCatalog.IsActivelyFishing(world, unit)) { activeTicks++; everActive.Add(unit.Id); }
            }
            foreach (Tracked track in observed.Values)
            {
                UnitData unit = track.Unit;
                int rod = FishingCatalog.RodLevel(unit); track.MaximumRod = Math.Max(track.MaximumRod, rod);
                if (rod > 1 && firstUpgradeTick == null) firstUpgradeTick = world.Tick;
                int delta = unit.FishCaughtCount - track.LastCaught;
                if (delta < 0) { valid = false; failure = "Catch history decreased for unit " + unit.Id; break; }
                if (delta > 0)
                {
                    FishDefinition fish = FishingCatalog.Get(unit.FishSpeciesId);
                    if (fish == null || fish.MinimumRodLevel > rod || unit.Age < 16 || !SimulationEngine.IsCivilized(unit.Species))
                    { valid = false; failure = "Invalid observed catch for unit " + unit.Id; break; }
                    catches[fish.Id.ToString()] += delta; catchCount += delta; firstCatchTick ??= world.Tick;
                }
                track.LastCaught = unit.FishCaughtCount;
            }
            foreach (AgriculturalProduct product in Products)
            {
                float stock = world.Buildings.Where(b => b.Health > 0).Sum(b => b.AgriculturalStock[(int)product]);
                maxStored[product.ToString()] = Math.Max(maxStored[product.ToString()], stock);
            }
            if (world.Tick % 100 == 0 && valid) { validations++; valid = world.Validate(out failure); }
            if (world.Tick % 600 == 0) { timeline.Add(Snapshot(world)); Console.WriteLine("seed=" + seed + "; seconds=" + world.Tick / 10 + "; catches=" + catchCount + "; population=" + world.Units.Count); }
        }
        if (valid) { validations++; valid = world.Validate(out failure); }
        if (world.Tick % 600 != 0) timeline.Add(Snapshot(world));
        complete = world.Tick == requestedTicks;
        return new { seed, commands, requestedTicks, completedTicks = world.Tick, completedSimulatedSeconds = world.Tick * .1,
            completedRequestedDuration = complete, truncatedByWallBudget = !complete && valid, wallSeconds = timer.Elapsed.TotalSeconds,
            invariantsPassed = valid, failure, validations, loadChecks, peakPopulation, fishTaskUnitTicks = taskTicks,
            activelyFishingUnitTicks = activeTicks, distinctFishTaskResidents = everFishing.Count, distinctActiveFishers = everActive.Count,
            observedCatches = catchCount, catchesBySpecies = catches, firstCatchTick, firstManufacturedRodTick = firstUpgradeTick,
            maximumRodAcrossObservedResidents = observed.Values.Select(t => t.MaximumRod).DefaultIfEmpty().Max(),
            maximumRodDistributionAcrossObservedResidents = observed.Values.Where(t => SimulationEngine.IsCivilized(t.Unit.Species)).GroupBy(t => t.MaximumRod).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            maximumStoredProductsAcrossWorld = maxStored, finalProductLedger = Products.ToDictionary(p => p.ToString(), p => new {
                stored = world.Buildings.Where(b => b.Health > 0).Sum(b => b.AgriculturalStock[(int)p]),
                carried = world.Units.Where(u => u.HarvestProduct == p).Sum(u => u.HarvestAmount),
                ground = world.AgriculturalDrops.Where(d => d.Product == p).Sum(d => d.Amount), lost = world.LostAgriculture[(int)p]
            }), timeline };
    }
    private static object Snapshot(WorldState world)
    {
        return new { tick = world.Tick, creatures = world.Units.Count,
            citizens = world.Units.Count(u => SimulationEngine.IsCivilized(u.Species)), fishBiomass = world.Infrastructure.Sum(i => (double)i.Fish),
            liveAdultRodLevels = world.Units.Where(u => SimulationEngine.IsCivilized(u.Species) && u.Age >= 16).GroupBy(FishingCatalog.RodLevel).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            towns = world.Settlements.Select(s => new { s.Id, s.Name, race = s.Species.ToString(), s.Population, s.Food,
                s.ResearchRate, s.ActiveTechnology, unlockedTechnologies = s.UnlockedTechnologies.ToArray(),
                nearbyWaterCellsWithin18 = Enumerable.Range(0, world.Cells.Length).Count(i => (i % world.Width - s.X) * (i % world.Width - s.X) + (i / world.Width - s.Y) * (i / world.Width - s.Y) <= 18 * 18 && FishingCatalog.IsWater(world, i)),
                buildings = world.Buildings.Where(b => b.SettlementId == s.Id && b.Health > 0).GroupBy(b => b.Type).ToDictionary(g => g.Key.ToString(), g => g.Count()),
                fishProducts = Products.ToDictionary(p => p.ToString(), p => world.GetAgriculturalStock(s.Id, p)),
                rodMaterials = new { plank = world.GetSettlementStock(s.Id, ItemType.Plank), fiber = world.GetAgriculturalStock(s.Id, AgriculturalProduct.Fiber), hide = world.GetAgriculturalStock(s.Id, AgriculturalProduct.Hide) }
            }).ToArray() };
    }
    private static string Hash(string path) { return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))); }
    private static Dictionary<string, string> Sources(string root)
    { return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => p.Replace('\\', '/'), Hash); }
}
