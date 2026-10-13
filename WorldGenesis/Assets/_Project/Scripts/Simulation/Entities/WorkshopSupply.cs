using System;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private float CraftingPlanksAvailable(SettlementData s)
        {
            float amount = s.Wood;
            for (int i = 0; i < World.Buildings.Count; i++) if (UsableBuilding(World.Buildings[i], s.Id)) amount += World.Buildings[i].Inventory[(int)ItemType.Plank];
            return amount;
        }
        private static ItemType SupplyMaterial(int slot) { return slot == 0 ? ItemType.Plank : slot == 1 ? ItemType.RawStone : slot == 5 ? ItemType.Firewood : ItemType.None; }
        private static AgriculturalProduct SupplyHarvest(int slot) { return slot == 2 ? AgriculturalProduct.Fiber : slot == 3 ? AgriculturalProduct.Hide : slot == 4 ? AgriculturalProduct.Grain : slot == 7 ? AgriculturalProduct.Hay : slot == 8 ? AgriculturalProduct.Berries : slot == 9 ? AgriculturalProduct.RiverPearl : slot == 10 ? AgriculturalProduct.AncientScale : AgriculturalProduct.None; }
        private static float SupplyStored(BuildingData b, int slot)
        {
            if (slot == 6) return b.BrewWaterStored;
            ItemType item = SupplyMaterial(slot); return item != ItemType.None ? b.Inventory[(int)item] : b.AgriculturalStock[(int)SupplyHarvest(slot)];
        }
        private float SupplyAvailable(BuildingData source, SettlementData s, int slot)
        {
            if (slot == 6) return source.Type == BuildingType.Well ? source.WaterStored : 0;
            ItemType item = SupplyMaterial(slot);
            if (item != ItemType.None) return AvailableForHaul(source, item) + (slot == 0 && source.Type == BuildingType.Hall ? s.Wood : 0);
            AgriculturalProduct product = SupplyHarvest(slot);
            return Math.Max(0, source.AgriculturalStock[(int)product] - IndustryAgricultureReserved(source, product));
        }
        // Both path legs are checked before pickup. The resident carries each batch; a workshop never debits a remote ledger.
        private bool SupplyAgriculturalWorkshop(UnitData u, SettlementData s, BuildingData b, float plank, float stone, float fiber, float hide, float grain, float fuel, float water, float hay = 0, float berries = 0, float pearls = 0, float scales = 0)
        {
            bool atWorkplace = AtBuilding(u, b);
            if (!atWorkplace && CarriedLoad(u) > .00001f) { if (!SetTarget(u, b.X, b.Y)) ChangeTask(u, UnitTask.Idle); return false; }
            if (atWorkplace)
            {
                if (u.CargoAmount > 0) { float amount = Math.Min(u.CargoAmount, FreeStorage(b)); b.Inventory[(int)u.CargoKind] += amount; u.CargoAmount -= amount; if (u.CargoAmount <= .00001f) { u.CargoAmount = 0; u.CargoKind = ItemType.None; } }
                if (u.HarvestAmount > 0) { float amount = Math.Min(u.HarvestAmount, StorageCapacity.FreeAgriculture(b, u.HarvestProduct)); b.AgriculturalStock[(int)u.HarvestProduct] += amount; u.HarvestAmount -= amount; if (u.HarvestAmount <= .00001f) { u.HarvestAmount = 0; u.HarvestProduct = AgriculturalProduct.None; } }
                if (u.CarriedWater > 0) { float amount = Math.Min(u.CarriedWater, 4 - b.BrewWaterStored); b.BrewWaterStored += amount; u.CarriedWater -= amount; }
                if (CarriedLoad(u) > .00001f) { ChangeTask(u, UnitTask.Idle); return false; }
            }
            for (int slot = 0; slot < 11; slot++)
            {
                float required = slot == 0 ? plank : slot == 1 ? stone : slot == 2 ? fiber : slot == 3 ? hide : slot == 4 ? grain : slot == 5 ? fuel : slot == 6 ? water : slot == 7 ? hay : slot == 8 ? berries : slot == 9 ? pearls : scales;
                float missing = required - SupplyStored(b, slot); if (missing <= .00001f) continue;
                // A first basket is woven beside the hearth; workshop planks still need withdrawing from the hearth pile.
                for (int i = 0; i < World.Buildings.Count; i++)
                {
                    BuildingData source = World.Buildings[i];
                    if (!UsableBuilding(source, s.Id) || SupplyAvailable(source, s, slot) <= .00001f || (source.Id == b.Id && !(slot == 0 && b.Type == BuildingType.Hall))) continue;
                    if (!AtBuilding(u, source)) continue;
                    if (!navigation.Find(World.Index(source.X, source.Y), World.Index(b.X, b.Y), scratchPath)) continue;
                    float amount = Math.Min(missing, Math.Min(CarrySpace(u), SupplyAvailable(source, s, slot)));
                    if (slot == 6) amount = Math.Min(1, amount);
                    if (amount <= .00001f) continue;
                    if (slot == 6) { source.WaterStored -= amount; u.CarriedWater += amount; }
                    else if (SupplyMaterial(slot) != ItemType.None)
                    {
                        ItemType item = SupplyMaterial(slot); float stocked = Math.Min(amount, AvailableForHaul(source, item)); source.Inventory[(int)item] -= stocked;
                        if (slot == 0 && source.Type == BuildingType.Hall) s.Wood -= amount - stocked;
                        u.CargoKind = item; u.CargoAmount += amount; u.CargoOwnerId = 0;
                    }
                    else { AgriculturalProduct product = SupplyHarvest(slot); source.AgriculturalStock[(int)product] -= amount; u.HarvestProduct = product; u.HarvestAmount += amount; }
                    SetTarget(u, b.X, b.Y); return false;
                }
                if (CarriedLoad(u) > .00001f) { if (!SetTarget(u, b.X, b.Y)) ChangeTask(u, UnitTask.Idle); return false; }
                for (int i = 0; i < World.Buildings.Count; i++)
                {
                    BuildingData source = World.Buildings[i];
                    if (source.Id == b.Id || !UsableBuilding(source, s.Id) || SupplyAvailable(source, s, slot) <= .00001f || !navigation.Find(World.Index(source.X, source.Y), World.Index(b.X, b.Y), scratchPath)) continue;
                    if (SetTarget(u, source.X, source.Y)) return false;
                }
                ChangeTask(u, UnitTask.Idle); return false;
            }
            if (!atWorkplace) { if (!SetTarget(u, b.X, b.Y)) ChangeTask(u, UnitTask.Idle); return false; }
            return true;
        }
    }
}
