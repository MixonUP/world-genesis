using System;
using System.Reflection;
using WorldGenesis.Simulation;
#if UNITY_5_3_OR_NEWER
using NUnit.Framework;
using UnityEngine;
#endif

namespace WorldGenesis.Tests
{
    public static class WarehouseCapacityScenarios
    {
        public static Func<WorldState, WorldState> Clone;
        public static Func<WorldState, string> Snapshot;
        private static void Need(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
        private static void Near(float expected, float actual, string text) { Need(Math.Abs(expected - actual) < .002f, text + ": expected " + expected + ", actual " + actual); }
        private static object Call(SimulationEngine engine, string method, params object[] args) { return typeof(SimulationEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(engine, args); }
        private static SimulationEngine Town()
        {
            WorldState world = new WorldState { Width = 40, Height = 40, Cells = new TileData[1600], Seed = 584, RngState = 91, NaturalWildlife = false };
            for (int i = 0; i < world.Cells.Length; i++) world.Cells[i] = new TileData { Terrain = TerrainType.Grass, Height = .5f, Moisture = .7f, Temperature = .5f, Vegetation = .7f, DrainageParent = -1 };
            SimulationEngine engine = new SimulationEngine(world); engine.Spawn(SpeciesType.Human, 20, 20, 4); world.Laws.Births = world.Laws.Aging = false;
            foreach (UnitData unit in world.Units) { unit.Age = 25; unit.Task = UnitTask.Idle; unit.DecisionTimer = 1000; unit.Hunger = unit.Thirst = unit.Fatigue = unit.SocialNeed = .1f; }
            SettlementData city = world.Settlements[0]; city.Food = 200; city.Wood = 500; city.NextPolicyReviewTick = city.NextPlanningTick = city.NextCaravanTick = 100000;
            foreach (TechnologyDefinition tech in TechnologyCatalog.All) if (!city.UnlockedTechnologies.Contains(tech.Id)) city.UnlockedTechnologies.Add(tech.Id);
            city.ActiveTechnology = ""; return engine;
        }
        private static BuildingData Add(SimulationEngine engine, BuildingType type)
        {
            WorldState world = engine.World;
            for (int y = 17; y < 24; y++) for (int x = 17; x < 24; x++)
            {
                if (world.Cells[world.Index(x, y)].BuildingId != 0) continue;
                Call(engine, "AddBuilding", world.Settlements[0], x, y, type);
                return world.Buildings[world.Buildings.Count - 1];
            }
            throw new InvalidOperationException("No free depot fixture site.");
        }
        private static void At(SimulationEngine engine, UnitData unit, BuildingData building)
        {
            int door=BuildingAccess.EntranceCell(engine.World,building);Need(door>=0,"Depot fixture has no door");
            unit.X=door%engine.World.Width+.5f;unit.Y=door/engine.World.Width+.5f;unit.Path.Clear();unit.PathCursor=0;
            unit.InsideBuildingId=unit.EnteringBuildingId=0;unit.BuildingInteractionTimer=0;unit.Task=UnitTask.Idle;unit.CargoOwnerId=0;
            unit.CargoKind = ItemType.None; unit.CargoAmount = unit.CarriedFood = unit.CarriedWood = unit.CarriedWater = unit.HarvestAmount = 0; unit.HarvestProduct = AgriculturalProduct.None;
            Need(engine.TryEnterBuilding(unit.Id,building.Id),"Depot entry refused");
            for(int i=0;i<20&&unit.InsideBuildingId!=building.Id;i++)engine.Step(.1f);
            Need(unit.InsideBuildingId==building.Id,"Carrier never entered depot");unit.TargetBuildingId=building.Id;unit.Task=UnitTask.Return;
        }
        public static void FourCapacitiesAndLegacyFifthLevelNeverRewriteGoods()
        {
            float[] limits = { 1000, 2500, 5000, 10000, 10000 };
            foreach (BuildingType type in new[] { BuildingType.Warehouse, BuildingType.Granary })
            {
                BuildingData b = new BuildingData { Type = type }; b.Inventory[(int)ItemType.RawStone] = 1100; b.AgriculturalStock[(int)AgriculturalProduct.Grain] = 50;
                for (int level = 1; level <= 5; level++) { b.Level = level; Near(limits[level - 1], StorageCapacity.Capacity(b), "grade capacity"); Near(1150, StorageCapacity.TotalUsed(b), "capacity lookup must preserve all goods"); Need(b.Level == level, "legacy level must not be rewritten"); }
                b.Level = 1; Near(150, StorageCapacity.Overflow(b), "lowered capacity reports overload"); Near(0, StorageCapacity.FreeAgriculture(b), "overloaded depot has no free space");
            }
        }
        public static void FoodAndMaterialDepotsHaveExclusiveIncomingCategories()
        {
            BuildingData warehouse = new BuildingData { Type = BuildingType.Warehouse }, granary = new BuildingData { Type = BuildingType.Granary };
            for (int item = 1; item < ResourceCatalog.ItemCount; item++) { Need(StorageCapacity.Accepts(warehouse, (ItemType)item), "warehouse rejects material"); Need(!StorageCapacity.Accepts(granary, (ItemType)item), "granary accepts material"); }
            foreach (AgriculturalProduct product in Enum.GetValues(typeof(AgriculturalProduct)))
            {
                if (product == AgriculturalProduct.None) continue;
                Need(StorageCapacity.Accepts(granary, product) != StorageCapacity.Accepts(warehouse, product), "each agricultural good needs exactly one depot type");
            }
            Need(StorageCapacity.Accepts(granary, AgriculturalProduct.Ale) && StorageCapacity.Accepts(granary, AgriculturalProduct.Hay) && StorageCapacity.Accepts(granary, AgriculturalProduct.Egg), "food, drink and fodder belong in granary");
            Need(StorageCapacity.Accepts(warehouse, AgriculturalProduct.Fiber) && StorageCapacity.Accepts(warehouse, AgriculturalProduct.Herbs) && StorageCapacity.Accepts(warehouse, AgriculturalProduct.RarePelt), "craft crops belong in warehouse");
        }
        public static void MaterialDeliverySharesSpaceWithPersonalTaxAndAgriculturalGoods()
        {
            SimulationEngine engine = Town(); BuildingData depot = Add(engine, BuildingType.Warehouse); UnitData unit = engine.World.Units[0];
            depot.Inventory[(int)ItemType.RawStone] = 600; depot.TaxStocks[(int)ItemType.Coal] = 100;
            depot.PersonalStocks.Add(new OwnedGoodsData { OwnerId = unit.Id, Item = ItemType.Clay, Amount = 100 }); depot.AgriculturalStock[(int)AgriculturalProduct.Fiber] = 199.5f;
            At(engine, unit, depot); unit.CargoKind = ItemType.Log; unit.CargoAmount = 1;
            Call(engine, "WorkResources", unit, engine.World.Settlements[0], .1f);
            Near(1000, StorageCapacity.TotalUsed(depot), "material delivery respects cross-compartment capacity"); Near(.5f, unit.CargoAmount, "overflow stays in hands");
            Near(100.0f, depot.PersonalStocks[0].Amount, "private lot preserved"); Near(100, depot.TaxStocks[(int)ItemType.Coal], "tax preserved");
            Need(engine.World.Validate(out string error), error);
        }
        public static void FoodDeliveryCountsLegacyMaterialsAndLeavesExcessOnCarrier()
        {
            SimulationEngine engine = Town(); BuildingData depot = Add(engine, BuildingType.Granary); UnitData unit = engine.World.Units[0];
            depot.Inventory[(int)ItemType.RawStone] = 100; depot.TaxStocks[(int)ItemType.Coal] = 100;
            depot.PersonalStocks.Add(new OwnedGoodsData { OwnerId = unit.Id, Item = ItemType.Clay, Amount = 100 }); depot.AgriculturalStock[(int)AgriculturalProduct.Grain] = 699.5f;
            At(engine, unit, depot); unit.HarvestProduct = AgriculturalProduct.Berries; unit.HarvestAmount = 1;
            Call(engine, "PerformAgriDelivery", unit, engine.World.Settlements[0], .1f);
            Near(1000, StorageCapacity.TotalUsed(depot), "food admission counts legacy wrong goods"); Near(.5f, unit.HarvestAmount, "food excess stays with carrier");
            Near(100, depot.Inventory[(int)ItemType.RawStone], "legacy material not deleted"); Need(engine.World.Validate(out string error), error);
        }
        public static void WrongIncomingGoodsStayWithTheirCarrier()
        {
            SimulationEngine engine = Town(); BuildingData warehouse = Add(engine, BuildingType.Warehouse), granary = Add(engine, BuildingType.Granary); UnitData unit = engine.World.Units[0];
            At(engine, unit, warehouse); unit.HarvestProduct = AgriculturalProduct.Grain; unit.HarvestAmount = 1; Call(engine, "PerformAgriDelivery", unit, engine.World.Settlements[0], .1f);
            Near(1, unit.HarvestAmount, "warehouse refuses new food without losing it"); Near(0, StorageCapacity.TotalUsed(warehouse), "no wrong food admission");
            At(engine, unit, granary); unit.CargoKind = ItemType.Log; unit.CargoAmount = 1; Call(engine, "WorkResources", unit, engine.World.Settlements[0], .1f);
            Near(1, unit.CargoAmount, "granary refuses material without losing it"); Near(0, StorageCapacity.TotalUsed(granary), "no wrong material admission");
            At(engine, unit, granary); unit.HarvestProduct = AgriculturalProduct.Fiber; unit.HarvestAmount = 1; Call(engine, "PerformAgriDelivery", unit, engine.World.Settlements[0], .1f);
            Near(1, unit.HarvestAmount, "granary refuses fibre"); Near(0, StorageCapacity.TotalUsed(granary), "no wrong crop admission");
        }
        public static void OverloadedAndLegacyWrongStocksRoundTripWithoutTruncation()
        {
            SimulationEngine first = Town(); BuildingData b = Add(first, BuildingType.Warehouse); UnitData owner = first.World.Units[0];
            b.Level = 5; b.Inventory[(int)ItemType.RawStone] = 7000; b.TaxStocks[(int)ItemType.Clay] = 100;
            b.PersonalStocks.Add(new OwnedGoodsData { OwnerId = owner.Id, Item = ItemType.Salt, Amount = 900 }); b.AgriculturalStock[(int)AgriculturalProduct.Grain] = 3000;
            Need(first.World.Validate(out string error), error); string before = Snapshot(first.World);
            WorldState loaded = Clone(first.World); Need(loaded.Validate(out error), error); SimulationEngine second = new SimulationEngine(loaded);
            BuildingData restored = second.World.GetBuilding(b.Id); Need(restored.Level == 5, "legacy fifth level retained"); Near(11000, StorageCapacity.TotalUsed(restored), "overload not trimmed on loading"); Near(0, StorageCapacity.FreeMaterials(restored), "overload blocks new goods");
            Need(Snapshot(first.World) == before, "validation/query altered original world");
            for (int i = 0; i < 20; i++) { first.Step(.1f); second.Step(.1f); }
            Need(Snapshot(first.World) == Snapshot(second.World), "save continuation diverged"); Need(second.World.Validate(out error), error);
        }
        public static void BothDepotUpgradesStopAtFourthLevelAndRetainInventory()
        {
            SimulationEngine engine = Town(); BuildingData warehouse = Add(engine, BuildingType.Warehouse), granary = Add(engine, BuildingType.Granary);
            warehouse.Inventory[(int)ItemType.Brick] = 50; warehouse.Inventory[(int)ItemType.StoneBlock] = 50; warehouse.Inventory[(int)ItemType.Salt] = 17;
            granary.AgriculturalStock[(int)AgriculturalProduct.Grain] = 321;
            foreach (BuildingData building in new[] { warehouse, granary })
            {
                for (int level = 1; level < 4; level++) Need(engine.TryUpgradeBuilding(building.Id), "real upgrade to level " + (level + 1) + " refused");
                Near(10000, StorageCapacity.Capacity(building), "fourth level capacity"); Need(!engine.TryUpgradeBuilding(building.Id), "fifth depot upgrade must be unavailable");
            }
            Near(321, granary.AgriculturalStock[(int)AgriculturalProduct.Grain], "upgrade preserves food"); Near(17, warehouse.Inventory[(int)ItemType.Salt], "upgrade preserves unrelated materials");
        }
        public static void NonDepotLimitsAndMalformedStocksRemainProtected()
        {
            BuildingData workshop = new BuildingData { Type = BuildingType.Sawmill, Level = 2 }; workshop.Inventory[(int)ItemType.Log] = 100; workshop.AgriculturalStock[(int)AgriculturalProduct.Fiber] = 200;
            Near(20, StorageCapacity.FreeMaterials(workshop), "non-depot material buffer unchanged"); Near(40, StorageCapacity.FreeAgriculture(workshop), "non-depot agriculture buffer unchanged");
            SimulationEngine engine = Town(); BuildingData depot = Add(engine, BuildingType.Warehouse); depot.Inventory[(int)ItemType.RawStone] = float.NaN;
            Need(!engine.World.Validate(out _), "NaN stock bypassed validation"); depot.Inventory[(int)ItemType.RawStone] = -1; Need(!engine.World.Validate(out _), "negative stock bypassed validation");
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class WarehouseCapacityTests
    {
        [SetUp] public void Serializers() { WarehouseCapacityScenarios.Clone = w => JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)); WarehouseCapacityScenarios.Snapshot = w => JsonUtility.ToJson(w); }
        [TestCase("FourCapacitiesAndLegacyFifthLevelNeverRewriteGoods")]
        [TestCase("FoodAndMaterialDepotsHaveExclusiveIncomingCategories")]
        [TestCase("MaterialDeliverySharesSpaceWithPersonalTaxAndAgriculturalGoods")]
        [TestCase("FoodDeliveryCountsLegacyMaterialsAndLeavesExcessOnCarrier")]
        [TestCase("WrongIncomingGoodsStayWithTheirCarrier")]
        [TestCase("OverloadedAndLegacyWrongStocksRoundTripWithoutTruncation")]
        [TestCase("BothDepotUpgradesStopAtFourthLevelAndRetainInventory")]
        [TestCase("NonDepotLimitsAndMalformedStocksRemainProtected")]
        public void Scenario(string name) { typeof(WarehouseCapacityScenarios).GetMethod(name).Invoke(null, null); }
    }
#endif
}
