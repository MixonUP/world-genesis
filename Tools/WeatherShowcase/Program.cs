using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using WorldGenesis.Simulation;

// Controlled native-review inputs. This tool does not claim autonomous disaster occurrence.
internal static class Program
{
    private sealed class Envelope { public int Schema = 1; public string Format = "WorldGenesis"; public WorldState World; }
    private static readonly JsonSerializerOptions Fields = new JsonSerializerOptions { IncludeFields = true, IgnoreReadOnlyProperties = true };
    private static void Require(bool ok, string error) { if (!ok) throw new InvalidOperationException(error); }
    private static int Main(string[] args)
    {
        string input = args.Length > 0 ? args[0] : "Builds/storage-assisted-v050.save.json";
        string prefix = args.Length > 1 ? args[1] : "Builds/weather-assisted-v050";
        string report = args.Length > 2 ? args[2] : "Evidence/Simulation/weather-assisted-v050.json";
        string source = File.ReadAllText(input); var records = new List<object>();
        foreach (WeatherKind kind in new[] { WeatherKind.Rain, WeatherKind.Snow, WeatherKind.Storm, WeatherKind.Drought, WeatherKind.Hurricane, WeatherKind.Flood })
        {
            WorldState w = JsonSerializer.Deserialize<Envelope>(source, Fields).World;
            Require(w != null, "Source must be a native save envelope.");
            var e = new SimulationEngine(w); w.Weather.Automatic = false;
            int x = 63, y = 64, temperatureEdits = 0;
            // The existing river fixture is warm. A disclosed cold-climate patch lets the same valley show snow.
            if (kind == WeatherKind.Snow)
                for (int cy = y-18; cy <= y+18; cy++) for (int cx = x-18; cx <= x+18; cx++)
                    if (w.InBounds(cx, cy) && (cx-x)*(cx-x)+(cy-y)*(cy-y) <= 18*18)
                    { int cell = w.Index(cx,cy); w.Cells[cell].Temperature = .18f; BiomeCatalog.RefreshCell(w,cell); temperatureEdits++; }
            int ticks = kind == WeatherKind.Snow ? 300 : kind == WeatherKind.Drought ? 400 : kind == WeatherKind.Flood ? 80 : 60;
            float intensity = kind == WeatherKind.Rain ? .7f : 1;
            Require(e.SetWeather(kind,x,y,18,intensity,120), "Weather command rejected.");
            for (int t=0; t<ticks; t++) { e.Step(.1f); Require(w.Validate(out string error), "Weather tick "+t+": "+error); }
            string output = prefix+"-"+kind.ToString().ToLowerInvariant()+".save.json";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonSerializer.Serialize(new Envelope { World=w }, Fields));
            WorldState restored = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(output),Fields).World;
            Require(restored.Validate(out string restoredError),restoredError);
            var restoredEngine = new SimulationEngine(restored);
            for (int t=0;t<100;t++) { e.Step(.1f); restoredEngine.Step(.1f); }
            Require(JsonSerializer.Serialize(w,Fields)==JsonSerializer.Serialize(restored,Fields), "Continuation changed for "+kind);
            // Read the saved moment for the manifest, not the additional continuation-check moment.
            WorldState saved=JsonSerializer.Deserialize<Envelope>(File.ReadAllText(output),Fields).World;
            records.Add(new {kind=kind.ToString(),output,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))),
                command=new{x,y,radius=18,intensity,seconds=120},preRollTicks=ticks,temperatureEdits,editedTemperature=temperatureEdits>0?.18f:(float?)null,
                tick=saved.Tick,weatherX=saved.Weather.CenterX,weatherY=saved.Weather.CenterY,floodedCells=saved.FloodCells.Count,
                maxSnow=saved.Cells.Max(c=>c.SnowCover),minMoisture=saved.Cells.Min(c=>c.Moisture),burningCells=saved.Cells.Count(c=>c.Fire>.01f),
                living=saved.Units.Count,buildings=saved.Buildings.Count,validation=true,continuedTicks=100,continuationEqual=true});
        }
        Directory.CreateDirectory(Path.GetDirectoryName(report));
        File.WriteAllText(report,JsonSerializer.Serialize(new {utc=DateTime.UtcNow,input,sourceSHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))),
            assistance="Source inherits the documented storage/transport material and technology grants. Weather is commanded, automatic changes disabled. Only the snow scenario receives a declared cold-climate patch. No render pixels are fabricated.",records},new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine("Prepared "+records.Count+" valid weather worlds and verified 100-tick save continuations: "+report); return 0;
    }
}
