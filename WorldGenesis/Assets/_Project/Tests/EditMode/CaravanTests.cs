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
    // Identical controlled scenarios under .NET and Unity; real serializer selected by each runner.
    public static class CaravanScenarios
    {
        public static Func<WorldState, WorldState> Clone;
        public static Func<WorldState, string> Snapshot;
    private sealed class Fixture
    { public SimulationEngine E; public WorldState W; public SettlementData A, B; public BuildingData Source, Market; }
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    private static object Call(SimulationEngine e, string name, params object[] args)
    { return typeof(SimulationEngine).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(e, args); }
    private static Fixture Make(bool paid = false, bool research = true)
    {
        WorldState w = WorldGenerator.Generate(1771, 32, 32);
        for (int i = 0; i < w.Cells.Length; i++) { w.Cells[i].Terrain = TerrainType.Grass; w.Cells[i].Height = .55f; w.Cells[i].Vegetation = .5f; w.Cells[i].River = false; }
        ResourceCatalog.Generate(w); w.Laws.Aging = false; w.Laws.Births = false; w.Laws.Warfare = true; w.Laws.VegetationGrowth = false;
        SimulationEngine e = new SimulationEngine(w); w.Units.RemoveAll(u => (int)u.Species > 3);
        e.Spawn(SpeciesType.Human, 5, 5, 4); e.Spawn(SpeciesType.Elf, 26, 26, 4);
        SettlementData a = w.Settlements[0], b = w.Settlements[1];
        foreach (SettlementData town in w.Settlements)
        {
            town.Government = GovernmentType.Feudal; town.NextElectionTick = 0; town.Food = 1000; town.Wood = 0; town.NextCaravanTick = long.MaxValue;
            if (research) foreach (TechnologyDefinition t in TechnologyCatalog.All) town.UnlockedTechnologies.Add(t.Id);
            e.SetEconomicPolicy(town.Id, paid ? EconomicPolicy.Market : EconomicPolicy.FreeDistribution, paid ? .2f : 0, ChildLaborPolicy.Forbidden, ResourceFocus.Balanced);
            town.NextPolicyReviewTick = long.MaxValue;
        }
        foreach (UnitData u in w.Units) { u.Hunger = .2f; u.Thirst = .1f; u.Fatigue = .1f; u.SocialNeed = .1f; u.Task = UnitTask.Idle; u.DecisionTimer = 0; u.Path.Clear(); u.PathCursor = 0; }
        for (int i = 0; i < w.ResourceNodes.Length; i++) w.ResourceNodes[i] = default(ResourceNodeData);
        BuildingData source = w.Buildings.First(x => x.SettlementId == a.Id && x.Type == BuildingType.Hall), market = w.Buildings.First(x => x.SettlementId == b.Id && x.Type == BuildingType.Hall);
        if (paid) foreach (UnitData u in w.Units) if (u.SettlementId == a.Id) { u.X = source.X + .5f; u.Y = source.Y + .5f; }
        source.Inventory[(int)ItemType.IronOre] = 30; market.Inventory[(int)ItemType.CopperOre] = 30;
        Require(w.Validate(out string error), error);
        return new Fixture { E = e, W = w, A = a, B = b, Source = source, Market = market };
    }
    private static double Total(WorldState w, ItemType item)
    {
        double total = w.LostResources[(int)item];
        foreach (BuildingData b in w.Buildings) { total += b.Inventory[(int)item] + b.TaxStocks[(int)item]; foreach (OwnedGoodsData p in b.PersonalStocks) if (p.Item == item) total += p.Amount; }
        foreach (UnitData u in w.Units) if (u.CargoKind == item) total += u.CargoAmount;
        foreach (GroundStockData g in w.GroundStocks) if (!g.IsFood && g.Item == item) total += g.Amount;
        return total;
    }
    private static void Step(Fixture f, int count)
    {
        for (int i = 0; i < count; i++) { f.E.Step(.1f); Require(f.W.Validate(out string error), error + " at tick " + f.W.Tick); }
    }
    private static CaravanData Start(Fixture f, int helpers = 2)
    { Require(f.E.TryStartCaravan(f.A.Id, f.B.Id, ItemType.IronOre, ItemType.CopperOre, helpers), "start failed"); return f.W.Caravans.Last(); }
    private static void Until(Fixture f, Func<bool> predicate, int maxTicks = 2000)
    { for (int i = 0; i < maxTicks && !predicate(); i++) Step(f, 1); Require(predicate(), "condition timeout: " + f.W.Caravans.Last().Phase.ToString()); }
    private static void Conserved(Fixture f)
    { Require(Math.Abs(Total(f.W, ItemType.IronOre) - 30) < .001, "iron changed"); Require(Math.Abs(Total(f.W, ItemType.CopperOre) - 30) < .001, "copper changed"); }
        public static void PhysicalCohortTradesAndReturnsConservingGoods()
        {
            Fixture f = Make(); CaravanData c = Start(f); Require(c.Members.Count == 3, "missing two helpers");
            Until(f, () => c.Phase == CaravanPhase.Completed);
            Require(f.W.CaravanExchanges.Count == 3, "each carrier must exchange once");
            Require(f.W.GetSettlementStock(f.A.Id, ItemType.CopperOre) > 2.99f, "imports not delivered home");
            Require(c.Members.All(m => f.W.GetUnit(m.UnitId).CaravanId == 0), "members not released"); Conserved(f);
        }
        public static void PaidMealsAndNaturalTaxCommitOnce()
        {
            Fixture f = Make(true); float food = f.A.Food; CaravanData c = Start(f);
            Require(Math.Abs(f.A.Food - (food - 2)) < .0001 && c.Members.Sum(x => x.HireFoodPaid) == 2, "meals unbacked or charged twice");
            Until(f, () => c.Phase == CaravanPhase.Completed);
            Require(Math.Abs(f.Market.TaxStocks[(int)ItemType.IronOre] - .6f) < .001, "tax not taken from actual payment"); Conserved(f);
        }
        public static void NoBudgetMeansNoPaidHelpers()
        {
            Fixture f = Make(true); f.A.Food = f.A.Population * 2; float food = f.A.Food; CaravanData c = Start(f);
            Require(c.Members.Count == 1 && f.A.Food == food, "hired unaffordable helpers");
        }
        public static void DistantPaidHelpersCannotEatRemoteFoodButFreeHelpersMayJoin()
        {
            foreach (bool paid in new[] { true, false })
            {
                Fixture f = Make(paid); UnitData[] residents = f.W.Units.Where(u => u.SettlementId == f.A.Id).ToArray();
                residents[0].X = f.Source.X + .5f; residents[0].Y = f.Source.Y + .5f;
                for (int i = 1; i < residents.Length; i++) { residents[i].X = 20.5f; residents[i].Y = 5.5f; }
                float food = f.A.Food, hunger = residents[1].Hunger; CaravanData c = Start(f);
                Require(c.Members.Count == (paid ? 1 : 3), "distant paid helpers hired, or free helpers incorrectly excluded");
                Require(f.A.Food == food && residents[1].Hunger == hunger && c.Members.All(m => m.HireFoodPaid == 0), "remote meal changed food or hunger");
                Require(f.W.Validate(out string error), error); Conserved(f);
            }
        }
        public static void TechnologyAndAdultGates()
        {
            Fixture noTech = Make(false, false); Require(!noTech.E.TryStartCaravan(noTech.A.Id, noTech.B.Id, ItemType.IronOre, ItemType.CopperOre), "unresearched caravan");
            Fixture f = Make(); UnitData child = f.W.Units.First(u => u.SettlementId == f.A.Id); child.Age = 13; child.Profession = UnitProfession.None;
            CaravanData c = Start(f); Require(c.Members.All(m => m.UnitId != child.Id), "child hired");
        }
        public static void CancellationReturnsOriginalCargo()
        {
            Fixture f = Make(); CaravanData c = Start(f); Until(f, () => c.Phase == CaravanPhase.Outbound); f.E.CancelCaravan(c.Id);
            Until(f, () => c.Phase == CaravanPhase.Cancelled); Require(f.W.CaravanExchanges.Count == 0, "cancel sold goods"); Conserved(f);
            Require(Math.Abs(f.W.GetSettlementStock(f.A.Id, ItemType.IronOre) - 30) < .001, "cancel failed to return export");
        }
        public static void LeaderDeathDropsCargoAndSurvivorsContinue()
        {
            Fixture f = Make(); CaravanData c = Start(f); Until(f, () => c.Phase == CaravanPhase.Outbound);
            UnitData u = f.W.GetUnit(c.Members[0].UnitId); float amount = u.CargoAmount; u.Health = 0; Step(f, 1);
            Require(f.W.GroundStocks.Any(g => g.Item == ItemType.IronOre && Math.Abs(g.Amount - amount) < .001), "death cargo not deposited");
            Require(f.W.LostResources[(int)ItemType.IronOre] == 0, "death counted both drop and loss"); Conserved(f);
            Until(f, () => c.Phase == CaravanPhase.Completed); Require(f.W.CaravanExchanges.Count == 2, "surviving cohort did not finish"); Conserved(f);
        }
        public static void WarCancelsTradePreservingCargo()
        {
            Fixture f = Make(); CaravanData c = Start(f); Until(f, () => c.Phase == CaravanPhase.Outbound); f.E.SetDiplomacy(f.A.Id, f.B.Id, true);
            Until(f, () => c.Phase == CaravanPhase.Cancelled); Require(f.W.CaravanExchanges.Count == 0, "traded at war"); Conserved(f);
        }
        public static void SaveLoadContinuesCaravanIdentically()
        {
            Fixture f = Make(); CaravanData c = Start(f); Until(f, () => c.Phase == CaravanPhase.Outbound); Step(f, 20);
            WorldState loaded = Clone(f.W); SimulationEngine second = new SimulationEngine(loaded);
            for (int i = 0; i < 600; i++) { f.E.Step(.1f); second.Step(.1f); }
            Require(Snapshot(f.W) == Snapshot(loaded), "continuation differs");
            Require(loaded.Validate(out string error), error); Conserved(f);
        }
        public static void ImportedBatchCannotBeSoldTwice()
        {
            Fixture f = Make(); CaravanData c = Start(f, 0); Until(f, () => c.Phase == CaravanPhase.Returning);
            UnitData u = f.W.GetUnit(c.LeaderId); int count = f.W.CaravanExchanges.Count;
            Require(!(bool)Call(f.E, "ExchangeCaravanLoad", c, u, f.Market), "second sale accepted"); Require(f.W.CaravanExchanges.Count == count, "second receipt"); Conserved(f);
        }
        public static void CorruptQuotaMembershipAndPhaseRejected()
        {
            Fixture f = Make(); CaravanData c = Start(f); c.Members[0].ExportQuota = float.NaN; Require(!f.W.Validate(out _), "NaN quota accepted");
            f = Make(); c = Start(f); f.W.GetUnit(c.Members[0].UnitId).CaravanId = 0; Require(!f.W.Validate(out _), "dangling participant accepted");
            f = Make(); c = Start(f); c.Members[0].Phase = CaravanMemberPhase.Returning; Require(!f.W.Validate(out _), "inconsistent phase accepted");
        }
        public static void ExchangeRequiresPhysicalArrival()
        {
            Fixture f = Make(); CaravanData c = Start(f); Step(f, 1);
            Require(f.W.CaravanExchanges.Count == 0 && f.Market.Inventory[(int)ItemType.CopperOre] == 30 && f.Market.Inventory[(int)ItemType.IronOre] == 0, "remote exchange");
            UnitData u = f.W.GetUnit(c.LeaderId); Require(!(bool)Call(f.E, "ExchangeCaravanLoad", c, u, f.Market), "remote direct exchange accepted"); Conserved(f);
        }
        public static void OwnedBasketCarriesLargerSingleBatch()
        {
            Fixture f = Make(); UnitData u = f.W.Units.First(x => x.SettlementId == f.A.Id); u.BasketLevel = 1;
            CaravanData c = Start(f, 0); Until(f, () => c.Phase == CaravanPhase.Outbound);
            Require(u.CargoAmount > 1 && u.CargoAmount <= 10, "basket capacity ignored");
            Require(Math.Abs(f.Source.Inventory[(int)ItemType.IronOre] + u.CargoAmount - 30) < .001, "cargo mirrored in source");
            Until(f, () => c.Phase == CaravanPhase.Completed); Require(f.W.CaravanExchanges.Count == 1, "basket batch not exchanged"); Conserved(f);
        }
        public static void ChangedReserveRefusesQuotedTrade()
        {
            Fixture f = Make(); CaravanData c = Start(f); Until(f, () => c.Phase == CaravanPhase.Outbound);
            f.Market.Inventory[(int)ItemType.CopperOre] = 3; f.W.LostResources[(int)ItemType.CopperOre] = 27;
            Until(f, () => c.Phase == CaravanPhase.Completed);
            Require(f.W.CaravanExchanges.Count == 0 && f.Market.Inventory[(int)ItemType.CopperOre] == 3, "sold protected reserve");
            Require(Math.Abs(f.W.GetSettlementStock(f.A.Id, ItemType.IronOre) - 30) < .001, "unsold goods lost"); Conserved(f);
        }
        public static void BlockedReturnLeavesPhysicalPile()
        {
            Fixture f = Make(); CaravanData c = Start(f, 0); Until(f, () => c.Phase == CaravanPhase.Returning);
            for (int y = 0; y < f.W.Height; y++) f.E.Paint(TerrainType.DeepWater, 15, y, 0);
            Until(f, () => c.Phase == CaravanPhase.Cancelled);
            Require(f.W.GroundStocks.Any(g => g.Item == ItemType.CopperOre && g.X > 15 && g.Amount > 0), "cargo teleported home or vanished"); Conserved(f);
        }
        public static void LegacyMigrationPreservesRandomAndIds()
        {
            Fixture f = Make(); uint rng = f.W.RngState; int next = f.W.NextId;
            f.W.CaravanVersion = 0; f.W.Caravans = null; f.W.CaravanExchanges = null; f.W.NextCaravanId = 0;
            Require(f.W.Validate(out string before), before); SimulationEngine second = new SimulationEngine(f.W);
            Require(f.W.RngState == rng && f.W.NextId == next && f.W.CaravanVersion == 1 && f.W.Caravans.Count == 0 && f.W.NextCaravanId == 1, "migration consumed random or entity state");
            Require(f.W.Validate(out string after), after); Conserved(f);
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class CaravanTests
    {
        [SetUp] public void SetSerializer() { CaravanScenarios.Clone = w => JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)); CaravanScenarios.Snapshot = w => JsonUtility.ToJson(w); }
        [Test] public void PhysicalCohortTradesAndReturnsConservingGoods() { CaravanScenarios.PhysicalCohortTradesAndReturnsConservingGoods(); }
        [Test] public void PaidMealsAndNaturalTaxCommitOnce() { CaravanScenarios.PaidMealsAndNaturalTaxCommitOnce(); }
        [Test] public void NoBudgetMeansNoPaidHelpers() { CaravanScenarios.NoBudgetMeansNoPaidHelpers(); }
        [Test] public void DistantPaidHelpersCannotEatRemoteFoodButFreeHelpersMayJoin() { CaravanScenarios.DistantPaidHelpersCannotEatRemoteFoodButFreeHelpersMayJoin(); }
        [Test] public void TechnologyAndAdultGates() { CaravanScenarios.TechnologyAndAdultGates(); }
        [Test] public void CancellationReturnsOriginalCargo() { CaravanScenarios.CancellationReturnsOriginalCargo(); }
        [Test] public void LeaderDeathDropsCargoAndSurvivorsContinue() { CaravanScenarios.LeaderDeathDropsCargoAndSurvivorsContinue(); }
        [Test] public void WarCancelsTradePreservingCargo() { CaravanScenarios.WarCancelsTradePreservingCargo(); }
        [Test] public void SaveLoadContinuesCaravanIdentically() { CaravanScenarios.SaveLoadContinuesCaravanIdentically(); }
        [Test] public void ImportedBatchCannotBeSoldTwice() { CaravanScenarios.ImportedBatchCannotBeSoldTwice(); }
        [Test] public void CorruptQuotaMembershipAndPhaseRejected() { CaravanScenarios.CorruptQuotaMembershipAndPhaseRejected(); }
        [Test] public void ExchangeRequiresPhysicalArrival() { CaravanScenarios.ExchangeRequiresPhysicalArrival(); }
        [Test] public void OwnedBasketCarriesLargerSingleBatch() { CaravanScenarios.OwnedBasketCarriesLargerSingleBatch(); }
        [Test] public void ChangedReserveRefusesQuotedTrade() { CaravanScenarios.ChangedReserveRefusesQuotedTrade(); }
        [Test] public void BlockedReturnLeavesPhysicalPile() { CaravanScenarios.BlockedReturnLeavesPhysicalPile(); }
        [Test] public void LegacyMigrationPreservesRandomAndIds() { CaravanScenarios.LegacyMigrationPreservesRandomAndIds(); }
    }
#endif
}
