using System;
using System.Linq;
using WorldGenesis.Simulation;
#if UNITY_5_3_OR_NEWER
using NUnit.Framework;
using UnityEngine;
#endif
namespace WorldGenesis.Tests
{
    public static class WildlifeScenarios
    {
        public static Func<WorldState, WorldState> Clone;
        public static Func<WorldState, string> Snapshot;
        private static void Require(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
        private static void Valid(WorldState w) { Require(w.Validate(out string error), error); }
        private static SimulationEngine Make(bool forest = false)
        {
            var w = WorldGenerator.Generate(944, 48, 48);
            for (int i = 0; i < w.Cells.Length; i++) { w.Cells[i].Terrain = forest ? TerrainType.Forest : TerrainType.Grass; w.Cells[i].Height = .55f; w.Cells[i].Vegetation = .7f; w.Cells[i].River = false; Hydrology.RefreshCell(w, i); }
            ResourceCatalog.Generate(w); w.Laws.Aging = false; return new SimulationEngine(w);
        }
        private static void Steps(SimulationEngine e, int n) { for (int i = 0; i < n; i++) e.Step(.1f); }
        public static void SpeciesIdsStatsAndExplicitPacksStayValid()
        {
            var e = Make(); Require((int)SpeciesType.Sheep == 4 && (int)SpeciesType.Wolf == 5 && (int)SpeciesType.Horse == 16, "Legacy species IDs changed");
            for (int s = 4; s <= 16; s++) e.Spawn((SpeciesType)s, 20, 20, 2);
            foreach (UnitData animal in e.World.Units) Require(animal.MaxHealth == WildlifeCatalog.MaxHealth(animal.Species) && animal.PackId > 0 && e.World.GetUnit(animal.PackLeaderId).Species == animal.Species, "Incorrect actual animal stats or pack"); Valid(e.World);
        }
        public static void WildlifeMigrationAndCorruptPackValidation()
        {
            var e = Make(); e.Spawn(SpeciesType.Sheep, 20, 20, 2); WorldState w = Clone(e.World); w.WildlifeVersion = 0; uint random = w.RngState; int next = w.NextId;
            new SimulationEngine(w); Require(w.RngState == random && w.NextId == next && w.Units.Count == 2, "Migration spawned wildlife or consumed random"); string before = Snapshot(w); new SimulationEngine(w); Require(Snapshot(w) == before, "Wildlife migration repeated"); Valid(w);
            w.Units[0].PackLeaderId = 999; Require(!w.Validate(out _), "Missing pack leader accepted");
        }
        public static void NaturalForestSpawnIsBoundedAndSaveDeterministic()
        {
            var a = Make(true); Steps(a, 650); Require(a.World.Units.Count > 0 && a.World.Units.All(u => WildlifeCatalog.IsAnimal(u.Species)), "No natural forest animals"); var b = new SimulationEngine(Clone(a.World));
            for (int i = 0; i < 1000; i++) { a.Step(.1f); b.Step(.1f); Require(a.World.Units.Count <= 36, "Natural population exceeded map cap"); if (i % 100 == 0) Valid(a.World); }
            Require(Snapshot(a.World) == Snapshot(b.World), "Ecology continued differently after save"); Valid(b.World);
            var grass = Make(); Steps(grass, 400); Require(grass.World.Units.Count == 0, "Forest spawner populated bare grass");
        }
        public static void BirthNeedsActualHealthyPairAndBirthLaw()
        {
            var e = Make(); e.Spawn(SpeciesType.Goat, 20, 20, 2); var female = e.World.Units[0]; var male = e.World.Units[1]; female.Sex = Sex.Female; male.Sex = Sex.Male;
            foreach (UnitData u in e.World.Units) { u.X = u.Y = 20.5f; u.Age = 3; u.Hunger = .1f; u.Task = UnitTask.Idle; u.DecisionTimer = 10000; } female.BreedingSeconds = 89;
            e.World.Laws.Births = false; Steps(e, 10); Require(e.World.Units.Count == 2, "Birth law ignored"); e.World.Laws.Births = true; male.X = 40.5f; Steps(e, 10); Require(e.World.Units.Count == 2, "Distant mate enabled pregnancy"); male.X = 20.5f; Steps(e, 10);
            Require(e.World.Units.Count == 3 && e.World.Units[2].Age == 0 && e.World.Units[2].PackId == female.PackId, "Actual pair did not produce pack offspring"); Valid(e.World);
        }
        public static void PredatorKillFeedsAndLeavesPhysicalLoot()
        {
            var e = Make(); e.Spawn(SpeciesType.Wolf, 20, 20, 1); e.Spawn(SpeciesType.Chicken, 21, 20, 1); UnitData wolf = e.World.Units[0], prey = e.World.Units[1]; wolf.X = prey.X = 20.5f; wolf.Y = prey.Y = 20.5f; wolf.Hunger = .7f; prey.Health = 1;
            e.Step(.1f); Require(e.World.GetUnit(prey.Id) == null && wolf.Hunger == 0, "Predator did not kill and feed");
            Require(e.World.AgriculturalDrops.Any(d => d.Product == AgriculturalProduct.Meat && d.Amount > 0 && d.X == prey.X && d.Y == prey.Y), "Remaining carcass was not on the actual death site"); Valid(e.World);
        }
        public static void HunterRequiresCraftedToolAndNeverHuntsAsChild()
        {
            var e = Make(); e.World.Laws.Births = false; e.Spawn(SpeciesType.Human, 14, 14, 1); e.Spawn(SpeciesType.Chicken, 16, 14, 1); UnitData hunter = e.World.Units[0], prey = e.World.Units[1]; SettlementData s = e.World.Settlements[0];
            hunter.Age = 25; hunter.Profession = UnitProfession.Hunter; hunter.X = 14.5f; hunter.Y = 14.5f; hunter.Hunger = hunter.Fatigue = hunter.SocialNeed = hunter.Thirst = .1f; hunter.DecisionTimer = 0; s.Food = 1; prey.X = 17.2f; prey.Y = 14.5f; prey.DecisionTimer = 10000;
            e.Step(.1f); Require(hunter.Task != UnitTask.Hunt && prey.Health == prey.MaxHealth, "Unarmed citizen became hunter");
            hunter.HuntingToolLevel = 1; hunter.Task = UnitTask.Idle; hunter.Path.Clear(); hunter.PathCursor = 0; hunter.DecisionTimer = 0; prey.Health = 1; e.Step(.1f); Require(hunter.Task == UnitTask.Hunt, "Equipped low-risk hunter did not select prey"); e.Step(.1f); Require(e.World.GetUnit(prey.Id) == null, "In-range hunting tool caused no real kill");
            e.Spawn(SpeciesType.Chicken, 17, 14, 1); hunter.Age = 12; hunter.Task = UnitTask.Hunt; hunter.TargetId = e.World.Units.Last().Id; hunter.DecisionTimer = 0; e.Step(.1f); Require(hunter.Task != UnitTask.Hunt && hunter.Task != UnitTask.Fight, "Child retained hunting job"); Valid(e.World);
        }
        public static void RareDeathDropsOnceAndPackElectsLivingLeader()
        {
            var e = Make(); e.Spawn(SpeciesType.DireWolf, 20, 20, 2); UnitData first = e.World.Units[0], second = e.World.Units[1]; first.Health = 0; second.X = 35.5f; second.Y = 35.5f; e.Step(.1f);
            Require(second.PackLeaderId == second.Id && e.World.AgriculturalDrops.Where(d => d.Product == AgriculturalProduct.RarePelt).Sum(d => d.Amount) == 1, "Rare trophy or surviving leader is wrong"); Steps(e, 10);
            Require(e.World.AgriculturalDrops.Where(d => d.Product == AgriculturalProduct.RarePelt).Sum(d => d.Amount) == 1, "Dead animal awarded trophy twice"); Valid(e.World);
        }
        public static void MountSpeedRequiresNearbyAnimalAndFollowingNeverTeleports()
        {
            var e = Make(); e.World.Laws.Births = false; e.Spawn(SpeciesType.Human, 14, 14, 1); e.Spawn(SpeciesType.Horse, 14, 15, 1); WorldState w = e.World; UnitData rider = w.Units[0], horse = w.Units[1];
            var pasture = new BuildingData { Id = w.NextId++, Type = BuildingType.Pasture, SettlementId = rider.SettlementId, X = 22, Y = 22 };
            w.Buildings.Add(pasture); w.Cells[w.Index(22,22)].BuildingId = pasture.Id; w.Cells[w.Index(22,22)].SettlementId = pasture.SettlementId; w.ResourceNodes[w.Index(22,22)] = new ResourceNodeData();
            rider.Age = 25; rider.X = 14.5f; rider.Y = 14.5f; rider.Fatigue = rider.Hunger = rider.SocialNeed = rider.Thirst = 0; rider.Task = UnitTask.Wander; rider.DecisionTimer = 10000; rider.Path.Clear(); rider.Path.Add(w.Index(15,14)); rider.PathCursor = 0; rider.PathRevision = w.NavigationRevision;
            horse.X = 14.5f; horse.Y = 15.5f; horse.IsDomestic = true; horse.AnimalOwnerId = rider.SettlementId; horse.PastureId = pasture.Id; horse.HandlerId = rider.Id; horse.Hunger = .05f; horse.Fatigue = 0; horse.DecisionTimer = 10000;
            rider.MountId = horse.Id; rider.MountSpecies = horse.Species; WorldState foot = Clone(w); foot.Units[0].MountId = 0; foot.Units[1].HandlerId = 0; var walking = new SimulationEngine(foot);
            e.Step(.1f); walking.Step(.1f); Require(rider.X - 14.5f > (foot.Units[0].X - 14.5f) * 1.3f, "Nearby real mount gave no movement advantage");
            rider.Path.Clear(); rider.PathCursor = 0; rider.X = 18.5f; rider.Task = UnitTask.Idle; horse.X = 14.5f; horse.Y = 14.5f; horse.DecisionTimer = 0; float original = horse.X; e.Step(.1f);
            Require(horse.X > original && horse.X - original < .5f && rider.X - horse.X > 3, "Mount teleported or failed to follow a route"); Steps(e, 9); Require(horse.X > original + 1 && rider.MountId == horse.Id, "Following or reciprocal mount association broke");
            e.Spawn(SpeciesType.Wolf,18,14,1); UnitData foe = w.Units.Last(); foe.X = rider.X + .5f; foe.Y = rider.Y; foe.AttackTimer = 10; rider.AttackTimer = 0; rider.HuntingToolLevel = 1; horse.X = rider.X; horse.Y = rider.Y; horse.DecisionTimer = 10000; horse.Path.Clear(); horse.PathCursor = 0;
            float before = foe.Health; e.Step(.1f); Require(Math.Abs(before - foe.Health - 8.4f) < .001f, "Real equipped nearby horse did not improve civilian defense");
            horse.X = rider.X - 3; rider.AttackTimer = 0; before = foe.Health; e.Step(.1f); Require(Math.Abs(before - foe.Health - 7) < .001f, "Distant animal still provided mounted combat bonus"); Valid(w);
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class WildlifeSimulationTests
    {
        [SetUp] public void SetSerializer() { WildlifeScenarios.Clone = w => JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)); WildlifeScenarios.Snapshot = w => JsonUtility.ToJson(w); }
        [Test] public void SpeciesAndPacks() { WildlifeScenarios.SpeciesIdsStatsAndExplicitPacksStayValid(); }
        [Test] public void MigrationAndValidation() { WildlifeScenarios.WildlifeMigrationAndCorruptPackValidation(); }
        [Test] public void ForestEcologySaveContinuation() { WildlifeScenarios.NaturalForestSpawnIsBoundedAndSaveDeterministic(); }
        [Test] public void HealthyPairReproduction() { WildlifeScenarios.BirthNeedsActualHealthyPairAndBirthLaw(); }
        [Test] public void ActualPredationAndLoot() { WildlifeScenarios.PredatorKillFeedsAndLeavesPhysicalLoot(); }
        [Test] public void EquipmentAndAgeGateHunting() { WildlifeScenarios.HunterRequiresCraftedToolAndNeverHuntsAsChild(); }
        [Test] public void RareTrophyAndPackSuccession() { WildlifeScenarios.RareDeathDropsOnceAndPackElectsLivingLeader(); }
        [Test] public void RealMountedMovement() { WildlifeScenarios.MountSpeedRequiresNearbyAnimalAndFollowingNeverTeleports(); }
    }
#endif
}
