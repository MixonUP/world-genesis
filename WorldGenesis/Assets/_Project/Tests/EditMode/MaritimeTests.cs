using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using WorldGenesis.Simulation;
#if UNITY_5_3_OR_NEWER
using NUnit.Framework;
using UnityEngine;
#endif
namespace WorldGenesis.Tests
{
    public static class MaritimeScenarios
    {
        public static Func<WorldState,WorldState> Clone;
        public static Func<WorldState,string> Snapshot;
        private static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        private static void Near(float expected,float actual,string message,float epsilon=.002f){Need(Math.Abs(expected-actual)<epsilon,message+": "+actual+" != "+expected);}
        private static object Call(SimulationEngine e,string name,params object[] args){return typeof(SimulationEngine).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(e,args);}
        private static SimulationEngine Fixture(int people=12)
        {
            var w=new WorldState{Seed=8192,RngState=8192,Width=32,Height=32,Cells=new TileData[1024],NaturalWildlife=false};
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)w.Cells[w.Index(x,y)]=new TileData{Terrain=x<6?TerrainType.DeepWater:TerrainType.Grass,Height=x<6?.15f:.55f,RouteHeight=x<6?.15f:.55f,Moisture=.5f,Temperature=.5f,DrainageParent=-1,WaterKind=x<6?WaterKind.Ocean:WaterKind.None,WaterDepth=x<6?2:0};
            var e=new SimulationEngine(w);e.Spawn(SpeciesType.Human,12,12,people);w.Laws.Aging=w.Laws.Births=w.Laws.Warfare=false;w.Weather.Automatic=false;w.NextWandererTick=long.MaxValue/2;
            foreach(UnitData u in w.Units){u.Age=25;u.X=12.5f;u.Y=12.5f;u.Hunger=u.Thirst=u.Fatigue=u.SocialNeed=0;u.Task=UnitTask.Idle;u.DecisionTimer=100000;u.Path.Clear();u.PathCursor=0;}
            SettlementData s=w.Settlements[0];s.NextPolicyReviewTick=s.NextPlanningTick=1000000;s.Food=200;s.Wood=100;
            foreach(TechnologyDefinition t in TechnologyCatalog.All)if(!s.UnlockedTechnologies.Contains(t.Id))s.UnlockedTechnologies.Add(t.Id);
            s.ActiveTechnology="";Add(e,BuildingType.Port,6,12);Add(e,BuildingType.Shipyard,6,14);Add(e,BuildingType.HarborWarehouse,8,12);return e;
        }
        private static BuildingData Add(SimulationEngine e,BuildingType type,int x,int y){return(BuildingData)Call(e,"AddBuilding",e.World.Settlements[0],x,y,type);}
        private static BuildingData Building(SimulationEngine e,BuildingType type){return e.World.Buildings.Find(b=>b.Type==type);}
        private static void Valid(SimulationEngine e){Need(e.World.Validate(out string error),error);}
        private static void Step(SimulationEngine e,int count=1){for(int i=0;i<count;i++){e.World.Settlements[0].BuildTimer=0;e.Step(.1f);if(i%10==0)Valid(e);}}
        private static ShipData PreparedShip(SimulationEngine e,ShipKind kind,bool crew=true,int crewCount=0)
        {
            BuildingData port=Building(e,BuildingType.Port);ShipDefinition d=MaritimeCatalog.Get(kind);var s=(ShipData)Call(e,"CreateShip",kind,e.World.Settlements[0].Id,port.BerthCell);s.HomePortId=s.DockedPortId=s.DestinationPortId=port.Id;s.Phase=ShipPhase.Docked;
            if(crew){int wanted=crewCount>0?crewCount:d.MinCrew;var units=e.World.Units.Where(u=>u.ShipId==0).Take(wanted).ToList();foreach(UnitData u in units){u.ShipId=s.Id;u.X=s.X;u.Y=s.Y;u.InsideBuildingId=u.EnteringBuildingId=0;u.Path.Clear();u.PathCursor=0;u.Task=UnitTask.Sail;u.Profession=s.CaptainUnitId==0?UnitProfession.Captain:UnitProfession.Sailor;if(s.CaptainUnitId==0)s.CaptainUnitId=u.Id;s.CrewIds.Add(u.Id);}s.Food=s.Water=wanted*4;}
            return s;
        }
        private static void Until(SimulationEngine e,Func<bool> condition,int maxTicks,string why){for(int i=0;i<maxTicks&&!condition();i++)Step(e);Need(condition(),why);}
        public static void ClosedLakeRejectsForeignArrivalAndConnectedSeaAdmitsRealCrew()
        {
            var e=Fixture();var p=Building(e,BuildingType.Port);for(int y=0;y<32;y++)for(int x=0;x<6;x++)e.World.Cells[e.World.Index(x,y)].WaterKind=WaterKind.Lake;
            uint rng=e.World.RngState;Need(!e.TryInviteWanderer(p.Id,ShipKind.WandererSmall),"closed lake accepted external visitor");Need(e.World.Ships.Count==0&&e.World.RngState==rng,"failed route spawned cargo/crew/RNG");
            for(int y=0;y<32;y++)for(int x=0;x<6;x++)e.World.Cells[e.World.Index(x,y)].WaterKind=WaterKind.Ocean;
            Need(e.TryInviteWanderer(p.Id,ShipKind.WandererSmall),"sea invitation");ShipData s=e.World.Ships[0];Need(s.X<1||s.Y<1||s.Y>31,"visitor did not start at boundary");Need(s.CrewIds.Count==3&&s.CaptainUnitId!=0,"missing actual sailors/captain");Need(s.CrewIds.All(id=>e.World.GetUnit(id).IsForeignMariner),"fake foreign crew");Near(5000,MaritimeCatalog.Get(s.Kind).Capacity,"small hold");Need(MaritimeCatalog.UsedHold(s)<5000,"foreign hold overflow");Near(s.Cargo[(int)ItemType.Salt],e.World.MaritimeImports[(int)ItemType.Salt],"external imports ledger");
            Until(e,()=>s.Phase==ShipPhase.Docked,600,"visitor never reached port");Need(s.DockedPortId==p.Id&&s.Food<24,"travel/provisions not real");Valid(e);
        }
        public static void ThreeWandererClassesHaveFiniteTotalHoldsAndExitAccounting()
        {
            foreach(ShipKind kind in new[]{ShipKind.WandererSmall,ShipKind.WandererMedium,ShipKind.WandererLarge})
            {var e=Fixture();Need(e.TryInviteWanderer(Building(e,BuildingType.Port).Id,kind),"invite class");ShipData s=e.World.Ships[0];Near(kind==ShipKind.WandererSmall?5000:kind==ShipKind.WandererMedium?10000:20000,MaritimeCatalog.Get(kind).Capacity,"class capacity");Until(e,()=>s.Phase==ShipPhase.Departed,2200,"visitor never departed");Near(0,MaritimeCatalog.UsedHold(s),"departed cargo remained");Need(s.CrewIds.Count==0&&e.World.ForeignCrewDeparted==MaritimeCatalog.Get(kind).MinCrew,"departed crew duplicated");Near(e.World.MaritimeImports[(int)ItemType.Salt],e.World.MaritimeExports[(int)ItemType.Salt],"unsold salt disappeared");Valid(e);}
        }
        public static void ConnectedWarehouseCapacityTransfersAndDisconnectionConserveStock()
        {
            var e=Fixture();BuildingData p=Building(e,BuildingType.Port),a=Building(e,BuildingType.HarborWarehouse),b=Add(e,BuildingType.HarborWarehouse,11,12),c=Add(e,BuildingType.HarborWarehouse,14,12);ShipData s=PreparedShip(e,ShipKind.Transport);
            Near(15000,e.HarborCapacity(p.Id),"three linked sections");a.Inventory[(int)ItemType.IronOre]=20;Need(e.TryTransferShipCargo(s.Id,a.Id,ItemType.IronOre,12,true),"physical dock load");Near(8,a.Inventory[(int)ItemType.IronOre],"shore debit");Near(12,s.Cargo[(int)ItemType.IronOre],"hold credit");Need(!e.TryTransferShipCargo(s.Id,a.Id,ItemType.IronOre,9,true),"oversold warehouse");Need(!e.TryTransferShipCargo(s.Id,a.Id,ItemType.IronOre,float.NaN,true),"NaN transfer");
            Need(e.TryTransferShipCargo(s.Id,c.Id,ItemType.IronOre,5,false),"linked distant section unload");b.Health=0;Call(e,"DestroyInvalidBuildings");Near(5000,e.HarborCapacity(p.Id),"disconnected capacity still linked");Near(5,c.Inventory[(int)ItemType.IronOre],"disconnected goods deleted");Need(!e.TryTransferShipCargo(s.Id,c.Id,ItemType.IronOre,1,true),"remote disconnected withdrawal");Valid(e);
        }
        public static void FoodWaterSupplyWalksFromCampfireAndRespectsOneHand()
        {
            var e=Fixture();BuildingData b=Building(e,BuildingType.HarborWarehouse),hall=Building(e,BuildingType.Hall);UnitData u=e.World.Units[0];hall.WaterStored=8;float food=e.World.Settlements[0].Food;
            Need(e.TrySupplyHarbor(u.Id,b.Id,ItemType.None,AgriculturalProduct.None,MaritimeProvision.Food,2),"start food supply");Near(0,b.PortFoodStored,"remote food credit");bool cargo=false;for(int i=0;i<500&&b.PortFoodStored<1.999f;i++){Step(e);if(u.CarriedFood>0)cargo=true;Need(SimulationEngine.CarriedLoad(u)<=1.00001f,"supply exceeded bare hand");}Need(cargo,"no physical provisions cargo");Near(2,b.PortFoodStored,"food not delivered");Near(food-2,e.World.Settlements[0].Food,"town food cost");
            Need(e.TrySupplyHarbor(u.Id,b.Id,ItemType.None,AgriculturalProduct.None,MaritimeProvision.Water,2),"start water supply");Until(e,()=>b.WaterStored>=1.999f,500,"water not delivered");Need(hall.WaterStored<8,"water not locally debited");Valid(e);
        }
        public static void ShipbuildingUsesLocalDeliveredInputsTimeAndResearch()
        {
            var e=Fixture();BuildingData yard=Building(e,BuildingType.Shipyard),hall=Building(e,BuildingType.Hall);UnitData u=e.World.Units[0];SettlementData town=e.World.Settlements[0];town.Wood=8;hall.Inventory[(int)ItemType.Rope]=2;town.UnlockedTechnologies.Remove("n01_boats");Need(!e.TryStartShipConstruction(u.Id,yard.Id,ShipKind.FishingBoat),"free research gate");town.UnlockedTechnologies.Add("n01_boats");
            Need(e.TryStartShipConstruction(u.Id,yard.Id,ShipKind.FishingBoat),"request paid boat");Need(e.World.Ships.Count==0,"instant hull");bool carried=false;for(int i=0;i<3500&&e.World.ShipsBuilt==0;i++){Step(e);if(u.CargoAmount>0)carried=true;Need(SimulationEngine.CarriedLoad(u)<=1.0001f,"construction hand overflow");}Need(carried&&e.World.ShipsBuilt==1,"no physical shipbuilding");Near(0,town.Wood,"planks unpaid");Near(0,hall.Inventory[(int)ItemType.Rope],"rope unpaid");Near(18,e.World.ShipOrders[0].Progress,"building time");Need(e.World.Ships[0].CaptainUnitId==0,"construction generated free crew");Valid(e);
        }
        public static void ConstructionCancelReturnsDeliveredEscrowWithoutHull()
        {
            var e=Fixture();UnitData u=e.World.Units[0];BuildingData yard=Building(e,BuildingType.Shipyard),hall=Building(e,BuildingType.Hall);e.World.Settlements[0].Wood=8;hall.Inventory[(int)ItemType.Rope]=2;Need(e.TryStartShipConstruction(u.Id,yard.Id,ShipKind.FishingBoat),"order");ShipConstructionData order=e.World.ShipOrders[0];Until(e,()=>order.Materials[(int)ItemType.Plank]>=1,500,"no delivered escrow");float delivered=order.Materials[(int)ItemType.Plank];Need(e.TryCancelShipConstruction(order.Id),"cancel");Near(delivered,yard.Inventory[(int)ItemType.Plank],"cancel lost delivered input");Need(e.World.Ships.Count==0&&order.Cancelled,"cancel manufactured hull");Near(8,e.World.Settlements[0].Wood+yard.Inventory[(int)ItemType.Plank]+u.CargoAmount,"cancel duplicated plank");Valid(e);
        }
        public static void CrewBoardsPhysicallyCaptainIsActualAdultAndGuardPaysEquipment()
        {
            var e=Fixture();ShipData s=PreparedShip(e,ShipKind.Warship,false);UnitData captain=e.World.Units[0],guard=e.World.Units[1],child=e.World.Units[2];child.Age=10;Need(!e.TryAssignCrew(child.Id,s.Id,true),"child captain");Need(!e.TryAssignCrew(guard.Id,s.Id,false,true),"unarmed naval guard");s.Cargo[(int)ItemType.Sword]=s.Cargo[(int)ItemType.Armor]=1;
            Need(e.TryAssignCrew(captain.Id,s.Id,true),"captain order");Need(s.CaptainUnitId==0&&captain.ShipId==0,"remote captain teleport");Need(e.TryAssignCrew(guard.Id,s.Id,false,true),"guard order");Until(e,()=>captain.ShipId==s.Id&&guard.ShipId==s.Id,300,"boarding never completed");Need(s.CaptainUnitId==captain.Id&&captain.Profession==UnitProfession.Captain,"captain identity");Need(guard.NavalWeapon==ItemType.Sword&&guard.NavalArmor==ItemType.Armor,"missing personal equipment");Near(0,s.Cargo[(int)ItemType.Sword],"sword not paid");Near(0,s.Cargo[(int)ItemType.Armor],"armor not paid");Valid(e);
        }
        public static void ProvisionsConsumePerPersonPerDayAndOldNeedsAreActuallyFed()
        {
            var e=Fixture();ShipData s=PreparedShip(e,ShipKind.Transport);s.Food=s.Water=10;var crew=s.CrewIds.Select(id=>e.World.GetUnit(id)).ToList();float beforeFood=s.Food,beforeWater=s.Water;
            for(int i=0;i<6000;i++)foreach(UnitData u in crew)Call(e,"UpdateMaritimeUnit",u,.1f);
            Near(beforeFood-2,s.Food,"2 people daily food",.008f);Near(beforeWater-2,s.Water,"2 people daily water",.008f);
            UnitData captain=crew[0];captain.Hunger=captain.Thirst=.95f;float health=captain.Health;Call(e,"UpdateMaritimeUnit",captain,.1f);Need(captain.Hunger<.56f&&captain.Thirst<.56f&&captain.Health==health,"hungry sailor died beside food");
            s.Food=s.Water=0;Need(e.ShipDepartureReason(s.Id)!="Готов к отплытию","empty ship ready");captain.Hunger=captain.Thirst=.95f;Call(e,"UpdateMaritimeUnit",captain,1f);Need(captain.Health<health,"starvation absent");Valid(e);
        }
        public static void TransportMovesPassengersAndGoodsOverWaterThenDisembarks()
        {
            var e=Fixture();ShipData s=PreparedShip(e,ShipKind.Transport);BuildingData dest=Add(e,BuildingType.Port,6,25);UnitData passenger=e.World.Units.First(u=>u.ShipId==0);passenger.Age=11;s.Cargo[(int)ItemType.IronIngot]=3;
            Need(e.TryBoardPassenger(passenger.Id,s.Id),"child passenger");Until(e,()=>passenger.ShipId==s.Id,300,"passenger did not board");Need(e.TrySailToPort(s.Id,dest.Id),"transport voyage");Need(!e.TryDisembark(passenger.Id),"disembarked at sea");Until(e,()=>s.DockedPortId==dest.Id,300,"transport never docked");Near(3,s.Cargo[(int)ItemType.IronIngot],"goods changed underway");Need(e.TryDisembark(passenger.Id),"disembark at port");Need(passenger.ShipId==0&&e.World.IsWalkable((int)passenger.X,(int)passenger.Y),"passenger left on water");Valid(e);
        }
        public static void ShipFishingConsumesBiomassAndReturnsRealFood()
        {
            var e=Fixture();ShipData s=PreparedShip(e,ShipKind.FishingBoat);int cell=e.World.Index(4,12);e.World.Infrastructure[cell].Fish=e.World.Infrastructure[cell].FishCapacity=1;Need(e.TryStartShipFishing(s.Id,cell),"fishing departure");Until(e,()=>s.FishCaught>=1,200,"ship caught nothing");Need(e.World.Infrastructure[cell].Fish<1,"fish not debited");Need(s.AgriculturalCargo.Sum()>=1,"catch not in hold");Until(e,()=>s.Phase==ShipPhase.Docked,250,"fisher not returned");Call(e,"AutoManageDockedShip",s);Need(Building(e,BuildingType.HarborWarehouse).AgriculturalStock.Sum()>=1,"catch not delivered");Valid(e);
        }
        public static void OverseasTradeCrossesBoundaryConsumesProvisionsAndKeepsFiniteOffer()
        {
            var e=Fixture();ShipData s=PreparedShip(e,ShipKind.Merchant);s.Cargo[(int)ItemType.Plank]=10;s.Cargo[(int)ItemType.CopperCoin]=30;float food=s.Food;Need(e.TryStartForeignVoyage(s.Id),"foreign voyage");Need(s.Phase==ShipPhase.Outbound&&e.World.MaritimeExports.Sum()==0,"remote export before edge");Until(e,()=>s.Phase==ShipPhase.Abroad,400,"never left physical ocean edge");Near(10,e.World.MaritimeExports[(int)ItemType.Plank],"export at boundary");float atEdge=s.Food;Step(e,50);Need(s.Food<atEdge,"offmap provisions free");Until(e,()=>s.Phase==ShipPhase.Docked,1100,"foreign ship never returned");Need(s.CompletedVoyages==1&&s.Cargo[(int)ItemType.Salt]>0&&s.Cargo[(int)ItemType.IronOre]>0,"no imported goods");Need(s.ForeignOffer[(int)ItemType.Salt]<24,"foreign assortment regenerated");Near(0,e.World.MintIssued.Sum(),"voyage minted coins");Need(s.Food<food,"voyage didn't consume food");Valid(e);
        }
        public static void NeutralCaptureTransfersPrizeCrewWithoutCreatingPeopleOrCargo()
        {
            var e=Fixture(16);e.World.Laws.Warfare=true;ShipData a=PreparedShip(e,ShipKind.Warship,true,7);foreach(int id in a.CrewIds){UnitData u=e.World.GetUnit(id);u.NavalWeapon=ItemType.Sword;u.NavalArmor=ItemType.Armor;}a.Cargo[(int)ItemType.BallistaBolts]=20;
            Need(e.TryInviteWanderer(Building(e,BuildingType.Port).Id,ShipKind.WandererSmall),"foreign target");ShipData t=e.World.Ships.Last();Until(e,()=>t.Phase==ShipPhase.Docked,600,"target approach");t.Health=t.MaxHealth*.35f;int people=e.World.Units.Count;float salt=t.Cargo[(int)ItemType.Salt];Need(e.TryAttackShip(a.Id,t.Id,true),"neutral attack refused");Need(a.Aggressor,"neutral aggression unrecorded");Until(e,()=>t.OwnerSettlementId==a.OwnerSettlementId,100,"capture failed");Need(e.World.Units.Count==people,"capture spawned crew");Need(t.CrewIds.Count==3&&t.PrisonerIds.Count==3&&a.CrewIds.Count==4,"prize manifest mismatch");Need(t.CaptainUnitId!=0&&e.World.GetUnit(t.CaptainUnitId).SettlementId==a.OwnerSettlementId,"captain magically recruited");Near(salt,t.Cargo[(int)ItemType.Salt],"capture duplicated/lost cargo");Valid(e);
        }
        public static void SinkingAndPortLossConserveSinksAndReleaseManifest()
        {
            var e=Fixture();ShipData s=PreparedShip(e,ShipKind.Transport);s.Cargo[(int)ItemType.GoldCoin]=7;s.AgriculturalCargo[(int)AgriculturalProduct.Fish]=4;s.X=2.5f;s.Y=15.5f;foreach(int id in s.CrewIds){e.World.GetUnit(id).X=s.X;e.World.GetUnit(id).Y=s.Y;}int crew=s.CrewIds.Count;Call(e,"SinkShip",s);Call(e,"RemoveDead");Near(7,e.World.LostResources[(int)ItemType.GoldCoin],"sunken coin sink");Near(4,e.World.LostAgriculture[(int)AgriculturalProduct.Fish],"sunken fish sink");Need(s.CrewIds.Count==0&&e.World.Units.Count==12-crew,"sunken crew duplicated");Call(e,"SinkShip",s);Near(7,e.World.LostResources[(int)ItemType.GoldCoin],"double sinking");
            ShipData next=PreparedShip(e,ShipKind.Transport);BuildingData port=Building(e,BuildingType.Port);port.Health=0;Call(e,"DestroyInvalidBuildings");Need(next.Phase==ShipPhase.Stranded&&next.DockedPortId==0,"destroyed port retained docking");Valid(e);
        }
        public static void SavedSeaTravelContinuesExactlyAndCorruptManifestIsRejected()
        {
            var e=Fixture();ShipData s=PreparedShip(e,ShipKind.Transport);BuildingData dest=Add(e,BuildingType.Port,6,25);Need(e.TrySailToPort(s.Id,dest.Id),"start route");Step(e,13);var second=new SimulationEngine(Clone(e.World));Need(Snapshot(e.World)==Snapshot(second.World),"load changed maritime state");for(int i=0;i<200;i++){e.Step(.1f);second.Step(.1f);}Need(Snapshot(e.World)==Snapshot(second.World),"saved voyage diverged");Valid(e);
            WorldState broken=Clone(e.World);broken.Ships[0].CrewIds.Add(broken.Ships[0].CrewIds[0]);Need(!broken.Validate(out _),"duplicated sailor accepted");broken=Clone(e.World);broken.Ships[0].Cargo[(int)ItemType.GoldCoin]=-1;Need(!broken.Validate(out _),"negative ship cargo accepted");broken=Clone(e.World);broken.MaritimeVersion=0;Need(!broken.Validate(out _),"maritime version downgrade accepted");
        }
        public static void NetworkTransferUsesAllSectionsAndRejectsPartialOverdraft()
        {
            var e=Fixture();ShipData ship=PreparedShip(e,ShipKind.Transport);BuildingData a=Building(e,BuildingType.HarborWarehouse),b=Add(e,BuildingType.HarborWarehouse,11,12);a.Inventory[(int)ItemType.IronOre]=3;b.Inventory[(int)ItemType.IronOre]=4;
            Need(e.TryTransferHarborCargo(ship.Id,ItemType.IronOre,7,true),"combined withdrawal");Near(0,a.Inventory[(int)ItemType.IronOre]+b.Inventory[(int)ItemType.IronOre],"network stores not debited");Near(7,ship.Cargo[(int)ItemType.IronOre],"combined ship credit");
            a.Inventory[(int)ItemType.RawStone]=5000;b.Inventory[(int)ItemType.RawStone]=4995;Need(!e.TryTransferHarborCargo(ship.Id,ItemType.IronOre,7,false),"partial overflowing deposit accepted");Near(7,ship.Cargo[(int)ItemType.IronOre],"failed deposit debited hold");Need(e.TryTransferHarborCargo(ship.Id,ItemType.IronOre,5,false),"second section capacity unavailable");Near(5,b.Inventory[(int)ItemType.IronOre],"wrong partition");
            b.Inventory[(int)ItemType.RawStone]-=10;a.PortFoodStored=0;b.PortFoodStored=2;b.WaterStored=3;Need(e.TryTransferHarborProvisions(ship.Id,2,3,true),"combined provisions");Near(0,b.PortFoodStored+b.WaterStored,"provision double stock");Valid(e);
        }
        public static void MissingCaptainStopsRouteAndBrokenChannelCannotTeleport()
        {
            var e=Fixture();ShipData ship=PreparedShip(e,ShipKind.Transport);BuildingData dest=Add(e,BuildingType.Port,6,25);Need(e.TrySailToPort(ship.Id,dest.Id),"start");Step(e,10);UnitData captain=e.World.GetUnit(ship.CaptainUnitId);float x=ship.X,y=ship.Y;captain.Health=0;Step(e,10);Near(x,ship.X,"dead captain moved ship x");Near(y,ship.Y,"dead captain moved ship y");Need(ship.CaptainUnitId==0,"dead captain retained");
            var other=Fixture();ShipData s=PreparedShip(other,ShipKind.Transport);BuildingData target=Add(other,BuildingType.Port,6,25);Need(other.TrySailToPort(s.Id,target.Id),"second start");for(int xx=0;xx<6;xx++)other.Paint(TerrainType.Grass,xx,18,0);Step(other,200);Need(s.DockedPortId!=target.Id&&s.Y<18,"crossed blocked sea channel");Valid(other);
        }
        public static void ShoreJobsRejectAboardResidentsWithoutChangingManifest()
        {
            var e=Fixture();ShipData s=PreparedShip(e,ShipKind.Transport);UnitData u=e.World.GetUnit(s.CaptainUnitId);BuildingData shop=Add(e,BuildingType.Carpentry,10,18);shop.Inventory[(int)ItemType.Plank]=10;int orders=e.World.ShipOrders.Count;
            Need(!e.TryStartProduction(u.Id,shop.Id,IndustryRecipeType.Furniture),"aboard industrial worker");Need(!e.TryStartEquipment(u.Id,1),"aboard equipment worker");Need(!(bool)Call(e,"SetTarget",u,12,12),"aboard path starts ashore");Need(!e.TryStartShipConstruction(u.Id,Building(e,BuildingType.Shipyard).Id,ShipKind.FishingBoat),"aboard shipwright");Need(e.World.ShipOrders.Count==orders&&u.Task==UnitTask.Sail&&u.Path.Count==0&&u.ShipId==s.Id,"failed shore command changed sailor");Valid(e);
        }
        public static void DockTradeUsesRealClerkCoinsAndFiniteForeignCargo()
        {
            var e=Fixture();BuildingData p=Building(e,BuildingType.Port),a=Building(e,BuildingType.HarborWarehouse),b=Add(e,BuildingType.HarborWarehouse,11,12);UnitData clerk=e.World.Units[0];a.Inventory[(int)ItemType.CopperCoin]=200;
            Need(e.TryInviteWanderer(p.Id,ShipKind.WandererSmall),"invite");ShipData s=e.World.Ships[0];Until(e,()=>s.Phase==ShipPhase.Docked,600,"dock");float salt=s.Cargo[(int)ItemType.Salt],coins=s.Cargo[(int)ItemType.CopperCoin]+a.Inventory[(int)ItemType.CopperCoin];
            Need(e.TryStartShipTrade(clerk.Id,s.Id,b.Id,ItemType.Salt,2,true),"trade clerk assignment");Near(salt,s.Cargo[(int)ItemType.Salt],"remote trade before clerk arrival");Until(e,()=>clerk.MaritimeOrderId==0,400,"trade not complete");Near(salt-2,s.Cargo[(int)ItemType.Salt],"foreign salt not debited");Near(2,a.Inventory[(int)ItemType.Salt]+b.Inventory[(int)ItemType.Salt],"combined warehouse didn't receive purchase");Near(coins,s.Cargo[(int)ItemType.CopperCoin]+a.Inventory[(int)ItemType.CopperCoin]+b.Inventory[(int)ItemType.CopperCoin]+a.TaxStocks[(int)ItemType.CopperCoin]+b.TaxStocks[(int)ItemType.CopperCoin],"coins minted/lost in market");Need(e.World.MarketTrades.Count>0,"no market receipt");Valid(e);
        }
        public static void SavedAbroadStateConsumesSuppliesAndFiniteOfferExactlyOnce()
        {
            var e=Fixture();ShipData s=PreparedShip(e,ShipKind.Merchant);s.Cargo[(int)ItemType.CopperCoin]=30;Need(e.TryStartForeignVoyage(s.Id),"foreign start");Until(e,()=>s.Phase==ShipPhase.Abroad,400,"edge");Step(e,121);var second=new SimulationEngine(Clone(e.World));for(int i=0;i<700;i++){e.Step(.1f);second.Step(.1f);}Need(Snapshot(e.World)==Snapshot(second.World),"abroad save replay diverged");Need(s.CompletedVoyages==1&&e.World.MaritimeImports[(int)ItemType.Salt]>0,"return exchange missing");Valid(e);
        }
        public static void TerminalHistoryIsBoundedWithoutDeletingLiveShipsOrLedgers()
        {
            var e=Fixture();ShipData live=PreparedShip(e,ShipKind.Transport);int portCell=Building(e,BuildingType.Port).BerthCell;for(int i=0;i<130;i++){ShipData old=(ShipData)Call(e,"CreateShip",ShipKind.FishingBoat,e.World.Settlements[0].Id,portCell);old.Phase=ShipPhase.Departed;}
            e.World.MaritimeExports[(int)ItemType.Salt]=123;Call(e,"CleanupMaritimeHistory");Need(e.World.Ships.Count<=64&&e.World.GetShip(live.Id)==live,"history deleted live vessel or stayed unbounded");Near(123,e.World.MaritimeExports[(int)ItemType.Salt],"cleanup lost aggregate ledger");Valid(e);
        }
        public static void FailedUnreachableConstructionDoesNotAllocateAnOrder()
        {
            var e=Fixture();BuildingData yard=Building(e,BuildingType.Shipyard);UnitData u=e.World.Units[0];foreach(ItemAmount input in MaritimeCatalog.Get(ShipKind.FishingBoat).Inputs)yard.Inventory[(int)input.Item]=input.Amount;
            for(int y=0;y<32;y++)e.Paint(TerrainType.DeepWater,10,y,0);int next=e.World.NextMaritimeOrderId,count=e.World.ShipOrders.Count;
            Need(!e.TryStartShipConstruction(u.Id,yard.Id,ShipKind.FishingBoat),"unreachable construction accepted");Need(e.World.ShipOrders.Count==count&&e.World.NextMaritimeOrderId==next&&yard.MaritimeOrderId==0,"failed command reserved order/ID/materials");Valid(e);
        }
        public static void NavalBattleStopsWithInsufficientLivingCrewOrProvisions()
        {
            var e=Fixture();e.World.Laws.Warfare=true;ShipData a=PreparedShip(e,ShipKind.Warship);foreach(int id in a.CrewIds){UnitData u=e.World.GetUnit(id);u.NavalWeapon=ItemType.Sword;u.NavalArmor=ItemType.Armor;}a.Cargo[(int)ItemType.BallistaBolts]=4;
            Need(e.TryInviteWanderer(Building(e,BuildingType.Port).Id,ShipKind.WandererSmall),"target");ShipData target=e.World.Ships.Last();Until(e,()=>target.Phase==ShipPhase.Docked,400,"target dock");Need(e.TryAttackShip(a.Id,target.Id,false),"attack");e.World.GetUnit(a.CrewIds.Last()).Health=0;Step(e,40);Near(4,a.Cargo[(int)ItemType.BallistaBolts],"understaffed gun fired");Need(a.Phase!=ShipPhase.Attacking,"understaffed attack continued");Valid(e);
        }
        public static void NavalEquipmentSurvivesDisembarkAndDropsOnLandDeath()
        {
            var e=Fixture();ShipData s=PreparedShip(e,ShipKind.Transport);UnitData u=e.World.GetUnit(s.CaptainUnitId);u.NavalWeapon=ItemType.Sword;u.NavalArmor=ItemType.Armor;Need(e.TryDisembark(u.Id),"land exit");u.Health=0;Call(e,"RemoveDead");Near(1,e.World.GroundStocks.Where(g=>g.Item==ItemType.Sword).Sum(g=>g.Amount),"sword vanished ashore");Near(1,e.World.GroundStocks.Where(g=>g.Item==ItemType.Armor).Sum(g=>g.Amount),"armor vanished ashore");Call(e,"RemoveDead");Near(1,e.World.GroundStocks.Where(g=>g.Item==ItemType.Sword).Sum(g=>g.Amount),"death duplicated sword");Valid(e);
        }
        public static void CivilianWorksBoardsVoyagesDisembarksAndWorksAgain()
        {
            var e=Fixture();UnitData u=e.World.Units[0],mate=e.World.Units[1];BuildingData carpenter=Add(e,BuildingType.Carpentry,10,18),warehouse=Building(e,BuildingType.HarborWarehouse),destination=Add(e,BuildingType.Port,6,25);carpenter.Inventory[(int)ItemType.Plank]=6;
            Need(e.TryStartProduction(u.Id,carpenter.Id,IndustryRecipeType.Furniture),"first civilian work");Until(e,()=>carpenter.CompletedIndustryBatches==1,400,"first work incomplete");
            ShipData s=PreparedShip(e,ShipKind.Transport,false);warehouse.PortFoodStored=warehouse.WaterStored=5;Need(e.TryTransferHarborProvisions(s.Id,5,5,true),"load real provisions");Need(e.TryAssignCrew(u.Id,s.Id,true)&&e.TryAssignCrew(mate.Id,s.Id),"crew orders");Until(e,()=>s.CrewIds.Count==2,500,"board actual citizens");Need(e.TrySailToPort(s.Id,destination.Id),"voyage");Until(e,()=>s.DockedPortId==destination.Id,300,"arrival");Need(e.TryDisembark(u.Id),"civilian disembark");Need(u.Profession!=UnitProfession.Captain&&u.Profession!=UnitProfession.Sailor&&u.MaritimeShoreLeaveUntilTick>e.World.Tick,"land worker remains locked in sea role");
            Need(e.TryStartProduction(u.Id,carpenter.Id,IndustryRecipeType.Furniture),"ordinary work rejected after sea service");Until(e,()=>carpenter.CompletedIndustryBatches==2,500,"second civilian work incomplete");Near(0,carpenter.Inventory[(int)ItemType.Plank],"both recipes unpaid");Need(u.ShipId==0,"civilian auto-recruited before doing ordinary work");Valid(e);
        }
        public static void FundedScarcityAutomaticallyAssignsAnActualDockClerk()
        {
            var e=Fixture();BuildingData p=Building(e,BuildingType.Port),b=Building(e,BuildingType.HarborWarehouse);b.Inventory[(int)ItemType.CopperCoin]=100;Need(e.TryInviteWanderer(p.Id,ShipKind.WandererSmall),"foreign trader");ShipData ship=e.World.Ships.Last();Until(e,()=>ship.Phase==ShipPhase.Docked,400,"dock");
            e.World.MarketNeeds.Add(new MarketNeedData{SettlementId=e.World.Settlements[0].Id,Item=ItemType.Salt,Target=2,State=MarketNeedState.Blocked,Reason="No local salt",ExpiresTick=e.World.Tick+1000,UpdatedTick=e.World.Tick});UnitData clerk=e.World.Units.First(u=>u.ShipId==0);e.World.Settlements[0].NextMaritimeTick=0;
            Need(!(bool)Call(e,"ChooseMaritimeProcurement",clerk,e.World.Settlements[0]),"automatic order without port authority");Need(e.World.DockOrders.Count==0,"absent authority allocated an order");
            BuildingData authority=Add(e,BuildingType.PortAuthority,8,15);Need(e.HasHarborAuthority(p.Id),"usable authority not recognized");clerk.DecisionTimer=0;e.World.Settlements[0].NextMaritimeTick=0;
            Until(e,()=>e.World.MarketTrades.Any(t=>t.VesselId==ship.Id&&t.Item==ItemType.Salt),500,"scarcity never assigned funded maritime procurement");Near(2,b.Inventory[(int)ItemType.Salt],"automatic import absent");Need(b.Inventory[(int)ItemType.CopperCoin]<100,"automatic import unpaid");
            float cargo=ship.Cargo[(int)ItemType.Salt];int orders=e.World.DockOrders.Count;authority.Health=0;Call(e,"DestroyInvalidBuildings");e.World.Settlements[0].NextMaritimeTick=0;
            UnitData other=e.World.Units.First(u=>u.ShipId==0&&u.Id!=clerk.Id);Need(!e.HasHarborAuthority(p.Id)&&!(bool)Call(e,"ChooseMaritimeProcurement",other,e.World.Settlements[0]),"destroyed authority schedules an order");Need(e.World.DockOrders.Count==orders,"destroyed authority allocated order");Near(cargo,ship.Cargo[(int)ItemType.Salt],"authority loss erased cargo");Need(e.TryStartShipTrade(other.Id,ship.Id,b.Id,ItemType.Salt,1,true),"manual trade requires authority");Valid(e);
        }
        public static void CoinTransfersNeverSplitOrRecombineFractionalLegacyLots()
        {
            var e=Fixture();ShipData ship=PreparedShip(e,ShipKind.Transport);BuildingData a=Building(e,BuildingType.HarborWarehouse),b=Add(e,BuildingType.HarborWarehouse,10,12);int coin=(int)ItemType.CopperCoin;
            a.Inventory[coin]=b.Inventory[coin]=.5f;
            Need(!e.TryTransferShipCargo(ship.Id,a.Id,ItemType.CopperCoin,.5f,true),"single store split a coin");Need(!e.TryTransferHarborCargo(ship.Id,ItemType.CopperCoin,.5f,true),"network split a coin");Need(!e.TryTransferHarborCargo(ship.Id,ItemType.CopperCoin,1,true),"network recombined legacy fractions");Near(0,e.HarborStock(ship.DockedPortId,ItemType.CopperCoin),"fractional coins advertised as spendable");
            a.Inventory[coin]=b.Inventory[coin]=0;ship.Cargo[coin]=1.5f;a.Inventory[(int)ItemType.RawStone]=4999.75f;b.Inventory[(int)ItemType.RawStone]=4999.25f;
            Need(!e.TryTransferHarborCargo(ship.Id,ItemType.CopperCoin,1,false),"one coin fragmented across free .25/.75 spaces");Near(1.5f,ship.Cargo[coin],"failed transfer debited legacy hold");
            b.Inventory[(int)ItemType.RawStone]=4998.75f;Need(e.TryTransferHarborCargo(ship.Id,ItemType.CopperCoin,1,false),"whole coin could not fit second store");Near(0,a.Inventory[coin],"coin fragment in first store");Near(1,b.Inventory[coin],"whole coin not assigned to second store");Near(.5f,ship.Cargo[coin],"legacy remainder changed");Need(!e.TryTransferShipCargo(ship.Id,b.Id,ItemType.CopperCoin,.5f,false),"single warehouse accepted fractional deposit");Valid(e);
        }
        public static void AuthorityOrganizesAutomaticForeignVoyagesButManualRemainsAvailable()
        {
            var e=Fixture();ShipData ship=PreparedShip(e,ShipKind.Merchant);ship.Cargo[(int)ItemType.CopperCoin]=30;ship.ActionTimer=31;
            Call(e,"AutoManageDockedShip",ship);Need(ship.Phase==ShipPhase.Docked,"merchant departed automatically without authority");
            BuildingData authority=Add(e,BuildingType.PortAuthority,8,15);authority.Health=0;Call(e,"DestroyInvalidBuildings");Call(e,"AutoManageDockedShip",ship);Need(ship.Phase==ShipPhase.Docked,"merchant departed with destroyed authority");Near(30,ship.Cargo[(int)ItemType.CopperCoin],"authority loss erased money");Need(e.TryStartForeignVoyage(ship.Id),"manual voyage requires authority");
            var organized=Fixture();ShipData merchant=PreparedShip(organized,ShipKind.Merchant);merchant.Cargo[(int)ItemType.CopperCoin]=30;merchant.ActionTimer=31;Add(organized,BuildingType.PortAuthority,8,15);Call(organized,"AutoManageDockedShip",merchant);Need(merchant.Phase==ShipPhase.Outbound,"usable authority did not organize funded voyage");Valid(e);Valid(organized);
        }
        public static void HarborNetworkRequiresDryPathOrBridgeAndFindsDryDetours()
        {
            var e=Fixture();BuildingData p=Building(e,BuildingType.Port),b=Building(e,BuildingType.HarborWarehouse);b.Inventory[(int)ItemType.IronOre]=5;
            for(int y=0;y<32;y++){int cell=e.World.Index(7,y);TileData t=e.World.Cells[cell];t.Terrain=TerrainType.ShallowWater;t.WaterKind=WaterKind.River;t.WaterDepth=.2f;e.World.Cells[cell]=t;}
            Need(e.World.IsWalkable(7,11),"fixture is not a ford");Need(e.GetHarborWarehouses(p.Id).Count==0,"ford was accepted as shared storage connection");Near(5,b.Inventory[(int)ItemType.IronOre],"disconnect destroyed stock");
            int detour=e.World.Index(7,10);TileData water=e.World.Cells[detour],dry=water;dry.Terrain=TerrainType.Grass;dry.WaterKind=WaterKind.None;dry.WaterDepth=0;e.World.Cells[detour]=dry;
            Need(e.GetHarborWarehouses(p.Id).Contains(b),"short ford hid valid bounded dry detour");e.World.Cells[detour]=water;
            int bridge=e.World.Index(7,11);InfrastructureCell span=e.World.Infrastructure[bridge];span.BridgeLevel=1;span.BridgeOwnerId=p.SettlementId;e.World.Infrastructure[bridge]=span;
            Need(e.GetHarborWarehouses(p.Id).Contains(b),"real bridge failed to reconnect storage");span.BridgeLevel=0;span.BridgeOwnerId=0;e.World.Infrastructure[bridge]=span;Need(e.GetHarborWarehouses(p.Id).Count==0,"removed bridge kept phantom connection");Valid(e);
        }
        public static void LegacyMaritimeMigrationHasNoShipsImportsOrRngDraw()
        {
            var e=Fixture();WorldState w=Clone(e.World);w.MaritimeVersion=0;uint rng=w.RngState;int next=w.NextId;float items=w.Buildings.Sum(b=>b.Inventory.Sum());var restored=new SimulationEngine(w);Need(w.MaritimeVersion==1&&w.Ships.Count==0&&w.MaritimeImports.Sum()==0,"migration granted fleet/goods");Need(w.RngState==rng&&w.NextId==next,"migration consumed RNG/IDs");Near(items,w.Buildings.Sum(b=>b.Inventory.Sum()),"migration moved stocks");Valid(restored);
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class MaritimeTests
    {
        [SetUp] public void Serializers(){MaritimeScenarios.Clone=w=>JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w));MaritimeScenarios.Snapshot=w=>JsonUtility.ToJson(w);}
        [TestCase("ClosedLakeRejectsForeignArrivalAndConnectedSeaAdmitsRealCrew")]
        [TestCase("ThreeWandererClassesHaveFiniteTotalHoldsAndExitAccounting")]
        [TestCase("ConnectedWarehouseCapacityTransfersAndDisconnectionConserveStock")]
        [TestCase("FoodWaterSupplyWalksFromCampfireAndRespectsOneHand")]
        [TestCase("ShipbuildingUsesLocalDeliveredInputsTimeAndResearch")]
        [TestCase("ConstructionCancelReturnsDeliveredEscrowWithoutHull")]
        [TestCase("CrewBoardsPhysicallyCaptainIsActualAdultAndGuardPaysEquipment")]
        [TestCase("ProvisionsConsumePerPersonPerDayAndOldNeedsAreActuallyFed")]
        [TestCase("TransportMovesPassengersAndGoodsOverWaterThenDisembarks")]
        [TestCase("ShipFishingConsumesBiomassAndReturnsRealFood")]
        [TestCase("OverseasTradeCrossesBoundaryConsumesProvisionsAndKeepsFiniteOffer")]
        [TestCase("NeutralCaptureTransfersPrizeCrewWithoutCreatingPeopleOrCargo")]
        [TestCase("SinkingAndPortLossConserveSinksAndReleaseManifest")]
        [TestCase("SavedSeaTravelContinuesExactlyAndCorruptManifestIsRejected")]
        [TestCase("NetworkTransferUsesAllSectionsAndRejectsPartialOverdraft")]
        [TestCase("MissingCaptainStopsRouteAndBrokenChannelCannotTeleport")]
        [TestCase("ShoreJobsRejectAboardResidentsWithoutChangingManifest")]
        [TestCase("DockTradeUsesRealClerkCoinsAndFiniteForeignCargo")]
        [TestCase("SavedAbroadStateConsumesSuppliesAndFiniteOfferExactlyOnce")]
        [TestCase("TerminalHistoryIsBoundedWithoutDeletingLiveShipsOrLedgers")]
        [TestCase("FailedUnreachableConstructionDoesNotAllocateAnOrder")]
        [TestCase("NavalBattleStopsWithInsufficientLivingCrewOrProvisions")]
        [TestCase("NavalEquipmentSurvivesDisembarkAndDropsOnLandDeath")]
        [TestCase("CivilianWorksBoardsVoyagesDisembarksAndWorksAgain")]
        [TestCase("FundedScarcityAutomaticallyAssignsAnActualDockClerk")]
        [TestCase("CoinTransfersNeverSplitOrRecombineFractionalLegacyLots")]
        [TestCase("AuthorityOrganizesAutomaticForeignVoyagesButManualRemainsAvailable")]
        [TestCase("HarborNetworkRequiresDryPathOrBridgeAndFindsDryDetours")]
        [TestCase("LegacyMaritimeMigrationHasNoShipsImportsOrRngDraw")]
        public void Scenario(string name){typeof(MaritimeScenarios).GetMethod(name).Invoke(null,null);}
    }
#endif
}
