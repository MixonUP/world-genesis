using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private bool MarketExchangeHarborNetwork(List<BuildingData> warehouses, float[] hold, float[] agriculturalHold, ItemType item, AgriculturalProduct product, float quantity, bool shipSells, int traderId, int vesselId, float holdFree)
        {
            bool agriculture = product != AgriculturalProduct.None;
            if (warehouses == null || warehouses.Count == 0 || hold == null || hold.Length != ResourceCatalog.ItemCount || float.IsNaN(quantity) || float.IsInfinity(quantity) || quantity <= 0 || quantity > 20000 || float.IsNaN(holdFree) || float.IsInfinity(holdFree) || holdFree < 0 || traderId < 0 || vesselId <= 0 || (agriculture ? !MarketCatalog.Tradable(product) || agriculturalHold == null || agriculturalHold.Length != FishingCatalog.AgriculturalProductCount : !MarketCatalog.Tradable(item))) return false;
            int town = warehouses[0].SettlementId; SettlementData settlement = World.GetSettlement(town); if (settlement == null) return false;
            float free = 0, supply = 0; long reservedMoney = 0; int[] coins = new int[3]; HashSet<int> seen = new HashSet<int>();
            foreach (BuildingData b in warehouses)
            {
                if (b == null || b.SettlementId != town || b.Type != BuildingType.HarborWarehouse || b.Health <= 0 || World.GetBuilding(b.Id) != b || !seen.Add(b.Id) || ReferenceEquals(b.Inventory, hold)) return false;
                free += Math.Max(0, StorageCapacity.Capacity(b) - StorageCapacity.TotalUsed(b)); reservedMoney += ReservedBudget(b, 0);
                supply += agriculture ? Math.Max(0, b.AgriculturalStock[(int)product] - IndustryAgricultureReserved(b, product)) : SaleAvailable(b, 0, item);
                int[] local = MarketCoins(b, 0); for (int i = 0; i < 3; i++) coins[i] += local[i];
            }
            float available = shipSells ? agriculture ? agriculturalHold[(int)product] : hold[(int)item] : supply;
            if (available + .00001f < quantity) return false;
            if (!shipSells && !agriculture)
            {
                float reserve = 0; foreach (MarketNeedData need in World.MarketNeeds) if (need.SettlementId == town && need.Item == item && need.State != MarketNeedState.Satisfied) reserve = Math.Max(reserve, need.Target);
                if (World.GetSettlementStock(town, item) - reserve + .00001f < quantity) return false;
            }
            double quoted = Math.Ceiling((agriculture ? World.AgriculturalMarketPrice(town, product) : World.MarketPrice(town, item)) * quantity); if (quoted < 1 || quoted > 10000000) return false; int price = (int)quoted;
            if (shipSells && MarketWealth(coins) - reservedMoney < price || !MarketCatalog.PlanPayment(shipSells ? coins : MarketCoins(hold), shipSells ? MarketCoins(hold) : coins, price, out int[] net)) return false;
            int[] taxes = MarketTaxes(net, settlement.TaxRate, price); int netCount = net[0] + net[1] + net[2], taxCount = taxes[0] + taxes[1] + taxes[2];
            float goodsDelta = shipSells ? quantity : -quantity, coinDelta = shipSells ? -netCount + taxCount : netCount;
            if (goodsDelta + coinDelta > free + .00001f || -goodsDelta - coinDelta > holdFree + .00001f) return false;
            // First create a complete plan against each real ledger. Nothing is debited until all allocations fit.
            float[][] materials = new float[warehouses.Count][], taxLots = new float[warehouses.Count][], harvest = new float[warehouses.Count][];
            for (int i = 0; i < warehouses.Count; i++) { materials[i] = new float[ResourceCatalog.ItemCount]; taxLots[i] = new float[ResourceCatalog.ItemCount]; harvest[i] = new float[FishingCatalog.AgriculturalProductCount]; }
            if (!shipSells)
            {
                float left = quantity;
                for (int i = 0; i < warehouses.Count && left > .000001f; i++)
                { float take = Math.Min(left, agriculture ? Math.Max(0, warehouses[i].AgriculturalStock[(int)product] - IndustryAgricultureReserved(warehouses[i], product)) : SaleAvailable(warehouses[i], 0, item)); if (agriculture) harvest[i][(int)product] -= take; else materials[i][(int)item] -= take; left -= take; }
                if (left > .0001f) return false;
            }
            for (int coin = 0; coin < 3; coin++)
            {
                int delta = shipSells ? -net[coin] : net[coin];
                if (delta >= 0) continue; float left = -delta; int slot = (int)MarketCatalog.Coins[coin];
                for (int i = 0; i < warehouses.Count && left > .000001f; i++)
                {
                    float take = Math.Min(left, WholeMarketCoins(warehouses[i].TaxStocks[slot])); taxLots[i][slot] -= take; left -= take;
                    take = Math.Min(left, WholeMarketCoins(warehouses[i].Inventory[slot])); materials[i][slot] -= take; left -= take;
                }
                if (left > .0001f) return false;
            }
            float[] plannedFree = new float[warehouses.Count];
            for (int i = 0; i < warehouses.Count; i++)
            { float delta = 0; foreach (float x in materials[i]) delta += x; foreach (float x in taxLots[i]) delta += x; foreach (float x in harvest[i]) delta += x; plannedFree[i] = Math.Max(0, StorageCapacity.Capacity(warehouses[i]) - StorageCapacity.TotalUsed(warehouses[i]) - delta); }
            // Credit actual change, proceeds and tax into available connected stores; never into an aggregate balance.
            for (int coin = 0; coin < 3; coin++)
            {
                int delta = shipSells ? -net[coin] : net[coin]; float credit = Math.Max(0, delta) + (shipSells ? taxes[coin] : 0);
                if (!AllocateMarketCredit(taxLots, (int)MarketCatalog.Coins[coin], credit, plannedFree, true)) return false;
            }
            if (shipSells && !AllocateMarketCredit(agriculture ? harvest : materials, agriculture ? (int)product : (int)item, quantity, plannedFree)) return false;
            for (int i = 0; i < warehouses.Count; i++)
            {
                for (int j = 1; j < ResourceCatalog.ItemCount; j++) { warehouses[i].Inventory[j] += materials[i][j]; warehouses[i].TaxStocks[j] += taxLots[i][j]; }
                for (int j = 1; j < FishingCatalog.AgriculturalProductCount; j++) warehouses[i].AgriculturalStock[j] += harvest[i][j];
            }
            if (agriculture) agriculturalHold[(int)product] -= goodsDelta; else hold[(int)item] -= goodsDelta;
            for (int i = 0; i < 3; i++) hold[(int)MarketCatalog.Coins[i]] += shipSells ? net[i] - taxes[i] : -net[i];
            EnsureMarketPrice(town, agriculture ? (int)product : (int)item, agriculture);
            RecordMarketTrade(new MarketTradeData { SettlementId = town, BuildingId = warehouses[0].Id, BuyerId = shipSells ? 0 : traderId, SellerId = shipSells ? traderId : 0, VesselId = vesselId, Item = item, Product = product, Amount = quantity, CopperPrice = price, Kind = shipSells ? MarketTradeKind.MaritimeImport : MarketTradeKind.MaritimeExport }, net, taxes[0] + 10 * taxes[1] + 100 * taxes[2]);
            return true;
        }
        private static bool AllocateMarketCredit(float[][] deltas, int slot, float amount, float[] free, bool whole = false)
        {
            for (int i = 0; i < deltas.Length && amount > .000001f; i++) { float take = Math.Min(amount, whole ? WholeMarketCoins(free[i]) : free[i]); deltas[i][slot] += take; free[i] -= take; amount -= take; }
            return amount <= .0001f;
        }
        private static int[] MarketTaxes(int[] payment, float rate, int price)
        {
            int[] taxes = new int[3]; int left = (int)Math.Floor(price * rate + .000001f);
            for (int i = 2; i >= 0; i--) { taxes[i] = Math.Min(CoinTax(payment[i], rate), left / MarketCatalog.Denominations[i]); left -= taxes[i] * MarketCatalog.Denominations[i]; }
            return taxes;
        }
        // Maritime owner calls only after berth/peace/captain/clerk checks and the real dockwork delay.
        // The selected connected harbor warehouse is physical stock, not an aggregate copy.
        private bool MarketExchangeStock(BuildingData shore, float[] hold, ItemType goods, float qty, bool shipSells, int traderId, int vesselId, float holdFree)
        { return MarketExchangeCargo(shore, hold, null, goods, AgriculturalProduct.None, qty, shipSells, traderId, vesselId, holdFree); }
        private bool MarketExchangeAgriculture(BuildingData shore, float[] hold, float[] agriculturalHold, AgriculturalProduct goods, float qty, bool shipSells, int traderId, int vesselId, float holdFree)
        { return MarketExchangeCargo(shore, hold, agriculturalHold, ItemType.None, goods, qty, shipSells, traderId, vesselId, holdFree); }
        private bool MarketExchangeCargo(BuildingData shore, float[] hold, float[] agriculturalHold, ItemType item, AgriculturalProduct product, float qty, bool shipSells, int traderId, int vesselId, float holdFree)
        {
            bool agricultural = product != AgriculturalProduct.None;
            if (shore == null || shore.Health <= 0 || World.GetSettlement(shore.SettlementId) == null || hold == null || hold.Length != ResourceCatalog.ItemCount || ReferenceEquals(hold, shore.Inventory) || !float.IsFinite(qty) || qty <= 0 || qty > 20000 || !float.IsFinite(holdFree) || holdFree < 0 || traderId < 0 || vesselId <= 0 || (agricultural ? !MarketCatalog.Tradable(product) || agriculturalHold == null || agriculturalHold.Length != FishingCatalog.AgriculturalProductCount || !StorageCapacity.Accepts(shore, product) : !MarketCatalog.Tradable(item) || !StorageCapacity.Accepts(shore, item))) return false;
            float sellerStock = shipSells ? agricultural ? agriculturalHold[(int)product] : hold[(int)item] : agricultural ? Math.Max(0, shore.AgriculturalStock[(int)product] - IndustryAgricultureReserved(shore, product)) : SaleAvailable(shore, 0, item);
            if (sellerStock + .00001f < qty) return false;
            if (!shipSells && !agricultural)
            {
                float reserve = 0; foreach (MarketNeedData need in World.MarketNeeds) if (need.SettlementId == shore.SettlementId && need.Item == item && need.State != MarketNeedState.Satisfied) reserve = Math.Max(reserve, need.Target);
                if (World.GetSettlementStock(shore.SettlementId, item) - reserve + .00001f < qty) return false;
            }
            float pricePer = agricultural ? World.AgriculturalMarketPrice(shore.SettlementId, product) : World.MarketPrice(shore.SettlementId, item);
            double gross = Math.Ceiling(pricePer * qty); if (gross < 1 || gross > 10000000) return false; int price = (int)gross;
            int[] buyer = shipSells ? MarketCoins(shore, 0) : MarketCoins(hold), seller = shipSells ? MarketCoins(hold) : MarketCoins(shore, 0);
            if (shipSells && MarketWealth(buyer) - ReservedBudget(shore, 0) < price || !MarketCatalog.PlanPayment(buyer, seller, price, out int[] net)) return false;
            int[] taxes = MarketTaxes(net, World.GetSettlement(shore.SettlementId).TaxRate, price);
            int netCount = net[0] + net[1] + net[2], taxCount = taxes[0] + taxes[1] + taxes[2], taxValue = taxes[0] + 10 * taxes[1] + 100 * taxes[2];
            float shoreCoinDelta = shipSells ? -netCount + taxCount : netCount;
            float goodsDelta = shipSells ? qty : -qty, holdDelta = -goodsDelta - shoreCoinDelta;
            if (holdDelta > holdFree + .00001f) return false;
            if (StorageCapacity.IsDepot(shore))
            { if (StorageCapacity.TotalUsed(shore) + shoreCoinDelta + goodsDelta > StorageCapacity.Capacity(shore) + .00001f) return false; }
            else if (shoreCoinDelta + (agricultural ? 0 : goodsDelta) > StorageCapacity.FreeMaterials(shore) + .00001f || (agricultural && goodsDelta > StorageCapacity.FreeAgriculture(shore, product) + .00001f)) return false;
            // All checks precede any debit. Coin denominations themselves are conserved.
            if (agricultural) { shore.AgriculturalStock[(int)product] += goodsDelta; agriculturalHold[(int)product] -= goodsDelta; }
            else { shore.Inventory[(int)item] += goodsDelta; hold[(int)item] -= goodsDelta; }
            for (int i = 0; i < 3; i++)
            {
                if (shipSells) { ChangeCoins(shore, 0, i, -net[i]); shore.TaxStocks[(int)MarketCatalog.Coins[i]] += taxes[i]; hold[(int)MarketCatalog.Coins[i]] += net[i] - taxes[i]; }
                else { hold[(int)MarketCatalog.Coins[i]] -= net[i]; ChangeCoins(shore, 0, i, net[i] - taxes[i]); shore.TaxStocks[(int)MarketCatalog.Coins[i]] += taxes[i]; }
            }
            EnsureMarketPrice(shore.SettlementId, agricultural ? (int)product : (int)item, agricultural);
            RecordMarketTrade(new MarketTradeData { SettlementId = shore.SettlementId, BuildingId = shore.Id, BuyerId = shipSells ? 0 : traderId, SellerId = shipSells ? traderId : 0, VesselId = vesselId, Item = item, Product = product, Amount = qty, CopperPrice = price, Kind = shipSells ? MarketTradeKind.MaritimeImport : MarketTradeKind.MaritimeExport }, net, taxValue);
            return true;
        }
    }
}
