using System;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private void MigrateResources()
        {
            if (World.ResourceVersion >= 1)
            {
                if (World.ResourceVersion < 3)
                {
                    World.LostResources = ExpandSaltStock(World.LostResources);
                    for (int i = 0; i < World.Buildings.Count; i++)
                    {
                        BuildingData b = World.Buildings[i];
                        b.Inventory = ExpandSaltStock(b.Inventory); b.TaxStocks = ExpandSaltStock(b.TaxStocks);
                        // PersonalStocks contains typed lots, not a dense array; preserve each owner and quantity.
                    }
                    World.ResourceVersion = 3; World.ResourceRevision++; World.Revision++;
                }
                return;
            }
            ResourceCatalog.Generate(World);
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData b = World.Buildings[i];
                b.Inventory = new float[ResourceCatalog.ItemCount]; b.WorkerId = 0; b.ActiveRecipe = RecipeType.None; b.ProcessProgress = 0;
                ClearNodeForBuilding(World.Index(b.X, b.Y));
            }
            for (int i = 0; i < World.Settlements.Count; i++)
            {
                SettlementData s = World.Settlements[i]; float remaining = s.Stone;
                for (int j = 0; j < World.Buildings.Count && remaining > 0; j++)
                {
                    BuildingData b = World.Buildings[j]; if (b.SettlementId != s.Id) continue;
                    float transferred = Math.Min(remaining, FreeStorage(b)); b.Inventory[(int)ItemType.RawStone] += transferred; remaining -= transferred;
                }
                // An old settlement without surviving storage cannot retain an invisible stone warehouse.
                World.LostResources[(int)ItemType.RawStone] += remaining; s.Stone = 0;
            }
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData u = World.Units[i]; u.CargoKind = ItemType.None; u.CargoAmount = 0; u.ResourceCell = -1; u.SourceBuildingId = 0; u.TargetBuildingId = 0;
                if (u.Task == UnitTask.Gather) { ChangeTask(u, UnitTask.Idle); ClearRoute(u); u.DecisionTimer = 0; }
            }
            World.ResourceRevision++; World.TerrainRevision++; World.Revision++;
        }
        private static float[] ExpandSaltStock(float[] existing)
        {
            if (existing != null && existing.Length == ResourceCatalog.ItemCount) return existing;
            float[] expanded = new float[ResourceCatalog.ItemCount];
            if (existing != null) Array.Copy(existing, expanded, existing.Length);
            return expanded;
        }

        private void UpdateResources()
        {
            bool changed = false;
            for (int cell = 0; cell < World.ResourceNodes.Length; cell++)
            {
                ref ResourceNodeData node = ref World.ResourceNodes[cell];
                if (node.Type == ResourceType.None) continue;
                ref TileData tile = ref World.Cells[cell];
                if (node.Amount > 0 && (tile.BuildingId != 0 || !ResourceCatalog.IsCompatible(node.Type, tile))) { ExhaustNode(cell, true); changed = true; }
                if (node.Type == ResourceType.Tree && node.Amount > 0)
                {
                    float supported = node.Capacity * tile.Vegetation;
                    if (supported + .00001f < node.Amount)
                    {
                        World.LostResources[(int)ItemType.Log] += node.Amount - supported;
                        node.Amount = supported; changed = true;
                        if (node.Amount <= .02f) ExhaustNode(cell, true);
                    }
                    tile.Vegetation = node.Amount / node.Capacity;
                }
                if (node.Amount > 0 || tile.BuildingId != 0 || tile.Fire > .01f || !ResourceCatalog.IsCompatible(node.Type, tile) || (node.Type == ResourceType.Tree && !World.Laws.VegetationGrowth)) continue;
                if (node.RegrowTicksRemaining > 0) node.RegrowTicksRemaining--;
                if (node.RegrowTicksRemaining == 0)
                {
                    node.Amount = node.Capacity;
                    if (node.Type == ResourceType.Tree) { tile.Vegetation = 1; World.TerrainRevision++; }
                    changed = true;
                }
            }
            if (changed) World.ResourceRevision++;
        }

        private void ExhaustNode(int cell, bool recordLoss)
        {
            ref ResourceNodeData node = ref World.ResourceNodes[cell];
            if (node.Type == ResourceType.None) return;
            if (recordLoss && node.Amount > 0) World.LostResources[(int)ResourceCatalog.GetOutput(node.Type)] += node.Amount;
            node.Amount = 0; node.RegrowTicksRemaining = ResourceCatalog.GetRegrowTicks(node.Type);
            if (node.Type == ResourceType.Tree) World.Cells[cell].Vegetation = 0;
            World.ResourceRevision++;
        }

        private void ClearNodeForBuilding(int cell) { if (World.ResourceNodes != null) ExhaustNode(cell, true); }

        private void RefreshPaintedResource(int cell)
        {
            // Painting is an explicit world-creation power; old deposits are accounted as a sink before recreation.
            if (World.ResourceNodes[cell].Type != ResourceType.None) ExhaustNode(cell, true);
            World.ResourceNodes[cell] = ResourceCatalog.GenerateCell(World, cell);
            if (World.Cells[cell].BuildingId != 0) ExhaustNode(cell, true);
            World.ResourceRevision++;
        }

        private static float FreeStorage(BuildingData building)
        {
            return StorageCapacity.FreeMaterials(building);
        }

        private bool UsableBuilding(BuildingData building, int settlementId)
        {
            return building != null && building.Health > 0 && building.SettlementId == settlementId && World.IsWalkable(building.X, building.Y) && World.Cells[World.Index(building.X, building.Y)].Fire <= .1f;
        }

        private void ReleaseWorkshop(UnitData unit)
        {
            if (unit.Task != UnitTask.Process || unit.TargetBuildingId == 0) return;
            BuildingData b = World.GetBuilding(unit.TargetBuildingId);
            if (b != null && b.WorkerId == unit.Id) b.WorkerId = 0;
        }

        private bool ChooseCargoDelivery(UnitData unit, SettlementData home)
        {
            if (unit.CargoAmount <= 0) return false;
            BuildingType preferred = unit.CargoKind == ItemType.Log ? BuildingType.Sawmill : unit.CargoKind == ItemType.RawStone ? BuildingType.Stoneworks : unit.CargoKind == ItemType.Clay || unit.CargoKind == ItemType.Firewood ? BuildingType.Kiln : BuildingType.Hall;
            for (int pass = 0; pass < 3; pass++)
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData b = World.Buildings[i];
                bool matches = pass == 0 ? b.Type == preferred : pass == 1 ? b.Type == BuildingType.Hall : b.Type == BuildingType.Warehouse || b.Type == BuildingType.Shop;
                if (!UsableBuilding(b, home.Id) || !matches || FreeStorage(b) < .01f || !SetTarget(unit, b.X, b.Y)) continue;
                ChangeTask(unit, UnitTask.Return); unit.TargetBuildingId = b.Id; unit.SourceBuildingId = 0; return true;
            }
            // Cargo remains carried when routes or storage are unavailable; it is never credited remotely.
            return false;
        }

        private RecipeType WantedRecipe(BuildingData building, SettlementData home)
        {
            if (building.ActiveRecipe != RecipeType.None) return building.ActiveRecipe;
            if (building.Type == BuildingType.Hall || building.Type == BuildingType.Sawmill)
            {
                // Construction repeatedly consumes the tenth plank. Reserve one real fuel batch for
                // existing processing demand so an operating kiln cannot starve behind perpetual building.
                bool kilnNeedsFuel = CountBuildings(home.Id, BuildingType.Kiln) > 0 && World.GetSettlementStock(home.Id, ItemType.Clay) >= 2 && World.GetResourceBalance(home.Id, ItemType.Brick).Deficit > 0;
                bool breweryNeedsFuel = CountBuildings(home.Id, BuildingType.Brewery) > 0 && World.GetAgriculturalStock(home.Id, AgriculturalProduct.Grain) >= 2;
                if ((kilnNeedsFuel || breweryNeedsFuel) && World.GetSettlementStock(home.Id, ItemType.Firewood) < 1) return RecipeType.Firewood;
                if (home.Wood < 10) return RecipeType.Plank;
                if (World.GetResourceBalance(home.Id, ItemType.Firewood).Deficit > 0) return RecipeType.Firewood;
                if (World.GetResourceBalance(home.Id, ItemType.Plank).Deficit > 0) return RecipeType.Plank;
            }
            if (building.Type == BuildingType.Stoneworks)
            {
                if (World.GetResourceBalance(home.Id, ItemType.StoneBlock).Deficit > 0) return RecipeType.StoneBlock;
                if (World.GetResourceBalance(home.Id, ItemType.StoneTile).Deficit > 0) return RecipeType.StoneTile;
            }
            if (building.Type == BuildingType.Kiln && World.GetResourceBalance(home.Id, ItemType.Brick).Deficit > 0) return RecipeType.Brick;
            return RecipeType.None;
        }

        private static bool HasInputs(BuildingData building, RecipeType recipe)
        {
            if (recipe == RecipeType.None || building.Inventory[(int)ResourceCatalog.Input(recipe)] + .00001f < ResourceCatalog.InputAmount(recipe)) return false;
            if (recipe == RecipeType.Brick && building.Inventory[(int)ItemType.Firewood] + .00001f < 1) return false;
            float output = ResourceCatalog.Output(recipe) == ItemType.Plank ? 0 : ResourceCatalog.OutputAmount(recipe);
            return FreeStorage(building) + ResourceCatalog.InputAmount(recipe) + (recipe == RecipeType.Brick ? 1 : 0) >= output;
        }

        private static float AvailableForHaul(BuildingData building, ItemType item)
        {
            float reserved = 0;
            if (building.ActiveRecipe != RecipeType.None)
            {
                if (ResourceCatalog.Input(building.ActiveRecipe) == item) reserved = ResourceCatalog.InputAmount(building.ActiveRecipe);
                if (building.ActiveRecipe == RecipeType.Brick && item == ItemType.Firewood) reserved = 1;
            }
            return Math.Max(0, building.Inventory[(int)item] - reserved);
        }

        private bool ChooseProcessing(UnitData unit, SettlementData home)
        {
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData b = World.Buildings[i];
                if (!UsableBuilding(b, home.Id) || (b.WorkerId != 0 && b.WorkerId != unit.Id) || (pass == 0 && b.Type == BuildingType.Hall) || (pass == 1 && b.Type != BuildingType.Hall)) continue;
                RecipeType recipe = WantedRecipe(b, home);
                if ((!HasInputs(b, recipe) && !CanObtainInputs(unit, home, b, recipe)) || !SetTarget(unit, b.X, b.Y)) continue;
                ReleaseWorkshop(unit); // Reassigning Process to another workshop keeps the task enum but must release its old slot.
                ChangeTask(unit, UnitTask.Process); unit.TargetBuildingId = b.Id; unit.SourceBuildingId = 0;
                b.WorkerId = unit.Id; b.ActiveRecipe = recipe;
                unit.WorkTimer = Math.Max(0, ResourceCatalog.ProcessingSeconds(b.Type, recipe) - b.ProcessProgress); return true;
            }
            return false;
        }

        private bool ChooseHaul(UnitData unit, SettlementData home)
        {
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData destination = World.Buildings[i];
                if (!UsableBuilding(destination, home.Id) || FreeStorage(destination) < 1) continue;
                RecipeType recipe = WantedRecipe(destination, home); if (recipe == RecipeType.None) continue;
                ItemType needed = ResourceCatalog.Input(recipe);
                float required = ResourceCatalog.InputAmount(recipe);
                if (destination.Inventory[(int)needed] >= required)
                {
                    if (recipe != RecipeType.Brick || destination.Inventory[(int)ItemType.Firewood] >= 1) continue;
                    needed = ItemType.Firewood; required = 1;
                }
                for (int j = 0; j < World.Buildings.Count; j++)
                {
                    BuildingData source = World.Buildings[j];
                    if (source.Id == destination.Id || !UsableBuilding(source, home.Id) || AvailableForHaul(source, needed) < .01f) continue;
                    if (!navigation.Find(World.Index(source.X, source.Y), World.Index(destination.X, destination.Y), scratchPath, 4096, true) || !SetTarget(unit, source.X, source.Y)) continue;
                    ChangeTask(unit, UnitTask.Haul); unit.SourceBuildingId = source.Id; unit.TargetBuildingId = destination.Id;
                    unit.CargoKind = ItemType.None; unit.PickupKind = needed;
                    return true;
                }
            }
            return false;
        }

        private bool ChooseNaturalResource(UnitData unit, SettlementData home)
        {
            if (CarriedLoad(unit) > 0 || unit.Age < 16) return false;
            int preferred = (int)(StableRandom.Hash(World.Seed, unit.Id, 0, 5200) % 5);
            if (home.Wood >= 10) preferred = home.ResourceFocus == ResourceFocus.Timber ? 0 : home.ResourceFocus == ResourceFocus.Masonry ? 2 + unit.Id % 2 : home.ResourceFocus == ResourceFocus.Metals ? 4 : preferred;
            int x = (int)unit.X, y = (int)unit.Y;
            for (int pass = 0; pass < 2; pass++)
            for (int r = 0; r <= 18; r++)
            for (int oy = -r; oy <= r; oy++)
            for (int ox = -r; ox <= r; ox++)
            {
                if (Math.Abs(ox) != r && Math.Abs(oy) != r) continue;
                int nx = x + ox, ny = y + oy;
                if (!World.IsWalkable(nx, ny)) continue;
                int cell = World.Index(nx, ny); ResourceNodeData node = World.ResourceNodes[cell]; TileData tile = World.Cells[cell];
                if (node.Type == ResourceType.None || node.Amount <= 0 || tile.BuildingId != 0 || tile.Fire > .1f || !ResourceCatalog.IsCompatible(node.Type, tile) || (tile.SettlementId != 0 && tile.SettlementId != home.Id) || reservations.ContainsKey(cell)) continue;
                ItemType item = ResourceCatalog.GetOutput(node.Type);
                int group = node.Type == ResourceType.Tree ? 0 : node.Type == ResourceType.Stone ? 2 : node.Type == ResourceType.Clay ? 3 : 4;
                if (pass == 0 && group != (preferred <= 1 ? 0 : preferred)) continue;
                if (World.GetResourceBalance(home.Id, item).Deficit <= 0) continue;
                if (!SetTarget(unit, nx, ny)) continue;
                ChangeTask(unit, UnitTask.Gather); unit.ResourceCell = cell; unit.SourceBuildingId = 0; unit.TargetBuildingId = 0; reservations[cell] = unit.Id; return true;
            }
            return false;
        }

        private bool ChooseIndustry(UnitData unit, SettlementData home)
        {
            if (unit.CargoAmount > 0) return ChooseCargoDelivery(unit, home);
            return ChooseProcessing(unit, home) || ChooseHaul(unit, home) || ChooseNaturalResource(unit, home);
        }

        private bool CommittedResourceJob(UnitData unit)
        {
            if (unit.Task == UnitTask.Process)
            {
                BuildingData b = World.GetBuilding(unit.TargetBuildingId);
                return UsableBuilding(b, unit.SettlementId) && b.WorkerId == unit.Id && (HasInputs(b, b.ActiveRecipe) || CanObtainInputs(unit, World.GetSettlement(unit.SettlementId), b, b.ActiveRecipe));
            }
            if (unit.Task == UnitTask.Haul) return unit.PathCursor < unit.Path.Count && (unit.GroundStockIndex >= 0 || (World.GetBuilding(unit.SourceBuildingId) != null && World.GetBuilding(unit.TargetBuildingId) != null));
            if (unit.Task != UnitTask.Gather || unit.ResourceCell < 0 || unit.ResourceCell >= World.ResourceNodes.Length) return false;
            return CarrySpace(unit) > .001f && World.ResourceNodes[unit.ResourceCell].Amount > 0;
        }

        private bool WorkResources(UnitData unit, SettlementData home, float dt)
        {
            if (PickUpGroundStock(unit)) return true;
            if (unit.Task == UnitTask.Gather)
            {
                int cell = unit.ResourceCell;
                if (cell < 0 || cell >= World.ResourceNodes.Length || DistanceSquared(unit.X, unit.Y, cell % World.Width + .5f, cell / World.Width + .5f) > 1.01f) { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return true; }
                ref ResourceNodeData node = ref World.ResourceNodes[cell];
                if (node.Amount <= 0 || World.Cells[cell].Fire > .1f || World.Cells[cell].BuildingId != 0 || !ResourceCatalog.IsCompatible(node.Type, World.Cells[cell])) { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return true; }
                ItemType item = ResourceCatalog.GetOutput(node.Type);
                if (unit.CargoAmount > 0 && unit.CargoKind != item) { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return true; }
                float amount = Math.Min(node.Amount, Math.Min(CarrySpace(unit), ResourceCatalog.HarvestPerSecond(node.Type) * dt));
                unit.CargoKind = item; unit.CargoOwnerId = unit.Id; unit.CargoAmount += amount; node.Amount = Math.Max(0, node.Amount - amount);
                if (amount > 0) GainCraftExperience(unit, dt);
                if (node.Type == ResourceType.Tree) { World.Cells[cell].Vegetation = node.Amount / node.Capacity; World.TerrainRevision++; }
                if (node.Amount <= .0001f) { node.Amount = 0; node.RegrowTicksRemaining = ResourceCatalog.GetRegrowTicks(node.Type); if (node.Type == ResourceType.Tree) World.Cells[cell].Vegetation = 0; }
                World.ResourceRevision++;
                if (node.Amount == 0 || CarrySpace(unit) <= .001f) { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; }
                return true;
            }
            if (unit.Task == UnitTask.Haul)
            {
                BuildingData source = World.GetBuilding(unit.SourceBuildingId), destination = World.GetBuilding(unit.TargetBuildingId);
                ItemType item = unit.PickupKind;
                if (!UsableBuilding(source, home.Id) || !UsableBuilding(destination, home.Id) || (int)item <= 0 || (int)item >= ResourceCatalog.ItemCount || DistanceSquared(unit.X, unit.Y, source.X + .5f, source.Y + .5f) > 1.01f) { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return true; }
                float amount = Math.Min(CarrySpace(unit), Math.Min(AvailableForHaul(source, item), FreeStorage(destination)));
                if (amount <= .0001f) { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return true; }
                source.Inventory[(int)item] -= amount; unit.CargoKind = item; unit.CargoAmount = amount; unit.CargoOwnerId = 0;
                int destinationId = destination.Id;
                if (SetTarget(unit, destination.X, destination.Y)) { ChangeTask(unit, UnitTask.Return); unit.SourceBuildingId = 0; unit.TargetBuildingId = destinationId; }
                else { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; }
                World.ResourceRevision++; return true;
            }
            if (unit.Task == UnitTask.Return && unit.CargoAmount > 0)
            {
                BuildingData b = World.GetBuilding(unit.TargetBuildingId);
                if (!UsableBuilding(b, home.Id) || DistanceSquared(unit.X, unit.Y, b.X + .5f, b.Y + .5f) > 1.01f) { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return true; }
                float deposited = Math.Min(unit.CargoAmount, FreeStorage(b));
                DepositMaterial(unit, b, deposited); unit.CargoAmount -= deposited;
                if (unit.CargoAmount <= .0001f) { unit.CargoAmount = 0; unit.CargoKind = ItemType.None; unit.CargoOwnerId = 0; }
                ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; World.ResourceRevision++; return true;
            }
            if (unit.Task != UnitTask.Process) return false;
            BuildingData workshop = World.GetBuilding(unit.TargetBuildingId);
            if (!UsableBuilding(workshop, home.Id) || workshop.WorkerId != unit.Id || DistanceSquared(unit.X, unit.Y, workshop.X + .5f, workshop.Y + .5f) > 1.01f) { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return true; }
            AcquireRecipeInputs(unit, home, workshop);
            if (!HasInputs(workshop, workshop.ActiveRecipe)) { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return true; }
            RecipeType recipeType = workshop.ActiveRecipe;
            float duration = ResourceCatalog.ProcessingSeconds(workshop.Type, recipeType);
            GainCraftExperience(unit, dt);
            workshop.ProcessProgress = Math.Min(duration, workshop.ProcessProgress + dt); unit.WorkTimer = Math.Max(0, duration - workshop.ProcessProgress);
            if (workshop.ProcessProgress + .00001f >= duration)
            {
                workshop.Inventory[(int)ResourceCatalog.Input(recipeType)] = Math.Max(0, workshop.Inventory[(int)ResourceCatalog.Input(recipeType)] - ResourceCatalog.InputAmount(recipeType));
                if (recipeType == RecipeType.Brick) workshop.Inventory[(int)ItemType.Firewood] = Math.Max(0, workshop.Inventory[(int)ItemType.Firewood] - 1);
                ItemType output = ResourceCatalog.Output(recipeType); float amount = ResourceCatalog.OutputAmount(recipeType);
                if (output == ItemType.Plank) home.Wood += amount;
                else workshop.Inventory[(int)output] += amount;
                workshop.ProcessProgress = 0; workshop.ActiveRecipe = RecipeType.None; workshop.WorkerId = 0;
                ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; World.ResourceRevision++;
            }
            return true;
        }

        private void LoseCargo(UnitData unit)
        {
            ReleaseWorkshop(unit);
            ReleasePrivateGoods(unit);
            if (unit.CargoAmount > 0) World.LostResources[(int)unit.CargoKind] += unit.CargoAmount;
            if (unit.CarriedWood > 0) World.LostResources[(int)ItemType.Plank] += unit.CarriedWood;
        }

        private void DestroyResourceStorage(BuildingData building)
        {
            for (int i = 1; i < building.Inventory.Length; i++) World.LostResources[i] += building.Inventory[i];
            for (int i = 1; i < building.TaxStocks.Length; i++) World.LostResources[i] += building.TaxStocks[i];
            for (int i = 0; i < building.PersonalStocks.Count; i++) World.LostResources[(int)building.PersonalStocks[i].Item] += building.PersonalStocks[i].Amount;
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData unit = World.Units[i];
                if (unit.SourceBuildingId != building.Id && unit.TargetBuildingId != building.Id) continue;
                ChangeTask(unit, UnitTask.Idle); ClearRoute(unit); unit.SourceBuildingId = 0; unit.TargetBuildingId = 0; unit.DecisionTimer = 0;
            }
            World.ResourceRevision++;
        }
    }
}
