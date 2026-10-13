using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorldGenesis.Simulation;
#if SPEED_CORRECTNESS
using WorldGenesis.Tests;
#endif

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = false };
    internal static string Hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
    internal static string StateHash(WorldState w) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(w, Json))));
    internal static Dictionary<string,string> Sources(string root) => Directory.GetFiles(root,"*.cs",SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.Ordinal).ToDictionary(p=>Path.GetRelativePath(root,p).Replace('\\','/'),Hash);
    internal static void Save(string p,object data) { Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,JsonSerializer.Serialize(data,new JsonSerializerOptions(Json){WriteIndented=true})); }
    private static int Main(string[] args)
    {
#if SPEED_CORRECTNESS
        if(args.Length>0 && args[0]=="--tests") return Tests(args);
#endif
        if(args.Length>0 && args[0]=="--export-stress") return NativeSpeedWorlds.Export(args);
        string output=args.Length>0?args[0]:"Evidence/Simulation/speed-baseline.json";
        string source=args.Length>1?args[1]:"WorldGenesis/Assets/_Project/Scripts/Simulation";
        int ticks=args.Length>2?int.Parse(args[2]):300;
        int only=args.Length>3?int.Parse(args[3]):0;
        var before=Sources(source);List<object> results=new List<object>();int failed=0;
        // Warmup is a different world and is never used as an experimental initial state.
        SimulationEngine warm=Create(WorldPreset.Island,100,1618033);for(int i=0;i<30;i++)warm.Step(.1f);
        foreach(WorldPreset preset in Enum.GetValues(typeof(WorldPreset))) foreach(int count in new[]{100,1000})
        {
            if(only!=0&&(count!=only||preset!=WorldPreset.Island))continue;
            try{results.Add(Run(preset,count,ticks));}catch(Exception ex){failed++;results.Add(new{preset=preset.ToString(),count,error=ex.ToString()});Console.WriteLine("FAIL "+preset+" "+count+" "+ex.Message);}
            Save(output,new{utc=DateTime.UtcNow,kind="Offline .NET exact fixed-tick benchmark; not Unity FPS",requestedTicks=ticks,failures=failed,sourceSHA256=before,assemblySHA256=Hash(Assembly.GetExecutingAssembly().Location),results});
        }
        var after=Sources(source);bool unchanged=before.Count==after.Count&&before.All(p=>after.TryGetValue(p.Key,out string h)&&h==p.Value);
        Save(output,new{utc=DateTime.UtcNow,kind="Offline .NET exact fixed-tick benchmark; not Unity FPS",requestedTicks=ticks,failures=failed,sourceUnchanged=unchanged,sourceSHA256=before,assemblySHA256=Hash(Assembly.GetExecutingAssembly().Location),runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,processorCount=Environment.ProcessorCount,tieredCompilation=Environment.GetEnvironmentVariable("DOTNET_TieredCompilation")??"default",results,trace=SpeedProbeTrace.Report()});
        return failed==0&&unchanged?0:1;
    }
#if SPEED_CORRECTNESS
    private static int Tests(string[] args)
    {
        SimulationPerformanceCorrectnessScenarios.Clone=w=>JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w,Json),Json);
        SimulationPerformanceCorrectnessScenarios.Snapshot=w=>JsonSerializer.Serialize(w,Json);
        var before=Sources("WorldGenesis/Assets/_Project/Scripts/Simulation");var results=new List<object>();int failed=0;
        foreach(MethodInfo m in typeof(SimulationPerformanceCorrectnessScenarios).GetMethods(BindingFlags.Public|BindingFlags.Static))
        {
            if(m.ReturnType!=typeof(void)||m.GetParameters().Length!=0)continue;
            try{m.Invoke(null,null);results.Add(new{name=m.Name,passed=true});Console.WriteLine("PASS "+m.Name);}
            catch(Exception ex){failed++;results.Add(new{name=m.Name,passed=false,error=(ex.InnerException??ex).ToString()});Console.WriteLine("FAIL "+m.Name+": "+(ex.InnerException??ex));}
        }
        var after=Sources("WorldGenesis/Assets/_Project/Scripts/Simulation");bool unchanged=before.Count==after.Count&&before.All(p=>after.TryGetValue(p.Key,out string h)&&h==p.Value);
        Save(args.Length>1?args[1]:"Evidence/Simulation/speed-correctness.json",new{utc=DateTime.UtcNow,tests=results.Count,failures=failed,sourceUnchanged=unchanged,sourceSHA256=before,results});return failed==0&&unchanged?0:1;
    }
#endif
    internal static SimulationEngine Create(WorldPreset preset,int count,int seed)
    {
        WorldState w=WorldGenerator.Generate(seed,128,128,preset);SimulationEngine e=new SimulationEngine(w);
        int[] xs={42,85,42,85},ys={42,85,85,42};
        for(int race=0;race<4;race++)
        {
            int best=-1;float distance=float.MaxValue;
            for(int i=0;i<w.Cells.Length;i++){int x=i%w.Width,y=i/w.Width;if(x<4||y<4||x>=124||y>=124||!w.IsWalkable(x,y)||w.Cells[i].Terrain==TerrainType.Snow)continue;float d=(x-xs[race])*(x-xs[race])+(y-ys[race])*(y-ys[race]);if(d<distance){distance=d;best=i;}}
            if(best<0)throw new Exception("No spawn position");e.Spawn((SpeciesType)race,best%w.Width,best/w.Width,count/4);
        }
        return e;
    }
    private static object Run(WorldPreset preset,int count,int ticks)
    {
        var e=Create(preset,count,1618033);WorldState w=e.World;if(!w.Validate(out string error))throw new Exception(error);
        string initial=StateHash(w);int actual=w.Units.Count;long startTick=w.Tick;double[] times=new double[ticks];int gc=GC.CollectionCount(0);long alloc=GC.GetAllocatedBytesForCurrentThread();long total=0;int checks=1;
        for(int i=0;i<ticks;i++){long t=Stopwatch.GetTimestamp();e.Step(.1f);long elapsed=Stopwatch.GetTimestamp()-t;total+=elapsed;times[i]=elapsed*1000d/Stopwatch.Frequency;if(i%100==99){checks++;if(!w.Validate(out error))throw new Exception("Tick"+w.Tick+": "+error);}}
        alloc=GC.GetAllocatedBytesForCurrentThread()-alloc;gc=GC.CollectionCount(0)-gc;if(!w.Validate(out error))throw new Exception(error);
        string final=StateHash(w);Array.Sort(times);double wall=total/(double)Stopwatch.Frequency;
        // Microbenchmarks do not mutate the world's RNG or serialized state.
        long checksum=0;Stopwatch sw=Stopwatch.StartNew();for(int i=0;i<200000;i++)checksum+=w.GetUnit(w.Units[i%w.Units.Count].Id).Id;double lookup=sw.Elapsed.TotalMilliseconds;
        sw.Restart();for(int i=0;i<200000;i++)checksum+=w.GetBuilding(w.Buildings[i%w.Buildings.Count].Id).Id;double buildings=sw.Elapsed.TotalMilliseconds;
        GridNavigation nav=new GridNavigation(w);List<int> route=new List<int>();int[] walk=Enumerable.Range(0,w.Cells.Length).Where(i=>w.IsWalkable(i%w.Width,i/w.Width)).ToArray();sw.Restart();int found=0;long pathHash=17;
        for(int i=0;i<200;i++){int a=walk[(i*67+13)%walk.Length],b=walk[(i*137+257)%walk.Length];if(nav.Find(a,b,route)){found++;foreach(int cell in route)pathHash=unchecked(pathHash*31+cell);}else pathHash=unchecked(pathHash*31-1);}
        double navigation=sw.Elapsed.TotalMilliseconds;
        if(initial==final||w.Tick-startTick!=ticks)throw new Exception("No exact-tick advance");if(StateHash(w)!=final)throw new Exception("Lookup mutated serialized world");
        Console.WriteLine(preset+" "+count+" -> "+w.Units.Count+" ticks="+ticks+" sim/wall="+(ticks*.1/wall).ToString("F2")+" p99ms="+times[(int)(.99*(ticks-1))].ToString("F3"));
        return new{preset=preset.ToString(),seed=1618033,width=128,height=128,requestedPopulation=count,initialPopulation=actual,finalPopulation=w.Units.Count,buildings=w.Buildings.Count,startTick,endTick=w.Tick,simulatedSeconds=ticks*.1,wallSeconds=wall,simulationSecondsPerWallSecond=ticks*.1/wall,meanTickMs=wall*1000/ticks,p50TickMs=times[(int)(.50*(ticks-1))],p95TickMs=times[(int)(.95*(ticks-1))],p99TickMs=times[(int)(.99*(ticks-1))],maxTickMs=times[ticks-1],allocatedBytes=alloc,gen0Collections=gc,validations=checks+1,initialSHA256=initial,finalSHA256=final,micro=new{queries=200000,unitLookupMs=lookup,buildingLookupMs=buildings,checksum,navigationQueries=200,pathsFound=found,navigationMs=navigation,pathHash},assistance="Ordinary Spawn commands create four large starting towns; no technologies/materials/needs/laws granted. This is a load scenario, not natural population growth."};
    }
}
internal static class SpeedProbeTrace
{
    private static readonly Dictionary<string,(long calls,long elapsed)> stats=new Dictionary<string,(long,long)>();
    public static long Begin()=>Stopwatch.GetTimestamp();
    public static void End(string name,long started){stats.TryGetValue(name,out var s);stats[name]=(s.calls+1,s.elapsed+Stopwatch.GetTimestamp()-started);}
    public static object Report()=>stats.ToDictionary(p=>p.Key,p=>new{calls=p.Value.calls,inclusiveMilliseconds=p.Value.elapsed*1000d/Stopwatch.Frequency});
}
