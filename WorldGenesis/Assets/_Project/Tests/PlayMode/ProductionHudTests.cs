using NUnit.Framework;
using WorldGenesis.Runtime;
using WorldGenesis.Simulation;

namespace WorldGenesis.Tests
{
    public sealed class ProductionHudTests
    {
        [Test]
        public void WorkshopInspectorShowsMissingLocalInputsAndActualBatchYield()
        {
            WorldState world = WorldGenerator.Generate(130701, 16, 16);
            new SimulationEngine(world);
            var town = new SettlementData { Id = 1, Name = "Тест" }; town.UnlockedTechnologies.Add("z02_ironworking"); world.Settlements.Add(town);
            var worker = new UnitData { Id = 500, Age = 28, Health = 100, Name = "Кузнец", SettlementId = 1, Task = UnitTask.Produce, TargetBuildingId = 501 };
            world.Units.Add(worker);
            var forge = new BuildingData { Id = 501, SettlementId = 1, Type = BuildingType.Smithy, X = 7, Y = 7, EntranceCell = world.Index(7, 6), ActiveIndustryRecipe = IndustryRecipeType.IronTools, IndustryWorkerId = worker.Id };
            world.Cells[world.Index(7, 7)].Terrain = TerrainType.Grass;
            world.Cells[world.Index(7, 7)].WaterDepth = 0; world.Cells[world.Index(7, 7)].WaterKind = WaterKind.None;
            world.Cells[world.Index(7, 7)].BuildingId = forge.Id;
            world.Cells[forge.EntranceCell].Terrain = TerrainType.Grass;
            world.Cells[forge.EntranceCell].WaterDepth = 0; world.Cells[forge.EntranceCell].WaterKind = WaterKind.None;
            world.Infrastructure[forge.EntranceCell].RoadLevel = 1;
            world.Buildings.Add(forge);
            worker.X = 7.5f; worker.Y = 6.5f; worker.EnteringBuildingId = forge.Id;
            Assert.IsTrue(BuildingAccess.IsUsable(world, forge), "Missing inputs must be inspected at a usable workshop, not a missing doorway.");
            string description = GameHud.ProductionDescription(world, forge);
            StringAssert.Contains("Доставка", description);
            StringAssert.Contains("Железный слиток 0 / 1", description);
            StringAssert.Contains("Выход партии: Железные инструменты × 1", description);
            StringAssert.Contains("Готовых партий: 0", description);
            Assert.AreEqual(0, forge.Inventory[(int)ItemType.IronIngot], "Reading the inspector cannot grant materials.");
        }

        [Test]
        public void EquipmentInspectorSeparatesProducedGoodsFromDefaultAppearance()
        {
            var unit = new UnitData();
            StringAssert.Contains("нового комплекта нет", GameHud.ManufacturedEquipmentDescription(unit));
            unit.WorkTool = ItemType.IronTools; unit.Clothes = ItemType.LinenClothes;
            string equipped = GameHud.ManufacturedEquipmentDescription(unit);
            StringAssert.Contains("Железные инструменты", equipped); StringAssert.Contains("Льняная одежда", equipped);
            Assert.IsFalse(equipped.Contains("броня"), "Civil clothing must not be described as military armor.");
        }
    }
}
