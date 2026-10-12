using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public enum EconomicPolicy { FreeDistribution, Market }
    public enum ChildLaborPolicy { Forbidden, LightWork }
    public enum ResourceFocus { Balanced, Timber, Masonry, Metals }
    public enum TradeKind { Contribution, Gift, Barter }

    [Serializable]
    public sealed class OwnedGoodsData { public int OwnerId; public ItemType Item; public float Amount; }
    [Serializable]
    public sealed class TradeRecord
    {
        public long Tick;
        public int BuildingId, SellerId, BuyerId;
        public TradeKind Kind;
        public ItemType Item, PaymentItem;
        public float Amount, PaymentAmount, TaxAmount;
    }
    [Serializable]
    public sealed class GroundStockData
    {
        public int OwnerId;
        public ItemType Item;
        public float Amount, X, Y;
        public bool IsFood;
    }

    public sealed partial class WorldState
    {
        public List<GroundStockData> GroundStocks = new List<GroundStockData>();
        public List<TradeRecord> Trades = new List<TradeRecord>();

        public float GetCitizenStock(int unitId, ItemType item)
        {
            float result = 0;
            for (int i = 0; i < Buildings.Count; i++)
                for (int j = 0; j < Buildings[i].PersonalStocks.Count; j++)
                { OwnedGoodsData lot = Buildings[i].PersonalStocks[j]; if (lot.OwnerId == unitId && lot.Item == item) result += lot.Amount; }
            return result;
        }
        public float GetTreasuryStock(int settlementId, ItemType item)
        {
            if ((int)item <= 0 || (int)item >= ResourceCatalog.ItemCount) return 0;
            float result = 0;
            for (int i = 0; i < Buildings.Count; i++) if (Buildings[i].SettlementId == settlementId) result += Buildings[i].TaxStocks[(int)item];
            return result;
        }
        private bool ValidateCivilEconomy(out string error)
        {
            error = null;
            if (DemographyVersion < 0 || DemographyVersion > 1 || EconomyVersion < 0 || EconomyVersion > 1) { error = "Unsupported civil economy version."; return false; }
            if (DemographyVersion == 1)
                for (int i = 0; i < Units.Count; i++)
                {
                    UnitData u = Units[i];
                    if ((int)u.Sex < 0 || (int)u.Sex > 1 || !UnitRange(u.Education) || !UnitRange(u.Prudence) || !UnitRange(u.Ambition) || !UnitRange(u.Generosity)) { error = "Invalid demographic state."; return false; }
                }
            if (EconomyVersion == 0) return true;
            if (GroundStocks == null || GroundStocks.Count > 30000 || Trades == null || Trades.Count > 128) { error = "Invalid local goods records."; return false; }
            for (int i = 0; i < Units.Count; i++)
                if ((Units[i].CargoOwnerId != 0 && Units[i].CargoOwnerId != Units[i].Id) || Units[i].GroundStockIndex < -1 || Units[i].GroundStockIndex >= GroundStocks.Count)
                { error = "Invalid cargo ownership or ground target."; return false; }
            for (int i = 0; i < Settlements.Count; i++)
            {
                SettlementData s = Settlements[i];
                if ((int)s.EconomyPolicy < 0 || (int)s.EconomyPolicy > 1 || (int)s.ChildLabor < 0 || (int)s.ChildLabor > 1 || (int)s.ResourceFocus < 0 || (int)s.ResourceFocus > 3 || !Finite(s.TaxRate) || s.TaxRate < 0 || s.TaxRate > .3f || s.PolicyRulerId < 0 || s.PolicyRulerId >= NextId || s.NextPolicyReviewTick < 0 || s.LastDecreeTick < 0 || s.LastDecreeTick > Tick || s.LastDecreeReason == null || s.LastDecreeReason.Length > 512)
                { error = "Invalid economic policy."; return false; }
            }
            for (int i = 0; i < Buildings.Count; i++)
            {
                BuildingData b = Buildings[i];
                if (b.PersonalStocks == null || b.PersonalStocks.Count > 10000 || b.TaxStocks == null || b.TaxStocks.Length != ResourceCatalog.ItemCount || b.TaxStocks[0] != 0 || b.TaxStocks[(int)ItemType.Plank] != 0) { error = "Invalid owned storage."; return false; }
                float total = 0;
                for (int k = 0; k < b.Inventory.Length; k++) { if (!Stock(b.TaxStocks[k])) { error = "Invalid natural tax."; return false; } total += b.Inventory[k] + b.TaxStocks[k]; }
                HashSet<long> unique = new HashSet<long>();
                for (int j = 0; j < b.PersonalStocks.Count; j++)
                {
                    OwnedGoodsData lot = b.PersonalStocks[j]; UnitData owner = lot == null ? null : GetUnit(lot.OwnerId);
                    if (lot == null || owner == null || owner.Health <= 0 || owner.Age < 16 || owner.SettlementId != b.SettlementId || (int)lot.Item <= 0 || (int)lot.Item >= ResourceCatalog.ItemCount || lot.Item == ItemType.Plank || !Stock(lot.Amount) || !unique.Add(((long)lot.OwnerId << 32) | (uint)lot.Item)) { error = "Invalid private goods lot."; return false; }
                    total += lot.Amount;
                }
                if (total > ResourceCatalog.StorageCapacity + .001f) { error = "Combined storage capacity exceeded."; return false; }
            }
            for (int i = 0; i < GroundStocks.Count; i++)
            {
                GroundStockData g = GroundStocks[i];
                if (g == null || !Stock(g.Amount) || !Finite(g.X) || !Finite(g.Y) || g.X < 0 || g.Y < 0 || g.X >= Width || g.Y >= Height || g.OwnerId < 0 || (g.OwnerId != 0 && GetUnit(g.OwnerId) == null) || (g.IsFood ? g.Item != ItemType.None : (int)g.Item <= 0 || (int)g.Item >= ResourceCatalog.ItemCount)) { error = "Invalid ground stock."; return false; }
            }
            for (int i = 0; i < Trades.Count; i++)
            {
                TradeRecord t = Trades[i];
                if (t == null || t.Tick < 0 || t.Tick > Tick || t.BuildingId <= 0 || t.BuildingId >= NextId || t.SellerId <= 0 || t.BuyerId <= 0 || t.SellerId >= NextId || t.BuyerId >= NextId || (int)t.Kind < 0 || (int)t.Kind > 2 || (int)t.Item <= 0 || (int)t.Item >= ResourceCatalog.ItemCount || !Stock(t.Amount) || t.Amount <= 0 || !Stock(t.PaymentAmount) || !Stock(t.TaxAmount) || t.TaxAmount > t.PaymentAmount * .30001f || (t.Kind == TradeKind.Barter ? t.SellerId == t.BuyerId || (int)t.PaymentItem <= 0 || (int)t.PaymentItem >= ResourceCatalog.ItemCount || t.PaymentAmount <= 0 : t.PaymentAmount != 0 || t.TaxAmount != 0 || t.PaymentItem != ItemType.None)) { error = "Invalid trade ledger."; return false; }
            }
            return true;
        }
    }

    public sealed partial class SimulationEngine
    {
        public const long PolicyReviewIntervalTicks = 1200;
        public const float CommunalDeliveryShare = .8f;
        private void MigrateCivilEconomy()
        {
            if (World.EconomyVersion == 1) return;
            World.GroundStocks = new List<GroundStockData>(); World.Trades = new List<TradeRecord>();
            for (int i = 0; i < World.Buildings.Count; i++) { World.Buildings[i].PersonalStocks = new List<OwnedGoodsData>(); World.Buildings[i].TaxStocks = new float[ResourceCatalog.ItemCount]; }
            for (int i = 0; i < World.Units.Count; i++) { World.Units[i].CargoOwnerId = 0; World.Units[i].GroundStockIndex = -1; }
            World.EconomyVersion = 1; UpdateEconomicPolicies(true); World.Revision++;
        }
        private void UpdateEconomicPolicies(bool force = false)
        {
            for (int i = 0; i < World.Settlements.Count; i++)
            {
                SettlementData s = World.Settlements[i]; UnitData ruler = World.GetUnit(s.RulerId);
                if (!force && s.PolicyRulerId == s.RulerId && World.Tick < s.NextPolicyReviewTick) continue;
                bool shortage = s.Population > 0 && s.Food < s.Population * 2;
                float generosity = ruler == null ? .5f : ruler.Generosity, prudence = ruler == null ? .5f : ruler.Prudence;
                s.EconomyPolicy = ruler == null || shortage || generosity >= .55f ? EconomicPolicy.FreeDistribution : EconomicPolicy.Market;
                s.TaxRate = s.EconomyPolicy == EconomicPolicy.FreeDistribution ? 0 : generosity < .25f ? .3f : .1f;
                s.ChildLabor = ruler != null && (shortage || (ruler.Ambition > .65f && generosity < .4f)) ? ChildLaborPolicy.LightWork : ChildLaborPolicy.Forbidden;
                s.ResourceFocus = ruler == null || shortage ? ResourceFocus.Balanced : prudence >= .5f ? (s.Wood < 20 ? ResourceFocus.Timber : World.GetResourceBalance(s.Id, ItemType.StoneBlock).Deficit > 0 ? ResourceFocus.Masonry : ResourceFocus.Balanced) : (ResourceFocus)(StableRandom.Hash(World.Seed, s.RulerId, 0, 6100) % 4);
                s.PolicyRulerId = s.RulerId; s.LastDecreeTick = World.Tick; s.NextPolicyReviewTick = World.Tick + PolicyReviewIntervalTicks;
                s.LastDecreeReason = shortage ? "Мало общей еды: бесплатное распределение и лёгкая помощь подростков." : ruler == null ? "Нет взрослого правителя: временное общее распределение." : prudence < .5f ? "Правитель следует личному предпочтению сырья; высокий налог зависит от щедрости." : "Правитель учитывает запас досок и строительных материалов; обмен и налог отражают его щедрость.";
            }
        }
        public void SetEconomicPolicy(int settlementId, EconomicPolicy policy, float taxRate, ChildLaborPolicy childLabor, ResourceFocus focus)
        {
            SettlementData s = World.GetSettlement(settlementId);
            if (s == null || (int)policy < 0 || (int)policy > 1 || float.IsNaN(taxRate) || taxRate < 0 || taxRate > .3f || (int)childLabor < 0 || (int)childLabor > 1 || (int)focus < 0 || (int)focus > 3) throw new ArgumentOutOfRangeException(nameof(settlementId));
            s.EconomyPolicy = policy; s.TaxRate = taxRate; s.ChildLabor = childLabor; s.ResourceFocus = focus; s.PolicyRulerId = s.RulerId; s.LastDecreeTick = World.Tick; s.NextPolicyReviewTick = World.Tick + PolicyReviewIntervalTicks; s.LastDecreeReason = "Явное изменение правил поселения.";
            for (int i = 0; i < World.Units.Count; i++) if (World.Units[i].SettlementId == s.Id) EnforceChildWorkLaw(World.Units[i], s);
            World.Revision++;
        }
        private static void AddOwnedGoods(BuildingData b, int ownerId, ItemType item, float amount)
        {
            if (amount <= 0) return;
            for (int i = 0; i < b.PersonalStocks.Count; i++)
                if (b.PersonalStocks[i].OwnerId == ownerId && b.PersonalStocks[i].Item == item) { b.PersonalStocks[i].Amount += amount; return; }
            b.PersonalStocks.Add(new OwnedGoodsData { OwnerId = ownerId, Item = item, Amount = amount });
        }
        private void DepositMaterial(UnitData unit, BuildingData b, float amount)
        {
            float personal = unit.CargoOwnerId == unit.Id && unit.Age >= 16 ? amount * (1 - CommunalDeliveryShare) : 0;
            b.Inventory[(int)unit.CargoKind] += amount - personal;
            AddOwnedGoods(b, unit.Id, unit.CargoKind, personal);
        }
        private OwnedGoodsData PaymentLot(BuildingData b, int buyerId, ItemType wanted)
        {
            for (int i = 0; i < b.PersonalStocks.Count; i++) { OwnedGoodsData p = b.PersonalStocks[i]; if (p.OwnerId == buyerId && p.Item != wanted && !RecipeNeeds(b.ActiveRecipe, p.Item) && p.Amount > .00001f) return p; }
            return null;
        }
        private static bool RecipeNeeds(RecipeType recipe, ItemType item)
        { return recipe != RecipeType.None && (ResourceCatalog.Input(recipe) == item || (recipe == RecipeType.Brick && item == ItemType.Firewood)); }
        private bool AcquisitionCost(UnitData buyer, SettlementData home, BuildingData b, ItemType item, float required, out float cost)
        {
            cost = 0; float available = b.Inventory[(int)item], sale = 0;
            for (int i = 0; i < b.PersonalStocks.Count; i++)
            {
                OwnedGoodsData lot = b.PersonalStocks[i]; if (lot.Item != item) continue;
                UnitData seller = World.GetUnit(lot.OwnerId); if (seller == null || seller.Health <= 0 || seller.Age < 16 || seller.SettlementId != home.Id) continue;
                if (lot.OwnerId == buyer.Id || home.EconomyPolicy == EconomicPolicy.FreeDistribution) available += lot.Amount;
                else sale += lot.Amount;
            }
            cost = Math.Max(0, required - available);
            return available + sale + .00001f >= required;
        }
        private bool CanObtainInputs(UnitData buyer, SettlementData home, BuildingData b, RecipeType recipe)
        {
            if (recipe == RecipeType.None || home == null || buyer.Age < 16) return false;
            float cost, fuelCost = 0;
            if (!AcquisitionCost(buyer, home, b, ResourceCatalog.Input(recipe), ResourceCatalog.InputAmount(recipe), out cost) || (recipe == RecipeType.Brick && !AcquisitionCost(buyer, home, b, ItemType.Firewood, 1, out fuelCost))) return false;
            float budget = 0;
            for (int i = 0; i < b.PersonalStocks.Count; i++) { OwnedGoodsData p = b.PersonalStocks[i]; if (p.OwnerId == buyer.Id && !RecipeNeeds(recipe, p.Item)) budget += p.Amount; }
            float output = ResourceCatalog.Output(recipe) == ItemType.Plank ? 0 : ResourceCatalog.OutputAmount(recipe);
            return cost + fuelCost <= budget + .00001f && FreeStorage(b) + ResourceCatalog.InputAmount(recipe) + (recipe == RecipeType.Brick ? 1 : 0) >= output;
        }
        private void AcquireItem(UnitData buyer, SettlementData home, BuildingData b, ItemType item, float required)
        {
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < b.PersonalStocks.Count && b.Inventory[(int)item] + .00001f < required; i++)
            {
                OwnedGoodsData lot = b.PersonalStocks[i]; if (lot.Item != item || lot.Amount <= 0) continue;
                UnitData seller = World.GetUnit(lot.OwnerId); if (seller == null || seller.Age < 16 || seller.Health <= 0) continue;
                bool own = seller.Id == buyer.Id, paid = !own && home.EconomyPolicy == EconomicPolicy.Market;
                if ((pass == 0) != own) continue;
                OwnedGoodsData payment = paid ? PaymentLot(b, buyer.Id, item) : null;
                if (paid && payment == null) continue;
                float amount = Math.Min(required - b.Inventory[(int)item], lot.Amount); if (paid) amount = Math.Min(amount, payment.Amount);
                if (amount <= .00001f) continue;
                float tax = paid ? amount * home.TaxRate : 0;
                lot.Amount -= amount; b.Inventory[(int)item] += amount;
                if (paid) { payment.Amount -= amount; AddOwnedGoods(b, seller.Id, payment.Item, amount - tax); b.TaxStocks[(int)payment.Item] += tax; }
                World.Trades.Add(new TradeRecord { Tick = World.Tick, BuildingId = b.Id, SellerId = seller.Id, BuyerId = buyer.Id, Kind = own ? TradeKind.Contribution : paid ? TradeKind.Barter : TradeKind.Gift, Item = item, Amount = amount, PaymentItem = paid ? payment.Item : ItemType.None, PaymentAmount = paid ? amount : 0, TaxAmount = tax });
                if (World.Trades.Count > 128) World.Trades.RemoveAt(0);
                World.ResourceRevision++;
                if (paid && lot.Amount > .00001f && b.Inventory[(int)item] + .00001f < required) i--; // Continue with the next real payment lot; no reused budget.
            }
        }
        private void AcquireRecipeInputs(UnitData unit, SettlementData home, BuildingData building)
        {
            if (unit.Age < 16 || unit.Health <= 0 || building.WorkerId != unit.Id || unit.Task != UnitTask.Process || DistanceSquared(unit.X, unit.Y, building.X + .5f, building.Y + .5f) > 1.01f) return;
            RecipeType recipe = building.ActiveRecipe;
            // One preflight reserves all recipe inputs and one shared barter budget before any ownership mutation.
            if (recipe == RecipeType.None || !CanObtainInputs(unit, home, building, recipe)) return;
            AcquireItem(unit, home, building, ResourceCatalog.Input(recipe), ResourceCatalog.InputAmount(recipe));
            if (recipe == RecipeType.Brick) AcquireItem(unit, home, building, ItemType.Firewood, 1);
        }
        private void ReleasePrivateGoods(UnitData unit)
        {
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData b = World.Buildings[i];
                for (int j = b.PersonalStocks.Count - 1; j >= 0; j--)
                    if (b.PersonalStocks[j].OwnerId == unit.Id) { b.Inventory[(int)b.PersonalStocks[j].Item] += b.PersonalStocks[j].Amount; b.PersonalStocks.RemoveAt(j); }
            }
            for (int i = 0; i < World.GroundStocks.Count; i++) if (World.GroundStocks[i].OwnerId == unit.Id) World.GroundStocks[i].OwnerId = 0;
        }
    }
}
