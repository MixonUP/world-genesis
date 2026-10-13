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
    public static class FishingScenarios
    {
        public static Func<WorldState, WorldState> Clone;
        public static Func<WorldState, string> Snapshot;
        public static readonly List<object> Progression = new List<object>();
        private static void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        private static void Near(float expected, float actual, string message) { Need(Math.Abs(expected - actual) < .0002f, message + ": " + actual + " vs " + expected); }
        private static object Call(SimulationEngine e, string method, params object[] args)
        { return typeof(SimulationEngine).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(e, args); }
        private static SimulationEngine Fixture()
        {
            var w = new WorldState { Seed = 9137, RngState = 9137, Width = 32, Height = 32, Cells = new TileData[1024] };
            for (int i = 0; i < w.Cells.Length; i++) w.Cells[i] = new TileData { Terrain = TerrainType.Grass, Height = .55f, RouteHeight = .55f, Flow = 1, DrainageParent = -1, Moisture = .65f, Temperature = .42f };
            var e = new SimulationEngine(w); e.Spawn(SpeciesType.Human, 12, 12, 2);
            w.Laws.Aging = w.Laws.Births = w.Laws.Warfare = w.NaturalWildlife = false;
            w.Weather.Automatic = false;
            foreach (UnitData u in w.Units) { u.Hunger = u.Thirst = u.Fatigue = u.SocialNeed = 0; u.DecisionTimer = 500; u.Task = UnitTask.Idle; u.Path.Clear(); u.PathCursor = 0; }
            return e;
        }
        private static int Water(SimulationEngine e, WaterKind kind = WaterKind.River, float temp = .42f)
        {
            WorldState w = e.World; int cell = w.Index(18, 12);
            e.Paint(TerrainType.ShallowWater, 18, 12, 0);
            TileData t = w.Cells[cell]; t.WaterKind = kind; t.WaterDepth = 1; t.CurrentStrength = .35f; t.River = kind != WaterKind.Lake && kind != WaterKind.Ocean;
            t.Temperature = temp; t.Moisture = .7f; t.Biome = (byte)(kind == WaterKind.Ocean || kind == WaterKind.Estuary ? BiomeType.Ocean : BiomeType.Freshwater); w.Cells[cell] = t;
            w.Infrastructure[cell].Fish = w.Infrastructure[cell].FishCapacity = 6;
            return cell;
        }
        private static UnitData AtBank(SimulationEngine e)
        { UnitData u = e.World.Units[0]; u.X = 17.5f; u.Y = 12.5f; u.Path.Clear(); u.PathCursor = 0; return u; }
        private static void Unlock(SettlementData s)
        { foreach (TechnologyDefinition t in TechnologyCatalog.All) if (!s.UnlockedTechnologies.Contains(t.Id)) s.UnlockedTechnologies.Add(t.Id); }
        private static BuildingData Sawmill(SimulationEngine e)
        {
            var s = e.World.Settlements[0];
            return (BuildingData)Call(e, "AddBuilding", s, s.X + 4, s.Y + 1, BuildingType.Sawmill);
        }
        private static void Catch(SimulationEngine e, UnitData u, int cell)
        {
            Need(e.TryStartFishing(u.Id, cell), "Cannot start valid fishing");
            Need(u.PathCursor == u.Path.Count, "Fixture fisher not on bank");
            Call(e, "PerformFishing", u, e.World.GetSettlement(u.SettlementId), 10f);
        }
        private static void WorkerStep(SimulationEngine e, UnitData u)
        {
            e.World.Tick++; e.World.SimulatedSeconds += .1f; Call(e, "BuildSpatialIndex"); Call(e, "UpdateUnit", u, .1f); Call(e, "UpdatePolitics");
            Need(SimulationEngine.CarriedLoad(u) <= SimulationEngine.CarryCapacityFor(u) + .0001f, "Physical carrier overflow");
        }
        private static uint RollFor(SimulationEngine e, int cell, int rod, FishSpeciesId species)
        {
            float total = 0, low = 0, high = 0;
            foreach (FishDefinition f in FishingCatalog.All)
            {
                if (!FishingCatalog.Eligible(e.World, cell, rod, f.Id)) continue;
                if (f.Id == species) low = total;
                total += f.Weight;
                if (f.Id == species) high = total;
            }
            Need(high > low, "Requested test species is ineligible");
            for (uint seed = 1; seed < 1000000; seed++) { uint state = seed; float value = StableRandom.Value(ref state) * total; if (value >= low && value < high) return seed; }
            throw new InvalidOperationException("No bounded test RNG state");
        }
        public static void PrimitiveCatchConsumesBiomassAndRequiresPhysicalDelivery()
        {
            var e = Fixture(); var w = e.World; int cell = Water(e); var u = AtBank(e); var s = w.Settlements[0];
            Near(1, FishingCatalog.RodLevel(u), "Primitive rod"); uint rng = w.RngState;
            Need(e.TryStartFishing(u.Id, cell), "Begin primitive cast");
            Call(e, "PerformFishing", u, s, .1f); Near(6, w.Infrastructure[cell].Fish, "Premature catch"); Need(w.RngState == rng, "Roll before cast completes");
            Call(e, "PerformFishing", u, s, 10f);
            Near(5, w.Infrastructure[cell].Fish, "Biomass not debited exactly once"); Near(1, u.HarvestAmount, "Missing catch"); Need(u.HarvestProduct == AgriculturalProduct.Fish && FishingCatalog.Get(u.FishSpeciesId).Rarity == FishRarity.Common, "Beginner gained rare fish");
            Near(0, w.GetAgriculturalStock(s.Id, AgriculturalProduct.Fish), "Remote fish credit");
            for (int i = 0; i < 200 && u.HarvestAmount > 0; i++) WorkerStep(e, u);
            Near(0, u.HarvestAmount, "Fish never delivered"); Near(1, w.GetAgriculturalStock(s.Id, AgriculturalProduct.Fish), "Physical warehouse transaction missing");
            Need(w.Validate(out string error), error);
        }
        public static void SpeciesRequireBothRodAndHabitat()
        {
            var e = Fixture(); int cell = Water(e); var w = e.World;
            foreach (FishDefinition f in FishingCatalog.All)
                for (int grade = 1; grade < f.MinimumRodLevel; grade++) Need(!FishingCatalog.Eligible(w, cell, grade, f.Id), "Early rarity unlock " + f.Id);
            Need(FishingCatalog.Eligible(w, cell, 3, FishSpeciesId.PearlCarp), "River rare not accessible");
            Need(FishingCatalog.Eligible(w, cell, 4, FishSpeciesId.MoonTrout), "Cool river epic not accessible");
            Need(FishingCatalog.Eligible(w, cell, 5, FishSpeciesId.AncientSturgeon), "Deep river legendary not accessible");
            Need(!FishingCatalog.Eligible(w, cell, 5, FishSpeciesId.StarMarlin), "Ocean fish in freshwater");
            Water(e, WaterKind.Ocean, .7f);
            Need(FishingCatalog.Eligible(w, cell, 5, FishSpeciesId.StarMarlin), "Warm ocean legendary inaccessible");
            Need(!FishingCatalog.Eligible(w, cell, 5, FishSpeciesId.MoonTrout), "Mountain fish in ocean");
            w.Cells[cell].Temperature = .2f; Need(!FishingCatalog.Eligible(w, cell, 5, FishSpeciesId.StarMarlin), "Tropical fish in freezing biome");
        }
        public static void EpicCatchSplitsFoodAndGroundMaterialWithoutCapacityOrTaxDuplication()
        {
            var e = Fixture(); var w = e.World; int cell = Water(e); var u = AtBank(e); var s = w.Settlements[0]; u.FishingRodLevel = 4;
            uint seed = RollFor(e, cell, 4, FishSpeciesId.MoonTrout); w.RngState = seed; uint next = seed; StableRandom.Value(ref next);
            float tax = w.Buildings.Sum(b => b.TaxStocks.Sum()); Catch(e, u, cell);
            Need(u.FishSpeciesId == FishSpeciesId.MoonTrout, "Wrong deterministic species"); Need(w.RngState == next, "Catch made more than one RNG draw");
            Near(1, u.HarvestAmount, "Rare food cargo"); Need(u.HarvestProduct == AgriculturalProduct.DelicacyFish, "Rare food type");
            Near(.25f, w.AgriculturalDrops.Where(d => d.Product == AgriculturalProduct.AncientScale).Sum(d => d.Amount), "Missing physical rare material");
            Need(w.AgriculturalDrops.All(d => d.X == u.X && d.Y == u.Y), "Material teleported");
            Near(0, w.GetAgriculturalStock(s.Id, AgriculturalProduct.AncientScale), "Free remote rare stock");
            Near(tax, w.Buildings.Sum(b => b.TaxStocks.Sum()), "Phantom tax on communal catch");
            Need(w.Validate(out string error), error);
        }
        public static void FishingRejectsChildrenCargoShortageAndDestroyedWater()
        {
            var e = Fixture(); var w = e.World; int cell = Water(e); var u = AtBank(e); uint rng = w.RngState;
            u.Age = 15; Need(!e.TryStartFishing(u.Id, cell), "Child fishing"); u.Age = 25;
            u.CarriedFood = .1f; Need(!e.TryStartFishing(u.Id, cell), "Overweight whole fish"); u.CarriedFood = 0;
            w.Infrastructure[cell].Fish = .99f; Need(!e.TryStartFishing(u.Id, cell), "Fractional biomass manufactured a fish"); w.Infrastructure[cell].Fish = 1;
            Need(e.TryStartFishing(u.Id, cell), "Valid final cast"); e.Paint(TerrainType.Grass, 18, 12, 0);
            Call(e, "PerformFishing", u, w.Settlements[0], 10f); Near(0, u.HarvestAmount, "Catch from painted dry land"); Need(w.RngState == rng, "Cancelled cast used RNG");
        }
        public static void NoReturnRouteMeansNoCatchAndNoStockDebit()
        {
            var e = Fixture(); var w = e.World; int cell = Water(e); var u = AtBank(e);
            for (int y = 0; y < w.Height; y++) e.Paint(TerrainType.DeepWater, 15, y, 0);
            uint rng = w.RngState;
            Need(!e.TryStartFishing(u.Id, cell), "Fishing accepted without a reachable receiving store");
            Near(6, w.Infrastructure[cell].Fish, "Unreachable job stole biomass"); Need(w.RngState == rng, "Path failure rolled catch");
        }
        public static void RodGradesTwoThroughFiveAreMadeWithActualWalkingAndLocalCosts()
        {
            Progression.Clear(); var e = Fixture(); var w = e.World; var s = w.Settlements[0]; var u = w.Units[0];
            var hall = w.Buildings.Find(b => b.Type == BuildingType.Hall); var mill = Sawmill(e); Unlock(s);
            s.Wood = 14; hall.AgriculturalStock[(int)AgriculturalProduct.Fiber] = 14; hall.AgriculturalStock[(int)AgriculturalProduct.Hide] = 6;
            hall.AgriculturalStock[(int)AgriculturalProduct.RiverPearl] = .5f; hall.AgriculturalStock[(int)AgriculturalProduct.AncientScale] = .25f;
            for (int grade = 2; grade <= 5; grade++)
            {
                // Keep the actual position and indoor state from the preceding grade;
                // the next order must leave the workshop and fetch its own inputs.
                u.Hunger = u.Thirst = u.Fatigue = u.SocialNeed = 0;
                Need(e.TryStartFishingRod(u.Id), "Cannot request grade " + grade); Near(grade - 1, FishingCatalog.RodLevel(u), "Instant rod grant");
                int moves = 0, carriedSamples = 0; float lastX = u.X, lastY = u.Y;
                for (int tick = 0; tick < 6000 && u.FishingRodLevel < grade; tick++)
                {
                    WorkerStep(e, u);
                    if (u.X != lastX || u.Y != lastY) moves++;
                    if (SimulationEngine.CarriedLoad(u) > 0) carriedSamples++;
                    lastX = u.X; lastY = u.Y;
                    if (u.Task == UnitTask.Idle && u.FishingRodLevel < grade) throw new InvalidOperationException("Craft interrupted before grade " + grade);
                }
                Need(u.FishingRodLevel == grade && moves > 0 && carriedSamples > 0, "Rod did not traverse real craft logistics");
                Need(Math.Abs(u.X - (mill.X + .5f)) < 1.1f && Math.Abs(u.Y - (mill.Y + .5f)) < 1.1f, "Upgrade completed away from workshop");
                Progression.Add(new { grade, tick = w.Tick, moves, carriedSamples, rod = FishingCatalog.RodName(grade) });
            }
            Near(0, s.Wood + w.Buildings.Sum(b => b.Inventory[(int)ItemType.Plank]), "Plank conservation");
            foreach (AgriculturalProduct p in new[] { AgriculturalProduct.Fiber, AgriculturalProduct.Hide, AgriculturalProduct.RiverPearl, AgriculturalProduct.AncientScale }) Near(0, w.GetAgriculturalStock(s.Id, p), "Recipe cost " + p);
            Need(!e.TryStartFishingRod(u.Id), "Sixth rod grade"); Need(w.Validate(out string error), error);
        }
        public static void RodCraftNeedsUnlockAndRareInputsAndCannotSkipGrades()
        {
            var e = Fixture(); var w = e.World; var s = w.Settlements[0]; var u = w.Units[0]; Sawmill(e);
            var hall = w.Buildings.Find(b => b.Type == BuildingType.Hall); s.Wood = 30; hall.AgriculturalStock[2] = 30; hall.AgriculturalStock[7] = 10;
            Need(!e.TryStartFishingRod(u.Id), "No research gate"); Unlock(s);
            Need(!e.TryStartEquipment(u.Id, 10), "Direct legendary rod request skipped grades");
            u.FishingRodLevel = 3; Need(!e.TryStartFishingRod(u.Id), "Epic rod without pearl");
            hall.AgriculturalStock[15] = .5f; Need(e.TryStartFishingRod(u.Id), "Real pearl not accepted");
            Call(e, "ChangeTask", u, UnitTask.Idle); u.FishingRodLevel = 4;
            Need(!e.TryStartFishingRod(u.Id), "Legendary rod without ancient scale");
            u.Age = 15; Need(!e.TryStartFishingRod(u.Id), "Child crafts rod");
        }
        public static void CancelledAndBlockedCraftKeepsAllUnspentIngredients()
        {
            var e = Fixture(); var w = e.World; var s = w.Settlements[0]; var u = w.Units[0]; var mill = Sawmill(e); Unlock(s);
            var hall = w.Buildings.Find(b => b.Type == BuildingType.Hall); s.Wood = 2; hall.AgriculturalStock[2] = 2;
            Need(e.TryStartFishingRod(u.Id), "Start craft before interruption");
            Call(e, "ChangeTask", u, UnitTask.Idle); Near(2, s.Wood, "Cancellation charged planks"); Near(2, hall.AgriculturalStock[2], "Cancellation charged fiber"); Near(1, FishingCatalog.RodLevel(u), "Cancellation granted rod");
            int barrier = mill.X - 1;
            for (int y = 0; y < w.Height; y++) e.Paint(TerrainType.DeepWater, barrier, y, 0);
            u.X = hall.X + .5f; u.Y = hall.Y + .5f;
            Need(!e.TryStartFishingRod(u.Id), "Unreachable workshop accepted craft"); Near(2, s.Wood, "Blocked craft charged planks");
        }
        public static void LegacyArraysCargoAndRngMigrateWithoutFreeUpgrades()
        {
            var e = Fixture(); var w = e.World; var u = w.Units[0]; var b = w.Buildings[0];
            b.AgriculturalStock[2] = 7; b.AgriculturalStock[12] = 3; u.HarvestProduct = AgriculturalProduct.Meat; u.HarvestAmount = .7f;
            w.AgricultureVersion = 1; Array.Resize(ref w.LostAgriculture, 13); w.LostAgriculture[7] = 2;
            foreach (BuildingData building in w.Buildings) Array.Resize(ref building.AgriculturalStock, 13);
            w.FishingVersion = 0; foreach (UnitData unit in w.Units) unit.FishingRodLevel = 0;
            uint rng = w.RngState; int next = w.NextId; long tick = w.Tick;
            Need(w.Validate(out string error), error); var restored = new SimulationEngine(Clone(w)); w = restored.World;
            Need(w.AgricultureVersion == 2 && w.FishingVersion == 1 && w.Buildings[0].AgriculturalStock.Length == 17, "Version migration");
            Near(7, w.Buildings[0].AgriculturalStock[2], "Legacy fiber lost"); Near(3, w.Buildings[0].AgriculturalStock[12], "Legacy ale lost"); Near(.7f, w.Units[0].HarvestAmount, "Legacy cargo lost"); Near(2, w.LostAgriculture[7], "Legacy sink lost");
            Need(w.RngState == rng && w.NextId == next && w.Tick == tick && w.Units.All(v => v.FishingRodLevel == 1), "Migration issued RNG/IDs/advanced rod");
            string once = Snapshot(w); new SimulationEngine(w); Need(once == Snapshot(w), "Migration not idempotent"); Need(w.Validate(out error), error);
        }
        public static void SaveContinuesMidCastAndDuringMaterialHaul()
        {
            var a = Fixture(); int cell = Water(a); var u = AtBank(a); u.FishingRodLevel = 4;
            Need(a.TryStartFishing(u.Id, cell), "Start saved cast"); a.Step(.3f);
            var b = new SimulationEngine(Clone(a.World)); Need(Snapshot(a.World) == Snapshot(b.World), "Cast load changed state");
            for (int i = 0; i < 111; i++) { a.Step(.1f); b.Step(.1f); } Need(Snapshot(a.World) == Snapshot(b.World), "Saved cast diverged");
            a = Fixture(); u = a.World.Units[0]; var s = a.World.Settlements[0]; Unlock(s); Sawmill(a); s.Wood = 2; a.World.Buildings[0].AgriculturalStock[2] = 2;
            Need(a.TryStartFishingRod(u.Id), "Start saved craft"); for (int i = 0; i < 33; i++) WorkerStep(a, u);
            b = new SimulationEngine(Clone(a.World)); Need(Snapshot(a.World) == Snapshot(b.World), "Craft load changed state");
            for (int i = 0; i < 211; i++) { a.Step(.1f); b.Step(.1f); } Need(Snapshot(a.World) == Snapshot(b.World), "Saved craft diverged");
        }
        public static void FishFoodAndMaterialsRespectTypedStorageAndDeathDrop()
        {
            var e = Fixture(); var w = e.World; var s = w.Settlements[0]; var u = w.Units[0]; var hall = w.Buildings[0];
            var warehouse = (BuildingData)Call(e, "AddBuilding", s, 7, 7, BuildingType.Warehouse);
            var granary = (BuildingData)Call(e, "AddBuilding", s, 8, 7, BuildingType.Granary);
            Need(StorageCapacity.Accepts(granary, AgriculturalProduct.DelicacyFish) && !StorageCapacity.Accepts(granary, AgriculturalProduct.AncientScale), "Granary classification");
            Need(StorageCapacity.Accepts(warehouse, AgriculturalProduct.RiverPearl) && !StorageCapacity.Accepts(warehouse, AgriculturalProduct.Fish), "Warehouse classification");
            hall.AgriculturalStock[15] = 2; hall.AgriculturalStock[16] = 3; hall.AgriculturalStock[14] = 1;
            float food = s.Food; w.Tick = 10; Call(e, "UpdateAgriculture"); Near(food + 4, s.Food, "Delicacy food yield"); Near(2, hall.AgriculturalStock[15], "Pearls eaten"); Near(3, hall.AgriculturalStock[16], "Scales eaten");
            u.HarvestProduct = AgriculturalProduct.DelicacyFish; u.HarvestAmount = 1; u.Health = 0; float x = u.X, y = u.Y;
            Call(e, "RemoveDead"); Need(w.AgriculturalDrops.Any(d => d.Product == AgriculturalProduct.DelicacyFish && d.Amount == 1 && d.X == x && d.Y == y), "Dead fisher lost cargo"); Need(w.Validate(out string error), error);
        }
        public static void CorruptedGradesHistoryAndNewInventoryAreRejected()
        {
            var e = Fixture(); var w = e.World; var u = w.Units[0];
            u.FishingRodLevel = 6; Need(!w.Validate(out _), "Grade6 accepted"); u.FishingRodLevel = -1; Need(!w.Validate(out _), "Negative grade accepted");
            u.FishingRodLevel = 1; u.FishSpeciesId = FishSpeciesId.StarMarlin; u.FishCaughtCount = 1; Need(!w.Validate(out _), "Primitive legendary history accepted");
            u.FishCaughtCount = 0; u.FishSpeciesId = FishSpeciesId.None; w.Buildings[0].AgriculturalStock[16] = float.NaN; Need(!w.Validate(out _), "NaN rare material accepted");
            w.Buildings[0].AgriculturalStock[16] = 0; w.FishingVersion = 2; Need(!w.Validate(out _), "Unknown fishing schema accepted");
        }
        public static void DistantFishingByproductsAreCollectedButRangeIsBounded()
        {
            var e = Fixture(); var w = e.World; var s = w.Settlements[0]; var u = w.Units[0];
            float x = s.X + .5f, y = s.Y + .5f;
            u.X = x + 14; u.Y = y; u.Profession = UnitProfession.Farmer;
            w.AgriculturalDrops.Add(new AgriculturalDrop { Product = AgriculturalProduct.RiverPearl, Amount = .25f, X = x + 14, Y = y });
            Need((bool)Call(e, "ChooseLoot", u, s), "Valid fishing byproduct outside radius12 was ignored");
            Near(.25f, u.HarvestAmount, "Distant pearl not physically picked up"); Near(0, w.GetAgriculturalStock(s.Id, AgriculturalProduct.RiverPearl), "Pickup credited remote stock");
            for (int i = 0; i < 500 && u.HarvestAmount > 0; i++) WorkerStep(e, u);
            Near(.25f, w.GetAgriculturalStock(s.Id, AgriculturalProduct.RiverPearl), "Distant pearl did not reach store");
            w.AgriculturalDrops.Clear(); u.X = x + 18.5f; u.Y = y;
            w.AgriculturalDrops.Add(new AgriculturalDrop { Product = AgriculturalProduct.AncientScale, Amount = .25f, X = u.X, Y = u.Y });
            Need(!(bool)Call(e, "ChooseLoot", u, s), "Fishing material exceeded bounded radius18");
            w.AgriculturalDrops.Clear(); u.X = x + 14;
            w.AgriculturalDrops.Add(new AgriculturalDrop { Product = AgriculturalProduct.Hide, Amount = 1, X = u.X, Y = u.Y });
            Need(!(bool)Call(e, "ChooseLoot", u, s), "Fishing fix expanded ordinary hunting loot");
        }
        public static void AvailableRareHabitatIsPreferredOverNearbyUnsuitableWater()
        {
            var e = Fixture(); var w = e.World; int river = Water(e); var u = w.Units.Find(v => (v.Id & 1) == 0); var s = w.Settlements[0];
            Need(u != null, "Fixture needs even resident"); u.X = 16.5f; u.Y = 12.5f; u.FishingRodLevel = 3;
            s.Food = 100; w.Buildings[0].AgriculturalStock[(int)AgriculturalProduct.DelicacyFish] = 4;
            e.Paint(TerrainType.ShallowWater, 16, 11, 0); int stream = w.Index(16, 11);
            w.Cells[stream].WaterKind = WaterKind.Stream; w.Cells[stream].WaterDepth = .4f; w.Cells[stream].Temperature = .2f;
            w.Infrastructure[stream].Fish = w.Infrastructure[stream].FishCapacity = 6;
            Need((bool)Call(e, "ChooseFishing", u, s), "Material search wrongly disabled by full food reserve");
            Need(u.ResourceCell == river, "Fisher selected nearby water that cannot provide required material");
            Near(6, w.Infrastructure[river].Fish, "Destination selection granted a rare catch");
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class FishingTests
    {
        [SetUp] public void Serializer() { FishingScenarios.Clone = w => JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)); FishingScenarios.Snapshot = w => JsonUtility.ToJson(w); }
        [Test] public void PrimitivePhysicalCatch() { FishingScenarios.PrimitiveCatchConsumesBiomassAndRequiresPhysicalDelivery(); }
        [Test] public void GradeAndHabitatGates() { FishingScenarios.SpeciesRequireBothRodAndHabitat(); }
        [Test] public void EpicPhysicalOutputs() { FishingScenarios.EpicCatchSplitsFoodAndGroundMaterialWithoutCapacityOrTaxDuplication(); }
        [Test] public void CancelledInvalidCast() { FishingScenarios.FishingRejectsChildrenCargoShortageAndDestroyedWater(); }
        [Test] public void DisconnectedBank() { FishingScenarios.NoReturnRouteMeansNoCatchAndNoStockDebit(); }
        [Test] public void EntireRodProgression() { FishingScenarios.RodGradesTwoThroughFiveAreMadeWithActualWalkingAndLocalCosts(); }
        [Test] public void ResearchAndRareInputs() { FishingScenarios.RodCraftNeedsUnlockAndRareInputsAndCannotSkipGrades(); }
        [Test] public void CancelledCraftConservation() { FishingScenarios.CancelledAndBlockedCraftKeepsAllUnspentIngredients(); }
        [Test] public void LegacyMigration() { FishingScenarios.LegacyArraysCargoAndRngMigrateWithoutFreeUpgrades(); }
        [Test] public void MidActivitySaveContinuation() { FishingScenarios.SaveContinuesMidCastAndDuringMaterialHaul(); }
        [Test] public void FoodStorageAndDeath() { FishingScenarios.FishFoodAndMaterialsRespectTypedStorageAndDeathDrop(); }
        [Test] public void MalformedData() { FishingScenarios.CorruptedGradesHistoryAndNewInventoryAreRejected(); }
        [Test] public void DistantRareMaterialCollection() { FishingScenarios.DistantFishingByproductsAreCollectedButRangeIsBounded(); }
        [Test] public void LocalHabitatPreference() { FishingScenarios.AvailableRareHabitatIsPreferredOverNearbyUnsuitableWater(); }
    }
#endif
}
