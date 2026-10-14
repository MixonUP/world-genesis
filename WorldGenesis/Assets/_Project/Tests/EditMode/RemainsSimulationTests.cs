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
    public static class RemainsScenarios
    {
        public static Func<WorldState, WorldState> Clone;
        public static Func<WorldState, string> Snapshot;
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Valid(WorldState w) { Need(w.Validate(out string error), error); }
        private static object Call(SimulationEngine e, string name, params object[] args) { return typeof(SimulationEngine).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(e, args); }
        private static SimulationEngine Make()
        {
            var w = new WorldState { Width = 40, Height = 40, Seed = 100014, RngState = 100014, Cells = new TileData[1600] };
            for (int i = 0; i < w.Cells.Length; i++) w.Cells[i] = new TileData { Terrain = TerrainType.Grass, Height = .55f, RouteHeight = .55f, Moisture = .6f, Temperature = .5f, DrainageParent = -1 };
            var e = new SimulationEngine(w); w.NaturalWildlife = w.Weather.Automatic = w.Laws.Aging = w.Laws.Births = w.Laws.Warfare = false; return e;
        }
        private static UnitData Add(SimulationEngine e, SpeciesType species)
        { int kind = (int)species; e.Spawn(species, kind < 4 ? 6 + kind % 2 * 26 : 12, kind < 4 ? 6 + kind / 2 * 26 : 12, 1); UnitData u = e.World.Units.Last(); u.Age = 25; u.X = 12.5f; u.Y = 12.5f; u.DecisionTimer = 100000; return u; }
        public static void AllSeventeenSpeciesLeaveOneActualDeathRecord()
        {
            var e = Make(); for (int species = 0; species <= 16; species++) Add(e, (SpeciesType)species).Health = 0;
            int[] ids = e.World.Units.Select(u => u.Id).ToArray(); e.Step(.1f);
            Need(e.World.Units.Count == 0 && e.World.Remains.Count == 17, "Actual removal did not leave exactly one record for every species");
            Need(e.World.Remains.All(r => ids.Contains(r.SourceUnitId) && r.X == 12.5f && r.Y == 12.5f && r.RemainingSeconds == RemainsRules.LifetimeSeconds), "Wrong death provenance, location or initial lifetime");
            float meat = e.World.AgriculturalDrops.Where(d => d.Product == AgriculturalProduct.Meat).Sum(d => d.Amount);
            e.Step(.1f); Need(e.World.Remains.Count == 17 && e.World.AgriculturalDrops.Where(d => d.Product == AgriculturalProduct.Meat).Sum(d => d.Amount) == meat, "Corpse duplicated or produced additional loot"); Valid(e.World);
        }
        public static void CountdownSurvivesSaveAndExpiresWithoutWallClock()
        {
            var a = Make(); Add(a, SpeciesType.Wolf).Health = 0; a.Step(.1f); a.World.Remains[0].RemainingSeconds = .35f;
            var b = new SimulationEngine(Clone(a.World)); Need(b.World.Remains[0].RemainingSeconds == .35f, "Load reset corpse lifetime");
            a.Step(0); Need(a.World.Remains[0].RemainingSeconds == .35f, "Paused simulation changed remains");
            for (int i = 0; i < 3; i++) { a.Step(.1f); b.Step(.1f); }
            Need(a.World.Remains.Count == 1 && b.World.Remains.Count == 1, "Corpse disappeared early");
            a.Step(.1f); b.Step(.1f); Need(a.World.Remains.Count == 0 && b.World.Remains.Count == 0, "Expired remains were retained");
            Need(Snapshot(a.World) == Snapshot(b.World), "Save continuation diverged"); Valid(a.World);
        }
        public static void MassDeathIsBoundedAndDiscardsOldestRecords()
        {
            var e = Make(); e.Spawn(SpeciesType.Sheep, 12, 12, RemainsRules.MaximumCount + 8);
            foreach (UnitData u in e.World.Units) u.Health = 0;
            e.Step(.1f); Need(e.World.Units.Count == 0 && e.World.Remains.Count == RemainsRules.MaximumCount, "Mass death exceeded fixed remains capacity");
            int oldest = e.World.Remains[0].SourceUnitId; Add(e, SpeciesType.Wolf).Health = 0; e.Step(.1f);
            Need(e.World.Remains.Count == RemainsRules.MaximumCount && e.World.Remains.All(r => r.SourceUnitId != oldest), "Overflow did not remove oldest record"); Valid(e.World);
        }
        public static void MigrationIsEmptyIdempotentAndCorruptSavesAreRejected()
        {
            var e = Make(); WorldState old = Clone(e.World); old.RemainsVersion = 0; old.Remains = null; uint rng = old.RngState; int next = old.NextId;
            new SimulationEngine(old); Need(old.RemainsVersion == 1 && old.Remains.Count == 0 && old.RngState == rng && old.NextId == next, "Migration invented deaths or consumed simulation IDs/randomness");
            string migrated = Snapshot(old); new SimulationEngine(old); Need(Snapshot(old) == migrated, "Repeated migration changed state");
            Add(e, SpeciesType.Chicken).Health = 0; e.Step(.1f); Valid(e.World);
            foreach (Action<WorldState> corrupt in new Action<WorldState>[] {
                w => w.Remains[0].RemainingSeconds = float.NaN, w => w.Remains[0].RemainingSeconds = RemainsRules.LifetimeSeconds + 1,
                w => w.Remains.Add(w.Remains[0]), w => w.Remains[0].X = w.Width, w => w.Remains[0].Direction = 4,
                w => w.Remains[0].DeathTick = w.Tick + 1, w => w.RemainsVersion = 0, w => w.Remains = null })
            { WorldState bad = Clone(e.World); corrupt(bad); Need(!bad.Validate(out _), "Corrupt remains save accepted"); }
        }
        public static void IndoorDeathUsesReleaseDoorAndShipDeathUsesVesselPosition()
        {
            var e = Make(); UnitData resident = Add(e, SpeciesType.Human); WorldState w = e.World;
            BuildingData house = (BuildingData)Call(e, "AddBuilding", w.Settlements[0], 20, 20, BuildingType.House);
            resident.InsideBuildingId = house.Id; resident.X = 20.5f; resident.Y = 20.5f; resident.Health = 0;
            e.Step(.1f); RemainsData indoor = w.Remains.Single();
            Need(indoor.X == resident.X && indoor.Y == resident.Y && indoor.X == house.EntranceCell % w.Width + .5f && indoor.Y == house.EntranceCell / w.Width + .5f, "Indoor corpse disagrees with actual release doorway"); Valid(w);
            UnitData sailor = Add(e, SpeciesType.Human); e.Paint(TerrainType.DeepWater, 30, 30, 0);
            var ship = new ShipData { Id = w.NextShipId++, Kind = ShipKind.FishingBoat, Phase = ShipPhase.Stranded, OwnerSettlementId = sailor.SettlementId, X = 30.5f, Y = 30.5f, Health = 80, MaxHealth = 80 };
            w.Ships.Add(ship); ship.CrewIds.Add(sailor.Id); sailor.ShipId = ship.Id; sailor.Health = 0;
            // Deliberately stale shore coordinates detect recording after maritime links are released.
            sailor.X = 12.5f; sailor.Y = 12.5f; e.Step(.1f); RemainsData atSea = w.Remains.Last();
            Need(atSea.X == ship.X && atSea.Y == ship.Y && atSea.WaterDeath && atSea.RemainingSeconds == RemainsRules.WaterLifetimeSeconds, "Ship death left bones at a stale shore position or used land lifetime");
            Need(ship.CrewIds.Count == 0 && w.GetUnit(sailor.Id) == null, "Remains blocked crew removal"); Valid(w);
        }
        public static void ChildScaleIsCivilianOnlyAndBridgeDoesNotSinkBones()
        {
            var e = Make(); UnitData child = Add(e, SpeciesType.Elf); child.Age = 8; child.Health = 0;
            e.Step(.1f); Need(e.World.Remains.Single().Child, "Child remains lost age classification");
            UnitData chicken = Add(e, SpeciesType.Chicken); chicken.Age = 1; chicken.Health = 0;
            int cell = e.World.Index(12, 12); e.Paint(TerrainType.ShallowWater, 12, 12, 0); e.World.Infrastructure[cell].BridgeLevel = 1;
            e.Step(.1f); RemainsData bones = e.World.Remains.Last(); Need(!bones.Child && !bones.WaterDeath, "Animal age became human childhood or bridge death sank");
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class RemainsSimulationTests
    {
        [SetUp] public void Serializer() { RemainsScenarios.Clone = w => JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)); RemainsScenarios.Snapshot = w => JsonUtility.ToJson(w); }
        [Test] public void DeathOnce() { RemainsScenarios.AllSeventeenSpeciesLeaveOneActualDeathRecord(); }
        [Test] public void SaveAndExpiry() { RemainsScenarios.CountdownSurvivesSaveAndExpiresWithoutWallClock(); }
        [Test] public void Bound() { RemainsScenarios.MassDeathIsBoundedAndDiscardsOldestRecords(); }
        [Test] public void MigrationAndValidation() { RemainsScenarios.MigrationIsEmptyIdempotentAndCorruptSavesAreRejected(); }
        [Test] public void IndoorAndShip() { RemainsScenarios.IndoorDeathUsesReleaseDoorAndShipDeathUsesVesselPosition(); }
        [Test] public void ChildAndBridge() { RemainsScenarios.ChildScaleIsCivilianOnlyAndBridgeDoesNotSinkBones(); }
    }
#endif
}
