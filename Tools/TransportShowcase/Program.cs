using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using WorldGenesis.Simulation;

// Controlled visual fixture. All assistance is listed in the receipt; construction uses ordinary ticks.
internal static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, IgnoreReadOnlyProperties = true };
    private sealed class Envelope { public int Schema = 1; public string Format = "WorldGenesis"; public WorldState World; }
    private static readonly List<object> Grants = new List<object>();
    private static readonly List<object> Jobs = new List<object>();
    private static readonly List<object> Deliveries = new List<object>();
    private static readonly List<string> TechnologyGrants = new List<string>();
    private static WorldState world;
    private static SimulationEngine engine;
    private static SettlementData town;
    private static BuildingData hall;
    private static UnitData builder;
    private static float walkedDistance;
    private static float maxCarry;
    private static int validTicks;

    private static object Call(string name, params object[] args) => typeof(SimulationEngine).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(engine, args);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Material(ItemType kind, float amount)
    {
        if (kind == ItemType.Plank) town.Wood += amount; else hall.Inventory[(int)kind] += amount;
        Grants.Add(new { tick = world.Tick, kind = kind.ToString(), amount, location = new[] { hall.X, hall.Y }, reason = "Explicit assisted construction bill; actual hauling and consumption follow." });
    }
    private static void Technology(string id)
    {
        if(town.UnlockedTechnologies.Contains(id))return;
        foreach(string prerequisite in TechnologyCatalog.Get(id).Prerequisites)Technology(prerequisite);
        town.UnlockedTechnologies.Add(id);TechnologyGrants.Add(id);
    }
    private static void Step()
    {
        float x = builder.X, y = builder.Y;
        var prior = world.TransportProjects.Select(p => new { project = p, delivered = p.Delivered }).ToArray();
        engine.Step(.1f);
        walkedDistance += (float)Math.Sqrt((builder.X-x)*(builder.X-x)+(builder.Y-y)*(builder.Y-y));
        float load = SimulationEngine.CarriedLoad(builder); maxCarry = Math.Max(maxCarry, load);
        Require(load <= 1.00001f, "Bare-handed builder exceeded one physical unit.");
        foreach (var pair in prior)
            if (pair.project.Delivered > pair.delivered)
                Deliveries.Add(new { tick = world.Tick, projectId = pair.project.Id, kind = pair.project.Material.ToString(), amount = pair.project.Delivered-pair.delivered, builderX = builder.X, builderY = builder.Y, bank = pair.project.BankCell });
        Require(world.Validate(out string error), "Tick " + world.Tick + ": " + error); validTicks++;
    }
    private static void Complete(string label, Func<bool> plan)
    {
        Require(plan(), label + " plan rejected.");
        TransportProjectData p = world.TransportProjects[0]; long start = world.Tick;
        // Selecting the worker is a disclosed debug command. Movement/work/needs remain production Step(.1).
        Call("ChooseTransportWork", builder, town);
        while (world.TransportProjects.Any(project => project.Id == p.Id) && world.Tick-start < 12000) Step();
        Require(world.TransportProjects.All(project => project.Id != p.Id), label + " timed out.");
        Require(p.Delivered == p.Required && p.Cells.All(cell => p.Bridge ? world.Infrastructure[cell].BridgeLevel == p.Level : world.Infrastructure[cell].RoadLevel == p.Level), label + " cancelled instead of completing.");
        Jobs.Add(new { label, projectId = p.Id, startTick = start, completedTick = world.Tick, material = p.Material.ToString(), required = p.Required, physicallyDelivered = p.Delivered, actualWorkSeconds = p.WorkSeconds, cells = p.Cells, level = p.Level });
    }
    private static void Tile(int x, int y, TerrainType terrain, float height)
    {
        engine.Paint(terrain, x, y, 0); int i = world.Index(x,y); world.Cells[i].Height = height;
        world.Cells[i].Moisture = .7f; world.Cells[i].Temperature = .6f;
    }
    private static void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonSerializer.Serialize(world, Json));
        File.WriteAllText(Path.ChangeExtension(path, ".save.json"), JsonSerializer.Serialize(new Envelope { World = world }, Json));
    }
    private static int Main(string[] args)
    {
        string prefix = args.Length > 0 ? args[0] : "Builds/transport-assisted-showcase-v050";
        string reportPath = args.Length > 1 ? args[1] : "Evidence/Simulation/transport-assisted-showcase-v050.json";
        world = WorldGenerator.Generate(713,128,128,WorldPreset.Island);
        world.NaturalWildlife = false; engine = new SimulationEngine(world);
        world.Laws.Births = world.Laws.Aging = world.Laws.Warfare = false;
        // Local authored river valley, 37*31 cells; the rest retains the generated seed geography.
        for (int y=49;y<=79;y++) for(int x=46;x<=82;x++) Tile(x,y,TerrainType.Grass,.58f);
        // Continuous river plus a shallower ford south of the bridge. These are explicitly authored cells.
        for(int y=49;y<=79;y++) for(int x=64;x<=66;x++)
        {
            Tile(x,y,TerrainType.ShallowWater,.49f); int i=world.Index(x,y);
            world.Cells[i].River=true; world.Cells[i].WaterKind=WaterKind.River; world.Cells[i].WaterDepth=y==73?.45f:1.35f;
            world.Cells[i].CurrentStrength=y==73?.35f:.5f; world.Cells[i].ChannelWidth=3;
        }
        for(int y=52;y<=58;y++) for(int x=72;x<=78;x++) if((x-75)*(x-75)+(y-55)*(y-55)<=13) Tile(x,y,TerrainType.Highlands,.70f);
        for(int y=66;y<=73;y++) for(int x=73;x<=79;x++) if((x-76)*(x-76)+(y-69)*(y-69)<=12) Tile(x,y,TerrainType.Hills,.64f);
        for(int y=57;y<=68;y++) for(int x=70;x<=71;x++) Tile(x,y,TerrainType.Ravine,.35f);
        for(int y=72;y<=76;y++) for(int x=58;x<=61;x++) Tile(x,y,TerrainType.Swamp,.50f);
        Tile(58,64,TerrainType.Hills,.68f); // Reachable dry founding elevation beside fresh water.
        world.NavigationRevision++;
        engine.Spawn(SpeciesType.Human,58,64,1); Require(world.Settlements.Count==1,"No showcase settlement.");
        town=world.Settlements[0]; town.Name="Речная пристань"; hall=world.Buildings.First(b=>b.Type==BuildingType.Hall&&b.SettlementId==town.Id);
        builder=world.Units.First(u=>u.SettlementId==town.Id); builder.Name="Мира — строитель переправы"; builder.Sex=Sex.Female; builder.Age=29;
        builder.Hunger=builder.Thirst=builder.Fatigue=builder.SocialNeed=.05f;
        town.Food=100; town.NextPolicyReviewTick=town.NextPlanningTick=1000000;
        town.EconomyPolicy=EconomicPolicy.FreeDistribution;
        // No free basket/mount/cart: a person makes repeated one-unit trips.
        foreach(string tech in new[]{"l06_paths","l09_gravel_roads","l07_paved_roads","l11_wooden_bridges"})
            Technology(tech);
        Grants.Add(new{tick=world.Tick,kind="Food",amount=72,location=new[]{hall.X,hall.Y},reason="Founding Food28 raised to100 for the controlled construction demonstration."});
        // Build the span before roads: repeated trips create genuine level1 footpaths.
        Material(ItemType.Plank,12); Complete("Wooden river bridge, 3 real spans",()=>engine.TryPlanBridge(town.Id,63,64,0,false));
        Save(prefix+"-bridge-complete.json");
        // Three short adjacent road bands. Ownership is claimed by the normal town-radius helper.
        Call("ClaimTerritory",town,12);
        for(byte level=2;level<=4;level++)
        {
            int row=60+(level-2)*2;
            for(int x=59;x<=62;x++)
            {
                if(world.Cells[world.Index(x,row)].BuildingId!=0) continue;
                ItemType kind=level==2?ItemType.Plank:level==3?ItemType.RawStone:ItemType.StoneTile;
                Material(kind,level==2?.25f:1f);
                int roadX=x,roadY=row; byte roadLevel=level;
                Complete("Road level "+level+" at "+x+","+row,()=>engine.TryPlanRoad(town.Id,roadX,roadY,roadLevel));
            }
        }
        // A second genuine project is saved after one delivery and a subsequent pickup.
        Material(ItemType.Plank,12); Require(engine.TryPlanBridge(town.Id,63,59,0,false),"Second bridge plan rejected.");
        TransportProjectData active=world.TransportProjects[0]; Call("ChooseTransportWork",builder,town);long secondStart=world.Tick;
        while(!(active.Delivered>=1&&builder.CargoAmount>0&&builder.Task==UnitTask.BuildTransport)&&world.Tick-secondStart<6000)Step();
        Require(active.Delivered>=1&&builder.CargoAmount>0&&builder.TransportProjectId==active.Id,"No in-flight material for second project.");
        int[] roadCounts=new int[5];foreach(var c in world.Infrastructure)if(c.RoadLevel>0)roadCounts[c.RoadLevel]++;
        Require(Enumerable.Range(1,4).All(level=>roadCounts[level]>0),"Not all four actual road levels are visible.");
        Save(prefix+".json");
        string saved=JsonSerializer.Serialize(world,Json);var clone=new SimulationEngine(JsonSerializer.Deserialize<WorldState>(saved,Json));
        for(int i=0;i<100;i++){engine.Step(.1f);clone.Step(.1f);}
        Require(JsonSerializer.Serialize(world,Json)==JsonSerializer.Serialize(clone.World,Json),"Showcase load continuation diverged.");
        Require(world.Validate(out string finalError),finalError);
        var receipt=new
        {
            utc=DateTime.UtcNow,kind="assisted visual/save fixture; not evidence of natural progression",seed=713,width=128,height=128,
            authoredRectangle=new{x0=46,y0=49,x1=82,y1=79,cells=1147},
            assistance=new{terrainAuthored=true,adultSpawned=1,initialAgeAndNeedsSet=true,technologyIds=TechnologyGrants,claimedRadius=12,policyReviewDelayed=true,automaticPlanningDelayed=true,workerSelectionViaDebugCommand=true,birthsAgingWarfareAndNaturalWildlifeDisabled=true,simulationStepSeconds=.1f,workTimeAcceleration=false,movementTeleport=false,capacityEquipmentGranted=false},
            grants=Grants,completedConstruction=Jobs,physicalDeliveries=Deliveries,bareHandedMaxLoad=maxCarry,builderDistanceTravelled=walkedDistance,validatedTicks=validTicks,
            roadCounts,saveTick=JsonSerializer.Deserialize<WorldState>(saved,Json).Tick,saveCurrentProject=active.Id,saveCurrentProjectDelivered=JsonSerializer.Deserialize<WorldState>(saved,Json).TransportProjects[0].Delivered,
            rawWorldSave=prefix+".json",saveStoreEnvelope=prefix+".save.json",bridgeCompleteSave=prefix+"-bridge-complete.json",
            sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(prefix+".json"))),deterministicContinuationTicks=100,
            inspectCells=new{overview=new[]{64,63},completedBridge=new[]{65,64},activeConstruction=new[]{63,59},ford=new[]{65,73},ravine=new[]{70,63},highlands=new[]{75,55},hill=new[]{76,69},swamp=new[]{59,74}},nativeScreenshot="not-run by this tool"
        };
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));File.WriteAllText(reportPath,JsonSerializer.Serialize(receipt,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        Console.WriteLine("PASS: "+Jobs.Count+" completed projects, "+Deliveries.Count+" actual deliveries, all four road levels, in-flight project, 100 deterministic continuation ticks. Save: "+prefix+".json");return 0;
    }
}
