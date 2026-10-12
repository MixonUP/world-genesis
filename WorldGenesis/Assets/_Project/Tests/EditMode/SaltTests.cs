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
    public static class SaltScenarios
    {
        public static Func<WorldState, WorldState> Clone;
        public static Func<WorldState, string> Snapshot;
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Near(float expected, float actual, string message) { Need(Math.Abs(expected - actual) < .001f, message + ": " + actual); }
        private static object Call(SimulationEngine e, string method, params object[] args) { return typeof(SimulationEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(e, args); }
        private static SimulationEngine Town()
        {
            WorldState w = WorldGenerator.Generate(553, 32, 32);
            for (int i = 0; i < w.Cells.Length; i++) { w.Cells[i].Terrain = TerrainType.Grass; w.Cells[i].Height = .55f; w.Cells[i].Moisture = .6f; w.Cells[i].River = false; Hydrology.RefreshCell(w, i); w.Cells[i].Vegetation = .5f; }
            ResourceCatalog.Generate(w); w.Laws.Aging = w.Laws.Births = w.Laws.Warfare = false;
            SimulationEngine e = new SimulationEngine(w); w.NaturalWildlife = false; w.Units.RemoveAll(u => !SimulationEngine.IsCivilized(u.Species)); e.Spawn(SpeciesType.Human, 8, 8, 4);
            foreach (UnitData u in w.Units) { u.Age = 25; u.Hunger = u.Thirst = u.Fatigue = u.SocialNeed = .1f; u.Task = UnitTask.Idle; u.DecisionTimer = 10000; u.Path.Clear(); u.PathCursor = 0; }
            foreach (SettlementData s in w.Settlements) { s.Food = 200; s.Wood = 0; s.NextPolicyReviewTick = s.NextCaravanTick = long.MaxValue; }
            for (int i = 0; i < w.ResourceNodes.Length; i++) w.ResourceNodes[i] = default(ResourceNodeData);
            Need(w.Validate(out string error), error); return e;
        }
        private static BuildingData Hall(WorldState w, int town) { return w.Buildings.First(b => b.SettlementId == town && b.Type == BuildingType.Hall); }
        private static void MakeLegacy(WorldState w, int version)
        {
            w.ResourceVersion = version; w.LostResources = w.LostResources.Take(17).ToArray();
            foreach (BuildingData b in w.Buildings) { b.Inventory = b.Inventory.Take(17).ToArray(); b.TaxStocks = b.TaxStocks.Take(17).ToArray(); }
        }
        private static float Total(WorldState w, ItemType item)
        {
            float total = w.LostResources[(int)item];
            foreach (BuildingData b in w.Buildings) { total += b.Inventory[(int)item] + b.TaxStocks[(int)item]; foreach (OwnedGoodsData lot in b.PersonalStocks) if (lot.Item == item) total += lot.Amount; }
            foreach (UnitData u in w.Units) if (u.CargoKind == item) total += u.CargoAmount;
            foreach (GroundStockData g in w.GroundStocks) if (!g.IsFood && g.Item == item) total += g.Amount;
            foreach (ResourceNodeData node in w.ResourceNodes) if (ResourceCatalog.GetOutput(node.Type) == item) total += node.Amount;
            return total;
        }
        private static void Steps(SimulationEngine e, int count)
        { for (int i = 0; i < count; i++) { e.Step(.1f); Need(e.World.Validate(out string error), error); } }
        public static void AppendedSaltKeepsEveryLegacyEnumNumber()
        {
            Need((int)ResourceType.Tin == 11 && (int)ResourceType.Salt == 12 && (int)ItemType.TinOre == 16 && (int)ItemType.Salt == 17 && ResourceCatalog.ItemCount == 18, "IDs shifted");
            Need(ResourceCatalog.GetOutput(ResourceType.Salt) == ItemType.Salt && ResourceCatalog.GetRegrowTicks(ResourceType.Salt) == 9000, "salt catalog mapping");
        }
        public static void LegacySeventeenSlotSavePreservesAllOwnershipAndAmounts()
        {
            SimulationEngine source = Town(); WorldState w = source.World; UnitData u = w.Units[0]; BuildingData b = Hall(w, u.SettlementId);
            b.Inventory[(int)ItemType.IronOre] = 7.5f; b.TaxStocks[(int)ItemType.CopperOre] = 2.25f; b.PersonalStocks.Add(new OwnedGoodsData { OwnerId = u.Id, Item = ItemType.RawStone, Amount = 1.5f }); w.LostResources[(int)ItemType.TinOre] = 1.25f;
            u.CargoKind = ItemType.CopperOre; u.CargoAmount = .4f; MakeLegacy(w, 2); w = Clone(w); Need(w.Validate(out string before), before);
            uint rng = w.RngState; int next = w.NextId; ResourceNodeData[] nodes = (ResourceNodeData[])w.ResourceNodes.Clone();
            new SimulationEngine(w); b = Hall(w, u.SettlementId);
            Need(w.ResourceVersion == 3 && b.Inventory.Length == 18 && b.TaxStocks.Length == 18 && w.LostResources.Length == 18, "arrays not expanded");
            Near(7.5f, b.Inventory[9], "public iron"); Near(2.25f, b.TaxStocks[10], "tax copper"); Near(1.5f, b.PersonalStocks.Single().Amount, "private stone"); Need(b.PersonalStocks[0].OwnerId == u.Id, "private owner changed"); Near(1.25f, w.LostResources[16], "loss ledger"); Near(.4f, w.GetUnit(u.Id).CargoAmount, "held copper");
            Need(w.RngState == rng && w.NextId == next && nodes.SequenceEqual(w.ResourceNodes), "migration rewrote geography, RNG or IDs"); Near(0, Total(w, ItemType.Salt), "migration minted salt");
            Need(w.Validate(out string after), after); string once = Snapshot(w); new SimulationEngine(w); Need(Snapshot(w) == once, "migration repeated");
        }
        public static void EarlierCarryingMigrationDoesNotDowngradeResourceVersion()
        {
            WorldState w = Town().World; UnitData u = w.Units[0]; u.CargoKind = ItemType.CopperOre; u.CargoAmount = 2; u.CarriedFood = .8f; u.CarriedWood = 1;
            MakeLegacy(w, 1); uint rng = w.RngState; int next = w.NextId; Need(w.Validate(out string before), before); new SimulationEngine(w);
            Need(w.ResourceVersion == 3 && w.RngState == rng && w.NextId == next, "carry migration downgraded salt schema"); Near(2, Total(w, ItemType.CopperOre), "copper overflow lost");
            Near(1, u.CarriedWood + w.GroundStocks.Where(g => g.Item == ItemType.Plank).Sum(g => g.Amount), "plank overflow lost"); Near(.8f, u.CarriedFood + w.GroundStocks.Where(g => g.IsFood).Sum(g => g.Amount), "food overflow lost"); Need(w.Validate(out string after), after);
        }
        public static void VersionAndCorruptSaltSlotsAreRejected()
        {
            WorldState w = Town().World; w.ResourceVersion = 4; Need(!w.Validate(out _), "future version accepted");
            w = Town().World; w.Buildings[0].Inventory = new float[17]; Need(!w.Validate(out _), "v3 short inventory accepted");
            w = Town().World; w.Buildings[0].TaxStocks = new float[17]; Need(!w.Validate(out _), "v3 short taxes accepted");
            w = Town().World; w.Buildings[0].Inventory[17] = float.NaN; Need(!w.Validate(out _), "NaN salt accepted");
            w = Town().World; w.ResourceVersion = 2; w.Buildings[0].Inventory[17] = 1; Need(!w.Validate(out _), "salt hidden in legacy slot accepted");
        }
        public static void SaltGenerationIsDeterministicRareAndHabitatBounded()
        {
            int saltCells = 0, landCells = 0, populatedRegions = 0, emptyRegions = 0;
            for (int seed = 1; seed <= 12; seed++)
            {
                WorldState w = WorldGenerator.Generate(seed * 7919, 128, 128); uint rng = w.RngState; ResourceCatalog.Generate(w); ResourceNodeData[] original = (ResourceNodeData[])w.ResourceNodes.Clone(); ResourceCatalog.Generate(w);
                Need(original.SequenceEqual(w.ResourceNodes) && rng == w.RngState, "salt generation consumes RNG or differs");
                for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
                {
                    if (w.IsWalkable(x, y)) landCells++; ResourceNodeData n = w.ResourceNodes[w.Index(x, y)]; if (n.Type != ResourceType.Salt) continue;
                    saltCells++; Need(ResourceCatalog.SaltHabitat(w, x, y) && n.Amount == n.Capacity && n.Capacity >= 4 && n.Capacity <= 8, "salt outside habitat/range");
                }
                for (int ry = 0; ry < 8; ry++) for (int rx = 0; rx < 8; rx++)
                { bool found = false; for (int y = ry * 16; y < ry * 16 + 16; y++) for (int x = rx * 16; x < rx * 16 + 16; x++) found |= w.ResourceNodes[w.Index(x, y)].Type == ResourceType.Salt; if (found) populatedRegions++; else emptyRegions++; }
            }
            Need(saltCells > 0 && saltCells < landCells / 20 && populatedRegions > 0 && emptyRegions > populatedRegions * 3, "salt not scarce or missing from whole seed matrix");
        }
        public static void SaltIsPhysicallyGatheredThenDeliveredWithoutDuplication()
        {
            SimulationEngine e = Town(); WorldState w = e.World; UnitData u = w.Units[0]; int cell = w.Index(19, 19); w.ResourceNodes[cell] = new ResourceNodeData { Type = ResourceType.Salt, Amount = 4, Capacity = 4 };
            u.X = u.Y = 19.5f; u.Task = UnitTask.Gather; u.ResourceCell = cell; u.TargetCell = cell; u.DecisionTimer = 0; SettlementData s = w.GetSettlement(u.SettlementId);
            Call(e, "WorkResources", u, s, 2.5f); Near(1, u.CargoAmount, "real cargo"); Need(u.CargoKind == ItemType.Salt, "salt mapped to other good"); Near(0, w.GetSettlementStock(s.Id, ItemType.Salt), "remote delivery"); Near(4, Total(w, ItemType.Salt), "gather conservation");
            for (int i = 0; i < 1500 && u.CargoAmount > 0; i++) Steps(e, 1);
            Need(u.CargoAmount == 0 && w.GetSettlementStock(s.Id, ItemType.Salt) > .79f, "salt did not reach local storage"); Near(4, Total(w, ItemType.Salt), "delivery conservation");
        }
        public static void SaltRegrowthIsSlowAndPausesOnIncompatibleTerrain()
        {
            SimulationEngine e = Town(); WorldState w = e.World; UnitData u = w.Units[0]; int cell = w.Index(19, 19); w.ResourceNodes[cell] = new ResourceNodeData { Type = ResourceType.Salt, Amount = 4, Capacity = 4 };
            u.BasketLevel = 1; u.X = u.Y = 19.5f; u.Task = UnitTask.Gather; u.ResourceCell = cell; u.TargetCell = cell; Call(e, "WorkResources", u, w.GetSettlement(u.SettlementId), 10f);
            Near(0, w.ResourceNodes[cell].Amount, "node not exhausted"); Need(w.ResourceNodes[cell].RegrowTicksRemaining == 9000, "slow timer missing");
            w.Cells[cell].Terrain = TerrainType.ShallowWater; Call(e, "UpdateResources"); Need(w.ResourceNodes[cell].RegrowTicksRemaining == 9000, "underwater salt regrew");
            w.Cells[cell].Terrain = TerrainType.Grass; for (int i = 0; i < 8999; i++) Call(e, "UpdateResources"); Near(0, w.ResourceNodes[cell].Amount, "early salt refill"); Need(w.ResourceNodes[cell].RegrowTicksRemaining == 1, "timer drift");
            Call(e, "UpdateResources"); Near(4, w.ResourceNodes[cell].Amount, "no eventual refill");
        }
        public static void SaltMovesThroughOrdinaryCaravanAndTaxContainers()
        {
            SimulationEngine e = Town(); WorldState w = e.World; e.Spawn(SpeciesType.Elf, 25, 25, 4); SettlementData a = w.Settlements[0], b = w.Settlements[1];
            foreach (SettlementData s in w.Settlements) { s.Food = 200; s.NextPolicyReviewTick = s.NextCaravanTick = long.MaxValue; foreach (TechnologyDefinition t in TechnologyCatalog.All) s.UnlockedTechnologies.Add(t.Id); }
            e.SetEconomicPolicy(a.Id, EconomicPolicy.FreeDistribution, 0, ChildLaborPolicy.Forbidden, ResourceFocus.Balanced); e.SetEconomicPolicy(b.Id, EconomicPolicy.Market, .2f, ChildLaborPolicy.Forbidden, ResourceFocus.Balanced);
            Hall(w, a.Id).Inventory[17] = 30; Hall(w, b.Id).Inventory[(int)ItemType.IronOre] = 30;
            Need(e.TryStartCaravan(a.Id, b.Id, ItemType.Salt, ItemType.IronOre, 0), "salt caravan refused"); CaravanData c = w.Caravans.Last();
            for (int i = 0; i < 2000 && c.Phase != CaravanPhase.Completed; i++) Steps(e, 1);
            Need(c.Phase == CaravanPhase.Completed && w.CaravanExchanges.Any(t => t.ExportItem == ItemType.Salt), "salt caravan never exchanged"); Near(30, Total(w, ItemType.Salt), "salt barter conservation"); Near(.2f, Hall(w, b.Id).TaxStocks[17], "salt tax missing");
        }
        public static void SaltSaveContinuesCargoStocksAndTimerIdentically()
        {
            SimulationEngine first = Town(); WorldState w = first.World; UnitData u = w.Units[0]; BuildingData b = Hall(w, u.SettlementId); b.Inventory[17] = 2; b.TaxStocks[17] = .25f; b.PersonalStocks.Add(new OwnedGoodsData { OwnerId = u.Id, Item = ItemType.Salt, Amount = .5f });
            w.ResourceNodes[w.Index(19, 19)] = new ResourceNodeData { Type = ResourceType.Salt, Capacity = 4, RegrowTicksRemaining = 5000 }; u.CargoKind = ItemType.Salt; u.CargoAmount = .75f; u.DecisionTimer = 0;
            SimulationEngine second = new SimulationEngine(Clone(w)); for (int i = 0; i < 600; i++) { first.Step(.1f); second.Step(.1f); }
            Need(Snapshot(first.World) == Snapshot(second.World), "salt save continuation differs"); Need(second.World.Validate(out string error), error);
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class SaltTests
    {
        [SetUp] public void Serializers() { SaltScenarios.Clone = w => JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)); SaltScenarios.Snapshot = w => JsonUtility.ToJson(w); }
        [TestCase("AppendedSaltKeepsEveryLegacyEnumNumber")]
        [TestCase("LegacySeventeenSlotSavePreservesAllOwnershipAndAmounts")]
        [TestCase("EarlierCarryingMigrationDoesNotDowngradeResourceVersion")]
        [TestCase("VersionAndCorruptSaltSlotsAreRejected")]
        [TestCase("SaltGenerationIsDeterministicRareAndHabitatBounded")]
        [TestCase("SaltIsPhysicallyGatheredThenDeliveredWithoutDuplication")]
        [TestCase("SaltRegrowthIsSlowAndPausesOnIncompatibleTerrain")]
        [TestCase("SaltMovesThroughOrdinaryCaravanAndTaxContainers")]
        [TestCase("SaltSaveContinuesCargoStocksAndTimerIdentically")]
        public void Scenario(string name) { typeof(SaltScenarios).GetMethod(name).Invoke(null, null); }
    }
#endif
}
