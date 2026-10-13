using System;
using NUnit.Framework;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Tests
{
    public sealed class InfrastructureTests
    {
        private static SimulationEngine Fixture()
        {
            WorldState world = new WorldState { Width = 32, Height = 32, Seed = 2718, RngState = 99, Cells = new TileData[1024] };
            for (int i = 0; i < world.Cells.Length; i++) world.Cells[i] = new TileData { Terrain = TerrainType.Grass, Height = .5f, Moisture = .7f, Temperature = .5f, Vegetation = .3f, DrainageParent = -1 };
            SimulationEngine engine = new SimulationEngine(world);
            engine.Spawn(SpeciesType.Human, 16, 16, 6); world.Laws.Births = world.Laws.Aging = false;
            return engine;
        }
        private static void UnlockAll(SettlementData settlement)
        {
            foreach (TechnologyDefinition definition in TechnologyCatalog.All) settlement.UnlockedTechnologies.Add(definition.Id);
            settlement.ActiveTechnology = ""; settlement.ResearchProgress = 0;
        }
        private static BuildingData Hall(WorldState world) { return world.Buildings.Find(b => b.Type == BuildingType.Hall); }

        [Test]
        public void WellChargesOnlyPublicMaterialsAndFillsBeforeDrinking()
        {
            SimulationEngine engine = Fixture(); WorldState w = engine.World; SettlementData s = w.Settlements[0]; BuildingData hall = Hall(w);
            s.Wood = 8; hall.PersonalStocks.Add(new OwnedGoodsData { OwnerId = w.Units[0].Id, Item = ItemType.RawStone, Amount = 2 });
            Assert.IsFalse(engine.TryBuildInfrastructure(s.Id, BuildingType.Well, 20, 16), "Private stone is not public construction stock.");
            Assert.AreEqual(8, s.Wood); hall.Inventory[(int)ItemType.RawStone] = 2;
            Assert.IsTrue(engine.TryBuildInfrastructure(s.Id, BuildingType.Well, 20, 16));
            BuildingData well = w.Buildings.Find(b => b.Type == BuildingType.Well);
            Assert.AreEqual(0, well.WaterStored); Assert.AreEqual(0, s.Wood); Assert.AreEqual(2, hall.PersonalStocks[0].Amount);
            engine.Step(5); Assert.Greater(well.WaterStored, .5f);
            UnitData drinker = w.Units[0]; drinker.X = 20.5f; drinker.Y = 16.5f; drinker.Thirst = .9f;
            drinker.Task = UnitTask.Drink; drinker.TargetBuildingId = well.Id; drinker.TargetCell = w.Index(20, 16); drinker.Path.Clear(); drinker.PathCursor = 0; drinker.WorkTimer = .1f; drinker.DecisionTimer = 5;
            float before = well.WaterStored; engine.Step(.1f);
            Assert.AreEqual(.9f + SimulationEngine.TickSeconds * SubsistenceRules.ThirstPerSecond - SubsistenceRules.NeedReliefPerPortion, drinker.Thirst, .00001f); Assert.AreEqual(before - 1, well.WaterStored, .00001f);
            Assert.IsTrue(w.Validate(out string error), error);
        }

        [Test]
        public void WallAndGateBlockPathWithoutDestroyingThemselves()
        {
            SimulationEngine engine = Fixture(); WorldState w = engine.World; SettlementData s = w.Settlements[0]; UnlockAll(s); s.Wood = 10;
            Assert.IsTrue(engine.TryBuildInfrastructure(s.Id, BuildingType.WoodWall, 20, 16));
            Assert.IsTrue(engine.TryBuildInfrastructure(s.Id, BuildingType.Gate, 20, 17));
            BuildingData wall = w.Buildings.Find(b => b.Type == BuildingType.WoodWall), gate = w.Buildings.Find(b => b.Type == BuildingType.Gate);
            Assert.IsFalse(w.IsWalkable(20, 16)); Assert.IsTrue(w.IsTerrainWalkable(20, 16)); Assert.IsTrue(w.IsWalkable(20, 17));
            int revision = w.NavigationRevision;
            Assert.IsTrue(engine.SetGate(gate.Id, false)); Assert.IsFalse(w.IsWalkable(20, 17)); Assert.Greater(w.NavigationRevision, revision);
            engine.Step(1); Assert.IsNotNull(w.GetBuilding(wall.Id)); Assert.IsNotNull(w.GetBuilding(gate.Id));
            Assert.IsTrue(engine.SetGate(gate.Id, true)); Assert.IsTrue(w.IsWalkable(20, 17));
        }

        [Test]
        public void MissingResearchAndOccupiedGatesRefuseWithoutCost()
        {
            SimulationEngine engine = Fixture(); WorldState w = engine.World; SettlementData s = w.Settlements[0]; s.Wood = 20;
            Assert.IsFalse(engine.TryBuildInfrastructure(s.Id, BuildingType.Gate, 20, 17)); Assert.AreEqual(20, s.Wood);
            UnlockAll(s); Assert.IsTrue(engine.TryBuildInfrastructure(s.Id, BuildingType.Gate, 20, 17));
            BuildingData gate = w.Buildings.Find(b => b.Type == BuildingType.Gate); w.Units[0].X = 20.5f; w.Units[0].Y = 17.5f;
            Assert.IsFalse(engine.SetGate(gate.Id, false)); Assert.IsTrue(gate.GateOpen);
        }

        [Test]
        public void RoadUpgradeConsumesTilesOnceAndFloodRemovesRoad()
        {
            SimulationEngine engine = Fixture(); WorldState w = engine.World; SettlementData s = w.Settlements[0]; UnlockAll(s); s.Wood = 1;
            int cell = w.Index(18, 16); w.Cells[cell].BuildingId = 0; w.Cells[cell].SettlementId = s.Id;
            Hall(w).Inventory[(int)ItemType.StoneTile] = 1;
            Assert.IsTrue(engine.TryBuildRoad(s.Id, 18, 16, true)); Assert.AreEqual(1, Hall(w).Inventory[(int)ItemType.StoneTile]);
            Assert.IsFalse(engine.TryBuildRoad(s.Id, 18, 16, true));
            for (int i = 0; i < 900 && w.Infrastructure[cell].RoadLevel != 4; i++) engine.Step(.1f);
            Assert.AreEqual(4, w.Infrastructure[cell].RoadLevel, "A resident must deliver the tile and work at the site.");
            Assert.AreEqual(0, Hall(w).Inventory[(int)ItemType.StoneTile]);
            engine.Paint(TerrainType.ShallowWater, 18, 16, 0); engine.Step(1);
            Assert.AreEqual(0, w.Infrastructure[cell].RoadLevel); Assert.AreEqual(0, w.Infrastructure[cell].RoadOwnerId);
        }

        [Test]
        public void WaterClassificationExcludesSaltWaterAndFishCannotBeInventedByFishing()
        {
            SimulationEngine engine = Fixture(); WorldState w = engine.World; int cell = w.Index(18, 16);
            engine.Paint(TerrainType.ShallowWater, 18, 16, 0); engine.Step(1);
            Assert.IsFalse(w.IsFreshWater(cell)); w.Cells[cell].River = true; Assert.IsTrue(w.IsFreshWater(cell));
            w.Infrastructure[cell].Fish = 1;
            UnitData fisher = w.Units[0]; fisher.X = 17.5f; fisher.Y = 16.5f; fisher.Task = UnitTask.Fish; fisher.ResourceCell = cell; fisher.WorkTimer = .01f; fisher.DecisionTimer = 10;
            fisher.TargetCell = w.Index(17, 16); fisher.Path.Clear(); fisher.PathCursor = 0; fisher.CarriedFood = fisher.CarriedWood = fisher.CargoAmount = 0;
            engine.Step(.1f); Assert.AreEqual(AgriculturalProduct.Fish, fisher.HarvestProduct); Assert.AreEqual(1, fisher.HarvestAmount, .00001f); Assert.AreEqual(0, w.Infrastructure[cell].Fish, .00001f);
            Assert.LessOrEqual(SimulationEngine.CarriedLoad(fisher), 1.00001f);
        }

        [Test]
        public void UpgradeIncreasesHouseComfortAndPreservesOwnerAcrossRoundtrip()
        {
            SimulationEngine engine = Fixture(); WorldState w = engine.World; SettlementData s = w.Settlements[0]; s.Wood = 30;
            // Let the normal builder construct the founding sawmill and then the first house.
            engine.Step(10); engine.Step(10); engine.Step(1);
            BuildingData house = w.Buildings.Find(b => b.Type == BuildingType.House); Assert.IsNotNull(house);
            UnitData resident = w.Units.Find(u => u.HomeBuildingId == house.Id); Assert.IsNotNull(resident);
            UnlockAll(s); s.Wood = 20; float oldComfort = w.GetHousingComfort(resident);
            Assert.IsTrue(engine.TryUpgradeBuilding(house.Id)); Assert.AreEqual(2, house.Level); Assert.Greater(w.GetHousingComfort(resident), oldComfort);
            string json = JsonUtility.ToJson(w); SimulationEngine restored = new SimulationEngine(JsonUtility.FromJson<WorldState>(json));
            Assert.AreEqual(json, JsonUtility.ToJson(restored.World));
            for (int i = 0; i < 121; i++) { engine.Step(.1f); restored.Step(.1f); }
            Assert.AreEqual(JsonUtility.ToJson(w), JsonUtility.ToJson(restored.World)); Assert.IsTrue(w.Validate(out string error), error);
        }

        [Test]
        public void ResearchDiminishesEveryGroupAndUnknownResearchIsRejected()
        {
            foreach (double weight in new[] { .1, 1, 2, 10 })
            {
                double first = SimulationEngine.DiminishedContribution(1, weight);
                double second = SimulationEngine.DiminishedContribution(2, weight) - first;
                Assert.AreEqual(weight, first, 1e-9); Assert.AreEqual(weight * .88, second, 1e-9);
            }
            SimulationEngine engine = Fixture(); SettlementData s = engine.World.Settlements[0]; engine.Step(1);
            Assert.Greater(s.ResearchRate, 0); Assert.Greater(s.ResearchProgress, 0);
            s.UnlockedTechnologies.Add("unknown-tech"); Assert.IsFalse(engine.World.Validate(out string error)); Assert.That(error, Does.Contain("technology"));
        }

        [Test]
        public void DevelopmentMigrationIsRepeatableWithoutConsumingRandomnessOrMaterials()
        {
            SimulationEngine engine = Fixture(); WorldState w = engine.World;
            w.DevelopmentVersion = 0; w.Infrastructure = null; uint rng = w.RngState; int nextId = w.NextId;
            float food = w.Settlements[0].Food, wood = w.Settlements[0].Wood;
            SimulationEngine first = new SimulationEngine(w); string once = JsonUtility.ToJson(w);
            SimulationEngine second = new SimulationEngine(w);
            Assert.AreEqual(once, JsonUtility.ToJson(w)); Assert.AreEqual(rng, w.RngState); Assert.AreEqual(nextId, w.NextId);
            Assert.AreEqual(food, w.Settlements[0].Food); Assert.AreEqual(wood, w.Settlements[0].Wood);
        }
    }
}
