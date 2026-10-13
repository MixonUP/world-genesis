using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using WorldGenesis.Simulation;
#if UNITY_5_3_OR_NEWER
using NUnit.Framework;
using UnityEngine;
#endif

namespace WorldGenesis.Tests
{
    public static class BuildingInteractionScenarios
    {
        public static Func<WorldState, WorldState> Clone;
        public static Func<WorldState, string> Snapshot;
        private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static void Near(float expected, float actual, string message) { Need(Math.Abs(expected - actual) < .001f, message + ": " + actual + " != " + expected); }
        private static object Call(SimulationEngine e, string method, params object[] args)
        { MethodInfo m = typeof(SimulationEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); Need(m != null, "Missing hook " + method); return m.Invoke(e, args); }
        private static SimulationEngine Fixture(int people = 8)
        {
            WorldState w = new WorldState { Seed = 720131, RngState = 720131, Width = 40, Height = 40, Cells = new TileData[1600] };
            for (int i = 0; i < w.Cells.Length; i++) w.Cells[i] = new TileData { Terrain = TerrainType.Grass, Height = .55f, RouteHeight = .55f, Flow = 1, DrainageParent = -1, Moisture = .65f, Temperature = .42f };
            SimulationEngine e = new SimulationEngine(w); e.Spawn(SpeciesType.Human, 8, 8, people);
            w.Laws.Aging = w.Laws.Births = w.Laws.Warfare = w.NaturalWildlife = false; w.Weather.Automatic = false;
            foreach (UnitData u in w.Units) { u.Age = 25; u.Hunger = u.Thirst = u.Fatigue = u.SocialNeed = 0; u.DecisionTimer = 500; u.Task = UnitTask.Idle; u.Path.Clear(); u.PathCursor = 0; }
            foreach (TechnologyDefinition t in TechnologyCatalog.All) w.Settlements[0].UnlockedTechnologies.Add(t.Id);
            w.Settlements[0].Wood = 0; return e;
        }
        private static BuildingData Add(SimulationEngine e, BuildingType type, int x = 22, int y = 20)
        { return (BuildingData)Call(e, "AddBuilding", e.World.Settlements[0], x, y, type); }
        private static void Valid(WorldState w) { Need(w.Validate(out string error), error); }
        private static void Step(SimulationEngine e, int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                e.Step(.1f); Valid(e.World);
                foreach (UnitData u in e.World.Units) Need(SimulationEngine.CarriedLoad(u) <= SimulationEngine.CarryCapacityFor(u) + .0001f, "Cargo exceeded capacity");
            }
        }
        private static void Until(SimulationEngine e, Func<bool> condition, string message, int limit = 1600)
        { for (int i = 0; i < limit && !condition(); i++) Step(e); Need(condition(), message); }
        private static int Cell(WorldState w, UnitData u) { return w.Index((int)u.X, (int)u.Y); }
        private static void AtDoor(WorldState w, UnitData u, BuildingData b)
        { int cell = BuildingAccess.EntranceCell(w, b); Need(cell >= 0, "No door"); u.X = cell % w.Width + .5f; u.Y = cell / w.Width + .5f; u.Path.Clear(); u.PathCursor = 0; u.DecisionTimer = 500; }
        private static void Enter(SimulationEngine e, UnitData u, BuildingData b)
        { Need(e.TryEnterBuilding(u.Id, b.Id), "Entry command rejected"); Until(e, () => u.InsideBuildingId == b.Id, "Never entered"); }
        private static float Material(WorldState w, ItemType item)
        {
            float amount = w.LostResources[(int)item];
            foreach (BuildingData b in w.Buildings) amount += b.Inventory[(int)item] + b.TaxStocks[(int)item] + b.PersonalStocks.Where(p => p.Item == item).Sum(p => p.Amount);
            foreach (UnitData u in w.Units) { if (u.CargoKind == item) amount += u.CargoAmount; if (item == ItemType.Plank) amount += u.CarriedWood; if (u.WorkTool == item) amount++; if (u.Clothes == item) amount++; }
            if (item == ItemType.Plank) amount += w.Settlements.Sum(s => s.Wood);
            return amount + w.GroundStocks.Where(g => !g.IsFood && g.Item == item).Sum(g => g.Amount);
        }
        private static float Agriculture(WorldState w, AgriculturalProduct product)
        { return w.LostAgriculture[(int)product] + w.Buildings.Sum(b => b.AgriculturalStock[(int)product]) + w.Units.Where(u => u.HarvestProduct == product).Sum(u => u.HarvestAmount) + w.AgriculturalDrops.Where(p => p.Product == product).Sum(p => p.Amount); }
        private static void MixedCargo(UnitData u) { u.CargoKind = ItemType.IronOre; u.CargoAmount = .6f; u.HarvestProduct = AgriculturalProduct.Fiber; u.HarvestAmount = .4f; }

        public static void OffCenterResidentCompletesDoorApproachWithoutTeleportOrDeadlock()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Add(e, BuildingType.House); UnitData u = w.Units[0];
            int door = b.EntranceCell; u.X = door % w.Width + .0782f; u.Y = door / w.Width + .5f;
            float before = u.X; Need(e.TryEnterBuilding(u.Id, b.Id), "Same-cell entry command rejected");
            Near(before, u.X, "Command teleported resident to doorway"); Need(u.InsideBuildingId == 0, "Entered before approaching");
            Step(e); Need(u.X > before && u.InsideBuildingId == 0, "No physical final approach");
            Until(e, () => u.InsideBuildingId == b.Id, "Off-centre doorway never entered", 30);
            Valid(w);
        }

        public static void TransitRouteGoesAroundAClosedBuildingFootprint()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Add(e, BuildingType.House); UnitData u = w.Units[0];
            u.X = b.X - 3 + .5f; u.Y = b.Y + .5f; Need(!w.IsWalkable(b.X, b.Y), "Building footprint is traversable");
            Need((bool)Call(e, "SetTarget", u, b.X + 3, b.Y), "No detour around one house");
            Need(u.Path.Count > 6 && !u.Path.Contains(w.Index(b.X, b.Y)), "Path cuts through walls");
            Until(e, () => u.PathCursor == u.Path.Count, "Detour did not finish");
            Need(Cell(w, u) == w.Index(b.X + 3, b.Y) && u.InsideBuildingId == 0, "Transit entered building");
        }
        public static void DistantEntryWalksToTheSavedDoorBeforeGoingInside()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Add(e, BuildingType.House); UnitData u = w.Units[0]; u.X = 15.5f; u.Y = 20.5f;
            float x = u.X, y = u.Y, health = u.Health; int door = BuildingAccess.EntranceCell(w, b); Need(e.TryEnterBuilding(u.Id, b.Id), "Distant entry rejected");
            Need(u.InsideBuildingId == 0 && u.X == x && u.Y == y, "Entry teleported remotely"); bool sawDoor = false, sawTimer = false;
            for (int i = 0; i < 400 && u.InsideBuildingId == 0; i++) { int previous = Cell(w, u); sawDoor |= previous == door; sawTimer |= u.BuildingInteractionTimer > 0; Step(e); if (u.InsideBuildingId != 0) Need(previous == door, "Entered through a wall"); }
            Need(u.InsideBuildingId == b.Id && sawDoor && sawTimer, "Door approach/entry timer absent"); Near(health, u.Health, "Indoor unit treated as drowning"); Valid(w);
        }
        public static void ANewOutdoorDestinationRequiresAnActualExit()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Add(e, BuildingType.House); UnitData u = w.Units[0]; AtDoor(w, u, b); Enter(e, u, b);
            int door = BuildingAccess.EntranceCell(w, b); Need((bool)Call(e, "SetTarget", u, b.X + 4, b.Y + 3), "Outdoor route rejected");
            bool crossedDoor = u.InsideBuildingId == 0 && Cell(w, u) == door;
            for (int i = 0; i < 500 && Cell(w, u) != w.Index(b.X + 4, b.Y + 3); i++) { Step(e); crossedDoor |= u.InsideBuildingId == 0 && Cell(w, u) == door; }
            Need(crossedDoor && u.InsideBuildingId == 0, "No physical doorway exit"); Need(Cell(w, u) == w.Index(b.X + 4, b.Y + 3), "Outdoor destination not reached");
        }
        public static void ProductionProgressAndOutputRequireTheWorkerInside()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Add(e, BuildingType.Smelter); UnitData u = w.Units[0]; u.X = 17.5f; u.Y = 20.5f;
            b.Inventory[(int)ItemType.IronOre] = 2; b.Inventory[(int)ItemType.Charcoal] = 1; Need(e.TryStartProduction(u.Id, b.Id, IndustryRecipeType.IronIngot), "Recipe command"); bool workedInside = false;
            for (int i = 0; i < 1000 && b.CompletedIndustryBatches == 0; i++) { float before = b.IndustryProgress; Step(e); if (b.IndustryProgress > before) { Need(u.InsideBuildingId == b.Id, "Worked outside closed workshop"); workedInside = true; } }
            Need(workedInside && b.CompletedIndustryBatches == 1, "Indoor recipe did not finish"); Near(1, Material(w, ItemType.IronIngot), "Output"); Near(0, Material(w, ItemType.IronOre), "Ore debit"); Near(0, Material(w, ItemType.Charcoal), "Fuel debit");
        }
        public static void EveryIndustrialRecipeStillCompletesAfterDoorEntry()
        {
            foreach(IndustryRecipe recipe in IndustryCatalog.All)
            {
                SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,recipe.Building);UnitData u=w.Units[0];AtDoor(w,u,b);
                foreach(ItemAmount input in recipe.Inputs)b.Inventory[(int)input.Item]=input.Amount;
                foreach(AgriculturalAmount input in recipe.Agriculture)b.AgriculturalStock[(int)input.Product]=input.Amount;
                Need(e.TryStartProduction(u.Id,b.Id,recipe.Id),"Indoor recipe refused "+recipe.Id);
                Until(e,()=>b.CompletedIndustryBatches==1,"Indoor recipe stalled "+recipe.Id,1000);
                Near(recipe.OutputAmount,Material(w,recipe.Output),"Indoor output "+recipe.Id);
                foreach(ItemAmount input in recipe.Inputs)Near(0,Material(w,input.Item),"Indoor material debit "+recipe.Id);
                foreach(AgriculturalAmount input in recipe.Agriculture)Near(0,Agriculture(w,input.Product),"Indoor agricultural debit "+recipe.Id);
            }
        }
        public static void MixedSuppliesEnterBothWarehouseAndWorkshop()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData source = Add(e, BuildingType.Warehouse, 15, 20), destination = Add(e, BuildingType.Tannery, 25, 20); UnitData u = w.Units[0]; AtDoor(w, u, destination);
            source.Inventory[(int)ItemType.Log] = 1; source.AgriculturalStock[(int)AgriculturalProduct.Hide] = 2; Need(e.TryStartProduction(u.Id, destination.Id, IndustryRecipeType.Leather), "Mixed recipe start"); bool sourceEntry = false, destinationEntry = false, material = false, agricultural = false;
            for (int i = 0; i < 1800 && destination.CompletedIndustryBatches == 0; i++)
            {
                float stocked=source.Inventory[(int)ItemType.Log]+source.AgriculturalStock[(int)AgriculturalProduct.Hide];
                bool atSource=u.InsideBuildingId==source.Id || (u.EnteringBuildingId==source.Id && Cell(w,u)==BuildingAccess.EntranceCell(w,source) && u.BuildingInteractionTimer+.10001f>=BuildingAccess.EntrySeconds);
                Step(e);
                if(source.Inventory[(int)ItemType.Log]+source.AgriculturalStock[(int)AgriculturalProduct.Hide]<stocked)
                { Need(atSource,"Supply debited before physical indoor entry");sourceEntry=true; }
                sourceEntry |= u.InsideBuildingId == source.Id; destinationEntry |= u.InsideBuildingId == destination.Id; material |= u.CargoAmount > 0; agricultural |= u.HarvestAmount > 0;
            }
            Need(sourceEntry && destinationEntry && material && agricultural, "Physical storage visits or mixed cargo missing: source="+sourceEntry+", destination="+destinationEntry+", material="+material+", agriculture="+agricultural+", batches="+destination.CompletedIndustryBatches+", task="+u.Task+", inside="+u.InsideBuildingId+", entering="+u.EnteringBuildingId); Need(destination.CompletedIndustryBatches == 1, "Mixed recipe stuck on door"); Near(2, Material(w, ItemType.Leather), "Leather output"); Near(0, Agriculture(w, AgriculturalProduct.Hide), "Hide consumption");
        }
        public static void ManufacturedEquipmentIsIssuedOnlyInsideTheStore()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Add(e, BuildingType.Smithy); UnitData u = w.Units[0]; u.X = 17.5f; u.Y = 20.5f; b.Inventory[(int)ItemType.IronTools] = 1;
            Need(e.TryEquipManufacturedGoods(u.Id, b.Id, ItemType.IronTools), "Equipment command"); Near(1, b.Inventory[(int)ItemType.IronTools], "Remote issue"); bool sawInside = false;
            for (int i = 0; i < 600 && u.WorkTool == ItemType.None; i++) { Step(e); sawInside |= u.InsideBuildingId == b.Id; }
            Need(sawInside && u.WorkTool == ItemType.IronTools, "Equipment issued without indoor visit"); Near(0, b.Inventory[(int)ItemType.IronTools], "Stock not debited"); Near(1, Material(w, ItemType.IronTools), "Equipment duplicated");
        }
        public static void TiredResidentChoosesAHouseAndRestsInside()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Add(e, BuildingType.House, 16, 16); UnitData u = w.Units[0]; AtDoor(w, u, b); u.Fatigue = .8f; u.HomeBuildingId = b.Id;
            Need((bool)Call(e, "ChooseRest", u, w.Settlements[0]), "Rest not chosen"); bool restedInside = false;
            for (int i = 0; i < 400 && u.Fatigue > .35f; i++) { float before = u.Fatigue; Step(e); if (u.Fatigue < before && u.InsideBuildingId == b.Id) restedInside = true; }
            Need(restedInside && u.Fatigue < .35f, "House rest remained outside or failed");
        }
        public static void InteriorCapacityAndAuthorityCannotBeBypassed()
        {
            SimulationEngine e = Fixture(16); WorldState w = e.World; BuildingData b = Add(e, BuildingType.House); int capacity = BuildingAccess.Capacity(b); Need(capacity > 0 && capacity < w.Units.Count, "Fixture lacks spare resident");
            for (int i = 0; i < capacity; i++) { UnitData u = w.Units[i]; AtDoor(w, u, b); Enter(e, u, b); u.DecisionTimer = 500; }
            UnitData extra = w.Units[capacity]; AtDoor(w, extra, b); e.TryEnterBuilding(extra.Id, b.Id); Step(e, 15);
            Need(BuildingAccess.Occupants(w, b) == capacity && extra.InsideBuildingId == 0, "Overcrowded interior");
            int home = extra.SettlementId; extra.SettlementId = 0; Need(!e.TryEnterBuilding(extra.Id, b.Id), "Unaffiliated entry"); extra.SettlementId = home; extra.Health = 0; Need(!e.TryEnterBuilding(extra.Id, b.Id), "Dead entry"); Call(e, "RemoveDead"); Valid(w);
        }
        public static void FireEvacuatesTheLivingCarrierWithoutDuplicatingCargo()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Add(e, BuildingType.House); UnitData u = w.Units[0]; AtDoor(w, u, b); MixedCargo(u); Enter(e, u, b);
            TileData tile = w.Cells[w.Index(b.X, b.Y)]; tile.Fire = .8f; w.Cells[w.Index(b.X, b.Y)] = tile;
            Until(e, () => u.InsideBuildingId == 0, "Fire did not evacuate", 60); Need(u.Health > 0 && w.IsWalkable((int)u.X, (int)u.Y), "Evacuation stranded or killed resident"); Near(.6f, Material(w, ItemType.IronOre), "Fire cargo loss"); Near(.4f, Agriculture(w, AgriculturalProduct.Fiber), "Fire agriculture loss");
        }
        public static void AdjacentFireCannotCreateAnExteriorRouteForAnIndoorResident()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,BuildingType.House);UnitData u=w.Units[0];AtDoor(w,u,b);MixedCargo(u);Enter(e,u,b);
            int door=BuildingAccess.EntranceCell(w,b);TileData hazard=w.Cells[door];hazard.Fire=.8f;w.Cells[door]=hazard;
            Near(0,w.Cells[w.Index(b.X,b.Y)].Fire,"Fixture must burn beside the building only");Step(e);
            Need(u.InsideBuildingId==0,"Adjacent fire did not use real doorway/emergency exit");Need(w.IsWalkable((int)u.X,(int)u.Y),"Fire escape crossed a wall");
            Near(.6f,Material(w,ItemType.IronOre),"Adjacent fire cargo");Near(.4f,Agriculture(w,AgriculturalProduct.Fiber),"Adjacent fire agriculture");
        }
        public static void PlannedDemolitionEvacuatesAndPreservesIndependentStocks()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Add(e, BuildingType.Smelter); UnitData u = w.Units[0]; AtDoor(w, u, b); MixedCargo(u); Enter(e, u, b); b.Inventory[(int)ItemType.Salt] = 3;
            Need(e.TryDemolishBuilding(w.Settlements[0].Id, b.Id, "Independent entry regression"), "Demolition rejected");
            Need(w.GetBuilding(b.Id) == null && u.InsideBuildingId == 0 && u.Health > 0, "Occupant remained in deleted building"); Near(.6f, Material(w, ItemType.IronOre), "Demolition cargo"); Near(.4f, Agriculture(w, AgriculturalProduct.Fiber), "Demolition agricultural cargo"); Near(3, Material(w, ItemType.Salt), "Separate warehouse stock"); Valid(w);
            Call(e, "DestroyInvalidBuildings"); Near(3, Material(w, ItemType.Salt), "Second cleanup duplicated stock");
        }
        public static void DeathInsideReleasesCapacityAndDropsRealEquipmentOnce()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Add(e, BuildingType.House); UnitData u = w.Units[0]; AtDoor(w, u, b); MixedCargo(u); u.WorkTool = ItemType.IronTools; Enter(e, u, b); int id = u.Id;
            u.Health = 0; Call(e, "RemoveDead"); Need(w.GetUnit(id) == null && BuildingAccess.Occupants(w, b) == 0, "Dead occupant keeps capacity");
            Near(.6f, Material(w, ItemType.IronOre), "Death cargo"); Near(.4f, Agriculture(w, AgriculturalProduct.Fiber), "Death harvest"); Near(1, Material(w, ItemType.IronTools), "Death tool"); Call(e, "RemoveDead"); Near(1, Material(w, ItemType.IronTools), "Repeated death drop"); Valid(w);
        }
        public static void FloodedDoorUsesOnlyAdjacentEmergencyEscape()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Add(e, BuildingType.House); UnitData u = w.Units[0]; AtDoor(w, u, b); MixedCargo(u); Enter(e, u, b); int door = BuildingAccess.EntranceCell(w, b);
            e.Paint(TerrainType.DeepWater, door % w.Width, door / w.Width, 0); Need(!w.IsWalkable(door % w.Width, door / w.Width), "Door did not flood");
            e.TryExitBuilding(u.Id); Until(e, () => u.InsideBuildingId == 0, "Blocked door has no nearby emergency escape", 60);
            int distance = Math.Abs((int)u.X - b.X) + Math.Abs((int)u.Y - b.Y); Need(distance == 1 && w.IsWalkable((int)u.X, (int)u.Y), "Emergency escape teleported or used flooded tile"); Near(.6f, Material(w, ItemType.IronOre), "Emergency cargo"); Near(.4f, Agriculture(w, AgriculturalProduct.Fiber), "Emergency harvest");
        }
        public static void FullyBlockedExitNeverTeleportsAndDemolitionReleasesIt()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Add(e, BuildingType.House); UnitData u = w.Units[0]; AtDoor(w, u, b); MixedCargo(u); Enter(e, u, b);
            int[] dx = {-1,0,1,0}, dy = {0,-1,0,1}; for(int i=0;i<4;i++) e.Paint(TerrainType.DeepWater,b.X+dx[i],b.Y+dy[i],0);
            TileData hazard=w.Cells[w.Index(b.X,b.Y)];hazard.Fire=.8f;w.Cells[w.Index(b.X,b.Y)]=hazard;float health=u.Health;
            e.TryExitBuilding(u.Id); Step(e,5); Need(u.InsideBuildingId == b.Id, "Escaped four blocked neighbours by teleport");Need(u.Health<health,"Trapped occupant immune to real fire");Near(.6f, Material(w,ItemType.IronOre),"Trapped cargo");
            b.Health=0;Call(e,"DestroyInvalidBuildings");Need(u.InsideBuildingId==0&&Cell(w,u)==w.Index(b.X,b.Y),"Deleted footprint did not free trapped resident");Near(.6f,Material(w,ItemType.IronOre),"Trapped demolition cargo");Near(.4f,Agriculture(w,AgriculturalProduct.Fiber),"Trapped demolition harvest");Valid(w);
        }
        public static void EntryAndIndoorSaveContinueWithIdenticalState()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,BuildingType.House);UnitData u=w.Units[0];u.X=16.5f;u.Y=20.5f;MixedCargo(u);Need(e.TryEnterBuilding(u.Id,b.Id),"Start entry");Step(e,3);
            SimulationEngine copy=new SimulationEngine(Clone(w));Need(Snapshot(w)==Snapshot(copy.World),"Current entry save changed during load");
            for(int i=0;i<150&&u.InsideBuildingId==0;i++){Step(e);Step(copy);}Need(Snapshot(w)==Snapshot(copy.World),"Entry continuation diverged");Need(u.InsideBuildingId==b.Id,"Entry never reached interior");
            copy=new SimulationEngine(Clone(w));Need(e.TryExitBuilding(u.Id)&&copy.TryExitBuilding(u.Id),"Saved occupant cannot exit");for(int i=0;i<30;i++){Step(e);Step(copy);}Need(Snapshot(w)==Snapshot(copy.World),"Indoor save exit diverged");Near(.6f,Material(w,ItemType.IronOre),"Saved cargo");
        }
        public static void LegacyWallCoordinatesMigrateWithoutRngIdsOrCargoLoss()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,BuildingType.House);UnitData u=w.Units[0];w.BuildingAccessVersion=0;
            foreach(BuildingData building in w.Buildings)building.EntranceCell=-1;
            foreach(UnitData resident in w.Units){resident.InsideBuildingId=resident.EnteringBuildingId=0;resident.BuildingInteractionTimer=0;}
            u.X=b.X+.5f;u.Y=b.Y+.5f;MixedCargo(u);uint rng=w.RngState;int id=w.NextId;Valid(w);
            WorldState loaded=new SimulationEngine(Clone(w)).World;Need(loaded.BuildingAccessVersion==1&&loaded.RngState==rng&&loaded.NextId==id,"Legacy migration changed RNG/IDs");Near(.6f,Material(loaded,ItemType.IronOre),"Migrated cargo");Near(.4f,Agriculture(loaded,AgriculturalProduct.Fiber),"Migrated harvest");
            UnitData residentLoaded=loaded.GetUnit(u.Id);Need(residentLoaded.InsideBuildingId==b.Id||loaded.IsWalkable((int)residentLoaded.X,(int)residentLoaded.Y),"Legacy resident left on impassable wall");Valid(loaded);
        }
        public static void CorruptOccupancyDoorAndTimerAreRejectedBeforeLoad()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,BuildingType.House);UnitData u=w.Units[0];AtDoor(w,u,b);Enter(e,u,b);
            WorldState bad=Clone(w);bad.GetUnit(u.Id).InsideBuildingId=999999;Need(!bad.Validate(out _),"Missing interior accepted");
            bad=Clone(w);bad.GetUnit(u.Id).BuildingInteractionTimer=float.NaN;Need(!bad.Validate(out _),"Nonfinite interaction accepted");
            bad=Clone(w);bad.GetBuilding(b.Id).EntranceCell=0;Need(!bad.Validate(out _),"Remote doorway accepted");
            bad=Clone(w);bad.BuildingAccessVersion=2;Need(!bad.Validate(out _),"Future access version accepted");
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class BuildingInteractionTests
    {
        [SetUp] public void Serializer(){BuildingInteractionScenarios.Clone=w=>JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w));BuildingInteractionScenarios.Snapshot=w=>JsonUtility.ToJson(w);}
        [Test] public void OffCenterDoorApproach(){BuildingInteractionScenarios.OffCenterResidentCompletesDoorApproachWithoutTeleportOrDeadlock();}
        [Test] public void TransitWalls(){BuildingInteractionScenarios.TransitRouteGoesAroundAClosedBuildingFootprint();}
        [Test] public void RealDoorApproach(){BuildingInteractionScenarios.DistantEntryWalksToTheSavedDoorBeforeGoingInside();}
        [Test] public void RealExit(){BuildingInteractionScenarios.ANewOutdoorDestinationRequiresAnActualExit();}
        [Test] public void IndoorProduction(){BuildingInteractionScenarios.ProductionProgressAndOutputRequireTheWorkerInside();}
        [Test] public void AllIndoorRecipes(){BuildingInteractionScenarios.EveryIndustrialRecipeStillCompletesAfterDoorEntry();}
        [Test] public void IndoorMixedSupplies(){BuildingInteractionScenarios.MixedSuppliesEnterBothWarehouseAndWorkshop();}
        [Test] public void IndoorEquipment(){BuildingInteractionScenarios.ManufacturedEquipmentIsIssuedOnlyInsideTheStore();}
        [Test] public void IndoorRest(){BuildingInteractionScenarios.TiredResidentChoosesAHouseAndRestsInside();}
        [Test] public void CapacityAndAuthority(){BuildingInteractionScenarios.InteriorCapacityAndAuthorityCannotBeBypassed();}
        [Test] public void FireEvacuation(){BuildingInteractionScenarios.FireEvacuatesTheLivingCarrierWithoutDuplicatingCargo();}
        [Test] public void AdjacentFireEvacuation(){BuildingInteractionScenarios.AdjacentFireCannotCreateAnExteriorRouteForAnIndoorResident();}
        [Test] public void DemolitionEvacuation(){BuildingInteractionScenarios.PlannedDemolitionEvacuatesAndPreservesIndependentStocks();}
        [Test] public void IndoorDeath(){BuildingInteractionScenarios.DeathInsideReleasesCapacityAndDropsRealEquipmentOnce();}
        [Test] public void FloodedEntrance(){BuildingInteractionScenarios.FloodedDoorUsesOnlyAdjacentEmergencyEscape();}
        [Test] public void FullyBlockedExit(){BuildingInteractionScenarios.FullyBlockedExitNeverTeleportsAndDemolitionReleasesIt();}
        [Test] public void SaveContinuation(){BuildingInteractionScenarios.EntryAndIndoorSaveContinueWithIdenticalState();}
        [Test] public void LegacyMigration(){BuildingInteractionScenarios.LegacyWallCoordinatesMigrateWithoutRngIdsOrCargoLoss();}
        [Test] public void InvalidSavedOccupancy(){BuildingInteractionScenarios.CorruptOccupancyDoorAndTimerAreRejectedBeforeLoad();}
    }
#endif
}
