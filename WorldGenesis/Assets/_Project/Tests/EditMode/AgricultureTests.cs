using System;
using System.Reflection;
using WorldGenesis.Simulation;
#if UNITY_5_3_OR_NEWER
using NUnit.Framework;
using UnityEngine;
#endif

namespace WorldGenesis.Tests
{
    public static class AgricultureScenarios
    {
        public static Func<WorldState, WorldState> Clone;
        public static Func<WorldState, string> Snapshot;
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Near(float expected, float actual, string label) { Need(Math.Abs(expected - actual) < .001f, label + ": expected " + expected + ", actual " + actual); }
        private static object Call(SimulationEngine e, string method, params object[] args) { return typeof(SimulationEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(e, args); }
        private static SimulationEngine Town()
        {
            WorldState w = new WorldState { Width = 40, Height = 40, Seed = 456, RngState = 81, Cells = new TileData[1600], NaturalWildlife = false };
            for (int i = 0; i < w.Cells.Length; i++) w.Cells[i] = new TileData { Terrain = TerrainType.Grass, Height = .5f, Moisture = .7f, Temperature = .5f, Vegetation = .6f, DrainageParent = -1 };
            SimulationEngine e = new SimulationEngine(w); e.Spawn(SpeciesType.Human, 20, 20, 4); w.Laws.Births = w.Laws.Aging = false;
            foreach (UnitData u in w.Units) { u.Age = 25; u.Task = UnitTask.Idle; u.DecisionTimer = 1000; u.Hunger = u.Thirst = u.Fatigue = u.SocialNeed = .1f; }
            SettlementData s = w.Settlements[0]; s.NextPolicyReviewTick = 100000; s.Food = 200; s.Wood = 100;
            foreach (TechnologyDefinition t in TechnologyCatalog.All) s.UnlockedTechnologies.Add(t.Id);
            s.ActiveTechnology = "";
            return e;
        }
        private static BuildingData Add(SimulationEngine e, BuildingType type, int x, int y)
        {
            Call(e, "AddBuilding", e.World.Settlements[0], x, y, type); return e.World.Buildings[e.World.Buildings.Count - 1];
        }
        // These isolated recipe checks begin after arrival. Real doorway traversal is
        // covered separately by CarpenterFetchesPlanks and BuildingInteractionScenarios.
        private static void At(UnitData u, BuildingData b)
        {
            u.X = b.X + .5f; u.Y = b.Y + .5f; u.Path.Clear(); u.PathCursor = 0; u.TargetCell = b.Y * 40 + b.X;
            u.InsideBuildingId = BuildingAccess.IsEnclosed(b.Type) ? b.Id : 0;
            u.EnteringBuildingId = 0; u.BuildingInteractionTimer = 0;
        }
        private static void Work(SimulationEngine e, UnitData u, float seconds) { Call(e, "PerformAgriculturalWork", u, e.World.Settlements[0], seconds); }
        private static UnitData Animal(SimulationEngine e, SpeciesType species, int x = 22, int y = 20)
        {
            e.Spawn(species, x, y, 1); UnitData a = e.World.Units[e.World.Units.Count - 1]; a.Age = 4; return a;
        }
        private static UnitData Tame(SimulationEngine e, UnitData u, SpeciesType species)
        {
            BuildingData p = e.World.Buildings.Find(b => b.Type == BuildingType.Pasture) ?? Add(e, BuildingType.Pasture, 22, 20);
            UnitData a = Animal(e, species);
            if (u.InsideBuildingId != 0) Need(e.TryExitBuilding(u.Id), "leave workshop before approaching animal");
            u.X = a.X; u.Y = a.Y; u.HarvestProduct = AgriculturalProduct.Hay; u.HarvestAmount = 1;
            Need(e.TryTameAnimal(u.Id, a.Id, p.Id), "tame with physical hay"); return a;
        }
        public static void CartRequiresResearchRecipeTimeAndCarpenter()
        {
            SimulationEngine e = Town(); WorldState w = e.World; SettlementData s = w.Settlements[0]; UnitData u = w.Units[0]; BuildingData mill = Add(e, BuildingType.Sawmill, 21, 20);
            mill.AgriculturalStock[(int)AgriculturalProduct.Fiber] = 4; mill.AgriculturalStock[(int)AgriculturalProduct.Hide] = 2; s.Wood = 0; mill.Inventory[(int)ItemType.Plank] = 12;
            s.UnlockedTechnologies.Remove("l02_carts"); Need(!e.TryStartEquipment(u.Id, 5), "no research no cart"); Near(12, mill.Inventory[2], "no cost before tech"); s.UnlockedTechnologies.Add("l02_carts");
            Need(e.TryStartEquipment(u.Id, 5), "start carpenter job"); Need(u.Profession == UnitProfession.Carpenter, "carpenter makes cart"); At(u, mill);
            Work(e, u, 7); Need(!u.OwnsWagon, "cart takes time"); Near(12, mill.Inventory[2], "inputs not prematurely consumed"); Work(e, u, 1);
            Need(u.OwnsWagon, "completed cart"); Near(0, mill.Inventory[2], "12 planks"); Near(0, mill.AgriculturalStock[2], "4 fiber"); Near(0, mill.AgriculturalStock[7], "2 hides");
            Near(1, SimulationEngine.CarryCapacityFor(u), "unhitched wagon doesn't grant capacity"); Need(!e.TryStartEquipment(u.Id, 5), "no duplicate cart");
            UnitData a = Tame(e, u, SpeciesType.Donkey); Need(e.TryMountNearby(u), "hitch real donkey"); Near(150, SimulationEngine.CarryCapacityFor(u), "cart150");
            u.BasketLevel = 3; a.HasSaddleBags = true; Call(e, "EnforceEquipment", u); Near(150, SimulationEngine.CarryCapacityFor(u), "cart not multiplied by bags/basket");
            Need(w.Validate(out string error), error);
        }
        public static void CancelledCraftAndChildrenCannotCreateEquipment()
        {
            SimulationEngine e = Town(); UnitData u = e.World.Units[0]; SettlementData s = e.World.Settlements[0]; BuildingData mill = Add(e, BuildingType.Sawmill, 21, 20); mill.Inventory[(int)ItemType.RawStone] = 1; s.Wood = 0; mill.Inventory[2] = 2;
            Need(e.TryStartEquipment(u.Id, 4), "hunting tool start"); At(u, mill); Work(e, u, 4); Call(e, "ChangeTask", u, UnitTask.Rest);
            Near(0, u.EquipmentProgress, "interruption resets timer"); Near(2, mill.Inventory[2], "cancel consumes no planks"); Near(1, mill.Inventory[(int)ItemType.RawStone], "cancel consumes no stone"); Need(u.HuntingToolLevel == 0, "no free tool");
            u.Age = 12; Need(!e.TryStartEquipment(u.Id, 4), "child cannot craft hunting tool"); u.BasketLevel = 3; Near(1, SimulationEngine.CarryCapacityFor(u), "light work stays one");
            u.Age = 25; Need(e.TryStartEquipment(u.Id, 4), "adult can resume fresh"); At(u, mill); Work(e, u, 8); Need(u.HuntingToolLevel == 1 && u.Profession == UnitProfession.Hunter, "real tool unlocks hunter"); Near(0, mill.Inventory[2], "tool planks paid");
        }
        public static void BasketGradesAndAnimalsUseDistinctCapacity()
        {
            SimulationEngine e = Town(); UnitData u = e.World.Units[0]; BuildingData hall = e.World.Buildings.Find(b => b.Type == BuildingType.Hall), mill = Add(e, BuildingType.Sawmill, 21, 20);
            hall.AgriculturalStock[2] = 3; mill.AgriculturalStock[2] = 10; mill.AgriculturalStock[7] = 4; mill.Inventory[2] = 2;
            for (int grade = 1; grade <= 3; grade++) { Need(e.TryStartEquipment(u.Id, grade), "start basket grade"); At(u, grade == 1 ? hall : mill); Work(e, u, 8); Near(grade == 1 ? 10 : grade == 2 ? 15 : 25, SimulationEngine.CarryCapacityFor(u), "basket grade"); }
            Near(0, hall.AgriculturalStock[2] + mill.AgriculturalStock[2], "fiber3+4+6 consumed"); UnitData a = Tame(e, u, SpeciesType.Horse); Need(e.TryMountNearby(u), "mount horse"); Near(75, SimulationEngine.CarryCapacityFor(u), "horse75");
            mill.AgriculturalStock[2] = 2; Need(e.TryStartEquipment(u.Id, 6), "craft bags"); At(u, mill);
            a.X = mill.EntranceCell % e.World.Width + .5f; a.Y = mill.EntranceCell / e.World.Width + .5f;
            Work(e, u, 8); Near(100, SimulationEngine.CarryCapacityFor(u), "horse with bags100"); Need(a.HasSaddleBags, "bags belong to animal");
        }
        public static void MountDeathDropsOverflowWithoutDuplicatingCargo()
        {
            SimulationEngine e = Town(); WorldState w = e.World; UnitData u = w.Units[0]; UnitData a = Tame(e, u, SpeciesType.Donkey); u.OwnsWagon = true; Need(e.TryMountNearby(u), "hitch");
            u.CargoKind = ItemType.IronOre; u.CargoAmount = 150; a.Health = 0; Call(e, "RemoveDead");
            Need(u.MountId == 0 && !u.HasWagon, "dead mount detached"); Near(1, u.CargoAmount, "one in hands"); float ground = 0; foreach (GroundStockData stock in w.GroundStocks) if (stock.Item == ItemType.IronOre) ground += stock.Amount;
            Near(149, ground, "149 on ground"); Call(e, "RemoveDead"); float again = 0; foreach (GroundStockData stock in w.GroundStocks) if (stock.Item == ItemType.IronOre) again += stock.Amount; Near(149, again, "no second spill"); Need(w.Validate(out string error), error);
        }
        public static void RareTamingRequiresRealBaitAndOnSiteContact()
        {
            SimulationEngine e = Town(); UnitData u = e.World.Units[0]; BuildingData p = Add(e, BuildingType.Pasture, 22, 20); UnitData a = Animal(e, SpeciesType.SpiritStag);
            u.BasketLevel = 1; u.HarvestProduct = AgriculturalProduct.Berries; u.HarvestAmount = 10; u.X = 3; u.Y = 3; Need(!e.TryTameAnimal(u.Id, a.Id, p.Id), "no remote tame"); Near(10, u.HarvestAmount, "bait retained");
            u.X = a.X; u.Y = a.Y; e.World.Settlements[0].UnlockedTechnologies.Remove("k08_apprenticeship"); Need(!e.TryTameAnimal(u.Id, a.Id, p.Id), "rare knowledge gate"); e.World.Settlements[0].UnlockedTechnologies.Add("k08_apprenticeship");
            Need(e.TryTameAnimal(u.Id, a.Id, p.Id), "rare tame success"); Near(0, u.HarvestAmount, "ten berries spent"); Need(!e.TryTameAnimal(u.Id, a.Id, p.Id), "tame only once");
        }
        public static void HarvestIsFiniteAndMustBeDelivered()
        {
            SimulationEngine e = Town(); UnitData u = e.World.Units[0]; SettlementData s = e.World.Settlements[0]; BuildingData f = e.World.Buildings.Find(b => b.Type == BuildingType.Farm); f.FarmCrop = CropType.Flax; f.HarvestReady = 3; f.CropGrowth = 1;
            At(u, f); u.Task = UnitTask.Farm; float food = s.Food; Call(e, "PerformAgriculture", u, s, 10f); Near(1, u.HarvestAmount, "one harvested"); Near(2, f.HarvestReady, "finite field"); Near(food, s.Food, "no remote food"); Near(0, e.World.GetAgriculturalStock(s.Id, AgriculturalProduct.Fiber), "cargo not stock");
            BuildingData hall = e.World.Buildings.Find(b => b.Type == BuildingType.Hall); At(u, hall); u.Task = UnitTask.Return; u.TargetBuildingId = hall.Id; Call(e, "PerformAgriDelivery", u, s, 1f); Near(1, hall.AgriculturalStock[2], "actual delivery"); Near(0, u.HarvestAmount, "cargo removed");
        }
        public static void BrewingConsumesGrainFuelAndFiniteWater()
        {
            SimulationEngine e = Town(); UnitData u = e.World.Units[0]; SettlementData s = e.World.Settlements[0]; BuildingData b = Add(e, BuildingType.Brewery, 21, 20), well = Add(e, BuildingType.Well, 23, 20);
            b.AgriculturalStock[(int)AgriculturalProduct.Grain] = 2; b.Inventory[(int)ItemType.Firewood] = 1; well.WaterStored = 1; At(u, b); u.Task = UnitTask.Brew; u.TargetBuildingId = b.Id; Work(e, u, 0); Near(1, well.WaterStored, "no remote water debit"); At(u, well); Work(e, u, 0); Near(1, u.CarriedWater, "water carried"); At(u, b); Work(e, u, 11); Near(0, b.AgriculturalStock[12], "not ready"); Work(e, u, 1);
            Near(2, b.AgriculturalStock[12], "two ale"); Near(0, b.AgriculturalStock[1], "grain consumed"); Near(0, b.Inventory[3], "fuel consumed"); Near(0, well.WaterStored, "water consumed");
            u.Task = UnitTask.Brew; u.TargetBuildingId = b.Id; Work(e, u, 12); Near(2, b.AgriculturalStock[12], "no free second batch");
        }
        public static void PastureBreedingRequiresPairFeedAndBirthLaw()
        {
            SimulationEngine e = Town(); WorldState w = e.World; UnitData u = w.Units[0]; UnitData female = Tame(e, u, SpeciesType.Goat), male = Tame(e, u, SpeciesType.Goat); female.Sex = Sex.Female; male.Sex = Sex.Male; female.BreedingSeconds = WildlifeCatalog.BreedingInterval(female.Species,true); female.NextBreedingTick=male.NextBreedingTick=0; female.Age=male.Age=WildlifeCatalog.MaturityAge(female.Species); female.Hunger = male.Hunger = .1f;
            BuildingData p = w.GetBuilding(female.PastureId); female.X = male.X = p.X + .5f; female.Y = male.Y = p.Y + .5f; p.AgriculturalStock[(int)AgriculturalProduct.Hay] = 2;
            int before = w.Units.Count; Call(e, "UpdateDomesticAnimals"); Need(w.Units.Count == before, "birth law respected"); w.Laws.Births = true; Call(e, "UpdateDomesticAnimals"); Need(w.Units.Count == before + 1, "one fed birth"); Near(0, p.AgriculturalStock[5], "two hay spent");
            Call(e, "UpdateDomesticAnimals"); Need(w.Units.Count == before + 1, "no repeated birth"); Need(w.Validate(out string error), error);
        }
        public static void AgricultureSaveContinuesIdenticallyAndRejectsBrokenMount()
        {
            SimulationEngine e = Town(); UnitData u = e.World.Units[0]; UnitData a = Tame(e, u, SpeciesType.Horse); Need(e.TryMountNearby(u), "mount"); u.BasketLevel = 2; u.Intoxication = .35f; u.AlcoholHabit = .2f;
            WorldState copy = Clone(e.World); Need(copy.Validate(out string error), error); SimulationEngine second = new SimulationEngine(copy);
            for (int i = 0; i < 300; i++) { e.Step(.1f); second.Step(.1f); } Need(Snapshot(e.World) == Snapshot(second.World), "save continuation");
            copy = Clone(e.World); UnitData corrupted = copy.GetUnit(u.Id); corrupted.MountId = int.MaxValue; Need(!copy.Validate(out _), "invalid mount rejected");
        }
        public static void AlcoholIsFiniteAdultLeisureWithWorkPenalty()
        {
            SimulationEngine e = Town(); UnitData u = e.World.Units.Find(a => a.Id % 4 == 0); Need(u != null, "leisure adult fixture"); BuildingData b = Add(e, BuildingType.Brewery, 21, 20); At(u, b); b.AgriculturalStock[(int)AgriculturalProduct.Ale] = 2; u.SocialNeed = .8f;
            float sober = e.GetWorkMultiplier(u); Call(e, "ChooseAgriculturalWork", u, e.World.Settlements[0]); Near(1, b.AgriculturalStock[12], "one real ale consumed"); Need(u.Intoxication > 0 && u.AlcoholHabit > 0 && u.SocialNeed < .8f, "leisure and habit"); Need(e.GetWorkMultiplier(u) < sober, "intoxication slows work");
            u.Age = 12; u.Intoxication = 0; u.SocialNeed = .8f; Call(e, "ChooseAgriculturalWork", u, e.World.Settlements[0]); Near(1, b.AgriculturalStock[12], "children excluded"); Near(0, u.Intoxication, "child sober");
        }
        public static void DestroyedPastureReleasesAnimalsAndPreservesTransportLoad()
        {
            SimulationEngine e = Town(); UnitData u = e.World.Units[0]; UnitData a = Tame(e, u, SpeciesType.Donkey); Need(e.TryMountNearby(u), "mount"); u.CargoKind = ItemType.IronOre; u.CargoAmount = 50;
            BuildingData p = e.World.GetBuilding(a.PastureId); p.Health = 0; Call(e, "DestroyInvalidBuildings"); Need(!a.IsDomestic && a.HandlerId == 0 && u.MountId == 0, "links cleared synchronously"); Near(1, u.CargoAmount, "one in hands"); float ground = 0; foreach (GroundStockData g in e.World.GroundStocks) if (g.Item == ItemType.IronOre) ground += g.Amount; Near(49, ground, "overflow preserved"); Need(e.World.Validate(out string error), error);
        }
        public static void CarpenterFetchesPlanksThroughRealRouteBeforeCrafting()
        {
            SimulationEngine e = Town(); WorldState w = e.World; UnitData u = w.Units[0]; SettlementData s = w.Settlements[0]; BuildingData mill = Add(e, BuildingType.Sawmill, 28, 20); At(u, mill);
            s.Wood = 12; mill.AgriculturalStock[2] = 4; mill.AgriculturalStock[7] = 2;
            for (int y = 0; y < 40; y++) w.Cells[w.Index(24, y)].Terrain = TerrainType.DeepWater; w.NavigationRevision++;
            Need(e.TryStartEquipment(u.Id, 5), "plan allowed beside mill"); Work(e, u, 8); Near(12, s.Wood, "unreachable planks not debited"); Need(!u.OwnsWagon && u.CargoAmount == 0, "no remote material");
            w.Cells[w.Index(24, 20)].Terrain = TerrainType.Grass; w.NavigationRevision++;
            Need(e.TryStartEquipment(u.Id, 5), "bridge opens supply route"); bool sawLoad = false, sawDelivery = false;
            for (int tick = 0; tick < 3000 && !u.OwnsWagon; tick++)
            {
                s.BuildTimer = 0; e.Step(.1f);
                if (u.CargoKind == ItemType.Plank && u.CargoAmount > 0) { sawLoad = true; Need(u.CargoAmount <= 1.0001f, "hands carry one plank"); }
                if (mill.Inventory[2] > 0 && !u.OwnsWagon) sawDelivery = true;
            }
            Need(sawLoad && sawDelivery && u.OwnsWagon, "real pickup, walking, delivery and completed cart"); Near(0, s.Wood, "all12 delivered"); Near(0, mill.Inventory[2], "12 then crafted"); Need(w.Validate(out string error), error);
        }
        public static void PastureCannotConsumeRemoteHay()
        {
            SimulationEngine e = Town(); WorldState w = e.World; UnitData u = w.Units[0], cow = Tame(e, u, SpeciesType.Cow); cow.Sex = Sex.Female; cow.AnimalProduceTimer = 30; cow.Hunger = .4f;
            BuildingData p = w.GetBuilding(cow.PastureId), hall = w.Buildings.Find(b => b.Type == BuildingType.Hall); cow.X = p.X + .5f; cow.Y = p.Y + .5f; hall.AgriculturalStock[5] = 3;
            Call(e, "UpdateDomesticAnimals"); Near(0, p.AgriculturalStock[8], "no milk from remote hay"); Near(3, hall.AgriculturalStock[5], "remote stock untouched"); Near(.4f, cow.Hunger, "not fed remotely");
            p.AgriculturalStock[5] = 1; Call(e, "UpdateDomesticAnimals"); Near(1, p.AgriculturalStock[8], "milk from local delivered hay"); Near(0, p.AgriculturalStock[5], "local hay consumed");
        }
        public static void FedWorkerCanMakeFirstBasketDespiteTownFoodShortage()
        {
            SimulationEngine e = Town(); UnitData u = e.World.Units[0]; BuildingData hall = e.World.Buildings.Find(b => b.Type == BuildingType.Hall); SettlementData s = e.World.Settlements[0]; s.Food = 1; hall.AgriculturalStock[2] = 3; At(u, hall);
            Need((bool)Call(e, "ChooseAgriculturalWork", u, s), "fed worker can improve hauling despite poor town"); Need(u.Task == UnitTask.CraftEquipment && u.EquipmentOrder == 1, "first basket job"); Work(e, u, 8); Need(u.BasketLevel == 1, "basket produced"); Near(0, hall.AgriculturalStock[2], "fiber paid"); Near(1, s.Food, "no granted food");
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class AgricultureTests
    {
        [SetUp] public void Serializers() { AgricultureScenarios.Clone = w => JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)); AgricultureScenarios.Snapshot = w => JsonUtility.ToJson(w); }
        [TestCase("CartRequiresResearchRecipeTimeAndCarpenter")]
        [TestCase("CancelledCraftAndChildrenCannotCreateEquipment")]
        [TestCase("BasketGradesAndAnimalsUseDistinctCapacity")]
        [TestCase("MountDeathDropsOverflowWithoutDuplicatingCargo")]
        [TestCase("RareTamingRequiresRealBaitAndOnSiteContact")]
        [TestCase("HarvestIsFiniteAndMustBeDelivered")]
        [TestCase("BrewingConsumesGrainFuelAndFiniteWater")]
        [TestCase("PastureBreedingRequiresPairFeedAndBirthLaw")]
        [TestCase("AgricultureSaveContinuesIdenticallyAndRejectsBrokenMount")]
        [TestCase("AlcoholIsFiniteAdultLeisureWithWorkPenalty")]
        [TestCase("DestroyedPastureReleasesAnimalsAndPreservesTransportLoad")]
        [TestCase("CarpenterFetchesPlanksThroughRealRouteBeforeCrafting")]
        [TestCase("PastureCannotConsumeRemoteHay")]
        [TestCase("FedWorkerCanMakeFirstBasketDespiteTownFoodShortage")]
        public void Scenario(string name) { typeof(AgricultureScenarios).GetMethod(name).Invoke(null, null); }
    }
#endif
}
