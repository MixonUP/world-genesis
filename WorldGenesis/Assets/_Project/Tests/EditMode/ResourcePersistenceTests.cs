using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using WorldGenesis.Runtime;
using WorldGenesis.Simulation;

namespace WorldGenesis.Tests
{
    public sealed class ResourcePersistenceTests
    {
        private string directory, path;
        [SetUp] public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "WorldGenesis-resource-save-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); path = Path.Combine(directory, "world.json");
        }
        [TearDown] public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void OldV020FileWithoutResourceFieldsMigratesOnlyOnceThroughUnitySerializer()
        {
            string fixture = Path.Combine(Application.dataPath, "_Project/Tests/EditMode/Fixtures/legacy-v020.json");
            string oldJson = File.ReadAllText(fixture);
            Assert.IsFalse(oldJson.Contains("ResourceVersion"), "Fixture must come from the previous schema, not the current class.");
            File.WriteAllText(path, oldJson);
            Assert.IsTrue(SaveStore.TryLoad(path, out WorldState oldWorld, out string error), error);
            Assert.AreEqual(0, oldWorld.ResourceVersion);
            Assert.AreEqual(87, oldWorld.Tick);
            float allCarriedPlanksBefore = TotalLegacyPlanks(oldWorld);
            SimulationEngine migrated = new SimulationEngine(oldWorld);
            Assert.AreEqual(2, migrated.World.ResourceVersion);
            Assert.AreEqual(3796401059u, migrated.World.RngState, "Migration must not consume simulation randomness.");
            Assert.AreEqual(15, migrated.World.NextId, "Deposits use cell identity, never new citizen/building IDs.");
            SettlementData town = migrated.World.Settlements[0];
            Assert.AreEqual(7, migrated.World.GetSettlementStock(town.Id, ItemType.RawStone));
            Assert.AreEqual(0, town.Stone, "Legacy stone cannot coexist with its migrated physical stock.");
            Assert.AreEqual(23, town.Wood);
            Assert.AreEqual(2, TotalLegacyPlanks(migrated.World, migrated.World.Units[0].Id), .0001f, "The first citizen's old load must remain carried or on the ground at its owner, never disappear.");
            Assert.AreEqual(allCarriedPlanksBefore, TotalLegacyPlanks(migrated.World), .0001f, "Other residents' genuine historical loads must also survive migration.");
            foreach (UnitData unit in migrated.World.Units) Assert.LessOrEqual(unit.CargoAmount + unit.CarriedFood + unit.CarriedWood, 1.0001f);
            Assert.IsTrue(migrated.World.Validate(out error), error);
            Assert.IsTrue(SaveStore.TrySave(path, migrated.World, out error), error);
            Assert.IsTrue(SaveStore.TryLoad(path, out WorldState loaded, out error), error);
            SimulationEngine second = new SimulationEngine(loaded);
            Assert.AreEqual(JsonUtility.ToJson(migrated.World), JsonUtility.ToJson(second.World), "Second load cannot duplicate resources or regenerate nodes.");
            for (int i = 0; i < 211; i++) { migrated.Step(.1f); second.Step(.1f); }
            Assert.AreEqual(JsonUtility.ToJson(migrated.World), JsonUtility.ToJson(second.World));
        }

        [Test]
        public void LiveCargoWorkshopAndDepletedNodeRoundtripThenContinueExactly()
        {
            SimulationEngine original = new SimulationEngine(WorldGenerator.Generate(7419, 32, 32));
            original.Paint(TerrainType.Grass, 16, 16, 10);
            original.Spawn(SpeciesType.Human, 16, 16, 6);
            WorldState world = original.World;
            SettlementData home = world.Settlements[0];
            BuildingData hearth = world.Buildings.Find(b => b.SettlementId == home.Id && b.Type == BuildingType.Hall);
            UnitData worker = world.Units[0];
            worker.X = hearth.X + .5f; worker.Y = hearth.Y + .5f;
            worker.Task = UnitTask.Process; worker.TargetBuildingId = hearth.Id;
            worker.SourceBuildingId = 0; worker.Path.Clear(); worker.PathCursor = 0;
            worker.WorkTimer = 7; worker.DecisionTimer = 1; worker.Hunger = 0; worker.Fatigue = 0;
            hearth.Inventory[(int)ItemType.Log] = 3;
            hearth.ActiveRecipe = RecipeType.Plank; hearth.ProcessProgress = 3; hearth.WorkerId = worker.Id;
            UnitData carrier = world.Units[1];
            carrier.CargoKind = ItemType.GoldOre; carrier.CargoAmount = 1;
            carrier.Task = UnitTask.Return; carrier.TargetBuildingId = hearth.Id;
            int cell = world.Index(1, 1);
            world.ResourceNodes[cell] = new ResourceNodeData { Type = ResourceType.Stone, Amount = 0, Capacity = 8, RegrowTicksRemaining = 730 };
            Assert.IsTrue(world.Validate(out string error), error);
            Assert.IsTrue(SaveStore.TrySave(path, world, out error), error);
            Assert.IsTrue(SaveStore.TryLoad(path, out WorldState loaded, out error), error);
            Assert.AreEqual(JsonUtility.ToJson(world), JsonUtility.ToJson(loaded));
            SimulationEngine resumed = new SimulationEngine(loaded);
            for (int i = 0; i < 211; i++) { original.Step(.1f); resumed.Step(.1f); }
            Assert.IsTrue(world.Validate(out error), error);
            Assert.AreEqual(JsonUtility.ToJson(world), JsonUtility.ToJson(resumed.World), "Loading must preserve job ownership, timers and physical stock at every transition.");
        }

        [Test]
        public void InvalidResourceInventoryCannotReplaceAValidSave()
        {
            SimulationEngine engine = new SimulationEngine(WorldGenerator.Generate(42, 16, 16));
            engine.Paint(TerrainType.Grass, 8, 8, 5); engine.Spawn(SpeciesType.Human, 8, 8, 4);
            Assert.IsTrue(SaveStore.TrySave(path, engine.World, out string error), error);
            string valid = File.ReadAllText(path);
            engine.World.Buildings[0].Inventory[(int)ItemType.Log] = -1;
            Assert.IsFalse(SaveStore.TrySave(path, engine.World, out error));
            Assert.AreEqual(valid, File.ReadAllText(path));
        }

        [Test]
        public void OldResourceLoadOverOneMigratesInPlaceAndRoundtripsWithoutDuplicatingGoods()
        {
            string oldJson = File.ReadAllText(Path.Combine(Application.dataPath, "_Project/Tests/EditMode/Fixtures/legacy-resources-v1.json"));
            Assert.IsFalse(oldJson.Contains("EconomyVersion"));
            File.WriteAllText(path, oldJson);
            Assert.IsTrue(SaveStore.TryLoad(path, out WorldState oldWorld, out string error), error);
            Assert.AreEqual(1, oldWorld.ResourceVersion);
            float before = TotalTransported(oldWorld);
            int oldId = oldWorld.Units[0].Id;
            float oldX = oldWorld.Units[0].X, oldY = oldWorld.Units[0].Y;
            SimulationEngine engine = new SimulationEngine(oldWorld);
            Assert.AreEqual(2, oldWorld.ResourceVersion);
            Assert.AreEqual(3539512653u, oldWorld.RngState);
            Assert.AreEqual(13, oldWorld.NextId);
            Assert.AreEqual(before, TotalTransported(oldWorld), .0001f);
            foreach (UnitData unit in oldWorld.Units) Assert.LessOrEqual(unit.CargoAmount + unit.CarriedFood + unit.CarriedWood, 1.0001f);
            GroundStockData overflow = oldWorld.GroundStocks.Find(g => g.OwnerId == oldId && g.Item == ItemType.IronOre);
            Assert.NotNull(overflow);
            Assert.AreEqual(oldX, overflow.X); Assert.AreEqual(oldY, overflow.Y);
            Assert.AreEqual(2, overflow.Amount, .0001f);
            Assert.IsTrue(SaveStore.TrySave(path, oldWorld, out error), error);
            Assert.IsTrue(SaveStore.TryLoad(path, out WorldState loaded, out error), error);
            SimulationEngine resumed = new SimulationEngine(loaded);
            Assert.AreEqual(JsonUtility.ToJson(oldWorld), JsonUtility.ToJson(loaded), "Migrating again must not issue a second pile or change policy.");
            for (int i = 0; i < 211; i++) { engine.Step(.1f); resumed.Step(.1f); }
            Assert.IsTrue(oldWorld.Validate(out error), error);
            Assert.AreEqual(JsonUtility.ToJson(oldWorld), JsonUtility.ToJson(loaded));
        }

        private static float TotalTransported(WorldState world)
        {
            float total = 0;
            foreach (UnitData unit in world.Units) total += unit.CargoAmount + unit.CarriedFood + unit.CarriedWood;
            if (world.GroundStocks != null) foreach (GroundStockData stock in world.GroundStocks) total += stock.Amount;
            return total;
        }

        [Test]
        public void ActualBarterAndNaturalTaxSurviveUnitySaveWithoutChargingAgain()
        {
            SimulationEngine engine = new SimulationEngine(WorldGenerator.Generate(7521, 24, 24));
            engine.Paint(TerrainType.Grass, 12, 12, 10);
            engine.Spawn(SpeciesType.Human, 12, 12, 6);
            engine.Step(1f);
            WorldState world = engine.World;
            SettlementData home = world.Settlements[0];
            engine.SetEconomicPolicy(home.Id, EconomicPolicy.Market, .2f, ChildLaborPolicy.Forbidden, ResourceFocus.Balanced);
            BuildingData hearth = world.Buildings.Find(b => b.SettlementId == home.Id && b.Type == BuildingType.Hall);
            UnitData buyer = world.Units[0], seller = world.Units[1];
            buyer.Age = seller.Age = 25;
            buyer.X = hearth.X + .5f; buyer.Y = hearth.Y + .5f;
            buyer.Task = UnitTask.Process; buyer.TargetBuildingId = hearth.Id; buyer.SourceBuildingId = 0;
            buyer.ResourceCell = -1; buyer.Path.Clear(); buyer.PathCursor = 0;
            buyer.Profession = UnitProfession.Forester; buyer.DecisionTimer = 2; buyer.WorkTimer = 10;
            buyer.Hunger = buyer.Fatigue = 0;
            buyer.CargoKind = ItemType.None; buyer.CargoAmount = buyer.CarriedFood = buyer.CarriedWood = 0;
            hearth.Inventory = new float[ResourceCatalog.ItemCount];
            hearth.PersonalStocks.Clear();
            hearth.PersonalStocks.Add(new OwnedGoodsData { OwnerId = seller.Id, Item = ItemType.Log, Amount = 1 });
            hearth.PersonalStocks.Add(new OwnedGoodsData { OwnerId = buyer.Id, Item = ItemType.IronOre, Amount = 1 });
            hearth.WorkerId = buyer.Id; hearth.ActiveRecipe = RecipeType.Plank; hearth.ProcessProgress = 0;
            engine.Step(.1f);
            Assert.AreEqual(1, world.Trades.FindAll(t => t.Kind == TradeKind.Barter).Count, "The ordinary on-site job must execute the exchange.");
            Assert.AreEqual(.2f, world.GetTreasuryStock(home.Id, ItemType.IronOre), .0001f);
            Assert.AreEqual(.8f, world.GetCitizenStock(seller.Id, ItemType.IronOre), .0001f);
            Assert.AreEqual(0, world.GetCitizenStock(buyer.Id, ItemType.IronOre), .0001f);
            Assert.AreEqual(1, hearth.Inventory[(int)ItemType.Log], .0001f);
            Assert.IsTrue(SaveStore.TrySave(path, world, out string error), error);
            Assert.IsTrue(SaveStore.TryLoad(path, out WorldState loaded, out error), error);
            SimulationEngine resumed = new SimulationEngine(loaded);
            Assert.AreEqual(JsonUtility.ToJson(world), JsonUtility.ToJson(loaded));
            engine.Step(.1f); resumed.Step(.1f);
            Assert.AreEqual(1, world.Trades.FindAll(t => t.Kind == TradeKind.Barter).Count, "Resuming an existing input cannot buy it twice.");
            Assert.AreEqual(.2f, loaded.GetTreasuryStock(home.Id, ItemType.IronOre), .0001f);
            for (int i = 0; i < 211; i++) { engine.Step(.1f); resumed.Step(.1f); }
            Assert.IsTrue(world.Validate(out error), error);
            Assert.AreEqual(JsonUtility.ToJson(world), JsonUtility.ToJson(loaded));
        }

        private static float TotalLegacyPlanks(WorldState world, int ownerId = 0)
        {
            float total = 0;
            foreach (UnitData unit in world.Units) if (ownerId == 0 || unit.Id == ownerId) total += unit.CarriedWood;
            if (world.GroundStocks != null)
                foreach (GroundStockData stock in world.GroundStocks) if (stock.Item == ItemType.Plank && (ownerId == 0 || stock.OwnerId == ownerId)) total += stock.Amount;
            return total;
        }
    }
}
