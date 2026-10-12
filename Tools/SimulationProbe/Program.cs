using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorldGenesis.Simulation;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    private static readonly List<object> Results = new List<object>();
    private static readonly List<object> Metrics = new List<object>();
    private static int failures;

    private static void Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "Evidence/Simulation/simulation-probe.json";
        Check("same seed creates identical complete world", () =>
        {
            Require(Hash(WorldGenerator.Generate(20261012)) == Hash(WorldGenerator.Generate(20261012)), "Repeat generation diverged");
            Require(Hash(WorldGenerator.Generate(20261012)) != Hash(WorldGenerator.Generate(20261013)), "Seeds ignored");
        });
        foreach (WorldPreset preset in Enum.GetValues(typeof(WorldPreset)))
        foreach (int seed in new[] { 0, 1, -1, 713, 20261012, int.MinValue, int.MaxValue })
        {
            int capturedSeed = seed; WorldPreset capturedPreset = preset;
            Check("generator invariants " + preset + " / " + seed, () => InspectMap(WorldGenerator.Generate(capturedSeed, 64, 64, capturedPreset)));
        }
        Check("non-square maps, smallest and largest dimensions", () =>
        {
            InspectMap(WorldGenerator.Generate(131, 16, 32));
            InspectMap(WorldGenerator.Generate(131, 512, 16));
        });
        Check("invalid map dimensions rejected", () =>
        {
            bool rejected = false;
            try { WorldGenerator.Generate(1, int.MaxValue, 128); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Oversized world accepted");
        });
        Check("simulation state remains valid with civilization development", () =>
        {
            SimulationEngine engine = Populated(81, 80);
            int initialBuildings = engine.World.Buildings.Count;
            for (int i = 0; i < 3000; i++)
            {
                engine.Step(.1f);
                if (i % 100 == 0) Require(engine.World.Validate(out string error), error);
            }
            Require(engine.World.Tick == 3000, "Incorrect tick advancement");
            Require(engine.World.Units.Count > 0, "All residents died in a fertile peaceful world");
            Require(engine.World.Buildings.Count > initialBuildings, "No construction in 300 simulated seconds");
            Metrics.Add(new { scenario = "civilization-300-seconds", units = engine.World.Units.Count, buildings = engine.World.Buildings.Count, settlements = engine.World.Settlements.Count });
        });
        Check("full save and continued simulation stay identical", () =>
        {
            SimulationEngine a = Populated(552, 50);
            for (int i = 0; i < 120; i++) a.Step(.1f);
            WorldState restored = JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(a.World, Json), Json);
            Require(restored.Validate(out string error), error);
            SimulationEngine b = new SimulationEngine(restored);
            for (int i = 0; i < 200; i++) { a.Step(.1f); b.Step(.1f); }
            Require(Hash(a.World) == Hash(b.World), "Save/load continuation lost state or depends on hidden engine fields");
        });
        Check("fire rain and meteor affect actual state", () =>
        {
            SimulationEngine e = Populated(719, 20);
            UnitData target = e.World.Units[0]; int x = (int)target.X, y = (int)target.Y;
            e.Paint(TerrainType.Forest, x, y, 4);
            e.ApplyPower(PowerType.Fire, x, y, 4);
            Require(e.World.Cells.Any(t => t.Fire > 0), "Fire did not ignite terrain");
            e.ApplyPower(PowerType.Rain, x, y, 6);
            Require(e.World.Cells.All(t => t.Fire == 0), "Rain left ignition in its radius");
            string before = Hash(e.World);
            float health = e.World.Units.Sum(u => u.Health);
            e.ApplyPower(PowerType.Meteor, x, y, 5);
            Require(before != Hash(e.World), "Meteor changed nothing");
            Require(e.World.Units.Sum(u => u.Health) < health, "Meteor did not harm nearby residents");
            for (int i = 0; i < 10; i++) e.Step(.1f);
            Require(e.World.Validate(out string error), error);
        });
        Check("painting at and beyond boundaries is safe", () =>
        {
            SimulationEngine e = Populated(32, 8);
            e.Paint(TerrainType.DeepWater, -500, -500, 2);
            e.Paint(TerrainType.Grass, 0, 0, 8);
            e.Paint(TerrainType.Sand, e.World.Width - 1, e.World.Height - 1, 8);
            e.ApplyPower(PowerType.Rain, -1, -1, 3);
            e.Step(.1f);
            Require(e.World.Validate(out string error), error);
        });
        Check("corrupt dimensions, references and nonfinite state rejected", () =>
        {
            WorldState w = Populated(731, 8).World;
            w.Units[0].X = float.NaN; Require(!w.Validate(out _), "NaN unit accepted");
            w = Populated(731, 8).World;
            w.Units[1].Id = w.Units[0].Id; Require(!w.Validate(out _), "Duplicate IDs accepted");
            w = Populated(731, 8).World;
            w.Cells[0].SettlementId = int.MaxValue; Require(!w.Validate(out _), "Dangling ownership accepted");
            w = Populated(731, 8).World;
            w.Width = int.MaxValue; Require(!w.Validate(out _), "Corrupt dimensions accepted");
        });
        foreach (int count in new[] { 100, 500, 1000 })
        {
            int n = count;
            Check("bounded core load / " + n + " units", () =>
            {
                SimulationEngine e = Populated(741, n);
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < 100; i++) e.Step(.1f);
                watch.Stop();
                Require(e.World.Validate(out string error), error);
                Metrics.Add(new { scenario = "core-only-100-ticks", requestedUnits = n, finalUnits = e.World.Units.Count, milliseconds = watch.Elapsed.TotalMilliseconds, mapWidth = e.World.Width, mapHeight = e.World.Height });
            });
        }
        string directory = Path.GetDirectoryName(Path.GetFullPath(output)); Directory.CreateDirectory(directory);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            utc = DateTime.UtcNow.ToString("o"), scope = "Production C# Simulation under .NET 8. No Unity rendering/input/performance claim.",
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            total = Results.Count, failed = failures, results = Results, metrics = Metrics
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Simulation checks: " + Results.Count + "; failures: " + failures + ". " + output);
        Environment.ExitCode = failures == 0 ? 0 : 1;
    }

    private static SimulationEngine Populated(int seed, int count)
    {
        WorldState world = WorldGenerator.Generate(seed, 128, 128);
        SimulationEngine engine = new SimulationEngine(world);
        engine.Paint(TerrainType.Grass, 64, 64, 20);
        engine.Paint(TerrainType.Forest, 78, 64, 7);
        world.Laws.Warfare = false;
        engine.Spawn(SpeciesType.Human, 64, 64, count);
        Require(world.Units.Count > 0, "Spawning created no units");
        return engine;
    }

    private static void InspectMap(WorldState world)
    {
        Require(world.Validate(out string error), error);
        float outletFlow = 0;
        for (int i = 0; i < world.Cells.Length; i++)
        {
            TileData c = world.Cells[i];
            Require(c.RouteHeight + .0001f >= c.Height, "Routing height below terrain");
            if (c.DrainageParent < 0) outletFlow += c.Flow;
            int p = i, hops = 0;
            while (world.Cells[p].DrainageParent >= 0)
            {
                int next = world.Cells[p].DrainageParent;
                Require(world.Cells[next].RouteHeight <= world.Cells[p].RouteHeight + .0001f, "Drainage climbs routing surface");
                Require(Math.Abs(next % world.Width - p % world.Width) + Math.Abs(next / world.Width - p / world.Width) == 1, "Drainage parent is not adjacent");
                p = next; Require(++hops < world.Cells.Length, "Drainage cycle");
            }
            Require(p % world.Width == 0 || p % world.Width == world.Width - 1 || p / world.Width == 0 || p / world.Width == world.Height - 1, "Drainage terminates inland");
        }
        Require(Math.Abs(outletFlow - world.Cells.Length) < .01, "Flow mass not conserved at outlets");
    }
    private static string Hash(WorldState world) { return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(world, Json)))); }
    private static void Require(bool ok, string error) { if (!ok) throw new InvalidOperationException(error); }
    private static void Check(string name, Action test)
    {
        Stopwatch watch = Stopwatch.StartNew();
        try { test(); Results.Add(new { name, passed = true, milliseconds = watch.Elapsed.TotalMilliseconds }); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failures++; Results.Add(new { name, passed = false, error = e.ToString(), milliseconds = watch.Elapsed.TotalMilliseconds }); Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }
}
