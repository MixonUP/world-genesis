using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public enum AgriculturalProduct { None, Grain, Fiber, Berries, Herbs, Hay, Meat, Hide, Milk, Egg, RareHorn, RarePelt, Ale, Fish, DelicacyFish, RiverPearl, AncientScale }
    public enum CropType { Wheat, Flax, BerryOrchard, HerbGarden, Hay }
    [Serializable]
    public sealed class AgriculturalDrop
    {
        public AgriculturalProduct Product;
        public float Amount, X, Y;
        public int OwnerSettlementId;
    }
    public sealed partial class BuildingData
    {
        public float[] AgriculturalStock = new float[FishingCatalog.AgriculturalProductCount];
        public CropType FarmCrop;
        public float CropGrowth, HarvestReady, BrewingProgress, BrewWaterStored;
        public int HerdSpecies;
    }
    public sealed partial class UnitData
    {
        public AgriculturalProduct HarvestProduct;
        public float HarvestAmount, CarriedWater;
        public int BasketLevel, MountId;
        public SpeciesType MountSpecies;
        public bool MountBags, HasWagon;
        public float Intoxication, AlcoholHabit, EquipmentProgress;
        public int EquipmentOrder, HuntingToolLevel;
        public bool OwnsWagon;
        public int AnimalOwnerId, HandlerId, PastureId, HusbandrySourceId;
        public bool IsDomestic;
        public bool HasSaddleBags;
        public float AnimalProduceTimer;
    }
    public sealed partial class WorldState
    {
        public int AgricultureVersion;
        public List<AgriculturalDrop> AgriculturalDrops = new List<AgriculturalDrop>();
        public float[] LostAgriculture = new float[FishingCatalog.AgriculturalProductCount];
        public float GetAgriculturalStock(int settlementId, AgriculturalProduct product)
        {
            if ((int)product <= 0 || (int)product >= FishingCatalog.AgriculturalProductCount) return 0;
            float amount = 0;
            for (int i = 0; i < Buildings.Count; i++) if (Buildings[i].SettlementId == settlementId && Buildings[i].Health > 0 && Buildings[i].AgriculturalStock != null && (int)product < Buildings[i].AgriculturalStock.Length) amount += Buildings[i].AgriculturalStock[(int)product];
            return amount;
        }
        private bool ValidateAgriculture(out string error)
        {
            error = null;
            if (AgricultureVersion < 0 || AgricultureVersion > 2) { error = "Unsupported agriculture version."; return false; }
            if (AgricultureVersion == 0) return true;
            int productCount = AgricultureVersion == 1 ? 13 : FishingCatalog.AgriculturalProductCount;
            if (AgriculturalDrops == null || AgriculturalDrops.Count > 20000 || LostAgriculture == null || LostAgriculture.Length != productCount) { error = "Invalid agricultural ledger."; return false; }
            for (int i = 0; i < LostAgriculture.Length; i++) if (!Stock(LostAgriculture[i])) { error = "Invalid agricultural sink."; return false; }
            for (int i = 0; i < AgriculturalDrops.Count; i++)
            {
                AgriculturalDrop drop = AgriculturalDrops[i];
                if (drop == null || (int)drop.Product < 1 || (int)drop.Product >= productCount || !Stock(drop.Amount) || !Finite(drop.X) || !Finite(drop.Y) || drop.X < 0 || drop.Y < 0 || drop.X >= Width || drop.Y >= Height || (drop.OwnerSettlementId != 0 && GetSettlement(drop.OwnerSettlementId) == null))
                { error = "Invalid agricultural drop."; return false; }
            }
            for (int i = 0; i < Buildings.Count; i++)
            {
                BuildingData b = Buildings[i];
                if (!Stock(b.BrewWaterStored) || b.BrewWaterStored > 4) { error = "Invalid brewing water."; return false; }
                if (b.AgriculturalStock == null || b.AgriculturalStock.Length != productCount || b.AgriculturalStock[0] != 0 || (int)b.FarmCrop < 0 || (int)b.FarmCrop > 4 || !UnitRange(b.CropGrowth) || !Stock(b.HarvestReady) || b.HarvestReady > 20 || !Stock(b.BrewingProgress) || b.BrewingProgress > 12)
                { error = "Invalid field or brewery."; return false; }
                float sum = 0;
                for (int n = 0; n < productCount; n++) { if (!Stock(b.AgriculturalStock[n])) { error = "Invalid agricultural inventory."; return false; } sum += b.AgriculturalStock[n]; }
                if (!Stock(sum) || (!StorageCapacity.IsDepot(b) && sum > 120 * b.Level + .001f) || (StorageCapacity.IsDepot(b) && !Stock(StorageCapacity.TotalUsed(b)))) { error = "Agricultural storage exceeded."; return false; }
            }
            for (int i = 0; i < Units.Count; i++)
            {
                UnitData u = Units[i];
                if (!UnitRange(u.CarriedWater)) { error = "Invalid carried water."; return false; }
                if ((int)u.HarvestProduct < 0 || (int)u.HarvestProduct >= productCount || !Stock(u.HarvestAmount) || (u.HarvestAmount > 0 && u.HarvestProduct == AgriculturalProduct.None) || (u.HarvestAmount == 0 && u.HarvestProduct != AgriculturalProduct.None) || u.BasketLevel < 0 || u.BasketLevel > 3 || !UnitRange(u.Intoxication) || !UnitRange(u.AlcoholHabit) || !Stock(u.EquipmentProgress) || u.EquipmentProgress > 12 || u.EquipmentOrder < 0 || u.EquipmentOrder > (AgricultureVersion == 1 ? 6 : 10) || u.HuntingToolLevel < 0 || u.HuntingToolLevel > 1 || u.MountId < 0 || (u.HasWagon && (!u.OwnsWagon || u.MountId == 0)))
                { error = "Invalid equipment, harvest or alcohol state."; return false; }
                if (!Stock(u.AnimalProduceTimer) || u.AnimalProduceTimer > 30 || u.AnimalOwnerId < 0 || u.HandlerId < 0 || u.PastureId < 0 || u.HusbandrySourceId < 0) { error = "Invalid domestic animal state."; return false; }
                if (u.IsDomestic)
                {
                    BuildingData pasture = GetBuilding(u.PastureId);
                    if (!WildlifeCatalog.IsAnimal(u.Species) || pasture == null || pasture.Type != BuildingType.Pasture || pasture.SettlementId != u.AnimalOwnerId || GetSettlement(u.AnimalOwnerId) == null) { error = "Invalid animal owner or pasture."; return false; }
                }
                else if (u.HandlerId != 0 || u.AnimalOwnerId != 0 || u.PastureId != 0) { error = "Wild animal has domestic links."; return false; }
                if (u.MountId != 0)
                {
                    UnitData mount = GetUnit(u.MountId);
                    if (u.Age < 16 || (int)u.Species > 3 || mount == null || !mount.IsDomestic || mount.HandlerId != u.Id || mount.AnimalOwnerId != u.SettlementId || mount.Species != u.MountSpecies || (mount.Species != SpeciesType.Donkey && mount.Species != SpeciesType.Horse && mount.Species != SpeciesType.SpiritStag) || u.MountBags != mount.HasSaddleBags || (u.HasWagon && mount.Species == SpeciesType.SpiritStag)) { error = "Invalid mount relationship."; return false; }
                }
                else if (u.HasWagon || u.MountBags) { error = "Transport flag without a mount."; return false; }
                if (u.HandlerId != 0) { UnitData handler = GetUnit(u.HandlerId); if (handler == null || handler.MountId != u.Id) { error = "Invalid handler link."; return false; } }
                if (ResourceVersion >= 2 && SimulationEngine.CarriedLoad(u) > SimulationEngine.CarryCapacityFor(u) + .0001f) { error = "Carrier exceeds equipped capacity."; return false; }
            }
            return true;
        }
    }
}
