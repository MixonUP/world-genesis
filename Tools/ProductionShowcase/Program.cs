using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using WorldGenesis.Simulation;

internal static class Program
{
    private sealed class Envelope { public int Schema = 1; public string Format = "WorldGenesis"; public WorldState World; }
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, IgnoreReadOnlyProperties = true };
    private static WorldState world;
    private static SimulationEngine engine;
    private static SettlementData town;
    private static bool approach;
    private static readonly List<object> subjects = new List<object>();
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
    private static BuildingData Build(BuildingType type, int x, int y)
    {
        Require(engine.TryBuildInfrastructure(town.Id, type, x, y), "Cannot create paid " + type + " at " + x + "," + y);
        return world.GetBuilding(world.Cells[world.Index(x, y)].BuildingId);
    }
    private static UnitData Citizen(int x, int y, string name, SpeciesType species)
    {
        var before = new HashSet<int>(world.Units.Select(u => u.Id));
        engine.Spawn(SpeciesType.Human, x, y, 1);
        UnitData u = world.Units.First(v => !before.Contains(v.Id));
        u.Species = species; u.SettlementId = town.Id; u.Name = name; u.Age = 28;
        u.Sex = u.Id % 2 == 0 ? Sex.Female : Sex.Male; u.X = x + .5f; u.Y = y + .5f;
        u.Hunger = u.Thirst = u.Fatigue = u.SocialNeed = .02f;
        u.DecisionTimer = 60; u.Task = UnitTask.Idle; u.Path.Clear(); u.PathCursor = 0;
        return u;
    }
    private static void Work(BuildingData b, IndustryRecipeType id, string name, SpeciesType species, bool localInputs)
    {
        IndustryRecipe recipe = IndustryCatalog.Get(id);
        UnitData u = Citizen(approach && b.Type == BuildingType.Smithy ? b.X : b.X - 1,
            approach && b.Type == BuildingType.Smithy ? b.Y + 5 : b.Y - 1, name, species);
        if (localInputs)
        {
            foreach (ItemAmount input in recipe.Inputs) b.Inventory[(int)input.Item] += input.Amount * 2;
            foreach (AgriculturalAmount input in recipe.Agriculture) b.AgriculturalStock[(int)input.Product] += input.Amount * 2;
        }
        Require(engine.TryStartProduction(u.Id, b.Id, id), "Could not start " + id);
        subjects.Add(new { b.Id, building = b.Type.ToString(), b.X, b.Y, worker = u.Id, recipe = id.ToString(), localInputsGranted = localInputs });
    }
    private static int Main(string[] args)
    {
        try { return Run(args); } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static int Run(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "Builds/production-showcase.save.json";
        string report = args.Length > 1 ? args[1] : "Evidence/Simulation/production-showcase.json";
        approach = args.Contains("--approach");
        world = WorldGenerator.Generate(130700, 128, 128, WorldPreset.Island);
        engine = new SimulationEngine(world); world.NaturalWildlife = false;
        world.Laws.Births = world.Laws.Aging = world.Laws.Warfare = false;
        world.Weather.Automatic = false;
        for (int y = 48; y <= 81; y++) for (int x = 43; x <= 84; x++)
        {
            engine.Paint(TerrainType.Grass, x, y, 0); int i = world.Index(x, y);
            world.Cells[i].Height = world.Cells[i].RouteHeight = .58f;
            world.Cells[i].Temperature = .55f; world.Cells[i].Moisture = .62f; world.Cells[i].Vegetation = .12f;
            world.Cells[i].WaterDepth = 0; world.Cells[i].River = false; world.Cells[i].WaterKind = WaterKind.None;
            world.Infrastructure[i].Stream = 0;
            // Workshop quarter is explicitly cleared for a controlled camera and paid construction.
            world.ResourceNodes[i] = default(ResourceNodeData);
        }
        for (int y = 48; y <= 81; y++) for (int x = 43; x <= 84; x++) BiomeCatalog.RefreshCell(world, world.Index(x, y));
        engine.Spawn(SpeciesType.Human, 63, 68, 1);
        town = world.GetSettlement(world.Units.First(u => (int)u.Species < 4).SettlementId);
        town.Name = "Мастерская долина"; town.Wood = 400; town.Food = 80;
        town.UnlockedTechnologies = TechnologyCatalog.All.Select(t => t.Id).ToList(); town.ActiveTechnology = ""; town.ResearchProgress = 0;
        town.NextPolicyReviewTick = town.NextPlanningTick = 1000000;
        for (int y = 50; y <= 79; y++) for (int x = 45; x <= 82; x++) world.Cells[world.Index(x, y)].SettlementId = town.Id;
        BuildingData warehouse = Build(BuildingType.Warehouse, 62, 60);
        foreach (ItemType item in new[] { ItemType.Log, ItemType.Plank, ItemType.RawStone, ItemType.Brick, ItemType.IronOre, ItemType.CopperOre, ItemType.TinOre, ItemType.SilverOre, ItemType.GoldOre, ItemType.Charcoal, ItemType.IronIngot, ItemType.CopperIngot, ItemType.SilverIngot, ItemType.GoldIngot, ItemType.Leather, ItemType.LinenCloth }) warehouse.Inventory[(int)item] = 20;
        warehouse.AgriculturalStock[(int)AgriculturalProduct.Hide] = 18; warehouse.AgriculturalStock[(int)AgriculturalProduct.Fiber] = 25;
        Build(BuildingType.Well, 68, 61);
        BuildingData smelter = Build(BuildingType.Smelter, 53, 66), smithy = Build(BuildingType.Smithy, 57, 66);
        BuildingData tannery = Build(BuildingType.Tannery, 53, 61), weaver = Build(BuildingType.Weaver, 57, 61);
        BuildingData tailor = Build(BuildingType.Tailor, 53, 56), carpenter = Build(BuildingType.Carpentry, 57, 56);
        BuildingData mint = Build(BuildingType.Mint, 62, 56);
        // Finish map edits before assigning routes: changing NavigationRevision
        // afterwards correctly cancels those paths and would spoil this fixture.
        world.NavigationRevision++; world.TerrainRevision++;
        Work(smelter, IndustryRecipeType.IronIngot, "Лада · плавильщица", SpeciesType.Dwarf, true);
        Work(smithy, IndustryRecipeType.Armor, "Бор · кузнец", SpeciesType.Orc, true);
        Work(tannery, IndustryRecipeType.Leather, "Мира · кожевница", SpeciesType.Human, true);
        Work(weaver, IndustryRecipeType.LinenCloth, "Эль · ткач", SpeciesType.Elf, true);
        Work(tailor, IndustryRecipeType.LinenClothes, "Вера · портная", SpeciesType.Human, true);
        Work(carpenter, IndustryRecipeType.Ballista, "Тар · плотник", SpeciesType.Dwarf, true);
        Work(mint, IndustryRecipeType.SilverCoin, "Орна · чеканщица", SpeciesType.Orc, false);
        for (int i = 0; i < (approach ? 0 : 30); i++) engine.Step(.1f);
        Require(world.Validate(out string error), error);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonSerializer.Serialize(new Envelope { World = world }, Json));
        WorldState copy = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(output), Json).World;
        Require(copy.Validate(out error), error); var continuation = new SimulationEngine(copy);
        for (int i = 0; i < 120; i++) { continuation.Step(.1f); Require(copy.Validate(out error), "Continuation tick " + i + ": " + error); }
        Directory.CreateDirectory(Path.GetDirectoryName(report));
        File.WriteAllText(report, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, output, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))), assistance = "Authored cleared quarter; technologies, construction budget, raw and intermediate stock granted. Buildings paid through API; production/hauling proceeds through real commands and ticks. This is not natural development.", approach, subjects, occupants = world.Units.Where(u => u.InsideBuildingId != 0).Select(u => new { u.Id, u.Name, u.InsideBuildingId }), sourceTick = world.Tick, continuationTicks = 120, validation = true, camera = new { x = 59, y = 62, zoom = 11 } }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Prepared production quarter: " + output); return 0;
    }
}
