using System;

namespace WorldGenesis.Simulation
{
    public enum IndustryRecipeType { None, Charcoal, IronIngot, CopperIngot, TinIngot, BronzeIngot, GoldIngot, SilverIngot, IronTools, BronzeTools, Sword, Spear, Armor, Leather, LinenCloth, LeatherClothes, LinenClothes, Furniture, Bow, CopperCoin, SilverCoin, GoldCoin, Rope, Ballista, Catapult, Trebuchet, BallistaBolts, StoneShot }
    public struct ItemAmount { public readonly ItemType Item; public readonly float Amount; public ItemAmount(ItemType item, float amount) { Item = item; Amount = amount; } }
    public struct AgriculturalAmount { public readonly AgriculturalProduct Product; public readonly float Amount; public AgriculturalAmount(AgriculturalProduct product, float amount) { Product = product; Amount = amount; } }
    public sealed class IndustryRecipe
    {
        public readonly IndustryRecipeType Id;
        public readonly BuildingType Building;
        public readonly UnitProfession Profession;
        public readonly string Technology;
        public readonly ItemAmount[] Inputs;
        public readonly AgriculturalAmount[] Agriculture;
        public readonly ItemType Output;
        public readonly float OutputAmount, Seconds;
        public IndustryRecipe(IndustryRecipeType id, BuildingType building, UnitProfession profession, string technology, ItemType output, float amount, float seconds, ItemAmount[] inputs, AgriculturalAmount[] agriculture = null)
        { Id = id; Building = building; Profession = profession; Technology = technology; Output = output; OutputAmount = amount; Seconds = seconds; Inputs = inputs; Agriculture = agriculture ?? Array.Empty<AgriculturalAmount>(); }
    }
    public sealed partial class BuildingData
    {
        public IndustryRecipeType ActiveIndustryRecipe;
        public int IndustryWorkerId, CompletedIndustryBatches, FurnitureCount;
        public float IndustryProgress;
    }
    public sealed partial class UnitData
    {
        public ItemType WorkTool, Clothes, DesiredIndustryEquipment;
        public int IndustryEquippedCount;
    }
    public sealed partial class WorldState
    {
        public int IndustryVersion, IndustryBatchesCompleted, WorkToolsIssued, ClothesIssued, FurnitureInstalled;
        public float[] ProducedItems = new float[ResourceCatalog.ItemCount];
        public float[] MintIssued = new float[ResourceCatalog.ItemCount];
        private bool ValidateIndustry(out string error)
        {
            error = null;
            if (IndustryVersion < 0 || IndustryVersion > 1) { error = "Unsupported industry version."; return false; }
            if (IndustryVersion == 0)
            {
                if (IndustryBatchesCompleted != 0 || WorkToolsIssued != 0 || ClothesIssued != 0 || FurnitureInstalled != 0) { error = "Legacy world cannot contain industry counters."; return false; }
                if (ProducedItems != null) foreach (float value in ProducedItems) if (value != 0) { error = "Legacy world cannot contain production ledger."; return false; }
                if (MintIssued != null) foreach (float value in MintIssued) if (value != 0) { error = "Legacy world cannot contain coin issuance."; return false; }
                foreach (BuildingData b in Buildings) if (b.ActiveIndustryRecipe != IndustryRecipeType.None || b.IndustryWorkerId != 0 || b.IndustryProgress != 0 || b.FurnitureCount != 0 || b.CompletedIndustryBatches != 0) { error = "Legacy world cannot contain industry state."; return false; }
                foreach (UnitData u in Units) if (u.WorkTool != ItemType.None || u.Clothes != ItemType.None || u.DesiredIndustryEquipment != ItemType.None || u.IndustryEquippedCount != 0 || u.Task == UnitTask.Produce || u.Task == UnitTask.InstallFurniture || u.Task == UnitTask.EquipGoods) { error = "Legacy world cannot contain industry equipment."; return false; }
                return true;
            }
            if (ResourceVersion != 4 || ProducedItems == null || MintIssued == null || ProducedItems.Length != ResourceCatalog.ItemCount || MintIssued.Length != ResourceCatalog.ItemCount || IndustryBatchesCompleted < 0 || WorkToolsIssued < 0 || ClothesIssued < 0 || FurnitureInstalled < 0)
            { error = "Invalid industry ledgers."; return false; }
            for (int i = 0; i < ResourceCatalog.ItemCount; i++) if (!Stock(ProducedItems[i]) || !Stock(MintIssued[i]) || MintIssued[i] > ProducedItems[i] || (!IndustryCatalog.IsCoin((ItemType)i) && MintIssued[i] != 0)) { error = "Invalid produced goods or coin issuance."; return false; }
            foreach (BuildingData b in Buildings)
            {
                IndustryRecipe recipe = IndustryCatalog.Get(b.ActiveIndustryRecipe);
                if (b.CompletedIndustryBatches < 0 || b.FurnitureCount < 0 || b.FurnitureCount > IndustryCatalog.FurnitureCapacity(b) || !Stock(b.IndustryProgress) || b.IndustryWorkerId < 0 || (b.ActiveIndustryRecipe != IndustryRecipeType.None && (recipe == null || recipe.Building != b.Type || b.IndustryProgress > recipe.Seconds + .001f)) || (recipe == null && (b.IndustryProgress != 0 || b.IndustryWorkerId != 0)))
                { error = "Invalid production workshop state."; return false; }
                if (b.IndustryWorkerId != 0)
                {
                    UnitData u = GetUnit(b.IndustryWorkerId);
                    if (u == null || u.Health <= 0 || u.Age < 16 || u.SettlementId != b.SettlementId || u.Task != UnitTask.Produce || u.TargetBuildingId != b.Id) { error = "Invalid production worker."; return false; }
                }
            }
            foreach (UnitData u in Units)
            {
                if ((u.WorkTool != ItemType.None && !IndustryCatalog.IsTool(u.WorkTool)) || (u.Clothes != ItemType.None && !IndustryCatalog.IsClothes(u.Clothes)) || (u.DesiredIndustryEquipment != ItemType.None && !IndustryCatalog.IsTool(u.DesiredIndustryEquipment) && !IndustryCatalog.IsClothes(u.DesiredIndustryEquipment)) || u.IndustryEquippedCount < 0 || (u.Age < 16 && (u.WorkTool != ItemType.None || u.Task == UnitTask.Produce || u.Task == UnitTask.InstallFurniture || IndustryCatalog.IsTool(u.DesiredIndustryEquipment))) || ((int)u.Species > 3 && (u.WorkTool != ItemType.None || u.Clothes != ItemType.None || u.Task == UnitTask.Produce || u.Task == UnitTask.InstallFurniture || u.Task == UnitTask.EquipGoods)))
                { error = "Invalid equipped manufactured goods."; return false; }
                if (u.Task == UnitTask.Produce)
                { BuildingData b = GetBuilding(u.TargetBuildingId); if (b == null || b.IndustryWorkerId != u.Id || b.ActiveIndustryRecipe == IndustryRecipeType.None) { error = "Production job has no matching workplace."; return false; } }
            }
            return true;
        }
    }
}
