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
    private sealed class Envelope { public int Schema = 1; public string Format = "WorldGenesis"; public WorldState World; }
    private static int Main(string[] args)
    {
        var json = new JsonSerializerOptions { IncludeFields = true, IgnoreReadOnlyProperties = true };
        SubsistenceScenarios.Clone = w => JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w, json), json);
        SubsistenceScenarios.Snapshot = w => JsonSerializer.Serialize(w, json);
        if (args.Length > 0 && args[0] == "--showcase") return Showcase(json);
        if (args.Length > 0 && args[0] == "--sustain") return Sustain(json);
        var results = new List<object>(); int failures = 0;
        foreach (MethodInfo method in typeof(SubsistenceScenarios).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0) continue;
            try { method.Invoke(null, null); results.Add(new { name = method.Name, passed = true }); Console.WriteLine("PASS " + method.Name); }
            catch (Exception ex) { failures++; string error = (ex.InnerException ?? ex).ToString(); results.Add(new { name = method.Name, passed = false, error }); Console.WriteLine("FAIL " + method.Name + ": " + error); }
        }
        string path = args.Length > 0 ? args[0] : "Evidence/Simulation/subsistence-probe.json";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, tests = results.Count, failures, results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }

    private static int Showcase(JsonSerializerOptions json)
    {
        var engine = (SimulationEngine)typeof(SubsistenceScenarios).GetMethod("Fixture", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { 25 });
        WorldState world = engine.World; SettlementData town = world.Settlements[0]; BuildingData hall = world.GetCampfire(town.Id);
        foreach (BuildingData building in world.Buildings) if (building.Type != BuildingType.Hall) building.Health = 0;
        typeof(SimulationEngine).GetMethod("DestroyInvalidBuildings", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(engine, null);
        int index = 0;
        foreach (UnitData unit in world.Units)
        {
            unit.X = town.X + .5f + index % 5 - 2; unit.Y = town.Y + .5f + index / 5 - 2;
            unit.DecisionTimer = 0; index++;
        }
        for (int tick = 0; tick < 600; tick++)
        {
            engine.Step(.1f);
            if (!world.Validate(out string error)) throw new InvalidOperationException("Showcase tick " + tick + ": " + error);
        }
        if (world.Units.Count != 25 || Math.Abs(town.Food - 2.5f) > .003f || Math.Abs(hall.WaterStored - 2.5f) > .003f || world.Buildings.Count != 1)
            throw new InvalidOperationException("Showcase did not preserve its single-source population and production budget.");
        string output = "Builds/subsistence-assisted-v051.save.json", report = "Evidence/Simulation/subsistence-assisted-v051.json";
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonSerializer.Serialize(new Envelope { World = world }, json));
        var restored = new SimulationEngine(JsonSerializer.Deserialize<Envelope>(File.ReadAllText(output), json).World);
        for (int tick = 0; tick < 100; tick++) { engine.Step(.1f); restored.Step(.1f); }
        if (JsonSerializer.Serialize(world, json) != JsonSerializer.Serialize(restored.World, json)) throw new InvalidOperationException("Showcase save continuation differs.");
        WorldState saved = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(output), json).World;
        SettlementData savedTown = saved.Settlements[0]; BuildingData savedHall = saved.GetCampfire(savedTown.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(report));
        File.WriteAllText(report, JsonSerializer.Serialize(new
        {
            utc = DateTime.UtcNow, output, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))),
            assistance = "Authored flat 48x48 grass fixture, seed80525. Explicit spawn of25 adults with initial need0.1 and arranged starting positions. Generated farm removed, natural wildlife/resources/vegetation disabled; weather, births, aging and warfare disabled. Initial town food28 was removed, no food/water/technology/material grants. Residents make ordinary decisions during the600-tick pre-roll. This is an assisted single-source demonstration, not a naturally developed town.",
            width = saved.Width, height = saved.Height, seed = saved.Seed, tick = saved.Tick,
            population = saved.Units.Count, buildings = saved.Buildings.Count, food = savedTown.Food, water = savedHall.WaterStored,
            campfireId = savedHall.Id, inspectX = savedHall.X, inspectY = savedHall.Y,
            daySeconds = SubsistenceRules.DaySeconds, supportedPeople = SubsistenceRules.CampfirePeople,
            productionPerSecond = SubsistenceRules.ProductionPerSecond, foodPerPersonPerDay = SubsistenceRules.FoodPerPersonPerDay,
            waterPerPersonPerDay = SubsistenceRules.WaterPerPersonPerDay,
            wells = saved.Buildings.Count(b => b.Type == BuildingType.Well), farms = saved.Buildings.Count(b => b.Type == BuildingType.Farm),
            validatedTicks = 600, continuedTicks = 100, continuationEqual = true
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Prepared single-campfire25-person showcase;600 valid ticks and100 identical continuation ticks: " + report);
        return 0;
    }

    private static int Sustain(JsonSerializerOptions json)
    {
        const int requestedTicks = 30000;
        string input = "Builds/subsistence-assisted-v051.save.json", report = "Evidence/Simulation/subsistence-sustain-final.json";
        string inputJson = File.ReadAllText(input);
        WorldState world = JsonSerializer.Deserialize<Envelope>(inputJson, json).World;
        var engine = new SimulationEngine(world); SettlementData town = world.Settlements[0]; BuildingData hall = world.GetCampfire(town.Id);
        var residents = world.Units.ToArray(); var oldHunger = new float[residents.Length]; var oldThirst = new float[residents.Length];
        var oldTasks = new UnitTask[residents.Length]; var mealCounts = new int[residents.Length]; var drinkCounts = new int[residents.Length];
        var failures = new List<string>(); var samples = new List<object>();
        float initialFood = town.Food, initialWater = hall.WaterStored, minimumFood = town.Food, minimumWater = hall.WaterStored;
        float maximumHunger = residents.Max(u => u.Hunger), maximumThirst = residents.Max(u => u.Thirst), minimumHealth = residents.Min(u => u.Health);
        double generatedFood = 0, generatedWater = 0; int totalMeals = 0, totalDrinks = 0, physicalMeals = 0, physicalDrinks = 0, validatedTicks = 0;
        long startTick = world.Tick; var timer = Stopwatch.StartNew();
        if (residents.Length != 25 || world.Buildings.Count != 1 || hall.Type != BuildingType.Hall || town.Food > 2.51f || hall.WaterStored > 2.51f ||
            world.Laws.Births || world.Laws.Aging || world.Laws.Warfare || world.Laws.VegetationGrowth || world.NaturalWildlife || world.Weather.Automatic)
            throw new InvalidOperationException("Sustain requires the disclosed single-campfire25-person showcase without gifts or alternate producers.");
        for (int tick = 0; tick < requestedTicks; tick++)
        {
            for (int i = 0; i < residents.Length; i++) { oldHunger[i] = residents[i].Hunger; oldThirst[i] = residents[i].Thirst; oldTasks[i] = residents[i].Task; }
            if (SubsistenceRules.IsWorkingCampfire(world, hall))
            {
                float produced = SubsistenceRules.ProductionPerSecond * SimulationEngine.TickSeconds;
                generatedFood += Math.Min(produced, Math.Max(0, SubsistenceRules.FoodReserve - town.Food));
                generatedWater += Math.Min(produced, Math.Max(0, SubsistenceRules.WaterReserve - hall.WaterStored));
            }
            engine.Step(SimulationEngine.TickSeconds);
            if (!world.Validate(out string error)) { failures.Add("Validation at tick " + world.Tick + ": " + error); break; }
            validatedTicks++;
            minimumFood = Math.Min(minimumFood, town.Food); minimumWater = Math.Min(minimumWater, hall.WaterStored);
            for (int i = 0; i < residents.Length; i++)
            {
                UnitData unit = residents[i];
                maximumHunger = Math.Max(maximumHunger, unit.Hunger); maximumThirst = Math.Max(maximumThirst, unit.Thirst); minimumHealth = Math.Min(minimumHealth, unit.Health);
                float distance = (unit.X - hall.X - .5f) * (unit.X - hall.X - .5f) + (unit.Y - hall.Y - .5f) * (unit.Y - hall.Y - .5f);
                float hungerRelief = oldHunger[i] + SubsistenceRules.HungerPerSecond * SimulationEngine.TickSeconds - unit.Hunger;
                float thirstRelief = oldThirst[i] + SubsistenceRules.ThirstPerSecond * SimulationEngine.TickSeconds - unit.Thirst;
                if (hungerRelief > .1f)
                {
                    totalMeals++; mealCounts[i]++;
                    if (oldTasks[i] == UnitTask.Eat && distance < 2 && Math.Abs(hungerRelief - SubsistenceRules.NeedReliefPerPortion) < .0001f) physicalMeals++;
                    else failures.Add("Nonphysical or nonportion food relief for resident " + unit.Id + " at tick " + world.Tick);
                }
                if (thirstRelief > .1f)
                {
                    totalDrinks++; drinkCounts[i]++;
                    if (oldTasks[i] == UnitTask.Drink && distance < 2 && Math.Abs(thirstRelief - SubsistenceRules.NeedReliefPerPortion) < .0001f) physicalDrinks++;
                    else failures.Add("Nonphysical or nonportion water relief for resident " + unit.Id + " at tick " + world.Tick);
                }
            }
            if (world.Units.Count != 25 || residents.Any(u => u.Health <= 0)) failures.Add("Resident died or population changed at tick " + world.Tick);
            if (town.Food < 0 || hall.WaterStored < 0) failures.Add("Negative stock at tick " + world.Tick);
            if (tick % 100 == 0 && (world.Buildings.Count != 1 || world.Cells.Any(t => t.Vegetation > 0 || t.WaterDepth > 0) || world.ResourceNodes.Any(n => n.Amount > 0) ||
                world.GroundStocks.Any(g => g.IsFood && g.Amount > 0) || world.AgriculturalDrops.Any(d => d.Amount > 0) || world.Infrastructure.Any(c => c.Stream > 0 || c.Fish > 0) ||
                hall.AgriculturalStock.Any(a => a > 0) || residents.Any(u => u.CarriedFood > 0 || u.CarriedWater > 0 || u.HarvestAmount > 0)))
                failures.Add("An alternate food/water source or carried gift appeared at tick " + world.Tick);
            if ((tick + 1) % 6000 == 0)
            {
                samples.Add(new { tick = world.Tick, elapsedEconomicDays = (tick + 1) * .1f / SubsistenceRules.DaySeconds, population = world.Units.Count,
                    food = town.Food, water = hall.WaterStored, totalMeals, totalDrinks, meanHunger = residents.Average(u => u.Hunger), meanThirst = residents.Average(u => u.Thirst),
                    meanMood = residents.Average(u => u.Mood), government = town.Government.ToString(), socialState = town.SocialState.ToString() });
                Console.WriteLine("Sustain day " + ((tick + 1) / 6000) + ": " + world.Units.Count + " alive, meals=" + totalMeals + ", drinks=" + totalDrinks);
            }
            if (failures.Count > 0) break;
        }
        timer.Stop();
        if (mealCounts.Any(n => n == 0) || drinkCounts.Any(n => n == 0)) failures.Add("At least one resident never physically ate or drank.");
        double foodBalanceError = initialFood + generatedFood - totalMeals - town.Food;
        double waterBalanceError = initialWater + generatedWater - totalDrinks - hall.WaterStored;
        if (Math.Abs(foodBalanceError) > .05 || Math.Abs(waterBalanceError) > .05) failures.Add("Stock ledger differs from actual observed portions beyond float accumulation tolerance.");
        Directory.CreateDirectory(Path.GetDirectoryName(report));
        File.WriteAllText(report, JsonSerializer.Serialize(new
        {
            utc = DateTime.UtcNow, passed = failures.Count == 0 && validatedTicks == requestedTicks, input,
            sourceSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))),
            assistance = "Continues the disclosed authored seed80525 single-campfire showcase at tick600 without any further state changes or grants. Flat48x48,25 adults; no farms, wells, natural vegetation, wildlife, stocks in transit or natural water. Births, aging, warfare, vegetation growth and automatic weather disabled. Normal resident decisions, mood, religion, rest and civic policies remain active.",
            scope = "Five additional economic days of a controlled single-source25-person settlement. This does not prove natural city balance, survival in disasters, housing adequacy or budgets including births, brewing and paid hiring.",
            requestedSimSeconds = requestedTicks * .1f, completedSimSeconds = (world.Tick - startTick) * .1f,
            startTick, endTick = world.Tick, economicDaySeconds = SubsistenceRules.DaySeconds, validatedTicks,
            initialPopulation = residents.Length, finalPopulation = world.Units.Count, initialFood, finalFood = town.Food, minimumFood,
            initialWater, finalWater = hall.WaterStored, minimumWater, generatedFood, generatedWater,
            totalMeals, physicalMeals, totalDrinks, physicalDrinks, foodBalanceError, waterBalanceError,
            maximumHunger, maximumThirst, minimumHealth, wallSeconds = timer.Elapsed.TotalSeconds,
            residents = residents.Select((u, i) => new { u.Id, u.Name, u.Health, u.Hunger, u.Thirst, u.Mood, task = u.Task.ToString(), meals = mealCounts[i], drinks = drinkCounts[i] }).ToArray(),
            samples, failures
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine((failures.Count == 0 ? "PASS" : "FAIL") + " controlled subsistence sustain: " + report);
        foreach (string failure in failures) Console.WriteLine(failure);
        return failures.Count == 0 && validatedTicks == requestedTicks ? 0 : 1;
    }
}
