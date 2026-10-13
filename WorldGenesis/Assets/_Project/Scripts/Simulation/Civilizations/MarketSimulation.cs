using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private void MigrateMarket()
        {
            if (World.MarketVersion == 0)
            {
                World.MarketOrders = new List<MarketOrderData>(); World.MarketTrades = new List<MarketTradeData>();
                World.MarketPrices = new List<MarketPriceData>(); World.MarketNeeds = new List<MarketNeedData>();
                World.NextMarketOrderId = 1; World.MarketVersion = 1;
            }
            RebuildMarketReservations();
        }
        public static float MarketReserved(BuildingData b, ItemType item)
        { return b.MarketReservedStock == null ? 0 : b.MarketReservedStock[(int)item]; }
        private void RebuildMarketReservations()
        {
            foreach (BuildingData b in World.Buildings)
            { if (b.MarketReservedStock == null || b.MarketReservedStock.Length != ResourceCatalog.ItemCount) b.MarketReservedStock = new float[ResourceCatalog.ItemCount]; else Array.Clear(b.MarketReservedStock, 0, b.MarketReservedStock.Length); }
            foreach (MarketOrderData order in World.MarketOrders)
            {
                BuildingData b = World.GetBuilding(order.BuildingId);
                if (order.State == MarketOrderState.Reserved && order.SellerId == 0 && b != null) b.MarketReservedStock[(int)order.Item] += order.Amount;
            }
        }
        private static float OwnedAmount(BuildingData b, int owner, ItemType item)
        {
            if (owner == 0) return b.Inventory[(int)item];
            float amount = 0; foreach (OwnedGoodsData lot in b.PersonalStocks) if (lot.OwnerId == owner && lot.Item == item) amount += lot.Amount; return amount;
        }
        private static void ChangeOwnedAmount(BuildingData b, int owner, ItemType item, float delta)
        {
            if (owner == 0) { b.Inventory[(int)item] += delta; return; }
            foreach (OwnedGoodsData lot in b.PersonalStocks) if (lot.OwnerId == owner && lot.Item == item) { lot.Amount = Math.Max(0, lot.Amount + delta); return; }
            if (delta > 0) AddOwnedGoods(b, owner, item, delta);
        }
        private static int[] MarketCoins(BuildingData b, int owner)
        {
            int[] coins = new int[3];
            for (int i = 0; i < 3; i++)
            { coins[i] = WholeMarketCoins(OwnedAmount(b, owner, MarketCatalog.Coins[i])); if (owner == 0) coins[i] += WholeMarketCoins(b.TaxStocks[(int)MarketCatalog.Coins[i]]); }
            return coins;
        }
        private static int WholeMarketCoins(float amount) { return (int)Math.Min(10000000, Math.Floor(Math.Max(0, amount) + .00001f)); }
        private static int[] MarketCoins(float[] inventory)
        { int[] coins = new int[3]; for (int i = 0; i < 3; i++) coins[i] = WholeMarketCoins(inventory[(int)MarketCatalog.Coins[i]]); return coins; }
        private static long MarketWealth(int[] coins)
        { return coins[0] + 10L * coins[1] + 100L * coins[2]; }
        private static void ChangeCoins(BuildingData b, int owner, int coin, int delta)
        {
            ItemType item = MarketCatalog.Coins[coin];
            if (owner != 0) { ChangeOwnedAmount(b, owner, item, delta); return; }
            if (delta >= 0) { b.TaxStocks[(int)item] += delta; return; }
            float take = Math.Min(WholeMarketCoins(b.TaxStocks[(int)item]), -delta); b.TaxStocks[(int)item] -= take;
            b.Inventory[(int)item] += delta + take;
        }
        private float ReservedGoods(BuildingData b, int owner, ItemType item, int exceptId = 0)
        {
            float reserved = 0;
            foreach (MarketOrderData o in World.MarketOrders) if (o.Id != exceptId && o.State == MarketOrderState.Reserved && o.BuildingId == b.Id && o.SellerId == owner && o.Item == item) reserved += o.Amount;
            return reserved;
        }
        private long ReservedBudget(BuildingData b, int owner, int exceptId = 0)
        {
            long reserved = 0;
            foreach (MarketOrderData o in World.MarketOrders)
                if (o.Id != exceptId && o.State == MarketOrderState.Reserved && o.BuildingId == b.Id && (o.PublicPurchase ? 0 : o.BuyerId) == owner) reserved += o.CopperPrice;
            return reserved;
        }
        private float SaleAvailable(BuildingData b, int owner, ItemType item, int exceptId = 0)
        {
            float available = OwnedAmount(b, owner, item) - ReservedGoods(b, owner, item, exceptId);
            if (owner == 0)
            {
                available -= IndustryReserved(b, item);
                if (b.ActiveRecipe != RecipeType.None && ResourceCatalog.Input(b.ActiveRecipe) == item) available -= ResourceCatalog.InputAmount(b.ActiveRecipe);
                if (b.ActiveRecipe == RecipeType.Brick && item == ItemType.Firewood) available -= 1;
            }
            return Math.Max(0, available);
        }
        private bool MarketAdult(UnitData u, int settlementId)
        { return u != null && u.Health > 0 && u.Age >= 16 && IsCivilized(u.Species) && u.SettlementId == settlementId; }
        public bool TryPlaceMarketOrder(int buyerId, int buildingId, int sellerId, ItemType item, float amount, bool publicPurchase = false)
        {
            UnitData buyer = World.GetUnit(buyerId); BuildingData b = World.GetBuilding(buildingId);
            if (b == null || !MarketAdult(buyer, b.SettlementId) || !UsableBuilding(b, b.SettlementId) || buyer.MarketOrderId != 0 || CarriedLoad(buyer) > .00001f || !MarketCatalog.Tradable(item) || item == ItemType.Plank || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0 || amount > 10000 || sellerId == buyerId || (publicPurchase && sellerId == 0) || (sellerId != 0 && !MarketAdult(World.GetUnit(sellerId), b.SettlementId))) return false;
            int owner = publicPurchase ? 0 : buyerId;
            double rawPrice = Math.Ceiling(World.MarketPrice(b.SettlementId, item) * amount); if (rawPrice < 1 || rawPrice > 10000000) return false;
            int price = (int)rawPrice; int[] money = MarketCoins(b, owner);
            if (MarketWealth(money) - ReservedBudget(b, owner) >= price)
            {
                EnsureMarketPrice(b.SettlementId, (int)item, false);
                foreach (MarketPriceData p in World.MarketPrices) if (p.SettlementId == b.SettlementId && !p.Agricultural && p.GoodId == (int)item)
                { p.DemandBuildingId = b.Id; p.DemandOwnerId = owner; p.RequestedQuantity = amount; p.DemandUntilTick = World.Tick + 600; }
                if (publicPurchase && SaleAvailable(b, sellerId, item) + .00001f < amount) RequestMarketNeed(b.SettlementId, item, amount, "Платёжеспособный заказ: предложение отсутствует");
            }
            if (SaleAvailable(b, sellerId, item) + .00001f < amount || MarketWealth(money) - ReservedBudget(b, owner) < price || !MarketCatalog.PlanPayment(money, MarketCoins(b, sellerId), price, out int[] payment)) return false;
            PruneMarketOrders(); if (World.MarketOrders.Count >= 256 || World.NextMarketOrderId == int.MaxValue || !SetTarget(buyer, b.X, b.Y)) return false;
            ChangeTask(buyer, (UnitTask)30);
            MarketOrderData order = new MarketOrderData { Id = World.NextMarketOrderId++, BuyerId = buyer.Id, SellerId = sellerId, BuildingId = b.Id, SettlementId = b.SettlementId, Item = item, Amount = amount, CopperPrice = price, PublicPurchase = publicPurchase, CreatedTick = World.Tick, ExpiresTick = World.Tick + 1200, Reason = "Товар зарезервирован; покупатель идёт к месту хранения" };
            World.MarketOrders.Add(order); buyer.MarketOrderId = order.Id; buyer.TargetBuildingId = b.Id; buyer.WorkTimer = .5f; buyer.DecisionTimer = 2;
            EnsureMarketPrice(b.SettlementId, (int)item, false); RebuildMarketReservations(); World.ResourceRevision++; return true;
        }
        public bool CancelMarketOrder(int buyerId)
        {
            UnitData u = World.GetUnit(buyerId); if (u == null || u.MarketOrderId == 0) return false;
            ReleaseMarket(u); if ((int)u.Task == 30) { ChangeTask(u, UnitTask.Idle); ClearRoute(u); u.DecisionTimer = 0; } return true;
        }
        private void ReleaseMarket(UnitData u)
        {
            if (u.MarketOrderId == 0) return;
            MarketOrderData order = World.GetMarketOrder(u.MarketOrderId);
            if (order != null && order.State == MarketOrderState.Reserved) { order.State = MarketOrderState.Cancelled; order.Reason = "Заказ отменён; товар и монеты остались у владельцев"; }
            u.MarketOrderId = 0; RebuildMarketReservations();
        }
        private void PruneMarketOrders()
        { for (int i = 0; World.MarketOrders.Count >= 128 && i < World.MarketOrders.Count;) { if (World.MarketOrders[i].State != MarketOrderState.Reserved) World.MarketOrders.RemoveAt(i); else i++; } }
        private void RecordMarketTrade(MarketTradeData trade, int[] payment, int tax)
        {
            trade.Tick = World.Tick; trade.CopperCoins = payment[0]; trade.SilverCoins = payment[1]; trade.GoldCoins = payment[2]; trade.CopperTax = tax;
            World.MarketTrades.Add(trade); if (World.MarketTrades.Count > 128) World.MarketTrades.RemoveAt(0);
            World.MarketTransactions++; World.MarketCopperTurnover += trade.CopperPrice; World.MarketCopperTaxes += tax;
            if (trade.Kind == MarketTradeKind.Wage) World.MarketCopperWages += trade.CopperPrice;
            World.ResourceRevision++;
        }
        // Rounding down per actual denomination never invents a coin for a fractional tax.
        private static int CoinTax(int netCoins, float rate) { return netCoins > 0 ? (int)Math.Floor(netCoins * rate + .000001f) : 0; }
        private bool CommitMarketOrder(UnitData u, MarketOrderData o, BuildingData b)
        {
            if (!MarketAdult(u, o.SettlementId) || !AtBuilding(u, b) || !UsableBuilding(b, o.SettlementId) || (o.SellerId != 0 && !MarketAdult(World.GetUnit(o.SellerId), o.SettlementId)) || SaleAvailable(b, o.SellerId, o.Item, o.Id) + .00001f < o.Amount) return false;
            int owner = o.PublicPurchase ? 0 : u.Id;
            if (MarketWealth(MarketCoins(b, owner)) - ReservedBudget(b, owner, o.Id) < o.CopperPrice || !MarketCatalog.PlanPayment(MarketCoins(b, owner), MarketCoins(b, o.SellerId), o.CopperPrice, out int[] net)) return false;
            SettlementData s = World.GetSettlement(o.SettlementId); int tax = 0; int[] taxes = MarketTaxes(net, s.TaxRate, o.CopperPrice);
            // Goods and coins stay at this physical warehouse; only their legal owners change.
            ChangeOwnedAmount(b, o.SellerId, o.Item, -o.Amount); ChangeOwnedAmount(b, owner, o.Item, o.Amount);
            for (int i = 0; i < 3; i++)
            {
                int taxed = o.SellerId == 0 ? 0 : taxes[i];
                ChangeCoins(b, owner, i, -net[i]); ChangeCoins(b, o.SellerId, i, net[i] - taxed); b.TaxStocks[(int)MarketCatalog.Coins[i]] += taxed; tax += taxed * MarketCatalog.Denominations[i];
            }
            o.State = MarketOrderState.Completed; o.Reason = "Оплачено на месте; товар передан владельцу, налог удержан один раз";
            RecordMarketTrade(new MarketTradeData { OrderId = o.Id, SettlementId = o.SettlementId, BuildingId = b.Id, BuyerId = u.Id, SellerId = o.SellerId, Item = o.Item, Amount = o.Amount, CopperPrice = o.CopperPrice, Kind = o.PublicPurchase ? MarketTradeKind.Procurement : MarketTradeKind.Purchase }, net, tax);
            u.MarketOrderId = 0; RebuildMarketReservations(); return true;
        }
        private bool PerformMarketActivity(UnitData u, SettlementData home, float dt)
        {
            if ((int)u.Task != 30) return false;
            MarketOrderData o = World.GetMarketOrder(u.MarketOrderId); BuildingData b = o == null ? null : World.GetBuilding(o.BuildingId);
            if (o == null || o.State != MarketOrderState.Reserved || b == null || World.Tick >= o.ExpiresTick || !UsableBuilding(b, u.SettlementId)) { CancelMarketOrder(u.Id); if ((int)u.Task == 30) ChangeTask(u, UnitTask.Idle); return true; }
            if (!AtBuilding(u, b)) { if (!SetTarget(u, b.X, b.Y)) CancelMarketOrder(u.Id); return true; }
            u.WorkTimer = Math.Max(0, u.WorkTimer - dt); if (u.WorkTimer > 0) return true;
            if (!CommitMarketOrder(u, o, b)) { o.Reason = "Платёж или предложение изменились; списаний нет"; CancelMarketOrder(u.Id); }
            else { ChangeTask(u, UnitTask.Idle); u.DecisionTimer = 0; }
            return true;
        }
        private void UpdateMarkets()
        {
            if (World.Tick % 10 != 0) return;
            foreach (MarketOrderData o in World.MarketOrders)
            {
                if (o.State != MarketOrderState.Reserved) continue;
                UnitData u = World.GetUnit(o.BuyerId); BuildingData b = World.GetBuilding(o.BuildingId);
                if (!MarketAdult(u, o.SettlementId) || b == null || b.Health <= 0 || World.Tick >= o.ExpiresTick || (o.SellerId != 0 && !MarketAdult(World.GetUnit(o.SellerId), o.SettlementId)))
                { o.State = MarketOrderState.Cancelled; o.Reason = "Истёк срок или недоступен участник/склад"; if (u != null) { u.MarketOrderId = 0; if ((int)u.Task == 30) { ChangeTask(u, UnitTask.Idle); ClearRoute(u); u.DecisionTimer = 0; } } }
            }
            RebuildMarketReservations();
            if (World.Tick % 100 != 0) return;
            PruneMarketOrders(); UpdateMarketNeeds();
            foreach (MarketPriceData p in World.MarketPrices)
            {
                float supply = 0, funded = 0;
                BuildingData demandSite = World.GetBuilding(p.DemandBuildingId);
                if (p.DemandUntilTick > World.Tick && demandSite != null && (p.DemandOwnerId == 0 || MarketAdult(World.GetUnit(p.DemandOwnerId), p.SettlementId)))
                { float basePrice = p.Agricultural ? MarketCatalog.BasePrice((AgriculturalProduct)p.GoodId) : MarketCatalog.BasePrice((ItemType)p.GoodId); funded = Math.Min(p.RequestedQuantity, MarketWealth(MarketCoins(demandSite, p.DemandOwnerId)) / (basePrice * p.Multiplier)); }
                foreach (BuildingData b in World.Buildings) if (b.SettlementId == p.SettlementId && b.Health > 0)
                { if (p.Agricultural) supply += b.AgriculturalStock[p.GoodId]; else { supply += Math.Max(0, b.Inventory[p.GoodId] - IndustryReserved(b, (ItemType)p.GoodId)); foreach (OwnedGoodsData lot in b.PersonalStocks) if ((int)lot.Item == p.GoodId) supply += lot.Amount; } }
                if (!p.Agricultural) foreach (MarketOrderData o in World.MarketOrders) if (o.State == MarketOrderState.Reserved && o.SettlementId == p.SettlementId && (int)o.Item == p.GoodId) { BuildingData b = World.GetBuilding(o.BuildingId); if (b != null && MarketWealth(MarketCoins(b, o.PublicPurchase ? 0 : o.BuyerId)) >= o.CopperPrice) funded = Math.Max(funded, o.Amount); }
                float target = Math.Max(.5f, Math.Min(2, 1 + .6f * (funded - supply) / (funded + supply + 4)));
                p.Multiplier = Math.Max(.5f, Math.Min(2, p.Multiplier + (target - p.Multiplier) * .15f)); p.LastTick = World.Tick;
            }
        }
        private void EnsureMarketPrice(int settlementId, int good, bool agricultural)
        {
            foreach (MarketPriceData p in World.MarketPrices) if (p.SettlementId == settlementId && p.GoodId == good && p.Agricultural == agricultural) return;
            World.MarketPrices.Add(new MarketPriceData { SettlementId = settlementId, GoodId = good, Agricultural = agricultural, LastTick = World.Tick });
        }
        private bool TryChooseMarket(UnitData u, SettlementData s)
        {
            if (s == null || !MarketAdult(u, s.Id) || CarriedLoad(u) > .00001f) return false;
            foreach (BuildingData b in World.Buildings)
            {
                if (!UsableBuilding(b, s.Id)) continue;
                if (AtBuilding(u, b) && TryUsePrivateEquipment(u, b)) return true;
                ItemType desired = u.WorkTool == ItemType.None ? ItemType.IronTools : u.Clothes == ItemType.None ? ItemType.LinenClothes : ItemType.None;
                if (desired != ItemType.None) foreach (OwnedGoodsData lot in b.PersonalStocks)
                    if (lot.OwnerId != u.Id && lot.Item == desired && lot.Amount >= 1 && TryPlaceMarketOrder(u.Id, b.Id, lot.OwnerId, desired, 1)) return true;
            }
            return false;
        }
        private bool TryUsePrivateEquipment(UnitData u, BuildingData b)
        {
            foreach (OwnedGoodsData lot in b.PersonalStocks)
            {
                if (lot.OwnerId != u.Id || lot.Amount < 1) continue;
                bool tool = IndustryCatalog.IsTool(lot.Item) && u.WorkTool == ItemType.None, clothes = IndustryCatalog.IsClothes(lot.Item) && u.Clothes == ItemType.None;
                if (!tool && !clothes) continue;
                lot.Amount -= 1; if (tool) { u.WorkTool = lot.Item; World.WorkToolsIssued++; } else { u.Clothes = lot.Item; World.ClothesIssued++; }
                u.IndustryEquippedCount++; World.ResourceRevision++; return true;
            }
            return false;
        }
        // Called once after an actual local production batch; no backpay or distance-based salary.
        private void PayProductionWage(UnitData worker, BuildingData workplace, IndustryRecipe recipe)
        {
            if (worker == null || !AtBuilding(worker, workplace) || !MarketAdult(worker, workplace.SettlementId) || recipe == null) return;
            if (IndustryCatalog.IsCoin(recipe.Output))
            { float minted = Math.Min(recipe.OutputAmount, workplace.Inventory[(int)recipe.Output]); workplace.Inventory[(int)recipe.Output] -= minted; workplace.TaxStocks[(int)recipe.Output] += minted; }
            int wage = Math.Max(1, Math.Min(4, (int)Math.Ceiling(recipe.Seconds / 6)));
            if (MarketWealth(MarketCoins(workplace, 0)) - ReservedBudget(workplace, 0) < wage || !MarketCatalog.PlanPayment(MarketCoins(workplace, 0), MarketCoins(workplace, worker.Id), wage, out int[] payment)) return;
            for (int i = 0; i < 3; i++) { ChangeCoins(workplace, 0, i, -payment[i]); ChangeCoins(workplace, worker.Id, i, payment[i]); }
            RecordMarketTrade(new MarketTradeData { SettlementId = workplace.SettlementId, BuildingId = workplace.Id, BuyerId = 0, SellerId = worker.Id, Item = recipe.Output, Amount = recipe.OutputAmount, CopperPrice = wage, Kind = MarketTradeKind.Wage }, payment, 0);
        }
    }
}
