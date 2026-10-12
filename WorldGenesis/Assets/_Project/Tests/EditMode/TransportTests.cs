using System;
using System.Reflection;
using WorldGenesis.Simulation;
#if UNITY_5_3_OR_NEWER
using NUnit.Framework;
using UnityEngine;
#endif
namespace WorldGenesis.Tests
{
    public static class TransportScenarios
    {
        public static Func<WorldState, WorldState> Clone;
        public static Func<WorldState, string> Snapshot;
        private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static object Call(SimulationEngine e, string method, params object[] args) { return typeof(SimulationEngine).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(e, args); }
        private static SimulationEngine Town()
        {
            WorldState w = new WorldState { Width = 40, Height = 40, Seed = 77, RngState = 11, Cells = new TileData[1600], NaturalWildlife = false };
            for (int i = 0; i < w.Cells.Length; i++) w.Cells[i] = new TileData { Terrain = TerrainType.Grass, Height = .55f, Vegetation = .6f, Moisture = .6f, DrainageParent = -1 };
            SimulationEngine e = new SimulationEngine(w); e.Spawn(SpeciesType.Human, 20, 20, 4);
            w.Laws.Aging = w.Laws.Births = w.Laws.Warfare = false;
            SettlementData s = w.Settlements[0]; s.Wood = 40; s.Food = 200; s.NextPolicyReviewTick = 100000;
            foreach (TechnologyDefinition t in TechnologyCatalog.All) s.UnlockedTechnologies.Add(t.Id);
            foreach (UnitData u in w.Units) { u.Age = 25; u.Hunger = u.Thirst = u.Fatigue = .05f; u.DecisionTimer = 1000; u.Task = UnitTask.Idle; }
            return e;
        }
        private static int Water(WorldState w, int x, int y, float depth = 1.2f, float current = .3f)
        {
            int cell = w.Index(x, y); w.Cells[cell].Terrain = TerrainType.ShallowWater; w.Cells[cell].River = true;
            w.Cells[cell].WaterKind = WaterKind.River; w.Cells[cell].WaterDepth = depth; w.Cells[cell].CurrentStrength = current; w.Cells[cell].ChannelWidth = 1; return cell;
        }
        public static void TerrainAndWaterAffectTraversal()
        {
            SimulationEngine e = Town(); WorldState w = e.World; int c = w.Index(24, 20);
            float plain = w.GetTerrainSpeed(c); w.Cells[c].Terrain = TerrainType.Swamp; Need(w.GetTerrainSpeed(c) < plain * .5f, "swamp slow");
            w.Cells[c].Terrain = TerrainType.Hills; Need(w.GetTerrainSpeed(c) < plain, "hills slow");
            w.Cells[c].Terrain = TerrainType.Highlands; Need(w.GetTerrainSpeed(c) < plain, "highlands slow");
            Water(w, 24, 20, .4f, .2f); Need(w.IsWalkable(24, 20) && w.GetTerrainSpeed(c) < .35f, "real slow ford");
            w.Cells[c].WaterDepth = .9f; Need(!w.IsWalkable(24, 20), "deep river blocks");
            w.Cells[c].WaterDepth = .3f; w.Cells[c].CurrentStrength = .9f; Need(!w.IsWalkable(24, 20), "rapid blocks even shallow");
        }
        public static void WeightedPathPrefersFasterDetour()
        {
            SimulationEngine e = Town(); WorldState w = e.World; UnitData u = w.Units[0]; u.X = 8.5f; u.Y = 10.5f;
            for (int x = 9; x <= 18; x++) w.Cells[w.Index(x, 10)].Terrain = TerrainType.Swamp;
            for (int x = 8; x <= 19; x++) { int c = w.Index(x, 8); w.Infrastructure[c].RoadLevel = 4; w.Infrastructure[c].RoadOwnerId = u.SettlementId; }
            Need((bool)Call(e, "SetTarget", u, 19, 10), "reachable weighted path");
            Need(u.Path.Exists(c => c / w.Width == 8), "uses slightly longer paved corridor");
            Need(!u.Path.Exists(c => c / w.Width == 10 && c % w.Width > 8 && c % w.Width < 19), "avoids direct swamp");
        }
        public static void DeepRiverBlocksRouteUntilPhysicalBridgeBuilt()
        {
            SimulationEngine e = Town(); WorldState w = e.World; SettlementData s = w.Settlements[0]; UnitData u = w.Units[0];
            for (int y = 0; y < 40; y++) { Water(w, 25, y); Water(w, 26, y); }
            Need(!(bool)Call(e, "SetTarget", u, 28, 20), "no crossing before bridge");
            float stock = s.Wood; Need(e.TryPlanBridge(s.Id, 24, 20, 0, false), "plan researched bridge");
            Need(s.Wood == stock && w.Infrastructure[w.Index(25, 20)].BridgeLevel == 0, "planning does not spend remote stock or build instantly");
            Need((bool)Call(e, "ChooseTransportWork", u, s), "resident accepts construction");
            bool carried = false;
            for (int i = 0; i < 1800 && w.TransportProjects.Count > 0; i++) { e.Step(.1f); carried |= u.CargoKind == ItemType.Plank && u.CargoAmount > 0; Need(SimulationEngine.CarriedLoad(u) <= SimulationEngine.CarryCapacityFor(u) + .0001f, "transport obeys capacity"); }
            Need(carried, "planks physically transported"); Need(w.Infrastructure[w.Index(25, 20)].BridgeLevel == 1 && w.Infrastructure[w.Index(26, 20)].BridgeLevel == 1, "whole span completed; projects=" + w.TransportProjects.Count + "; delivered=" + (w.TransportProjects.Count == 0 ? -1 : w.TransportProjects[0].Delivered) + "; work=" + (w.TransportProjects.Count == 0 ? -1 : w.TransportProjects[0].WorkSeconds) + "; task=" + u.Task + "; x=" + u.X + "; y=" + u.Y + "; cargo=" + u.CargoAmount + "; stock=" + s.Wood);
            Need((bool)Call(e, "SetTarget", u, 28, 20), "new bridge opens route"); Need(w.Validate(out string error), error);
        }
        public static void NoTechnologyNoFreeBridgeAndOceanRefused()
        {
            SimulationEngine e = Town(); WorldState w = e.World; SettlementData s = w.Settlements[0]; Water(w, 25, 20);
            s.UnlockedTechnologies.Remove("l11_wooden_bridges"); float stock = s.Wood;
            Need(!e.TryPlanBridge(s.Id, 24, 20, 0, false), "technology required"); Need(s.Wood == stock, "no charge");
            s.UnlockedTechnologies.Add("l11_wooden_bridges"); w.Cells[w.Index(25, 20)].WaterKind = WaterKind.Ocean;
            Need(!e.TryPlanBridge(s.Id, 24, 20, 0, false), "no ocean bridge shortcut");
        }
        public static void TrampledPathNeedsPassagesAndRoadRequiresDelivery()
        {
            SimulationEngine e = Town(); WorldState w = e.World; SettlementData s = w.Settlements[0]; UnitData u = w.Units[0]; int c = w.Index(23, 20); w.Cells[c].SettlementId = s.Id;
            for (int i = 0; i < 7; i++) Call(e, "RecordFootstep", u, c);
            Need(w.Infrastructure[c].RoadLevel == 0, "insufficient passages"); Call(e, "RecordFootstep", u, c); Need(w.Infrastructure[c].RoadLevel == 1, "trampled without technology or materials");
            float wood = s.Wood; Need(e.TryPlanRoad(s.Id, 23, 20, 2), "dirt project"); Need(s.Wood == wood && w.Infrastructure[c].RoadLevel == 1, "project not instant");
            Call(e, "ChooseTransportWork", u, s);
            for (int i = 0; i < 600 && w.Infrastructure[c].RoadLevel < 2; i++) e.Step(.1f);
            Need(w.Infrastructure[c].RoadLevel >= 2, "worker builds road");
            e.Paint(TerrainType.DeepWater, 23, 20, 0); Need(w.Infrastructure[c].RoadLevel == 0, "flood clears road immediately");
        }
        public static void CancelledProjectDropsDeliveredMaterials()
        {
            SimulationEngine e = Town(); WorldState w = e.World; SettlementData s = w.Settlements[0]; Water(w, 25, 20);
            Need(e.TryPlanBridge(s.Id, 24, 20, 0, false), "project"); TransportProjectData p = w.TransportProjects[0]; p.Delivered = 2; s.Wood -= 2;
            e.Paint(TerrainType.Grass, 25, 20, 0); e.Step(.1f);
            Need(w.TransportProjects.Count == 0, "invalid project canceled"); Need(w.GroundStocks.Exists(g => g.Item == ItemType.Plank && Math.Abs(g.Amount - 2) < .001f), "materials preserved at work site"); Need(w.Validate(out string error), error);
        }
        public static void TransportSaveContinuesIdenticallyAndRejectsCorruption()
        {
            SimulationEngine e = Town(); WorldState w = e.World; Water(w, 25, 20); Need(e.TryPlanBridge(w.Settlements[0].Id, 24, 20, 0, false), "project");
            e.Step(2); SimulationEngine restored = new SimulationEngine(Clone(w));
            for (int i = 0; i < 100; i++) { e.Step(.1f); restored.Step(.1f); }
            Need(Snapshot(w) == Snapshot(restored.World), "save deterministic");
            WorldState bad = Clone(w); bad.Infrastructure[0].FootTraffic = float.NaN; Need(!bad.Validate(out string error), "nan rejected");
        }
        public static void LegacyRoadGradesMigrateOnce()
        {
            SimulationEngine e = Town(); WorldState w = e.World; int cell = w.Index(23, 20); w.TransportVersion = 0; w.Infrastructure[cell].RoadLevel = 2; w.Infrastructure[cell].RoadOwnerId = w.Settlements[0].Id;
            uint rng = w.RngState; int id = w.NextId; new SimulationEngine(w); Need(w.Infrastructure[cell].RoadLevel == 4, "old paved upgraded schema");
            string once = Snapshot(w); new SimulationEngine(w); Need(once == Snapshot(w) && rng == w.RngState && id == w.NextId, "migration idempotent no rng");
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class TransportTests
    {
        [SetUp] public void Serializers() { TransportScenarios.Clone = w => JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)); TransportScenarios.Snapshot = w => JsonUtility.ToJson(w); }
        [Test] public void TerrainCosts() { TransportScenarios.TerrainAndWaterAffectTraversal(); }
        [Test] public void WeightedPath() { TransportScenarios.WeightedPathPrefersFasterDetour(); }
        [Test] public void BridgeConstruction() { TransportScenarios.DeepRiverBlocksRouteUntilPhysicalBridgeBuilt(); }
        [Test] public void BridgeRequirements() { TransportScenarios.NoTechnologyNoFreeBridgeAndOceanRefused(); }
        [Test] public void TrailsAndRoads() { TransportScenarios.TrampledPathNeedsPassagesAndRoadRequiresDelivery(); }
        [Test] public void Cancellation() { TransportScenarios.CancelledProjectDropsDeliveredMaterials(); }
        [Test] public void Persistence() { TransportScenarios.TransportSaveContinuesIdenticallyAndRejectsCorruption(); }
        [Test] public void Migration() { TransportScenarios.LegacyRoadGradesMigrateOnce(); }
    }
#endif
}
