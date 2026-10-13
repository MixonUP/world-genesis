using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using WorldGenesis.Simulation;

internal static class NativeSpeedWorlds
{
    internal static int Export(string[] args)
    {
        string output=args.Length>1?args[1]:"Evidence/Simulation/native-speed-worlds.json";
        var sources=Program.Sources("WorldGenesis/Assets/_Project/Scripts/Simulation");var results=new List<object>();
        foreach(var requested in new[]{(WorldPreset.Island,100),(WorldPreset.Island,1000),(WorldPreset.Archipelago,1000)})
        {
            WorldState w=WorldGenerator.Generate(1618033,128,128,requested.Item1);SimulationEngine e=new SimulationEngine(w);
            int[] xs={42,85,42,85},ys={42,85,85,42};var commands=new List<object>();
            for(int race=0;race<4;race++)
            {
                int best=-1;float distance=float.MaxValue;
                for(int i=0;i<w.Cells.Length;i++){int x=i%128,y=i/128;if(x<4||y<4||x>=124||y>=124||!w.IsWalkable(x,y)||w.Cells[i].Terrain==TerrainType.Snow)continue;float d=(x-xs[race])*(x-xs[race])+(y-ys[race])*(y-ys[race]);if(d<distance){distance=d;best=i;}}
                if(best<0)throw new InvalidOperationException("No spawn position");int before=w.Units.Count;
                e.Spawn((SpeciesType)race,best%128,best/128,requested.Item2/4);
                commands.Add(new{species=((SpeciesType)race).ToString(),x=best%128,y=best/128,count=requested.Item2/4,actualAdded=w.Units.Count-before});
            }
            e.SetLaw(WorldLaw.Aging,false);e.SetLaw(WorldLaw.Births,false);e.SetLaw(WorldLaw.Warfare,false);e.SetLaw(WorldLaw.FireSpread,false);
            w.Weather.Automatic=false;w.NaturalWildlife=false;
            if(w.Weather.Kind!=WeatherKind.Clear||w.FloodCells.Count!=0)throw new InvalidOperationException("Expected clear generated starting weather");
            foreach(UnitData u in w.Units)u.Health=u.MaxHealth=100000;
            if(w.Units.Count!=requested.Item2)throw new InvalidOperationException("Population did not reach requested load");
            if(!w.Validate(out string error))throw new InvalidOperationException(error);
            string name=requested.Item1.ToString().ToLowerInvariant()+"-"+requested.Item2;
            string path="Builds/NativeSpeedWorld/"+name+".save.json";
            Program.Save(path,new{Schema=1,Format="WorldGenesis",World=w});
            long bytes=new FileInfo(path).Length;if(bytes>64L*1024*1024)throw new InvalidOperationException("SaveStore size exceeded");
            WorldState copied=JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w,Program.Json),Program.Json);
            string saved=Program.StateHash(copied);SimulationEngine loaded=new SimulationEngine(copied);
            if(saved!=Program.StateHash(copied))throw new InvalidOperationException("Current save changed on construction");
            Stopwatch watch=Stopwatch.StartNew();int validations=0;long loadChecks=0;
            for(int tick=0;tick<600;tick++)
            {
                loaded.Step(.1f);
                if(tick%100==99){validations++;if(!copied.Validate(out error))throw new InvalidOperationException(error);}
                foreach(UnitData u in copied.Units){loadChecks++;if(SimulationEngine.CarriedLoad(u)>SimulationEngine.CarryCapacityFor(u)+.0001f)throw new InvalidOperationException("Overcapacity");}
            }
            if(copied.Units.Count!=w.Units.Count)throw new InvalidOperationException("Short continuation changed stress population");
            results.Add(new{name,seed=w.Seed,preset=w.Preset.ToString(),w.Width,w.Height,commands,actualPopulation=w.Units.Count,actualCivilianPopulation=w.Units.Count(u=>SimulationEngine.IsCivilized(u.Species)),actualPopulationBySpecies=w.Units.GroupBy(u=>u.Species).ToDictionary(g=>g.Key.ToString(),g=>g.Count()),laws=w.Laws,naturalWildlife=w.NaturalWildlife,automaticWeather=w.Weather.Automatic,weather=w.Weather.Kind.ToString(),healthBuffer=100000,worldTick=w.Tick,path,sha256=Program.Hash(path),bytes,envelope=new{Schema=1,Format="WorldGenesis"},worldValid=true,dotNetConstructionUnchanged=true,verification=new{ticks=600,simulatedSeconds=60,wallSeconds=watch.Elapsed.TotalSeconds,validations,loadChecks,finalPopulation=copied.Units.Count},nativeSaveStoreLoad="pending Player verification by integrator"});
            Console.WriteLine("EXPORTED "+path+" units="+w.Units.Count+" bytes="+bytes+" continuation="+copied.Units.Count);
        }
        var after=Program.Sources("WorldGenesis/Assets/_Project/Scripts/Simulation");bool unchanged=sources.Count==after.Count&&sources.All(p=>after.TryGetValue(p.Key,out string h)&&h==p.Value);
        Program.Save(output,new{utc=DateTime.UtcNow,kind="Controlled native stress starting saves; not normal survival or natural economy",sourceUnchanged=unchanged,sourceSHA256=sources,assemblySHA256=Program.Hash(Assembly.GetExecutingAssembly().Location),assistance=new[]{"Ordinary Spawn creates exactly four races in requested quantities.","Aging, Births, Warfare and FireSpread disabled through real laws; automatic weather and natural wildlife disabled.","No Hunger/Thirst law exists. Explicit finite starting Health=MaxHealth=100000 protects population while preserving real needs and decisions.","No grants of inventory, food, technology, equipment; no edits to DecisionTimer, paths, professions, needs, world RNG, resource nodes or simulation code.","The saved world stays at Tick0. The 600tick continuation runs only on a discarded clone."},results});
        return unchanged?0:1;
    }
}
