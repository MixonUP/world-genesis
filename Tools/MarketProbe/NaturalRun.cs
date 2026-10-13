using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using WorldGenesis.Simulation;

internal static class NaturalRun
{
    internal static int Run(string[] args)
    {
        string path = args.Length > 1 ? args[1] : "Evidence/Simulation/market-natural.json";
        int seconds = args.Length > 2 ? int.Parse(args[2]) : 1800;
        if (seconds < 1 || seconds > 7200) throw new ArgumentException("Simulated duration must be1..7200 seconds");
        Dictionary<string, string> before = Program.Sources(); List<object> results = new List<object>(); Stopwatch timer = Stopwatch.StartNew(); bool valid = true, complete = true;
        int[] seeds = args.Length > 3 ? new[] { int.Parse(args[3]) } : new[] { 1618033, 271828 };
        foreach (int seed in seeds)
        {
            try { results.Add(Seed(seed, seconds, 180, out bool ok, out bool ended)); valid &= ok; complete &= ended; }
            catch (Exception ex) { valid = complete = false; results.Add(new { seed, error = ex.ToString(), passed = false }); }
        }
        Dictionary<string, string> after = Program.Sources(); string[] changed = before.Keys.Union(after.Keys).Where(p => !before.TryGetValue(p, out string a) || !after.TryGetValue(p, out string b) || a != b).ToArray();
        Program.Save(path, new { utc = DateTime.UtcNow, requestedSimulatedSecondsPerSeed = seconds, wallBudgetSecondsPerSeed = 180, actualWallSeconds = timer.Elapsed.TotalSeconds, allInvariantsPassed = valid, allRequestedDurationsCompleted = complete,
            sourceUnchanged = changed.Length == 0, sourceChangedDuringRun = changed, sourceSHA256 = before, assemblySHA256 = Program.Hash(Assembly.GetExecutingAssembly().Location),
            kind = "Authored offline .NET natural simulation; not native FPS, prepared showcase, or full natural technology-tree proof",
            startup = "Island128x128; ordinary nearest walkable non-snow positions to GameBootstrap four anchors; Spawn9 citizens and3 sheep per race; no post-start overrides/grants of resources, technologies, profession, equipment, needs, law or weather. Only Step(.1).", results });
        Console.WriteLine("Natural receipt=" + path + "; invariants=" + valid + "; complete=" + complete + "; sourceUnchanged=" + (changed.Length == 0));
        return valid && changed.Length == 0 ? complete ? 0 : 2 : 1;
    }
    private static object Seed(int seed, int seconds, double budget, out bool valid, out bool complete)
    {
        Stopwatch timer = Stopwatch.StartNew(); WorldState w = WorldGenerator.Generate(seed, 128, 128); SimulationEngine e = new SimulationEngine(w);
        int[] xs = {42,85,42,85}, ys = {42,85,85,42}; List<object> commands = new List<object>();
        for (int race = 0; race < 4; race++)
        {
            int bx = -1, by = -1; float best = float.MaxValue;
            for (int y = 4; y < w.Height - 4; y++) for (int x = 4; x < w.Width - 4; x++)
            { if (!w.IsWalkable(x, y) || w.Cells[w.Index(x,y)].Terrain == TerrainType.Snow) continue; float distance = (x-xs[race])*(x-xs[race])+(y-ys[race])*(y-ys[race]); if (distance < best) { best = distance; bx=x;by=y; } }
            if (bx < 0) continue; e.Spawn((SpeciesType)race,bx,by,9);e.Spawn(SpeciesType.Sheep,bx+3,by+2,3);commands.Add(new {race=((SpeciesType)race).ToString(),x=bx,y=by,citizens=9,sheep=3});
        }
        int checks=1, peak=w.Units.Count; long loads=0; string failure=null; valid=w.Validate(out failure);
        Dictionary<string,long> firstProduct=new Dictionary<string,long>(), firstBuilding=new Dictionary<string,long>(), firstTechnology=new Dictionary<string,long>();
        Dictionary<string,long> adultTaskTicks=new Dictionary<string,long>(), needBlockedSeconds=new Dictionary<string,long>();
        long hungryAdultTicks=0,thirstyAdultTicks=0,adultTicks=0,firstToolIssuedTick=0,firstClothesIssuedTick=0;
        List<object> timeline=new List<object>();timeline.Add(Snapshot(w));
        while(valid && w.Tick < seconds*10 && timer.Elapsed.TotalSeconds < budget)
        {
            e.Step(.1f);peak=Math.Max(peak,w.Units.Count);
            if(firstToolIssuedTick==0&&w.WorkToolsIssued>0)firstToolIssuedTick=w.Tick;if(firstClothesIssuedTick==0&&w.ClothesIssued>0)firstClothesIssuedTick=w.Tick;
            foreach(UnitData u in w.Units)if(SimulationEngine.IsCivilized(u.Species)&&u.Age>=16){adultTicks++;if(u.Hunger>.72f)hungryAdultTicks++;if(u.Thirst>.85f)thirstyAdultTicks++;string key=u.SettlementId+":"+u.Task;adultTaskTicks.TryGetValue(key,out long old);adultTaskTicks[key]=old+1;}
            if(w.Tick%10==0)foreach(MarketNeedData need in w.MarketNeeds){string key=need.SettlementId+":"+need.Item+":"+need.State+":"+need.Reason;needBlockedSeconds.TryGetValue(key,out long old);needBlockedSeconds[key]=old+1;}
            foreach(UnitData u in w.Units)
            { float load=SimulationEngine.CarriedLoad(u), capacity=SimulationEngine.CarryCapacityFor(u);loads++;if(!float.IsFinite(load)||!float.IsFinite(capacity)||load<0||load>capacity+.0001f){valid=false;failure="Carrier "+u.Id+" at "+w.Tick+" load "+load+"/"+capacity;break;} }
            if(w.Tick%100==0){checks++;valid &= w.Validate(out string error);if(error!=null)failure=error;}
            foreach(IndustryRecipe r in IndustryCatalog.All)if(w.ProducedItems[(int)r.Output]>0 && !firstProduct.ContainsKey(r.Output.ToString()))firstProduct[r.Output.ToString()]=w.Tick;
            foreach(BuildingData b in w.Buildings)if(IndustryCatalog.IsWorkshop(b.Type)&&!firstBuilding.ContainsKey(b.Type.ToString()))firstBuilding[b.Type.ToString()]=w.Tick;
            foreach(SettlementData s in w.Settlements)foreach(string tech in s.UnlockedTechnologies)if(!firstTechnology.ContainsKey(tech))firstTechnology[tech]=w.Tick;
            if(w.Tick%3000==0)timeline.Add(Snapshot(w));
        }
        checks++;valid &= w.Validate(out string ending);if(ending!=null)failure=ending; complete=w.Tick>=seconds*10;
        string failedWorld = null;
        if (!valid) { failedWorld = "Builds/MarketProbeFailures/seed-" + seed + "-tick-" + w.Tick + ".json"; Program.Save(failedWorld, w); }
        string exportedWorld = null, exportedWorldSHA256 = null; long exportedBytes = 0;
        if (valid)
        {
            exportedWorld = "Builds/MarketNatural/seed-" + seed + "-tick-" + w.Tick + ".world.json";
            Program.Save(exportedWorld, new { Schema = 1, Format = "WorldGenesis", World = w });
            exportedWorldSHA256 = Program.Hash(exportedWorld); exportedBytes = new FileInfo(exportedWorld).Length;
            if (exportedBytes > 64L * 1024 * 1024) throw new InvalidOperationException("Export exceeds SaveStore size limit");
        }
        if(w.Tick%3000!=0)timeline.Add(Snapshot(w));
        Console.WriteLine("Natural "+seed+" tick="+w.Tick+" batches="+w.IndustryBatchesCompleted+" valid="+valid);
        return new {seed,commands,simulatedSeconds=w.Tick*.1f,wallSeconds=timer.Elapsed.TotalSeconds,completed=complete,invariantsPassed=valid,failure,failedWorld,exportedWorld,exportedWorldSHA256,exportedBytes,
            suspiciousResourceJobs=w.Units.Where(u=>(u.SourceBuildingId!=0&&w.GetBuilding(u.SourceBuildingId)==null)||(u.TargetBuildingId!=0&&w.GetBuilding(u.TargetBuildingId)==null)||(u.CargoAmount==0&&u.CargoKind!=ItemType.None)||(u.CargoAmount>0&&u.CargoKind==ItemType.None)).Select(u=>new{u.Id,task=u.Task.ToString(),u.SourceBuildingId,u.TargetBuildingId,u.CargoAmount,cargo=u.CargoKind.ToString(),u.X,u.Y}).ToArray(),
            worldValidations=checks,carrierChecks=loads,peakCreatures=peak,firstProductTick=firstProduct,firstWorkshopTick=firstBuilding,firstTechnologyTick=firstTechnology,firstToolIssuedTick,firstClothesIssuedTick,
            adultResidentSeconds=adultTicks*.1, hungryAdultResidentSeconds=hungryAdultTicks*.1,thirstyAdultResidentSeconds=thirstyAdultTicks*.1,adultTaskResidentSeconds=adultTaskTicks.ToDictionary(p=>p.Key,p=>p.Value*.1),sampledNeedStateSeconds=needBlockedSeconds,timeline,final=Snapshot(w)};
    }
    private static object Snapshot(WorldState w)
    {
        return new {tick=w.Tick,creatures=w.Units.Count,citizens=w.Units.Count(u=>SimulationEngine.IsCivilized(u.Species)),batches=w.IndustryBatchesCompleted,workToolsIssued=w.WorkToolsIssued,clothesIssued=w.ClothesIssued,furnitureInstalled=w.FurnitureInstalled,
            marketTransactions=w.MarketTransactions,marketWages=w.MarketCopperWages,marketTaxes=w.MarketCopperTaxes,marketNeeds=w.MarketNeeds.Select(n=>new{n.SettlementId,item=n.Item.ToString(),n.Target,state=n.State.ToString(),n.Reason}).ToArray(),
            produced=IndustryCatalog.All.ToDictionary(r=>r.Output.ToString(),r=>w.ProducedItems[(int)r.Output]),mint=IndustryCatalog.All.Where(r=>IndustryCatalog.IsCoin(r.Output)).ToDictionary(r=>r.Output.ToString(),r=>w.MintIssued[(int)r.Output]),
            tasks=w.Units.GroupBy(u=>u.Task).ToDictionary(g=>g.Key.ToString(),g=>g.Count()),settlements=w.Settlements.Select(s=>new {s.Id,s.Name,s.Population,s.Food,s.Wood,s.ResearchRate,s.ActiveTechnology,s.ResearchProgress,tech=s.UnlockedTechnologies.ToArray(),
                workshops=w.Buildings.Where(b=>b.SettlementId==s.Id&&IndustryCatalog.IsWorkshop(b.Type)).Select(b=>new {b.Id,b.X,b.Y,type=b.Type.ToString(),recipe=b.ActiveIndustryRecipe.ToString(),b.IndustryWorkerId,b.IndustryProgress,b.CompletedIndustryBatches,waiting=IndustryCatalog.WaitingReason(w,b)}).ToArray(),
                materialStocks=Enumerable.Range(1,ResourceCatalog.ItemCount-1).Select(i=>new {item=((ItemType)i).ToString(),amount=w.GetSettlementStock(s.Id,(ItemType)i)}).Where(v=>v.amount>0).ToArray(),
                agriculturalStocks=Enumerable.Range(1,FishingCatalog.AgriculturalProductCount-1).Select(i=>new {item=((AgriculturalProduct)i).ToString(),amount=w.GetAgriculturalStock(s.Id,(AgriculturalProduct)i)}).Where(v=>v.amount>0).ToArray()
            }).ToArray()};
    }
}
