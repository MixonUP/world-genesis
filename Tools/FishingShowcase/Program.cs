using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using WorldGenesis.Simulation;

// Authored native review fixture; gameplay thereafter runs through ordinary SimulationEngine.Step.
internal static class Program
{
    private sealed class Envelope { public int Schema = 1; public string Format = "WorldGenesis"; public WorldState World; }
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, IgnoreReadOnlyProperties = true };
    private static WorldState world;
    private static SimulationEngine engine;
    private static readonly List<object> subjects = new List<object>();
    private static int Center(int x) => 66 + (int)Math.Round(Math.Sin((x - 48) * .13) * 2);
    private static void Require(bool ok, string detail) { if (!ok) throw new InvalidOperationException(detail); }
    private static UnitData Citizen(int x, int y, string name, SpeciesType race = SpeciesType.Human, bool child = false)
    {
        var ids = new HashSet<int>(world.Units.Select(u => u.Id));
        engine.Spawn(SpeciesType.Human, x, y, 1);
        UnitData u = world.Units.First(v => !ids.Contains(v.Id));
        u.Name = name; u.Species = race; u.Sex = u.Id % 2 == 0 ? Sex.Female : Sex.Male; u.Age = child ? 10 : 27;
        u.X = x + .5f; u.Y = y + .5f;
        u.Hunger = u.Thirst = u.Fatigue = u.SocialNeed = .02f;
        u.Task = UnitTask.Idle; u.DecisionTimer = 50; u.Path.Clear(); u.PathCursor = 0;
        return u;
    }
    private static void Walk(UnitData u, int endY)
    {
        u.Task = UnitTask.Wander; u.PathRevision = world.NavigationRevision;
        int x = (int)u.X;
        for (int y = (int)u.Y + 1; y <= endY; y++) u.Path.Add(world.Index(x, y));
        u.TargetCell = world.Index(x, endY);
        subjects.Add(new { u.Id, u.Name, start = new[] { u.X, u.Y }, end = new[] { x, endY }, task = "Prepared walking route, normal movement speed" });
    }
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
    private static int Run(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "Builds/fishing-river-showcase.save.json";
        string report = args.Length > 1 ? args[1] : "Evidence/Simulation/fishing-river-showcase.json";
        world = WorldGenerator.Generate(130613, 128, 128, WorldPreset.Island);
        engine = new SimulationEngine(world); world.NaturalWildlife = false;
        world.Laws.Births = world.Laws.Aging = world.Laws.Warfare = false;
        world.Weather.Automatic = false;
        for (int y = 43; y <= 88; y++) for (int x = 35; x <= 94; x++)
        {
            engine.Paint(TerrainType.Grass, x, y, 0);
            int i = world.Index(x, y); world.Cells[i].Height = .58f; world.Cells[i].Temperature = .55f;
            world.Cells[i].Moisture = .74f; world.Cells[i].Vegetation = .18f;
            world.Cells[i].WaterDepth = 0; world.Cells[i].River = false; world.Cells[i].WaterKind = WaterKind.None;
            world.Infrastructure[i].Stream = 0;
        }
        for (int x = 35; x <= 94; x++)
        {
            int center = Center(x), half = x < 48 ? 1 : x < 80 ? 2 : 3;
            for (int y = center - half; y <= center + half; y++)
            {
                engine.Paint(TerrainType.ShallowWater, x, y, 0); int i = world.Index(x, y);
                world.Cells[i].WaterKind = WaterKind.River; world.Cells[i].River = true;
                world.Cells[i].Height = .48f; world.Cells[i].WaterDepth = x >= 68 && x <= 71 ? .48f : (y == center ? 1.2f : .72f);
                world.Cells[i].CurrentStrength = x >= 68 && x <= 71 ? .32f : .52f;
                world.Cells[i].ChannelWidth = (byte)(half * 2 + 1); world.Cells[i].Flow = 20 + x;
                world.Cells[i].Temperature = x <= 64 ? .42f : .55f; world.Cells[i].Moisture = .74f;
                world.Cells[i].DrainageParent = world.Index(Math.Min(95, x + 1), y);
                BiomeCatalog.RefreshCell(world, i);
                world.Infrastructure[i].FishCapacity = 8; world.Infrastructure[i].Fish = 8;
            }
        }
        // A small connected lake widens the east end; the bank renderer sees real neighboring water cells.
        for (int y = 60; y <= 75; y++) for (int x = 79; x <= 92; x++)
            if ((x - 85) * (x - 85) / 36f + (y - 67) * (y - 67) / 49f < 1)
            {
                engine.Paint(TerrainType.ShallowWater, x, y, 0); int i = world.Index(x, y);
                world.Cells[i].WaterKind = WaterKind.Lake; world.Cells[i].WaterDepth = .8f + .04f * (6 - Math.Abs(x - 85));
                world.Cells[i].River = false; world.Cells[i].CurrentStrength = 0; world.Cells[i].DrainageParent = -1;
                BiomeCatalog.RefreshCell(world, i);
                world.Infrastructure[i].Fish = world.Infrastructure[i].FishCapacity = 8;
            }
        for (int y = Center(76) - 2; y <= Center(76) + 2; y++)
        { int i = world.Index(76, y); world.Infrastructure[i].BridgeLevel = 1; world.Infrastructure[i].BridgeAxis = 1; }
        world.NavigationRevision++; world.TerrainRevision++;
        var founder = Citizen(59, 58, "Пристань — основатель");
        SettlementData town = world.GetSettlement(founder.SettlementId); town.Name = "Излучина · проверка рыбалки";
        for (int y = Center(76) - 2; y <= Center(76) + 2; y++) world.Infrastructure[world.Index(76, y)].BridgeOwnerId = town.Id;
        town.NextPolicyReviewTick = town.NextPlanningTick = 1000000; town.BuildTimer = 0; town.BirthTimer = 0;
        town.Food = 20;
        for (int grade = 1; grade <= 5; grade++)
        {
            int x = 52 + grade * 3, bank = Center(x) - 3;
            UnitData u = Citizen(x, bank, "Рыбак · снасть " + grade);
            u.FishingRodLevel = grade; u.Task = UnitTask.Fish; u.WorkTimer = 20; u.DecisionTimer = 30;
            u.ResourceCell = world.Index(x, bank + 1); u.TargetCell = world.Index(x, bank);
            subjects.Add(new { u.Id, u.Name, grade, bank = new[] { x, bank }, fishCell = u.ResourceCell, assistance = "Granted rod, selected a stocked spot, initial cast extended to20s for visual review across loading frames. Ordinary catch and delivery follow" });
        }
        UnitData adult = Citizen(69, Center(69) - 3, "Брод · эльфийка", SpeciesType.Elf); adult.BasketLevel = 1;
        UnitData child = Citizen(71, Center(71) - 3, "Брод · ребёнок", SpeciesType.Dwarf, true);
        UnitData bridge = Citizen(76, Center(76) - 3, "Мост · орк", SpeciesType.Orc);
        // Start two subjects already on shallow water, with a traversable remaining route to the far bank.
        adult.Y += 1.5f; child.Y += 1.5f;
        Walk(adult, Center(69) + 3); Walk(child, Center(71) + 3); Walk(bridge, Center(76) + 3);
        Require(world.Validate(out string error), error);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonSerializer.Serialize(new Envelope { World = world }, Json));
        var copy = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(output), Json).World;
        Require(copy.Validate(out error), error);
        var continuation = new SimulationEngine(copy); int ticks = 0;
        for (; ticks < 100; ticks++) { continuation.Step(.1f); Require(copy.Validate(out error), "Tick " + ticks + ": " + error); }
        Directory.CreateDirectory(Path.GetDirectoryName(report));
        File.WriteAllText(report, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, output, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))),
            assistance = "Authored valley, fish biomass, bridge, granted rods, races/ages, initial fishing tasks and wading routes. This proves native production rendering and ordinary simulation continuation, not natural technology progression.",
            subjects, continuationTicks = ticks, validation = true, camera = new { x = 65, y = 66, zoom = 10 } }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Prepared and validated river fixture: " + output); return 0;
    }
}
