using System;
using System.Linq;
using System.Reflection;
using WorldGenesis.Simulation;
#if UNITY_5_3_OR_NEWER
using NUnit.Framework;
using UnityEngine;
#endif

namespace WorldGenesis.Tests
{
    public static class MarketScenarios
    {
        public static Func<WorldState, WorldState> Clone;
        public static Func<WorldState, string> Snapshot;
        private static void Need(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        private static void Near(double a, double b, string message) { Need(Math.Abs(a-b) < .001, message + ": " + b + " != " + a); }
        private static object Call(SimulationEngine e, string method, params object[] args) { return typeof(SimulationEngine).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Invoke(e,args); }
        private static void Valid(WorldState w) { Need(w.Validate(out string error), error); }
        private static SimulationEngine Fixture(bool technology = true)
        {
            WorldState w = new WorldState { Seed=3819, RngState=3819, Width=40, Height=40, Cells=new TileData[1600] };
            for(int i=0;i<w.Cells.Length;i++)w.Cells[i]=new TileData { Terrain=TerrainType.Grass, Height=.55f,RouteHeight=.55f,Flow=1,DrainageParent=-1,Moisture=.65f,Temperature=.42f };
            SimulationEngine e=new SimulationEngine(w);e.Spawn(SpeciesType.Human,10,10,3);w.Laws.Aging=w.Laws.Births=w.Laws.Warfare=w.NaturalWildlife=false;w.Weather.Automatic=false;
            foreach(UnitData u in w.Units){u.Age=25;u.Hunger=u.Thirst=u.Fatigue=u.SocialNeed=0;u.DecisionTimer=500;u.Task=UnitTask.Idle;u.Path.Clear();u.PathCursor=0;}
            SettlementData s=w.Settlements[0];s.Food=100;s.Wood=0;s.EconomyPolicy=EconomicPolicy.Market;s.TaxRate=.1f;s.NextPolicyReviewTick=50000;
            if(technology)foreach(TechnologyDefinition t in TechnologyCatalog.All)s.UnlockedTechnologies.Add(t.Id);
            return e;
        }
        private static BuildingData Add(SimulationEngine e,BuildingType kind,int x=20,int y=10) {return (BuildingData)Call(e,"AddBuilding",e.World.Settlements[0],x,y,kind);}
        private static BuildingData Hall(WorldState w){return w.Buildings.First(b=>b.Type==BuildingType.Hall);}
        private static void Own(BuildingData b,UnitData u,ItemType item,float amount){b.PersonalStocks.Add(new OwnedGoodsData {OwnerId=u.Id,Item=item,Amount=amount});}
        private static float Owned(BuildingData b,UnitData u,ItemType item){return b.PersonalStocks.Where(x=>x.OwnerId==u.Id&&x.Item==item).Sum(x=>x.Amount);}
        private static float Total(WorldState w,ItemType item)
        {return w.Buildings.Sum(b=>b.Inventory[(int)item]+b.TaxStocks[(int)item]+b.PersonalStocks.Where(x=>x.Item==item).Sum(x=>x.Amount))+w.Units.Where(u=>u.CargoKind==item).Sum(u=>u.CargoAmount)+w.GroundStocks.Where(g=>!g.IsFood&&g.Item==item).Sum(g=>g.Amount)+w.LostResources[(int)item];}
        private static void WorkerTick(SimulationEngine e,UnitData u)
        {
            e.World.Tick++;e.World.SimulatedSeconds+=.1f;Call(e,"BuildSpatialIndex");Call(e,"UpdateUnit",u,.1f);Call(e,"UpdatePolitics");Call(e,"UpdateMarkets");
            Need(SimulationEngine.CarriedLoad(u)<=SimulationEngine.CarryCapacityFor(u)+.0001f,"Carry overflow");Valid(e.World);
        }
        private static void Finish(SimulationEngine e,UnitData u){for(int i=0;i<1600&&u.MarketOrderId!=0;i++)WorkerTick(e,u);Need(u.MarketOrderId==0,"Order never finished");Valid(e.World);}
        private static bool Exchange(SimulationEngine e,BuildingData b,float[] hold,ItemType item,float quantity,bool sells,float free=1000)
        {return (bool)Call(e,"MarketExchangeStock",b,hold,item,quantity,sells,0,100,free);}

        public static void ExactDenominationsAndChangeNeverMintMoney()
        {
            Need(!MarketCatalog.PlanPayment(new[]{0,0,1},new[]{0,0,0},18,out int[] none),"Change invented");
            Need(MarketCatalog.PlanPayment(new[]{0,2,0},new[]{2,0,0},18,out int[] net),"Real change rejected");Need(net.SequenceEqual(new[]{-2,2,0}),"Wrong denomination tender");
            for(int price=1;price<500;price++)if(MarketCatalog.PlanPayment(new[]{7,8,9},new[]{9,9,2},price,out net))
            {Near(price,net[0]+10*net[1]+100*net[2],"Value changed");for(int i=0;i<3;i++)Need(new[]{7,8,9}[i]-net[i]>=0&&new[]{9,9,2}[i]+net[i]>=0,"Negative purse");}
        }
        public static void RemoteOrderWaitsForDoorThenTransfersExactlyOnce()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,BuildingType.Shop);UnitData buyer=w.Units[0],seller=w.Units[1];Own(b,buyer,ItemType.CopperCoin,40);Own(b,seller,ItemType.IronTools,1);
            Need(e.TryPlaceMarketOrder(buyer.Id,b.Id,seller.Id,ItemType.IronTools,1),"Order rejected");WorkerTick(e,buyer);Near(40,Owned(b,buyer,ItemType.CopperCoin),"Remote debit");Near(1,Owned(b,seller,ItemType.IronTools),"Remote transfer");
            int id=buyer.MarketOrderId;Finish(e,buyer);Need(w.GetMarketOrder(id).State==MarketOrderState.Completed,"Not completed");Near(22,Owned(b,buyer,ItemType.CopperCoin),"Buyer price");Near(17,Owned(b,seller,ItemType.CopperCoin),"Seller receipt");Near(1,b.TaxStocks[(int)ItemType.CopperCoin],"Tax once");Near(1,Owned(b,buyer,ItemType.IronTools),"Bought tool");
            long trades=w.MarketTransactions;for(int i=0;i<10;i++)WorkerTick(e,buyer);Near(trades,w.MarketTransactions,"Repeated payment");Near(40,Total(w,ItemType.CopperCoin),"Coin conservation");
        }
        public static void LastPrivateGoodCannotBeReservedTwice()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,BuildingType.Shop);UnitData a=w.Units[0],seller=w.Units[1],c=w.Units[2];Own(b,a,ItemType.CopperCoin,30);Own(b,c,ItemType.CopperCoin,30);Own(b,seller,ItemType.IronTools,1);
            Need(e.TryPlaceMarketOrder(a.Id,b.Id,seller.Id,ItemType.IronTools,1),"First order");Need(!e.TryPlaceMarketOrder(c.Id,b.Id,seller.Id,ItemType.IronTools,1),"Double reservation");Finish(e,a);Near(1,Owned(b,a,ItemType.IronTools),"Single good lost");Near(0,Owned(b,c,ItemType.IronTools),"Duplicate good");
        }
        public static void CancelledOrderReleasesStockAndPreservesBothPurses()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,BuildingType.Shop);UnitData a=w.Units[0],seller=w.Units[1];Own(b,a,ItemType.CopperCoin,30);b.Inventory[(int)ItemType.IronTools]=1;
            Need(e.TryPlaceMarketOrder(a.Id,b.Id,0,ItemType.IronTools,1),"Public sale");Near(1,SimulationEngine.MarketReserved(b,ItemType.IronTools),"Missing shared reservation");Need(e.CancelMarketOrder(a.Id),"Cancel");Near(0,SimulationEngine.MarketReserved(b,ItemType.IronTools),"Reservation leaked");Near(30,Total(w,ItemType.CopperCoin),"Cancel spent money");Near(1,b.Inventory[(int)ItemType.IronTools],"Cancel deleted good");Valid(w);
        }
        public static void InsolventChildForeignAndMissingSupplyNeverCreateGoods()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,BuildingType.Shop);UnitData a=w.Units[0],seller=w.Units[1];Own(b,seller,ItemType.Salt,1);
            Need(!e.TryPlaceMarketOrder(a.Id,b.Id,seller.Id,ItemType.Salt,1),"Poor buyer acquired salt");Own(b,a,ItemType.CopperCoin,20);a.Age=12;Need(!e.TryPlaceMarketOrder(a.Id,b.Id,seller.Id,ItemType.Salt,1),"Child trade");a.Age=25;int home=a.SettlementId;a.SettlementId=0;Need(!e.TryPlaceMarketOrder(a.Id,b.Id,seller.Id,ItemType.Salt,1),"Foreign warehouse purchase");a.SettlementId=home;
            Need(!e.TryPlaceMarketOrder(a.Id,b.Id,seller.Id,ItemType.GoldOre,1),"Absent gold created");Need(!e.TryPlaceMarketOrder(a.Id,b.Id,seller.Id,ItemType.Salt,float.NaN),"NaN order");Near(20,Total(w,ItemType.CopperCoin),"Rejected command debit");Near(0,Total(w,ItemType.GoldOre),"Rejected gold");Valid(w);
        }
        public static void ChangedPaymentBudgetCancelsBeforeAnyGoodsDebit()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,BuildingType.Shop);UnitData a=w.Units[0],seller=w.Units[1];Own(b,seller,ItemType.IronOre,2);b.TaxStocks[(int)ItemType.CopperCoin]=10;
            Need(e.TryPlaceMarketOrder(a.Id,b.Id,seller.Id,ItemType.IronOre,2,true),"Procurement");b.TaxStocks[(int)ItemType.CopperCoin]-=10;Hall(w).TaxStocks[(int)ItemType.CopperCoin]+=10;Finish(e,a);
            Near(2,Owned(b,seller,ItemType.IronOre),"Partial unpaid purchase");Near(0,b.Inventory[(int)ItemType.IronOre],"Free procurement");Near(10,Total(w,ItemType.CopperCoin),"Money lost on cancel");Near(0,w.MarketTransactions,"False sale receipt");
        }
        public static void DeadBuyerAndDemolishedShopReleasePendingReservations()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,BuildingType.Shop);UnitData a=w.Units[0];Own(b,a,ItemType.CopperCoin,30);b.Inventory[(int)ItemType.IronTools]=1;
            Need(e.TryPlaceMarketOrder(a.Id,b.Id,0,ItemType.IronTools,1),"Order");a.Health=0;Call(e,"RemoveDead");Need(w.MarketOrders[0].State==MarketOrderState.Cancelled,"Death reservation");Near(30,Total(w,ItemType.CopperCoin),"Death money loss");Near(1,Total(w,ItemType.IronTools),"Death product loss");Valid(w);
            UnitData c=w.Units[0];Own(b,c,ItemType.CopperCoin,30);Need(e.TryPlaceMarketOrder(c.Id,b.Id,0,ItemType.IronTools,1),"Second order");b.Health=0;Call(e,"DestroyInvalidBuildings");for(int i=0;i<10;i++)WorkerTick(e,c);Need(c.MarketOrderId==0,"Destroyed building reservation");Near(1,Total(w,ItemType.IronTools),"Destroyed stock unaccounted");
        }
        public static void SaveDuringApproachContinuesWithSamePaymentAndTax()
        {
            SimulationEngine a=Fixture();WorldState w=a.World;BuildingData b=Add(a,BuildingType.Shop);UnitData buyer=w.Units[0],seller=w.Units[1];Own(b,buyer,ItemType.CopperCoin,40);Own(b,seller,ItemType.IronTools,1);Need(a.TryPlaceMarketOrder(buyer.Id,b.Id,seller.Id,ItemType.IronTools,1),"Order");WorkerTick(a,buyer);
            SimulationEngine copy=new SimulationEngine(Clone(w));UnitData other=copy.World.GetUnit(buyer.Id);
            for(int i=0;i<400;i++){WorkerTick(a,buyer);WorkerTick(copy,other);}Need(Snapshot(w)==Snapshot(copy.World),"Market continuation diverged");Need(w.MarketTrades.Count==1,"Expected exactly one payment");
        }
        public static void TreasuryProcurementIsPaidFromExistingLocalCoins()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Hall(w);UnitData buyer=w.Units[0],seller=w.Units[1];Own(b,seller,ItemType.IronOre,10);b.TaxStocks[(int)ItemType.CopperCoin]=50;
            Need(e.TryPlaceMarketOrder(buyer.Id,b.Id,seller.Id,ItemType.IronOre,10,true),"Treasury purchase");Finish(e,buyer);Near(10,b.Inventory[(int)ItemType.IronOre],"Public goods");Near(27,Owned(b,seller,ItemType.CopperCoin),"Supplier pay");Near(23,b.TaxStocks[(int)ItemType.CopperCoin],"Treasury after payment and tax");Near(50,Total(w,ItemType.CopperCoin),"Money emission");
        }
        public static void MaritimeImportUsesRealHoldAndWholeCoinChange()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,BuildingType.Warehouse);float[] hold=new float[ResourceCatalog.ItemCount];hold[(int)ItemType.IronTools]=1;hold[(int)ItemType.CopperCoin]=2;b.Inventory[(int)ItemType.SilverCoin]=2;
            Need(Exchange(e,b,hold,ItemType.IronTools,1,true),"Ship import");Near(1,b.Inventory[(int)ItemType.IronTools],"Imported good");Near(0,hold[(int)ItemType.IronTools],"Ship retained sold good");Near(2,b.TaxStocks[(int)ItemType.CopperCoin],"Physical change");Near(2,hold[(int)ItemType.SilverCoin],"Ship payment");Near(0,hold[(int)ItemType.CopperCoin],"Ship change not debited");Need(!Exchange(e,b,hold,ItemType.IronTools,1,true),"Sold twice");Valid(w);
        }
        public static void MaritimeExportTaxAndDenominationsConserveBothSides()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,BuildingType.Warehouse);float[] hold=new float[ResourceCatalog.ItemCount];b.Inventory[(int)ItemType.IronOre]=10;hold[(int)ItemType.CopperCoin]=50;
            Need(Exchange(e,b,hold,ItemType.IronOre,10,false),"Ship export");Near(10,hold[(int)ItemType.IronOre],"Export goods");Near(20,hold[(int)ItemType.CopperCoin],"Ship payment");Near(30,b.TaxStocks[(int)ItemType.CopperCoin],"Real export proceeds");Near(3,w.MarketCopperTaxes,"Tax record");Near(50,Total(w,ItemType.CopperCoin)+hold[(int)ItemType.CopperCoin],"Export coin mass");Valid(w);
        }
        public static void ShipAndShoreCapacityFailAtomically()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,BuildingType.Warehouse);float[] hold=new float[ResourceCatalog.ItemCount];hold[(int)ItemType.Salt]=10;b.TaxStocks[(int)ItemType.GoldCoin]=1;hold[(int)ItemType.CopperCoin]=40;
            b.Inventory[(int)ItemType.RawStone]=StorageCapacity.Capacity(b)-1;float[] before=(float[])hold.Clone();Need(!Exchange(e,b,hold,ItemType.Salt,10,true),"Shore overflow");Need(hold.SequenceEqual(before),"Rejected trade mutated hold");Near(1,b.TaxStocks[(int)ItemType.GoldCoin],"Rejected trade debited gold");
            b.Inventory[(int)ItemType.RawStone]=100;hold[(int)ItemType.CopperCoin]=0;hold[(int)ItemType.GoldCoin]=1;b.TaxStocks[(int)ItemType.CopperCoin]=90;Need(!Exchange(e,b,hold,ItemType.RawStone,10,false,0),"Full ship overflow");Near(100,b.Inventory[(int)ItemType.RawStone],"Failed export lost stock");
        }
        public static void RareAgriculturalTradeRequiresAnActuallyCarriedRareItem()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,BuildingType.Warehouse);float[] hold=new float[ResourceCatalog.ItemCount],agri=new float[FishingCatalog.AgriculturalProductCount];b.TaxStocks[(int)ItemType.CopperCoin]=200;
            Need(!(bool)Call(e,"MarketExchangeAgriculture",b,hold,agri,AgriculturalProduct.RiverPearl,1f,true,0,100,1000f),"Undiscovered pearl offered");agri[(int)AgriculturalProduct.RiverPearl]=1;
            Need((bool)Call(e,"MarketExchangeAgriculture",b,hold,agri,AgriculturalProduct.RiverPearl,1f,true,0,100,1000f),"Real rare import rejected");Near(1,b.AgriculturalStock[(int)AgriculturalProduct.RiverPearl],"Rare import");Near(0,agri[(int)AgriculturalProduct.RiverPearl],"Rare duplicate");Near(200,Total(w,ItemType.CopperCoin)+hold[(int)ItemType.CopperCoin],"Rare currency conservation");Valid(w);
        }
        public static void ConnectedHarborUsesCoinsAndSpaceAcrossRealWarehouses()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData a=Add(e,BuildingType.HarborWarehouse,20,10),b=Add(e,BuildingType.HarborWarehouse,22,10);var network=new System.Collections.Generic.List<BuildingData>{a,b};
            a.Inventory[(int)ItemType.RawStone]=StorageCapacity.Capacity(a)-1;a.TaxStocks[(int)ItemType.SilverCoin]=1;float[] hold=new float[ResourceCatalog.ItemCount],agri=new float[FishingCatalog.AgriculturalProductCount];hold[(int)ItemType.Salt]=1;hold[(int)ItemType.CopperCoin]=4;
            Need((bool)Call(e,"MarketExchangeHarborNetwork",network,hold,agri,ItemType.Salt,AgriculturalProduct.None,1f,true,0,100,1000f),"Connected network import failed");Near(1,Total(w,ItemType.Salt),"Network import count");Near(4,Total(w,ItemType.CopperCoin),"Network change");Near(1,hold[(int)ItemType.SilverCoin],"Network payment");Need(StorageCapacity.Overflow(a)==0&&StorageCapacity.Overflow(b)==0,"Network overfilled one warehouse");Near(1,b.Inventory[(int)ItemType.Salt],"Goods did not use connected free space");Valid(w);
        }
        public static void HarborExportCanMakeChangeFromAnotherWarehouseAtomically()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData a=Add(e,BuildingType.HarborWarehouse,20,10),b=Add(e,BuildingType.HarborWarehouse,22,10);var network=new System.Collections.Generic.List<BuildingData>{a,b};
            a.TaxStocks[(int)ItemType.SilverCoin]=7;b.Inventory[(int)ItemType.IronOre]=10;float[] hold=new float[ResourceCatalog.ItemCount],agri=new float[FishingCatalog.AgriculturalProductCount];hold[(int)ItemType.GoldCoin]=1;
            Need(!Exchange(e,b,hold,ItemType.IronOre,10,false),"Single warehouse invented change");Need((bool)Call(e,"MarketExchangeHarborNetwork",network,hold,agri,ItemType.IronOre,AgriculturalProduct.None,10f,false,0,100,1000f),"Network export rejected actual change");Near(10,hold[(int)ItemType.IronOre],"Network goods");Near(7,hold[(int)ItemType.SilverCoin],"Change not from physical A");Near(1,Total(w,ItemType.GoldCoin),"Network export proceeds");Near(0,a.TaxStocks[(int)ItemType.SilverCoin],"Change duplicated");string before=Snapshot(w);float[] copy=(float[])hold.Clone();Need(!(bool)Call(e,"MarketExchangeHarborNetwork",network,hold,agri,ItemType.IronOre,AgriculturalProduct.None,10f,false,0,100,1000f),"Network sold twice");Need(before==Snapshot(w)&&copy.SequenceEqual(hold),"Rejected network mutation");Valid(w);
        }
        public static void HarborCannotSplitCoinAcrossFractionalFreeSpaces()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;w.Settlements[0].TaxRate=0;
            BuildingData a=Add(e,BuildingType.HarborWarehouse,20,10),b=Add(e,BuildingType.HarborWarehouse,22,10);var network=new System.Collections.Generic.List<BuildingData>{a,b};
            a.Inventory[(int)ItemType.Salt]=.1f;a.Inventory[(int)ItemType.RawStone]=4999;a.PortFoodStored=.7f;
            b.Inventory[(int)ItemType.RawStone]=4999;b.PortFoodStored=.3f;
            float[] hold=new float[ResourceCatalog.ItemCount],agri=new float[FishingCatalog.AgriculturalProductCount];hold[(int)ItemType.CopperCoin]=1;
            string before=Snapshot(w);float[] saved=(float[])hold.Clone();
            Need(!(bool)Call(e,"MarketExchangeHarborNetwork",network,hold,agri,ItemType.Salt,AgriculturalProduct.None,.1f,false,0,100,1000f),"Coin split into fractional free spaces");
            Need(before==Snapshot(w)&&saved.SequenceEqual(hold),"Failed integer allocation mutated ledger");
            b.PortFoodStored=0;
            Need((bool)Call(e,"MarketExchangeHarborNetwork",network,hold,agri,ItemType.Salt,AgriculturalProduct.None,.1f,false,0,100,1000f),"Whole coin slot rejected");
            Near(0,a.TaxStocks[(int)ItemType.CopperCoin],"Coin split at A");Near(1,b.TaxStocks[(int)ItemType.CopperCoin],"Whole coin absent at B");Near(0,hold[(int)ItemType.CopperCoin],"Unpaid exported good");Valid(w);
        }
        public static void FractionalLegacyCoinLotsAreNotInventedWholePayments()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,BuildingType.HarborWarehouse);float[] hold=new float[ResourceCatalog.ItemCount];hold[(int)ItemType.Log]=1;
            b.Inventory[(int)ItemType.CopperCoin]=.7f;b.TaxStocks[(int)ItemType.CopperCoin]=.3f;
            string before=Snapshot(w);Need(!Exchange(e,b,hold,ItemType.Log,1,true),"Separate fragments combined into payable coin");Need(before==Snapshot(w),"Fractional legacy holdings mutated");
            b.Inventory[(int)ItemType.CopperCoin]=1.7f;Need(Exchange(e,b,hold,ItemType.Log,1,true),"Actual whole inventory coin unavailable");Near(.3f,b.TaxStocks[(int)ItemType.CopperCoin],"Tax fragment debited");Near(.7f,b.Inventory[(int)ItemType.CopperCoin],"Inventory fragment changed");Near(1,hold[(int)ItemType.CopperCoin],"Whole payment absent");Valid(w);
        }
        public static void MintPaysRealCompletedWorkButNoIdleStipend()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Add(e,BuildingType.Mint);UnitData u=w.Units[0];b.Inventory[(int)ItemType.CopperIngot]=1;b.Inventory[(int)ItemType.Charcoal]=1;
            Need(e.TryStartProduction(u.Id,b.Id,IndustryRecipeType.CopperCoin),"Mint order");for(int i=0;i<1000&&b.CompletedIndustryBatches==0;i++)WorkerTick(e,u);
            Near(1,b.CompletedIndustryBatches,"No mint work");Near(10,Total(w,ItemType.CopperCoin),"Mint output duplicated");Near(1,Owned(b,u,ItemType.CopperCoin),"No actual wage");Near(9,b.TaxStocks[(int)ItemType.CopperCoin],"Mint treasury");Near(1,w.MarketCopperWages,"Wage record");long paid=w.MarketCopperWages;for(int i=0;i<20;i++)WorkerTick(e,u);Near(paid,w.MarketCopperWages,"Idle wage minted");
        }
        public static void MigrationCreatesNoCashAndRejectsForgedMarketState()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;uint rng=w.RngState;int id=w.NextId;w.MarketVersion=0;w.MarketOrders=null;w.MarketPrices=null;w.MarketNeeds=null;w.MarketTrades=null;
            Valid(w);SimulationEngine migrated=new SimulationEngine(Clone(w));Near(rng,migrated.World.RngState,"Migration RNG");Near(id,migrated.World.NextId,"Migration IDs");Near(0,Total(migrated.World,ItemType.GoldCoin),"Migration coins");Valid(migrated.World);
            WorldState bad=Clone(migrated.World);bad.MarketVersion=2;Need(!bad.Validate(out _),"Future market");bad=Clone(migrated.World);bad.MarketPrices.Add(new MarketPriceData{SettlementId=bad.Settlements[0].Id,GoodId=(int)ItemType.Salt,Multiplier=float.NaN});Need(!bad.Validate(out _),"NaN price");bad=Clone(migrated.World);bad.MarketVersion=0;bad.MarketCopperWages=1;Need(!bad.Validate(out _),"Fake legacy wages");
        }
        public static void FundedScarcityRaisesPriceSmoothlyWithoutCreatingStock()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Hall(w);UnitData buyer=w.Units[0],seller=w.Units[1];Own(b,buyer,ItemType.CopperCoin,70);
            Need(!e.TryPlaceMarketOrder(buyer.Id,b.Id,seller.Id,ItemType.Salt,10),"Absent salt accepted");float before=w.MarketPrice(b.SettlementId,ItemType.Salt);w.Tick=100;Call(e,"UpdatePolitics");Call(e,"UpdateMarkets");float after=w.MarketPrice(b.SettlementId,ItemType.Salt);Need(after>before&&after<before*1.15f,"Price not smooth or funded scarcity ignored");Near(0,Total(w,ItemType.Salt),"Price created salt");Near(70,Total(w,ItemType.CopperCoin),"Price created money");Valid(w);
        }
        public static void PlannedChainProducesAndEquipsToolsAndClothesWithoutIntermediateGrants()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;SettlementData s=w.Settlements[0];BuildingData hall=Hall(w);Add(e,BuildingType.Sawmill,15,10);Add(e,BuildingType.Smelter,19,10);Add(e,BuildingType.Smithy,23,10);Add(e,BuildingType.Weaver,19,15);Add(e,BuildingType.Tailor,23,15);
            s.Wood=12;hall.Inventory[(int)ItemType.Log]=50;hall.Inventory[(int)ItemType.IronOre]=6;hall.AgriculturalStock[(int)AgriculturalProduct.Fiber]=12;
            foreach(UnitData u in w.Units){u.BasketLevel=3;u.DecisionTimer=0;}
            for(int i=0;i<4500&&(w.WorkToolsIssued==0||w.ClothesIssued==0);i++){e.Step(.1f);if(i%10==0)Valid(w);}
            string diagnostic="; produced="+string.Join(",",IndustryCatalog.All.Where(r=>w.ProducedItems[(int)r.Output]>0).Select(r=>r.Output+":"+w.ProducedItems[(int)r.Output]))+"; needs="+string.Join(";",w.MarketNeeds.Select(n=>n.Item+":"+n.Reason))+"; buildings="+string.Join(";",w.Buildings.Select(b=>b.Id+":"+b.Type+"/"+b.ActiveIndustryRecipe+"/"+b.IndustryWorkerId+"="+string.Join(",",Enumerable.Range(1,ResourceCatalog.ItemCount-1).Where(i=>b.Inventory[i]>0).Select(i=>((ItemType)i)+":"+b.Inventory[i]))))+"; citizens="+string.Join(";",w.Units.Select(u=>u.Id+":"+u.Task+"/"+u.TargetBuildingId+" H"+u.Hunger+" T"+u.Thirst+" F"+u.Fatigue));
            Need(w.WorkToolsIssued>0,"Planner did not produce/use tool"+diagnostic);Need(w.ClothesIssued>0,"Planner did not produce/use clothes"+diagnostic);Need(w.ProducedItems[(int)ItemType.IronIngot]>0&&w.ProducedItems[(int)ItemType.Charcoal]>0&&w.ProducedItems[(int)ItemType.LinenCloth]>0,"Skipped intermediates");Near(0,w.MintIssued.Sum(),"Communal bootstrap needs free money");Valid(w);
        }
        public static void DuplicateSavedOwnershipLotsAreRejectedBeforeTrading()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;BuildingData b=Hall(w);UnitData seller=w.Units[1];
            Own(b,seller,ItemType.Log,.6f);Own(b,seller,ItemType.Log,.6f);
            Need(!w.Validate(out string reason)&&reason=="Invalid private goods lot.","Duplicate owner/item lots accepted before load");
        }
        public static void WorkshopConstructionDoesNotCountReservedPlanksAsBuildingBudget()
        {
            SimulationEngine e=Fixture();WorldState w=e.World;SettlementData s=w.Settlements[0];BuildingData hall=Hall(w),saw=Add(e,BuildingType.Sawmill,15,10);
            hall.Inventory[(int)ItemType.Log]=10;hall.Inventory[(int)ItemType.Plank]=12;hall.AgriculturalStock[(int)AgriculturalProduct.Fiber]=3;s.Wood=0;
            e.RequestMarketNeed(s.Id,ItemType.LinenCloth,2,"Нужна ткань");UnitData u=w.Units[0];
            Need((bool)Call(e,"TryChooseProductionOrder",u,s),"Planner mistook stored workshop planks for communal construction budget");
            Need(u.Task==UnitTask.Process||u.Task==UnitTask.Haul,"No actual preparation order");Near(0,s.Wood,"Remote/free construction credit");
            for(int i=0;i<800&&s.Wood<1;i++)WorkerTick(e,u);
            Need(s.Wood>=1,"No real hearth plank output");Near(12,hall.Inventory[(int)ItemType.Plank],"Reserved workshop planks stolen");Valid(w);
        }
        public static void PlannerExplainsMissingDepositAndCannotBypassResearch()
        {
            SimulationEngine e=Fixture(false);WorldState w=e.World;SettlementData s=w.Settlements[0];Add(e,BuildingType.Sawmill);Need(e.RequestMarketNeed(s.Id,ItemType.GoldOre,2,"Нужна руда"),"Need request");Call(e,"TryChooseProductionOrder",w.Units[0],s);
            MarketNeedData n=w.MarketNeeds.First(x=>x.Item==ItemType.GoldOre);Need(n.State==MarketNeedState.Blocked&&n.Reason.Contains("источника"),"Missing source unexplained");Near(0,Total(w,ItemType.GoldOre),"Planner made gold");
            e.RequestMarketNeed(s.Id,ItemType.IronTools,1,"Нужен инструмент");Call(e,"TryChooseProductionOrder",w.Units[0],s);Near(0,w.ProducedItems.Sum(),"Locked technology product");Need(w.Units.All(u=>u.Profession!=UnitProfession.Guard),"Planner created army");Valid(w);
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class MarketTests
    {
        [SetUp] public void Serializer(){MarketScenarios.Clone=w=>JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w));MarketScenarios.Snapshot=w=>JsonUtility.ToJson(w);}
        [Test] public void Coins(){MarketScenarios.ExactDenominationsAndChangeNeverMintMoney();}
        [Test] public void PhysicalBuyer(){MarketScenarios.RemoteOrderWaitsForDoorThenTransfersExactlyOnce();}
        [Test] public void Reservation(){MarketScenarios.LastPrivateGoodCannotBeReservedTwice();}
        [Test] public void Cancellation(){MarketScenarios.CancelledOrderReleasesStockAndPreservesBothPurses();}
        [Test] public void InvalidBuyer(){MarketScenarios.InsolventChildForeignAndMissingSupplyNeverCreateGoods();}
        [Test] public void LostBudget(){MarketScenarios.ChangedPaymentBudgetCancelsBeforeAnyGoodsDebit();}
        [Test] public void DeathAndDestruction(){MarketScenarios.DeadBuyerAndDemolishedShopReleasePendingReservations();}
        [Test] public void Save(){MarketScenarios.SaveDuringApproachContinuesWithSamePaymentAndTax();}
        [Test] public void Procurement(){MarketScenarios.TreasuryProcurementIsPaidFromExistingLocalCoins();}
        [Test] public void ShipImport(){MarketScenarios.MaritimeImportUsesRealHoldAndWholeCoinChange();}
        [Test] public void ShipExport(){MarketScenarios.MaritimeExportTaxAndDenominationsConserveBothSides();}
        [Test] public void Capacity(){MarketScenarios.ShipAndShoreCapacityFailAtomically();}
        [Test] public void RareGoods(){MarketScenarios.RareAgriculturalTradeRequiresAnActuallyCarriedRareItem();}
        [Test] public void HarborNetworkImport(){MarketScenarios.ConnectedHarborUsesCoinsAndSpaceAcrossRealWarehouses();}
        [Test] public void HarborNetworkExport(){MarketScenarios.HarborExportCanMakeChangeFromAnotherWarehouseAtomically();}
        [Test] public void FractionalCapacity(){MarketScenarios.HarborCannotSplitCoinAcrossFractionalFreeSpaces();}
        [Test] public void LegacyCoinFragments(){MarketScenarios.FractionalLegacyCoinLotsAreNotInventedWholePayments();}
        [Test] public void Wage(){MarketScenarios.MintPaysRealCompletedWorkButNoIdleStipend();}
        [Test] public void Migration(){MarketScenarios.MigrationCreatesNoCashAndRejectsForgedMarketState();}
        [Test] public void Prices(){MarketScenarios.FundedScarcityRaisesPriceSmoothlyWithoutCreatingStock();}
        [Test] public void AutonomousChain(){MarketScenarios.PlannedChainProducesAndEquipsToolsAndClothesWithoutIntermediateGrants();}
        [Test] public void SplitSavedLots(){MarketScenarios.DuplicateSavedOwnershipLotsAreRejectedBeforeTrading();}
        [Test] public void ConstructionBudget(){MarketScenarios.WorkshopConstructionDoesNotCountReservedPlanksAsBuildingBudget();}
        [Test] public void BlockedPlan(){MarketScenarios.PlannerExplainsMissingDepositAndCannotBypassResearch();}
    }
#endif
}
