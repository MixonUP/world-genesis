using System;
using System.Collections.Generic;
namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        public bool TryInviteWanderer(int portId,ShipKind kind)
        {
            BuildingData port=World.GetBuilding(portId);if(!UsablePort(port)||!MaritimeCatalog.IsForeign(kind)||World.Ships.Count>=256||World.Units.Count+MaritimeCatalog.Get(kind).MinCrew>10000)return false;
            foreach(ShipData other in World.Ships)if(other.OwnerSettlementId==0&&other.DestinationPortId==portId&&other.Phase!=ShipPhase.Departed&&other.Phase!=ShipPhase.Sunk)return false;
            var path=new List<int>();int edge=FindOceanExit(port.BerthCell,kind,path);if(edge<0)return false;
            ShipData ship=CreateShip(kind,0,edge);ship.HomePortId=portId;ship.DestinationPortId=portId;ship.OceanExitCell=edge;
            ShipDefinition d=MaritimeCatalog.Get(kind);ship.Food=ship.Water=d.MinCrew*8;
            ship.Cargo[(int)ItemType.Plank]=d.Capacity*.12f;ship.Cargo[(int)ItemType.Salt]=d.Capacity*.04f;ship.Cargo[(int)ItemType.IronOre]=d.Capacity*.06f;ship.Cargo[(int)ItemType.CopperCoin]=100;ship.Cargo[(int)ItemType.SilverCoin]=40;ship.Cargo[(int)ItemType.GoldCoin]=10;
            ship.AgriculturalCargo[(int)AgriculturalProduct.Grain]=d.Capacity*.08f;ship.AgriculturalCargo[(int)AgriculturalProduct.RiverPearl]=2+(int)kind-(int)ShipKind.WandererSmall;
            for(int i=0;i<d.MinCrew;i++)
            {
                AddUnit(SpeciesType.Human,edge%World.Width,edge/World.Width,0,false);UnitData u=World.Units[World.Units.Count-1];u.IsForeignMariner=true;u.ShipId=ship.Id;u.Task=UnitTask.Sail;u.Profession=i==0?UnitProfession.Captain:UnitProfession.Sailor;u.Hunger=u.Thirst=u.Fatigue=0;u.NavalWeapon=ItemType.Sword;u.NavalArmor=ItemType.Armor;ship.CrewIds.Add(u.Id);if(i==0)ship.CaptainUnitId=u.Id;World.ForeignCrewArrived++;World.MaritimeImports[(int)ItemType.Sword]++;World.MaritimeImports[(int)ItemType.Armor]++;
            }
            AccountShipCrossing(ship,true);SeaRoute(ship,port.BerthCell,ShipPhase.Inbound,false);World.Revision++;return true;
        }
        private void AccountShipCrossing(ShipData s,bool entering)
        {
            float[] material=entering?World.MaritimeImports:World.MaritimeExports,agri=entering?World.MaritimeAgricultureImports:World.MaritimeAgricultureExports;
            for(int i=1;i<material.Length;i++)material[i]+=s.Cargo[i];for(int i=1;i<agri.Length;i++)agri[i]+=s.AgriculturalCargo[i];
            if(entering){World.MaritimeFoodImported+=s.Food;World.MaritimeWaterImported+=s.Water;}else{World.MaritimeFoodExported+=s.Food;World.MaritimeWaterExported+=s.Water;}
        }
        public bool TryStartForeignVoyage(int shipId)
        {
            ShipData s=World.GetShip(shipId);if(s==null||s.Kind!=ShipKind.Merchant||s.Phase!=ShipPhase.Docked||s.OwnerSettlementId==0||!World.HasTechnology(s.OwnerSettlementId,"n05_overseas_trade"))return false;
            var path=new List<int>();int edge=FindOceanExit(World.Index((int)s.X,(int)s.Y),s.Kind,path);if(edge<0){s.LastReason="Нет выхода к граничному морю";return false;}
            if(!SeaRoute(s,edge,ShipPhase.Outbound,true,path.Count/MaritimeCatalog.Get(s.Kind).Speed+60))return false;s.OceanExitCell=edge;s.DestinationPortId=s.HomePortId;
            if(s.CompletedVoyages==0&&s.ForeignOffer[(int)ItemType.CopperCoin]==0&&s.ForeignOffer[(int)ItemType.Salt]==0)
            {s.ForeignOffer[(int)ItemType.CopperCoin]=200;s.ForeignOffer[(int)ItemType.Salt]=24;s.ForeignOffer[(int)ItemType.IronOre]=40;s.ForeignAgricultureOffer[(int)AgriculturalProduct.RiverPearl]=2;}
            return true;
        }
        private void ForeignMarketVisit(ShipData s)
        {
            // This finite remote merchant account persists across voyages. Coins move, never change denomination.
            foreach(ItemType item in new[]{ItemType.Plank,ItemType.Furniture,ItemType.LinenCloth})
            {float sold=(float)Math.Floor(Math.Min(MaritimeCatalog.FreeHold(s),Math.Min(s.Cargo[(int)item],Math.Min(10,s.ForeignOffer[(int)ItemType.CopperCoin]/2))));if(sold<=0)continue;s.Cargo[(int)item]-=sold;s.ForeignOffer[(int)item]+=sold;s.ForeignOffer[(int)ItemType.CopperCoin]-=sold*2;s.Cargo[(int)ItemType.CopperCoin]+=sold*2;}
            foreach(ItemType item in new[]{ItemType.Salt,ItemType.IronOre})
            {float bought=(float)Math.Floor(Math.Min(s.ForeignOffer[(int)item],Math.Min(8,s.Cargo[(int)ItemType.CopperCoin]/3)));if(bought<=0)continue;s.Cargo[(int)ItemType.CopperCoin]-=bought*3;s.ForeignOffer[(int)ItemType.CopperCoin]+=bought*3;s.ForeignOffer[(int)item]-=bought;s.Cargo[(int)item]+=bought;}
        }
        public bool TryStartShipFishing(int shipId,int waterCell)
        {
            ShipData s=World.GetShip(shipId);ShipDefinition d=s==null?null:MaritimeCatalog.Get(s.Kind);BuildingData home=s==null?null:World.GetBuilding(s.HomePortId);
            if(d==null||d.FishingGrade<=0||!UsablePort(home)||!MaritimeCatalog.IsWater(World,waterCell,s.Kind)||World.Infrastructure[waterCell].Fish<1||s.Phase!=ShipPhase.Docked||MaritimeCatalog.FreeHold(s)<2)return false;
            var back=new List<int>();if(!FindSeaPath(waterCell,home.BerthCell,s.Kind,back))return false;
            if(!SeaRoute(s,waterCell,ShipPhase.Sailing,true,back.Count/d.Speed+20))return false;s.FishingCell=waterCell;s.DestinationPortId=0;s.AutoFishing=true;s.ActionTimer=0;return true;
        }
        private void FishFromShip(ShipData s,float dt)
        {
            int cell=World.Index((int)s.X,(int)s.Y);ShipDefinition d=MaritimeCatalog.Get(s.Kind);
            if(s.CaptainUnitId==0||s.CrewIds.Count<d.MinCrew||ShipEnduranceSeconds(s.Id)<60||MaritimeCatalog.FreeHold(s)<2||World.Infrastructure[cell].Fish<1){ReturnShipHome(s);return;}
            s.ActionTimer+=dt;if(s.ActionTimer<8)return;s.ActionTimer=0;
            float total=0;foreach(FishDefinition f in FishingCatalog.All)if(FishingCatalog.Eligible(World,cell,d.FishingGrade,f.Id))total+=f.Weight;
            if(total<=0){ReturnShipHome(s);return;}float roll=RandomValue()*total;FishDefinition caught=null;
            foreach(FishDefinition f in FishingCatalog.All)if(FishingCatalog.Eligible(World,cell,d.FishingGrade,f.Id)){roll-=f.Weight;if(roll<=0){caught=f;break;}}
            if(caught==null)return;float weight=1+caught.PearlYield+caught.ScaleYield;if(MaritimeCatalog.FreeHold(s)+.00001f<weight){ReturnShipHome(s);return;}
            World.Infrastructure[cell].Fish=Math.Max(0,World.Infrastructure[cell].Fish-1);s.AgriculturalCargo[(int)caught.Food]++;s.AgriculturalCargo[(int)AgriculturalProduct.RiverPearl]+=caught.PearlYield;s.AgriculturalCargo[(int)AgriculturalProduct.AncientScale]+=caught.ScaleYield;s.FishCaught++;
            if(s.FishCaught%5==0)ReturnShipHome(s);
        }
        private void ReturnShipHome(ShipData s)
        {
            BuildingData p=World.GetBuilding(s.HomePortId);if(!UsablePort(p))p=NearestHomePort(s.OwnerSettlementId,World.Index((int)s.X,(int)s.Y));
            if(p!=null&&SeaRoute(s,p.BerthCell,ShipPhase.Sailing,false)){s.DestinationPortId=p.Id;s.FishingCell=-1;return;}
            s.Phase=ShipPhase.Stranded;s.Path.Clear();s.PathCursor=0;s.LastReason="Нет пути к безопасному порту";
        }
        private void UpdateMaritime(float dt)
        {
            if(World.Tick%100==0)CleanupMaritimeHistory();
            // All ships move before the people tick; every aboard resident is then at the same canonical position.
            foreach(ShipConstructionData o in World.ShipOrders)if(o.WorkerId!=0){UnitData u=World.GetUnit(o.WorkerId);if(u==null||u.Health<=0||u.Task!=UnitTask.Shipbuild||u.MaritimeOrderId!=o.Id)o.WorkerId=0;}
            foreach(DockOrderData o in World.DockOrders)if(!o.Complete&&!o.Cancelled){UnitData u=World.GetUnit(o.WorkerId);if(u==null||u.Health<=0||u.Task!=UnitTask.Dockwork||u.MaritimeOrderId!=o.Id)CancelDockOrder(o);}
            foreach(ShipData s in World.Ships)
            {
                if(s.Phase==ShipPhase.Sunk||s.Phase==ShipPhase.Departed)continue;
                if(s.Health<=0){SinkShip(s);continue;}
                if(s.Phase==ShipPhase.Abroad)
                {
                    s.AbroadTimer+=dt;
                    if(s.AbroadTimer>=60){if(CanNavigateShip(s))ForeignMarketVisit(s);s.CompletedVoyages++;AccountShipCrossing(s,true);BuildingData p=World.GetBuilding(s.HomePortId);s.AbroadTimer=0;if(CanNavigateShip(s)&&UsablePort(p)&&SeaRoute(s,p.BerthCell,ShipPhase.Inbound,false))s.DestinationPortId=p.Id;else{s.Phase=ShipPhase.Stranded;s.LastReason="Домашний порт потерян";}}
                    continue;
                }
                int cell=World.Index((int)s.X,(int)s.Y);
                if(!MaritimeCatalog.IsWater(World,cell,s.Kind)){s.Health=Math.Max(0,s.Health-dt*5);s.Phase=ShipPhase.Stranded;s.Path.Clear();s.PathCursor=0;s.DockedPortId=0;s.LastReason="Корабль на мели";continue;}
                s.Health=Math.Max(0,s.Health-World.Cells[cell].Fire*dt*8);
                if(s.Phase==ShipPhase.Docked)
                {
                    BuildingData p=World.GetBuilding(s.DockedPortId);if(!UsablePort(p)){s.DockedPortId=0;s.Phase=ShipPhase.Stranded;continue;}
                    s.ActionTimer+=dt;
                    if(s.OwnerSettlementId==0&&s.ActionTimer>=90){var path=new List<int>();int edge=FindOceanExit(cell,s.Kind,path);if(edge>=0&&SeaRoute(s,edge,ShipPhase.Outbound)){s.OceanExitCell=edge;s.ActionTimer=0;}}
                    else if(s.OwnerSettlementId!=0&&World.Tick%10==s.Id%10)AutoManageDockedShip(s);
                    continue;
                }
                if(s.Phase==ShipPhase.Attacking){UpdateNavalAttack(s,dt);continue;}
                if(s.Phase==ShipPhase.Fishing){FishFromShip(s,dt);continue;}
                if(s.Phase==ShipPhase.Stranded)
                {if(s.OwnerSettlementId!=0&&s.CaptainUnitId!=0&&s.CrewIds.Count>=MaritimeCatalog.Get(s.Kind).MinCrew&&ShipEnduranceSeconds(s.Id)>60&&World.Tick%30==s.Id%30)ReturnShipHome(s);continue;}
                if(!CanNavigateShip(s)){s.LastReason="Недостаточный экипаж: ход остановлен";continue;}
                if(s.PathCursor<s.Path.Count)
                {
                    int next=s.Path[s.PathCursor];if(!MaritimeCatalog.IsWater(World,next,s.Kind))
                    {int end=s.Path[s.Path.Count-1];ShipPhase phase=s.Phase;if(!SeaRoute(s,end,phase,false)){s.Phase=ShipPhase.Stranded;s.Path.Clear();s.PathCursor=0;}continue;}
                    float tx=next%World.Width+.5f,ty=next/World.Width+.5f,dx=tx-s.X,dy=ty-s.Y,len=(float)Math.Sqrt(dx*dx+dy*dy);float distance=dt*MaritimeCatalog.Get(s.Kind).Speed;
                    if(len>.00001f){s.HeadingX=dx/len;s.HeadingY=dy/len;}if(distance>=len){s.X=tx;s.Y=ty;s.PathCursor++;}else{s.X+=dx/len*distance;s.Y+=dy/len*distance;}
                    if(s.OwnerSettlementId!=0&&ShipEnduranceSeconds(s.Id)<20&&s.Phase!=ShipPhase.Inbound&&s.Phase!=ShipPhase.Sailing){s.LastReason="Возвращение: заканчивается провизия";ReturnShipHome(s);}
                }
                else ArriveShip(s);
            }
            if(World.Tick>=World.NextWandererTick)
            {World.NextWandererTick=World.Tick+1200;foreach(BuildingData b in World.Buildings)if(UsablePort(b)&&GetHarborWarehouses(b.Id).Count>0){if(TryInviteWanderer(b.Id,(ShipKind)((int)ShipKind.WandererSmall+(World.Tick/1200)%3)))break;}}
        }
        private void ArriveShip(ShipData s)
        {
            s.Path.Clear();s.PathCursor=0;
            if(s.Phase==ShipPhase.Outbound)
            {
                if(World.Index((int)s.X,(int)s.Y)!=s.OceanExitCell){s.Phase=ShipPhase.Stranded;return;}
                AccountShipCrossing(s,false);
                if(s.OwnerSettlementId==0){DepartWanderer(s);return;}
                s.Phase=ShipPhase.Abroad;s.AbroadTimer=0;return;
            }
            if(s.FishingCell>=0&&s.DestinationPortId==0){s.Phase=ShipPhase.Fishing;return;}
            BuildingData p=World.GetBuilding(s.DestinationPortId);if(!UsablePort(p)||World.Index((int)s.X,(int)s.Y)!=p.BerthCell){s.Phase=ShipPhase.Stranded;s.LastReason="Порт назначения недоступен";return;}
            s.Phase=ShipPhase.Docked;s.DockedPortId=p.Id;s.ActionTimer=0;s.LastReason="У причала";
        }
        private void DepartWanderer(ShipData s)
        {
            var ids=new List<int>();ids.AddRange(s.CrewIds);ids.AddRange(s.PassengerIds);ids.AddRange(s.PrisonerIds);
            foreach(int id in ids){UnitData u=World.GetUnit(id);if(u==null)continue;if(u.NavalWeapon!=ItemType.None)World.MaritimeExports[(int)u.NavalWeapon]++;if(u.NavalArmor!=ItemType.None)World.MaritimeExports[(int)u.NavalArmor]++;if(u.IsForeignMariner){World.ForeignCrewDeparted++;World.Units.Remove(u);}else{u.Health=0;ReleaseMaritimeUnit(u);}}
            s.CrewIds.Clear();s.PassengerIds.Clear();s.PrisonerIds.Clear();s.CaptainUnitId=0;Array.Clear(s.Cargo,0,s.Cargo.Length);Array.Clear(s.AgriculturalCargo,0,s.AgriculturalCargo.Length);s.Food=s.Water=0;s.Phase=ShipPhase.Departed;s.DockedPortId=0;
        }
        private void AutoManageDockedShip(ShipData s)
        {
            if(s.CaptainUnitId==0&&s.CrewIds.Count>0)TryAppointCaptain(s.Id,s.CrewIds[0]);
            foreach(BuildingData b in GetHarborWarehouses(s.DockedPortId))
            {
                float target=Math.Max(4,MaritimeCatalog.People(s)*2),food=Math.Min(Math.Max(0,target-s.Food),b.PortFoodStored),water=Math.Min(Math.Max(0,target-s.Water),b.WaterStored);
                if(food+water<=MaritimeCatalog.FreeHold(s)&&food+water>0)TryTransferShipProvisions(s.Id,b.Id,food,water,true);
                if(MaritimeCatalog.Get(s.Kind).FishingGrade>0)for(int i=1;i<s.AgriculturalCargo.Length;i++){float n=Math.Min(s.AgriculturalCargo[i],StorageCapacity.FreeAgriculture(b,(AgriculturalProduct)i));if(n>.00001f)TryTransferShipAgriculture(s.Id,b.Id,(AgriculturalProduct)i,n,false);}
            }
            if(s.Kind==ShipKind.Merchant&&s.ActionTimer>=30&&HasHarborAuthority(s.DockedPortId)&&CanNavigateShip(s)&&ShipEnduranceSeconds(s.Id)>180)
            {
                if(s.Cargo[(int)ItemType.CopperCoin]<30){float coins=WholeMaritimeUnits(Math.Min(30-s.Cargo[(int)ItemType.CopperCoin],HarborStock(s.DockedPortId,ItemType.CopperCoin)));if(coins>=1)TryTransferHarborCargo(s.Id,ItemType.CopperCoin,coins,true);}
                if(s.Cargo[(int)ItemType.Plank]<10){float reserved=0;foreach(MarketNeedData need in World.MarketNeeds)if(need.SettlementId==s.OwnerSettlementId&&need.Item==ItemType.Plank&&need.State!=MarketNeedState.Satisfied)reserved=Math.Max(reserved,need.Target);float surplus=Math.Max(0,World.GetSettlementStock(s.OwnerSettlementId,ItemType.Plank)-reserved),planks=Math.Min(10-s.Cargo[(int)ItemType.Plank],Math.Min(surplus,HarborStock(s.DockedPortId,ItemType.Plank)));if(planks>=1)TryTransferHarborCargo(s.Id,ItemType.Plank,planks,true);}
                if(s.Cargo[(int)ItemType.CopperCoin]>=3||s.Cargo[(int)ItemType.Plank]>=1){if(TryStartForeignVoyage(s.Id))return;}
            }
            if(MaritimeCatalog.Get(s.Kind).FishingGrade>0&&s.CaptainUnitId!=0&&s.CrewIds.Count>=MaritimeCatalog.Get(s.Kind).MinCrew&&ShipEnduranceSeconds(s.Id)>90)
            {
                int cx=(int)s.X,cy=(int)s.Y;
                for(int radius=2;radius<=12;radius++)for(int y=Math.Max(0,cy-radius);y<=Math.Min(World.Height-1,cy+radius);y++)for(int x=Math.Max(0,cx-radius);x<=Math.Min(World.Width-1,cx+radius);x++)
                {int cell=World.Index(x,y);if(World.Infrastructure[cell].Fish>=1&&TryStartShipFishing(s.Id,cell))return;}
            }
        }
        public bool TryAttackShip(int attackerId,int targetId,bool attemptCapture)
        {
            ShipData a=World.GetShip(attackerId),t=World.GetShip(targetId);
            if(!World.Laws.Warfare||a==null||t==null||a.Phase!=ShipPhase.Docked&&a.Phase!=ShipPhase.Sailing&&a.Phase!=ShipPhase.Stranded&&a.Phase!=ShipPhase.Attacking||a.Id==t.Id||a.Kind!=ShipKind.Warship||a.OwnerSettlementId==0||a.OwnerSettlementId==t.OwnerSettlementId||a.Health<=0||t.Health<=0||t.Phase==ShipPhase.Abroad||t.Phase==ShipPhase.Departed||t.Phase==ShipPhase.Sunk||!World.HasTechnology(a.OwnerSettlementId,"n06_naval_warfare")||ArmedCrew(a)==0||a.CaptainUnitId==0||a.CrewIds.Count<MaritimeCatalog.Get(a.Kind).MinCrew)return false;
            if(t.OwnerSettlementId!=0&&!AtWar(a.OwnerSettlementId,t.OwnerSettlementId))return false;
            var path=new List<int>();if(!FindSeaPath(World.Index((int)a.X,(int)a.Y),World.Index((int)t.X,(int)t.Y),a.Kind,path))return false;
            if(ShipDepartureReason(a.Id,path.Count/MaritimeCatalog.Get(a.Kind).Speed+30)!="Готов к отплытию")return false;
            a.TargetShipId=t.Id;a.AttemptCapture=attemptCapture;a.Aggressor=t.OwnerSettlementId==0;a.DockedPortId=0;a.Phase=ShipPhase.Attacking;a.Path=path;a.PathCursor=0;a.ActionTimer=0;t.LastReason=a.Aggressor?"Атака нейтрального судна":"Вражеский корабль";return true;
        }
        private int ArmedCrew(ShipData s){int n=0;foreach(int id in s.CrewIds){UnitData u=World.GetUnit(id);if(u!=null&&u.Health>0&&u.NavalWeapon!=ItemType.None&&u.NavalArmor==ItemType.Armor)n++;}return n;}
        private void UpdateNavalAttack(ShipData a,float dt)
        {
            ShipData t=World.GetShip(a.TargetShipId);if(!World.Laws.Warfare||t==null||t.Health<=0||t.Phase==ShipPhase.Sunk||t.Phase==ShipPhase.Departed||t.Phase==ShipPhase.Abroad||!CanNavigateShip(a)||ArmedCrew(a)==0||t.OwnerSettlementId==a.OwnerSettlementId||t.OwnerSettlementId!=0&&!AtWar(a.OwnerSettlementId,t.OwnerSettlementId)){a.TargetShipId=0;ReturnShipHome(a);return;}
            if(ShipEnduranceSeconds(a.Id)<60){a.LastReason="Бой прерван: заканчивается провизия";a.TargetShipId=0;ReturnShipHome(a);return;}
            float range=DistanceSquared(a.X,a.Y,t.X,t.Y);
            if(range>2.25f)
            {
                if(a.PathCursor>=a.Path.Count||World.Tick%10==a.Id%10){FindSeaPath(World.Index((int)a.X,(int)a.Y),World.Index((int)t.X,(int)t.Y),a.Kind,a.Path);a.PathCursor=0;}
                if(a.PathCursor<a.Path.Count){int c=a.Path[a.PathCursor];if(!MaritimeCatalog.IsWater(World,c,a.Kind)){a.Path.Clear();a.PathCursor=0;return;}float tx=c%World.Width+.5f,ty=c/World.Width+.5f,dx=tx-a.X,dy=ty-a.Y,len=(float)Math.Sqrt(dx*dx+dy*dy),step=dt*MaritimeCatalog.Get(a.Kind).Speed;if(len>.00001f){a.HeadingX=dx/len;a.HeadingY=dy/len;}if(step>=len){a.X=tx;a.Y=ty;a.PathCursor++;}else{a.X+=dx/len*step;a.Y+=dy/len*step;}}
                return;
            }
            a.ActionTimer+=dt;if(a.ActionTimer<2)return;a.ActionTimer=0;
            if(a.AttemptCapture&&t.Health<=t.MaxHealth*.4f&&TryCaptureVessel(a,t))return;
            if(a.Cargo[(int)ItemType.BallistaBolts]<1){a.LastReason="Нет баллистных болтов";return;}
            a.Cargo[(int)ItemType.BallistaBolts]--;World.LostResources[(int)ItemType.BallistaBolts]++;a.LastAttackTick=World.Tick;
            t.Health=Math.Max(0,t.Health-(12+ArmedCrew(a)*5));a.Health=Math.Max(0,a.Health-ArmedCrew(t)*2);
            if(t.CrewIds.Count>0){UnitData struck=World.GetUnit(t.CrewIds[(int)(World.Tick%t.CrewIds.Count)]);if(struck!=null)struck.Health=Math.Max(0,struck.Health-8);}
            if(a.Health<=0)SinkShip(a);if(t.Health<=0)SinkShip(t);
        }
        private bool TryCaptureVessel(ShipData a,ShipData t)
        {
            int needed=MaritimeCatalog.Get(t.Kind).MinCrew;if(a.CrewIds.Count-needed<MaritimeCatalog.Get(a.Kind).MinCrew)return false;
            var prize=new List<int>();foreach(int id in a.CrewIds)if(id!=a.CaptainUnitId&&prize.Count<needed)prize.Add(id);if(prize.Count<needed)return false;
            foreach(int id in t.CrewIds)t.PrisonerIds.Add(id);t.CrewIds.Clear();t.CaptainUnitId=0;
            foreach(int id in prize){a.CrewIds.Remove(id);t.CrewIds.Add(id);UnitData u=World.GetUnit(id);u.ShipId=t.Id;u.X=t.X;u.Y=t.Y;if(t.CaptainUnitId==0){t.CaptainUnitId=id;u.Profession=UnitProfession.Captain;}}
            t.OwnerSettlementId=a.OwnerSettlementId;t.HomePortId=a.HomePortId;t.DockedPortId=0;t.TargetShipId=0;t.FishingCell=-1;t.LastReason="Захвачен; призовая команда и пленные на борту";World.ShipsCaptured++;
            a.TargetShipId=0;ReturnShipHome(t);ReturnShipHome(a);return true;
        }
        private void SinkShip(ShipData s)
        {
            if(s.Phase==ShipPhase.Sunk||s.Phase==ShipPhase.Departed)return;
            for(int i=1;i<s.Cargo.Length;i++){World.LostResources[i]+=s.Cargo[i];s.Cargo[i]=0;}for(int i=1;i<s.AgriculturalCargo.Length;i++){World.LostAgriculture[i]+=s.AgriculturalCargo[i];s.AgriculturalCargo[i]=0;}World.MaritimeFoodLost+=s.Food;World.MaritimeWaterLost+=s.Water;s.Food=s.Water=0;
            var people=new List<int>();people.AddRange(s.CrewIds);people.AddRange(s.PassengerIds);people.AddRange(s.PrisonerIds);
            foreach(int id in people){UnitData u=World.GetUnit(id);if(u==null)continue;int exit=-1,x=(int)s.X,y=(int)s.Y;for(int d=0;d<4;d++)if(World.IsWalkable(x+Dx[d],y+Dy[d])){exit=World.Index(x+Dx[d],y+Dy[d]);break;}if(exit>=0){RemoveShipPerson(s,u);u.X=exit%World.Width+.5f;u.Y=exit/World.Width+.5f;u.Health=Math.Max(0,u.Health-30);ChangeTask(u,UnitTask.Flee);u.DecisionTimer=0;}else{u.Health=0;ReleaseMaritimeUnit(u);}}
            s.CrewIds.Clear();s.PassengerIds.Clear();s.PrisonerIds.Clear();s.CaptainUnitId=0;s.DockedPortId=0;s.Health=0;s.Phase=ShipPhase.Sunk;s.Path.Clear();s.PathCursor=0;World.ShipsSunk++;
        }
        private void ApplyMaritimePower(PowerType power,int x,int y,int radius)
        {foreach(ShipData s in World.Ships){if(s.Phase==ShipPhase.Sunk||s.Phase==ShipPhase.Departed||s.Phase==ShipPhase.Abroad||DistanceSquared(s.X,s.Y,x+.5f,y+.5f)>radius*radius)continue;if(power==PowerType.Lightning||power==PowerType.Meteor)s.Health=Math.Max(0,s.Health-(power==PowerType.Meteor?180:85));else if(power==PowerType.Fire)s.Health=Math.Max(0,s.Health-20);else if(power==PowerType.Heal)s.Health=s.MaxHealth;if(s.Health<=0)SinkShip(s);}}
    }
}
