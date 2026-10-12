using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorldGenesis.Simulation;

internal static class NaturalDiagnostics
{
    public static int Run(string[] args)
    {
        string path = args.Length > 1 ? args[1] : "Evidence/Simulation/natural-diagnostics.json";
        int ticks = args.Length > 2 ? int.Parse(args[2]) : 18000;
        int seed = args.Length > 3 ? int.Parse(args[3]) : 1618033;
        var w = WorldGenerator.Generate(seed, 128, 128); var e = new SimulationEngine(w);
        int[] ax = { 42, 85, 42, 85 }, ay = { 42, 85, 85, 42 };
        for (int s = 0; s < 4; s++)
        {
            int bx = -1, by = -1; float best = float.MaxValue;
            for (int y = 4; y < 124; y++) for (int x = 4; x < 124; x++)
            {
                if (!w.IsWalkable(x, y) || w.Cells[w.Index(x, y)].Terrain == TerrainType.Snow) continue;
                float score = (x-ax[s])*(x-ax[s])+(y-ay[s])*(y-ay[s]);
                if (score < best) { best = score; bx = x; by = y; }
            }
            if (bx >= 0) { e.Spawn((SpeciesType)s, bx, by, 9); e.Spawn(SpeciesType.Sheep, bx+3, by+2, 3); }
        }
        var timeline = new List<object>(); var deaths = new Dictionary<string,int>();
        var previous = new Dictionary<int,(UnitData unit, float hunger, float age, bool attacked)>();
        var live = new HashSet<int>(); string error = ""; int validations = 0, carries = 0;
        int firstBasket = 0;
        var weatherTransitions = new List<object>(); int lastWeatherSequence = -1, maxFloodCells = 0;
        WeatherKind lastWeather = (WeatherKind)(-1);
        for (int t = 1; t <= ticks; t++)
        {
            previous.Clear();
            foreach (UnitData u in w.Units.Where(u => SimulationEngine.IsCivilized(u.Species)))
                previous[u.Id] = (u,u.Hunger,u.Age,w.Units.Any(a => a.TargetId==u.Id && (a.Task==UnitTask.Fight || a.Task==UnitTask.Hunt)));
            e.Step(.1f); live.Clear(); foreach(UnitData u in w.Units)live.Add(u.Id);
            maxFloodCells = Math.Max(maxFloodCells, w.FloodCells.Count);
            if (w.Weather.Sequence != lastWeatherSequence || w.Weather.Kind != lastWeather)
            {
                weatherTransitions.Add(new { tick=w.Tick, kind=w.Weather.Kind.ToString(), w.Weather.Sequence, w.Weather.CenterX, w.Weather.CenterY, w.Weather.Radius, w.Weather.Intensity, w.Weather.RemainingSeconds });
                lastWeatherSequence=w.Weather.Sequence; lastWeather=w.Weather.Kind;
            }
            foreach (var p in previous.Values)
                if (!live.Contains(p.unit.Id))
                {
                    string cause = p.age > (p.unit.Species == SpeciesType.Elf ? 180 : 85) ? "old-age" : p.hunger > .9f ? "starvation" : p.attacked ? "combat" : "other";
                    string key = p.unit.SettlementId + ":" + cause; deaths[key] = deaths.GetValueOrDefault(key)+1;
                }
            foreach(UnitData u in w.Units)
            {
                carries++;
                if (SimulationEngine.CarriedLoad(u)>SimulationEngine.CarryCapacityFor(u)+.001f) { error="Carry overflow " + u.Id; break; }
                if(firstBasket==0 && u.BasketLevel>0)firstBasket=t;
            }
            if (!string.IsNullOrEmpty(error)) break;
            if(t%100==0) { validations++; if(!w.Validate(out error))break; }
            if(t%1000==0)
            {
                timeline.Add(new {tick=t, weather=w.Weather.Kind.ToString(), floodedCells=w.FloodCells.Count, meanSnow=w.Cells.Average(c=>c.SnowCover), towns=w.Settlements.Select(s=>new{s.Id,s.Population,s.Food,s.Wood,s.Housing,
                    tasks=w.Units.Where(u=>u.SettlementId==s.Id).GroupBy(u=>u.Task).ToDictionary(g=>g.Key.ToString(),g=>g.Count()),
                    technologies=s.UnlockedTechnologies.ToArray(),s.ActiveTechnology,s.ResearchProgress,
                    buildings=w.Buildings.Where(b=>b.SettlementId==s.Id).GroupBy(b=>b.Type).ToDictionary(g=>g.Key.ToString(),g=>g.Count()),
                    baskets=w.Units.Count(u=>u.SettlementId==s.Id&&u.BasketLevel>0)}).ToArray()});
                Console.WriteLine("tick="+t+" citizens="+w.Units.Count(u=>SimulationEngine.IsCivilized(u.Species))+" baskets="+w.Units.Count(u=>u.BasketLevel>0));
            }
        }
        var json = new JsonSerializerOptions {IncludeFields=true,WriteIndented=true,IgnoreReadOnlyProperties=true};
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string worldPath=Path.ChangeExtension(path,"world.json"); File.WriteAllText(worldPath,JsonSerializer.Serialize(w,json));
        File.WriteAllText(path,JsonSerializer.Serialize(new{utc=DateTime.UtcNow,seed,requestedTicks=ticks,w.Tick,passed=string.IsNullOrEmpty(error),error,validations,carries,firstBasket,
            weather=new {automatic=w.Weather.Automatic,maxFloodCells,transitions=weatherTransitions},
            transport=new {roads=w.Infrastructure.GroupBy(c=>c.RoadLevel).ToDictionary(g=>g.Key.ToString(),g=>g.Count()),bridgeCells=w.Infrastructure.Count(c=>c.BridgeLevel>0),projects=w.TransportProjects.Count},
            geography=new {terrains=w.Cells.GroupBy(c=>c.Terrain).ToDictionary(g=>g.Key.ToString(),g=>g.Count()),biomes=w.Cells.GroupBy(c=>(BiomeType)c.Biome).ToDictionary(g=>g.Key.ToString(),g=>g.Count()),water=w.Cells.GroupBy(c=>c.WaterKind).ToDictionary(g=>g.Key.ToString(),g=>g.Count()),saltDeposits=w.ResourceNodes.Count(n=>n.Type==ResourceType.Salt)},
            deathClassification="Last pre-tick age/hunger/combat-target; concurrent causes are prioritized age, starvation, combat; other includes hazards.",deaths,timeline,worldPath},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(string.IsNullOrEmpty(error)?"PASS":"FAIL "+error);return string.IsNullOrEmpty(error)?0:1;
    }
}
