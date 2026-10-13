using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using WorldGenesis.Simulation;

internal static class Program
{
    private sealed class Envelope { public int Schema=1; public string Format="WorldGenesis"; public WorldState World; }
    private static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true,IgnoreReadOnlyProperties=true};
    private static WorldState w;
    private static SimulationEngine e;
    private static SettlementData town;
    private static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static object Call(string name,params object[] args){return typeof(SimulationEngine).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(e,args);}
    private static BuildingData Build(BuildingType kind,int x,int y)
    {Need(e.TryBuildInfrastructure(town.Id,kind,x,y),"Cannot build "+kind+" at "+x+","+y);return w.GetBuilding(w.Cells[w.Index(x,y)].BuildingId);}
    private static ShipData Hull(ShipKind kind,BuildingData port,int x,int y,int sailors)
    {
        ShipData s=(ShipData)Call("CreateShip",kind,town.Id,w.Index(x,y));
        s.HomePortId=port.Id;s.Phase=ShipPhase.Stranded;
        foreach(UnitData u in w.Units.Where(u=>u.ShipId==0).Take(sailors).ToList())
        {u.ShipId=s.Id;u.X=s.X;u.Y=s.Y;u.Task=UnitTask.Sail;u.InsideBuildingId=u.EnteringBuildingId=0;u.Path.Clear();u.PathCursor=0;s.CrewIds.Add(u.Id);u.Profession=s.CaptainUnitId==0?UnitProfession.Captain:UnitProfession.Sailor;if(s.CaptainUnitId==0)s.CaptainUnitId=u.Id;}
        s.Food=s.Water=sailors*3;
        return s;
    }
    private static void Save(string name,object subjects)
    {
        Need(w.Validate(out string error),error);
        string output="Builds/MaritimeShowcase/"+name+".save.json";
        Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonSerializer.Serialize(new Envelope{World=w},Json));
        WorldState copy=JsonSerializer.Deserialize<Envelope>(File.ReadAllText(output),Json).World;Need(copy.Validate(out error),error);
        SimulationEngine continuation=new SimulationEngine(copy);
        for(int i=0;i<120;i++){continuation.Step(.1f);Need(copy.Validate(out error),name+" continuation "+i+": "+error);}
        string report="Evidence/Simulation/maritime-showcase-"+name+".json";
        Directory.CreateDirectory(Path.GetDirectoryName(report));
        File.WriteAllText(report,JsonSerializer.Serialize(new{utc=DateTime.UtcNow,output,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))),sourceTick=w.Tick,subjects,continuationTicks=120,validation=true,assistance="Authored coastal terrain, all research, finite building budget/stocks and prepared hulls/aboard crew/positions granted. Infrastructure uses paid public commands. Movement, trade, capture and provisions use production simulation. Ship construction and physical boarding separately tested; this scene does not prove natural progression."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(output);
    }
    private static int Main()
    {
        try
        {
            w=WorldGenerator.Generate(130800,128,128,WorldPreset.Island);e=new SimulationEngine(w);
            w.NaturalWildlife=false;w.Laws.Births=w.Laws.Aging=w.Laws.Warfare=false;w.Weather.Automatic=false;
            for(int y=0;y<128;y++)for(int x=0;x<76;x++)
            {
                int i=w.Index(x,y);bool sea=x<48;
                e.Paint(sea?(x<43?TerrainType.DeepWater:TerrainType.ShallowWater):TerrainType.Grass,x,y,0);
                w.Cells[i].Height=w.Cells[i].RouteHeight=sea?.18f:.57f;w.Cells[i].Temperature=.58f;w.Cells[i].Moisture=.61f;w.Cells[i].Vegetation=sea?0:.12f;
                w.Cells[i].WaterDepth=sea?(x<43?2:.8f):0;w.Cells[i].WaterKind=sea?WaterKind.Ocean:WaterKind.None;w.Cells[i].River=false;w.Cells[i].DrainageParent=-1;w.Infrastructure[i].Stream=0;
                w.ResourceNodes[i]=default(ResourceNodeData);BiomeCatalog.RefreshCell(w,i);
            }
            e.Spawn(SpeciesType.Human,62,64,36);town=w.GetSettlement(w.Units.First(u=>(int)u.Species<4).SettlementId);
            town.Name="Залив трёх парусов";town.Wood=2000;town.Food=300;town.UnlockedTechnologies=TechnologyCatalog.All.Select(t=>t.Id).ToList();town.ActiveTechnology="";town.ResearchProgress=0;
            town.NextPlanningTick=town.NextPolicyReviewTick=town.NextMaritimeConstructionTick=1000000;w.NextWandererTick=1000000;
            foreach(UnitData u in w.Units){u.Age=28;u.X=60.5f;u.Y=64.5f;u.Hunger=u.Thirst=u.Fatigue=u.SocialNeed=.02f;u.DecisionTimer=100000;u.Task=UnitTask.Idle;u.Path.Clear();u.PathCursor=0;}
            BuildingData hall=w.Buildings.First(b=>b.Type==BuildingType.Hall);hall.Inventory[(int)ItemType.RawStone]=120;hall.Inventory[(int)ItemType.Brick]=20;hall.WaterStored=25;
            for(int y=35;y<=95;y++)for(int x=48;x<76;x++)w.Cells[w.Index(x,y)].SettlementId=town.Id;
            BuildingData port=Build(BuildingType.Port,48,64),south=Build(BuildingType.Port,48,54),north=Build(BuildingType.Port,48,74);
            BuildingData a=Build(BuildingType.HarborWarehouse,50,64),b=Build(BuildingType.HarborWarehouse,53,64),c=Build(BuildingType.HarborWarehouse,56,64);
            Need(e.TryUpgradeBuilding(b.Id),"level2 warehouse");Need(e.TryUpgradeBuilding(c.Id)&&e.TryUpgradeBuilding(c.Id),"level3 warehouse");
            BuildingData yard=Build(BuildingType.Shipyard,48,68);Build(BuildingType.PortAuthority,50,66);Build(BuildingType.Well,56,69);
            Build(BuildingType.Carpentry,57,72);Build(BuildingType.Shop,57,59);
            a.PortFoodStored=a.WaterStored=150;a.Inventory[(int)ItemType.CopperCoin]=300;a.Inventory[(int)ItemType.Plank]=150;a.Inventory[(int)ItemType.IronOre]=60;a.Inventory[(int)ItemType.Salt]=15;
            b.Inventory[(int)ItemType.Furniture]=30;b.AgriculturalStock[(int)AgriculturalProduct.Grain]=120;c.Inventory[(int)ItemType.IronTools]=12;
            w.NavigationRevision++;w.TerrainRevision++;
            Need(e.HarborCapacity(port.Id)==35000,"warehouse chain capacity");
            ShipData boat=Hull(ShipKind.FishingBoat,port,43,57,1),cutter=Hull(ShipKind.FishingCutter,port,41,61,2),trawler=Hull(ShipKind.FishingTrawler,port,43,65,3);
            ShipData transport=Hull(ShipKind.Transport,port,40,70,2),merchant=Hull(ShipKind.Merchant,port,43,74,3),warship=Hull(ShipKind.Warship,port,40,78,6);
            merchant.Cargo[(int)ItemType.Plank]=20;merchant.Cargo[(int)ItemType.CopperCoin]=50;transport.Cargo[(int)ItemType.Furniture]=12;warship.Cargo[(int)ItemType.BallistaBolts]=30;
            foreach(int id in warship.CrewIds){w.GetUnit(id).NavalWeapon=ItemType.Sword;w.GetUnit(id).NavalArmor=ItemType.Armor;}
            foreach(ShipData s in w.Ships)Need(e.TrySailToPort(s.Id,s.Kind==ShipKind.Transport||s.Kind==ShipKind.Warship?north.Id:south.Id),"sail "+s.Kind);
            Save("fleet",new{port=port.Id,warehouseLevels=new[]{a.Level,b.Level,c.Level},capacity=e.HarborCapacity(port.Id),ships=w.Ships.Select(s=>new{s.Id,kind=s.Kind.ToString(),s.X,s.Y})});
            // Merchant preparation is a controlled reposition for a separate docked transaction view.
            merchant.X=port.BerthCell%w.Width+.5f;merchant.Y=port.BerthCell/w.Width+.5f;merchant.Phase=ShipPhase.Docked;merchant.DockedPortId=merchant.HomePortId=port.Id;merchant.Path.Clear();merchant.PathCursor=0;
            foreach(int id in merchant.CrewIds){w.GetUnit(id).X=merchant.X;w.GetUnit(id).Y=merchant.Y;}
            Need(e.TryTransferShipCargo(merchant.Id,c.Id,ItemType.IronTools,5,true),"network load");Need(e.TryTransferShipProvisions(merchant.Id,a.Id,4,4,true),"network provisions");
            Save("harbor",new{inspectShip=merchant.Id,inspectPort=port.Id,warehouse=c.Id,capacity=e.HarborCapacity(port.Id)});
            Need(e.TryStartForeignVoyage(merchant.Id),"outbound foreign voyage");Save("voyage",new{inspectShip=merchant.Id});
            Need(e.TryInviteWanderer(port.Id,ShipKind.WandererLarge),"foreign arrival");ShipData visitor=w.Ships.Last();
            for(int i=0;i<2000&&visitor.Phase!=ShipPhase.Docked;i++)e.Step(.1f);
            Need(visitor.Phase==ShipPhase.Docked,"foreign dock");
            Save("visitor",new{inspectShip=visitor.Id,capacity=MaritimeCatalog.Get(visitor.Kind).Capacity,port=port.Id});
            w.Laws.Warfare=true;warship.X=45.5f;warship.Y=64.5f;warship.Phase=ShipPhase.Stranded;warship.Path.Clear();warship.PathCursor=0;
            foreach(int id in warship.CrewIds){w.GetUnit(id).X=warship.X;w.GetUnit(id).Y=warship.Y;}
            Need(e.TryAttackShip(warship.Id,visitor.Id,true),"naval assault");Save("engagement",new{inspectShip=warship.Id,target=visitor.Id});
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
