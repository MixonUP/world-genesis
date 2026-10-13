using System;
using System.Collections.Generic;
namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private int[] seaParents,seaQueue;
        private void MigrateMaritime()
        { if(World.MaritimeVersion>=1)return;World.MaritimeVersion=1;World.NextWandererTick=World.Tick+1200;foreach(BuildingData b in World.Buildings)InitializeMaritimeBuilding(b); }
        private void InitializeMaritimeBuilding(BuildingData b)
        { if(b.Type==BuildingType.Port||b.Type==BuildingType.Shipyard)b.BerthCell=FindBerth(b.X,b.Y); }
        public int FindBerth(int x,int y)
        {
            int fallback=-1;
            for(int d=0;d<4;d++)
            {
                int nx=x+Dx[d],ny=y+Dy[d];if(!World.InBounds(nx,ny)||!MaritimeCatalog.IsWater(World,World.Index(nx,ny),ShipKind.FishingBoat))continue;
                if(fallback<0)fallback=World.Index(nx,ny);int outerX=nx+Dx[d],outerY=ny+Dy[d];
                if(World.InBounds(outerX,outerY)&&MaritimeCatalog.IsWater(World,World.Index(outerX,outerY),ShipKind.FishingBoat))return World.Index(outerX,outerY);
            }
            return fallback;
        }
        private bool MaritimeBerthAvailable(BuildingData b)
        {
            if(b==null||b.BerthCell<0||b.BerthCell>=World.Cells.Length)return false;int dx=b.BerthCell%World.Width-b.X,dy=b.BerthCell/World.Width-b.Y,length=Math.Abs(dx)+Math.Abs(dy);
            if(length<1||length>2||dx!=0&&dy!=0)return false;
            for(int n=1;n<=length;n++)if(!MaritimeCatalog.IsWater(World,World.Index(b.X+Math.Sign(dx)*n,b.Y+Math.Sign(dy)*n),ShipKind.FishingBoat))return false;
            return true;
        }
        public bool IsCoastalSite(int x,int y) {return World.InBounds(x,y)&&World.IsWalkable(x,y)&&FindBerth(x,y)>=0;}
        private bool UsablePort(BuildingData b)
        {return b!=null&&b.Type==BuildingType.Port&&BuildingAccess.IsUsable(World,b)&&MaritimeBerthAvailable(b);}
        private bool FindSeaPath(int start,int target,ShipKind kind,List<int> result)
        {
            result.Clear();if(!MaritimeCatalog.IsWater(World,start,kind)||!MaritimeCatalog.IsWater(World,target,kind))return false;
            if(start==target)return true;
            if(seaParents==null||seaParents.Length!=World.Cells.Length){seaParents=new int[World.Cells.Length];seaQueue=new int[World.Cells.Length];}
            for(int i=0;i<seaParents.Length;i++)seaParents[i]=-2;int head=0,tail=0;seaParents[start]=-1;seaQueue[tail++]=start;
            while(head<tail){int c=seaQueue[head++],x=c%World.Width,y=c/World.Width;for(int d=0;d<4;d++){int nx=x+Dx[d],ny=y+Dy[d];if(!World.InBounds(nx,ny))continue;int n=World.Index(nx,ny);if(seaParents[n]!=-2||!MaritimeCatalog.IsWater(World,n,kind))continue;seaParents[n]=c;if(n==target){for(int p=n;p!=start;p=seaParents[p])result.Add(p);result.Reverse();return true;}seaQueue[tail++]=n;}}
            return false;
        }
        public bool HasOceanRoute(int portId)
        {BuildingData p=World.GetBuilding(portId);return UsablePort(p)&&FindOceanExit(p.BerthCell,ShipKind.FishingBoat,new List<int>())>=0;}
        private int FindOceanExit(int start,ShipKind kind,List<int> result)
        {
            result.Clear();if(!MaritimeCatalog.IsWater(World,start,kind))return -1;
            if(seaParents==null||seaParents.Length!=World.Cells.Length){seaParents=new int[World.Cells.Length];seaQueue=new int[World.Cells.Length];}
            for(int i=0;i<seaParents.Length;i++)seaParents[i]=-2;
            int head=0,tail=0;seaQueue[tail++]=start;seaParents[start]=-1;
            // One traversal of this water component, independent of boundary length.
            while(head<tail)
            {
                int c=seaQueue[head++],x=c%World.Width,y=c/World.Width;TileData tile=World.Cells[c];
                if((x==0||y==0||x==World.Width-1||y==World.Height-1)&&(tile.WaterKind==WaterKind.Ocean||tile.WaterKind==WaterKind.Estuary))
                {for(int p=c;p!=start;p=seaParents[p])result.Add(p);result.Reverse();return c;}
                for(int d=0;d<4;d++){int nx=x+Dx[d],ny=y+Dy[d];if(!World.InBounds(nx,ny))continue;int n=World.Index(nx,ny);if(seaParents[n]!=-2||!MaritimeCatalog.IsWater(World,n,kind))continue;seaParents[n]=c;seaQueue[tail++]=n;}
            }
            return -1;
        }
        public List<BuildingData> GetHarborWarehouses(int portId)
        {
            var result=new List<BuildingData>();BuildingData p=World.GetBuilding(portId);if(!UsablePort(p))return result;
            var connected=new List<BuildingData>{p};
            for(int q=0;q<connected.Count;q++)foreach(BuildingData b in World.Buildings)
            {
                if(b.Type!=BuildingType.HarborWarehouse||b.SettlementId!=p.SettlementId||!BuildingAccess.IsUsable(World,b)||result.Contains(b))continue;
                BuildingData a=connected[q];if(Math.Abs(a.X-b.X)+Math.Abs(a.Y-b.Y)>3)continue;
                if(!MaritimeLandPath(World.Index(a.X,a.Y),World.Index(b.X,b.Y),6))continue;
                result.Add(b);connected.Add(b);
            }
            return result;
        }
        public float HarborCapacity(int portId){float n=0;foreach(BuildingData b in GetHarborWarehouses(portId))n+=StorageCapacity.Capacity(b);return n;}
        public float HarborUsed(int portId){float n=0;foreach(BuildingData b in GetHarborWarehouses(portId))n+=StorageCapacity.TotalUsed(b);return n;}
        private bool ShipAtHarbor(ShipData s,BuildingData b)
        {if(s==null||s.Health<=0||s.Phase!=ShipPhase.Docked||b==null)return false;BuildingData p=World.GetBuilding(s.DockedPortId);return UsablePort(p)&&GetHarborWarehouses(p.Id).Contains(b)&&Math.Abs(s.X-(p.BerthCell%World.Width+.5f))<.01f&&Math.Abs(s.Y-(p.BerthCell/World.Width+.5f))<.01f;}
        public bool TryTransferShipCargo(int shipId,int warehouseId,ItemType item,float quantity,bool toShip)
        {
            ShipData s=World.GetShip(shipId);BuildingData b=World.GetBuilding(warehouseId);
            if(!ShipAtHarbor(s,b)||s.OwnerSettlementId!=b.SettlementId||(int)item<=0||(int)item>=ResourceCatalog.ItemCount||!PositiveFinite(quantity))return false;
            if(MaritimeCoin(item)&&quantity!=WholeMaritimeUnits(quantity))return false;
            if(toShip){if(AvailableForHaul(b,item)+.00001f<quantity||MaritimeCatalog.FreeHold(s)+.00001f<quantity)return false;b.Inventory[(int)item]-=quantity;s.Cargo[(int)item]+=quantity;}
            else{if(s.Cargo[(int)item]+.00001f<quantity||StorageCapacity.FreeMaterials(b)+.00001f<quantity)return false;s.Cargo[(int)item]-=quantity;b.Inventory[(int)item]+=quantity;}
            World.ResourceRevision++;World.Revision++;return true;
        }
        public bool TryTransferShipAgriculture(int shipId,int warehouseId,AgriculturalProduct item,float quantity,bool toShip)
        {
            ShipData s=World.GetShip(shipId);BuildingData b=World.GetBuilding(warehouseId);
            if(!ShipAtHarbor(s,b)||s.OwnerSettlementId!=b.SettlementId||(int)item<=0||(int)item>=FishingCatalog.AgriculturalProductCount||!PositiveFinite(quantity))return false;
            if(toShip){if(b.AgriculturalStock[(int)item]-IndustryAgricultureReserved(b,item)+.00001f<quantity||MaritimeCatalog.FreeHold(s)+.00001f<quantity)return false;b.AgriculturalStock[(int)item]-=quantity;s.AgriculturalCargo[(int)item]+=quantity;}
            else{if(s.AgriculturalCargo[(int)item]+.00001f<quantity||StorageCapacity.FreeAgriculture(b,item)+.00001f<quantity)return false;s.AgriculturalCargo[(int)item]-=quantity;b.AgriculturalStock[(int)item]+=quantity;}
            World.ResourceRevision++;World.Revision++;return true;
        }
        public bool TryTransferShipProvisions(int shipId,int warehouseId,float food,float water,bool toShip)
        {
            ShipData s=World.GetShip(shipId);BuildingData b=World.GetBuilding(warehouseId);
            if(!ShipAtHarbor(s,b)||s.OwnerSettlementId!=b.SettlementId||!NonnegativeFinite(food)||!NonnegativeFinite(water)||food+water<=0)return false;
            if(toShip){if(b.PortFoodStored+.00001f<food||b.WaterStored+.00001f<water||MaritimeCatalog.FreeHold(s)+.00001f<food+water)return false;b.PortFoodStored-=food;b.WaterStored-=water;s.Food+=food;s.Water+=water;}
            else{if(s.Food+.00001f<food||s.Water+.00001f<water||StorageCapacity.FreeMaterials(b)+.00001f<food+water)return false;s.Food-=food;s.Water-=water;b.PortFoodStored+=food;b.WaterStored+=water;}
            World.Revision++;return true;
        }
        private static bool NonnegativeFinite(float v){return !float.IsNaN(v)&&!float.IsInfinity(v)&&v>=0;}
        private static bool PositiveFinite(float v){return NonnegativeFinite(v)&&v>.00001f;}
        public float ShipEnduranceSeconds(int shipId)
        {ShipData s=World.GetShip(shipId);int n=MaritimeCatalog.People(s);return s==null||n==0?0:Math.Min(s.Food,s.Water)*SubsistenceRules.DaySeconds/n;}
        public string ShipDepartureReason(int shipId,float plannedSeconds=60)
        {
            ShipData s=World.GetShip(shipId);if(s==null||s.Health<=0)return "Корабль недоступен";
            UnitData captain=World.GetUnit(s.CaptainUnitId);if(captain==null||captain.Health<=0||captain.ShipId!=s.Id)return "Нужен капитан на борту";
            if(LiveCrew(s)<MaritimeCatalog.Get(s.Kind).MinCrew)return "Недостаточно моряков";
            if(ShipEnduranceSeconds(shipId)+.001f<plannedSeconds+30)return "Недостаточно еды и воды для пути и резерва";
            return "Готов к отплытию";
        }
        private int LiveCrew(ShipData s){int n=0;foreach(int id in s.CrewIds){UnitData u=World.GetUnit(id);if(u!=null&&u.Health>0)n++;}return n;}
        private bool CanNavigateShip(ShipData s){UnitData c=World.GetUnit(s.CaptainUnitId);return c!=null&&c.Health>0&&c.ShipId==s.Id&&LiveCrew(s)>=MaritimeCatalog.Get(s.Kind).MinCrew;}
        private bool SeaRoute(ShipData s,int target,ShipPhase phase,bool provisionCheck=true,float extraSeconds=0)
        {
            var path=new List<int>();if(!FindSeaPath(World.Index((int)s.X,(int)s.Y),target,s.Kind,path)){s.LastReason="Нет проходимого водного пути";return false;}
            if(provisionCheck){string reason=ShipDepartureReason(s.Id,path.Count/MaritimeCatalog.Get(s.Kind).Speed+extraSeconds);if(reason!="Готов к отплытию"){s.LastReason=reason;return false;}}
            s.Path=path;s.PathCursor=0;s.RouteRevision=World.TerrainRevision;s.Phase=phase;s.DockedPortId=0;s.LastChangeTick=World.Tick;s.LastReason="";return true;
        }
        public bool TrySailToPort(int shipId,int portId)
        {
            ShipData s=World.GetShip(shipId);BuildingData p=World.GetBuilding(portId);
            if(s==null||!UsablePort(p)||s.Health<=0||s.OwnerSettlementId==0||s.Phase==ShipPhase.Abroad||s.Phase==ShipPhase.Sunk||s.Phase==ShipPhase.Departed||AtWar(s.OwnerSettlementId,p.SettlementId))return false;
            if(!SeaRoute(s,p.BerthCell,ShipPhase.Sailing))return false;s.DestinationPortId=p.Id;s.AutoFishing=false;return true;
        }
        public bool TryAppointCaptain(int shipId,int unitId)
        {ShipData s=World.GetShip(shipId);UnitData u=World.GetUnit(unitId);if(s==null||u==null||u.Health<=0||u.Age<16||s.CaptainUnitId!=0||u.ShipId!=s.Id||!s.CrewIds.Contains(u.Id)||s.Phase!=ShipPhase.Docked&&s.Phase!=ShipPhase.Stranded)return false;s.CaptainUnitId=u.Id;u.Profession=UnitProfession.Captain;return true;}
        public bool TryDisembark(int unitId)
        {
            UnitData u=World.GetUnit(unitId);ShipData s=u==null?null:World.GetShip(u.ShipId);BuildingData p=s==null?null:World.GetBuilding(s.DockedPortId);
            if(s==null||!UsablePort(p)||s.Phase!=ShipPhase.Docked||s.PrisonerIds.Contains(unitId))return false;
            int exit=FindBuildingExit(p,true);if(exit<0)return false;
            RemoveShipPerson(s,u);u.X=exit%World.Width+.5f;u.Y=exit/World.Width+.5f;u.Profession=u.Age>=16?ProfessionFor(u.Id):UnitProfession.None;u.MaritimeShoreLeaveUntilTick=World.Tick+600;ChangeTask(u,UnitTask.Idle);u.DecisionTimer=0;return true;
        }
        private void RemoveShipPerson(ShipData s,UnitData u)
        {s.CrewIds.Remove(u.Id);s.PassengerIds.Remove(u.Id);s.PrisonerIds.Remove(u.Id);if(s.CaptainUnitId==u.Id)s.CaptainUnitId=0;u.ShipId=0;u.MaritimeOrderId=0;}
        private bool UpdateMaritimeUnit(UnitData u,float dt)
        {
            if(u.ShipId==0)return false;ShipData s=World.GetShip(u.ShipId);if(s==null)return false;
            u.X=s.X;u.Y=s.Y;ClearRoute(u);u.InsideBuildingId=0;u.Task=UnitTask.Sail;
            if(World.Laws.Aging)u.Age+=dt*.1f;
            float need=dt/SubsistenceRules.DaySeconds;
            float food=Math.Min(need,s.Food),water=Math.Min(need,s.Water);s.Food-=food;s.Water-=water;World.MaritimeFoodConsumed+=food;World.MaritimeWaterConsumed+=water;
            u.Hunger=Math.Max(0,Math.Min(1,u.Hunger+(need-food)*SubsistenceRules.NeedReliefPerPortion));
            u.Thirst=Math.Max(0,Math.Min(1,u.Thirst+(need-water)*SubsistenceRules.NeedReliefPerPortion));
            if(u.Hunger>.55f&&s.Food>=1){s.Food--;World.MaritimeFoodConsumed++;u.Hunger=Math.Max(0,u.Hunger-SubsistenceRules.NeedReliefPerPortion);}
            if(u.Thirst>.55f&&s.Water>=1){s.Water--;World.MaritimeWaterConsumed++;u.Thirst=Math.Max(0,u.Thirst-SubsistenceRules.NeedReliefPerPortion);}
            u.Fatigue=Math.Max(0,u.Fatigue-dt*.0004f);u.SocialNeed=Math.Max(0,u.SocialNeed-dt*.001f);
            if(u.Infected)u.Health=Math.Max(0,u.Health-dt*1.1f);
            if(World.Laws.Aging&&u.Age>(u.Species==SpeciesType.Elf?180:85))u.Health=Math.Max(0,u.Health-dt*1.5f);
            if(u.Hunger>.9f||u.Thirst>.9f)u.Health=Math.Max(0,u.Health-dt*5);
            return true;
        }
        private void ReleaseMaritimeUnit(UnitData u)
        {
            int orderId=u.MaritimeOrderId;ShipData s=World.GetShip(u.ShipId);
            foreach(ItemType gear in new[]{u.NavalWeapon,u.NavalArmor})if(gear!=ItemType.None)
            {if(World.IsWalkable((int)u.X,(int)u.Y))World.GroundStocks.Add(new GroundStockData{X=u.X,Y=u.Y,Item=gear,Amount=1});else World.LostResources[(int)gear]++;}
            u.NavalWeapon=u.NavalArmor=ItemType.None;
            if(u.IsForeignMariner){World.ForeignCrewDead++;u.IsForeignMariner=false;}
            if(s!=null)RemoveShipPerson(s,u);
            ShipConstructionData c=World.GetShipOrder(orderId);if(c!=null&&c.WorkerId==u.Id)c.WorkerId=0;
            DockOrderData o=World.GetDockOrder(orderId);if(o!=null&&!o.Complete)o.Cancelled=true;u.MaritimeOrderId=0;
        }
        private void OnMaritimeBuildingRemoved(BuildingData b)
        {
            foreach(ShipConstructionData o in World.ShipOrders)if(o.ShipyardId==b.Id&&!o.Complete&&!o.Cancelled){foreach(ItemAmount m in MaritimeCatalog.Get(o.Kind).Inputs){World.LostResources[(int)m.Item]+=o.Materials[(int)m.Item];o.Materials[(int)m.Item]=0;}CancelShipOrder(o);}
            foreach(DockOrderData o in World.DockOrders)if(o.BuildingId==b.Id&&!o.Complete&&!o.Cancelled)CancelDockOrder(o);
            foreach(ShipData s in World.Ships){if(s.DockedPortId==b.Id){s.DockedPortId=0;s.Phase=ShipPhase.Stranded;s.LastReason="Порт разрушен";}if(s.DestinationPortId==b.Id)s.DestinationPortId=0;if(s.HomePortId==b.Id)s.HomePortId=0;}
            World.MaritimeFoodLost+=b.PortFoodStored;World.MaritimeWaterLost+=b.Type==BuildingType.HarborWarehouse?b.WaterStored:0;b.PortFoodStored=0;
        }
    }
}
