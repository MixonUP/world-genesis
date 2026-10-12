using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using WorldGenesis.Simulation;

// Assisted visual fixture only: grants are disclosed; building and upgrade costs use production APIs.
internal static class Program
{
    private sealed class Envelope { public int Schema = 1; public string Format = "WorldGenesis"; public WorldState World; }
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, IgnoreReadOnlyProperties = true };
    private static int Main(string[] args)
    {
        string input = args.Length > 0 ? args[0] : "Builds/transport-assisted-showcase-v050-bridge-complete.json";
        if (!File.Exists(input) && args.Length == 0) input = "Builds/transport-assisted-showcase-v050.json";
        string output = args.Length > 1 ? args[1] : "Builds/storage-assisted-v050.save.json";
        string receiptPath = args.Length > 2 ? args[2] : "Evidence/Simulation/storage-assisted-v050.json";
        string source = File.ReadAllText(input);
        using JsonDocument document = JsonDocument.Parse(source);
        WorldState world = document.RootElement.TryGetProperty("World", out JsonElement nested)
            ? JsonSerializer.Deserialize<WorldState>(nested.GetRawText(), Json) : JsonSerializer.Deserialize<WorldState>(source, Json);
        var engine = new SimulationEngine(world);
        Require(world.Validate(out string initialError), "Initial world: " + initialError);
        int[] cancelledProjects = world.TransportProjects.Select(p => p.Id).ToArray();
        // Production cancellation preserves delivered goods on the ground and resets assigned workers.
        MethodInfo cancel = typeof(SimulationEngine).GetMethod("CancelTransportProject", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (TransportProjectData project in world.TransportProjects.ToArray()) cancel.Invoke(engine, new object[] { project });
        SettlementData town = world.Settlements.First(s => s.Population > 0 && world.Buildings.Any(b => b.SettlementId == s.Id && b.Type == BuildingType.Hall));
        BuildingData hall = world.Buildings.First(b => b.SettlementId == town.Id && b.Type == BuildingType.Hall);
        var techGrants = new List<string>();
        string activeTechnologyBefore = town.ActiveTechnology;
        float researchProgressBefore = town.ResearchProgress;
        void GrantTechnology(string id)
        {
            TechnologyDefinition definition = TechnologyCatalog.Get(id);
            Require(definition != null, "Unknown technology " + id);
            foreach (string prerequisite in definition.Prerequisites) GrantTechnology(prerequisite);
            if (!town.UnlockedTechnologies.Contains(id)) { town.UnlockedTechnologies.Add(id); techGrants.Add(id); }
        }
        foreach (string id in new[] { "f01_preservation", "m10_comfort_housing", "m11_town_houses", "m12_stone_houses" }) GrantTechnology(id);
        bool clearedGrantedResearch = !string.IsNullOrEmpty(town.ActiveTechnology) && town.UnlockedTechnologies.Contains(town.ActiveTechnology);
        if (clearedGrantedResearch) { town.ActiveTechnology = ""; town.ResearchProgress = 0; town.ResearchRate = 0; }

        float woodBefore = town.Wood;
        // Two buildings: 8 each; three upgrades each: 6, 12, 18 planks + 4 bricks + 6 blocks.
        town.Wood += 88;
        BuildingData Build(BuildingType type, int desiredX, int desiredY)
        {
            var candidates = new List<(int x, int y, int score)>();
            for (int y = hall.Y - 12; y <= hall.Y + 12; y++) for (int x = hall.X - 12; x <= hall.X + 12; x++)
            {
                if (!world.InBounds(x, y) || !world.IsWalkable(x, y) || world.Cells[world.Index(x, y)].WaterDepth > 0) continue;
                if (world.Buildings.Any(b => b.Health > 0 && Math.Abs(b.X - x) < 3 && Math.Abs(b.Y - y) < 3)) continue;
                candidates.Add((x, y, (x - desiredX) * (x - desiredX) + (y - desiredY) * (y - desiredY)));
            }
            foreach (var site in candidates.OrderBy(c => c.score).ThenBy(c => c.y).ThenBy(c => c.x))
                if (engine.TryBuildInfrastructure(town.Id, type, site.x, site.y)) return world.GetBuilding(world.Cells[world.Index(site.x, site.y)].BuildingId);
            throw new InvalidOperationException("No legal connected site for " + type);
        }
        BuildingData warehouse = Build(BuildingType.Warehouse, hall.X - 5, hall.Y - 4);
        BuildingData granary = Build(BuildingType.Granary, hall.X + 1, hall.Y - 4);
        warehouse.Inventory[(int)ItemType.Brick] += 8;
        warehouse.Inventory[(int)ItemType.StoneBlock] += 12;
        foreach (BuildingData building in new[] { warehouse, granary })
        {
            while (building.Level < 4) Require(engine.TryUpgradeBuilding(building.Id), "Paid upgrade failed: " + building.Type + " level " + building.Level);
            Require(!engine.TryUpgradeBuilding(building.Id), "Unexpected fifth depot upgrade");
        }
        Require(Math.Abs(town.Wood - woodBefore) < .001f, "Construction did not spend exactly 88 planks");
        // Explicit generated showcase parcels, not simulation production or caravan delivery.
        warehouse.Inventory[(int)ItemType.RawStone] += 300;
        warehouse.AgriculturalStock[(int)AgriculturalProduct.Fiber] += 100;
        granary.AgriculturalStock[(int)AgriculturalProduct.Grain] += 300;
        granary.AgriculturalStock[(int)AgriculturalProduct.Hay] += 100;
        granary.AgriculturalStock[(int)AgriculturalProduct.Berries] += 50;
        world.ResourceRevision++; world.TerrainRevision++; world.Revision++;
        Require(world.Validate(out string finalError), "Final world: " + finalError);
        string saved = JsonSerializer.Serialize(new Envelope { World = world }, Json);
        Envelope roundTrip = JsonSerializer.Deserialize<Envelope>(saved, Json);
        Require(roundTrip.Schema == 1 && roundTrip.Format == "WorldGenesis" && roundTrip.World.Validate(out _), "Envelope round trip failed");
        Require(StorageCapacity.TotalUsed(roundTrip.World.GetBuilding(warehouse.Id)) == StorageCapacity.TotalUsed(warehouse), "Warehouse changed in save round trip");
        Require(StorageCapacity.TotalUsed(roundTrip.World.GetBuilding(granary.Id)) == StorageCapacity.TotalUsed(granary), "Granary changed in save round trip");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, saved);
        object Depot(BuildingData b) => new { id = b.Id, type = b.Type.ToString(), x = b.X, y = b.Y, inspectCell = b.X + "," + b.Y, level = b.Level, capacity = StorageCapacity.Capacity(b), used = StorageCapacity.TotalUsed(b), free = StorageCapacity.Capacity(b) - StorageCapacity.TotalUsed(b) };
        var receipt = new {
            utc = DateTime.UtcNow, status = "validated-assisted-fixture", naturalProgressionProof = false,
            source = input, sourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))),
            sourceWasAlreadyAssisted = true, cancelledTransportProjectIds = cancelledProjects,
            cancellation = "Production cancellation retains delivered materials on the ground; no projects existed if list is empty.",
            construction = "TryBuildInfrastructure twice; TryUpgradeBuilding six times; both level-5 requests rejected. No time was simulated.",
            technologyGrants = techGrants,
            researchAffectedByGrant = new { clearedGrantedResearch, priorTechnology = activeTechnologyBefore, priorProgress = researchProgressBefore },
            generatedConstructionMaterials = new { Plank = 88, Brick = 8, StoneBlock = 12 },
            paidConstructionMaterials = new { Plank = 88, Brick = 8, StoneBlock = 12 },
            generatedShowcaseParcels = new { warehouse = new { RawStone = 300, Fiber = 100 }, granary = new { Grain = 300, Hay = 100, Berries = 50 } },
            town = new { id = town.Id, name = town.Name, hallX = hall.X, hallY = hall.Y }, tick = world.Tick,
            warehouse = Depot(warehouse), granary = Depot(granary), worldValidation = true, envelopeRoundTripValidation = true,
            savedWorld = output, bytes = new FileInfo(output).Length, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output)))
        };
        Directory.CreateDirectory(Path.GetDirectoryName(receiptPath));
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS " + output + " (" + new FileInfo(output).Length + " bytes)");
        Console.WriteLine("Warehouse inspect-cell " + warehouse.X + "," + warehouse.Y + "; Granary inspect-cell " + granary.X + "," + granary.Y);
        Console.WriteLine("Receipt " + receiptPath);
        return 0;
    }
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
}
