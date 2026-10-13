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
    public static class ProductionScenarios
    {
        public static Func<WorldState, WorldState> Clone;
        public static Func<WorldState, string> Snapshot;
        public static readonly List<object> Batches = new List<object>();
        private static void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        private static void Near(float expected, float actual, string message) { Need(Math.Abs(expected - actual) < .001f, message + ": " + actual + " != " + expected); }
        private static object Call(SimulationEngine e, string name, params object[] args)
        { MethodInfo method = typeof(SimulationEngine).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance); Need(method != null, "Missing hook " + name); return method.Invoke(e, args); }
        private static SimulationEngine Fixture(bool unlock = true)
        {
            WorldState w = new WorldState { Seed = 9137, RngState = 9137, Width = 40, Height = 40, Cells = new TileData[1600] };
            for (int i = 0; i < w.Cells.Length; i++) w.Cells[i] = new TileData { Terrain = TerrainType.Grass, Height = .55f, RouteHeight = .55f, Flow = 1, DrainageParent = -1, Moisture = .65f, Temperature = .42f };
            SimulationEngine e = new SimulationEngine(w); e.Spawn(SpeciesType.Human, 10, 10, 3);
            w.Laws.Aging = w.Laws.Births = w.Laws.Warfare = w.NaturalWildlife = false; w.Weather.Automatic = false;
            foreach (UnitData u in w.Units) { u.Age = 25; u.Hunger = u.Thirst = u.Fatigue = u.SocialNeed = 0; u.DecisionTimer = 500; u.Task = UnitTask.Idle; u.Path.Clear(); u.PathCursor = 0; }
            if (unlock) foreach (TechnologyDefinition t in TechnologyCatalog.All) w.Settlements[0].UnlockedTechnologies.Add(t.Id);
            w.Settlements[0].Wood = 0;
            return e;
        }
        private static BuildingData Add(SimulationEngine e, BuildingType type, int x = 18, int y = 10)
        { return (BuildingData)Call(e, "AddBuilding", e.World.Settlements[0], x, y, type); }
        private static BuildingData Hall(WorldState w) { return w.Buildings.First(b => b.Type == BuildingType.Hall); }
        private static void Here(WorldState w, UnitData u, BuildingData b)
        {
            int cell = BuildingAccess.IsEnclosed(b.Type) ? BuildingAccess.EntranceCell(w, b) : w.Index(b.X, b.Y);
            Need(cell >= 0, "Fixture building has no doorway"); u.X = cell % w.Width + .5f; u.Y = cell / w.Width + .5f;
            u.InsideBuildingId = u.EnteringBuildingId = 0; u.BuildingInteractionTimer = 0; u.Path.Clear(); u.PathCursor = 0; u.DecisionTimer = 500;
        }
        private static void Supply(BuildingData b, IndustryRecipe r)
        { foreach (ItemAmount i in r.Inputs) b.Inventory[(int)i.Item] += i.Amount; foreach (AgriculturalAmount i in r.Agriculture) b.AgriculturalStock[(int)i.Product] += i.Amount; }
        private static void Valid(WorldState w) { Need(w.Validate(out string error), error); }
        private static void WorkerStep(SimulationEngine e, params UnitData[] units)
        {
            e.World.Tick++; e.World.SimulatedSeconds += .1f; Call(e, "BuildSpatialIndex");
            foreach (UnitData u in units) if (u.Health > 0) { Call(e, "UpdateUnit", u, .1f); Need(SimulationEngine.CarriedLoad(u) <= SimulationEngine.CarryCapacityFor(u) + .0001f, "Carrier overflow"); }
            Call(e, "UpdatePolitics");
            if (e.World.Tick % 10 == 0) Valid(e.World);
        }
        private static void Finish(SimulationEngine e, UnitData u, BuildingData b, int before = 0)
        { for (int i = 0; i < 4000 && b.CompletedIndustryBatches <= before; i++) WorkerStep(e, u); Need(b.CompletedIndustryBatches == before + 1, "Batch never completed " + b.ActiveIndustryRecipe); Valid(e.World); }
        private static float Material(WorldState w, ItemType item)
        {
            float total = w.LostResources[(int)item];
            if (item == ItemType.Plank) total += w.Settlements.Sum(s => s.Wood);
            foreach (BuildingData b in w.Buildings) { total += b.Inventory[(int)item] + b.TaxStocks[(int)item] + b.PersonalStocks.Where(p => p.Item == item).Sum(p => p.Amount); if (item == ItemType.Furniture) total += b.FurnitureCount; }
            foreach (UnitData u in w.Units) { if (u.CargoKind == item) total += u.CargoAmount; if (item == ItemType.Plank) total += u.CarriedWood; if (u.WorkTool == item) total++; if (u.Clothes == item) total++; }
            return total + w.GroundStocks.Where(g => !g.IsFood && g.Item == item).Sum(g => g.Amount);
        }
        private static float Agricultural(WorldState w, AgriculturalProduct product)
        { return w.LostAgriculture[(int)product] + w.Buildings.Sum(b => b.AgriculturalStock[(int)product]) + w.Units.Where(u => u.HarvestProduct == product).Sum(u => u.HarvestAmount) + w.AgriculturalDrops.Where(d => d.Product == product).Sum(d => d.Amount); }

        public static void EveryRecipeDebitsExactlyOneBatchAndProducesPhysicalOutput()
        {
            Batches.Clear();
            foreach (IndustryRecipe r in IndustryCatalog.All)
            {
                SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Add(e, r.Building); UnitData u = w.Units[0]; Here(w, u, b); Supply(b, r);
                Need(e.TryStartProduction(u.Id, b.Id, r.Id), "Recipe rejected " + r.Id); Near(0, Material(w, r.Output), "Early product");
                Finish(e, u, b); Near(r.OutputAmount, Material(w, r.Output), "Physical output " + r.Id);
                foreach (ItemAmount input in r.Inputs) Near(0, Material(w, input.Item), "Unspent input " + r.Id + "/" + input.Item);
                foreach (AgriculturalAmount input in r.Agriculture) Near(0, Agricultural(w, input.Product), "Unspent agricultural input " + r.Id);
                Near(r.OutputAmount, w.ProducedItems[(int)r.Output], "Production receipt"); Need(u.Profession == r.Profession, "Wrong production profession");
                Near(IndustryCatalog.IsCoin(r.Output) ? r.OutputAmount : 0, w.MintIssued[(int)r.Output], "Mint receipt");
                Batches.Add(new { recipe = r.Id.ToString(), output = r.Output.ToString(), amount = r.OutputAmount, tick = w.Tick, profession = u.Profession.ToString() });
            }
        }
        public static void DistantOreAndFuelTravelBeforeAnyIngotExists()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; IndustryRecipe r = IndustryCatalog.Get(IndustryRecipeType.IronIngot); BuildingData b = Add(e, r.Building, 25, 10); BuildingData source = Hall(w); UnitData u = w.Units[0]; Here(w, u, b); Supply(source, r);
            Need(e.TryStartProduction(u.Id, b.Id, r.Id), "Distant job rejected"); WorkerStep(e, u); Near(0, b.IndustryProgress, "Remote work progress"); Near(0, Material(w, r.Output), "Remote ingot"); Near(2, source.Inventory[(int)ItemType.IronOre], "Remote source debit");
            bool sawCargo = false; for (int i = 0; i < 4000 && b.CompletedIndustryBatches == 0; i++) { WorkerStep(e, u); sawCargo |= u.CargoAmount > 0; }
            Need(sawCargo, "No physical input cargo"); Need(b.CompletedIndustryBatches == 1, "Distant job never completed"); Near(1, Material(w, ItemType.IronIngot), "Delivered output"); Valid(w);
        }
        public static void MixedHideAndWoodTravelThroughDistinctLedgers()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; IndustryRecipe r = IndustryCatalog.Get(IndustryRecipeType.Leather); BuildingData b = Add(e, r.Building, 23, 10); UnitData u = w.Units[0]; Here(w, u, b); Supply(Hall(w), r);
            Need(e.TryStartProduction(u.Id, b.Id, r.Id), "Mixed recipe rejected"); bool material = false, agricultural = false;
            for (int i = 0; i < 4000 && b.CompletedIndustryBatches == 0; i++) { WorkerStep(e, u); material |= u.CargoAmount > 0; agricultural |= u.HarvestAmount > 0; }
            Need(material && agricultural, "Missing one real cargo channel"); Near(r.OutputAmount, Material(w, ItemType.Leather), "Leather output"); Near(0, Agricultural(w, AgriculturalProduct.Hide), "Hide not consumed"); Valid(w);
        }
        public static void MissingInputCannotMintAnOutputOrPartiallyConsumeRecipe()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; IndustryRecipe r = IndustryCatalog.Get(IndustryRecipeType.GoldCoin); BuildingData b = Add(e, r.Building); UnitData u = w.Units[0]; Here(w, u, b); b.Inventory[(int)ItemType.GoldIngot] = 1;
            e.TryStartProduction(u.Id, b.Id, r.Id); for (int i = 0; i < 300; i++) WorkerStep(e, u);
            Near(1, Material(w, ItemType.GoldIngot), "Partial recipe consumption"); Near(0, Material(w, ItemType.GoldCoin), "Coin without fuel"); Near(0, w.MintIssued.Sum(), "Free mint issuance");
        }
        public static void FullOutputStoragePreservesInputsUntilRoomExists()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; IndustryRecipe r = IndustryCatalog.Get(IndustryRecipeType.GoldCoin); BuildingData b = Add(e, r.Building); UnitData u = w.Units[0]; Here(w, u, b); Supply(b, r);
            b.Inventory[(int)ItemType.Salt] = ResourceCatalog.StorageCapacity - r.Inputs.Sum(i => i.Amount);
            e.TryStartProduction(u.Id, b.Id, r.Id); for (int i = 0; i < 250; i++) WorkerStep(e, u);
            Near(0, Material(w, ItemType.GoldCoin), "Overflow product"); Near(1, b.Inventory[(int)ItemType.GoldIngot], "Full store ate gold"); Near(1, b.Inventory[(int)ItemType.Charcoal], "Full store ate fuel"); Valid(w);
            b.Inventory[(int)ItemType.Salt] -= r.OutputAmount; if (u.Task != UnitTask.Produce) Need(e.TryStartProduction(u.Id, b.Id, r.Id), "Job did not restart after free space"); Finish(e, u, b); Near(r.OutputAmount, Material(w, ItemType.GoldCoin), "Freed storage did not resume");
        }
        public static void ResearchAgeWorkplaceAndForeignSettlementAreHardGates()
        {
            SimulationEngine e = Fixture(false); WorldState w = e.World; IndustryRecipe r = IndustryCatalog.Get(IndustryRecipeType.IronIngot); BuildingData b = Add(e, r.Building); UnitData u = w.Units[0]; Here(w, u, b); Supply(b, r);
            Need(!e.TryStartProduction(u.Id, b.Id, r.Id), "No-tech production"); foreach (TechnologyDefinition t in TechnologyCatalog.All) w.Settlements[0].UnlockedTechnologies.Add(t.Id);
            u.Age = 15; Need(!e.TryStartProduction(u.Id, b.Id, r.Id), "Child heavy industry"); u.Age = 25;
            Need(!e.TryStartProduction(u.Id, Hall(w).Id, r.Id), "Wrong building production"); int own = u.SettlementId; u.SettlementId = 0; Need(!e.TryStartProduction(u.Id, b.Id, r.Id), "Foreign producer"); u.SettlementId = own;
            Need(!e.TryStartProduction(u.Id, b.Id, (IndustryRecipeType)999), "Invalid recipe ID"); Near(0, w.ProducedItems.Sum(), "Rejected commands produced goods"); Valid(w);
        }
        public static void UnreachableSourceCannotBeDebitedOrTeleportToWorkshop()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; IndustryRecipe r = IndustryCatalog.Get(IndustryRecipeType.IronIngot); BuildingData b = Add(e, r.Building, 25, 10); UnitData u = w.Units[0]; Here(w, u, b); Supply(Hall(w), r);
            for (int y = 0; y < w.Height; y++) e.Paint(TerrainType.DeepWater, 17, y, 0);
            e.TryStartProduction(u.Id, b.Id, r.Id); for (int i = 0; i < 100; i++) WorkerStep(e, u);
            Near(2, Hall(w).Inventory[(int)ItemType.IronOre], "Unreachable ore removed"); Near(1, Hall(w).Inventory[(int)ItemType.Charcoal], "Unreachable fuel removed"); Near(0, Material(w, ItemType.IronIngot), "Remote product"); Valid(w);
        }
        public static void OneWorkplaceCannotHaveTwoWorkersOrStealAnActiveOrder()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Add(e, BuildingType.Smelter); IndustryRecipe r = IndustryCatalog.Get(IndustryRecipeType.IronIngot); Supply(b, r); UnitData a = w.Units[0], z = w.Units[1]; Here(w, a, b); Here(w, z, b);
            Need(e.TryStartProduction(a.Id, b.Id, r.Id), "First worker rejected"); Need(!e.TryStartProduction(z.Id, b.Id, r.Id), "Second worker stole slot"); Need(b.IndustryWorkerId == a.Id, "Owner changed"); Finish(e, a, b); Near(1, Material(w, ItemType.IronIngot), "Duplicate workplace output");
        }
        public static void TwoWorkshopsSharingLastInputsCannotDuplicateGoods()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; IndustryRecipe r = IndustryCatalog.Get(IndustryRecipeType.IronIngot); BuildingData a = Add(e, r.Building, 20, 9), b = Add(e, r.Building, 20, 13); UnitData u = w.Units[0], v = w.Units[1]; Here(w, u, a); Here(w, v, b); Supply(Hall(w), r);
            e.TryStartProduction(u.Id, a.Id, r.Id); e.TryStartProduction(v.Id, b.Id, r.Id);
            for (int i = 0; i < 1400 && a.CompletedIndustryBatches + b.CompletedIndustryBatches == 0; i++) WorkerStep(e, u, v);
            Need(a.CompletedIndustryBatches + b.CompletedIndustryBatches == 1, "Single recipe budget split into deadlock or duplicate output"); Near(1, Material(w, ItemType.IronIngot), "Shared budget output"); Near(0, Material(w, ItemType.IronOre), "Ore conservation"); Near(0, Material(w, ItemType.Charcoal), "Fuel conservation"); Valid(w);
        }
        public static void CancellingAfterPickupKeepsCargoAndReleasesTheWorkplace()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; IndustryRecipe r = IndustryCatalog.Get(IndustryRecipeType.IronIngot); BuildingData b = Add(e, r.Building, 24, 10); UnitData u = w.Units[0]; Here(w, u, b); Supply(Hall(w), r); Need(e.TryStartProduction(u.Id, b.Id, r.Id), "Start");
            for (int i = 0; i < 1000 && u.CargoAmount == 0; i++) WorkerStep(e, u); Need(u.CargoAmount > 0, "Never picked up"); float amount = u.CargoAmount; ItemType item = u.CargoKind;
            Call(e, "ChangeTask", u, UnitTask.Idle); Near(amount, u.CargoAmount, "Cancellation erased cargo"); Need(u.CargoKind == item && b.IndustryWorkerId == 0, "Cancellation leaked worker or item type"); Near(2, Material(w, ItemType.IronOre), "Cancelled ore conservation"); Near(1, Material(w, ItemType.Charcoal), "Cancelled fuel conservation"); Valid(w);
        }
        public static void DeathAndDestroyedWorkshopAccountForInputsAndReleaseLinks()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; IndustryRecipe r = IndustryCatalog.Get(IndustryRecipeType.IronIngot); BuildingData b = Add(e, r.Building); UnitData u = w.Units[0]; Here(w, u, b); Supply(b, r); Need(e.TryStartProduction(u.Id, b.Id, r.Id), "Start"); WorkerStep(e, u);
            u.Health = 0; Call(e, "RemoveDead"); Need(b.IndustryWorkerId == 0, "Dead worker still owns workplace"); Near(2, Material(w, ItemType.IronOre), "Death removed unspent ore"); Near(0, Material(w, ItemType.IronIngot), "Death completed order");
            b.Health = 0; Call(e, "DestroyInvalidBuildings"); Near(2, Material(w, ItemType.IronOre), "Destruction lost ore accounting"); Near(1, Material(w, ItemType.Charcoal), "Destruction lost fuel accounting"); Call(e, "DestroyInvalidBuildings"); Near(2, Material(w, ItemType.IronOre), "Repeated removal duplicated ore"); Valid(w);
        }
        public static void MidHaulAndMidWorkSaveContinueIdentically()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; IndustryRecipe r = IndustryCatalog.Get(IndustryRecipeType.IronIngot); BuildingData b = Add(e, r.Building, 24, 10); UnitData u = w.Units[0]; Here(w, u, b); Supply(Hall(w), r); Need(e.TryStartProduction(u.Id, b.Id, r.Id), "Start");
            for (int i = 0; i < 1000 && u.CargoAmount == 0; i++) WorkerStep(e, u); Need(u.CargoAmount > 0, "Missing mid-haul fixture");
            SimulationEngine copy = new SimulationEngine(Clone(w)); Need(Snapshot(w) == Snapshot(copy.World), "Constructor changed current save");
            for (int i = 0; i < 300; i++) { WorkerStep(e, u); WorkerStep(copy, copy.World.GetUnit(u.Id)); }
            Need(Snapshot(w) == Snapshot(copy.World), "Mid-haul continuation diverged");
            for (int i = 0; i < 1000 && b.IndustryProgress <= 0; i++) WorkerStep(e, u); Need(b.IndustryProgress > 0 && b.CompletedIndustryBatches == 0, "Missing mid-work fixture"); copy = new SimulationEngine(Clone(w));
            for (int i = 0; i < 150; i++) { WorkerStep(e, u); WorkerStep(copy, copy.World.GetUnit(u.Id)); }
            Need(Snapshot(w) == Snapshot(copy.World), "Mid-work continuation diverged"); Near(1, w.ProducedItems[(int)ItemType.IronIngot], "Saved order duplicated or lost output");
        }
        public static void LegacyMaterialArraysAndPaidInvoicesExpandWithoutLossOrRng()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Hall(w); w.ResourceVersion = 3; w.IndustryVersion = 0;
            Array.Resize(ref w.LostResources, 18); foreach (BuildingData building in w.Buildings) { Array.Resize(ref building.Inventory, 18); Array.Resize(ref building.TaxStocks, 18); Array.Resize(ref building.PaidConstructionMaterials, 18); }
            b.Inventory[(int)ItemType.Salt] = 3; b.Inventory[(int)ItemType.IronOre] = 4; b.TaxStocks[(int)ItemType.Salt] = 2; b.PaidConstructionMaterials[(int)ItemType.Plank] = 8; w.LostResources[(int)ItemType.Salt] = 1; w.Settlements[0].Wood = 7;
            uint rng = w.RngState; int id = w.NextId; Valid(w); SimulationEngine loaded = new SimulationEngine(Clone(w)); WorldState n = loaded.World;
            Need(n.ResourceVersion == 4 && n.IndustryVersion == 1 && n.RngState == rng && n.NextId == id, "Migration changed RNG/IDs/version"); Near(3, n.GetBuilding(b.Id).Inventory[(int)ItemType.Salt], "Old salt lost"); Near(8, n.GetBuilding(b.Id).PaidConstructionMaterials[(int)ItemType.Plank], "Paid invoice lost"); Near(7, n.Settlements[0].Wood, "Legacy plank lost");
            foreach (BuildingData building in n.Buildings) Need(building.Inventory.Length == ResourceCatalog.ItemCount && building.TaxStocks.Length == ResourceCatalog.ItemCount && building.PaidConstructionMaterials.Length == ResourceCatalog.ItemCount, "Old array not expanded"); Near(0, n.ProducedItems.Sum(), "Migration minted output"); Near(0, n.MintIssued.Sum(), "Migration minted money"); Valid(n);
        }
        public static void ForgedProgressNanAndCoinIssuanceAreRejected()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Add(e, BuildingType.Mint); WorldState changed = Clone(w); changed.MintIssued[(int)ItemType.GoldCoin] = 1; Need(!changed.Validate(out _), "Issuance exceeds production");
            changed = Clone(w); changed.ProducedItems[(int)ItemType.GoldCoin] = float.NaN; Need(!changed.Validate(out _), "NaN production ledger"); changed = Clone(w); changed.GetBuilding(b.Id).IndustryProgress = float.PositiveInfinity; Need(!changed.Validate(out _), "Infinite work progress");
            changed = Clone(w); changed.GetBuilding(b.Id).IndustryWorkerId = changed.Units[0].Id; Need(!changed.Validate(out _), "Unmatched worker accepted");
            changed = Clone(w); changed.IndustryVersion = 0; changed.IndustryBatchesCompleted = 1; Need(!changed.Validate(out _), "Legacy migration would erase forged production count");
            changed = Clone(w); changed.IndustryVersion = 0; changed.Units[0].DesiredIndustryEquipment = ItemType.IronTools; Need(!changed.Validate(out _), "Legacy migration would erase forged equipment order");
        }
        public static void SiegeGoodsNeedResearchAndCarpenterButNeverCreateWarriors()
        {
            foreach (IndustryRecipeType id in new[] { IndustryRecipeType.Ballista, IndustryRecipeType.Catapult, IndustryRecipeType.Trebuchet, IndustryRecipeType.BallistaBolts, IndustryRecipeType.StoneShot })
            {
                SimulationEngine e = Fixture(false); WorldState w = e.World; IndustryRecipe r = IndustryCatalog.Get(id); Need(r.Building == BuildingType.Carpentry && r.Profession == UnitProfession.Carpenter, "Siege production has invented workshop/profession"); BuildingData b = Add(e, BuildingType.Carpentry); UnitData u = w.Units[0]; Here(w, u, b); Supply(b, r);
                Need(!e.TryStartProduction(u.Id, b.Id, id), "Siege technology bypass"); foreach (TechnologyDefinition t in TechnologyCatalog.All) w.Settlements[0].UnlockedTechnologies.Add(t.Id); Need(e.TryStartProduction(u.Id, b.Id, id), "Researched siege rejected"); Finish(e, u, b);
                Need(w.Units.All(v => v.Profession != UnitProfession.Guard), "Production created a free warrior"); Near(r.OutputAmount, Material(w, r.Output), "Missing physical siege goods");
            }
        }
        public static void ConnectedChainsUseEarlierOutputsWithoutIntermediateGrants()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; UnitData u = w.Units[0]; u.BasketLevel = 3;
            BuildingData warehouse = Add(e, BuildingType.Warehouse, 14, 20); w.Settlements[0].Wood = 100;
            foreach (ItemType item in new[] { ItemType.Log, ItemType.IronOre, ItemType.CopperOre, ItemType.TinOre, ItemType.GoldOre, ItemType.SilverOre, ItemType.RawStone }) warehouse.Inventory[(int)item] = 70;
            warehouse.AgriculturalStock[(int)AgriculturalProduct.Hide] = 60; warehouse.AgriculturalStock[(int)AgriculturalProduct.Fiber] = 90;
            Dictionary<BuildingType, BuildingData> shops = new Dictionary<BuildingType, BuildingData>(); int place = 0;
            foreach (BuildingType type in IndustryCatalog.All.Select(r => r.Building).Distinct()) { shops[type] = Add(e, type, 18 + (place % 4) * 4, 16 + (place / 4) * 5); place++; }
            int batches = 0;
            Action<IndustryRecipe> make = null;
            make = r =>
            {
                foreach (ItemAmount input in r.Inputs)
                {
                    IndustryRecipe source = IndustryCatalog.All.FirstOrDefault(candidate => candidate.Output == input.Item);
                    while (w.GetSettlementStock(u.SettlementId, input.Item) + .0001f < input.Amount)
                    { Need(source != null, "Raw fixture shortage " + input.Item); make(source); }
                }
                // Need resets are declared fixture assistance between batches; inputs and worker position are never teleported.
                u.Hunger = u.Thirst = u.Fatigue = u.SocialNeed = 0;
                BuildingData b = shops[r.Building]; int old = b.CompletedIndustryBatches;
                float before = Material(w, r.Output); Need(e.TryStartProduction(u.Id, b.Id, r.Id), "Connected recipe start " + r.Id); Finish(e, u, b, old); Near(before + r.OutputAmount, Material(w, r.Output), "Connected output " + r.Id); batches++;
            };
            foreach (IndustryRecipe r in IndustryCatalog.All) make(r);
            Need(batches > IndustryCatalog.All.Length, "No upstream recipe repeated for real downstream demand");
            foreach (IndustryRecipe r in IndustryCatalog.All) Need(w.ProducedItems[(int)r.Output] >= r.OutputAmount, "Unreached chain " + r.Id);
            Need(w.Units.All(v => v.Profession != UnitProfession.Guard), "Factory created troops"); Valid(w);
        }
        public static void ToolsAndClothesRequirePhysicalVisitAndConsumeOnlyOneObject()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Add(e, BuildingType.Smithy, 26, 10); UnitData u = w.Units[0]; b.Inventory[(int)ItemType.IronTools] = 1; b.Inventory[(int)ItemType.LinenClothes] = 1;
            Need(e.TryEquipManufacturedGoods(u.Id, b.Id, ItemType.IronTools), "Equip tool order rejected"); Need(u.WorkTool == ItemType.None, "Remote tool grant"); Near(1, b.Inventory[(int)ItemType.IronTools], "Remote equip debit");
            for (int i = 0; i < 1000 && u.WorkTool == ItemType.None; i++) WorkerStep(e, u);
            Need(u.WorkTool == ItemType.IronTools, "Physical tool not equipped"); Near(0, b.Inventory[(int)ItemType.IronTools], "Tool inventory duplicated"); Near(1, Material(w, ItemType.IronTools), "Tool uniqueness"); Need(IndustryCatalog.ToolMultiplier(u.WorkTool) > IndustryCatalog.ToolMultiplier(ItemType.None), "Tool has no effect");
            Need(!e.TryEquipManufacturedGoods(u.Id, b.Id, ItemType.IronTools), "Repeated tool grant"); Need(e.TryEquipManufacturedGoods(u.Id, b.Id, ItemType.LinenClothes), "Clothes rejected"); WorkerStep(e, u);
            Need(u.Clothes == ItemType.LinenClothes, "Clothes not equipped"); Near(1, Material(w, ItemType.LinenClothes), "Clothes uniqueness");
            IndustryRecipe next = IndustryCatalog.Get(IndustryRecipeType.IronTools); Supply(b, next); Need(e.TryStartProduction(u.Id, b.Id, next.Id), "Equipped worker could not work"); u.Fatigue = 0;
            SimulationEngine plain = new SimulationEngine(Clone(w)); UnitData plainUnit = plain.World.GetUnit(u.Id); plainUnit.WorkTool = plainUnit.Clothes = ItemType.None;
            for (int i = 0; i < 10; i++) { WorkerStep(e, u); WorkerStep(plain, plainUnit); }
            Need(b.IndustryProgress > plain.World.GetBuilding(b.Id).IndustryProgress * 1.25f, "Equipped tool does not speed actual production"); Need(u.Fatigue < plainUnit.Fatigue, "Clothing does not reduce actual fatigue"); Valid(w); Valid(plain.World);
        }
        public static void ChildCannotEquipToolAndDeadOwnerDropsManufacturedGoods()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData b = Add(e, BuildingType.Smithy); UnitData u = w.Units[0]; Here(w, u, b); b.Inventory[(int)ItemType.IronTools] = 1; b.Inventory[(int)ItemType.LinenClothes] = 1;
            u.Age = 12; Need(!e.TryEquipManufacturedGoods(u.Id, b.Id, ItemType.IronTools), "Child heavy tool"); u.Age = 25;
            Need(e.TryEquipManufacturedGoods(u.Id, b.Id, ItemType.IronTools), "Adult tool rejected");
            for (int i = 0; i < 100 && u.WorkTool == ItemType.None; i++) WorkerStep(e, u);
            Need(u.WorkTool == ItemType.IronTools, "Adult did not enter and receive tool");
            Need(e.TryEquipManufacturedGoods(u.Id, b.Id, ItemType.LinenClothes), "Adult clothes rejected");
            for (int i = 0; i < 100 && u.Clothes == ItemType.None; i++) WorkerStep(e, u);
            Need(u.Clothes == ItemType.LinenClothes, "Adult did not receive clothes");
            u.Health = 0; Call(e, "RemoveDead"); Near(1, Material(w, ItemType.IronTools), "Death lost tool"); Near(1, Material(w, ItemType.LinenClothes), "Death lost clothes");
            Near(1, w.GroundStocks.Where(g => g.Item == ItemType.IronTools).Sum(g => g.Amount), "No local dropped tool"); Call(e, "RemoveDead"); Near(1, Material(w, ItemType.IronTools), "Second cleanup duplicates equipment"); Valid(w);
        }
        public static void FurnitureIsCarriedToHomeAndSurvivesCancellationAccounting()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; BuildingData source = Add(e, BuildingType.Carpentry, 22, 10), house = Add(e, BuildingType.House, 27, 18); UnitData u = w.Units[0]; source.Inventory[(int)ItemType.Furniture] = 1; u.HomeBuildingId = house.Id; float comfortBefore = w.GetHousingComfort(u);
            Need(e.TryInstallFurniture(u.Id, house.Id), "Furniture order rejected"); Near(0, house.FurnitureCount, "Remote furnishing"); bool carried = false;
            for (int i = 0; i < 1500 && house.FurnitureCount == 0; i++) { WorkerStep(e, u); carried |= u.CargoKind == ItemType.Furniture && u.CargoAmount == 1; }
            Need(carried && house.FurnitureCount == 1, "Missing physical furniture installation"); Near(1, Material(w, ItemType.Furniture), "Furniture duplicated"); Near(0, source.Inventory[(int)ItemType.Furniture], "Furniture source not consumed"); Need(w.GetHousingComfort(u) > comfortBefore, "Installed furniture has no housing effect");
            house.Health = 0; Call(e, "DestroyInvalidBuildings"); Near(1, Material(w, ItemType.Furniture), "Destroyed furnishing not accounted"); Valid(w);
        }
        public static void TwoCollectorsAtAnExhaustedPileLeaveNoZeroAmountTypedCargo()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; UnitData a = w.Units[0], b = w.Units[1];
            w.GroundStocks.Add(new GroundStockData { Item = ItemType.RawStone, Amount = 1, X = 10.5f, Y = 10.5f });
            foreach (UnitData u in new[] {a,b}) { u.X = u.Y = 10.5f; u.Task = UnitTask.Haul; u.GroundStockIndex = 0; u.SourceBuildingId = u.TargetBuildingId = 0; u.Path.Clear(); u.PathCursor = 0; }
            Call(e, "PickUpGroundStock", a); Call(e, "PickUpGroundStock", b);
            Near(1, a.CargoAmount, "First collector did not collect"); Near(0, b.CargoAmount, "Second collector duplicated pile"); Need(b.CargoKind == ItemType.None, "Depleted pile created zero-amount typed cargo"); Valid(w);
        }
        public static void RouteCutAfterPickupPreservesThePhysicalCargo()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; IndustryRecipe r = IndustryCatalog.Get(IndustryRecipeType.IronIngot); BuildingData b = Add(e, r.Building, 25, 10); UnitData u = w.Units[0]; Here(w, u, b); Supply(Hall(w), r);
            Need(e.TryStartProduction(u.Id, b.Id, r.Id), "Start"); for (int i = 0; i < 1000 && u.CargoAmount == 0; i++) WorkerStep(e, u); Need(u.CargoAmount > 0, "No pickup before route cut");
            for (int y = 0; y < w.Height; y++) e.Paint(TerrainType.DeepWater, 17, y, 0);
            for (int i = 0; i < 100 && u.Task == UnitTask.Produce; i++) WorkerStep(e, u);
            Near(2, Material(w, ItemType.IronOre), "Broken route lost ore"); Near(1, Material(w, ItemType.Charcoal), "Broken route lost fuel"); Near(0, Material(w, ItemType.IronIngot), "Broken route made output"); Valid(w);
        }
        public static void PrivateAndTaxLotsAreNotFreePublicMintInputs()
        {
            SimulationEngine e = Fixture(); WorldState w = e.World; IndustryRecipe r = IndustryCatalog.Get(IndustryRecipeType.GoldCoin); BuildingData b = Add(e, r.Building); UnitData u = w.Units[0]; Here(w, u, b);
            b.TaxStocks[(int)ItemType.GoldIngot] = 1; b.PersonalStocks.Add(new OwnedGoodsData { OwnerId = w.Units[1].Id, Item = ItemType.Charcoal, Amount = 1 });
            Need(!e.TryStartProduction(u.Id, b.Id, r.Id), "Mint appropriated private/tax stock as public"); Near(1, b.TaxStocks[(int)ItemType.GoldIngot], "Tax disappeared"); Near(1, b.PersonalStocks[0].Amount, "Private fuel disappeared"); Near(0, w.MintIssued.Sum(), "Minted without public inputs"); Valid(w);
        }
        public static void NewWorkshopNeedsResearchMaterialsAndPaidConstructionLedger()
        {
            SimulationEngine e = Fixture(false); WorldState w = e.World; SettlementData s = w.Settlements[0]; BuildingData source = Hall(w);
            s.Wood = 8; source.Inventory[(int)ItemType.RawStone] = 3;
            Need(!e.TryBuildInfrastructure(s.Id, BuildingType.Smelter, 15, 15), "Unresearched smelter"); Near(8, s.Wood, "Rejected construction debit");
            foreach (TechnologyDefinition t in TechnologyCatalog.All) s.UnlockedTechnologies.Add(t.Id); source.Inventory[(int)ItemType.RawStone] = 2;
            Need(!e.TryBuildInfrastructure(s.Id, BuildingType.Smelter, 15, 15), "Smelter without enough stone"); Near(8, s.Wood, "Partial construction spend"); source.Inventory[(int)ItemType.RawStone] = 3;
            Need(e.TryBuildInfrastructure(s.Id, BuildingType.Smelter, 15, 15), "Valid smelter failed"); BuildingData made = w.Buildings.First(b => b.Type == BuildingType.Smelter);
            Near(0, s.Wood, "Wood construction cost"); Near(0, source.Inventory[(int)ItemType.RawStone], "Stone construction cost"); Near(8, made.PaidConstructionMaterials[(int)ItemType.Plank], "Missing actual plank invoice"); Near(3, made.PaidConstructionMaterials[(int)ItemType.RawStone], "Missing actual stone invoice"); Valid(w);
        }
        public static void CompleteLocalInputsOutrankAnUnfulfilledRemoteClaim()
        {
            foreach (IndustryRecipeType kind in new[] {IndustryRecipeType.IronIngot, IndustryRecipeType.Leather})
            {
                SimulationEngine e = Fixture(); WorldState w = e.World; IndustryRecipe r = IndustryCatalog.Get(kind); BuildingData a = Add(e, r.Building, 22, 9), b = Add(e, r.Building, 22, 14), source = Hall(w); UnitData u = w.Units[0], v = w.Units[1]; Here(w, u, a);Here(w, v, b);Supply(source,r);Supply(b,r);
                Need(e.TryStartProduction(u.Id,a.Id,r.Id),"Remote claimant start");
                // Another legitimate consumer exhausts the unreserved source; the second workshop already owns a complete local batch.
                foreach(ItemAmount input in r.Inputs){source.Inventory[(int)input.Item]-=input.Amount;w.LostResources[(int)input.Item]+=input.Amount;}
                foreach(AgriculturalAmount input in r.Agriculture){source.AgriculturalStock[(int)input.Product]-=input.Amount;w.LostAgriculture[(int)input.Product]+=input.Amount;}
                Need(e.TryStartProduction(v.Id,b.Id,r.Id),"Complete local batch blocked by pending remote claim "+kind);Finish(e,v,b);Near(r.OutputAmount,b.Inventory[(int)r.Output],"Local priority output");
            }
        }
        public static void AbandonedUnreachableRecipeCanBeReplannedWithoutDeletingInputs()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,BuildingType.Smelter);UnitData u=w.Units[0];Here(w, u, b);IndustryRecipe old=IndustryCatalog.Get(IndustryRecipeType.IronIngot);Supply(b,old);
            Need(e.TryStartProduction(u.Id,b.Id,old.Id),"Old order start");for(int i=0;i<100&&b.IndustryProgress<=0;i++)WorkerStep(e,u);Need(b.IndustryProgress>0,"No interrupted work");Call(e,"ChangeTask",u,UnitTask.Idle);
            b.Inventory[(int)ItemType.Charcoal]-=1;w.LostResources[(int)ItemType.Charcoal]+=1;b.Inventory[(int)ItemType.Log]=2;
            Need(e.GetAvailableProductionRecipe(u.Id,b.Id)==IndustryRecipeType.Charcoal,"Automatic selector cannot recover blocked smelter");Need(e.TryStartProduction(u.Id,b.Id,IndustryRecipeType.Charcoal),"Cannot replan abandoned order");Near(0,b.IndustryProgress,"Old progress leaked into different recipe");Near(2,b.Inventory[(int)ItemType.IronOre],"Replanning erased ore");Finish(e,u,b);Near(3,b.Inventory[(int)ItemType.Charcoal],"Recovery did not make fuel");Near(2,b.Inventory[(int)ItemType.IronOre],"Recovery spent old ore");Valid(w);
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class ProductionTests
    {
        [SetUp] public void Serializer() { ProductionScenarios.Clone = w => JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)); ProductionScenarios.Snapshot = w => JsonUtility.ToJson(w); }
        [Test] public void EveryRecipe() { ProductionScenarios.EveryRecipeDebitsExactlyOneBatchAndProducesPhysicalOutput(); }
        [Test] public void DistantPhysicalInputs() { ProductionScenarios.DistantOreAndFuelTravelBeforeAnyIngotExists(); }
        [Test] public void MixedCargo() { ProductionScenarios.MixedHideAndWoodTravelThroughDistinctLedgers(); }
        [Test] public void InsufficientRecipe() { ProductionScenarios.MissingInputCannotMintAnOutputOrPartiallyConsumeRecipe(); }
        [Test] public void OutputCapacity() { ProductionScenarios.FullOutputStoragePreservesInputsUntilRoomExists(); }
        [Test] public void ResearchAndChildGates() { ProductionScenarios.ResearchAgeWorkplaceAndForeignSettlementAreHardGates(); }
        [Test] public void InaccessibleSource() { ProductionScenarios.UnreachableSourceCannotBeDebitedOrTeleportToWorkshop(); }
        [Test] public void OneWorkerSlot() { ProductionScenarios.OneWorkplaceCannotHaveTwoWorkersOrStealAnActiveOrder(); }
        [Test] public void SharedScarceInputs() { ProductionScenarios.TwoWorkshopsSharingLastInputsCannotDuplicateGoods(); }
        [Test] public void CancelConservation() { ProductionScenarios.CancellingAfterPickupKeepsCargoAndReleasesTheWorkplace(); }
        [Test] public void DeathAndDestruction() { ProductionScenarios.DeathAndDestroyedWorkshopAccountForInputsAndReleaseLinks(); }
        [Test] public void SaveContinuation() { ProductionScenarios.MidHaulAndMidWorkSaveContinueIdentically(); }
        [Test] public void LegacyArrayMigration() { ProductionScenarios.LegacyMaterialArraysAndPaidInvoicesExpandWithoutLossOrRng(); }
        [Test] public void CorruptData() { ProductionScenarios.ForgedProgressNanAndCoinIssuanceAreRejected(); }
        [Test] public void SiegeTechnologyAndGoods() { ProductionScenarios.SiegeGoodsNeedResearchAndCarpenterButNeverCreateWarriors(); }
        [Test] public void ConnectedProductionChains() { ProductionScenarios.ConnectedChainsUseEarlierOutputsWithoutIntermediateGrants(); }
        [Test] public void PhysicalEquipmentConsumption() { ProductionScenarios.ToolsAndClothesRequirePhysicalVisitAndConsumeOnlyOneObject(); }
        [Test] public void EquipmentAgeAndDeath() { ProductionScenarios.ChildCannotEquipToolAndDeadOwnerDropsManufacturedGoods(); }
        [Test] public void PhysicalFurnitureInstallation() { ProductionScenarios.FurnitureIsCarriedToHomeAndSurvivesCancellationAccounting(); }
        [Test] public void EmptySharedPile() { ProductionScenarios.TwoCollectorsAtAnExhaustedPileLeaveNoZeroAmountTypedCargo(); }
        [Test] public void BrokenReturnRoute() { ProductionScenarios.RouteCutAfterPickupPreservesThePhysicalCargo(); }
        [Test] public void OwnershipAtMint() { ProductionScenarios.PrivateAndTaxLotsAreNotFreePublicMintInputs(); }
        [Test] public void ActualWorkshopConstruction() { ProductionScenarios.NewWorkshopNeedsResearchMaterialsAndPaidConstructionLedger(); }
        [Test] public void CompleteLocalReservationPriority() { ProductionScenarios.CompleteLocalInputsOutrankAnUnfulfilledRemoteClaim(); }
        [Test] public void AbandonedRecipeRecovery() { ProductionScenarios.AbandonedUnreachableRecipeCanBeReplannedWithoutDeletingInputs(); }
    }
#endif
}
