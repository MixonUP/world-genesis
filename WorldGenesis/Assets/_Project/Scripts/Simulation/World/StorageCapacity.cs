using System;

namespace WorldGenesis.Simulation
{
    /// <summary>One physical capacity per depot, independent of ownership or item compartment.</summary>
    public static class StorageCapacity
    {
        public static bool IsDepot(BuildingData building) { return building != null && (building.Type == BuildingType.Warehouse || building.Type == BuildingType.Granary); }
        public static int MaximumLevel(BuildingType type) { return type == BuildingType.Warehouse || type == BuildingType.Granary ? 4 : 5; }
        public static int EffectiveLevel(BuildingData building) { return building == null ? 0 : Math.Max(1, Math.Min(MaximumLevel(building.Type), building.Level)); }
        // Legacy level 5 is interpreted as tier 4. No saved goods or level field is rewritten here.
        public static float Capacity(BuildingData building)
        {
            if (building == null) return 0;
            if (!IsDepot(building)) return ResourceCatalog.StorageCapacity * (IndustryCatalog.IsWorkshop(building.Type) ? Math.Max(1, Math.Min(5, building.Level)) : 1);
            switch (EffectiveLevel(building)) { case 1: return 1000; case 2: return 2500; case 3: return 5000; default: return 10000; }
        }
        public static bool IsFood(AgriculturalProduct product)
        {
            switch (product)
            {
                case AgriculturalProduct.Grain: case AgriculturalProduct.Berries: case AgriculturalProduct.Hay:
                case AgriculturalProduct.Meat: case AgriculturalProduct.Milk: case AgriculturalProduct.Egg: case AgriculturalProduct.Ale: case AgriculturalProduct.Fish: case AgriculturalProduct.DelicacyFish: return true;
                default: return false;
            }
        }
        public static bool Accepts(BuildingData building, ItemType item)
        { return building != null && (int)item > 0 && (int)item < ResourceCatalog.ItemCount && building.Type != BuildingType.Granary; }
        public static bool Accepts(BuildingData building, AgriculturalProduct product)
        {
            if (building == null || (int)product <= 0 || (int)product >= FishingCatalog.AgriculturalProductCount) return false;
            if (building.Type == BuildingType.Granary) return IsFood(product);
            return building.Type != BuildingType.Warehouse || !IsFood(product);
        }
        public static float MaterialUsed(BuildingData building)
        {
            if (building == null) return 0;
            double used = Sum(building.Inventory) + Sum(building.TaxStocks);
            if (building.PersonalStocks != null) for (int i = 0; i < building.PersonalStocks.Count; i++)
                if (building.PersonalStocks[i] != null) used += building.PersonalStocks[i].Amount;
            return (float)used;
        }
        public static float AgriculturalUsed(BuildingData building) { return building == null ? 0 : (float)Sum(building.AgriculturalStock); }
        public static float TotalUsed(BuildingData building) { return MaterialUsed(building) + AgriculturalUsed(building); }
        public static float FreeMaterials(BuildingData building)
        {
            if (building == null || building.Type == BuildingType.Granary) return 0;
            return Math.Max(0, Capacity(building) - (IsDepot(building) ? TotalUsed(building) : MaterialUsed(building)));
        }
        // Used for recipe room checks. Actual incoming products MUST use the typed overload.
        public static float FreeAgriculture(BuildingData building)
        {
            if (building == null) return 0;
            return Math.Max(0, IsDepot(building) ? Capacity(building) - TotalUsed(building) : ResourceCatalog.StorageCapacity * Math.Max(1, building.Level) - AgriculturalUsed(building));
        }
        public static float FreeAgriculture(BuildingData building, AgriculturalProduct product)
        { return Accepts(building, product) ? FreeAgriculture(building) : 0; }
        public static float Overflow(BuildingData building)
        {
            if (building == null) return 0;
            return IsDepot(building) ? Math.Max(0, TotalUsed(building) - Capacity(building)) :
                Math.Max(0, MaterialUsed(building) - Capacity(building)) + Math.Max(0, AgriculturalUsed(building) - ResourceCatalog.StorageCapacity * Math.Max(1, building.Level));
        }
        private static double Sum(float[] values)
        { double amount = 0; if (values != null) for (int i = 1; i < values.Length; i++) amount += values[i]; return amount; }
    }
}
