using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using WorldGenesis.Simulation;

internal static class RemainsShowcase
{
    private sealed class Envelope { public int Schema = 1; public string Format = "WorldGenesis"; public WorldState World; }
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, IgnoreReadOnlyProperties = true };
    public static void Generate()
    {
        var w = new WorldState { Width = 64, Height = 64, Seed = 101014, RngState = 101014, Cells = new TileData[4096] };
        for (int i = 0; i < w.Cells.Length; i++) w.Cells[i] = new TileData { Terrain = TerrainType.Grass, Height = .55f, RouteHeight = .55f, Moisture = .6f, Temperature = .5f, DrainageParent = -1 };
        var e = new SimulationEngine(w); w.NaturalWildlife = w.Weather.Automatic = w.Laws.Aging = w.Laws.Births = w.Laws.Warfare = w.Laws.VegetationGrowth = false;
        SpeciesType[] species = { SpeciesType.Human, SpeciesType.Elf, SpeciesType.Dwarf, SpeciesType.Orc, SpeciesType.Wolf, SpeciesType.Bear, SpeciesType.Chicken, SpeciesType.Deer, SpeciesType.Horse, SpeciesType.Cow, SpeciesType.SpiritStag, SpeciesType.Human };
        TerrainType[] terrain = { TerrainType.Grass, TerrainType.Sand, TerrainType.Snow, TerrainType.Swamp, TerrainType.Forest, TerrainType.Grass };
        var ids = new List<int>();
        for (int i = 0; i < species.Length; i++)
        {
            int x = 22 + i % 6 * 5, y = 42 - i / 6 * 6;
            for (int yy = y - 2; yy <= y + 2; yy++) for (int xx = x - 2; xx <= x + 2; xx++) { e.Paint(terrain[i % 6], xx, yy, 0); w.Cells[w.Index(xx, yy)].Vegetation = 0; }
            int kind = (int)species[i]; int sx = kind < 4 ? 6 + kind % 2 * 48 : x, sy = kind < 4 ? 6 + kind / 2 * 48 : y;
            e.Spawn(species[i], sx, sy, 1); UnitData unit = w.Units.Last(); unit.X = x + .5f; unit.Y = y + .5f; unit.Age = i == 1 ? 8 : 25; unit.Name = "Останки · " + species[i]; unit.Health = 0; ids.Add(unit.Id);
            if (i == 11) e.Paint(TerrainType.ShallowWater, x, y, 1);
        }
        e.Spawn(SpeciesType.Human, 6, 6, 1); UnitData observer = w.Units.Last(); observer.X = 34.5f; observer.Y = 45.5f; observer.Name = "Живой наблюдатель"; observer.Age = 25; observer.Health = observer.MaxHealth; observer.Hunger = observer.Thirst = observer.Fatigue = observer.SocialNeed = 0; observer.DecisionTimer = 100000; observer.Task = UnitTask.Idle;
        foreach (SettlementData city in w.Settlements) { city.NextPlanningTick = city.NextPolicyReviewTick = city.NextMaritimeConstructionTick = 1000000; }
        BuildingData field = (BuildingData)typeof(SimulationEngine).GetMethod("AddBuilding", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(e, new object[] { w.Settlements[0], 22, 36, BuildingType.Farm }); field.CropGrowth = 1;
        e.Step(.1f);
        Need(w.Remains.Count == 12 && ids.All(id => w.GetUnit(id) == null), "Prepared death input did not execute through actual simulation removal"); Need(w.AgriculturalDrops.Count > 0, "Animal loot is missing");
        var records = new List<object>(); records.Add(Save(w, "fresh"));
        for (int i = 0; i < 1400; i++) { e.Step(.1f); if (i % 10 == 0) Need(w.Validate(out string error), error); }
        Need(w.Remains.Count == 11 && w.Remains.All(r => !r.WaterDeath && r.RemainingSeconds < 11 && r.RemainingSeconds > 9), "Actual 140-second decay or water expiry failed"); records.Add(Save(w, "fading"));
        Directory.CreateDirectory("Evidence/Simulation");
        File.WriteAllText("Evidence/Simulation/remains-showcase.json", JsonSerializer.Serialize(new { utc = DateTime.UtcNow, camera = new { x = 35, y = 39, zoom = 12 }, assistance = "Prepared terrain patches and spawn coordinates, explicit HP=0 death inputs. One actual fixed Step records all twelve deaths, retains the real animal drops and living observer. The second save follows 1400 ordinary ticks (140 simulation seconds); TTL is never edited. This generator renders no images.", subjects = ids, frames = records, validationEveryTenTicks = true, continuedTicks = 1400 }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static object Save(WorldState world, string name)
    {
        Need(world.Validate(out string error), error); string output = "Builds/RemainsShowcase/" + name + ".save.json"; Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonSerializer.Serialize(new Envelope { World = world }, Json));
        WorldState loaded = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(output), Json).World; Need(loaded.Validate(out error), error);
        Need(loaded.Remains.Count == world.Remains.Count && loaded.Remains.Select(r => r.RemainingSeconds).SequenceEqual(world.Remains.Select(r => r.RemainingSeconds)), "Save reset a decay timer");
        Console.WriteLine(output + " validated, tick=" + world.Tick + ", remains=" + world.Remains.Count);
        return new { output, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))), world.Tick, remains = JsonSerializer.SerializeToElement(world.Remains, Json), realAgriculturalDrops = world.AgriculturalDrops.Count };
    }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
