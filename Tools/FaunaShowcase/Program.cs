using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using WorldGenesis.Simulation;

// Controlled production-simulation fixtures. No Unity dependency or alternate renderer.
internal static class Program
{
    private sealed class Envelope { public int Schema = 1; public string Format = "WorldGenesis"; public WorldState World; }
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, IgnoreReadOnlyProperties = true };
    private static WorldState world;
    private static SimulationEngine engine;
    private static readonly List<object> subjects = new List<object>();
    private static readonly int[] Dx = { 0, -1, 1, 0 }, Dy = { -1, 0, 0, 1 };
    private static readonly string[] DirectionNames = { "south", "west", "east", "north" };
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static void NewWorld()
    {
        world = WorldGenerator.Generate(130900, 128, 128, WorldPreset.Island); engine = new SimulationEngine(world); subjects.Clear();
        world.NaturalWildlife = false; world.Weather.Automatic = false;
        world.Laws.Births = world.Laws.Aging = world.Laws.Warfare = world.Laws.VegetationGrowth = false;
        // Prepared temperate meadow; no hidden tree/resource sprite can mask the review subjects.
        for (int y = 30; y <= 108; y++) for (int x = 30; x <= 104; x++)
        {
            engine.Paint(TerrainType.Grass, x, y, 0); int cell = world.Index(x, y);
            world.Cells[cell].Height = world.Cells[cell].RouteHeight = .58f; world.Cells[cell].Temperature = .55f; world.Cells[cell].Moisture = .65f; world.Cells[cell].Vegetation = .14f;
            world.Cells[cell].WaterDepth = world.Cells[cell].CurrentStrength = 0; world.Cells[cell].WaterKind = WaterKind.None; world.Cells[cell].River = false; world.Cells[cell].DrainageParent = -1;
            world.Infrastructure[cell].Stream = 0; world.ResourceNodes[cell] = default(ResourceNodeData); BiomeCatalog.RefreshCell(world, cell);
        }
        world.NavigationRevision++; world.TerrainRevision++; world.NextWandererTick = 1000000;
    }
    private static UnitData Add(SpeciesType species, int x, int y, string name)
    {
        var ids = new HashSet<int>(world.Units.Select(u => u.Id)); engine.Spawn(species, x, y, 1);
        UnitData unit = world.Units.Single(u => !ids.Contains(u.Id));
        unit.X = x + .5f; unit.Y = y + .5f; unit.Name = name; unit.Age = (int)species < 4 ? 28 : 6; unit.Sex = Sex.Male;
        unit.Health = unit.MaxHealth; unit.Hunger = unit.Thirst = unit.Fatigue = unit.SocialNeed = .02f;
        unit.Task = UnitTask.Idle; unit.DecisionTimer = 100000; unit.Path.Clear(); unit.PathCursor = 0; unit.PackId = unit.PackLeaderId = 0;
        unit.NextBreedingTick = 1000000; return unit;
    }
    private static void Route(UnitData unit, int direction, int length)
    {
        unit.Path.Clear(); unit.PathCursor = 0; unit.Task = UnitTask.Wander; unit.TaskStartedTick = world.Tick;
        int x = (int)unit.X, y = (int)unit.Y;
        for (int step = 1; step <= length; step++) { int tx = x + Dx[direction] * step, ty = y + Dy[direction] * step; Need(world.IsWalkable(tx, ty), "Blocked prepared route"); unit.Path.Add(world.Index(tx, ty)); }
        unit.TargetCell = unit.Path[unit.Path.Count - 1];
        subjects.Add(new { unit.Id, unit.Name, species = unit.Species.ToString(), direction = DirectionNames[direction], start = new[] { unit.X, unit.Y }, routeLength = length });
    }
    private static void Animals(string name, SpeciesType[] species)
    {
        NewWorld();
        for (int i = 0; i < species.Length; i++)
        {
            int cx = 53 + (i % 2) * 24, cy = 92 - (i / 2) * 14;
            for (int direction = 0; direction < 4; direction++)
            {
                // Four separate animals, arranged S/W/E/N around the species' center.
                UnitData animal = Add(species[i], cx + Dx[direction] * 2, cy + Dy[direction] * 2, WildlifeCatalog.Name(species[i]) + " · " + DirectionNames[direction]);
                Route(animal, direction, 1);
            }
        }
        Save(name, new { x = 65, y = species.Length > 6 ? 72 : 79, zoom = species.Length > 6 ? 27 : 21, closeups = new[] { new { x = 53, y = 92, zoom = 7 }, new { x = 77, y = 78, zoom = 7 } } });
    }
    private static void Motion()
    {
        NewWorld();
        // Same-species subjects avoid accidental hunting/fleeing during the gait comparison.
        for (int direction = 0; direction < 4; direction++)
        {
            UnitData animal = Add(SpeciesType.Deer, 64 + Dx[direction] * 3, 65 + Dy[direction] * 3, "Олень · шаг " + DirectionNames[direction]);
            animal.Sex = direction == 0 ? Sex.Female : Sex.Male; Route(animal, direction, 12);
        }
        Save("walking", new { x = 64, y = 65, zoom = 20, recommendedDelay = 0, recommendedSeconds = 4 });
    }
    private static void SpiritStag()
    {
        NewWorld();
        for (int direction = 0; direction < 4; direction++)
        {
            UnitData animal = Add(SpeciesType.SpiritStag, 64 + Dx[direction] * 2, 65 + Dy[direction] * 2, "Лунный олень · " + DirectionNames[direction]);
            Route(animal, direction, 32);
        }
        Save("spirit-stag-directions", new { x = 64, y = 65, zoom = 15, recommendedSeconds = 1 }, true);
    }
    private static void DiagnoseSpiritArrival()
    {
        const string source = "Builds/FaunaShowcase/woodland.save.json";
        if (!File.Exists(source)) return;
        WorldState copy = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(source), Json).World;
        var continuation = new SimulationEngine(copy); var samples = new List<object>();
        for (int tick = 0; tick <= 8; tick++)
        {
            foreach (UnitData unit in copy.Units.Where(u => u.Species == SpeciesType.SpiritStag))
            {
                bool moving = unit.PathCursor < unit.Path.Count;
                int target = moving ? unit.Path[unit.PathCursor] : unit.TargetCell;
                float dx = target % copy.Width + .5f - unit.X, dy = target / copy.Width + .5f - unit.Y;
                samples.Add(new { copy.Tick, unit.Id, unit.Name, unit.X, unit.Y, unit.PathCursor, moving, dx, dy, squaredDistance = dx * dx + dy * dy, preFixTravelDirectionFallsBackEast = moving && dx * dx + dy * dy <= .0001f });
            }
            if (tick < 8) continuation.Step(.1f);
        }
        Directory.CreateDirectory("Evidence/Simulation");
        File.WriteAllText("Evidence/Simulation/fauna-spirit-stag-arrival-diagnostic.json", JsonSerializer.Serialize(new { utc = DateTime.UtcNow, source, sourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))), scope = "Read-only replay of the existing saved fixture through production Simulation. The flag mirrors the pre-fix WorldView.TravelDirection squared-distance threshold (0.0001); this is not a Unity render.", samples }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void Place(BuildingData building, int x, int y)
    {
        world.Cells[world.Index(building.X, building.Y)].BuildingId = 0;
        Need(world.Cells[world.Index(x, y)].BuildingId == 0, "Occupied fixture site");
        building.X = x; building.Y = y; building.EntranceCell = BuildingAccess.IsEnclosed(building.Type) ? world.Index(x, y - 1) : -1;
        world.Cells[world.Index(x, y)].BuildingId = building.Id; world.Cells[world.Index(x, y)].SettlementId = building.SettlementId;
    }
    private static void FieldsAndRulers()
    {
        NewWorld();
        for (int race = 0; race < 4; race++)
        {
            int x = 53 + race * 8;
            UnitData ruler = Add((SpeciesType)race, x, 74, "Правитель · " + (SpeciesType)race); ruler.Sex = race % 2 == 0 ? Sex.Female : Sex.Male; ruler.X = x + .5f; ruler.Y = 70.5f;
            SettlementData city = world.GetSettlement(ruler.SettlementId); city.Name = "Община " + (SpeciesType)race; city.X = x; city.Y = 74;
            city.Government = GovernmentType.Feudal; city.RulerId = city.PolicyRulerId = ruler.Id; city.GovernmentSinceTick = world.Tick; city.NextElectionTick = 0;
            city.NextPlanningTick = city.NextPolicyReviewTick = city.NextMaritimeConstructionTick = 1000000; city.Food = 50; city.UnlockedTechnologies = TechnologyCatalog.All.Select(t => t.Id).ToList(); city.ActiveTechnology = ""; city.ResearchProgress = 0;
            BuildingData hall = world.Buildings.First(b => b.SettlementId == city.Id && b.Type == BuildingType.Hall); Place(hall, x, 74); hall.WaterStored = 25;
            BuildingData farm = world.Buildings.First(b => b.SettlementId == city.Id && b.Type == BuildingType.Farm); Place(farm, x, 63);
            farm.FarmCrop = race % 2 == 0 ? CropType.Wheat : CropType.Flax; farm.CropGrowth = .76f; farm.HarvestReady = 0;
            UnitData farmer = Add((SpeciesType)race, x, 64, "Работник на поле · " + (SpeciesType)race); farmer.Sex = ruler.Sex == Sex.Female ? Sex.Male : Sex.Female;
            farmer.Profession = UnitProfession.Farmer; farmer.Task = UnitTask.Farm; farmer.TargetCell = world.Index(farm.X, farm.Y); farmer.TargetBuildingId = farm.Id;
            subjects.Add(new { ruler = ruler.Id, species = ruler.Species.ToString(), sex = ruler.Sex.ToString(), rulerPoint = new[] { ruler.X, ruler.Y }, field = farm.Id, farmer = farmer.Id, farmerPoint = new[] { farmer.X, farmer.Y } });
        }
        SettlementData first = world.Settlements[0];
        var pasture = new BuildingData { Id = world.NextId++, SettlementId = first.Id, Type = BuildingType.Pasture, X = 0, Y = 0, Level = 1, HerdSpecies = (int)SpeciesType.Cow };
        world.Buildings.Add(pasture); Place(pasture, 58, 63);
        UnitData herder = Add(SpeciesType.Human, 58, 64, "Пастух на пастбище"); herder.Profession = UnitProfession.Herder; herder.Task = UnitTask.Tend; herder.TargetBuildingId = pasture.Id; herder.WorkTimer = 20;
        UnitData cow = Add(SpeciesType.Cow, 59, 63, "Корова на пастбище"); cow.IsDomestic = true; cow.AnimalOwnerId = first.Id; cow.PastureId = pasture.Id;
        subjects.Add(new { pasture = pasture.Id, herder = herder.Id, cow = cow.Id });
        foreach (SettlementData city in world.Settlements) { city.Population = world.Units.Count(u => u.SettlementId == city.Id); city.Buildings = world.Buildings.Count(b => b.SettlementId == city.Id); }
        Save("fields-rulers", new { x = 65, y = 68, zoom = 13, crownCloseup = new { x = 53, y = 70, zoom = 6 }, fieldsCloseup = new { x = 56, y = 64, zoom = 6 } });
    }
    private static void Save(string name, object camera, bool requireActiveRoutes = false)
    {
        world.NavigationRevision++; world.TerrainRevision++;
        foreach (UnitData unit in world.Units) unit.PathRevision = world.NavigationRevision;
        Need(world.Validate(out string error), name + ": " + error);
        string output = "Builds/FaunaShowcase/" + name + ".save.json"; Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonSerializer.Serialize(new Envelope { World = world }, Json));
        WorldState copy = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(output), Json).World; Need(copy.Validate(out error), error);
        SimulationEngine continuation = new SimulationEngine(copy);
        for (int tick = 0; tick < 120; tick++)
        {
            continuation.Step(.1f); Need(copy.Validate(out error), name + " tick " + tick + ": " + error);
            if (requireActiveRoutes) Need(world.Units.All(u => copy.GetUnit(u.Id).PathCursor < copy.GetUnit(u.Id).Path.Count), name + ": direction proof route ended during the 120-tick allowance");
        }
        Need(world.Units.All(u => copy.GetUnit(u.Id) != null && copy.GetUnit(u.Id).Health > 0), name + ": review subject was lost during loading allowance");
        string report = "Evidence/Simulation/fauna-showcase-" + name + ".json"; Directory.CreateDirectory(Path.GetDirectoryName(report));
        File.WriteAllText(report, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, output, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))), sourceTick = world.Tick, camera, subjects, continuedTicks = 120, validation = true, allOriginalSubjectsSurvive = true, allRoutesRemainActiveFor120Ticks = requireActiveRoutes ? (bool?)true : null, assistance = "Authored dry meadow and controlled initial positions, needs, ages, directions, one-cell or long paths; births/war/automatic weather and new wildlife disabled. Fields/pasture, crop stage, ownership, four towns and rulers prepared explicitly. Ordinary simulation moves units and performs work; this is not evidence of natural progression, balance or taming. The generator does not render images." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output + " — validated, roundtrip +120 ticks");
    }
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--spirit-stag-only") { DiagnoseSpiritArrival(); SpiritStag(); return 0; }
            Need(args.Length == 0, "Usage: FaunaShowcase [--spirit-stag-only]");
            Animals("woodland", new[] { SpeciesType.Wolf, SpeciesType.Bear, SpeciesType.Fox, SpeciesType.DireWolf, SpeciesType.Deer, SpeciesType.Boar, SpeciesType.SpiritStag });
            Animals("farm-animals", new[] { SpeciesType.Sheep, SpeciesType.Goat, SpeciesType.Cow, SpeciesType.Chicken, SpeciesType.Donkey, SpeciesType.Horse });
            Motion(); FieldsAndRulers(); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
