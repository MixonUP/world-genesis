using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using WorldGenesis.Simulation;

// Explicitly assisted visual fixture: authored terrain/buildings and starting stocks/research.
// Equipment is completed by the production work operation, caravan cargo moves via production ticks.
WorldState w = WorldGenerator.Generate(20261012, 64, 64);
for (int y = 18; y <= 48; y++) for (int x = 10; x <= 54; x++)
{
    int cell = w.Index(x, y); w.Cells[cell].Terrain = TerrainType.Grass; w.Cells[cell].Height = .52f; w.Cells[cell].River = false; w.Cells[cell].Moisture = .7f; w.Cells[cell].Vegetation = .35f;
}
SimulationEngine e = new SimulationEngine(w); w.Laws.Births = w.Laws.Aging = w.Laws.Warfare = false; w.NaturalWildlife = false;
e.Spawn(SpeciesType.Human, 24, 32, 16); e.Spawn(SpeciesType.Elf, 47, 32, 8);
SettlementData s = w.Settlements[0], other = w.Settlements[1]; s.EconomyPolicy = other.EconomyPolicy = EconomicPolicy.FreeDistribution;
foreach (SettlementData town in w.Settlements) { town.Food = 180; town.Wood = 100; town.NextPolicyReviewTick = 100000; foreach (TechnologyDefinition tech in TechnologyCatalog.All) town.UnlockedTechnologies.Add(tech.Id); town.ActiveTechnology = ""; }
BuildingData Add(BuildingType type, int x, int y)
{
    BuildingData prior = w.GetBuilding(w.Cells[w.Index(x, y)].BuildingId);
    if (prior != null) return prior;
    typeof(SimulationEngine).GetMethod("AddBuilding", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(e, new object[] { s, x, y, type });
    return w.Buildings[w.Buildings.Count - 1];
}
for (int i = 0; i < 5; i++) { BuildingData house = Add(BuildingType.House, 19 + i * 2, 35); house.Level = i + 1; BuildingData farm = Add(BuildingType.Farm, 19 + i * 2, 29); farm.FarmCrop = (CropType)i; farm.CropGrowth = .3f + i * .14f; }
BuildingData mill = Add(BuildingType.Sawmill, 25, 32), pasture = Add(BuildingType.Pasture, 21, 32), well = Add(BuildingType.Well, 24, 34), brewery = Add(BuildingType.Brewery, 26, 34);
Add(BuildingType.Warehouse, 28, 32); Add(BuildingType.Shop, 28, 34); Add(BuildingType.School, 18, 32); well.WaterStored = 4;
pasture.AgriculturalStock[(int)AgriculturalProduct.Hay] = 12; brewery.AgriculturalStock[(int)AgriculturalProduct.Grain] = 4; brewery.Inventory[(int)ItemType.Firewood] = 2; brewery.BrewWaterStored = 2;
foreach (UnitData resident in w.Units) { resident.Age = 25; resident.Hunger = resident.Thirst = resident.Fatigue = resident.SocialNeed = .1f; resident.DecisionTimer = 10000; resident.Task = UnitTask.Idle; }
UnitData trader = w.Units.First(u => u.SettlementId == s.Id); trader.X = mill.X + .5f; trader.Y = mill.Y + .5f; trader.BasketLevel = 1;
mill.Inventory[(int)ItemType.Plank] = 12; mill.AgriculturalStock[(int)AgriculturalProduct.Fiber] = 4; mill.AgriculturalStock[(int)AgriculturalProduct.Hide] = 2;
if (!e.TryStartEquipment(trader.Id, 5)) throw new Exception("Cart work not available"); trader.Path.Clear(); trader.PathCursor = 0;
typeof(SimulationEngine).GetMethod("PerformAgriculturalWork", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(e, new object[] { trader, s, 8f });
if (!trader.OwnsWagon) throw new Exception("Cart was not produced");
e.Spawn(SpeciesType.Horse, 25, 32, 1); UnitData horse = w.Units[w.Units.Count - 1]; horse.Age = 4; trader.X = horse.X; trader.Y = horse.Y; trader.HarvestProduct = AgriculturalProduct.Hay; trader.HarvestAmount = 1;
if (!e.TryTameAnimal(trader.Id, horse.Id, pasture.Id) || !e.TryMountNearby(trader)) throw new Exception("Horse taming or hitch failed");
foreach (SpeciesType species in new[] { SpeciesType.Cow, SpeciesType.Goat, SpeciesType.Chicken, SpeciesType.Donkey })
{
    e.Spawn(species, 21, 32, 1); UnitData animal = w.Units[w.Units.Count - 1]; animal.Age = 4; UnitData herder = w.Units.First(u => u.SettlementId == s.Id && u.Id != trader.Id); herder.X = animal.X; herder.Y = animal.Y; herder.HarvestProduct = AgriculturalProduct.Hay; herder.HarvestAmount = 1;
    if (!e.TryTameAnimal(herder.Id, animal.Id, pasture.Id)) throw new Exception("Pasture tame failed");
}
e.Spawn(SpeciesType.Deer, 15, 39, 3); e.Spawn(SpeciesType.Bear, 12, 44, 1);
BuildingData hall = w.Buildings.First(b => b.SettlementId == s.Id && b.Type == BuildingType.Hall), market = w.Buildings.First(b => b.SettlementId == other.Id && b.Type == BuildingType.Hall);
hall.Inventory[(int)ItemType.IronOre] = 90; market.Inventory[(int)ItemType.CopperOre] = 90;
if (!e.TryStartCaravan(s.Id, other.Id, ItemType.IronOre, ItemType.CopperOre, 2)) throw new Exception("Caravan could not start");
for (int tick = 0; tick < 300; tick++) { foreach (SettlementData town in w.Settlements) town.BuildTimer = 0; e.Step(.1f); if (w.Caravans[0].Phase == CaravanPhase.Outbound && trader.CargoAmount > 0) break; }
if (!w.Validate(out string error)) throw new Exception(error);
JsonSerializerOptions json = new JsonSerializerOptions { IncludeFields = true, IgnoreReadOnlyProperties = true, WriteIndented = false };
File.WriteAllText("Builds/rural-assisted-showcase.json", JsonSerializer.Serialize(w, json));
File.WriteAllText("Evidence/Simulation/rural-assisted-showcase.json", JsonSerializer.Serialize(new { utc = DateTime.UtcNow, type = "assisted visual fixture, not natural progression proof", terrainAndBuildingsAuthored = true, researchGranted = true, initialMaterialsGranted = true, workSecondsApplied = 8, cartMadeByProductionRecipe = trader.OwnsWagon, cartCapacity = SimulationEngine.CarryCapacityFor(trader), caravan = w.Caravans[0], tick = w.Tick, population = w.Units.Count, savedWorld = "Builds/rural-assisted-showcase.json" }, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
Console.WriteLine("Assisted showcase saved, caravan phase=" + w.Caravans[0].Phase + ", cart cargo=" + trader.CargoAmount);
