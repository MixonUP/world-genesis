using System;

namespace WorldGenesis.Simulation
{
    public enum ResourceType { None, Tree, Stone, IronOre, CopperOre, GoldOre, SilverOre, Coal, Clay, Sand, Limestone, Tin, Salt }
    public enum ItemType { None, Log, Plank, Firewood, RawStone, StoneBlock, StoneTile, Clay, Brick, IronOre, CopperOre, GoldOre, SilverOre, Coal, Sand, Limestone, TinOre, Salt, Charcoal, IronIngot, CopperIngot, TinIngot, BronzeIngot, GoldIngot, SilverIngot, IronTools, BronzeTools, Sword, Spear, Armor, Leather, LinenCloth, LeatherClothes, LinenClothes, Furniture, Bow, CopperCoin, SilverCoin, GoldCoin, Rope, Ballista, Catapult, Trebuchet, BallistaBolts, StoneShot }
    public enum RecipeType { None, Plank, Firewood, StoneBlock, StoneTile, Brick }

    [Serializable]
    public struct ResourceNodeData
    {
        public ResourceType Type;
        public float Amount, Capacity;
        public int RegrowTicksRemaining;
    }

    public struct ResourceBalance
    {
        public float Stock, Target, Deficit, Surplus;
    }

    public sealed partial class WorldState
    {
        public int ResourceVersion, ResourceRevision;
        public ResourceNodeData[] ResourceNodes;
        // Explicit sinks for destroyed cargo, inventory and cleared/burnt deposits; never hidden stock.
        public float[] LostResources = new float[ResourceCatalog.ItemCount];

        public ResourceNodeData GetResourceAt(int x, int y)
        {
            return InBounds(x, y) && ResourceNodes != null ? ResourceNodes[Index(x, y)] : default(ResourceNodeData);
        }

        public BuildingData GetBuilding(int id)
        {
            for (int i = 0; i < Buildings.Count; i++) if (Buildings[i].Id == id) return Buildings[i];
            return null;
        }

        public float GetSettlementStock(int settlementId, ItemType item)
        {
            SettlementData home = GetSettlement(settlementId);
            if (home == null || item == ItemType.None || (int)item >= ResourceCatalog.ItemCount || (int)item < 0) return 0;
            float amount = item == ItemType.Plank ? home.Wood : 0;
            for (int i = 0; i < Buildings.Count; i++)
                if (Buildings[i].SettlementId == settlementId && Buildings[i].Inventory != null && (int)item < Buildings[i].Inventory.Length && Buildings[i].Health > 0) amount += Buildings[i].Inventory[(int)item];
            return amount;
        }

        public ResourceBalance GetResourceBalance(int settlementId, ItemType item)
        {
            SettlementData home = GetSettlement(settlementId);
            float stock = GetSettlementStock(settlementId, item);
            float target = home == null || home.Population == 0 ? 0 : ResourceCatalog.StockTarget(item, home.Population);
            return new ResourceBalance { Stock = stock, Target = target, Deficit = Math.Max(0, target - stock), Surplus = Math.Max(0, stock - target) };
        }

        private bool ResourceStockArrayMatchesVersion(float[] values)
        {
            if (values == null) return false;
            if (ResourceVersion == 4) return values.Length == ResourceCatalog.ItemCount;
            int count = ResourceVersion == 3 ? ResourceCatalog.SaltItemCount : ResourceCatalog.LegacyItemCount;
            if (values.Length == count) return true;
            if (values.Length != ResourceCatalog.ItemCount && !(count == 17 && values.Length == 18)) return false;
            for (int i = count; i < values.Length; i++) if (values[i] != 0) return false;
            return true;
        }

        private bool ValidateResources(out string error)
        {
            error = null;
            if (ResourceVersion < 0 || ResourceVersion > 4) { error = "Unsupported resource version."; return false; }
            if (ResourceVersion == 0) return true; // Original saves have no resource fields; the engine migrates them once.
            if (ResourceNodes == null || ResourceNodes.Length != Cells.Length || !ResourceStockArrayMatchesVersion(LostResources))
            { error = "Invalid resource arrays."; return false; }
            for (int i = 0; i < LostResources.Length; i++) if (!Stock(LostResources[i])) { error = "Invalid resource loss ledger."; return false; }
            for (int i = 0; i < ResourceNodes.Length; i++)
            {
                ResourceNodeData n = ResourceNodes[i];
                if ((int)n.Type < 0 || (int)n.Type > (ResourceVersion >= 3 ? (int)ResourceType.Salt : (int)ResourceType.Tin) || !Stock(n.Amount) || !Stock(n.Capacity) || n.Capacity > 100 || n.Amount > n.Capacity + .0001f || n.RegrowTicksRemaining < 0 || n.RegrowTicksRemaining > ResourceCatalog.GetRegrowTicks(n.Type) || (n.Type == ResourceType.None && (n.Amount != 0 || n.Capacity != 0 || n.RegrowTicksRemaining != 0)) || (n.Type != ResourceType.None && n.Capacity <= 0))
                { error = "Invalid resource node."; return false; }
            }
            for (int i = 0; i < Units.Count; i++)
            {
                UnitData u = Units[i];
                int itemLimit = ResourceVersion >= 4 ? ResourceCatalog.ItemCount : ResourceVersion == 3 ? ResourceCatalog.SaltItemCount : ResourceCatalog.LegacyItemCount;
                if ((int)u.CargoKind < 0 || (int)u.CargoKind >= itemLimit || (int)u.PickupKind < 0 || (int)u.PickupKind >= itemLimit || !Stock(u.CargoAmount) || u.CargoAmount > (ResourceVersion < 2 ? 3 : AgricultureVersion == 0 ? ResourceCatalog.CarryCapacity : SimulationEngine.CarryCapacityFor(u)) + .0001f || (ResourceVersion >= 2 && SimulationEngine.CarriedLoad(u) > (AgricultureVersion == 0 ? ResourceCatalog.CarryCapacity : SimulationEngine.CarryCapacityFor(u)) + .0001f) || (u.CargoAmount > 0 && (u.CargoKind == ItemType.None || (u.CargoKind == ItemType.Plank && AgricultureVersion == 0))) || (u.CargoAmount == 0 && u.CargoKind != ItemType.None) || u.ResourceCell < -1 || u.ResourceCell >= Cells.Length || u.SourceBuildingId < 0 || u.TargetBuildingId < 0 || (u.SourceBuildingId != 0 && GetBuilding(u.SourceBuildingId) == null) || (u.TargetBuildingId != 0 && GetBuilding(u.TargetBuildingId) == null))
                { error = "Invalid resource cargo or job."; return false; }
            }
            for (int i = 0; i < Buildings.Count; i++)
            {
                BuildingData b = Buildings[i];
                if (!ResourceStockArrayMatchesVersion(b.Inventory) || b.Inventory[0] != 0 || (b.Inventory[(int)ItemType.Plank] != 0 && AgricultureVersion == 0) || b.WorkerId < 0 || (int)b.ActiveRecipe < 0 || (int)b.ActiveRecipe > 5 || !Stock(b.ProcessProgress) || (b.ActiveRecipe == RecipeType.None && b.ProcessProgress != 0) || (b.ActiveRecipe != RecipeType.None && (!ResourceCatalog.CanProcess(b.Type, b.ActiveRecipe) || b.ProcessProgress > ResourceCatalog.ProcessingSeconds(b.Type, b.ActiveRecipe) + .001f)))
                { error = "Invalid workshop state."; return false; }
                float stored = 0;
                for (int j = 0; j < b.Inventory.Length; j++) { if (!Stock(b.Inventory[j])) { error = "Invalid stored resource."; return false; } stored += b.Inventory[j]; }
                if (!Stock(stored) || (!StorageCapacity.IsDepot(b) && stored > StorageCapacity.Capacity(b) + .001f)) { error = "Storage capacity exceeded."; return false; }
                if (b.WorkerId != 0)
                {
                    UnitData worker = GetUnit(b.WorkerId);
                    if (worker == null || worker.Health <= 0 || worker.SettlementId != b.SettlementId || worker.TargetBuildingId != b.Id || worker.Task != UnitTask.Process)
                    { error = "Invalid workshop worker."; return false; }
                }
            }
            return true;
        }
    }
}
