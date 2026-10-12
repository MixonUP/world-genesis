using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        public static bool TradeableItem(ItemType item)
        { return (int)item > 0 && (int)item < ResourceCatalog.ItemCount && item != ItemType.Plank; }

        private void MigrateCaravans()
        {
            if (World.CaravanVersion == 1) return;
            World.Caravans = new List<CaravanData>(); World.CaravanExchanges = new List<CaravanExchangeData>(); World.NextCaravanId = 1;
            for (int i = 0; i < World.Units.Count; i++) World.Units[i].CaravanId = 0;
            for (int i = 0; i < World.Settlements.Count; i++) World.Settlements[i].NextCaravanTick = World.Tick + 300;
            World.CaravanVersion = 1;
        }
        private CaravanMemberData CaravanMember(CaravanData caravan, int unitId)
        {
            if (caravan != null) for (int i = 0; i < caravan.Members.Count; i++) if (caravan.Members[i].UnitId == unitId) return caravan.Members[i];
            return null;
        }
        private bool CaravanAvailable(UnitData u, int townId)
        {
            return u != null && IsCivilized(u.Species) && u.SettlementId == townId && u.Health > u.MaxHealth * .5f && u.Age >= 16 && u.CaravanId == 0 && CarriedLoad(u) <= .0001f && u.Hunger < .65f && u.Thirst < .75f && u.Fatigue < .75f && u.Task != UnitTask.Fight && u.Task != UnitTask.Flee && u.Task != UnitTask.CraftEquipment && u.Task != UnitTask.Process;
        }
        private bool HasActiveCaravan(int settlementId)
        {
            for (int i = 0; i < World.Caravans.Count; i++) if (World.Caravans[i].OriginId == settlementId && (int)World.Caravans[i].Phase < (int)CaravanPhase.Completed) return true;
            return false;
        }
        private bool AtCaravanHiringHall(UnitData unit, int settlementId)
        {
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData hall = World.Buildings[i];
                if (hall.Type == BuildingType.Hall && UsableBuilding(hall, settlementId) && DistanceSquared(unit.X, unit.Y, hall.X + .5f, hall.Y + .5f) < 2) return true;
            }
            return false;
        }
        private BuildingData CaravanStockBuilding(int settlementId, ItemType item, bool loading)
        {
            BuildingData best = null; float score = 0;
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData b = World.Buildings[i]; if (!UsableBuilding(b, settlementId)) continue;
                float available = loading ? AvailableForHaul(b, item) : FreeStorage(b);
                if (available > score + .0001f) { score = available; best = b; }
            }
            return best;
        }
        private float CaravanQuote(SettlementData home, SettlementData destination, ItemType export, ItemType import)
        {
            float tax = destination.EconomyPolicy == EconomicPolicy.Market ? destination.TaxRate : 0;
            return Math.Max(0, Math.Min(Math.Min(World.GetResourceBalance(home.Id, export).Surplus, World.GetResourceBalance(destination.Id, import).Surplus), Math.Min(World.GetResourceBalance(home.Id, import).Deficit, World.GetResourceBalance(destination.Id, export).Deficit / (1 - tax))));
        }
        public bool TryStartCaravan(int originId, int destinationId, ItemType export, ItemType import, int requestedHelpers = 2)
        {
            for (int i = 0; i < World.Units.Count; i++)
                if (CaravanAvailable(World.Units[i], originId) && StartCaravan(World.Units[i], World.GetSettlement(destinationId), export, import, requestedHelpers)) return true;
            return false;
        }
        private bool StartCaravan(UnitData leader, SettlementData destination, ItemType export, ItemType import, int requestedHelpers)
        {
            TrimCaravans(); if (World.Caravans.Count >= 64) return false;
            SettlementData home = World.GetSettlement(leader.SettlementId);
            if (home == null || destination == null || home.Id == destination.Id || destination.Population <= 0 || !TradeableItem(export) || !TradeableItem(import) || export == import || !World.HasTechnology(home.Id, "l03_caravans") || AtWar(home.Id, destination.Id) || HasActiveCaravan(home.Id) || World.NextCaravanId == int.MaxValue || requestedHelpers < 0 || requestedHelpers > 3) return false;
            BuildingData source = CaravanStockBuilding(home.Id, export, true), market = CaravanStockBuilding(destination.Id, import, true);
            float budget = CaravanQuote(home, destination, export, import);
            if (source == null || market == null || budget < .05f || !navigation.Find(World.Index(source.X, source.Y), World.Index(market.X, market.Y), scratchPath)) return false;
            budget = Math.Min(budget, Math.Min(AvailableForHaul(source, export), AvailableForHaul(market, import)));
            CaravanData caravan = new CaravanData { Id = World.NextCaravanId, OriginId = home.Id, DestinationId = destination.Id, LeaderId = leader.Id, MarketBuildingId = market.Id, ExportItem = export, ImportItem = import, StartedTick = World.Tick, LastChangeTick = World.Tick, LastReason = "Сбор груза для обмена; товары остаются у физических носителей." };
            bool paid = home.EconomyPolicy == EconomicPolicy.Market;
            for (int pass = -1; pass < World.Units.Count && caravan.Members.Count <= requestedHelpers && budget >= .05f; pass++)
            {
                UnitData u = pass < 0 ? leader : World.Units[pass];
                if (pass >= 0 && u.Id == leader.Id || !CaravanAvailable(u, home.Id)) continue;
                bool helper = caravan.Members.Count > 0;
                if (helper && paid && (!AtCaravanHiringHall(u, home.Id) || home.Food - caravan.Members.Count < home.Population * 2)) continue;
                if (!navigation.Find(World.Index((int)u.X, (int)u.Y), World.Index(source.X, source.Y), scratchPath)) continue;
                float quota = Math.Min(CarryCapacityFor(u), budget);
                caravan.Members.Add(new CaravanMemberData { UnitId = u.Id, SourceBuildingId = source.Id, ExportQuota = quota, HireFoodPaid = helper && paid ? 1 : 0 }); budget -= quota;
            }
            if (caravan.Members.Count == 0 || caravan.Members[0].UnitId != leader.Id) return false;
            // All contracts/routes/costs were checked. The meal is consumed, not duplicated as a stock.
            for (int i = 0; i < caravan.Members.Count; i++)
            {
                CaravanMemberData m = caravan.Members[i]; UnitData u = World.GetUnit(m.UnitId);
                if (m.HireFoodPaid > 0) { home.Food -= m.HireFoodPaid; u.Hunger = Math.Max(0, u.Hunger - .4f); }
                u.CaravanId = caravan.Id; ChangeTask(u, UnitTask.Idle); ClearRoute(u); u.DecisionTimer = 0;
            }
            World.NextCaravanId++; World.Caravans.Add(caravan); home.NextCaravanTick = World.Tick + 1200;
            TrimCaravans(); World.Revision++;
            AddEvent(EventKind.Built, home.X + .5f, home.Y + .5f, home.Name + ": караван из " + caravan.Members.Count + " жителей отправляется в " + destination.Name + ".");
            return true;
        }
        private void TrimCaravans()
        {
            for (int i = 0; i < World.Caravans.Count && World.Caravans.Count > 48;)
                if ((int)World.Caravans[i].Phase >= (int)CaravanPhase.Completed) World.Caravans.RemoveAt(i); else i++;
        }
        private bool TryChooseCaravan(UnitData unit, SettlementData home)
        {
            if (unit.CaravanId != 0)
            {
                CaravanData c = World.GetCaravan(unit.CaravanId); CaravanMemberData m = CaravanMember(c, unit.Id);
                if (c == null || m == null || m.Phase == CaravanMemberPhase.Done) { unit.CaravanId = 0; return false; }
                RouteCaravanMember(unit, c, m); return true;
            }
            if (home == null || World.Tick < home.NextCaravanTick || !CaravanAvailable(unit, home.Id) || !World.HasTechnology(home.Id, "l03_caravans") || HasActiveCaravan(home.Id)) return false;
            home.NextCaravanTick = World.Tick + 300;
            for (int t = 0; t < World.Settlements.Count; t++)
            {
                SettlementData destination = World.Settlements[t];
                if (destination.Id == home.Id || destination.Population <= 0 || AtWar(home.Id, destination.Id)) continue;
                for (int e = 1; e < ResourceCatalog.ItemCount; e++) for (int b = 1; b < ResourceCatalog.ItemCount; b++)
                    if (e != b && TradeableItem((ItemType)e) && TradeableItem((ItemType)b) && CaravanQuote(home, destination, (ItemType)e, (ItemType)b) >= .5f && StartCaravan(unit, destination, (ItemType)e, (ItemType)b, 2))
                    { RouteCaravanMember(unit, World.GetCaravan(unit.CaravanId), CaravanMember(World.GetCaravan(unit.CaravanId), unit.Id)); return true; }
            }
            return false;
        }
        private void RouteCaravanMember(UnitData u, CaravanData c, CaravanMemberData m)
        {
            BuildingData target = null;
            if (m.Phase == CaravanMemberPhase.Loading || m.Phase == CaravanMemberPhase.Ready)
            {
                target = World.GetBuilding(m.SourceBuildingId);
                if (!UsableBuilding(target, c.OriginId)) { target = CaravanStockBuilding(c.OriginId, c.ExportItem, true); if (target != null) m.SourceBuildingId = target.Id; }
            }
            else if (m.Phase == CaravanMemberPhase.Outbound || m.Phase == CaravanMemberPhase.AtMarket) target = World.GetBuilding(c.MarketBuildingId);
            else if (m.Phase == CaravanMemberPhase.Returning) target = CaravanStockBuilding(c.OriginId, u.CargoKind, false);
            if (target != null && UsableBuilding(target, m.Phase == CaravanMemberPhase.Outbound || m.Phase == CaravanMemberPhase.AtMarket ? c.DestinationId : c.OriginId) && SetTarget(u, target.X, target.Y))
            { ChangeTask(u, UnitTask.Caravan); m.BlockedSinceTick = 0; return; }
            if (m.BlockedSinceTick == 0) m.BlockedSinceTick = Math.Max(1, World.Tick);
            ChangeTask(u, UnitTask.Caravan); ClearRoute(u);
        }
        private bool PerformCaravanActivity(UnitData unit, SettlementData home, float dt)
        {
            if (unit.Task != UnitTask.Caravan || unit.CaravanId == 0) return false;
            CaravanData c = World.GetCaravan(unit.CaravanId); CaravanMemberData m = CaravanMember(c, unit.Id);
            if (c == null || m == null) { unit.CaravanId = 0; ChangeTask(unit, UnitTask.Idle); return true; }
            if (m.Phase == CaravanMemberPhase.Loading)
            {
                BuildingData b = World.GetBuilding(m.SourceBuildingId);
                if (!UsableBuilding(b, c.OriginId) || DistanceSquared(unit.X, unit.Y, b.X + .5f, b.Y + .5f) >= 2) { if (unit.DecisionTimer <= 0) RouteCaravanMember(unit, c, m); return true; }
                float amount = Math.Min(m.ExportQuota, Math.Min(CarrySpace(unit), Math.Min(AvailableForHaul(b, c.ExportItem), World.GetResourceBalance(c.OriginId, c.ExportItem).Surplus)));
                if (amount <= .0001f) { FinishCaravanMember(unit, m); return true; }
                b.Inventory[(int)c.ExportItem] -= amount; unit.CargoKind = c.ExportItem; unit.CargoAmount = amount; unit.CargoOwnerId = 0; m.Phase = CaravanMemberPhase.Ready; World.ResourceRevision++;
            }
            else if (m.Phase == CaravanMemberPhase.Outbound)
            {
                BuildingData market = World.GetBuilding(c.MarketBuildingId);
                if (UsableBuilding(market, c.DestinationId) && DistanceSquared(unit.X, unit.Y, market.X + .5f, market.Y + .5f) < 2) m.Phase = CaravanMemberPhase.AtMarket;
                else if (unit.DecisionTimer <= 0) RouteCaravanMember(unit, c, m);
            }
            else if (m.Phase == CaravanMemberPhase.Returning)
            {
                int cell = World.Index((int)unit.X, (int)unit.Y); BuildingData b = World.GetBuilding(World.Cells[cell].BuildingId);
                if (UsableBuilding(b, c.OriginId) && DistanceSquared(unit.X, unit.Y, b.X + .5f, b.Y + .5f) < 2)
                {
                    float amount = Math.Min(unit.CargoAmount, FreeStorage(b));
                    if (amount > 0) { b.Inventory[(int)unit.CargoKind] += amount; unit.CargoAmount -= amount; World.ResourceRevision++; }
                    if (unit.CargoAmount <= .0001f) { unit.CargoAmount = 0; unit.CargoKind = ItemType.None; unit.CargoOwnerId = 0; FinishCaravanMember(unit, m); }
                    else RouteCaravanMember(unit, c, m);
                }
                else if (unit.DecisionTimer <= 0) RouteCaravanMember(unit, c, m);
            }
            return true;
        }
        private void FinishCaravanMember(UnitData unit, CaravanMemberData member)
        { member.Phase = CaravanMemberPhase.Done; member.BlockedSinceTick = 0; unit.CaravanId = 0; ChangeTask(unit, UnitTask.Idle); ClearRoute(unit); unit.DecisionTimer = 0; }

        private bool ExchangeCaravanLoad(CaravanData c, UnitData unit, BuildingData market)
        {
            SettlementData home = World.GetSettlement(c.OriginId), destination = World.GetSettlement(c.DestinationId);
            if (home == null || destination == null || AtWar(home.Id, destination.Id) || unit.CargoKind != c.ExportItem || unit.CargoAmount <= 0 || !UsableBuilding(market, destination.Id) || DistanceSquared(unit.X, unit.Y, market.X + .5f, market.Y + .5f) >= 2) return false;
            float incoming = 0;
            for (int i = 0; i < c.Members.Count; i++) { UnitData other = World.GetUnit(c.Members[i].UnitId); if (other != null && other.CargoKind == c.ImportItem) incoming += other.CargoAmount; }
            float taxRate = destination.EconomyPolicy == EconomicPolicy.Market ? destination.TaxRate : 0;
            float amount = unit.CargoAmount;
            float allowed = Math.Min(AvailableForHaul(market, c.ImportItem), Math.Min(World.GetResourceBalance(destination.Id, c.ImportItem).Surplus, Math.Min(Math.Max(0, World.GetResourceBalance(home.Id, c.ImportItem).Deficit - incoming), World.GetResourceBalance(destination.Id, c.ExportItem).Deficit / (1 - taxRate))));
            // One cargo slot: an indivisible carrier batch prevents deleting an unsold second item.
            if (amount > allowed || CarriedLoad(unit) > CarryCapacityFor(unit) + .00001f) return false;
            float tax = amount * taxRate;
            market.Inventory[(int)c.ImportItem] -= amount;
            market.Inventory[(int)c.ExportItem] += amount - tax; market.TaxStocks[(int)c.ExportItem] += tax;
            unit.CargoKind = c.ImportItem; // Same physical quantity, now purchased at the destination.
            World.CaravanExchanges.Add(new CaravanExchangeData { CaravanId = c.Id, UnitId = unit.Id, BuildingId = market.Id, Tick = World.Tick, ExportItem = c.ExportItem, ImportItem = c.ImportItem, Amount = amount, TaxAmount = tax });
            if (World.CaravanExchanges.Count > 128) World.CaravanExchanges.RemoveAt(0);
            World.ResourceRevision++; return true;
        }
        public void CancelCaravan(int id)
        {
            CaravanData c = World.GetCaravan(id); if (c == null || (int)c.Phase >= (int)CaravanPhase.Completed) return;
            if (c.Phase == CaravanPhase.Returning) { c.Aborted = true; return; }
            c.Aborted = true; c.Phase = CaravanPhase.Returning; c.LastChangeTick = World.Tick; c.LastReason = "Рейс отменён; груз возвращается с носителями.";
            for (int i = 0; i < c.Members.Count; i++)
            {
                CaravanMemberData m = c.Members[i]; UnitData u = World.GetUnit(m.UnitId); if (m.Phase == CaravanMemberPhase.Done || u == null) continue;
                if (u.CargoAmount <= 0) FinishCaravanMember(u, m);
                else { m.Phase = CaravanMemberPhase.Returning; m.BlockedSinceTick = 0; ChangeTask(u, UnitTask.Idle); ClearRoute(u); u.DecisionTimer = 0; }
            }
            World.Revision++;
        }
        private void DropCaravanCargo(UnitData unit)
        {
            if (unit.CargoAmount > 0)
                World.GroundStocks.Add(new GroundStockData { Item = unit.CargoKind, Amount = unit.CargoAmount, X = unit.X, Y = unit.Y });
            unit.CargoAmount = 0; unit.CargoKind = ItemType.None; unit.CargoOwnerId = 0; World.ResourceRevision++;
        }
        private void ReleaseCaravanOnDeath(UnitData unit)
        {
            if (unit.CaravanId == 0) return;
            CaravanData c = World.GetCaravan(unit.CaravanId); CaravanMemberData m = CaravanMember(c, unit.Id);
            DropCaravanCargo(unit);
            if (m != null) { m.Phase = CaravanMemberPhase.Done; m.BlockedSinceTick = 0; }
            unit.CaravanId = 0;
        }
        private void UpdateCaravans()
        {
            for (int i = 0; i < World.Caravans.Count; i++)
            {
                CaravanData c = World.Caravans[i]; if ((int)c.Phase >= (int)CaravanPhase.Completed) continue;
                if (AtWar(c.OriginId, c.DestinationId) || !UsableBuilding(World.GetBuilding(c.MarketBuildingId), c.DestinationId)) CancelCaravan(c.Id);
                int active = 0, ready = 0, atMarket = 0;
                for (int n = 0; n < c.Members.Count; n++)
                {
                    CaravanMemberData m = c.Members[n]; if (m.Phase == CaravanMemberPhase.Done) continue;
                    UnitData u = World.GetUnit(m.UnitId);
                    if (u == null) { m.Phase = CaravanMemberPhase.Done; continue; }
                    if (u.Health <= 0) { ReleaseCaravanOnDeath(u); continue; }
                    if (m.BlockedSinceTick > 0 && World.Tick - m.BlockedSinceTick >= 600)
                    {
                        if (m.Phase != CaravanMemberPhase.Returning) { CancelCaravan(c.Id); }
                        else { DropCaravanCargo(u); FinishCaravanMember(u, m); c.Aborted = true; c.LastReason = "Путь домой закрыт: груз оставлен физической кучкой у носителя."; }
                    }
                    if (m.Phase == CaravanMemberPhase.Done) continue;
                    active++; if (m.Phase == CaravanMemberPhase.Ready) ready++;
                    BuildingData market = World.GetBuilding(c.MarketBuildingId);
                    if (m.Phase == CaravanMemberPhase.AtMarket && market != null && DistanceSquared(u.X, u.Y, market.X + .5f, market.Y + .5f) < 2) atMarket++;
                }
                if (active == 0) { c.Phase = c.Aborted ? CaravanPhase.Cancelled : CaravanPhase.Completed; c.LastChangeTick = World.Tick; continue; }
                if (c.Phase == CaravanPhase.Loading && ready == active)
                {
                    c.Phase = CaravanPhase.Outbound; c.LastChangeTick = World.Tick; c.LastReason = "Все носители загрузились; общий маршрут к партнёру.";
                    for (int n = 0; n < c.Members.Count; n++) if (c.Members[n].Phase == CaravanMemberPhase.Ready) { CaravanMemberData m = c.Members[n]; m.Phase = CaravanMemberPhase.Outbound; UnitData u = World.GetUnit(m.UnitId); ChangeTask(u, UnitTask.Idle); ClearRoute(u); u.DecisionTimer = 0; }
                }
                else if (c.Phase == CaravanPhase.Outbound && atMarket == active)
                {
                    int exchanges = 0;
                    for (int n = 0; n < c.Members.Count; n++)
                    {
                        CaravanMemberData m = c.Members[n]; if (m.Phase == CaravanMemberPhase.Done) continue; UnitData u = World.GetUnit(m.UnitId);
                        if (ExchangeCaravanLoad(c, u, World.GetBuilding(c.MarketBuildingId))) exchanges++;
                        m.Phase = CaravanMemberPhase.Returning; ChangeTask(u, UnitTask.Idle); ClearRoute(u); u.DecisionTimer = 0;
                    }
                    c.Phase = CaravanPhase.Returning; c.LastChangeTick = World.Tick; c.LastReason = "Обменено партий: " + exchanges + "; остаток возвращается без потери.";
                }
            }
            TrimCaravans();
        }
    }
}
