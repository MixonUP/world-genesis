using System;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        public static float CarryCapacityFor(UnitData unit)
        {
            if (unit.Age < 16 && (int)unit.Species <= 3) return 1;
            if (unit.MountId != 0)
            {
                if (unit.HasWagon) return 150;
                return (unit.MountSpecies == SpeciesType.Horse ? 75 : 50) + (unit.MountBags ? 25 : 0);
            }
            return unit.BasketLevel == 3 ? 25 : unit.BasketLevel == 2 ? 15 : unit.BasketLevel == 1 ? 10 : 1;
        }
        private void MigrateAgriculture()
        {
            if (World.AgricultureVersion == 2) return;
            if (World.AgricultureVersion == 0)
            {
                World.AgriculturalDrops = new System.Collections.Generic.List<AgriculturalDrop>();
                World.LostAgriculture = new float[FishingCatalog.AgriculturalProductCount];
                for (int i = 0; i < World.Buildings.Count; i++) InitializeAgricultureBuilding(World.Buildings[i]);
            }
            else
            {
                Array.Resize(ref World.LostAgriculture, FishingCatalog.AgriculturalProductCount);
                for (int i = 0; i < World.Buildings.Count; i++) Array.Resize(ref World.Buildings[i].AgriculturalStock, FishingCatalog.AgriculturalProductCount);
            }
            World.AgricultureVersion = 2;
        }
        private void InitializeAgricultureBuilding(BuildingData b)
        {
            b.AgriculturalStock = new float[FishingCatalog.AgriculturalProductCount]; b.CropGrowth = b.HarvestReady = b.BrewingProgress = 0;
            if (b.Type == BuildingType.Farm)
            {
                int earlier = 0;
                for (int i = 0; i < World.Buildings.Count; i++) if (World.Buildings[i].SettlementId == b.SettlementId && World.Buildings[i].Type == BuildingType.Farm && World.Buildings[i].Id < b.Id) earlier++;
                b.FarmCrop = (CropType)(earlier % 5);
            }
        }
        private static AgriculturalProduct CropProduct(CropType crop)
        {
            return crop == CropType.Flax ? AgriculturalProduct.Fiber : crop == CropType.BerryOrchard ? AgriculturalProduct.Berries : crop == CropType.HerbGarden ? AgriculturalProduct.Herbs : crop == CropType.Hay ? AgriculturalProduct.Hay : AgriculturalProduct.Grain;
        }
        public void DropAgriculturalLoot(UnitData at, AgriculturalProduct product, float amount)
        {
            if (at == null || amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount) || (int)product < 1 || (int)product >= FishingCatalog.AgriculturalProductCount) return;
            for (int i = 0; i < World.AgriculturalDrops.Count; i++)
            {
                AgriculturalDrop drop = World.AgriculturalDrops[i];
                if (drop.Product == product && drop.X == at.X && drop.Y == at.Y && drop.OwnerSettlementId == 0) { drop.Amount += amount; return; }
            }
            World.AgriculturalDrops.Add(new AgriculturalDrop { Product = product, Amount = amount, X = at.X, Y = at.Y });
        }
        private float AgriStorageSpace(BuildingData b)
        {
            return StorageCapacity.FreeAgriculture(b);
        }
        private bool SpendAgriculture(int settlementId, AgriculturalProduct product, float amount)
        {
            if (amount <= 0) return true;
            if (World.GetAgriculturalStock(settlementId, product) + .00001f < amount) return false;
            for (int i = 0; i < World.Buildings.Count && amount > .00001f; i++)
            {
                BuildingData b = World.Buildings[i]; if (b.SettlementId != settlementId || b.Health <= 0) continue;
                float spent = Math.Min(amount, b.AgriculturalStock[(int)product]); b.AgriculturalStock[(int)product] -= spent; amount -= spent;
            }
            return true;
        }
        private void UpdateAgriculture()
        {
            if (World.Tick % 10 != 0) return;
            UpdateDomesticAnimals();
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData b = World.Buildings[i]; if (b.Health <= 0) continue;
                if (b.Type == BuildingType.Brewery) b.BrewingProgress = 0;
                TileData tile = World.Cells[World.Index(b.X, b.Y)];
                if (b.Type == BuildingType.Farm && b.HarvestReady <= .00001f && tile.Fire <= .01f && PlacementRules.IsDryGround(World,b.X,b.Y))
                {
                    float water = Math.Max(.2f, tile.Moisture);
                    b.CropGrowth = Math.Min(1, b.CropGrowth + (.005f + .01f * water) * (1 + .15f * (b.Level - 1)) * World.GetWeatherGrowthMultiplier(World.Index(b.X, b.Y)));
                    if (b.CropGrowth >= 1) b.HarvestReady = b.FarmCrop == CropType.BerryOrchard ? 16 : 12;
                }
                // Delivered food becomes the existing town meal reserve. Dedicated cooks and meal hauling are a later system.
                // Legacy food in material warehouses remains usable rather than being deleted after migration.
                if (b.Type == BuildingType.Hall || b.Type == BuildingType.Warehouse || b.Type == BuildingType.Granary)
                {
                    SettlementData s = World.GetSettlement(b.SettlementId);
                    AgriculturalProduct[] food = { AgriculturalProduct.Grain, AgriculturalProduct.Berries, AgriculturalProduct.Meat, AgriculturalProduct.Milk, AgriculturalProduct.Egg, AgriculturalProduct.Fish, AgriculturalProduct.DelicacyFish };
                    for (int p = 0; p < food.Length; p++)
                    {
                        float reserve = food[p] == AgriculturalProduct.Grain && s != null && World.HasTechnology(s.Id, "f08_brewing") && s.Food > s.Population * 2 ? 4 : 0;
                        float amount = Math.Min(1, Math.Max(0, b.AgriculturalStock[(int)food[p]] - reserve));
                        if (s != null && amount > 0 && s.Food < s.Population * 8 + 20)
                        { b.AgriculturalStock[(int)food[p]] -= amount; s.Food += amount * (food[p] == AgriculturalProduct.DelicacyFish ? 4 : food[p] == AgriculturalProduct.Fish ? 1 : food[p] == AgriculturalProduct.Meat ? 2.5f : food[p] == AgriculturalProduct.Grain ? 2 : 1); }
                    }
                }
            }
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData u = World.Units[i];
                if (u.Task == UnitTask.Brew) { BuildingData brewery = World.GetBuilding(u.TargetBuildingId); if (brewery != null) brewery.BrewingProgress = Math.Max(brewery.BrewingProgress, Math.Min(12, u.WorkTimer)); }
                u.Intoxication = Math.Max(0, u.Intoxication - .008f); u.AlcoholHabit = Math.Max(0, u.AlcoholHabit - .0003f);
                if (IsCivilized(u.Species) && u.Age >= 16 && u.AlcoholHabit > .7f && u.Intoxication > .7f) { u.Health -= .04f; u.EventStress = Math.Min(1, u.EventStress + .002f); }
                EnforceEquipment(u);
            }
            // Empty records are removed only after jobs complete; drop jobs use product/location, not unstable indices.
            for (int i = World.AgriculturalDrops.Count - 1; i >= 0; i--) if (World.AgriculturalDrops[i].Amount <= .00001f) World.AgriculturalDrops.RemoveAt(i);
        }
        private void EnforceEquipment(UnitData u)
        {
            UnitData mount = World.GetUnit(u.MountId);
            bool valid = mount != null && mount.Health > 0 && mount.IsDomestic && mount.HandlerId == u.Id && mount.AnimalOwnerId == u.SettlementId && u.Age >= 16 && IsCivilized(u.Species) && DistanceSquared(u.X, u.Y, mount.X, mount.Y) < 36;
            if (valid) { u.MountBags = mount.HasSaddleBags; u.HasWagon = u.OwnsWagon && (mount.Species == SpeciesType.Donkey || mount.Species == SpeciesType.Horse); }
            if (u.MountId != 0 && !valid)
            {
                if (mount != null && mount.HandlerId == u.Id) mount.HandlerId = 0;
                u.MountId = 0; u.MountBags = u.HasWagon = false;
            }
            if (u.MountId == 0) u.MountBags = u.HasWagon = false;
            float capacity = CarryCapacityFor(u), extra = Math.Max(0, CarriedLoad(u) - capacity);
            if (extra <= .00001f) return;
            float spill = Math.Min(extra, u.CarriedWater); u.CarriedWater -= spill; extra -= spill;
            spill = Math.Min(extra, u.HarvestAmount); if (spill > 0) { DropAgriculturalLoot(u, u.HarvestProduct, spill); u.HarvestAmount -= spill; extra -= spill; if (u.HarvestAmount <= 0) u.HarvestProduct = AgriculturalProduct.None; }
            spill = Math.Min(extra, u.CargoAmount); if (spill > 0) { PutOnGround(u, u.CargoKind, spill, false); u.CargoAmount -= spill; extra -= spill; if (u.CargoAmount <= 0) u.CargoKind = ItemType.None; }
            spill = Math.Min(extra, u.CarriedWood); if (spill > 0) { PutOnGround(u, ItemType.Plank, spill, false); u.CarriedWood -= spill; extra -= spill; }
            spill = Math.Min(extra, u.CarriedFood); if (spill > 0) { PutOnGround(u, ItemType.None, spill, true); u.CarriedFood -= spill; }
        }
        private bool ChooseHarvestDelivery(UnitData unit, SettlementData home)
        {
            if (unit.HarvestAmount <= 0 || home == null) return false;
            for (int pass = 0; pass < 2; pass++) for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData b = World.Buildings[i];
                BuildingType depot = StorageCapacity.IsFood(unit.HarvestProduct) ? BuildingType.Granary : BuildingType.Warehouse;
                if (b.SettlementId != home.Id || b.Type != (pass == 0 ? depot : BuildingType.Hall) || !UsableBuilding(b, home.Id) || StorageCapacity.FreeAgriculture(b, unit.HarvestProduct) < .01f || !SetTarget(unit, b.X, b.Y)) continue;
                ChangeTask(unit, UnitTask.Return); unit.TargetBuildingId = b.Id; return true;
            }
            return false;
        }
        private bool PerformAgriDelivery(UnitData unit, SettlementData home, float dt)
        {
            if (unit.Task != UnitTask.Return || unit.HarvestAmount <= 0) return false;
            BuildingData b = World.GetBuilding(unit.TargetBuildingId);
            if (b != null && b.SettlementId == unit.SettlementId && UsableBuilding(b, unit.SettlementId) && AtBuilding(unit, b))
            {
                float amount = Math.Min(unit.HarvestAmount, StorageCapacity.FreeAgriculture(b, unit.HarvestProduct)); b.AgriculturalStock[(int)unit.HarvestProduct] += amount; unit.HarvestAmount -= amount;
                if (unit.HarvestAmount <= .00001f) { unit.HarvestAmount = 0; unit.HarvestProduct = AgriculturalProduct.None; }
            }
            ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return true;
        }
        private bool PerformAgriculture(UnitData unit, SettlementData home, float dt)
        {
            if (unit.Task != UnitTask.Farm) return false;
            BuildingData farm = unit.TargetCell >= 0 && unit.TargetCell < World.Cells.Length ? World.GetBuilding(World.Cells[unit.TargetCell].BuildingId) : null;
            if (farm == null || farm.Type != BuildingType.Farm || !UsableBuilding(farm, home.Id) || DistanceSquared(unit.X, unit.Y, farm.X + .5f, farm.Y + .5f) > 2) return false;
            if (farm.HarvestReady <= 0) { unit.WorkTimer += dt; if (unit.WorkTimer >= 2) { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 1; } return true; }
            AgriculturalProduct product = CropProduct(farm.FarmCrop);
            if (unit.HarvestAmount > 0 && unit.HarvestProduct != product) { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return true; }
            float amount = Math.Min(farm.HarvestReady, Math.Min(CarrySpace(unit), dt * .65f));
            if (amount > 0) { farm.HarvestReady -= amount; unit.HarvestProduct = product; unit.HarvestAmount += amount; GainCraftExperience(unit, dt); }
            if (farm.HarvestReady <= .00001f) { farm.HarvestReady = 0; farm.CropGrowth = 0; }
            if (CarrySpace(unit) <= .001f || farm.HarvestReady == 0) { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; }
            return true;
        }
        private bool ChooseLoot(UnitData unit, SettlementData home)
        {
            if (unit.Age < 16 || home == null || unit.HarvestAmount > 0 || CarrySpace(unit) <= .01f) return false;
            AgriculturalDrop best = null; float nearest = 18 * 18;
            float homeRange = unit.Profession == UnitProfession.Hunter && unit.HuntingToolLevel > 0 ? 20 : 12;
            for (int i = 0; i < World.AgriculturalDrops.Count; i++)
            {
                AgriculturalDrop drop = World.AgriculturalDrops[i];
                float distance = DistanceSquared(unit.X, unit.Y, drop.X, drop.Y);
                float range = drop.Product == AgriculturalProduct.RiverPearl || drop.Product == AgriculturalProduct.AncientScale ? 18 : homeRange;
                if (drop.Amount <= 0 || (drop.OwnerSettlementId != 0 && drop.OwnerSettlementId != home.Id) || distance >= nearest || DistanceSquared(home.X + .5f, home.Y + .5f, drop.X, drop.Y) > range * range) continue;
                if (drop.Product == AgriculturalProduct.Meat && home.Food > home.Population * 6) continue;
                if (drop.Product == AgriculturalProduct.Hide && World.GetAgriculturalStock(home.Id, drop.Product) >= home.Population * .5f + 8) continue;
                bool canReturn = false;
                for (int b = 0; b < World.Buildings.Count && !canReturn; b++)
                {
                    BuildingData store = World.Buildings[b];
                    if ((store.Type == BuildingType.Hall || StorageCapacity.IsDepot(store)) && UsableBuilding(store, home.Id) && StorageCapacity.FreeAgriculture(store, drop.Product) >= .01f)
                        canReturn = navigation.Find(World.Index((int)drop.X, (int)drop.Y), World.Index(store.X, store.Y), scratchPath);
                }
                if (canReturn) { best = drop; nearest = distance; }
            }
            if (best == null) return false;
            if (nearest <= 2)
            {
                float amount = Math.Min(CarrySpace(unit), best.Amount); unit.HarvestProduct = best.Product; unit.HarvestAmount = amount; best.Amount -= amount; return ChooseHarvestDelivery(unit, home);
            }
            if (!SetTarget(unit, (int)best.X, (int)best.Y)) return false;
            ChangeTask(unit, UnitTask.Wander); unit.DecisionTimer = 2; return true;
        }
        private void LoseAgriculturalStorage(BuildingData b)
        {
            if (b.AgriculturalStock == null) return;
            for (int i = 1; i < b.AgriculturalStock.Length; i++) World.LostAgriculture[i] += b.AgriculturalStock[i];
        }
        private void DropAgriculturalCargo(UnitData u)
        {
            u.CarriedWater = 0; // Unsealed water spills; solid goods remain collectible.
            if (u.HarvestAmount > 0) { DropAgriculturalLoot(u, u.HarvestProduct, u.HarvestAmount); u.HarvestAmount = 0; u.HarvestProduct = AgriculturalProduct.None; }
            if (u.MountId != 0) { UnitData mount = World.GetUnit(u.MountId); if (mount != null && mount.HandlerId == u.Id) mount.HandlerId = 0; u.MountId = 0; u.HasWagon = false; }
        }
    }
}
