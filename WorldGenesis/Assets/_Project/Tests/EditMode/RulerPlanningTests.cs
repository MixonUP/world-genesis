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
    public static class RulerPlanningScenarios
    {
        public static Func<WorldState, WorldState> Clone;
        public static Func<WorldState, string> Snapshot;
        private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static void Near(float expected, float actual, string message) { Need(Math.Abs(expected - actual) < .001f, message + ": " + actual); }
        private static object Call(SimulationEngine e, string method, params object[] args) { return typeof(SimulationEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(e, args); }
        private static SimulationEngine Flat(bool town = true)
        {
            WorldState w = WorldGenerator.Generate(913, 32, 32);
            for (int i = 0; i < w.Cells.Length; i++)
            { TileData t = w.Cells[i]; t.Terrain = TerrainType.Grass; t.Height = .5f; t.Moisture = .6f; t.Vegetation = .5f; t.River = false; t.WaterKind = WaterKind.None; t.WaterDepth = t.CurrentStrength = 0; t.ChannelWidth = 0; w.Cells[i] = t; }
            ResourceCatalog.Generate(w); w.NaturalWildlife = false; w.Laws.Aging = w.Laws.Births = w.Laws.Warfare = false;
            SimulationEngine e = new SimulationEngine(w); w.Units.Clear();
            for (int i = 0; i < w.Infrastructure.Length; i++) w.Infrastructure[i] = default(InfrastructureCell);
            for (int i = 0; i < w.ResourceNodes.Length; i++) w.ResourceNodes[i] = default(ResourceNodeData);
            if (town)
            {
                e.Spawn(SpeciesType.Human, 8, 8, 4); SettlementData s = w.Settlements[0]; s.Food = 200; s.Wood = 0; s.NextPolicyReviewTick = s.NextCaravanTick = long.MaxValue;
                foreach (UnitData u in w.Units) { u.Age = 25; u.Hunger = u.Thirst = u.Fatigue = u.SocialNeed = .1f; u.DecisionTimer = 10000; u.Path.Clear(); u.PathCursor = 0; }
            }
            Need(w.Validate(out string error), error); return e;
        }
        private static BuildingData Add(SimulationEngine e, BuildingType type, int x, int y)
        { Call(e, "AddBuilding", e.World.Settlements[0], x, y, type); return e.World.Buildings.Last(); }
        private static void Fresh(WorldState w, int x, int y)
        { int i = w.Index(x, y); w.Cells[i].Terrain = TerrainType.ShallowWater; w.Cells[i].WaterKind = WaterKind.Lake; w.Cells[i].WaterDepth = 1; w.Cells[i].CurrentStrength = 0; w.Cells[i].ChannelWidth = 1; w.Cells[i].Vegetation = 0; w.NavigationRevision++; }
        public static void SeatPrefersReachableElevationNearFoodAndFreshWaterWithoutRng()
        {
            SimulationEngine e = Flat(false); WorldState w = e.World; Fresh(w, 8, 5); w.Cells[w.Index(12, 8)].Height = .95f; w.Cells[w.Index(12, 8)].Terrain = TerrainType.Highlands;
            uint rng = w.RngState; int next = w.NextId; int seat = (int)Call(e, "ChooseSettlementSeat", 8, 8);
            Need(seat == w.Index(12, 8), "higher suitable seat not chosen"); Need(w.RngState == rng && w.NextId == next, "seat search consumes RNG/IDs");
        }
        public static void SeatNeverCrossesUnreachableRingOrLeavesSixCellSearch()
        {
            SimulationEngine e = Flat(false); WorldState w = e.World; Fresh(w, 7, 5); int peak = w.Index(12, 8); w.Cells[peak].Height = 1;
            foreach (int cell in new[] { peak - 1, peak + 1, peak - w.Width, peak + w.Width }) w.Cells[cell].Terrain = TerrainType.Mountain;
            w.Cells[w.Index(16, 8)].Height = 1; w.Cells[w.Index(9, 8)].Height = .7f; w.NavigationRevision++;
            int seat = (int)Call(e, "ChooseSettlementSeat", 8, 8);
            Need(seat == w.Index(9, 8), "unreachable or distant peak chosen");
        }
        public static void SpawnActuallyFoundHallOnTheChosenReachableHighland()
        {
            SimulationEngine e = Flat(false); WorldState w = e.World; Fresh(w, 8, 5); int peak = w.Index(12, 8); w.Cells[peak].Terrain = TerrainType.Highlands; w.Cells[peak].Height = .95f;
            e.Spawn(SpeciesType.Human, 8, 8, 4); SettlementData s = w.Settlements.Single(); BuildingData hall = w.Buildings.Single(b => b.Type == BuildingType.Hall);
            Need(s.X == 12 && s.Y == 8 && hall.X == 12 && hall.Y == 8 && hall.SettlementId == s.Id, "FoundSettlement ignored chosen seat"); Need(w.Cells[peak].BuildingId == hall.Id && w.Units.Count(u => u.SettlementId == s.Id) == 4, "founding references incorrect"); Need(w.Validate(out string error), error);
        }
        public static void DryStartRetainsOriginalLocationAndBasicWellEscape()
        {
            SimulationEngine e = Flat(false); e.World.Cells[e.World.Index(11, 8)].Height = 1;
            Need((int)Call(e, "ChooseSettlementSeat", 8, 8) == e.World.Index(8, 8), "dry map relocation ignored water");
        }
        public static void ShallowFordFoundingUsesDryBankAndRejectsWaterOnlyStart()
        {
            SimulationEngine e = Flat(false); WorldState w = e.World; Fresh(w, 8, 8); w.Cells[w.Index(8, 8)].WaterDepth = .2f;
            Need(w.IsWalkable(8, 8), "fixture ford blocked"); e.Spawn(SpeciesType.Human, 8, 8, 1); BuildingData hall = w.Buildings.Single(b => b.Type == BuildingType.Hall);
            Need(w.Cells[w.Index(hall.X, hall.Y)].WaterDepth == 0, "hall founded in ford"); Need(w.Validate(out string error), error);
            e = Flat(false); w = e.World;
            for (int i = 0; i < w.Cells.Length; i++) { w.Cells[i].Terrain = TerrainType.ShallowWater; w.Cells[i].WaterKind = WaterKind.Lake; w.Cells[i].WaterDepth = .2f; w.Cells[i].ChannelWidth = 1; }
            Need((int)Call(e, "ChooseSettlementSeat", 8, 8) == -1, "water-only founding accepted");
        }
        public static void HallLastFarmAndWellAreProtectedWithoutMutation()
        {
            SimulationEngine e = Flat(); WorldState w = e.World; SettlementData s = w.Settlements[0]; BuildingData well = Add(e, BuildingType.Well, 12, 12);
            string before = Snapshot(w);
            foreach (BuildingData b in w.Buildings.ToArray()) Need(!e.TryDemolishBuilding(s.Id, b.Id, "Проверка защиты"), "protected structure removed");
            Need(Snapshot(w) == before, "blocked demolition changed state");
        }
        public static void DemolitionPreservesPublicPrivateTaxAndAgriculturalGoodsAtSite()
        {
            SimulationEngine e = Flat(); WorldState w = e.World; SettlementData s = w.Settlements[0]; UnitData u = w.Units[0]; BuildingData b = Add(e, BuildingType.House, 12, 12);
            b.Inventory[(int)ItemType.Salt] = 3; b.TaxStocks[(int)ItemType.Salt] = .5f; b.PersonalStocks.Add(new OwnedGoodsData { OwnerId = u.Id, Item = ItemType.CopperOre, Amount = .75f }); b.AgriculturalStock[(int)AgriculturalProduct.Fiber] = 2;
            b.OwnerId = u.Id; b.Ownership = BuildingOwnership.Private; u.HomeBuildingId = b.Id; u.Trust = u.Mood = .8f; u.CargoKind = ItemType.IronOre; u.CargoAmount = .25f;
            Need(e.TryDemolishBuilding(s.Id, b.Id, "Освободить площадку"), "safe demolition rejected"); Need(w.GetBuilding(b.Id) == null && w.Cells[w.Index(12, 12)].BuildingId == 0, "building survived");
            Near(3.5f, w.GroundStocks.Where(g => g.Item == ItemType.Salt && g.OwnerId == 0).Sum(g => g.Amount), "public/tax salvage");
            Need(w.GroundStocks.Single(g => g.Item == ItemType.CopperOre).OwnerId == u.Id, "private title lost"); Near(.75f, w.GroundStocks.Single(g => g.Item == ItemType.CopperOre).Amount, "private amount");
            Need(w.GroundStocks.All(g => g.X == 12.5f && g.Y == 12.5f), "teleported salvage"); Near(2, w.AgriculturalDrops.Single().Amount, "farm goods lost"); Need(w.AgriculturalDrops.Single().OwnerSettlementId == s.Id, "farm goods town lost");
            Near(0, w.LostResources.Sum() + w.LostAgriculture.Sum(), "salvage double-counted as destroyed"); Near(.25f, u.CargoAmount, "worker cargo lost"); Need(u.HomeBuildingId != b.Id && u.Trust < .8f && u.Mood < .8f, "home/stress not updated");
            Need(!e.TryDemolishBuilding(s.Id, b.Id, "Ещё раз"), "duplicate demolition accepted"); Need(w.Validate(out string error), error);
        }
        public static void SecondFarmHarvestSurvivesAndUnsealedBrewWaterBlocksDemolition()
        {
            SimulationEngine e = Flat(); WorldState w = e.World; SettlementData s = w.Settlements[0]; BuildingData farm = Add(e, BuildingType.Farm, 12, 12); farm.FarmCrop = CropType.Flax; farm.HarvestReady = 3;
            Need(e.TryDemolishBuilding(s.Id, farm.Id, "Перенести второе поле"), "second farm rejected"); Near(3, w.AgriculturalDrops.Where(d => d.Product == AgriculturalProduct.Fiber).Sum(d => d.Amount), "ready crop lost");
            w.Tick = 300; BuildingData brewery = Add(e, BuildingType.Brewery, 15, 15); brewery.BrewWaterStored = 1; string before = Snapshot(w);
            Need(!e.TryDemolishBuilding(s.Id, brewery.Id, "Перестройка"), "unsealed water erased"); Need(Snapshot(w) == before, "blocked water demolition mutated");
        }
        public static void PrudentRoadClearancePlansRealWorkAndDoesNotCreateRoadOrMaterial()
        {
            SimulationEngine e = Flat(); WorldState w = e.World; SettlementData s = w.Settlements[0]; UnitData r = w.GetUnit(s.RulerId); r.Prudence = 1; r.Ambition = .2f;
            Add(e, BuildingType.Sawmill, 13, 10); BuildingData b = Add(e, BuildingType.House, 12, 12); s.UnlockedTechnologies.Add("m01_forestry"); s.UnlockedTechnologies.Add("l06_paths");
            w.Infrastructure[w.Index(11, 12)].RoadLevel = w.Infrastructure[w.Index(13, 12)].RoadLevel = 2; w.Infrastructure[w.Index(11, 12)].RoadOwnerId = w.Infrastructure[w.Index(13, 12)].RoadOwnerId = s.Id;
            s.Wood = .25f; Call(e, "UpdateRulerPlanning");
            Need(w.GetBuilding(b.Id) == null && w.TransportProjects.Count == 1, "road gap not planned"); Need(w.Infrastructure[w.Index(12, 12)].RoadLevel == 0, "instant road"); Near(.25f, s.Wood, "planning consumed uncarried wood"); Near(0, w.TransportProjects[0].Delivered, "free delivery");
            string once = Snapshot(w); Call(e, "UpdateRulerPlanning"); Need(Snapshot(w) == once, "multiple same-tick ruler actions"); Need(w.Validate(out string error), error);
        }
        public static void AmbitiousReplacementNeedsTechnologyAndConsumesActualPlanks()
        {
            SimulationEngine e = Flat(); WorldState w = e.World; SettlementData s = w.Settlements[0]; UnitData r = w.GetUnit(s.RulerId); r.Prudence = .1f; r.Ambition = 1;
            Add(e, BuildingType.Sawmill, 13, 10); BuildingData b = Add(e, BuildingType.House, 12, 12); s.Wood = 18;
            Call(e, "UpdateRulerPlanning"); Need(w.GetBuilding(b.Id) != null, "unresearched prestige tower"); w.Tick = 300;
            s.UnlockedTechnologies.Add("m01_forestry"); s.UnlockedTechnologies.Add("m02_carpentry"); s.UnlockedTechnologies.Add("d10_wooden_defenses");
            Call(e, "UpdateRulerPlanning"); Need(w.GetBuilding(b.Id) == null && w.Buildings.Any(z => z.Type == BuildingType.Tower && z.X == 12 && z.Y == 12), "prestige project absent"); Near(8, s.Wood, "tower not paid"); Need(w.Validate(out string error), error);
        }
        public static void PrudentSchoolReplacementRequiresResearchAndPreservesHousing()
        {
            SimulationEngine e = Flat(); WorldState w = e.World; SettlementData s = w.Settlements[0]; UnitData r = w.GetUnit(s.RulerId); r.Prudence = 1; r.Ambition = 0;
            Add(e, BuildingType.Sawmill, 13, 10); BuildingData b = Add(e, BuildingType.House, 12, 12); s.Wood = 8;
            Call(e, "UpdateRulerPlanning"); Need(w.GetBuilding(b.Id) != null, "school before teaching"); w.Tick = 300;
            s.UnlockedTechnologies.Add("m01_forestry"); s.UnlockedTechnologies.Add("m02_carpentry"); s.UnlockedTechnologies.Add("k01_teaching"); Call(e, "UpdateRulerPlanning");
            Need(w.GetBuilding(b.Id) == null && w.Buildings.Any(z => z.Type == BuildingType.School && z.X == 12 && z.Y == 12), "school replacement failed"); Near(0, s.Wood, "school not paid"); Need(s.Housing >= s.Population, "prudent school made people homeless"); Need(w.Validate(out string error), error);
        }
        public static void ActiveHaulerKeepsEveryCargoChannelWhenDestinationIsDemolished()
        {
            SimulationEngine e = Flat(); WorldState w = e.World; SettlementData s = w.Settlements[0]; UnitData u = w.Units[0]; BuildingData b = Add(e, BuildingType.Warehouse, 12, 12);
            u.CargoKind = ItemType.Salt; u.CargoAmount = .25f; u.CargoOwnerId = u.Id; u.CarriedFood = .1f; u.CarriedWood = .1f; u.HarvestProduct = AgriculturalProduct.Fiber; u.HarvestAmount = .1f; u.CarriedWater = .1f;
            u.Task = UnitTask.Haul; u.SourceBuildingId = w.Buildings.First(z => z.Type == BuildingType.Hall).Id; u.TargetBuildingId = b.Id; Call(e, "SetTarget", u, b.X, b.Y);
            Need(e.TryDemolishBuilding(s.Id, b.Id, "Разбор склада с рабочим в пути"), "demolition failed");
            Near(.25f, u.CargoAmount, "material cargo"); Need(u.CargoKind == ItemType.Salt && u.CargoOwnerId == u.Id, "cargo title changed"); Near(.1f, u.CarriedFood, "food cargo"); Near(.1f, u.CarriedWood, "legacy plank cargo"); Near(.1f, u.HarvestAmount, "agricultural cargo"); Need(u.HarvestProduct == AgriculturalProduct.Fiber, "crop type lost"); Near(.1f, u.CarriedWater, "carried water lost");
            Need(u.Task == UnitTask.Idle && u.SourceBuildingId == 0 && u.TargetBuildingId == 0 && u.Path.Count == 0, "stale demolition route"); Need(w.Validate(out string error), error);
            WorldState restored = Clone(w); new SimulationEngine(restored); Need(restored.Validate(out error), error); Near(.65f, SimulationEngine.CarriedLoad(restored.GetUnit(u.Id)), "save cargo after cancellation");
        }
        public static void PlanningCooldownAndReasonSurviveSaveAndMalformedDatesFail()
        {
            SimulationEngine e = Flat(); WorldState w = e.World; SettlementData s = w.Settlements[0]; BuildingData house = Add(e, BuildingType.House, 12, 12);
            Need(e.TryDemolishBuilding(s.Id, house.Id, "Записанное решение"), "demolition failed"); WorldState loaded = Clone(w); new SimulationEngine(loaded);
            Need(loaded.Settlements[0].NextPlanningTick == 300 && loaded.Settlements[0].LastPlanningReason == "Записанное решение", "planning save lost"); Need(loaded.Validate(out string error), error);
            loaded.Settlements[0].LastPlanningTick = loaded.Tick + 1; Need(!loaded.Validate(out _), "future action accepted");
        }
        public static void RealCombatUsesElevationBonusWithoutRngDifferenceAndCapsAtTwentyPercent()
        {
            SimulationEngine setup = Flat(); WorldState source = setup.World; setup.Spawn(SpeciesType.Wolf, 20, 20, 1); UnitData attacker = source.Units.First(u => SimulationEngine.IsCivilized(u.Species)); UnitData target = source.Units.Single(u => u.Species == SpeciesType.Wolf);
            attacker.X = 14.5f; attacker.Y = 15.5f; target.X = 15.5f; target.Y = 15.5f; attacker.AttackTimer = 0; attacker.Blessed = false; attacker.Path.Clear(); attacker.PathCursor = 0; attacker.Task = UnitTask.Idle;
            float[] heights = { .5f, .55f, .1f, .6f, 1f }; float[] expected = { 7, 7.7f, 7, 8.4f, 8.4f };
            for (int i = 0; i < heights.Length; i++)
            {
                WorldState w = Clone(source); SimulationEngine e = new SimulationEngine(w); UnitData a = w.GetUnit(attacker.Id), d = w.GetUnit(target.Id); w.Cells[w.Index(14, 15)].Height = heights[i]; w.Cells[w.Index(15, 15)].Height = .5f; uint rng = w.RngState;
                Call(e, "BuildSpatialIndex"); float health = d.Health; Call(e, "UpdateUnit", a, .1f); Near(expected[i], health - d.Health, "elevation hit " + i); Need(a.Task == UnitTask.Fight && w.RngState == rng, "combat fixture not a deterministic actual hit");
                float after = d.Health; Call(e, "UpdateUnit", a, .1f); Near(after, d.Health, "height bypassed cooldown"); Need(w.Validate(out string error), error);
            }
        }
        private static void UnlockAll(SettlementData s)
        { foreach (TechnologyDefinition t in TechnologyCatalog.All) if (!s.UnlockedTechnologies.Contains(t.Id)) s.UnlockedTechnologies.Add(t.Id); }
        private static float Ground(WorldState w, ItemType item) { return w.GroundStocks.Where(g => !g.IsFood && g.Item == item).Sum(g => g.Amount); }
        public static void FreeFoundingAndUnknownLegacyConstructionNeverMintSalvage()
        {
            SimulationEngine e = Flat(); WorldState w = e.World; BuildingData hall = w.Buildings.Single(b => b.Type == BuildingType.Hall); uint beforeFree = w.RngState; hall.Health = 0; Call(e, "DestroyInvalidBuildings"); Near(0, w.GroundStocks.Sum(g => g.Amount), "free hall refunded"); Need(w.RngState == beforeFree && hall.ConstructionRecoveryPercent == 0, "free construction consumed a roll");
            BuildingData b = Add(e, BuildingType.House, 12, 12); b.Level = 5; b.PaidConstructionMaterials = null; b.ConstructionLedgerVersion = 0; b.Health = 0;
            uint rng = w.RngState; int next = w.NextId; Call(e, "DestroyInvalidBuildings"); Near(0, w.GroundStocks.Sum(g => g.Amount), "legacy inferred cost"); Need(rng == w.RngState && next == w.NextId, "salvage used RNG or ID");
        }
        public static void RealConstructionAndEveryUpgradeUseOneRecordedPercentageAfterSave()
        {
            SimulationEngine e = Flat(); WorldState w = e.World; SettlementData s = w.Settlements[0]; UnlockAll(s); s.Wood = 100; BuildingData hall = w.Buildings.Single(b => b.Type == BuildingType.Hall); hall.Inventory[(int)ItemType.Brick] = 10; hall.Inventory[(int)ItemType.StoneBlock] = 20;
            Need(e.TryBuildInfrastructure(s.Id, BuildingType.School, 12, 12), "paid school failed"); BuildingData b = w.Buildings.Single(z => z.Type == BuildingType.School);
            for (int i = 0; i < 4; i++) Need(e.TryUpgradeBuilding(b.Id), "paid upgrade failed " + i);
            Near(68, b.PaidConstructionMaterials[(int)ItemType.Plank], "construction plus upgrades invoice"); Near(4, b.PaidConstructionMaterials[(int)ItemType.Brick], "brick invoice"); Near(14, b.PaidConstructionMaterials[(int)ItemType.StoneBlock], "stone invoice");
            w = Clone(w); e = new SimulationEngine(w); b = w.GetBuilding(b.Id); b.Health = 0; Call(e, "DestroyInvalidBuildings");
            float recovery = b.ConstructionRecoveryPercent / 100f; Need(recovery >= .1f && recovery <= .5f, "recorded percentage out of range"); Near(68 * recovery, Ground(w, ItemType.Plank), "plank recovery"); Near(4 * recovery, Ground(w, ItemType.Brick), "brick recovery"); Near(14 * recovery, Ground(w, ItemType.StoneBlock), "stone recovery"); Need(w.GroundStocks.All(g => g.X == 12.5f && g.Y == 12.5f), "salvage teleported"); Need(w.Validate(out string error), error);
        }
        public static void CatastrophicStorageLossAndBuildingSalvageUseSeparateLedgersOnce()
        {
            SimulationEngine e = Flat(); WorldState w = e.World; SettlementData s = w.Settlements[0]; UnlockAll(s); s.Wood = 8; Need(e.TryBuildInfrastructure(s.Id, BuildingType.Warehouse, 12, 12), "warehouse failed"); BuildingData b = w.Buildings.Single(z => z.Type == BuildingType.Warehouse);
            b.Inventory[(int)ItemType.Salt] = 3; b.AgriculturalStock[(int)AgriculturalProduct.Fiber] = 2; b.Health = 0; Call(e, "DestroyInvalidBuildings");
            Near(8 * b.ConstructionRecoveryPercent / 100f, Ground(w, ItemType.Plank), "building invoice salvage"); Near(0, Ground(w, ItemType.Salt), "catastrophe fabricated intact contents"); Near(3, w.LostResources[(int)ItemType.Salt], "stored goods missing loss"); Near(2, w.LostAgriculture[(int)AgriculturalProduct.Fiber], "crop missing loss");
            string once = Snapshot(w); Call(e, "SalvageConstructionMaterials", b); Call(e, "DestroyInvalidBuildings"); Need(Snapshot(w) == once, "second destruction doubled salvage"); Need(w.Validate(out string error), error);
        }
        public static void PlannedDemolitionPreservesContentsPlusRolledConstructionAndRejectsBadInvoice()
        {
            SimulationEngine e = Flat(); WorldState w = e.World; SettlementData s = w.Settlements[0]; UnlockAll(s); s.Wood = 8; Need(e.TryBuildInfrastructure(s.Id, BuildingType.Warehouse, 12, 12), "warehouse failed"); BuildingData b = w.Buildings.Single(z => z.Type == BuildingType.Warehouse); b.Inventory[(int)ItemType.Plank] = 2;
            Need(e.TryDemolishBuilding(s.Id, b.Id, "Плановая разборка"), "planned demolition failed"); Near(2 + 8 * b.ConstructionRecoveryPercent / 100f, Ground(w, ItemType.Plank), "contents plus rolled cost"); Near(0, w.LostResources[(int)ItemType.Plank], "planned goods counted lost");
            BuildingData remaining = w.Buildings[0]; remaining.ConstructionLedgerVersion = 1; remaining.PaidConstructionMaterials[(int)ItemType.Plank] = float.NaN; Need(!w.Validate(out _), "nonfinite invoice accepted"); remaining.PaidConstructionMaterials[(int)ItemType.Plank] = 1; remaining.ConstructionLedgerVersion = 0; Need(!w.Validate(out _), "legacy synthetic invoice accepted");
        }
        private static SimulationEngine PaidWell()
        {
            SimulationEngine e = Flat(); SettlementData s = e.World.Settlements[0]; s.Wood = 8; e.World.Buildings.Single(b => b.Type == BuildingType.Hall).Inventory[(int)ItemType.RawStone] = 2;
            Need(e.TryBuildInfrastructure(s.Id, BuildingType.Well, 12, 12), "paid well failed"); return e;
        }
        public static void RecoverySeedMatrixIncludesBothBoundsAndUsesExactlyOneRngDraw()
        {
            WorldState source = PaidWell().World; bool sawTen = false, sawFifty = false;
            for (int seed = 1; seed <= 512; seed++)
            {
                WorldState w = Clone(source); SimulationEngine e = new SimulationEngine(w); BuildingData b = w.Buildings.Single(z => z.Type == BuildingType.Well); w.RngState = StableRandom.Hash(seed, 17, 0, 8129); uint expectedNext = w.RngState; StableRandom.Next(ref expectedNext); b.Health = 0;
                Call(e, "DestroyInvalidBuildings"); int percent = b.ConstructionRecoveryPercent; sawTen |= percent == 10; sawFifty |= percent == 50;
                Need(percent >= 10 && percent <= 50 && w.RngState == expectedNext, "range or number of RNG draws"); Near(8 * percent / 100f, Ground(w, ItemType.Plank), "same roll for wood"); Near(2 * percent / 100f, Ground(w, ItemType.RawStone), "same roll for stone"); Need(w.Validate(out string error), error);
            }
            Need(sawTen && sawFifty, "matrix does not exercise inclusive bounds");
        }
        public static void RolledRecoveryPersistsAcrossCleanupSaveWithoutSecondDrawOrEvent()
        {
            SimulationEngine e = PaidWell(); WorldState w = e.World; BuildingData b = w.Buildings.Single(z => z.Type == BuildingType.Well); b.Health = 0; Call(e, "SalvageConstructionMaterials", b); Need(w.Validate(out string before), before);
            int percent = b.ConstructionRecoveryPercent, events = w.Events.Count; uint rng = w.RngState; float plank = Ground(w, ItemType.Plank), stone = Ground(w, ItemType.RawStone);
            w = Clone(w); e = new SimulationEngine(w); b = w.GetBuilding(b.Id); Need(b.ConstructionSalvaged && b.ConstructionRecoveryPercent == percent, "recorded roll not saved"); Call(e, "SalvageConstructionMaterials", b); Call(e, "DestroyInvalidBuildings");
            Near(plank, Ground(w, ItemType.Plank), "duplicated plank after load"); Near(stone, Ground(w, ItemType.RawStone), "duplicated stone after load"); Need(w.RngState == rng && w.Events.Count == events, "rerolled recovery or duplicate event"); Need(w.Validate(out string error), error);
        }
        public static void RecoveryValidatorRejectsPrematureOutOfRangeAndUnpaidRolls()
        {
            SimulationEngine e = PaidWell(); WorldState w = e.World; BuildingData b = w.Buildings.Single(z => z.Type == BuildingType.Well); b.ConstructionRecoveryPercent = 10; Need(!w.Validate(out _), "alive premature recovery accepted");
            b.Health = 0; b.ConstructionSalvaged = true; foreach (int invalid in new[] { -1, 0, 9, 51, 100 }) { b.ConstructionRecoveryPercent = invalid; Need(!w.Validate(out _), "invalid percent accepted " + invalid); }
            b.ConstructionRecoveryPercent = 10; Need(w.Validate(out string valid), valid); Array.Clear(b.PaidConstructionMaterials, 0, b.PaidConstructionMaterials.Length); Need(!w.Validate(out _), "unpaid roll accepted"); b.ConstructionRecoveryPercent = 0; Need(w.Validate(out valid), valid);
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class RulerPlanningTests
    {
        [SetUp] public void Serializers() { RulerPlanningScenarios.Clone = w => JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)); RulerPlanningScenarios.Snapshot = w => JsonUtility.ToJson(w); }
        [TestCase("SeatPrefersReachableElevationNearFoodAndFreshWaterWithoutRng")]
        [TestCase("SeatNeverCrossesUnreachableRingOrLeavesSixCellSearch")]
        [TestCase("SpawnActuallyFoundHallOnTheChosenReachableHighland")]
        [TestCase("DryStartRetainsOriginalLocationAndBasicWellEscape")]
        [TestCase("ShallowFordFoundingUsesDryBankAndRejectsWaterOnlyStart")]
        [TestCase("HallLastFarmAndWellAreProtectedWithoutMutation")]
        [TestCase("DemolitionPreservesPublicPrivateTaxAndAgriculturalGoodsAtSite")]
        [TestCase("SecondFarmHarvestSurvivesAndUnsealedBrewWaterBlocksDemolition")]
        [TestCase("PrudentRoadClearancePlansRealWorkAndDoesNotCreateRoadOrMaterial")]
        [TestCase("AmbitiousReplacementNeedsTechnologyAndConsumesActualPlanks")]
        [TestCase("PrudentSchoolReplacementRequiresResearchAndPreservesHousing")]
        [TestCase("ActiveHaulerKeepsEveryCargoChannelWhenDestinationIsDemolished")]
        [TestCase("PlanningCooldownAndReasonSurviveSaveAndMalformedDatesFail")]
        [TestCase("RealCombatUsesElevationBonusWithoutRngDifferenceAndCapsAtTwentyPercent")]
        [TestCase("FreeFoundingAndUnknownLegacyConstructionNeverMintSalvage")]
        [TestCase("RealConstructionAndEveryUpgradeUseOneRecordedPercentageAfterSave")]
        [TestCase("CatastrophicStorageLossAndBuildingSalvageUseSeparateLedgersOnce")]
        [TestCase("PlannedDemolitionPreservesContentsPlusRolledConstructionAndRejectsBadInvoice")]
        [TestCase("RecoverySeedMatrixIncludesBothBoundsAndUsesExactlyOneRngDraw")]
        [TestCase("RolledRecoveryPersistsAcrossCleanupSaveWithoutSecondDrawOrEvent")]
        [TestCase("RecoveryValidatorRejectsPrematureOutOfRangeAndUnpaidRolls")]
        public void Scenario(string name) { typeof(RulerPlanningScenarios).GetMethod(name).Invoke(null, null); }
    }
#endif
}
