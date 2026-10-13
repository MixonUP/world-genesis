using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public enum MarketOrderState { Reserved, Completed, Cancelled }
    public enum MarketTradeKind { Purchase, Procurement, Wage, MaritimeImport, MaritimeExport }
    public enum MarketNeedState { Requested, Research, Workshop, Gather, Produce, Deliver, Satisfied, Blocked }

    [Serializable]
    public sealed class MarketOrderData
    {
        public int Id, BuyerId, SellerId, BuildingId, SettlementId;
        public ItemType Item;
        public float Amount;
        public int CopperPrice;
        public bool PublicPurchase;
        public long CreatedTick, ExpiresTick;
        public MarketOrderState State;
        public string Reason = "";
    }
    [Serializable]
    public sealed class MarketTradeData
    {
        public long Tick;
        public int OrderId, SettlementId, BuildingId, BuyerId, SellerId, VesselId;
        public MarketTradeKind Kind;
        public ItemType Item;
        public AgriculturalProduct Product;
        public float Amount;
        public int CopperPrice, CopperTax;
        // Signed physical coins paid by the buyer. A negative entry is actual change.
        public int CopperCoins, SilverCoins, GoldCoins;
    }
    [Serializable]
    public sealed class MarketPriceData
    {
        public int SettlementId, GoodId;
        public bool Agricultural;
        public float Multiplier = 1;
        public long LastTick;
        public int DemandBuildingId, DemandOwnerId;
        public float RequestedQuantity;
        public long DemandUntilTick;
    }
    [Serializable]
    public sealed class MarketNeedData
    {
        public int SettlementId;
        public ItemType Item;
        public float Target;
        public MarketNeedState State;
        public string Reason = "";
        public long UpdatedTick, ExpiresTick;
        public bool Automatic;
    }
    public sealed partial class UnitData { public int MarketOrderId; }
    public sealed partial class BuildingData
    {
        // Rebuilt from MarketOrders; never a second inventory and never spent directly.
        [NonSerialized] internal float[] MarketReservedStock;
    }
    public sealed partial class WorldState
    {
        public int MarketVersion, NextMarketOrderId = 1;
        public long MarketTransactions, MarketCopperTurnover, MarketCopperTaxes, MarketCopperWages;
        public List<MarketOrderData> MarketOrders = new List<MarketOrderData>();
        public List<MarketTradeData> MarketTrades = new List<MarketTradeData>();
        public List<MarketPriceData> MarketPrices = new List<MarketPriceData>();
        public List<MarketNeedData> MarketNeeds = new List<MarketNeedData>();

        public MarketOrderData GetMarketOrder(int id)
        { if (MarketOrders != null) foreach (MarketOrderData order in MarketOrders) if (order.Id == id) return order; return null; }
        public float MarketPrice(int settlementId, ItemType item)
        { return MarketCatalog.BasePrice(item) * MarketMultiplier(settlementId, (int)item, false); }
        public float AgriculturalMarketPrice(int settlementId, AgriculturalProduct item)
        { return MarketCatalog.BasePrice(item) * MarketMultiplier(settlementId, (int)item, true); }
        private float MarketMultiplier(int settlementId, int goodId, bool agricultural)
        { if (MarketPrices != null) foreach (MarketPriceData p in MarketPrices) if (p.SettlementId == settlementId && p.GoodId == goodId && p.Agricultural == agricultural) return p.Multiplier; return 1; }
        public string MarketWaitingReason(int settlementId)
        { if (MarketNeeds != null) foreach (MarketNeedData n in MarketNeeds) if (n.SettlementId == settlementId && n.State != MarketNeedState.Satisfied) return n.Reason; return "Нет незакрытых заказов"; }

        private bool ValidateMarket(out string error)
        {
            error = null;
            if (MarketVersion < 0 || MarketVersion > 1) { error = "Unsupported market version."; return false; }
            if (MarketVersion == 0)
            {
                if (MarketTransactions != 0 || MarketCopperTurnover != 0 || MarketCopperTaxes != 0 || MarketCopperWages != 0 || NextMarketOrderId != 1 || (MarketOrders != null && MarketOrders.Count != 0) || (MarketTrades != null && MarketTrades.Count != 0) || (MarketPrices != null && MarketPrices.Count != 0) || (MarketNeeds != null && MarketNeeds.Count != 0)) { error = "Legacy world contains market state."; return false; }
                foreach (UnitData u in Units) if (u.MarketOrderId != 0 || (int)u.Task == 30) { error = "Legacy market assignment."; return false; }
                return true;
            }
            if (NextMarketOrderId < 1 || MarketTransactions < 0 || MarketCopperTurnover < 0 || MarketCopperTaxes < 0 || MarketCopperTaxes > MarketCopperTurnover || MarketCopperWages < 0 || MarketOrders == null || MarketOrders.Count > 256 || MarketTrades == null || MarketTrades.Count > 128 || MarketPrices == null || MarketPrices.Count > Settlements.Count * (ResourceCatalog.ItemCount + FishingCatalog.AgriculturalProductCount) || MarketNeeds == null || MarketNeeds.Count > Settlements.Count * ResourceCatalog.ItemCount)
            { error = "Invalid market ledger."; return false; }
            HashSet<int> ids = new HashSet<int>(); HashSet<int> buyers = new HashSet<int>();
            foreach (MarketOrderData o in MarketOrders)
            {
                if (o == null || o.Id <= 0 || o.Id >= NextMarketOrderId || !ids.Add(o.Id) || o.BuyerId <= 0 || o.BuyerId >= NextId || o.SellerId < 0 || o.SellerId >= NextId || o.SellerId == o.BuyerId || o.BuildingId <= 0 || o.BuildingId >= NextId || GetSettlement(o.SettlementId) == null || !MarketCatalog.Tradable(o.Item) || o.Item == ItemType.Plank || !Stock(o.Amount) || o.Amount <= 0 || o.Amount > 10000 || o.CopperPrice <= 0 || o.CopperPrice > 10000000 || o.CreatedTick < 0 || o.CreatedTick > Tick || o.ExpiresTick < o.CreatedTick || o.ExpiresTick - o.CreatedTick > 1200 || (int)o.State < 0 || (int)o.State > 2 || o.Reason == null || o.Reason.Length > 256)
                { error = "Invalid market reservation."; return false; }
                if (o.State != MarketOrderState.Reserved) continue;
                UnitData u = GetUnit(o.BuyerId); BuildingData b = GetBuilding(o.BuildingId); UnitData seller = o.SellerId == 0 ? null : GetUnit(o.SellerId);
                if (u == null || u.Health <= 0 || u.Age < 16 || u.SettlementId != o.SettlementId || u.MarketOrderId != o.Id || (int)u.Task != 30 || !buyers.Add(u.Id) || b == null || b.SettlementId != o.SettlementId || (o.SellerId != 0 && (seller == null || seller.Health <= 0 || seller.Age < 16 || seller.SettlementId != o.SettlementId)))
                { error = "Orphan market reservation."; return false; }
            }
            foreach (UnitData u in Units) if ((u.MarketOrderId != 0 && (!buyers.Contains(u.Id) || (int)u.Task != 30)) || ((int)u.Task == 30 && u.MarketOrderId == 0)) { error = "Market task has no reservation."; return false; }
            HashSet<string> prices = new HashSet<string>();
            foreach (MarketPriceData p in MarketPrices)
                if (p == null || GetSettlement(p.SettlementId) == null || !Stock(p.Multiplier) || p.Multiplier < .5f || p.Multiplier > 2 || p.LastTick < 0 || p.LastTick > Tick || p.DemandBuildingId < 0 || p.DemandBuildingId >= NextId || p.DemandOwnerId < 0 || p.DemandOwnerId >= NextId || !Stock(p.RequestedQuantity) || p.RequestedQuantity > 10000 || p.DemandUntilTick < 0 || !(p.Agricultural ? MarketCatalog.Tradable((AgriculturalProduct)p.GoodId) : MarketCatalog.Tradable((ItemType)p.GoodId)) || !prices.Add(p.SettlementId + ":" + p.Agricultural + ":" + p.GoodId)) { error = "Invalid market price."; return false; }
            HashSet<string> needs = new HashSet<string>();
            foreach (MarketNeedData n in MarketNeeds)
                if (n == null || GetSettlement(n.SettlementId) == null || !MarketCatalog.Tradable(n.Item) || !Stock(n.Target) || n.Target <= 0 || n.Target > 10000 || (int)n.State < 0 || (int)n.State > 7 || n.Reason == null || n.Reason.Length > 256 || n.UpdatedTick < 0 || n.UpdatedTick > Tick || n.ExpiresTick < 0 || !needs.Add(n.SettlementId + ":" + n.Item)) { error = "Invalid demand order."; return false; }
            foreach (MarketTradeData t in MarketTrades)
                if (t == null || t.Tick < 0 || t.Tick > Tick || GetSettlement(t.SettlementId) == null || t.BuildingId <= 0 || t.BuildingId >= NextId || t.BuyerId < 0 || t.SellerId < 0 || t.VesselId < 0 || (int)t.Kind < 0 || (int)t.Kind > 4 || !Stock(t.Amount) || t.Amount < 0 || t.CopperPrice <= 0 || t.CopperPrice > 10000000 || t.CopperTax < 0 || t.CopperTax > t.CopperPrice * .30001f || (long)t.CopperCoins + 10L * t.SilverCoins + 100L * t.GoldCoins != t.CopperPrice || (t.Kind != MarketTradeKind.Wage && !(t.Product == AgriculturalProduct.None ? MarketCatalog.Tradable(t.Item) : t.Item == ItemType.None && MarketCatalog.Tradable(t.Product)))) { error = "Invalid monetary transaction."; return false; }
            return true;
        }
    }
}
