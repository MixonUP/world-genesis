using System;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private void MigrateIndustry()
        {
            if (World.IndustryVersion == 1) return;
            World.ProducedItems = new float[ResourceCatalog.ItemCount]; World.MintIssued = new float[ResourceCatalog.ItemCount];
            World.IndustryVersion = 1;
        }
        private static float IndustryReserved(BuildingData b, ItemType item)
        {
            IndustryRecipe r = IndustryCatalog.Get(b.ActiveIndustryRecipe); if (r == null) return 0;
            foreach (ItemAmount input in r.Inputs) if (input.Item == item) return input.Amount;
            return 0;
        }
        private static float IndustryAgricultureReserved(BuildingData b, AgriculturalProduct product)
        {
            IndustryRecipe r = IndustryCatalog.Get(b.ActiveIndustryRecipe); if (r == null) return 0;
            foreach (AgriculturalAmount input in r.Agriculture) if (input.Product == product) return input.Amount;
            return 0;
        }
        private void ReleaseIndustry(UnitData unit)
        {
            if (unit.Task != UnitTask.Produce) return;
            BuildingData b = World.GetBuilding(unit.TargetBuildingId);
            if (b != null && b.IndustryWorkerId == unit.Id) b.IndustryWorkerId = 0;
        }
        private bool ProductionInputsReachable(UnitData u, SettlementData s, BuildingData destination, IndustryRecipe recipe)
        {
            foreach (ItemAmount input in recipe.Inputs)
            {
                // A neighbour's missing delivery cannot claim inputs already at this workshop.
                if (destination.Inventory[(int)input.Item] + .00001f >= input.Amount) continue;
                float amount = destination.Inventory[(int)input.Item] - PendingIndustryMaterials(s.Id, destination.Id, input.Item);
                for (int i = 0; amount + .00001f < input.Amount && i < World.Buildings.Count; i++)
                {
                    BuildingData source = World.Buildings[i]; if (source.Id == destination.Id || !UsableBuilding(source, s.Id)) continue;
                    float available = AvailableForHaul(source, input.Item) + (input.Item == ItemType.Plank && source.Type == BuildingType.Hall ? s.Wood : 0);
                    if (available > 0 && navigation.Find(World.Index((int)u.X, (int)u.Y), World.Index(source.X, source.Y), scratchPath, 4096, true) && navigation.Find(World.Index(source.X, source.Y), World.Index(destination.X, destination.Y), scratchPath, 4096, true)) amount += available;
                }
                if (input.Item == ItemType.Plank && destination.Type == BuildingType.Hall) amount += s.Wood;
                if (amount + .00001f < input.Amount) return false;
            }
            foreach (AgriculturalAmount input in recipe.Agriculture)
            {
                if (destination.AgriculturalStock[(int)input.Product] + .00001f >= input.Amount) continue;
                float amount = destination.AgriculturalStock[(int)input.Product] - PendingIndustryAgriculture(s.Id, destination.Id, input.Product);
                for (int i = 0; amount + .00001f < input.Amount && i < World.Buildings.Count; i++)
                {
                    BuildingData source = World.Buildings[i]; if (source.Id == destination.Id || !UsableBuilding(source, s.Id)) continue;
                    float available = Math.Max(0, source.AgriculturalStock[(int)input.Product] - IndustryAgricultureReserved(source, input.Product));
                    if (available > 0 && navigation.Find(World.Index((int)u.X, (int)u.Y), World.Index(source.X, source.Y), scratchPath, 4096, true) && navigation.Find(World.Index(source.X, source.Y), World.Index(destination.X, destination.Y), scratchPath, 4096, true)) amount += available;
                }
                if (amount + .00001f < input.Amount) return false;
            }
            return true;
        }
        private float PendingIndustryMaterials(int settlementId, int exceptBuilding, ItemType item)
        {
            float reserved = 0;
            foreach (BuildingData b in World.Buildings)
            {
                if (b.Id == exceptBuilding || b.SettlementId != settlementId || b.Health <= 0) continue;
                float need = IndustryReserved(b, item) - b.Inventory[(int)item];
                UnitData worker = World.GetUnit(b.IndustryWorkerId);
                if (worker != null && worker.CargoKind == item) need -= worker.CargoAmount;
                reserved += Math.Max(0, need);
            }
            return reserved;
        }
        private float PendingIndustryAgriculture(int settlementId, int exceptBuilding, AgriculturalProduct product)
        {
            float reserved = 0;
            foreach (BuildingData b in World.Buildings)
            {
                if (b.Id == exceptBuilding || b.SettlementId != settlementId || b.Health <= 0) continue;
                float need = IndustryAgricultureReserved(b, product) - b.AgriculturalStock[(int)product];
                UnitData worker = World.GetUnit(b.IndustryWorkerId);
                if (worker != null && worker.HarvestProduct == product) need -= worker.HarvestAmount;
                reserved += Math.Max(0, need);
            }
            return reserved;
        }
        private bool CanReplaceIndustryOrder(UnitData u, SettlementData s, BuildingData b, IndustryRecipeType next)
        {
            if (b.ActiveIndustryRecipe == IndustryRecipeType.None || b.ActiveIndustryRecipe == next) return true;
            // Keep an interrupted, still feasible batch. If its missing supplies were lost,
            // an idle workshop can replan (for example, make its own replacement charcoal).
            // Inputs are never consumed until completion, so cancellation loses only work time.
            IndustryRecipe previous = IndustryCatalog.Get(b.ActiveIndustryRecipe);
            return b.IndustryWorkerId == 0 && previous != null && !ProductionInputsReachable(u, s, b, previous);
        }
        public bool TryStartProduction(int unitId, int buildingId, IndustryRecipeType recipeId)
        {
            UnitData u = World.GetUnit(unitId); BuildingData b = World.GetBuilding(buildingId); IndustryRecipe recipe = IndustryCatalog.Get(recipeId);
            SettlementData s = u == null ? null : World.GetSettlement(u.SettlementId);
            if (u == null || u.Health <= 0 || u.Age < 16 || !IsCivilized(u.Species) || s == null || CarriedLoad(u) > .0001f || recipe == null || !UsableBuilding(b, s.Id) || b.Type != recipe.Building || !World.HasTechnology(s.Id, recipe.Technology) || (b.IndustryWorkerId != 0 && b.IndustryWorkerId != u.Id) || !CanReplaceIndustryOrder(u, s, b, recipeId)) return false;
            if (!ProductionInputsReachable(u, s, b, recipe) || !SetTarget(u, b.X, b.Y)) return false;
            ReleaseIndustry(u); ChangeTask(u, UnitTask.Produce); u.TargetBuildingId = b.Id; u.SourceBuildingId = 0;
            if (b.ActiveIndustryRecipe != recipeId) b.IndustryProgress = 0;
            b.ActiveIndustryRecipe = recipeId; b.IndustryWorkerId = u.Id; u.Profession = recipe.Profession; u.DecisionTimer = 2;
            return true;
        }
        public IndustryRecipeType GetAvailableProductionRecipe(int unitId, int buildingId)
        {
            UnitData u = World.GetUnit(unitId); BuildingData b = World.GetBuilding(buildingId);
            SettlementData s = u == null ? null : World.GetSettlement(u.SettlementId);
            if (u == null || u.Age < 16 || u.Health <= 0 || !IsCivilized(u.Species) || s == null || !UsableBuilding(b, s.Id) || b.IndustryWorkerId != 0 && b.IndustryWorkerId != u.Id || CarriedLoad(u) > .0001f) return IndustryRecipeType.None;
            foreach (IndustryRecipe recipe in IndustryCatalog.All)
                if (recipe.Building == b.Type && CanReplaceIndustryOrder(u, s, b, recipe.Id) && World.HasTechnology(s.Id, recipe.Technology) && ProductionInputsReachable(u, s, b, recipe)) return recipe.Id;
            return IndustryRecipeType.None;
        }
        private float ProductionStock(int settlementId, ItemType item)
        {
            float amount = World.GetSettlementStock(settlementId, item);
            foreach (UnitData u in World.Units) if (u.SettlementId == settlementId && u.CargoKind == item) amount += u.CargoAmount;
            return amount;
        }
        private bool WantsProduction(SettlementData s, IndustryRecipe recipe)
        {
            if (!World.HasTechnology(s.Id, recipe.Technology)) return false;
            float stock = ProductionStock(s.Id, recipe.Output);
            foreach (BuildingData b in World.Buildings) if (b.SettlementId == s.Id && b.ActiveIndustryRecipe == recipe.Id) stock += recipe.OutputAmount;
            return stock + .0001f < IndustryCatalog.TargetStock(recipe.Output, s.Population);
        }
        private bool ChooseProduction(UnitData u, SettlementData s)
        {
            if (u.Age < 16 || s == null || u.Hunger > .5f || u.Thirst > .65f || u.Fatigue > .72f || s.Food < Math.Max(2, s.Population * .5f) || CarriedLoad(u) > .0001f) return false;
            if (ChooseIndustryOutputHaul(u, s)) return true;
            // Resume prepared batches first. New recipes rotate by tick and worker to prevent charcoal
            // or the first recipe starving every downstream output in a shared workshop.
            foreach (BuildingData b in World.Buildings)
                if (b.SettlementId == s.Id && b.ActiveIndustryRecipe != IndustryRecipeType.None && b.IndustryWorkerId == 0 && TryStartProduction(u.Id, b.Id, b.ActiveIndustryRecipe)) return true;
            int start = (int)((World.Tick / 40 + u.Id) % IndustryCatalog.All.Length);
            for (int r = 0; r < IndustryCatalog.All.Length; r++)
            {
                IndustryRecipe recipe = IndustryCatalog.All[(start + r) % IndustryCatalog.All.Length];
                if (!WantsProduction(s, recipe)) continue;
                foreach (BuildingData b in World.Buildings)
                    if (b.SettlementId == s.Id && b.Type == recipe.Building && b.IndustryWorkerId == 0 && TryStartProduction(u.Id, b.Id, recipe.Id)) return true;
            }
            return false;
        }
        private bool ChooseIndustryOutputHaul(UnitData u, SettlementData s)
        {
            foreach (BuildingData source in World.Buildings)
            {
                if (!IndustryCatalog.IsWorkshop(source.Type) || !UsableBuilding(source, s.Id) || FreeStorage(source) > StorageCapacity.Capacity(source) * .4f) continue;
                for (int item = ResourceCatalog.SaltItemCount; item < ResourceCatalog.ItemCount; item++)
                {
                    if (AvailableForHaul(source, (ItemType)item) < .01f) continue;
                    foreach (BuildingData destination in World.Buildings)
                    {
                        if (destination.Type != BuildingType.Warehouse || !UsableBuilding(destination, s.Id) || FreeStorage(destination) < 1 || !navigation.Find(World.Index(source.X, source.Y), World.Index(destination.X, destination.Y), scratchPath, 4096, true) || !SetTarget(u, source.X, source.Y)) continue;
                        ChangeTask(u, UnitTask.Haul); u.SourceBuildingId = source.Id; u.TargetBuildingId = destination.Id; u.PickupKind = (ItemType)item; return true;
                    }
                }
            }
            return false;
        }
        private bool SupplyProduction(UnitData u, SettlementData s, BuildingData b, IndustryRecipe recipe)
        {
            bool atWork = AtBuilding(u, b);
            if (CarriedLoad(u) > .00001f)
            {
                if (!atWork) { if (!SetTarget(u, b.X, b.Y)) { ChangeTask(u, UnitTask.Idle); u.DecisionTimer = 0; } return false; }
                if (u.CargoAmount > 0)
                {
                    float amount = Math.Min(u.CargoAmount, FreeStorage(b)); b.Inventory[(int)u.CargoKind] += amount; u.CargoAmount -= amount;
                    if (u.CargoAmount <= .00001f) { u.CargoAmount = 0; u.CargoKind = ItemType.None; u.CargoOwnerId = 0; }
                }
                if (u.HarvestAmount > 0)
                {
                    float amount = Math.Min(u.HarvestAmount, StorageCapacity.FreeAgriculture(b, u.HarvestProduct)); b.AgriculturalStock[(int)u.HarvestProduct] += amount; u.HarvestAmount -= amount;
                    if (u.HarvestAmount <= .00001f) { u.HarvestAmount = 0; u.HarvestProduct = AgriculturalProduct.None; }
                }
                if (CarriedLoad(u) > .00001f) { ChangeTask(u, UnitTask.Idle); u.DecisionTimer = 0; return false; }
            }
            foreach (ItemAmount input in recipe.Inputs)
            {
                float missing = input.Amount - b.Inventory[(int)input.Item]; if (missing <= .00001f) continue;
                if (FreeStorage(b) < Math.Min(1, missing)) { ChangeTask(u, UnitTask.Idle); return false; }
                foreach (BuildingData source in World.Buildings)
                {
                    if (source.Id == b.Id || !UsableBuilding(source, s.Id)) continue;
                    float available = AvailableForHaul(source, input.Item) + (input.Item == ItemType.Plank && source.Type == BuildingType.Hall ? s.Wood : 0);
                    if (available <= .00001f || !navigation.Find(World.Index(source.X, source.Y), World.Index(b.X, b.Y), scratchPath, 4096, true)) continue;
                    if (!AtBuilding(u, source))
                    { if (SetTarget(u, source.X, source.Y)) return false; continue; }
                    float take = Math.Min(missing, Math.Min(CarrySpace(u), Math.Min(available, FreeStorage(b))));
                    if (take <= .00001f || !SetTarget(u, b.X, b.Y)) continue;
                    float stocked = Math.Min(take, AvailableForHaul(source, input.Item)); source.Inventory[(int)input.Item] -= stocked;
                    if (input.Item == ItemType.Plank && source.Type == BuildingType.Hall) s.Wood -= take - stocked;
                    u.CargoKind = input.Item; u.CargoAmount = take; u.CargoOwnerId = 0; return false;
                }
                ChangeTask(u, UnitTask.Idle); u.DecisionTimer = 0; return false;
            }
            foreach (AgriculturalAmount input in recipe.Agriculture)
            {
                float missing = input.Amount - b.AgriculturalStock[(int)input.Product]; if (missing <= .00001f) continue;
                if (StorageCapacity.FreeAgriculture(b, input.Product) < Math.Min(1, missing)) { ChangeTask(u, UnitTask.Idle); return false; }
                foreach (BuildingData source in World.Buildings)
                {
                    if (source.Id == b.Id || !UsableBuilding(source, s.Id)) continue;
                    float available = Math.Max(0, source.AgriculturalStock[(int)input.Product] - IndustryAgricultureReserved(source, input.Product));
                    if (available <= .00001f || !navigation.Find(World.Index(source.X, source.Y), World.Index(b.X, b.Y), scratchPath, 4096, true)) continue;
                    if (!AtBuilding(u, source))
                    { if (SetTarget(u, source.X, source.Y)) return false; continue; }
                    float take = Math.Min(missing, Math.Min(CarrySpace(u), Math.Min(available, StorageCapacity.FreeAgriculture(b, input.Product))));
                    if (take <= .00001f || !SetTarget(u, b.X, b.Y)) continue;
                    source.AgriculturalStock[(int)input.Product] -= take; u.HarvestProduct = input.Product; u.HarvestAmount = take; return false;
                }
                ChangeTask(u, UnitTask.Idle); u.DecisionTimer = 0; return false;
            }
            if (!atWork) { if (!SetTarget(u, b.X, b.Y)) { ChangeTask(u, UnitTask.Idle); u.DecisionTimer = 0; } return false; }
            return IndustryCatalog.HasLocalInputs(b, recipe);
        }
        private bool PerformProduction(UnitData u, SettlementData s, float dt)
        {
            if (u.Task != UnitTask.Produce) return false;
            BuildingData b = World.GetBuilding(u.TargetBuildingId); IndustryRecipe recipe = b == null ? null : IndustryCatalog.Get(b.ActiveIndustryRecipe);
            if (u.Age < 16 || s == null || !UsableBuilding(b, s.Id) || b.IndustryWorkerId != u.Id || recipe == null || !World.HasTechnology(s.Id, recipe.Technology)) { ChangeTask(u, UnitTask.Idle); u.DecisionTimer = 0; return true; }
            if (!SupplyProduction(u, s, b, recipe)) return true;
            b.IndustryProgress = Math.Min(recipe.Seconds, b.IndustryProgress + dt * (1 + (b.Level - 1) * .1f));
            GainCraftExperience(u, dt);
            if (b.IndustryProgress + .00001f < recipe.Seconds) return true;
            // Single state owner: final local check and every debit/output are one uninterrupted operation.
            if (!IndustryCatalog.HasLocalInputs(b, recipe)) return true;
            foreach (ItemAmount input in recipe.Inputs) b.Inventory[(int)input.Item] = Math.Max(0, b.Inventory[(int)input.Item] - input.Amount);
            foreach (AgriculturalAmount input in recipe.Agriculture) b.AgriculturalStock[(int)input.Product] = Math.Max(0, b.AgriculturalStock[(int)input.Product] - input.Amount);
            b.Inventory[(int)recipe.Output] += recipe.OutputAmount;
            World.ProducedItems[(int)recipe.Output] += recipe.OutputAmount;
            if (IndustryCatalog.IsCoin(recipe.Output)) World.MintIssued[(int)recipe.Output] += recipe.OutputAmount;
            b.CompletedIndustryBatches++; World.IndustryBatchesCompleted++; PayProductionWage(u, b, recipe);
            b.IndustryProgress = 0; b.ActiveIndustryRecipe = IndustryRecipeType.None; b.IndustryWorkerId = 0;
            World.ResourceRevision++; ChangeTask(u, UnitTask.Idle); u.DecisionTimer = 0; return true;
        }
        public bool TryEquipManufacturedGoods(int unitId, int buildingId, ItemType item)
        {
            UnitData u = World.GetUnit(unitId); BuildingData b = World.GetBuilding(buildingId);
            if (u == null || u.Health <= 0 || !IsCivilized(u.Species) || (!IndustryCatalog.IsTool(item) && !IndustryCatalog.IsClothes(item)) || !UsableBuilding(b, u.SettlementId) || CarriedLoad(u) > .0001f || AvailableForHaul(b, item) < 1) return false;
            if (IndustryCatalog.IsTool(item) && (u.Age < 16 || IndustryCatalog.ToolMultiplier(u.WorkTool) >= IndustryCatalog.ToolMultiplier(item)) || IndustryCatalog.IsClothes(item) && u.Clothes != ItemType.None) return false;
            if (!SetTarget(u, b.X, b.Y)) return false;
            ChangeTask(u, UnitTask.EquipGoods); u.TargetBuildingId = b.Id; u.DesiredIndustryEquipment = item; u.DecisionTimer = 1; return true;
        }
        private bool ChooseManufacturedGoods(UnitData u, SettlementData s)
        {
            if (s == null || !IsCivilized(u.Species) || CarriedLoad(u) > .0001f) return false;
            ItemType[] items = { ItemType.IronTools, ItemType.BronzeTools, ItemType.LeatherClothes, ItemType.LinenClothes };
            foreach (ItemType item in items)
            {
                if (IndustryCatalog.IsTool(item) && (u.Age < 16 || IndustryCatalog.ToolMultiplier(u.WorkTool) >= IndustryCatalog.ToolMultiplier(item)) || IndustryCatalog.IsClothes(item) && u.Clothes != ItemType.None) continue;
                foreach (BuildingData b in World.Buildings) if (b.SettlementId == s.Id && b.Inventory[(int)item] >= 1 && TryEquipManufacturedGoods(u.Id, b.Id, item)) return true;
            }
            return false;
        }
        private bool PerformManufacturedUse(UnitData u, SettlementData s)
        {
            if (u.Task != UnitTask.EquipGoods && u.Task != UnitTask.InstallFurniture) return false;
            BuildingData b = World.GetBuilding(u.TargetBuildingId);
            if (s == null || !UsableBuilding(b, s.Id) || !IsCivilized(u.Species)) { ChangeTask(u, UnitTask.Idle); u.DecisionTimer = 0; return true; }
            if (u.Task == UnitTask.EquipGoods)
            {
                ItemType item = u.DesiredIndustryEquipment;
                if (AtBuilding(u, b) && AvailableForHaul(b, item) >= 1)
                {
                    if (IndustryCatalog.IsTool(item) && u.Age >= 16 && IndustryCatalog.ToolMultiplier(item) > IndustryCatalog.ToolMultiplier(u.WorkTool))
                    {
                        b.Inventory[(int)item]--; if (u.WorkTool != ItemType.None) b.Inventory[(int)u.WorkTool]++;
                        u.WorkTool = item; World.WorkToolsIssued++; u.IndustryEquippedCount++;
                    }
                    else if (IndustryCatalog.IsClothes(item) && u.Clothes == ItemType.None)
                    { b.Inventory[(int)item]--; u.Clothes = item; World.ClothesIssued++; u.IndustryEquippedCount++; }
                }
                ChangeTask(u, UnitTask.Idle); u.DecisionTimer = 0; World.ResourceRevision++; return true;
            }
            if (u.Age < 16 || b.FurnitureCount >= IndustryCatalog.FurnitureCapacity(b)) { ChangeTask(u, UnitTask.Idle); return true; }
            if (u.CargoAmount == 0)
            {
                BuildingData source = World.GetBuilding(u.SourceBuildingId);
                if (!UsableBuilding(source, s.Id) || AvailableForHaul(source, ItemType.Furniture) < 1 || !AtBuilding(u, source) || !SetTarget(u, b.X, b.Y)) { ChangeTask(u, UnitTask.Idle); return true; }
                source.Inventory[(int)ItemType.Furniture]--; u.CargoKind = ItemType.Furniture; u.CargoAmount = 1; u.CargoOwnerId = 0; return true;
            }
            if (u.CargoKind == ItemType.Furniture && u.CargoAmount >= 1 && AtBuilding(u, b))
            { b.FurnitureCount++; World.FurnitureInstalled++; u.CargoAmount--; if (u.CargoAmount <= .00001f) { u.CargoAmount = 0; u.CargoKind = ItemType.None; } World.ResourceRevision++; }
            ChangeTask(u, UnitTask.Idle); u.DecisionTimer = 0; return true;
        }
        public bool TryInstallFurniture(int unitId, int houseId)
        {
            UnitData u = World.GetUnit(unitId); BuildingData house = World.GetBuilding(houseId);
            if (u == null || u.Health <= 0 || u.Age < 16 || !IsCivilized(u.Species) || !UsableBuilding(house, u.SettlementId) || house.FurnitureCount >= IndustryCatalog.FurnitureCapacity(house) || CarriedLoad(u) > .00001f) return false;
            foreach (BuildingData source in World.Buildings)
            {
                if (!UsableBuilding(source, u.SettlementId) || AvailableForHaul(source, ItemType.Furniture) < 1 || !navigation.Find(World.Index(source.X, source.Y), World.Index(house.X, house.Y), scratchPath, 4096, true) || !SetTarget(u, source.X, source.Y)) continue;
                ChangeTask(u, UnitTask.InstallFurniture); u.TargetBuildingId = house.Id; u.SourceBuildingId = source.Id; u.DecisionTimer = 2; return true;
            }
            return false;
        }
        private bool ChooseFurniture(UnitData u, SettlementData s)
        {
            if (u.Age < 16 || s == null || s.Food < s.Population || CarriedLoad(u) > .0001f) return false;
            foreach (BuildingData b in World.Buildings) if (b.SettlementId == s.Id && b.FurnitureCount < IndustryCatalog.FurnitureCapacity(b) && TryInstallFurniture(u.Id, b.Id)) return true;
            return false;
        }
        private void LoseManufacturedEquipment(UnitData u)
        {
            ReleaseIndustry(u);
            if (u.WorkTool != ItemType.None) World.GroundStocks.Add(new GroundStockData { Item = u.WorkTool, Amount = 1, X = u.X, Y = u.Y });
            if (u.Clothes != ItemType.None) World.GroundStocks.Add(new GroundStockData { Item = u.Clothes, Amount = 1, X = u.X, Y = u.Y });
            u.WorkTool = u.Clothes = ItemType.None;
        }
        private void DestroyIndustryBuilding(BuildingData b)
        {
            if (b.FurnitureCount > 0) { World.LostResources[(int)ItemType.Furniture] += b.FurnitureCount; b.FurnitureCount = 0; }
            foreach (UnitData u in World.Units) if (u.TargetBuildingId == b.Id && (u.Task == UnitTask.Produce || u.Task == UnitTask.InstallFurniture || u.Task == UnitTask.EquipGoods)) { ChangeTask(u, UnitTask.Idle); ClearRoute(u); u.DecisionTimer = 0; }
            b.IndustryWorkerId = 0; b.ActiveIndustryRecipe = IndustryRecipeType.None; b.IndustryProgress = 0;
        }
        private bool TryDevelopIndustry(SettlementData s)
        {
            if (s.Food < s.Population || s.BuildTimer < 8 || s.Wood < 8 || CountBuildings(s.Id, BuildingType.Sawmill) == 0 || CountBuildings(s.Id, BuildingType.House) == 0) return false;
            foreach (IndustryRecipe recipe in IndustryCatalog.All)
            {
                if (!WantsProduction(s, recipe) || CountBuildings(s.Id, recipe.Building) > 0) continue;
                bool inputs = true;
                foreach (ItemAmount input in recipe.Inputs) if (World.GetSettlementStock(s.Id, input.Item) + .00001f < input.Amount) { inputs = false; break; }
                if (!inputs) continue;
                foreach (AgriculturalAmount input in recipe.Agriculture) if (World.GetAgriculturalStock(s.Id, input.Product) + .00001f < input.Amount) { inputs = false; break; }
                if (!inputs) continue;
                int site = FindBuildingSite(s);
                if (site < 0) continue;
                // Do not pay for an automatic workshop on the far side of an impassable river.
                // The ordinary construction command still owns payment and the final site check.
                BuildingData planned = new BuildingData { X = site % World.Width, Y = site / World.Width, Type = recipe.Building, SettlementId = s.Id };
                bool reachable = false;
                foreach (UnitData worker in World.Units)
                {
                    if (worker.SettlementId != s.Id || worker.Health <= 0 || worker.Age < 16 || !IsCivilized(worker.Species)) continue;
                    if (navigation.Find(World.Index((int)worker.X, (int)worker.Y), site, scratchPath, 4096, true) && ProductionInputsReachable(worker, s, planned, recipe)) { reachable = true; break; }
                }
                if (reachable && TryBuildInfrastructure(s.Id, recipe.Building, planned.X, planned.Y)) { s.BuildTimer = 0; return true; }
            }
            return false;
        }
        private float IndustryResearchPriority(SettlementData s, TechnologyDefinition tech)
        {
            if (tech.Id == "f01_preservation" || tech.Id == "m01_forestry") return 120;
            if (tech.Id == "c01_herbalism" || tech.Id == "c02_clean_water") return 90;
            if (tech.Id == "m02_carpentry" && s.Wood >= 3) return 85;
            if (tech.Id == "t01_flax" && CountBuildings(s.Id, BuildingType.Farm) > 0) return 80;
            foreach (IndustryRecipe recipe in IndustryCatalog.All)
            {
                if (recipe.Technology != tech.Id) continue;
                bool available = true;
                foreach (ItemAmount input in recipe.Inputs) if (World.GetSettlementStock(s.Id, input.Item) < input.Amount) { available = false; break; }
                if (!available) continue;
                foreach (AgriculturalAmount input in recipe.Agriculture) if (World.GetAgriculturalStock(s.Id, input.Product) < input.Amount) { available = false; break; }
                if (available) return 75;
            }
            // Unlock metallurgical discovery from actual ore even before its charcoal intermediary exists.
            if (tech.Id == "z02_ironworking" && World.GetSettlementStock(s.Id, ItemType.IronOre) >= 2) return 72;
            if (tech.Id == "z06_nonferrous" && (World.GetSettlementStock(s.Id, ItemType.CopperOre) >= 2 || World.GetSettlementStock(s.Id, ItemType.GoldOre) >= 2 || World.GetSettlementStock(s.Id, ItemType.SilverOre) >= 2)) return 71;
            return 0;
        }
    }
}
