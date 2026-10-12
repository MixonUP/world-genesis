using System;
using System.Linq;
using System.Reflection;
using WorldGenesis.Simulation;
#if UNITY_5_3_OR_NEWER
using NUnit.Framework;
using UnityEngine;
#endif

namespace WorldGenesis.Tests
{
    public static class SubsistenceScenarios
    {
        public static Func<WorldState, WorldState> Clone;
        public static Func<WorldState, string> Snapshot;
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static void Near(float expected, float actual, string message, float tolerance = .003f) { Require(Math.Abs(expected - actual) <= tolerance, message + ": expected " + expected + ", actual " + actual); }
        private static object Call(SimulationEngine engine, string name, params object[] args)
        {
            MethodInfo method = typeof(SimulationEngine).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Require(method != null, "Missing production method " + name); return method.Invoke(engine, args);
        }
        private static void Step(SimulationEngine engine, int ticks) { for (int i = 0; i < ticks; i++) engine.Step(.1f); }
        private static void Supply(SimulationEngine engine, int ticks) { for (int i = 0; i < ticks; i++) Call(engine, "UpdateCampfireSupply", .1f); }
        private static SimulationEngine Fixture(int people = 1)
        {
            var world = new WorldState { Width = 48, Height = 48, Seed = 80525, RngState = 9901, Cells = new TileData[48 * 48], NaturalWildlife = false };
            for (int i = 0; i < world.Cells.Length; i++) world.Cells[i] = new TileData { Terrain = TerrainType.Grass, Height = .5f, Moisture = .6f, Temperature = .5f, DrainageParent = -1 };
            var engine = new SimulationEngine(world); world.Weather.Automatic = false;
            world.Laws.Births = world.Laws.Aging = world.Laws.Warfare = world.Laws.FireSpread = world.Laws.VegetationGrowth = false;
            engine.Spawn(SpeciesType.Human, 16, 24, people);
            for (int i = 0; i < world.ResourceNodes.Length; i++) world.ResourceNodes[i] = default(ResourceNodeData);
            SettlementData town = world.Settlements[0]; town.Food = town.Wood = 0; town.NextPlanningTick = town.NextPolicyReviewTick = 100000;
            foreach (UnitData unit in world.Units)
            {
                unit.Age = 25; unit.Hunger = unit.Thirst = .1f; unit.Fatigue = unit.SocialNeed = 0;
                unit.Task = UnitTask.Idle; unit.DecisionTimer = 100000; unit.Path.Clear(); unit.PathCursor = 0;
                unit.X = town.X + .5f; unit.Y = town.Y + .5f;
            }
            Require(world.Validate(out string error), error); return engine;
        }
        private static BuildingData Hall(SimulationEngine engine) { return engine.World.GetCampfire(engine.World.Settlements[0].Id); }
        private static BuildingData Add(SimulationEngine engine, BuildingType type, int dx, int dy)
        {
            SettlementData town = engine.World.Settlements[0]; return (BuildingData)Call(engine, "AddBuilding", town, town.X + dx, town.Y + dy, type);
        }
        private static void At(UnitData unit, BuildingData building) { unit.X = building.X + .5f; unit.Y = building.Y + .5f; unit.Path.Clear(); unit.PathCursor = 0; }
        private static void Drink(SimulationEngine engine, UnitData unit, BuildingData source)
        {
            unit.Task = UnitTask.Drink; unit.WorkTimer = 2; unit.TargetBuildingId = source.Id; unit.TargetCell = engine.World.Index(source.X, source.Y);
            Call(engine, "PerformWaterActivity", unit, engine.World.GetSettlement(unit.SettlementId), 2f);
        }

        public static void OneDayProducesTwentyFivePortionsWithoutWorkersOrRandomDraws()
        {
            SimulationEngine engine = Fixture(); WorldState world = engine.World; BuildingData hall = Hall(engine); SettlementData town = world.Settlements[0];
            uint rng = world.RngState; int id = world.NextId; float wood = town.Wood;
            Near(600, SubsistenceRules.DaySeconds, "day duration"); Near(1, SubsistenceRules.FoodPerPersonPerDay, "daily food"); Near(1, SubsistenceRules.WaterPerPersonPerDay, "daily water");
            Supply(engine, 6000);
            Near(25, town.Food, "one campfire food per day"); Near(25, hall.WaterStored, "one campfire water per day");
            Require(world.RngState == rng && world.NextId == id && hall.WorkerId == 0, "Passive supply consumed RNG, IDs or required a worker"); Near(wood, town.Wood, "unexpected fuel debit");
            Require(world.Validate(out string error), error);
        }
        public static void DuplicateCampfireAndLevelDoNotMultiplyProduction()
        {
            SimulationEngine engine = Fixture(); WorldState world = engine.World; BuildingData first = Hall(engine), duplicate = Add(engine, BuildingType.Hall, 8, 0);
            first.Level = duplicate.Level = 5; world.Buildings.Reverse();
            Require(world.GetCampfire(world.Settlements[0].Id).Id == first.Id, "Campfire selection depends on list order instead of lowest ID");
            Supply(engine, 600);
            Near(2.5f, world.Settlements[0].Food, "duplicate/grade food exploit"); Near(2.5f, first.WaterStored, "duplicate/grade water exploit"); Near(0, duplicate.WaterStored, "secondary campfire also produced water");
            Near(50, SubsistenceRules.WaterCapacity(first), "grade-dependent campfire capacity"); Require(world.Validate(out string error), error);
        }
        public static void TwoDayReserveStopsNewProductionWithoutDeletingExistingFood()
        {
            SimulationEngine engine = Fixture(); SettlementData town = engine.World.Settlements[0]; BuildingData hall = Hall(engine);
            town.Food = hall.WaterStored = 49.99f; Supply(engine, 20);
            Near(50, town.Food, "food overflow", .0001f); Near(50, hall.WaterStored, "water overflow", .0001f);
            town.Food = 123.25f; Supply(engine, 10); Near(123.25f, town.Food, "older/larger communal reserve was truncated", .0001f);
            Require(engine.World.Validate(out string error), error);
        }
        public static void DeadBurningOrFloodedCampfireDoesNotProduce()
        {
            SimulationEngine engine = Fixture(); WorldState world = engine.World; BuildingData hall = Hall(engine); SettlementData town = world.Settlements[0]; int cell = world.Index(hall.X, hall.Y);
            hall.Health = 0; Require(!SubsistenceRules.IsWorkingCampfire(world, hall), "Dead fire declared working"); Supply(engine, 10);
            hall.Health = 100; world.Cells[cell].Fire = .5f; Require(!SubsistenceRules.IsWorkingCampfire(world, hall), "Burning fire declared working"); Supply(engine, 10);
            world.Cells[cell].Fire = 0; world.Cells[cell].Terrain = TerrainType.DeepWater; world.Cells[cell].WaterDepth = 3; world.Cells[cell].WaterKind = WaterKind.Ocean;
            Require(!SubsistenceRules.IsWorkingCampfire(world, hall), "Inaccessible underwater fire declared working"); Supply(engine, 10);
            Near(0, town.Food, "inactive campfire food"); Near(0, hall.WaterStored, "inactive campfire water");
        }
        public static void RemovedCampfireStopsSupplyAndDoesNotReappear()
        {
            SimulationEngine engine = Fixture(); WorldState world = engine.World; BuildingData hall = Hall(engine); SettlementData town = world.Settlements[0];
            hall.Health = 0; Call(engine, "DestroyInvalidBuildings");
            Require(world.GetCampfire(town.Id) == null, "Deleted campfire still selected"); Supply(engine, 600);
            Near(0, town.Food, "food generated after demolition"); Require(world.Buildings.All(b => b.Id != hall.Id), "Supplier resurrected destroyed building"); Require(world.Validate(out string error), error);
        }
        public static void ARealDayAddsOnePortionOfNeedToAdultsAndChildren()
        {
            SimulationEngine engine = Fixture(2); WorldState world = engine.World;
            UnitData child = world.Units.First(u => u.Id != world.Settlements[0].RulerId); child.Age = 8; child.Profession = UnitProfession.None;
            Step(engine, 6000);
            foreach (UnitData unit in world.Units) { Near(.5f, unit.Hunger, "daily hunger by age"); Near(.5f, unit.Thirst, "daily thirst by age"); }
            Near(25, world.Settlements[0].Food, "fixed-tick supply missing"); Near(25, Hall(engine).WaterStored, "fixed-tick water supply missing");
            Require(world.Units.Count == 2 && world.Tick == 6000 && world.Validate(out string error), "Population/day invariant failed");
        }
        public static void ResidentWalksToCampfireAndDrinksExactlyOnce()
        {
            SimulationEngine engine = Fixture(); WorldState world = engine.World; UnitData unit = world.Units[0]; BuildingData hall = Hall(engine);
            hall.WaterStored = 2; unit.X = hall.X + 6.5f; unit.Y = hall.Y + .5f; unit.Thirst = .8f; unit.DecisionTimer = 0;
            Step(engine, 10); Require(unit.Thirst >= .8f && hall.WaterStored > 2, "Remote drink debited stock before arrival");
            int ticks = 10; while (unit.Thirst > .6f && ticks < 250) { engine.Step(.1f); ticks++; }
            Require(unit.Thirst < .6f && (unit.X - hall.X - .5f) * (unit.X - hall.X - .5f) + (unit.Y - hall.Y - .5f) * (unit.Y - hall.Y - .5f) < 2, "No physical arrival and drink");
            Near(1 + ticks * .1f * SubsistenceRules.ProductionPerSecond, hall.WaterStored, "Drink must debit one water after arrival");
            Require(world.Validate(out string error), error);
        }
        public static void WaterBarrierPreventsRemoteFoodAndWaterConsumption()
        {
            SimulationEngine engine = Fixture(); WorldState world = engine.World; SettlementData town = world.Settlements[0]; BuildingData hall = Hall(engine); UnitData unit = world.Units[0];
            town.Food = hall.WaterStored = 10; int barrier = hall.X + 3;
            for (int y = 0; y < world.Height; y++) { int cell = world.Index(barrier, y); world.Cells[cell].Terrain = TerrainType.DeepWater; world.Cells[cell].WaterDepth = 3; world.Cells[cell].WaterKind = WaterKind.Ocean; world.Cells[cell].River = false; world.Infrastructure[cell] = default(InfrastructureCell); }
            world.NavigationRevision++; unit.X = barrier + 3.5f; unit.Y = hall.Y + .5f; unit.Hunger = unit.Thirst = .8f; unit.DecisionTimer = 0;
            Step(engine, 200);
            Near(10 + 20 * SubsistenceRules.ProductionPerSecond, town.Food, "food teleported across barrier"); Near(10 + 20 * SubsistenceRules.ProductionPerSecond, hall.WaterStored, "water teleported across barrier");
            Require(unit.Hunger > .8f && unit.Thirst > .8f && unit.X > barrier, "Disconnected resident received relief or crossed water"); Require(world.Validate(out string error), error);
        }
        public static void DrinkUsesOneWaterAndPartialCarriedWaterGivesProportionalRelief()
        {
            SimulationEngine engine = Fixture(); UnitData unit = engine.World.Units[0]; BuildingData hall = Hall(engine); hall.WaterStored = 3; unit.Thirst = .8f; At(unit, hall);
            Drink(engine, unit, hall); Near(2, hall.WaterStored, "one drink size", .00001f); Near(.4f, unit.Thirst, "one portion relief", .00001f);
            Call(engine, "PerformWaterActivity", unit, engine.World.Settlements[0], 2f); Near(2, hall.WaterStored, "idle activity double-debited water", .00001f);
            unit.CarriedWater = .25f; unit.Thirst = .8f; Require((bool)Call(engine, "ChooseWater", unit, engine.World.Settlements[0]), "Carried water ignored");
            Call(engine, "PerformWaterActivity", unit, engine.World.Settlements[0], 2f); Near(0, unit.CarriedWater, "partial cargo not consumed"); Near(.7f, unit.Thirst, "partial cargo invented relief", .00001f); Near(2, hall.WaterStored, "carried drink also took camp water");
            Require(engine.World.Validate(out string error), error);
        }
        public static void TwentyFiveResidentsBalanceOneDayWhileTheTwentySixthNeedsOtherFoodAndWater()
        {
            SimulationEngine engine = Fixture(26); WorldState world = engine.World; SettlementData town = world.Settlements[0]; BuildingData hall = Hall(engine); Supply(engine, 6000);
            for (int i = 0; i < world.Units.Count; i++)
            {
                UnitData unit = world.Units[i]; At(unit, hall); unit.Hunger = .6f; unit.Thirst = .7f; unit.Task = UnitTask.Eat; unit.WorkTimer = 1.2f; unit.TargetCell = world.Index(hall.X, hall.Y);
                Call(engine, "PerformNeedsActivity", unit, town, 1.2f); Drink(engine, unit, hall);
                if (i < 25) { Near(.2f, unit.Hunger, "supported resident food"); Near(.3f, unit.Thirst, "supported resident water"); }
                else { Near(.6f, unit.Hunger, "26th free meal invented"); Near(.7f, unit.Thirst, "26th free water invented"); }
            }
            Require(town.Food >= 0 && town.Food < .01f && hall.WaterStored >= 0 && hall.WaterStored < .01f, "Daily budget not conserved"); Require(world.Validate(out string error), error);
        }
        public static void CampfireWaterCannotReplaceWellWaterInBrewing()
        {
            SimulationEngine engine = Fixture(); SettlementData town = engine.World.Settlements[0]; BuildingData hall = Hall(engine), brewery = Add(engine, BuildingType.Brewery, 4, 4), well = Add(engine, BuildingType.Well, 4, 0);
            hall.WaterStored = 50; well.WaterStored = 0;
            Require(Call(engine, "BrewingWell", town, brewery) == null, "Brewing took subsistence water instead of requiring a well");
            Near(0, (float)Call(engine, "SupplyAvailable", hall, town, 6), "Workshop water pickup includes campfire reserve");
            well.WaterStored = 1; Require(((BuildingData)Call(engine, "BrewingWell", town, brewery)).Id == well.Id, "Actual well stopped supplying brewery"); Require(engine.World.Validate(out string error), error);
        }
        public static void CampfireReserveRoundTripsAndContinuesIdenticallyDuringADrinkJourney()
        {
            SimulationEngine engine = Fixture(); WorldState world = engine.World; BuildingData hall = Hall(engine); UnitData unit = world.Units[0]; hall.WaterStored = 7.25f; world.Settlements[0].Food = 11.5f;
            unit.X = hall.X + 5.5f; unit.Y = hall.Y + .5f; unit.Thirst = .8f; unit.DecisionTimer = 0; Step(engine, 7);
            var restored = new SimulationEngine(Clone(world)); Near(hall.WaterStored, Hall(restored).WaterStored, "water changed on load", .000001f);
            Step(engine, 211); Step(restored, 211); Require(Snapshot(world) == Snapshot(restored.World), "Save continuation duplicated supply, drink or path"); Require(world.Validate(out string error), error);
        }
        public static void ExistingEmptyCampfireLoadsWithoutStockGrantOrDevelopmentReset()
        {
            SimulationEngine engine = Fixture(); WorldState legacy = Clone(engine.World); legacy.SubsistenceVersion = 0; SettlementData town = legacy.Settlements[0]; BuildingData hall = legacy.GetCampfire(town.Id);
            town.Food = 31.25f; hall.WaterStored = 0; foreach (TechnologyDefinition technology in TechnologyCatalog.All) town.UnlockedTechnologies.Add(technology.Id);
            town.ActiveTechnology = ""; town.ResearchProgress = 0; int road = legacy.Index(3, 3); legacy.Infrastructure[road].RoadLevel = 1;
            uint rng = legacy.RngState; int id = legacy.NextId; long tick = legacy.Tick; int technologies = town.UnlockedTechnologies.Count;
            var restored = new SimulationEngine(legacy); Near(31.25f, town.Food, "legacy food grant/reset"); Near(0, hall.WaterStored, "legacy initial water grant");
            Require(legacy.SubsistenceVersion == 1 && legacy.RngState == rng && legacy.NextId == id && legacy.Tick == tick && legacy.Infrastructure[road].RoadLevel == 1 && town.UnlockedTechnologies.Count == technologies, "Load reset development, clock, RNG or IDs");
            string once = Snapshot(legacy); new SimulationEngine(legacy); Require(Snapshot(legacy) == once, "Load initialization not idempotent"); Require(restored.World.Validate(out string error), error);
        }
        public static void InvalidWaterReservesRemainRejected()
        {
            SimulationEngine engine = Fixture(); WorldState good = engine.World; BuildingData hall = Hall(engine); hall.WaterStored = 50;
            Require(good.Validate(out string error), "New hall capacity rejected: " + error);
            WorldState bad = Clone(good); bad.GetCampfire(bad.Settlements[0].Id).WaterStored = 50.1f; Require(!bad.Validate(out _), "Campfire overflow accepted");
            bad = Clone(good); bad.GetCampfire(bad.Settlements[0].Id).WaterStored = float.NaN; Require(!bad.Validate(out _), "NaN reserve accepted");
            bad = Clone(good); bad.GetCampfire(bad.Settlements[0].Id).WaterStored = -1; Require(!bad.Validate(out _), "Negative reserve accepted");
            bad = Clone(good); bad.SubsistenceVersion = 0; Require(!bad.Validate(out _), "Legacy version admitted a new campfire reserve");
            bad = Clone(good); bad.SubsistenceVersion = 2; Require(!bad.Validate(out _), "Unknown future subsistence version accepted");
            bad = Clone(good); bad.SubsistenceVersion = -1; Require(!bad.Validate(out _), "Negative subsistence version accepted");
            BuildingData house = Add(engine, BuildingType.House, 7, 3); house.WaterStored = 1; Require(!good.Validate(out _), "Unsupported building admitted a water reserve");
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class SubsistenceTests
    {
        [SetUp] public void Serializers() { SubsistenceScenarios.Clone = w => JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)); SubsistenceScenarios.Snapshot = w => JsonUtility.ToJson(w); }
        [TestCase("OneDayProducesTwentyFivePortionsWithoutWorkersOrRandomDraws")]
        [TestCase("DuplicateCampfireAndLevelDoNotMultiplyProduction")]
        [TestCase("TwoDayReserveStopsNewProductionWithoutDeletingExistingFood")]
        [TestCase("DeadBurningOrFloodedCampfireDoesNotProduce")]
        [TestCase("RemovedCampfireStopsSupplyAndDoesNotReappear")]
        [TestCase("ARealDayAddsOnePortionOfNeedToAdultsAndChildren")]
        [TestCase("ResidentWalksToCampfireAndDrinksExactlyOnce")]
        [TestCase("WaterBarrierPreventsRemoteFoodAndWaterConsumption")]
        [TestCase("DrinkUsesOneWaterAndPartialCarriedWaterGivesProportionalRelief")]
        [TestCase("TwentyFiveResidentsBalanceOneDayWhileTheTwentySixthNeedsOtherFoodAndWater")]
        [TestCase("CampfireWaterCannotReplaceWellWaterInBrewing")]
        [TestCase("CampfireReserveRoundTripsAndContinuesIdenticallyDuringADrinkJourney")]
        [TestCase("ExistingEmptyCampfireLoadsWithoutStockGrantOrDevelopmentReset")]
        [TestCase("InvalidWaterReservesRemainRejected")]
        public void Scenario(string name) { typeof(SubsistenceScenarios).GetMethod(name).Invoke(null, null); }
    }
#endif
}
