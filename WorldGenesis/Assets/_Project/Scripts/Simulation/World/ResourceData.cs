using System;

namespace WorldGenesis.Simulation
{
    public enum ResourceType { None, Tree, Stone, IronOre, CopperOre, GoldOre, SilverOre, Coal, Clay, Sand, Limestone, Tin }
    public enum ItemType { None, Log, Plank, Firewood, RawStone, StoneBlock, StoneTile, Clay, Brick, IronOre, CopperOre, GoldOre, SilverOre, Coal, Sand, Limestone, TinOre }
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
            if (item == ItemType.Plank) return home.Wood;
            float amount = 0;
            for (int i = 0; i < Buildings.Count; i++)
                if (Buildings[i].SettlementId == settlementId && Buildings[i].Inventory != null && Buildings[i].Health > 0) amount += Buildings[i].Inventory[(int)item];
            return amount;
        }

        public ResourceBalance GetResourceBalance(int settlementId, ItemType item)
        {
            SettlementData home = GetSettlement(settlementId);
            float stock = GetSettlementStock(settlementId, item);
            float target = home == null || home.Population == 0 ? 0 : ResourceCatalog.StockTarget(item, home.Population);
            return new ResourceBalance { Stock = stock, Target = target, Deficit = Math.Max(0, target - stock), Surplus = Math.Max(0, stock - target) };
        }

        private bool ValidateResources(out string error)
        {
            error = null;
            if (ResourceVersion < 0 || ResourceVersion > 2) { error = "Unsupported resource version."; return false; }
            if (ResourceVersion == 0) return true; // Original saves have no resource fields; the engine migrates them once.
            if (ResourceNodes == null || ResourceNodes.Length != Cells.Length || LostResources == null || LostResources.Length != ResourceCatalog.ItemCount)
            { error = "Invalid resource arrays."; return false; }
            for (int i = 0; i < LostResources.Length; i++) if (!Stock(LostResources[i])) { error = "Invalid resource loss ledger."; return false; }
            for (int i = 0; i < ResourceNodes.Length; i++)
            {
                ResourceNodeData n = ResourceNodes[i];
                if ((int)n.Type < 0 || (int)n.Type > 11 || !Stock(n.Amount) || !Stock(n.Capacity) || n.Capacity > 100 || n.Amount > n.Capacity + .0001f || n.RegrowTicksRemaining < 0 || n.RegrowTicksRemaining > ResourceCatalog.GetRegrowTicks(n.Type) || (n.Type == ResourceType.None && (n.Amount != 0 || n.Capacity != 0 || n.RegrowTicksRemaining != 0)) || (n.Type != ResourceType.None && n.Capacity <= 0))
                { error = "Invalid resource node."; return false; }
            }
            for (int i = 0; i < Units.Count; i++)
            {
                UnitData u = Units[i];
                if ((int)u.CargoKind < 0 || (int)u.CargoKind >= ResourceCatalog.ItemCount || (int)u.PickupKind < 0 || (int)u.PickupKind >= ResourceCatalog.ItemCount || !Stock(u.CargoAmount) || u.CargoAmount > (ResourceVersion < 2 ? 3 : ResourceCatalog.CarryCapacity) + .0001f || (ResourceVersion >= 2 && SimulationEngine.CarriedLoad(u) > ResourceCatalog.CarryCapacity + .0001f) || (u.CargoAmount > 0 && (u.CargoKind == ItemType.None || u.CargoKind == ItemType.Plank)) || (u.CargoAmount == 0 && u.CargoKind != ItemType.None) || u.ResourceCell < -1 || u.ResourceCell >= Cells.Length || u.SourceBuildingId < 0 || u.TargetBuildingId < 0 || (u.SourceBuildingId != 0 && GetBuilding(u.SourceBuildingId) == null) || (u.TargetBuildingId != 0 && GetBuilding(u.TargetBuildingId) == null))
                { error = "Invalid resource cargo or job."; return false; }
            }
            for (int i = 0; i < Buildings.Count; i++)
            {
                BuildingData b = Buildings[i];
                if (b.Inventory == null || b.Inventory.Length != ResourceCatalog.ItemCount || b.Inventory[0] != 0 || b.Inventory[(int)ItemType.Plank] != 0 || b.WorkerId < 0 || (int)b.ActiveRecipe < 0 || (int)b.ActiveRecipe > 5 || !Stock(b.ProcessProgress) || (b.ActiveRecipe == RecipeType.None && b.ProcessProgress != 0) || (b.ActiveRecipe != RecipeType.None && (!ResourceCatalog.CanProcess(b.Type, b.ActiveRecipe) || b.ProcessProgress > ResourceCatalog.ProcessingSeconds(b.Type, b.ActiveRecipe) + .001f)))
                { error = "Invalid workshop state."; return false; }
                float stored = 0;
                for (int j = 0; j < b.Inventory.Length; j++) { if (!Stock(b.Inventory[j])) { error = "Invalid stored resource."; return false; } stored += b.Inventory[j]; }
                if (stored > ResourceCatalog.StorageCapacity + .001f) { error = "Storage capacity exceeded."; return false; }
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
