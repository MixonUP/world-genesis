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
    public static class SettlementExpansionScenarios
    {
        public static Func<WorldState, WorldState> Clone;
        public static Func<WorldState, string> Snapshot;
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static object Call(SimulationEngine engine, string method, params object[] args) { return typeof(SimulationEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(engine, args); }
        public static SimulationEngine Fixture(int seed = 1701, int residents = 24)
        {
            var world = new WorldState { Width = 96, Height = 96, Seed = seed, RngState = (uint)seed, Cells = new TileData[96 * 96], NaturalWildlife = false };
            for (int i = 0; i < world.Cells.Length; i++) world.Cells[i] = new TileData { Terrain = TerrainType.Grass, Height = .58f, RouteHeight = .58f, Vegetation = .7f, Moisture = .5f, Temperature = .6f, DrainageParent = -1 };
            var engine = new SimulationEngine(world);
            world.Weather.Automatic = false; world.Laws.Aging = false; world.Laws.Births = false; world.Laws.FireSpread = false;
            engine.Spawn(SpeciesType.Human, 16, 44, residents);
            SettlementData town = world.Settlements[0]; town.Wood = 4; town.Food = 30; town.NextColonizationTick = 100000;
            world.GetCampfire(town.Id).WaterStored = 10;
            for (int i = 0; i < world.Units.Count; i++)
            {
                UnitData unit = world.Units[i]; unit.X = town.X + .5f; unit.Y = town.Y + .5f; unit.Age = 25; unit.Sex = (Sex)(i % 2);
                unit.Task = UnitTask.Idle; unit.DecisionTimer = 10000; unit.Fatigue = 0; unit.Hunger = unit.Thirst = .1f;
            }
            Valid(engine); return engine;
        }
        private static int Target(SimulationEngine engine) { SettlementData s = engine.World.Settlements[0]; return engine.World.Index(s.X + 32, s.Y); }
        private static SettlementExpeditionData Start(SimulationEngine engine)
        {
            Need(engine.TryStartSettlementExpedition(engine.World.Settlements[0].Id, Target(engine)), "expedition did not start");
            return engine.World.SettlementExpeditions.Last();
        }
        private static void Valid(SimulationEngine engine) { Need(engine.World.Validate(out string error), error); }
        public static void AdvanceUntil(SimulationEngine engine, Func<bool> done, int maxTicks = 1500)
        {
            for (int i = 0; i < maxTicks && !done(); i++) { engine.Step(.1f); if (i % 10 == 0) Valid(engine); }
            Need(done(), "expedition deadline: " + string.Join("; ", engine.World.SettlementExpeditions.Select(e => e.Phase + "/" + e.LastReason)));
            Valid(engine);
        }
        public static void ExistingPeoplePhysicallyCarryAndPayForNewIndependentTown()
        {
            var engine = Fixture(); var world = engine.World; int[] initialIds = world.Units.Select(u => u.Id).ToArray(); int buildings = world.Buildings.Count;
            var expedition = Start(engine); int[] ids = expedition.MemberIds.ToArray();
            Need(world.Settlements.Count == 1 && world.Settlements[0].Wood == 4 && ids.All(id => world.GetUnit(id).CarriedWood == 0), "remote loading at command time");
            AdvanceUntil(engine, () => expedition.Phase == SettlementExpeditionPhase.Travelling);
            Need(world.Settlements[0].Wood == 0 && ids.Sum(id => world.GetUnit(id).CarriedWood) == 4, "planks were not physically loaded");
            Need(ids.Sum(id => world.GetUnit(id).CarriedFood) <= 1 && ids.Sum(id => world.GetUnit(id).CarriedWater) <= 1, "free provisions");
            float startX = world.GetUnit(ids[0]).X; engine.Step(.1f);
            Need(world.GetUnit(ids[0]).X > startX && world.GetUnit(ids[0]).X - startX < .3f, "settler moved by teleport");
            Need(ids.All(id => SimulationEngine.CarriedLoad(world.GetUnit(id)) <= SimulationEngine.CarryCapacityFor(world.GetUnit(id)) + .0001f), "overloaded settler");
            AdvanceUntil(engine, () => !ExpansionCatalog.Active(expedition));
            Need(expedition.Phase == SettlementExpeditionPhase.Completed && expedition.PlanksSpent == 4 && world.SettlementsFoundedByExpeditions == 1, "paid founding failed");
            Need(initialIds.SequenceEqual(world.Units.Select(u => u.Id)), "people were spawned, replaced or deleted");
            SettlementData town = world.GetSettlement(expedition.FoundedSettlementId); BuildingData hall = world.GetCampfire(town.Id);
            Need(town.ParentSettlementId == expedition.SourceSettlementId && town.SovereignSettlementId == 0 && town.EnemyId == 0, "new town is not independent and peaceful");
            Need(world.Units.Count(u => u.SettlementId == town.Id) == 6 && world.Buildings.Count == buildings + 1 && hall.PaidConstructionMaterials[(int)ItemType.Plank] == 4, "founding created free people/buildings or omitted invoice");
            Need(town.Wood == 0 && town.Food <= 1.01f && hall.WaterStored <= 1.01f && expedition.FoodConsumed > 0 && expedition.WaterConsumed > 0, "founding supplied gifts or ignored consumption");
        }
        public static void OffCentreGatheringUsesMovementAndCompletes()
        {
            var engine = Fixture(); foreach (var u in engine.World.Units) { u.X += .35f; u.Y -= .3f; }
            var expedition = Start(engine); var first = engine.World.GetUnit(expedition.MemberIds[0]); float x = first.X, y = first.Y;
            engine.Step(.1f); Need(Math.Abs(first.X - x) + Math.Abs(first.Y - y) > .001f && Math.Abs(first.X - x) + Math.Abs(first.Y - y) < .3f, "off-centre waypoint did not use real movement");
            AdvanceUntil(engine, () => !ExpansionCatalog.Active(expedition)); Need(expedition.Phase == SettlementExpeditionPhase.Completed, "off-centre source blocked settlement");
        }
        public static void InsufficientGoodsChildrenAndBusyCrewCannotBeDrafted()
        {
            var engine = Fixture(); var town = engine.World.Settlements[0]; town.Wood = 3;
            int next = engine.World.NextExpeditionId; uint rng = engine.World.RngState;
            Need(!engine.TryStartSettlementExpedition(town.Id, Target(engine)) && engine.World.NextExpeditionId == next && engine.World.RngState == rng, "failed order mutated ids/RNG");
            town.Wood = 4;
            foreach (var unit in engine.World.Units) if (unit.Id != town.RulerId) unit.Age = 12;
            Need(!engine.TryStartSettlementExpedition(town.Id, Target(engine)), "drafted children");
            foreach (var unit in engine.World.Units) { unit.Age = 25; unit.Task = UnitTask.Produce; }
            Need(!engine.TryStartSettlementExpedition(town.Id, Target(engine)), "stole an active industrial worker");
            Need(engine.World.SettlementExpeditions.Count == 0 && town.Wood == 4, "failed order charged goods");
        }
        public static void ASecondOrderCannotUseTheSameCitizensOrSupplies()
        {
            var engine = Fixture(); var expedition = Start(engine); var town = engine.World.Settlements[0];
            Need(!engine.TryStartSettlementExpedition(town.Id, engine.World.Index(town.X, town.Y + 32)), "parallel order duplicated the party");
            Need(engine.World.SettlementExpeditions.Count == 1 && expedition.MemberIds.Distinct().Count() == 6, "duplicate membership");
            Valid(engine);
        }
        public static void CancelledTripWalksBackAndReturnsActualPlanks()
        {
            var engine = Fixture(); var expedition = Start(engine); var world = engine.World;
            AdvanceUntil(engine, () => expedition.Phase == SettlementExpeditionPhase.Travelling);
            for (int i = 0; i < 60; i++) engine.Step(.1f);
            Need(engine.CancelSettlementExpedition(expedition.Id), "cancel rejected");
            Need(world.Units.Any(u => u.ExpeditionId != 0 && u.CarriedWood > 0), "cancel teleported cargo home");
            AdvanceUntil(engine, () => !ExpansionCatalog.Active(expedition));
            Need(expedition.Phase == SettlementExpeditionPhase.Cancelled && world.Settlements.Count == 1 && world.Settlements[0].Wood == 4 && world.Units.All(u => u.ExpeditionId == 0), "return duplicated or lost planks");
        }
        public static void DeathAbortsAndConservesRemainingCargoWithoutCloning()
        {
            var engine = Fixture(); var expedition = Start(engine); var world = engine.World;
            AdvanceUntil(engine, () => expedition.Phase == SettlementExpeditionPhase.Travelling);
            UnitData carrier = world.GetUnit(expedition.MemberIds[0]); carrier.Health = 0;
            engine.Step(.1f); AdvanceUntil(engine, () => !ExpansionCatalog.Active(expedition));
            Need(world.Units.Count == 23 && world.Settlements.Count == 1 && world.Settlements[0].Wood == 3 && world.LostResources[(int)ItemType.Plank] == 1, "death cargo ledger or identities changed");
            Need(expedition.Phase == SettlementExpeditionPhase.Cancelled, "founded despite missing carrier");
        }
        public static void DestroyedHomeReleasesSurvivorsWithTheirPhysicalCargo()
        {
            var engine = Fixture(); var expedition = Start(engine); var world = engine.World;
            AdvanceUntil(engine, () => expedition.Phase == SettlementExpeditionPhase.Travelling);
            for (int i = 0; i < 100; i++) engine.Step(.1f);
            world.GetBuilding(expedition.SourceBuildingId).Health = 0; Call(engine, "DestroyInvalidBuildings");
            engine.Step(.1f);
            Need(world.Settlements.Count == 1 && world.Units.All(u => u.ExpeditionId == 0) && world.Units.Sum(u => u.CarriedWood) == 4, "destroyed camp swallowed or teleported settler cargo"); Valid(engine);
        }
        public static void BrokenRouteNeverTeleportsAcrossBarrier()
        {
            var engine = Fixture(); var expedition = Start(engine); var world = engine.World;
            AdvanceUntil(engine, () => expedition.Phase == SettlementExpeditionPhase.Travelling);
            int wall = world.Settlements[0].X + 10;
            for (int y = 0; y < world.Height; y++) { int i = world.Index(wall, y); world.Cells[i].Terrain = TerrainType.Mountain; }
            world.NavigationRevision++;
            AdvanceUntil(engine, () => !ExpansionCatalog.Active(expedition));
            Need(expedition.Phase == SettlementExpeditionPhase.Cancelled && world.Settlements.Count == 1 && world.Units.All(u => u.X < wall) && world.Settlements[0].Wood == 4, "crossed impossible route or lost return cargo");
        }
        public static void FloodedOrOccupiedDestinationCannotReceiveFreeCity()
        {
            var engine = Fixture(); var expedition = Start(engine); var world = engine.World;
            AdvanceUntil(engine, () => expedition.Phase == SettlementExpeditionPhase.Travelling);
            int cell = expedition.TargetCell; engine.Paint(TerrainType.ShallowWater, cell % world.Width, cell / world.Width, 1);
            AdvanceUntil(engine, () => !ExpansionCatalog.Active(expedition));
            Need(expedition.Phase == SettlementExpeditionPhase.Cancelled && world.Settlements.Count == 1 && world.Cells[cell].BuildingId == 0, "town founded in water");
        }
        public static void SavedMarchContinuesIdenticallyWithoutRecharging()
        {
            var first = Fixture(); var expedition = Start(first);
            AdvanceUntil(first, () => expedition.Phase == SettlementExpeditionPhase.Travelling);
            for (int i = 0; i < 50; i++) first.Step(.1f);
            var second = new SimulationEngine(Clone(first.World));
            for (int i = 0; i < 430; i++) { first.Step(.1f); second.Step(.1f); }
            Need(Snapshot(first.World) == Snapshot(second.World), "save continuation diverged"); Valid(first); Valid(second);
        }
        public static void LegacyExpansionMigrationCreatesNoCitizensGoodsOrRng()
        {
            var engine = Fixture(); var world = Clone(engine.World); world.ExpansionVersion = 0;
            foreach (var town in world.Settlements) { town.TerritoryRadius = 0; town.NextExpansionTick = town.NextColonizationTick = 0; }
            uint rng = world.RngState; int id = world.NextId, population = world.Units.Count; float wood = world.Settlements[0].Wood;
            var restored = new SimulationEngine(world);
            Need(world.RngState == rng && world.NextId == id && world.Units.Count == population && world.Settlements[0].Wood == wood && world.ExpansionVersion == 1, "migration issued resources/people/RNG");
            string once = Snapshot(world); new SimulationEngine(world); Need(once == Snapshot(world), "migration not idempotent"); Valid(restored);
        }
        public static void TerritoryGrowsGraduallyWithoutStealingForeignClaims()
        {
            var engine = Fixture(); var town = engine.World.Settlements[0]; int old = town.TerritoryRadius;
            town.NextExpansionTick = 0;
            Call(engine, "UpdateSettlementExpansion");
            Need(town.TerritoryRadius == old + 1 && ExpansionCatalog.BuildingLimit(town) >= 24, "territory jumped or remained frozen");
            int grown = town.TerritoryRadius; Call(engine, "UpdateSettlementExpansion"); Need(town.TerritoryRadius == grown, "territory ignored growth timer"); Valid(engine);
        }
        public static void AutonomousTownSelectsHighDrySiteAndFounders()
        {
            foreach (int seed in new[] { 1701, 1702, 1703 })
            {
                var engine = Fixture(seed); var world = engine.World; var town = world.Settlements[0];
                int high = world.Index(town.X + 32, town.Y); world.Cells[high].Height = .72f; world.Cells[high].Terrain = TerrainType.Highlands;
                Need(engine.FindSettlementExpansionSite(town.Id) == high, "site selector ignored reachable high ground");
                town.NextColonizationTick = 0;
                engine.Step(1);
                Need(world.SettlementExpeditions.Count == 1, "autonomous planner never dispatched citizens");
                var expedition = world.SettlementExpeditions[0]; AdvanceUntil(engine, () => !ExpansionCatalog.Active(expedition));
                Need(expedition.Phase == SettlementExpeditionPhase.Completed && expedition.TargetCell == high, "autonomous trip failed");
            }
        }
        public static void DepartureSeparatesSixWalkersWithoutIncreasingCarryingCapacity()
        {
            var engine = Fixture(); var expedition = Start(engine);
            AdvanceUntil(engine, () => expedition.Phase == SettlementExpeditionPhase.Travelling);
            for (int i = 0; i < 60; i++) engine.Step(.1f);
            var people = expedition.MemberIds.Select(id => engine.World.GetUnit(id)).ToArray();
            Need(people.Select(u => (int)Math.Floor(u.X * 2)).Distinct().Count() == 6, "walkers are perfectly superimposed");
            Need(people.All(u => SimulationEngine.CarriedLoad(u) <= 1.00001f), "march invented carrying capacity"); Valid(engine);
        }
        public static void MalformedMembershipAndNaNProgressAreRejected()
        {
            var engine = Fixture(); var expedition = Start(engine); var world = Clone(engine.World);
            world.SettlementExpeditions[0].MemberIds[1] = world.SettlementExpeditions[0].MemberIds[0];
            Need(!world.Validate(out _), "duplicate settler accepted");
            world = Clone(engine.World); world.SettlementExpeditions[0].FoundingProgress = float.NaN; Need(!world.Validate(out _), "NaN founding progress accepted");
            world = Clone(engine.World); world.Units.First(u => u.ExpeditionId != 0).ExpeditionId = 0; Need(!world.Validate(out _), "orphan membership accepted");
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class SettlementExpansionTests
    {
        [SetUp] public void Setup() { SettlementExpansionScenarios.Clone = w => JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)); SettlementExpansionScenarios.Snapshot = w => JsonUtility.ToJson(w); }
        [TestCase("ExistingPeoplePhysicallyCarryAndPayForNewIndependentTown")]
        [TestCase("OffCentreGatheringUsesMovementAndCompletes")]
        [TestCase("InsufficientGoodsChildrenAndBusyCrewCannotBeDrafted")]
        [TestCase("ASecondOrderCannotUseTheSameCitizensOrSupplies")]
        [TestCase("CancelledTripWalksBackAndReturnsActualPlanks")]
        [TestCase("DeathAbortsAndConservesRemainingCargoWithoutCloning")]
        [TestCase("DestroyedHomeReleasesSurvivorsWithTheirPhysicalCargo")]
        [TestCase("BrokenRouteNeverTeleportsAcrossBarrier")]
        [TestCase("FloodedOrOccupiedDestinationCannotReceiveFreeCity")]
        [TestCase("SavedMarchContinuesIdenticallyWithoutRecharging")]
        [TestCase("LegacyExpansionMigrationCreatesNoCitizensGoodsOrRng")]
        [TestCase("TerritoryGrowsGraduallyWithoutStealingForeignClaims")]
        [TestCase("AutonomousTownSelectsHighDrySiteAndFounders")]
        [TestCase("MalformedMembershipAndNaNProgressAreRejected")]
        [TestCase("DepartureSeparatesSixWalkersWithoutIncreasingCarryingCapacity")]
        public void Scenario(string name) { typeof(SettlementExpansionScenarios).GetMethod(name).Invoke(null, null); }
    }
#endif
}
