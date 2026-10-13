using System;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        public bool RequestMarketNeed(int settlementId, ItemType item, float target, string reason)
        {
            if (World.GetSettlement(settlementId) == null || !MarketCatalog.Tradable(item) || float.IsNaN(target) || float.IsInfinity(target) || target <= 0 || target > 10000) return false;
            MarketNeedData need = FindMarketNeed(settlementId, item);
            if (need == null) { need = new MarketNeedData { SettlementId = settlementId, Item = item }; World.MarketNeeds.Add(need); }
            need.Target = Math.Max(need.Target, target); need.ExpiresTick = World.Tick + 6000; need.UpdatedTick = World.Tick; need.State = MarketNeedState.Requested;
            need.Reason = string.IsNullOrEmpty(reason) ? "Запрошен реальный товар" : reason.Substring(0, Math.Min(256, reason.Length)); return true;
        }
        private MarketNeedData FindMarketNeed(int settlement, ItemType item)
        { foreach (MarketNeedData need in World.MarketNeeds) if (need.SettlementId == settlement && need.Item == item) return need; return null; }
        private void SetAutomaticNeed(SettlementData s, ItemType item, bool wanted)
        {
            MarketNeedData n = FindMarketNeed(s.Id, item);
            if (n != null && !n.Automatic && n.ExpiresTick > World.Tick) return;
            if (!wanted) { if (n != null) { n.State = MarketNeedState.Satisfied; n.Reason = "Потребность обеспечена"; n.UpdatedTick = World.Tick; } return; }
            if (n == null) { n = new MarketNeedData { SettlementId = s.Id, Item = item, Automatic = true }; World.MarketNeeds.Add(n); }
            n.Automatic = true; n.Target = 1; n.ExpiresTick = 0;
            if (ProductionStock(s.Id, item) >= 1) { n.State = MarketNeedState.Satisfied; n.Reason = "Изделие изготовлено; ожидает физической выдачи"; }
            else if (n.State == MarketNeedState.Satisfied) { n.State = MarketNeedState.Requested; n.Reason = "Следующему жителю требуется изделие"; }
            n.UpdatedTick = World.Tick;
        }
        private void UpdateMarketNeeds()
        {
            foreach (SettlementData s in World.Settlements)
            {
                int tools = 0, clothes = 0;
                foreach (UnitData u in World.Units) if (u.SettlementId == s.Id && u.Health > 0 && u.Age >= 16 && IsCivilized(u.Species)) { if (u.WorkTool == ItemType.None) tools++; if (u.Clothes == ItemType.None) clothes++; }
                SetAutomaticNeed(s, ItemType.IronTools, tools > 0);
                SetAutomaticNeed(s, ItemType.LinenClothes, clothes > 0);
            }
            foreach (MarketNeedData n in World.MarketNeeds)
            {
                if (!n.Automatic && n.ExpiresTick <= World.Tick) { n.State = MarketNeedState.Satisfied; n.Reason = "Срок внешнего запроса истёк"; n.UpdatedTick = World.Tick; }
                else if (ProductionStock(n.SettlementId, n.Item) >= n.Target) { n.State = MarketNeedState.Satisfied; n.Reason = "Необходимый запас доступен; требуется доставка к потребителю"; n.UpdatedTick = World.Tick; }
            }
        }
        private void ExplainMarketNeed(MarketNeedData need, MarketNeedState state, string reason)
        { need.State = state; need.Reason = reason; need.UpdatedTick = World.Tick; }
        private IndustryRecipe RecipeForMarketItem(ItemType item)
        { foreach (IndustryRecipe recipe in IndustryCatalog.All) if (recipe.Output == item) return recipe; return null; }
        private bool TryChooseProductionOrder(UnitData u, SettlementData s)
        {
            if (s == null || !MarketAdult(u, s.Id) || CarriedLoad(u) > .00001f || u.Hunger > .5f || u.Thirst > .65f || u.Fatigue > .72f || s.Food < Math.Max(2, s.Population * .5f) || CountBuildings(s.Id, BuildingType.Sawmill) == 0) return false;
            // Keep food and construction workers available; a town's entire population cannot be drafted into one chain.
            int employed = 0;
            foreach (UnitData worker in World.Units) if (worker.Id != u.Id && worker.SettlementId == s.Id && worker.Task == UnitTask.Produce) employed++;
            if (employed >= Math.Max(1, s.Population / 3)) return false;
            int start = (int)((World.Tick / 100 + u.Id) % Math.Max(1, World.MarketNeeds.Count));
            for (int i = 0; i < World.MarketNeeds.Count; i++)
            {
                MarketNeedData need = World.MarketNeeds[(start + i) % World.MarketNeeds.Count];
                if (need.SettlementId != s.Id || need.State == MarketNeedState.Satisfied || (!need.Automatic && need.ExpiresTick <= World.Tick)) continue;
                if (TryPlanMarketItem(u, s, need, need.Item, need.Target, 0)) return true;
            }
            return false;
        }
        private bool TryPlanMarketItem(UnitData u, SettlementData s, MarketNeedData need, ItemType item, float target, int depth)
        {
            if (depth > 8) { ExplainMarketNeed(need, MarketNeedState.Blocked, "Слишком длинная производственная цепь"); return false; }
            if (ProductionStock(s.Id, item) + .00001f >= target)
            { ExplainMarketNeed(need, MarketNeedState.Deliver, "Есть запас " + MarketCatalog.Name(item) + "; ожидается доставка или свободный цех"); return false; }
            IndustryRecipe recipe = RecipeForMarketItem(item);
            if (recipe == null)
            {
                if (item == ItemType.Plank || item == ItemType.Firewood || item == ItemType.StoneBlock || item == ItemType.StoneTile || item == ItemType.Brick)
                {
                    if (ChooseProcessing(u, s) || ChooseHaul(u, s)) { ExplainMarketNeed(need, MarketNeedState.Produce, "Подготовка базовых строительных материалов"); return true; }
                    ItemType raw = item == ItemType.Plank || item == ItemType.Firewood ? ItemType.Log : item == ItemType.Brick ? ItemType.Clay : ItemType.RawStone;
                    return ChooseMarketResource(u, s, need, raw);
                }
                // A funded town buys the actual private lot at its warehouse before scheduling extraction.
                foreach (BuildingData b in World.Buildings) if (UsableBuilding(b, s.Id))
                    foreach (OwnedGoodsData lot in b.PersonalStocks) if (lot.Item == item && lot.Amount > .00001f && lot.OwnerId != u.Id && TryPlaceMarketOrder(u.Id, b.Id, lot.OwnerId, item, Math.Min(target, lot.Amount), true))
                    { ExplainMarketNeed(need, MarketNeedState.Deliver, "Казна покупает реальную личную партию сырья на месте"); return true; }
                return ChooseMarketResource(u, s, need, item);
            }
            if (!World.HasTechnology(s.Id, recipe.Technology))
            {
                ExplainMarketNeed(need, MarketNeedState.Research, "Нужно исследование: " + recipe.Technology);
                // Preparation is useful while research is running; no locked recipe can execute.
                foreach (ItemAmount input in recipe.Inputs)
                    if (ProductionStock(s.Id, input.Item) + .00001f < input.Amount && TryPlanMarketItem(u, s, need, input.Item, input.Amount, depth + 1)) return true;
                return false;
            }
            BuildingData workshop = null;
            foreach (BuildingData b in World.Buildings) if (UsableBuilding(b, s.Id) && b.Type == recipe.Building)
            {
                workshop = b;
                if (b.IndustryWorkerId == 0 && TryStartProduction(u.Id, b.Id, recipe.Id)) { ExplainMarketNeed(need, MarketNeedState.Produce, "Изготавливается: " + IndustryCatalog.ItemName(item)); return true; }
            }
            bool missingMaterial = false;
            foreach (ItemAmount input in recipe.Inputs)
                if (ProductionStock(s.Id, input.Item) + .00001f < input.Amount) { missingMaterial = true; if (TryPlanMarketItem(u, s, need, input.Item, input.Amount, depth + 1)) return true; }
            if (missingMaterial) return false;
            foreach (AgriculturalAmount input in recipe.Agriculture)
                if (World.GetAgriculturalStock(s.Id, input.Product) + .00001f < input.Amount)
                {
                    if (input.Product == AgriculturalProduct.Fiber)
                    {
                        foreach (BuildingData farm in World.Buildings)
                            if (farm.SettlementId == s.Id && farm.Type == BuildingType.Farm && farm.FarmCrop == CropType.Flax && UsableBuilding(farm, s.Id) && SetTarget(u, farm.X, farm.Y)) { ChangeTask(u, UnitTask.Farm); ExplainMarketNeed(need, MarketNeedState.Gather, "Льняное поле поставляет волокно для ткани"); return true; }
                        ExplainMarketNeed(need, MarketNeedState.Blocked, "Для ткани нужно настоящее льняное поле и волокно"); return false;
                    }
                    if (input.Product == AgriculturalProduct.Hide && ChooseHunt(u, s)) { ExplainMarketNeed(need, MarketNeedState.Gather, "Охотник добывает настоящую шкуру"); return true; }
                    ExplainMarketNeed(need, MarketNeedState.Blocked, "Нет аграрного сырья: " + input.Product); return false;
                }
            if (workshop == null)
            {
                // Reuse the same priced construction command; never create a free workshop for a plan.
                if (HasTransportProject(s.Id)) { ExplainMarketNeed(need, MarketNeedState.Workshop, "Строительная очередь занята дорогой или мостом"); return false; }
                // Construction spends the communal hearth plank ledger, not planks reserved inside other workshops.
                if (s.Wood < 8)
                {
                    if (ChooseProcessing(u, s) || ChooseHaul(u, s)) { ExplainMarketNeed(need, MarketNeedState.Produce, "Готовятся общественные доски для строительства цеха"); return true; }
                    return ChooseMarketResource(u, s, need, ItemType.Log);
                }
                bool stone = recipe.Building == BuildingType.Smelter || recipe.Building == BuildingType.Smithy || recipe.Building == BuildingType.Mint;
                if (stone && FreePublicStock(s.Id, ItemType.RawStone) < 3) return TryPlanMarketItem(u, s, need, ItemType.RawStone, 3, depth + 1);
                if (s.BuildTimer >= 8)
                {
                    int site = FindBuildingSite(s);
                    if (site >= 0 && navigation.Find(World.Index((int)u.X, (int)u.Y), site, scratchPath, 4096, true) && TryBuildInfrastructure(s.Id, recipe.Building, site % World.Width, site / World.Width))
                    { s.BuildTimer = 0; ExplainMarketNeed(need, MarketNeedState.Workshop, "За реальные материалы построен цех: " + IndustryCatalog.BuildingName(recipe.Building)); return true; }
                }
                ExplainMarketNeed(need, MarketNeedState.Workshop, "Нужны место, строительные материалы и доступный цех: " + IndustryCatalog.BuildingName(recipe.Building)); return false;
            }
            ExplainMarketNeed(need, MarketNeedState.Blocked, "Цех занят, путь закрыт, сырьё зарезервировано или нет места"); return false;
        }
        private bool ChooseMarketResource(UnitData u, SettlementData s, MarketNeedData need, ItemType wanted)
        {
            int workers = 0;
            foreach (UnitData other in World.Units) if (other.Id != u.Id && other.SettlementId == s.Id && other.Task == UnitTask.Gather && other.ResourceCell >= 0 && ResourceCatalog.GetOutput(World.ResourceNodes[other.ResourceCell].Type) == wanted) workers++;
            if (workers >= 2) { ExplainMarketNeed(need, MarketNeedState.Gather, "Два работника уже добывают: " + MarketCatalog.Name(wanted)); return false; }
            for (int r = 0; r <= 28; r++) for (int oy = -r; oy <= r; oy++) for (int ox = -r; ox <= r; ox++)
            {
                if (Math.Abs(ox) != r && Math.Abs(oy) != r) continue;
                int x = s.X + ox, y = s.Y + oy; if (!World.IsWalkable(x, y)) continue;
                int cell = World.Index(x, y); ResourceNodeData node = World.ResourceNodes[cell]; TileData tile = World.Cells[cell];
                if (node.Amount <= 0 || ResourceCatalog.GetOutput(node.Type) != wanted || !ResourceCatalog.IsCompatible(node.Type, tile) || tile.BuildingId != 0 || tile.Fire > .1f || (tile.SettlementId != 0 && tile.SettlementId != s.Id) || reservations.ContainsKey(cell) || !SetTarget(u, x, y)) continue;
                ChangeTask(u, UnitTask.Gather); u.ResourceCell = cell; u.TargetBuildingId = u.SourceBuildingId = 0; reservations[cell] = u.Id;
                ExplainMarketNeed(need, MarketNeedState.Gather, "Заказ добычи: " + MarketCatalog.Name(wanted)); return true;
            }
            ExplainMarketNeed(need, MarketNeedState.Blocked, "Нет доступного источника " + MarketCatalog.Name(wanted) + ": нужен импорт или новый путь"); return false;
        }
        private float MarketResearchPriority(SettlementData settlement, TechnologyDefinition tech)
        {
            foreach (MarketNeedData need in World.MarketNeeds)
            {
                if (need.SettlementId != settlement.Id || need.State == MarketNeedState.Satisfied || (!need.Automatic && need.ExpiresTick <= World.Tick)) continue;
                if (MarketResearchNeeded(need.Item, tech.Id, 0)) return 86;
            }
            return 0;
        }
        private bool MarketResearchNeeded(ItemType item, string technology, int depth)
        {
            if (depth > 8) return false; IndustryRecipe recipe = RecipeForMarketItem(item); if (recipe == null) return false;
            if (TechnologyDependsOn(recipe.Technology, technology, 0)) return true;
            foreach (ItemAmount input in recipe.Inputs) if (MarketResearchNeeded(input.Item, technology, depth + 1)) return true;
            return false;
        }
        private bool TechnologyDependsOn(string id, string target, int depth)
        {
            if (id == target) return true; if (depth > 12) return false; TechnologyDefinition t = TechnologyCatalog.Get(id); if (t == null) return false;
            foreach (string prerequisite in t.Prerequisites) if (TechnologyDependsOn(prerequisite, target, depth + 1)) return true;
            return false;
        }
    }
}
