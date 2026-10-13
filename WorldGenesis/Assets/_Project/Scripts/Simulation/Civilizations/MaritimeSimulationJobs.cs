using System;
using System.Collections.Generic;
namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private bool MaritimeAdult(UnitData u) {return u!=null&&u.Health>0&&u.Age>=16&&IsCivilized(u.Species)&&u.ShipId==0&&u.CaravanId==0;}
        private BuildingData NearestHomePort(int settlementId,int cell)
        {BuildingData best=null;int distance=int.MaxValue;foreach(BuildingData b in World.Buildings){if(b.SettlementId!=settlementId||!UsablePort(b))continue;int n=Math.Abs(b.X-cell%World.Width)+Math.Abs(b.Y-cell/World.Width);if(n<distance){best=b;distance=n;}}return best;}
        private float ShipMaterialAvailable(SettlementData s,ItemType item,BuildingData destination)
        {float n=0;foreach(BuildingData b in World.Buildings)if(UsableBuilding(b,s.Id)&&navigation.Find(World.Index(b.X,b.Y),World.Index(destination.X,destination.Y),scratchPath,4096,true))n+=AvailableForHaul(b,item)+(b.Type==BuildingType.Hall&&item==ItemType.Plank?s.Wood:0);return n;}
        public bool TryStartShipConstruction(int unitId,int shipyardId,ShipKind kind)
        {
            UnitData u=World.GetUnit(unitId);BuildingData b=World.GetBuilding(shipyardId);ShipDefinition d=MaritimeCatalog.Get(kind);
            if(!MaritimeAdult(u)||d==null||MaritimeCatalog.IsForeign(kind)||b==null||b.Type!=BuildingType.Shipyard||!UsableBuilding(b,u.SettlementId)||b.BerthCell<0||!MaritimeCatalog.IsWater(World,b.BerthCell,kind)||!World.HasTechnology(u.SettlementId,d.Technology)||CarriedLoad(u)>.0001f||World.Ships.Count>=256)return false;
            bool targetReady=false;ShipConstructionData existing=World.GetShipOrder(b.MaritimeOrderId);
            if(existing!=null&&!existing.Complete&&!existing.Cancelled){if(existing.Kind!=kind||existing.WorkerId!=0&&existing.WorkerId!=u.Id)return false;}
            else
            {
                SettlementData s=World.GetSettlement(u.SettlementId);if(s==null||World.ShipOrders.Count>=512)return false;
                foreach(ItemAmount input in d.Inputs){float reserved=0;foreach(ShipConstructionData other in World.ShipOrders){BuildingData ob=World.GetBuilding(other.ShipyardId);if(ob==null||ob.SettlementId!=s.Id||other.Complete||other.Cancelled)continue;foreach(ItemAmount m in MaritimeCatalog.Get(other.Kind).Inputs)if(m.Item==input.Item)reserved+=Math.Max(0,m.Amount-other.Materials[(int)m.Item]);}if(ShipMaterialAvailable(s,input.Item,b)-reserved+.0001f<input.Amount)return false;}
                if(!SetTarget(u,b.X,b.Y))return false;targetReady=true;
                existing=new ShipConstructionData{Id=World.NextMaritimeOrderId++,ShipyardId=b.Id,Kind=kind};World.ShipOrders.Add(existing);b.MaritimeOrderId=existing.Id;
            }
            if(!targetReady&&!SetTarget(u,b.X,b.Y))return false;
            ChangeTask(u,UnitTask.Shipbuild);u.MaritimeOrderId=existing.Id;u.TargetBuildingId=b.Id;u.Profession=UnitProfession.Shipwright;u.DecisionTimer=1;existing.WorkerId=u.Id;return true;
        }
        public bool TryCancelShipConstruction(int orderId)
        {
            ShipConstructionData o=World.GetShipOrder(orderId);BuildingData b=o==null?null:World.GetBuilding(o.ShipyardId);if(o==null||o.Complete||o.Cancelled||b==null)return false;
            foreach(ItemAmount m in MaritimeCatalog.Get(o.Kind).Inputs){float n=o.Materials[(int)m.Item],store=Math.Min(n,StorageCapacity.FreeMaterials(b));b.Inventory[(int)m.Item]+=store;n-=store;if(n>.00001f)World.GroundStocks.Add(new GroundStockData{X=b.X+.5f,Y=b.Y+.5f,Item=m.Item,Amount=n});o.Materials[(int)m.Item]=0;}
            CancelShipOrder(o);return true;
        }
        private void CancelShipOrder(ShipConstructionData o)
        {o.Cancelled=true;UnitData u=World.GetUnit(o.WorkerId);o.WorkerId=0;if(u!=null&&u.MaritimeOrderId==o.Id){u.MaritimeOrderId=0;ChangeTask(u,UnitTask.Idle);u.DecisionTimer=0;}BuildingData b=World.GetBuilding(o.ShipyardId);if(b!=null&&b.MaritimeOrderId==o.Id)b.MaritimeOrderId=0;}
        private bool SupplyShipConstruction(UnitData u,SettlementData town,BuildingData b,ShipConstructionData o)
        {
            if(u.CargoAmount>0){if(!AtBuilding(u,b)){if(!SetTarget(u,b.X,b.Y))ChangeTask(u,UnitTask.Idle);return false;}o.Materials[(int)u.CargoKind]+=u.CargoAmount;u.CargoAmount=0;u.CargoKind=ItemType.None;}
            foreach(ItemAmount m in MaritimeCatalog.Get(o.Kind).Inputs)
            {
                float missing=m.Amount-o.Materials[(int)m.Item];if(missing<=.00001f)continue;
                foreach(BuildingData source in World.Buildings)
                {
                    if(!UsableBuilding(source,town.Id))continue;float available=AvailableForHaul(source,m.Item)+(source.Type==BuildingType.Hall&&m.Item==ItemType.Plank?town.Wood:0);if(available<=.00001f||!navigation.Find(World.Index(source.X,source.Y),World.Index(b.X,b.Y),scratchPath,4096,true))continue;
                    if(!AtBuilding(u,source)){if(SetTarget(u,source.X,source.Y))return false;continue;}
                    float take=Math.Min(missing,Math.Min(CarrySpace(u),available));if(take<=.00001f||!SetTarget(u,b.X,b.Y))continue;
                    float stocked=Math.Min(take,AvailableForHaul(source,m.Item));source.Inventory[(int)m.Item]-=stocked;if(m.Item==ItemType.Plank&&source.Type==BuildingType.Hall)town.Wood-=take-stocked;
                    u.CargoKind=m.Item;u.CargoAmount=take;u.CargoOwnerId=0;o.LastReason="Доставка материалов";return false;
                }
                o.LastReason="Не хватает доступных материалов";ChangeTask(u,UnitTask.Idle);return false;
            }
            if(!AtBuilding(u,b)){if(!SetTarget(u,b.X,b.Y))ChangeTask(u,UnitTask.Idle);return false;}return true;
        }
        private bool PerformMaritimeActivity(UnitData u,SettlementData town,float dt)
        {
            if(u.Task==UnitTask.Shipbuild)
            {
                ShipConstructionData o=World.GetShipOrder(u.MaritimeOrderId);BuildingData b=o==null?null:World.GetBuilding(o.ShipyardId);
                if(o==null||o.Complete||o.Cancelled||!MaritimeAdult(u)||town==null||!UsableBuilding(b,town.Id)){ChangeTask(u,UnitTask.Idle);return true;}
                if(!SupplyShipConstruction(u,town,b,o))return true;
                ShipDefinition d=MaritimeCatalog.Get(o.Kind);o.Progress=Math.Min(d.Seconds,o.Progress+dt*GetWorkMultiplier(u));o.LastReason="Строит корпус";GainCraftExperience(u,dt);
                if(o.Progress+.00001f<d.Seconds)return true;
                if(!MaritimeCatalog.IsWater(World,b.BerthCell,o.Kind)){o.LastReason="Нет воды для спуска";return true;}
                BuildingData port=NearestHomePort(town.Id,b.BerthCell);if(port==null||!FindSeaPath(b.BerthCell,port.BerthCell,o.Kind,scratchPath)){o.LastReason="Нет водного пути к порту";return true;}
                ShipData ship=CreateShip(o.Kind,town.Id,b.BerthCell);ship.HomePortId=port.Id;ship.DestinationPortId=port.Id;ship.Phase=ShipPhase.Stranded;ship.LastReason="Нужен экипаж у верфи";
                // Launch remains at the actual yard berth. Boarding uses the adjacent shipyard until the first voyage.
                if(port.BerthCell==b.BerthCell){ship.DockedPortId=port.Id;ship.Phase=ShipPhase.Docked;}
                o.ShipId=ship.Id;o.Complete=true;o.WorkerId=0;Array.Clear(o.Materials,0,o.Materials.Length);b.MaritimeOrderId=0;World.ShipsBuilt++;u.MaritimeOrderId=0;ChangeTask(u,UnitTask.Idle);return true;
            }
            if(u.Task!=UnitTask.Dockwork)return false;DockOrderData order=World.GetDockOrder(u.MaritimeOrderId);
            if(order==null||order.Cancelled||order.Complete||u.Health<=0||!IsCivilized(u.Species)){ChangeTask(u,UnitTask.Idle);return true;}
            BuildingData destination=World.GetBuilding(order.BuildingId);if(destination==null||!BuildingAccess.IsUsable(World,destination)){CancelDockOrder(order);return true;}
            if(order.Kind==DockOrderKind.Supply)return PerformHarborSupply(u,town,destination,order);
            ShipData vessel=World.GetShip(order.ShipId);if(vessel==null||vessel.Health<=0||vessel.Phase!=ShipPhase.Docked&&vessel.Phase!=ShipPhase.Stranded){CancelDockOrder(order);return true;}
            if(!AtBuilding(u,destination)){if(!SetTarget(u,destination.X,destination.Y))CancelDockOrder(order);return true;}
            if(order.Kind==DockOrderKind.Crew||order.Kind==DockOrderKind.Passenger)
            {
                if(destination.BerthCell<0||World.Index((int)vessel.X,(int)vessel.Y)!=destination.BerthCell||!MaritimeBerthAvailable(destination)||DistanceSquared(u.X,u.Y,vessel.X,vessel.Y)>4.01f){CancelDockOrder(order);return true;}
                order.Progress=Math.Min(2,order.Progress+dt);if(order.Progress<2)return true;
                ShipDefinition d=MaritimeCatalog.Get(vessel.Kind);if((order.Kind==DockOrderKind.Crew&&vessel.CrewIds.Count>=d.MaxCrew)||(order.Kind==DockOrderKind.Passenger&&vessel.PassengerIds.Count>=d.PassengerCapacity)){CancelDockOrder(order);return true;}
                if(order.Guard&&u.NavalWeapon==ItemType.None){if(vessel.Cargo[(int)ItemType.Sword]<1||vessel.Cargo[(int)ItemType.Armor]<1){CancelDockOrder(order);return true;}vessel.Cargo[(int)ItemType.Sword]--;vessel.Cargo[(int)ItemType.Armor]--;u.NavalWeapon=ItemType.Sword;u.NavalArmor=ItemType.Armor;}
                CompleteDockOrder(order,u);ClearRoute(u);u.InsideBuildingId=0;u.ShipId=vessel.Id;u.Task=UnitTask.Sail;u.X=vessel.X;u.Y=vessel.Y;
                if(order.Kind==DockOrderKind.Crew){vessel.CrewIds.Add(u.Id);u.Profession=order.Captain?UnitProfession.Captain:order.Guard?UnitProfession.NavalGuard:UnitProfession.Sailor;if(order.Captain)vessel.CaptainUnitId=u.Id;}else vessel.PassengerIds.Add(u.Id);
                return true;
            }
            order.Progress=Math.Min(2,order.Progress+dt);if(order.Progress<2)return true;
            bool legal=ShipAtHarbor(vessel,destination)&&!AtWar(u.SettlementId,vessel.OwnerSettlementId)&&u.Age>=16;
            bool success=legal&&MarketExchangeHarborNetwork(GetHarborWarehouses(vessel.DockedPortId),vessel.Cargo,vessel.AgriculturalCargo,order.Item,order.Product,order.Remaining,order.ShipSells,u.Id,vessel.Id,MaritimeCatalog.FreeHold(vessel));
            if(success)CompleteDockOrder(order,u);else{vessel.LastReason="Сделка недоступна: запасы, деньги, вместимость или мир";CancelDockOrder(order);}return true;
        }
        private ShipData CreateShip(ShipKind kind,int owner,int cell)
        {ShipDefinition d=MaritimeCatalog.Get(kind);var s=new ShipData{Id=World.NextShipId++,Kind=kind,OwnerSettlementId=owner,X=cell%World.Width+.5f,Y=cell/World.Width+.5f,Health=d.Health,MaxHealth=d.Health,LastChangeTick=World.Tick};World.Ships.Add(s);return s;}
        private int PendingCrew(int shipId) {int n=0;foreach(DockOrderData o in World.DockOrders)if(o.ShipId==shipId&&o.Kind==DockOrderKind.Crew&&!o.Complete&&!o.Cancelled)n++;return n;}
        private BuildingData BoardingSite(ShipData s)
        {
            BuildingData port=World.GetBuilding(s.DockedPortId);if(UsablePort(port))return port;
            if(s.Phase==ShipPhase.Stranded)foreach(BuildingData b in World.Buildings)if(b.Type==BuildingType.Shipyard&&b.SettlementId==s.OwnerSettlementId&&BuildingAccess.IsUsable(World,b)&&MaritimeBerthAvailable(b)&&Math.Abs(s.X-(b.BerthCell%World.Width+.5f))<.01f&&Math.Abs(s.Y-(b.BerthCell/World.Width+.5f))<.01f)return b;
            return null;
        }
        private bool StartDockOrder(UnitData u,BuildingData b,DockOrderData o)
        {
            if(u==null||b==null||u.Health<=0||u.ShipId!=0||u.CaravanId!=0||u.MaritimeOrderId!=0||World.DockOrders.Count>=1024||!SetTarget(u,b.X,b.Y))return false;
            ChangeTask(u,UnitTask.Dockwork);o.Id=World.NextMaritimeOrderId++;o.WorkerId=u.Id;o.BuildingId=b.Id;World.DockOrders.Add(o);u.MaritimeOrderId=o.Id;u.TargetBuildingId=b.Id;u.DecisionTimer=1;return true;
        }
        public bool TryAssignCrew(int unitId,int shipId,bool captain=false,bool guard=false)
        {
            UnitData u=World.GetUnit(unitId);ShipData s=World.GetShip(shipId);BuildingData b=s==null?null:BoardingSite(s);
            if(!MaritimeAdult(u)||s==null||b==null||u.SettlementId!=s.OwnerSettlementId||CarriedLoad(u)>.0001f||s.CrewIds.Count+PendingCrew(s.Id)>=MaritimeCatalog.Get(s.Kind).MaxCrew||captain&&s.CaptainUnitId!=0||guard&&(!World.HasTechnology(u.SettlementId,"n06_naval_warfare")||u.NavalWeapon==ItemType.None&&(s.Cargo[(int)ItemType.Sword]<1||s.Cargo[(int)ItemType.Armor]<1)))return false;
            if(captain)foreach(DockOrderData o in World.DockOrders)if(o.ShipId==s.Id&&o.Captain&&!o.Complete&&!o.Cancelled)return false;
            return StartDockOrder(u,b,new DockOrderData{Kind=DockOrderKind.Crew,ShipId=s.Id,Captain=captain,Guard=guard});
        }
        public bool TryBoardPassenger(int unitId,int shipId)
        {
            UnitData u=World.GetUnit(unitId);ShipData s=World.GetShip(shipId);BuildingData b=s==null?null:BoardingSite(s);
            return u!=null&&CarriedLoad(u)<.00001f&&IsCivilized(u.Species)&&s!=null&&b!=null&&u.SettlementId==s.OwnerSettlementId&&s.PassengerIds.Count<MaritimeCatalog.Get(s.Kind).PassengerCapacity&&StartDockOrder(u,b,new DockOrderData{Kind=DockOrderKind.Passenger,ShipId=s.Id});
        }
        public bool TryStartShipTrade(int unitId,int shipId,int warehouseId,ItemType item,float quantity,bool shipSells)
        {UnitData u=World.GetUnit(unitId);ShipData s=World.GetShip(shipId);BuildingData b=World.GetBuilding(warehouseId);if(!MaritimeAdult(u)||!ShipAtHarbor(s,b)||u.SettlementId!=b.SettlementId||!PositiveFinite(quantity)||AtWar(u.SettlementId,s.OwnerSettlementId))return false;bool result=StartDockOrder(u,b,new DockOrderData{Kind=DockOrderKind.TradeMaterial,ShipId=shipId,Item=item,Remaining=quantity,ShipSells=shipSells});if(result)u.Profession=UnitProfession.HarborMaster;return result;}
        public bool TryStartShipAgricultureTrade(int unitId,int shipId,int warehouseId,AgriculturalProduct item,float quantity,bool shipSells)
        {UnitData u=World.GetUnit(unitId);ShipData s=World.GetShip(shipId);BuildingData b=World.GetBuilding(warehouseId);if(!MaritimeAdult(u)||!ShipAtHarbor(s,b)||u.SettlementId!=b.SettlementId||!PositiveFinite(quantity)||AtWar(u.SettlementId,s.OwnerSettlementId))return false;return StartDockOrder(u,b,new DockOrderData{Kind=DockOrderKind.TradeAgriculture,ShipId=shipId,Product=item,Remaining=quantity,ShipSells=shipSells});}
        private void CompleteDockOrder(DockOrderData o,UnitData u){o.Complete=true;u.MaritimeOrderId=0;ChangeTask(u,UnitTask.Idle);u.DecisionTimer=0;}
        private void CancelDockOrder(DockOrderData o){o.Cancelled=true;UnitData u=World.GetUnit(o.WorkerId);if(u!=null&&u.MaritimeOrderId==o.Id){u.MaritimeOrderId=0;ChangeTask(u,UnitTask.Idle);u.DecisionTimer=0;}}
        private void ReleaseMaritimeJob(UnitData u,UnitTask next)
        {
            if(u.Task!=UnitTask.Shipbuild&&u.Task!=UnitTask.Dockwork)return;
            if(u.Task==next)return;ShipConstructionData c=World.GetShipOrder(u.MaritimeOrderId);if(c!=null&&c.WorkerId==u.Id)c.WorkerId=0;
            DockOrderData o=World.GetDockOrder(u.MaritimeOrderId);if(o!=null&&!o.Complete)o.Cancelled=true;u.MaritimeOrderId=0;
        }
        public bool TrySupplyHarbor(int unitId,int warehouseId,ItemType item,AgriculturalProduct product,MaritimeProvision provision,float quantity)
        {
            UnitData u=World.GetUnit(unitId);BuildingData b=World.GetBuilding(warehouseId);if(!MaritimeAdult(u)||b==null||b.Type!=BuildingType.HarborWarehouse||b.SettlementId!=u.SettlementId||!PositiveFinite(quantity)||CarriedLoad(u)>.00001f||((item!=ItemType.None?1:0)+(product!=AgriculturalProduct.None?1:0)+(provision!=MaritimeProvision.None?1:0)!=1))return false;
            bool result=StartDockOrder(u,b,new DockOrderData{Kind=DockOrderKind.Supply,Item=item,Product=product,Provision=provision,Remaining=quantity});if(result)u.Profession=UnitProfession.Dockworker;return result;
        }
        public bool TrySupplyShip(int unitId,int shipId,MaritimeProvision provision,float quantity)
        {
            UnitData u=World.GetUnit(unitId);ShipData ship=World.GetShip(shipId);BuildingData site=ship==null?null:BoardingSite(ship);
            if(!MaritimeAdult(u)||ship==null||site==null||ship.OwnerSettlementId!=u.SettlementId||provision==MaritimeProvision.None||!PositiveFinite(quantity)||CarriedLoad(u)>.00001f)return false;
            bool result=StartDockOrder(u,site,new DockOrderData{Kind=DockOrderKind.Supply,ShipId=shipId,Provision=provision,Remaining=quantity});if(result)u.Profession=UnitProfession.Dockworker;return result;
        }
        private bool PerformHarborSupply(UnitData u,SettlementData town,BuildingData dest,DockOrderData o)
        {
            if(town==null||u.Age<16){CancelDockOrder(o);return true;}
            ShipData supplyShip=World.GetShip(o.ShipId);
            if(o.ShipId!=0&&(supplyShip==null||BoardingSite(supplyShip)!=dest)){CancelDockOrder(o);return true;}
            float availableRoom=supplyShip==null?StorageCapacity.FreeMaterials(dest):MaritimeCatalog.FreeHold(supplyShip);
            if(CarriedLoad(u)>.00001f)
            {
                if(!AtBuilding(u,dest)){if(!SetTarget(u,dest.X,dest.Y))CancelDockOrder(o);return true;}
                float free=availableRoom,delivered=0;
                if(o.Item!=ItemType.None&&u.CargoKind==o.Item){delivered=Math.Min(free,u.CargoAmount);dest.Inventory[(int)o.Item]+=delivered;u.CargoAmount-=delivered;if(u.CargoAmount<=.00001f){u.CargoAmount=0;u.CargoKind=ItemType.None;}}
                else if(o.Product!=AgriculturalProduct.None&&u.HarvestProduct==o.Product){delivered=Math.Min(free,u.HarvestAmount);dest.AgriculturalStock[(int)o.Product]+=delivered;u.HarvestAmount-=delivered;if(u.HarvestAmount<=.00001f){u.HarvestAmount=0;u.HarvestProduct=AgriculturalProduct.None;}}
                else if(o.Provision==MaritimeProvision.Food){delivered=Math.Min(free,u.CarriedFood);if(supplyShip==null)dest.PortFoodStored+=delivered;else supplyShip.Food+=delivered;u.CarriedFood-=delivered;}
                else if(o.Provision==MaritimeProvision.Water){delivered=Math.Min(free,u.CarriedWater);if(supplyShip==null)dest.WaterStored+=delivered;else supplyShip.Water+=delivered;u.CarriedWater-=delivered;}
                o.Remaining=Math.Max(0,o.Remaining-delivered);if(o.Remaining<=.00001f){CompleteDockOrder(o,u);return true;}if(CarriedLoad(u)>.00001f){CancelDockOrder(o);return true;}
            }
            foreach(BuildingData source in World.Buildings)
            {
                if(source.Id==dest.Id||!UsableBuilding(source,town.Id))continue;
                float available=o.Item!=ItemType.None?AvailableForHaul(source,o.Item)+(o.Item==ItemType.Plank&&source.Type==BuildingType.Hall?town.Wood:0):o.Product!=AgriculturalProduct.None?Math.Max(0,source.AgriculturalStock[(int)o.Product]-IndustryAgricultureReserved(source,o.Product)):o.Provision==MaritimeProvision.Food?(source.Type==BuildingType.Hall?town.Food:source.PortFoodStored):source.Type==BuildingType.Hall||source.Type==BuildingType.Well||source.Type==BuildingType.HarborWarehouse?source.WaterStored:0;
                if(available<=.00001f||!navigation.Find(World.Index(source.X,source.Y),World.Index(dest.X,dest.Y),scratchPath,4096,true))continue;
                if(!AtBuilding(u,source)){if(SetTarget(u,source.X,source.Y))return true;continue;}
                float take=Math.Min(o.Remaining,Math.Min(available,Math.Min(CarrySpace(u),availableRoom)));if(take<=.00001f||!SetTarget(u,dest.X,dest.Y))continue;
                if(o.Item!=ItemType.None){float stocked=Math.Min(take,AvailableForHaul(source,o.Item));source.Inventory[(int)o.Item]-=stocked;if(o.Item==ItemType.Plank&&source.Type==BuildingType.Hall)town.Wood-=take-stocked;u.CargoKind=o.Item;u.CargoAmount=take;u.CargoOwnerId=0;}
                else if(o.Product!=AgriculturalProduct.None){source.AgriculturalStock[(int)o.Product]-=take;u.HarvestProduct=o.Product;u.HarvestAmount=take;}
                else if(o.Provision==MaritimeProvision.Food){if(source.Type==BuildingType.Hall)town.Food-=take;else source.PortFoodStored-=take;u.CarriedFood=take;}
                else{source.WaterStored-=take;u.CarriedWater=take;}
                return true;
            }
            CancelDockOrder(o);return true;
        }
        private bool ChooseMaritimeProcurement(UnitData u,SettlementData town)
        {
            if(World.Tick<town.NextMaritimeTick)return false;
            foreach(DockOrderData o in World.DockOrders)if(!o.Complete&&!o.Cancelled&&(o.Kind==DockOrderKind.TradeMaterial||o.Kind==DockOrderKind.TradeAgriculture)){BuildingData b=World.GetBuilding(o.BuildingId);if(b!=null&&b.SettlementId==town.Id)return false;}
            foreach(ShipData ship in World.Ships)
            {
                BuildingData port=World.GetBuilding(ship.DockedPortId);if(ship.OwnerSettlementId!=0||ship.Phase!=ShipPhase.Docked||port==null||port.SettlementId!=town.Id||!HasHarborAuthority(port.Id))continue;
                List<BuildingData> parts=GetHarborWarehouses(port.Id);if(parts.Count==0)continue;
                float budget=0;foreach(BuildingData b in parts)budget+=WholeMaritimeUnits(b.Inventory[(int)ItemType.CopperCoin])+WholeMaritimeUnits(b.TaxStocks[(int)ItemType.CopperCoin])+10*(WholeMaritimeUnits(b.Inventory[(int)ItemType.SilverCoin])+WholeMaritimeUnits(b.TaxStocks[(int)ItemType.SilverCoin]))+100*(WholeMaritimeUnits(b.Inventory[(int)ItemType.GoldCoin])+WholeMaritimeUnits(b.TaxStocks[(int)ItemType.GoldCoin]));
                foreach(MarketNeedData need in World.MarketNeeds)
                {
                    if(need.SettlementId!=town.Id||need.State==MarketNeedState.Satisfied||need.ExpiresTick<World.Tick)continue;
                    float deficit=Math.Max(0,need.Target-World.GetSettlementStock(town.Id,need.Item));float quantity=(float)Math.Floor(Math.Min(10,Math.Min(deficit,Math.Min(ship.Cargo[(int)need.Item],budget/Math.Max(1,World.MarketPrice(town.Id,need.Item))))));
                    if(quantity>=1&&TryStartShipTrade(u.Id,ship.Id,parts[0].Id,need.Item,quantity,true)){town.NextMaritimeTick=World.Tick+50;return true;}
                }
            }
            town.NextMaritimeTick=World.Tick+30;return false;
        }
        private bool ChooseMaritimeWork(UnitData u,SettlementData town)
        {
            if(!MaritimeAdult(u)||town==null||u.Hunger>.5f||u.Thirst>.65f||u.Fatigue>.72f||CarriedLoad(u)>.00001f)return false;
            if(ChooseMaritimeProcurement(u,town))return true;
            foreach(ShipConstructionData o in World.ShipOrders){BuildingData b=World.GetBuilding(o.ShipyardId);if(!o.Complete&&!o.Cancelled&&o.WorkerId==0&&b!=null&&b.SettlementId==town.Id&&TryStartShipConstruction(u.Id,b.Id,o.Kind))return true;}
            foreach(ShipData s in World.Ships)if(s.OwnerSettlementId==town.Id&&s.Phase==ShipPhase.Stranded&&BoardingSite(s)!=null&&(s.Food<4||s.Water<4))
            {bool supply=false;foreach(DockOrderData o in World.DockOrders)if(o.ShipId==s.Id&&o.Kind==DockOrderKind.Supply&&!o.Complete&&!o.Cancelled)supply=true;if(!supply&&TrySupplyShip(u.Id,s.Id,s.Food<4?MaritimeProvision.Food:MaritimeProvision.Water,4))return true;}
            foreach(ShipData s in World.Ships)if(World.Tick>=u.MaritimeShoreLeaveUntilTick&&s.OwnerSettlementId==town.Id&&BoardingSite(s)!=null&&s.CrewIds.Count+PendingCrew(s.Id)<MaritimeCatalog.Get(s.Kind).MinCrew){bool reservedCaptain=false;foreach(DockOrderData o in World.DockOrders)if(o.ShipId==s.Id&&!o.Complete&&!o.Cancelled&&o.Captain)reservedCaptain=true;if(TryAssignCrew(u.Id,s.Id,s.CaptainUnitId==0&&!reservedCaptain))return true;}
            if(town.Food<town.Population)return false;
            foreach(BuildingData b in World.Buildings)if(b.Type==BuildingType.HarborWarehouse&&b.SettlementId==town.Id&&BuildingAccess.IsUsable(World,b))
            {bool supplied=false;foreach(DockOrderData o in World.DockOrders)if(o.BuildingId==b.Id&&o.Kind==DockOrderKind.Supply&&!o.Complete&&!o.Cancelled)supplied=true;if(supplied)continue;if(b.PortFoodStored<12&&TrySupplyHarbor(u.Id,b.Id,ItemType.None,AgriculturalProduct.None,MaritimeProvision.Food,Math.Min(4,12-b.PortFoodStored)))return true;if(b.WaterStored<12&&TrySupplyHarbor(u.Id,b.Id,ItemType.None,AgriculturalProduct.None,MaritimeProvision.Water,Math.Min(4,12-b.WaterStored)))return true;}
            bool owns=false;foreach(ShipData s in World.Ships)if(s.OwnerSettlementId==town.Id&&s.Phase!=ShipPhase.Sunk&&s.Phase!=ShipPhase.Departed)owns=true;
            if(!owns&&town.Population>=8)foreach(BuildingData b in World.Buildings)if(b.Type==BuildingType.Shipyard&&b.SettlementId==town.Id&&TryStartShipConstruction(u.Id,b.Id,ShipKind.FishingBoat))return true;
            return false;
        }
    }
}
