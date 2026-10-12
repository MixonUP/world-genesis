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
    private static string filter;

    private static void Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "Evidence/Simulation/simulation-probe.json";
        filter = args.Length > 1 ? args[1] : null;
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
        Check("behavior / fire interrupts work and preserves carried resources", () =>
        {
            SimulationEngine e = BehaviorWorld(); e.Spawn(SpeciesType.Human, 16, 16);
            UnitData u = e.World.Units[0]; u.X = 16.5f; u.Y = 16.5f; u.Task = UnitTask.Gather; u.TargetCell = e.World.Index(16, 16); u.CarriedWood = 1; u.DecisionTimer = 50;
            e.ApplyPower(PowerType.Fire, 16, 16, 1); e.Step(.1f);
            Require(u.Task == UnitTask.Flee && u.CarriedWood == 1 && u.Path.Count > 0, "Fire did not interrupt work safely");
            for (int i = 0; i < 35; i++) e.Step(.1f);
            Require(u.Health > 0 && e.World.Cells[e.World.Index((int)u.X, (int)u.Y)].Fire < .1f, "Escape did not reach safety");
            Require(e.World.Validate(out string error), error);
        });
        Check("behavior / eating is a single interruptible resource transaction", () =>
        {
            SimulationEngine e = BehaviorWorld(); e.Spawn(SpeciesType.Human, 16, 16);
            UnitData u = e.World.Units[0]; SettlementData home = e.World.Settlements[0];
            u.X = 16.5f; u.Y = 16.5f; u.Hunger = .8f; u.Fatigue = 0; u.DecisionTimer = 0; home.Food = 80;
            e.Step(.1f); Require(u.Task == UnitTask.Eat && home.Food == 80 && u.WorkTimer > 0, "Eating was instant or not selected");
            e.ApplyPower(PowerType.Fire, 16, 16, 1); e.Step(.1f);
            Require(u.Task == UnitTask.Flee && home.Food == 80, "Interrupted meal consumed stock");
            e.ApplyPower(PowerType.Rain, 16, 16, 10);
            u.X = 16.5f; u.Y = 16.5f; u.Task = UnitTask.Idle; u.Path.Clear(); u.PathCursor = 0; u.DecisionTimer = 0;
            for (int i = 0; i < 14; i++) e.Step(.1f);
            Require(home.Food == 79 && u.Hunger < .5f, "Meal was not exactly one stock transaction");
        });
        Check("behavior / sheep flee predators and fatigue causes interruptible rest", () =>
        {
            SimulationEngine e = BehaviorWorld(); e.Spawn(SpeciesType.Sheep, 12, 16); e.Spawn(SpeciesType.Wolf, 15, 16);
            UnitData sheep = e.World.Units[0], wolf = e.World.Units[1];
            sheep.X = 12.5f; sheep.Y = 16.5f; wolf.X = 15.5f; wolf.Y = 16.5f; wolf.DecisionTimer = 100;
            for (int i = 0; i < 10; i++) e.Step(.1f);
            Require(sheep.Task == UnitTask.Flee && Math.Abs(sheep.X - 15.5f) + Math.Abs(sheep.Y - 16.5f) > 3, "Sheep did not increase separation");
            e.Spawn(SpeciesType.Human, 5, 5); UnitData u = e.World.Units[2];
            u.X = 5.5f; u.Y = 5.5f; u.Fatigue = .95f; u.DecisionTimer = 0; u.Hunger = .1f;
            e.Step(.1f); Require(u.Task == UnitTask.Rest, "Exhaustion did not select rest");
            for (int i = 0; i < 100; i++) e.Step(.1f);
            Require(u.Fatigue < .4f, "Rest did not recover fatigue");
            e.ApplyPower(PowerType.Fire, (int)u.X, (int)u.Y, 1); e.Step(.1f);
            Require(u.Task == UnitTask.Flee, "Rest prevented emergency response");
        });
        Check("behavior / social contact uses living nearby partners", () =>
        {
            SimulationEngine e = BehaviorWorld(); e.Spawn(SpeciesType.Human, 16, 16, 2);
            UnitData a = e.World.Units[0], b = e.World.Units[1];
            a.X = 16.5f; a.Y = 16.5f; b.X = 17.5f; b.Y = 16.5f;
            a.SocialNeed = .9f; a.Fatigue = 0; a.DecisionTimer = 0;
            b.Task = UnitTask.Rest; b.Fatigue = .7f; b.TargetCell = e.World.Index(17, 16); b.DecisionTimer = 100;
            e.Step(.1f); Require(a.Task == UnitTask.Socialize, "No social visit selected");
            for (int i = 0; i < 20; i++) e.Step(.1f);
            Require(a.SocialNeed < .8f, "Social need did not respond to actual contact");
            b.Health = 0; e.Step(.1f); e.Step(.1f);
            Require(a.TargetId != b.Id && a.Task != UnitTask.Socialize, "Social visit retained dead partner");
        });
        Check("behavior / legacy v1 migrates once without RNG draws", () =>
        {
            SimulationEngine original = BehaviorWorld(); original.Spawn(SpeciesType.Human, 16, 16, 12);
            System.Text.Json.Nodes.JsonNode legacy = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(original.World, Json));
            legacy.AsObject().Remove("BehaviorVersion");
            foreach (System.Text.Json.Nodes.JsonNode unit in legacy["Units"].AsArray())
                foreach (string field in new[] { "Profession", "Fatigue", "SocialNeed", "TaskStartedTick" }) unit.AsObject().Remove(field);
            WorldState restored = JsonSerializer.Deserialize<WorldState>(legacy.ToJsonString(), Json);
            Require(restored.BehaviorVersion == 0 && restored.Validate(out _), "Legacy shape rejected");
            uint rng = restored.RngState; new SimulationEngine(restored);
            Require(restored.BehaviorVersion == 2 && restored.RngState == rng, "Migration changed RNG");
            Require(restored.Units.All(u => u.Profession != UnitProfession.None && u.Fatigue == 0), "Legacy defaults incorrect");
            string migratedHash = Hash(restored); new SimulationEngine(restored);
            Require(migratedHash == Hash(restored), "Migration is not idempotent");
            restored.Units[0].Fatigue = float.NaN; Require(!restored.Validate(out _), "Invalid fatigue accepted");
        });
        Check("behavior / save continuation retains incomplete meals and rest", () =>
        {
            SimulationEngine a = BehaviorWorld(); a.Spawn(SpeciesType.Human, 16, 16, 2);
            UnitData hungry = a.World.Units[0], tired = a.World.Units[1];
            hungry.X = 16.5f; hungry.Y = 16.5f; hungry.Hunger = .8f; hungry.DecisionTimer = 0;
            tired.X = 16.5f; tired.Y = 16.5f; tired.Fatigue = .95f; tired.DecisionTimer = 0;
            a.Step(.1f); Require(hungry.Task == UnitTask.Eat && tired.Task == UnitTask.Rest, "Fixture did not enter target actions");
            SimulationEngine b = new SimulationEngine(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(a.World, Json), Json));
            for (int i = 0; i < 150; i++) { a.Step(.1f); b.Step(.1f); }
            Require(Hash(a.World) == Hash(b.World), "New behavior depends on hidden unsaved state");
        });
        Check("behavior / founding spawning and adulthood create civilians only", () =>
        {
            for (int seed = 0; seed < 12; seed++)
            {
                SimulationEngine e = BehaviorWorld(); e.World.Seed = seed;
                // Each independent founder needs unclaimed land, rather than four halls on the same cell.
                for (int species = 0; species < 4; species++) e.Spawn((SpeciesType)species, 6 + (species % 2) * 19, 6 + (species / 2) * 19, 24);
                Require(e.World.Units.Count == 96 && e.World.Units.All(u => u.Profession == UnitProfession.Farmer || u.Profession == UnitProfession.Forester), "Founding or spawning assigned a military office");
                for (int i = 0; i < 12; i++) { e.World.Units[i].Age = 15.99f; e.World.Units[i].Profession = UnitProfession.None; }
                for (int i = 0; i < 3; i++) e.Step(.1f);
                Require(e.World.BehaviorVersion == 2 && e.World.Units.All(u => u.Profession == UnitProfession.Farmer || u.Profession == UnitProfession.Forester), "Maturing assigned a military office");
                Require(e.World.Validate(out string error), error);
            }
        });
        Check("behavior / experimental guards demobilize without losing saved needs cargo or RNG", () =>
        {
            SimulationEngine source = BehaviorWorld(); source.Spawn(SpeciesType.Human, 16, 16, 3);
            WorldState legacy = source.World; legacy.BehaviorVersion = 1; legacy.ResourceVersion = 1; legacy.Tick = 50; legacy.SimulatedSeconds = 5;
            UnitData patrol = legacy.Units[0], soldier = legacy.Units[1], civilian = legacy.Units[2];
            patrol.Profession = UnitProfession.Guard; patrol.Task = UnitTask.Patrol; patrol.TargetCell = legacy.Index(17, 16); patrol.Path.Add(patrol.TargetCell); patrol.CarriedWood = 2.5f; patrol.CarriedFood = .6f; patrol.Fatigue = .8f; patrol.SocialNeed = .9f;
            soldier.Profession = UnitProfession.Guard; soldier.Task = UnitTask.Fight; soldier.TargetId = civilian.Id; soldier.TargetCell = legacy.Index(16, 16); soldier.Path.Add(soldier.TargetCell);
            civilian.Profession = UnitProfession.Farmer; civilian.Task = UnitTask.Rest; civilian.Fatigue = .6f; civilian.TaskStartedTick = 7;
            Require(legacy.Validate(out string error), error);
            uint rng = legacy.RngState; int nextId = legacy.NextId; float food = legacy.Settlements[0].Food;
            SimulationEngine a = new SimulationEngine(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(legacy, Json), Json));
            UnitData retired = a.World.GetUnit(patrol.Id), retiredSoldier = a.World.GetUnit(soldier.Id), unchanged = a.World.GetUnit(civilian.Id);
            Require(a.World.BehaviorVersion == 2 && a.World.RngState == rng && a.World.NextId == nextId && a.World.Tick == 50 && a.World.Settlements[0].Food == food, "Demobilization mutated RNG, IDs, clock or stock");
            Require(retired.Profession != UnitProfession.Guard && retiredSoldier.Profession != UnitProfession.Guard && retired.Task == UnitTask.Idle && retiredSoldier.Task == UnitTask.Idle && retired.Path.Count == 0 && retiredSoldier.Path.Count == 0 && retiredSoldier.TargetId == 0, "Military role or order survived");
            Require(Math.Abs(retired.CarriedWood + a.World.GroundStocks.Where(g => g.Item == ItemType.Plank).Sum(g => g.Amount) - 2.5f) < .00001f && retired.CarriedFood == .6f && retired.Fatigue == .8f && retired.SocialNeed == .9f && unchanged.Task == UnitTask.Rest && unchanged.Fatigue == .6f && unchanged.TaskStartedTick == 7, "Unrelated saved state lost");
            string once = Hash(a.World); new SimulationEngine(a.World); Require(once == Hash(a.World), "Demobilization repeats");
            SimulationEngine b = new SimulationEngine(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(a.World, Json), Json));
            for (int i = 0; i < 50; i++) { a.Step(.1f); b.Step(.1f); }
            Require(Hash(a.World) == Hash(b.World), "Demobilized save continuation diverged");
            retired.Profession = UnitProfession.Guard; Require(!a.World.Validate(out _), "Current format admitted an unconfirmed military role");
        });
        Check("behavior / temporary civilian militia defends locally then resumes work", () =>
        {
            SimulationEngine e = BehaviorWorld(); e.SetLaw(WorldLaw.Warfare, true); e.Spawn(SpeciesType.Human, 5, 5); e.Spawn(SpeciesType.Orc, 26, 26);
            SettlementData home = e.World.Settlements[0], foeHome = e.World.Settlements[1]; e.SetDiplomacy(home.Id, foeHome.Id, true);
            UnitData citizen = e.World.Units[0], foe = e.World.Units[1]; citizen.Profession = UnitProfession.Farmer; citizen.Fatigue = 0; citizen.DecisionTimer = 0;
            for (int i = 0; i < 30; i++) e.Step(.1f);
            Require(citizen.Task != UnitTask.Fight && citizen.Task != UnitTask.Patrol && Math.Abs(citizen.X - home.X) + Math.Abs(citizen.Y - home.Y) < 16, "Distant war mobilized an unequipped worker");
            foe.X = citizen.X + 1; foe.Y = citizen.Y; foe.Task = UnitTask.Fight; foe.TargetId = citizen.Id; foe.AttackTimer = 0; foe.DecisionTimer = 0; foe.Path.Clear(); foe.PathCursor = 0;
            float initialHealth = citizen.Health, foeHealth = foe.Health; e.Step(.1f);
            Require(citizen.Task == UnitTask.Fight && citizen.Profession == UnitProfession.Farmer && citizen.Health < initialHealth && foe.Health < foeHealth, "Temporary defense did not answer the attack");
            Require(e.World.Buildings.All(b => b.Health == 100), "Civilian defense became a siege");
            foe.Health = 0; e.Step(.1f); e.Step(.1f);
            Require(citizen.Task != UnitTask.Fight && citizen.Task != UnitTask.Patrol && citizen.Profession == UnitProfession.Farmer, "Emergency defense did not end");
            Require(e.World.Validate(out string error), error);
        });
        Check("behavior / children and severely wounded civilians flee local attackers", () =>
        {
            for (int scenario = 0; scenario < 2; scenario++)
            {
                SimulationEngine e = BehaviorWorld(); e.SetLaw(WorldLaw.Warfare, true); e.Spawn(SpeciesType.Human, 5, 5); e.Spawn(SpeciesType.Orc, 26, 26);
                e.SetDiplomacy(e.World.Settlements[0].Id, e.World.Settlements[1].Id, true);
                UnitData citizen = e.World.Units[0], foe = e.World.Units[1];
                citizen.X = 5.5f; citizen.Y = 5.5f; citizen.Age = scenario == 0 ? 12 : 30; citizen.Health = scenario == 0 ? 100 : 20; citizen.Profession = scenario == 0 ? UnitProfession.None : UnitProfession.Forester;
                foe.X = 6.5f; foe.Y = 5.5f; foe.Task = UnitTask.Fight; foe.TargetId = citizen.Id; e.Step(.1f);
                Require(citizen.Task == UnitTask.Flee && citizen.Profession != UnitProfession.Guard && citizen.Path.Count > 0, "Vulnerable citizen joined combat");
                Require(e.World.Validate(out string error), error);
            }
        });
        Check("politics / seeded governments cover all forms and retain real rulers", () =>
        {
            HashSet<GovernmentType> seen = new HashSet<GovernmentType>();
            for (int seed = 0; seed < 32; seed++)
            {
                SimulationEngine a = BehaviorWorld(), b = BehaviorWorld(); a.World.Seed = seed; b.World.Seed = seed;
                a.Spawn(SpeciesType.Human, 16, 16, 5); b.Spawn(SpeciesType.Human, 16, 16, 5);
                SettlementData s = a.World.Settlements[0]; seen.Add(s.Government);
                Require(s.Government != GovernmentType.Unset && Hash(a.World) == Hash(b.World), "Seeded government diverged");
                UnitData ruler = a.World.GetUnit(s.RulerId);
                Require(ruler != null && ruler.SettlementId == s.Id && ruler.Age >= 16 && ruler.Health > 0, "No real eligible ruler");
                GovernmentType government = s.Government; int rulerId = s.RulerId;
                a.Spawn(SpeciesType.Human, 16, 16, 2);
                Require(s.Government == government && s.RulerId == rulerId, "Ordinary spawning replaced government or incumbent");
                Require(a.World.Validate(out string error), error);
            }
            Require(seen.Count == 3, "A government form was never generated");
        });
        Check("politics / succession prioritizes age or blessing and clears deaths at command boundary", () =>
        {
            SimulationEngine e = BehaviorWorld(); e.Spawn(SpeciesType.Human, 16, 16, 3);
            SettlementData s = e.World.Settlements[0]; s.Government = GovernmentType.Feudal; s.NextElectionTick = 0; s.RulerId = 0;
            UnitData young = e.World.Units[0], middle = e.World.Units[1], oldest = e.World.Units[2];
            young.Age = 18; middle.Age = 30; oldest.Age = 50;
            e.Step(.1f); Require(s.RulerId == oldest.Id, "Feudal vacancy ignored eldest citizen");
            oldest.Health = 0; e.Step(.1f); Require(s.RulerId == middle.Id, "Dead ruler retained or succession wrong");
            s.Government = GovernmentType.Theocracy; s.RulerId = 0; young.Blessed = true;
            e.Step(.1f); Require(s.RulerId == young.Id, "Theocracy ignored blessed candidate");
            foreach (UnitData u in e.World.Units) { u.X = 16.5f; u.Y = 16.5f; u.Health = 10; }
            e.ApplyPower(PowerType.Lightning, 16, 16, 1);
            Require(e.World.Units.Count == 0 && s.RulerId == 0, "Damaging command left dangling ruler");
            Require(e.World.Validate(out string error), error);
        });
        Check("politics / adulthood and membership determine eligibility", () =>
        {
            SimulationEngine e = BehaviorWorld(); e.Spawn(SpeciesType.Human, 6, 6, 2); e.Spawn(SpeciesType.Elf, 26, 26);
            SettlementData home = e.World.Settlements[0], other = e.World.Settlements[1]; home.RulerId = 0;
            foreach (UnitData u in e.World.Units) if (u.SettlementId == home.Id) u.Age = 10;
            e.Step(.1f); Require(home.RulerId == 0, "Child appointed");
            UnitData child = e.World.Units[0]; child.Age = 15.99f;
            for (int i = 0; i < 3; i++) e.Step(.1f);
            Require(home.RulerId == child.Id, "Mature citizen not appointed");
            child.SettlementId = other.Id; e.Step(.1f);
            Require(home.RulerId == 0, "Foreign citizen retained office"); Require(e.World.Validate(out string error), error);
        });
        Check("politics / republic votes at saved deadline and continues identically after load", () =>
        {
            SimulationEngine a = BehaviorWorld(); a.Spawn(SpeciesType.Human, 16, 16, 8);
            a.SetLaw(WorldLaw.Births, false); a.SetLaw(WorldLaw.Aging, false);
            SettlementData s = a.World.Settlements[0]; s.Government = GovernmentType.Republic; s.GovernmentSinceTick = 0; s.NextElectionTick = 600; s.Food = 10000;
            int incumbent = s.RulerId;
            for (int i = 0; i < 599; i++) a.Step(.1f);
            Require(s.RulerId == incumbent && s.NextElectionTick == 600, "Early election or lost incumbent");
            SimulationEngine b = new SimulationEngine(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(a.World, Json), Json));
            a.Step(.1f); b.Step(.1f);
            Require(s.NextElectionTick == 1200 && a.World.GetUnit(s.RulerId) != null, "Election deadline ignored");
            Require(Hash(a.World) == Hash(b.World), "Election save continuation diverged");
            for (int i = 0; i < 30; i++) { a.Step(.1f); b.Step(.1f); }
            Require(Hash(a.World) == Hash(b.World), "Post-election continuation diverged"); Require(a.World.Validate(out string error), error);
        });
        Check("politics / legacy missing fields migrate without RNG or IDs and malformed data is rejected", () =>
        {
            SimulationEngine source = BehaviorWorld(); source.Spawn(SpeciesType.Human, 16, 16, 5);
            System.Text.Json.Nodes.JsonNode legacy = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(source.World, Json));
            legacy.AsObject().Remove("PoliticsVersion");
            foreach (System.Text.Json.Nodes.JsonNode settlement in legacy["Settlements"].AsArray())
                foreach (string field in new[] { "Government", "RulerId", "GovernmentSinceTick", "NextElectionTick" }) settlement.AsObject().Remove(field);
            WorldState restored = JsonSerializer.Deserialize<WorldState>(legacy.ToJsonString(), Json);
            Require(restored.PoliticsVersion == 0 && restored.Validate(out _), "Old v1 shape rejected");
            uint rng = restored.RngState; int nextId = restored.NextId; new SimulationEngine(restored);
            Require(restored.PoliticsVersion == 1 && restored.RngState == rng && restored.NextId == nextId && restored.Settlements[0].RulerId != 0, "Migration consumed randomness or fabricated citizens");
            string once = Hash(restored); new SimulationEngine(restored); Require(once == Hash(restored), "Politics migration not idempotent");
            SettlementData s = restored.Settlements[0]; int ruler = s.RulerId;
            s.RulerId = int.MaxValue; Require(!restored.Validate(out _), "Dangling ruler accepted"); s.RulerId = ruler;
            s.Government = (GovernmentType)99; Require(!restored.Validate(out _), "Unknown government accepted");
            s.Government = GovernmentType.Republic; s.NextElectionTick = restored.Tick + 601; Require(!restored.Validate(out _), "Out-of-range election accepted");
            s.NextElectionTick = restored.Tick + 600; s.GovernmentSinceTick = restored.Tick + 1; Require(!restored.Validate(out _), "Future government origin accepted");
        });
        Check("diplomacy / legacy enemy cache cannot capture peaceful abandoned territory", () =>
        {
            SimulationEngine e = BehaviorWorld(); e.SetLaw(WorldLaw.Warfare, true); e.SetLaw(WorldLaw.Births, false);
            e.Spawn(SpeciesType.Human, 6, 6, 8); e.Spawn(SpeciesType.Orc, 25, 25, 1);
            WorldState w = e.World; SettlementData home = w.Settlements[0], abandoned = w.Settlements[1];
            home.EnemyId = abandoned.Id; abandoned.EnemyId = home.Id;
            foreach (UnitData u in w.Units) if (u.SettlementId == abandoned.Id) u.Health = 0;
            e.Step(.1f);
            UnitData visitor = w.Units[0]; visitor.X = abandoned.X + .5f; visitor.Y = abandoned.Y + .5f; visitor.Task = UnitTask.Idle; visitor.Path.Clear(); visitor.PathCursor = 0; visitor.TargetCell = -1; visitor.DecisionTimer = 10;
            WorldState restored = JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w, Json), Json);
            SimulationEngine resumed = new SimulationEngine(restored); resumed.Step(1);
            Require(restored.Cells[restored.Index(abandoned.X, abandoned.Y)].SettlementId == abandoned.Id && restored.Buildings.Where(b => b.X == abandoned.X && b.Y == abandoned.Y).All(b => b.SettlementId == abandoned.Id), "A peaceful abandoned town was captured");
            Require(restored.Relations.Count == 0 && restored.GetSettlement(home.Id).EnemyId == 0 && restored.GetSettlement(abandoned.Id).EnemyId == 0, "Stale implicit war references survived the settlement update");
            resumed.SetDiplomacy(home.Id, abandoned.Id, true); resumed.Step(1);
            Require(restored.Cells[restored.Index(abandoned.X, abandoned.Y)].SettlementId == home.Id, "Explicit war capture stopped working");
            Require(restored.Validate(out string error), error);
        });
        Check("resources / autonomous tree harvest delivery and campfire processing conserve wood", () =>
        {
            SimulationEngine e = ResourceFixture(); WorldState w = e.World; UnitData u = w.Units[0]; SettlementData s = w.Settlements[0];
            int source = w.Index(20, 16); w.Cells[source].Terrain = TerrainType.Forest; w.Cells[source].Vegetation = 1;
            w.ResourceNodes[source] = new ResourceNodeData { Type = ResourceType.Tree, Amount = 3, Capacity = 3 };
            bool carried = false, processing = false, depleted = false;
            for (int i = 0; i < 650 && s.Wood < 2.99f; i++)
            {
                e.Step(.1f); carried |= u.CargoAmount > 0; processing |= u.Task == UnitTask.Process; depleted |= w.ResourceNodes[source].Amount == 0;
                float logs = w.ResourceNodes[source].Amount + (u.CargoKind == ItemType.Log ? u.CargoAmount : 0) + w.GetSettlementStock(s.Id, ItemType.Log) + w.GetCitizenStock(u.Id, ItemType.Log) + s.Wood;
                Require(Math.Abs(logs - 3) < .002f, "Tree/log/plank mass changed: " + logs);
                Require(w.Validate(out string error), error);
            }
            Require(carried && processing && depleted && Math.Abs(s.Wood - 3) < .002f && w.Cells[source].Vegetation == 0, "Autonomous source-to-plank cycle failed: wood=" + s.Wood + " cargo=" + u.CargoAmount + " storedLog=" + w.GetSettlementStock(s.Id, ItemType.Log) + " node=" + w.ResourceNodes[source].Amount + " vegetation=" + w.Cells[source].Vegetation + " task=" + u.Task + " progress=" + w.Buildings[0].ProcessProgress);
            Require(w.Buildings.All(b => b.Type != BuildingType.Sawmill), "Test bypassed campfire using a free sawmill");
        });
        Check("resources / workshop rate is real on-site time and clay requires fuel", () =>
        {
            SimulationEngine slow = ResourceFixture(), fast = ResourceFixture();
            BuildingData camp = slow.World.Buildings[0], mill = fast.World.Buildings[0]; mill.Type = BuildingType.Sawmill;
            camp.Inventory[(int)ItemType.Log] = 1; mill.Inventory[(int)ItemType.Log] = 1;
            for (int i = 0; i < 30; i++) { slow.Step(.1f); fast.Step(.1f); }
            Require(slow.World.Settlements[0].Wood == 0 && fast.World.Settlements[0].Wood == 1, "Sawmill is not faster than campfire");
            Require(camp.ProcessProgress > 2.9f && camp.Inventory[(int)ItemType.Log] == 1 && mill.Inventory[(int)ItemType.Log] == 0, "Inputs not retained until work transaction");
            for (int i = 30; i < 100; i++) slow.Step(.1f);
            Require(slow.World.Settlements[0].Wood == 1 && camp.Inventory[(int)ItemType.Log] == 0, "Campfire did not finish after ten seconds");
            SimulationEngine stone = ResourceFixture(); BuildingData stoneworks = stone.World.Buildings[0]; stoneworks.Type = BuildingType.Stoneworks; stoneworks.Inventory[(int)ItemType.RawStone] = 4;
            for (int i = 0; i < 50; i++) stone.Step(.1f);
            Require(stoneworks.Inventory[(int)ItemType.StoneBlock] == 1 && stoneworks.Inventory[(int)ItemType.RawStone] == 2, "Stone block recipe did not conserve inputs");
            stoneworks.ActiveRecipe = RecipeType.StoneTile;
            for (int i = 0; i < 60; i++) stone.Step(.1f);
            Require(stoneworks.Inventory[(int)ItemType.StoneTile] == 2 && stoneworks.Inventory[(int)ItemType.RawStone] == 0, "Stone tile recipe failed");
            SimulationEngine clay = ResourceFixture(); BuildingData kiln = clay.World.Buildings[0]; kiln.Type = BuildingType.Kiln; kiln.Inventory[(int)ItemType.Clay] = 2;
            for (int i = 0; i < 30; i++) clay.Step(.1f);
            Require(kiln.Inventory[(int)ItemType.Brick] == 0 && kiln.Inventory[(int)ItemType.Clay] == 2, "Bricks produced without fuel");
            kiln.Inventory[(int)ItemType.Firewood] = 1; clay.World.Units[0].CarriedFood = 0; clay.World.Units[0].DecisionTimer = 0; clay.World.Units[0].Task = UnitTask.Idle; clay.World.Units[0].Path.Clear(); clay.World.Units[0].PathCursor = 0; clay.World.Units[0].X = 16.5f; clay.World.Units[0].Y = 16.5f;
            for (int i = 0; i < 80; i++) clay.Step(.1f);
            Require(kiln.Inventory[(int)ItemType.Brick] == 2 && kiln.Inventory[(int)ItemType.Clay] == 0 && kiln.Inventory[(int)ItemType.Firewood] == 0, "Fuel-backed brick recipe failed: progress=" + kiln.ProcessProgress + " brick=" + kiln.Inventory[(int)ItemType.Brick] + " task=" + clay.World.Units[0].Task + " worker=" + kiln.WorkerId + " cargo=" + clay.World.Units[0].CargoAmount);
        });
        Check("resources / actual hauling supplies a separate workshop", () =>
        {
            SimulationEngine e = ResourceFixture(); WorldState w = e.World; SettlementData s = w.Settlements[0]; s.Wood = 100;
            BuildingData camp = w.Buildings[0], works = ResourceBuilding(w, BuildingType.Stoneworks, 20, 16);
            w.Units[0].X = 13.5f;
            camp.Inventory[(int)ItemType.RawStone] = 2;
            bool pickup = false, inTransit = false;
            for (int i = 0; i < 300 && works.Inventory[(int)ItemType.StoneBlock] == 0; i++)
            {
                e.Step(.1f); UnitData u = w.Units[0]; pickup |= u.Task == UnitTask.Haul; inTransit |= u.CargoKind == ItemType.RawStone && u.CargoAmount > 0;
                float total = camp.Inventory[(int)ItemType.RawStone] + works.Inventory[(int)ItemType.RawStone] + (u.CargoKind == ItemType.RawStone ? u.CargoAmount : 0) + works.Inventory[(int)ItemType.StoneBlock] * 2;
                Require(Math.Abs(total - 2) < .001f, "Hauling lost or duplicated stone"); Require(w.Validate(out string error), error);
            }
            Require(pickup && inTransit && works.Inventory[(int)ItemType.StoneBlock] == 1, "Remote stone turned into blocks without an actual haul or never arrived");
        });
        Check("resources / blocked cargo remains carried and a repaired route restores delivery", () =>
        {
            SimulationEngine e = ResourceFixture(); WorldState w = e.World; UnitData u = w.Units[0]; SettlementData s = w.Settlements[0];
            for (int y = 0; y < 32; y++) e.Paint(TerrainType.DeepWater, 18, y, 0);
            u.X = 21.5f; u.Y = 16.5f; u.CargoKind = ItemType.Log; u.CargoAmount = 1;
            for (int i = 0; i < 100; i++) e.Step(.1f);
            Require(u.CargoAmount == 1 && w.GetSettlementStock(s.Id, ItemType.Log) == 0 && s.Wood == 0 && u.X > 18, "Cargo teleported through water");
            e.Paint(TerrainType.Grass, 18, 16, 0);
            for (int i = 0; i < 250 && w.GetSettlementStock(s.Id, ItemType.Log) == 0; i++) e.Step(.1f);
            Require(w.GetSettlementStock(s.Id, ItemType.Log) > 0 && u.CargoAmount == 0, "Repaired bridge did not restore delivery"); Require(w.Validate(out string error), error);
        });
        Check("resources / all deposits use saved individual regrowth times and incompatible terrain pauses", () =>
        {
            WorldState w = WorldGenerator.Generate(51, 16, 16); Array.Clear(w.ResourceNodes, 0, w.ResourceNodes.Length);
            for (int i = 0; i < w.Cells.Length; i++) { w.Cells[i].Terrain = TerrainType.Grass; w.Cells[i].Vegetation = 0; w.Cells[i].Fire = 0; }
            SimulationEngine e = new SimulationEngine(w);
            for (int type = 1; type <= 11; type++) w.ResourceNodes[w.Index(type, 5)] = new ResourceNodeData { Type = (ResourceType)type, Capacity = 6, Amount = 0, RegrowTicksRemaining = ResourceCatalog.GetRegrowTicks((ResourceType)type) };
            int clay = w.Index((int)ResourceType.Clay, 5); w.Cells[clay].Terrain = TerrainType.DeepWater;
            for (int i = 1; i <= 6000; i++)
            {
                e.Step(.1f);
                for (int type = 1; type <= 11; type++)
                {
                    if (type == (int)ResourceType.Clay) continue;
                    ResourceNodeData node = w.ResourceNodes[w.Index(type, 5)];
                    Require(node.Amount == (i >= ResourceCatalog.GetRegrowTicks((ResourceType)type) ? 6 : 0), "Regrowth occurred on the wrong tick for " + type);
                }
            }
            Require(w.ResourceNodes[clay].Amount == 0 && w.ResourceNodes[clay].RegrowTicksRemaining == 1200, "Water did not pause incompatible regrowth");
            w.Cells[clay].Terrain = TerrainType.Grass;
            for (int i = 0; i < 1200; i++) e.Step(.1f);
            Require(w.ResourceNodes[clay].Amount == 6 && w.Cells[w.Index(1, 5)].Vegetation == 1, "Regrowth did not restore node and tree canopy");
            Require(w.Validate(out string error), error);
        });
        Check("resources / interruption and workshop reassignment release old slots and preserve progress", () =>
        {
            SimulationEngine e = ResourceFixture(); WorldState w = e.World; UnitData u = w.Units[0];
            BuildingData first = ResourceBuilding(w, BuildingType.Sawmill, 24, 16), second = ResourceBuilding(w, BuildingType.Sawmill, 18, 16);
            first.Inventory[(int)ItemType.Log] = 1; second.Inventory[(int)ItemType.Log] = 1;
            e.Step(.1f); Require(u.Task == UnitTask.Process && u.TargetBuildingId == first.Id, "Fixture did not reserve first station");
            w.Cells[w.Index(first.X, first.Y)].Fire = .3f; u.DecisionTimer = 0;
            e.Step(.1f); Require(u.Task == UnitTask.Process && u.TargetBuildingId == second.Id && first.WorkerId == 0 && second.WorkerId == u.Id, "Same-task reassignment leaked old worker slot");
            Require(w.Validate(out string error), error);
            e.ApplyPower(PowerType.Rain, first.X, first.Y, 1);
            for (int i = 0; i < 16; i++) e.Step(.1f);
            float progress = second.ProcessProgress, logs = second.Inventory[(int)ItemType.Log];
            Require(progress > 0 && progress < 3, "Fixture never started work");
            e.ApplyPower(PowerType.Fire, (int)u.X, (int)u.Y, 1); e.Step(.1f);
            Require(u.Task == UnitTask.Flee && second.WorkerId == 0 && second.ProcessProgress == progress && second.Inventory[(int)ItemType.Log] == logs, "Fire interruption consumed inputs or lost work progress");
            Require(w.Validate(out error), error);
        });
        Check("resources / cargo death and storage destruction are explicit sinks", () =>
        {
            SimulationEngine e = ResourceFixture(); WorldState w = e.World; UnitData u = w.Units[0]; BuildingData camp = w.Buildings[0];
            u.CargoKind = ItemType.IronOre; u.CargoAmount = 1; camp.Inventory[(int)ItemType.Log] = 4;
            e.ApplyPower(PowerType.Meteor, 16, 16, 3);
            Require(w.Units.Count == 0 && w.GetBuilding(camp.Id) == null && w.LostResources[(int)ItemType.IronOre] == 1 && w.LostResources[(int)ItemType.Log] == 4, "Destroyed resources disappeared without a sink or reached storage");
            Require(w.Validate(out string error), error);
        });
        Check("resources / legacy stock migration consumes no RNG and save continuation preserves the whole job", () =>
        {
            SimulationEngine original = ResourceFixture(); original.World.Settlements[0].Wood = 23; original.World.Settlements[0].Stone = 7; original.World.Units[0].CarriedWood = 2;
            System.Text.Json.Nodes.JsonNode old = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(original.World, Json));
            foreach (string field in new[] { "ResourceVersion", "ResourceRevision", "ResourceNodes", "LostResources" }) old.AsObject().Remove(field);
            foreach (System.Text.Json.Nodes.JsonNode node in old["Buildings"].AsArray()) foreach (string field in new[] { "Inventory", "WorkerId", "ActiveRecipe", "ProcessProgress" }) node.AsObject().Remove(field);
            foreach (System.Text.Json.Nodes.JsonNode node in old["Units"].AsArray()) foreach (string field in new[] { "CargoKind", "CargoAmount", "PickupKind", "ResourceCell", "SourceBuildingId", "TargetBuildingId" }) node.AsObject().Remove(field);
            WorldState loaded = JsonSerializer.Deserialize<WorldState>(old.ToJsonString(), Json); uint rng = loaded.RngState; int nextId = loaded.NextId;
            SimulationEngine a = new SimulationEngine(loaded);
            Require(loaded.ResourceVersion == 3 && loaded.RngState == rng && loaded.NextId == nextId && loaded.Settlements[0].Wood == 23 && loaded.Settlements[0].Stone == 0 && loaded.Units[0].CarriedWood + loaded.GroundStocks.Where(g => g.Item == ItemType.Plank).Sum(g => g.Amount) == 2 && loaded.GetSettlementStock(loaded.Settlements[0].Id, ItemType.RawStone) == 7, "Legacy migration altered or duplicated stock");
            string once = Hash(loaded); new SimulationEngine(loaded); Require(once == Hash(loaded), "Migration repeated");
            for (int i = 0; i < 71; i++) a.Step(.1f);
            SimulationEngine b = new SimulationEngine(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(a.World, Json), Json));
            for (int i = 0; i < 300; i++) { a.Step(.1f); b.Step(.1f); }
            Require(Hash(a.World) == Hash(b.World), "Resource continuation lost saved state");
        });
        Check("resources / geographic distribution and balances reflect real unequal stocks", () =>
        {
            WorldState w = WorldGenerator.Generate(873, 128, 128, WorldPreset.Continent);
            HashSet<ResourceType> kinds = new HashSet<ResourceType>(w.ResourceNodes.Where(n => n.Amount > 0).Select(n => n.Type));
            Require(kinds.Count >= 8, "Resource distribution lacks variety");
            int richRegions = 0, emptyRegions = 0;
            for (int y = 0; y < 128; y += 16) for (int x = 0; x < 128; x += 16)
            {
                int metals = 0;
                for (int dy = 0; dy < 16; dy++) for (int dx = 0; dx < 16; dx++) { ResourceType t = w.ResourceNodes[w.Index(x + dx, y + dy)].Type; if (t == ResourceType.IronOre || t == ResourceType.CopperOre || t == ResourceType.GoldOre || t == ResourceType.SilverOre || t == ResourceType.Tin) metals++; }
                if (metals > 0) richRegions++; else emptyRegions++;
            }
            Require(richRegions > 0 && emptyRegions > richRegions, "Every region was given metal resources");
            SimulationEngine e = ResourceFixture(); SettlementData s = e.World.Settlements[0]; e.World.Buildings[0].Inventory[(int)ItemType.IronOre] = 7;
            ResourceBalance balance = e.World.GetResourceBalance(s.Id, ItemType.IronOre);
            Require(balance.Stock == 7 && balance.Surplus == 4 && balance.Deficit == 0, "Balance did not measure actual stored stock");
            e.Spawn(SpeciesType.Orc, 26, 26, 8); e.Spawn(SpeciesType.Human, 16, 16, 8); e.Step(2);
            Require(e.World.Settlements.All(t => t.EnemyId == 0), "Different species started an unrequested automatic war");
        });
        Check("resources / every raw material is physically harvested and deposited", () =>
        {
            for (int type = 1; type <= 11; type++)
            {
                SimulationEngine e = ResourceFixture(); WorldState w = e.World; SettlementData s = w.Settlements[0]; s.Wood = 100; w.Buildings[0].Inventory[(int)ItemType.Firewood] = 10;
                ResourceType kind = (ResourceType)type; ItemType item = ResourceCatalog.GetOutput(kind); int cell = w.Index(20, 16);
                w.ResourceNodes[cell] = new ResourceNodeData { Type = kind, Amount = 3, Capacity = 3 }; w.Cells[cell].Vegetation = kind == ResourceType.Tree ? 1 : 0;
                bool carried = false;
                for (int i = 0; i < 350 && w.GetSettlementStock(s.Id, item) + w.GetCitizenStock(w.Units[0].Id, item) < 2.99f; i++) { e.Step(.1f); carried |= w.Units[0].CargoKind == item && w.Units[0].CargoAmount > 0; }
                Require(carried && w.GetSettlementStock(s.Id, item) + w.GetCitizenStock(w.Units[0].Id, item) >= 2.99f && w.ResourceNodes[cell].Amount == 0, "Resource was never physically delivered: " + kind);
                Require(w.Validate(out string error), error);
            }
            SimulationEngine remainder = ResourceFixture(); int tail = remainder.World.Index(20, 16);
            remainder.World.ResourceNodes[tail] = new ResourceNodeData { Type = ResourceType.Stone, Amount = .005f, Capacity = 3 };
            for (int t = 0; t < 250; t++) remainder.Step(.1f);
            Require(remainder.World.ResourceNodes[tail].Amount == 0 && remainder.World.ResourceNodes[tail].RegrowTicksRemaining > 0, "A tiny interrupted deposit never exhausted");
            Require(Math.Abs(remainder.World.GetSettlementStock(remainder.World.Settlements[0].Id, ItemType.RawStone) + remainder.World.GetCitizenStock(remainder.World.Units[0].Id, ItemType.RawStone) - .005f) < .00001f, "Tiny remainder was lost instead of delivered");
        });
        Check("resources / zero-plank settlement bootstraps real workshops and wood stone clay chains", () =>
        {
            SimulationEngine e = ResourceFixture(); WorldState w = e.World; SettlementData s = w.Settlements[0]; e.Spawn(SpeciesType.Human, 16, 16, 3);
            foreach (UnitData u in w.Units) { u.Profession = UnitProfession.Forester; u.Fatigue = 0; u.Hunger = 0; u.SocialNeed = 0; u.DecisionTimer = 0; }
            int tree = w.Index(20, 16), stone = w.Index(20, 19), clay = w.Index(20, 22);
            w.ResourceNodes[tree] = new ResourceNodeData { Type = ResourceType.Tree, Amount = 60, Capacity = 60 }; w.Cells[tree].Vegetation = 1;
            w.ResourceNodes[stone] = new ResourceNodeData { Type = ResourceType.Stone, Amount = 12, Capacity = 12 };
            w.ResourceNodes[clay] = new ResourceNodeData { Type = ResourceType.Clay, Amount = 12, Capacity = 12 };
            Require(s.Wood == 0 && w.Buildings.All(b => b.Type != BuildingType.Sawmill), "Founding bypassed the campfire");
            long firstMill = 0;
            for (int i = 0; i < 6000; i++)
            {
                e.Step(.1f); if (firstMill == 0 && w.Buildings.Any(b => b.Type == BuildingType.Sawmill)) firstMill = w.Tick;
                if (i % 100 == 0) Require(w.Validate(out string error), error);
                if (w.GetSettlementStock(s.Id, ItemType.StoneBlock) > 0 && w.GetSettlementStock(s.Id, ItemType.Brick) > 0) break;
            }
            Require(firstMill >= 1000, "Sawmill appeared before ten campfire planks: " + firstMill);
            Require(w.GetSettlementStock(s.Id, ItemType.StoneBlock) > 0 && w.GetSettlementStock(s.Id, ItemType.Brick) > 0, "Complete workshop chain stalled: tick=" + w.Tick + " buildings=" + string.Join(",",w.Buildings.Select(b=>b.Type)) + " units=" + string.Join(",",w.Units.Select(u=>u.Id+":"+u.Profession+":"+u.Task)) + " planks=" + s.Wood + " blocks=" + w.GetSettlementStock(s.Id, ItemType.StoneBlock) + " logs=" + w.GetSettlementStock(s.Id, ItemType.Log) + " tree=" + w.ResourceNodes[tree].Amount + " stone=" + w.GetSettlementStock(s.Id, ItemType.RawStone) + " clay=" + w.GetSettlementStock(s.Id, ItemType.Clay) + " fuel=" + w.GetSettlementStock(s.Id, ItemType.Firewood));
            Metrics.Add(new { scenario = "zero-plank-workshop-chain", firstSawmillTick = firstMill, firstBlockAndBrickByTick = w.Tick, units = w.Units.Count, buildings = w.Buildings.Count });
        });
        Check("households / barter transfers real goods and natural tax once on site", () =>
        {
            SimulationEngine e = TradeFixture(EconomicPolicy.Market); WorldState w = e.World; BuildingData camp = w.Buildings[0]; UnitData buyer = w.Units[0], seller = w.Units[1];
            buyer.X = 21.5f; buyer.Y = 16.5f; e.Step(.1f);
            Require(w.Trades.Count == 0 && camp.Inventory[(int)ItemType.Log] == 0, "A remote worker acquired goods");
            for (int i = 0; i < 60 && w.Trades.Count == 0; i++) e.Step(.1f);
            Require(w.Trades.Count == 1 && w.Trades[0].Kind == TradeKind.Barter && w.Trades[0].PaymentItem == ItemType.IronOre, "No genuine on-site barter");
            Require(Math.Abs(w.GetCitizenStock(seller.Id, ItemType.IronOre) - .8f) < .00001f && Math.Abs(w.GetTreasuryStock(buyer.SettlementId, ItemType.IronOre) - .2f) < .00001f && w.GetCitizenStock(buyer.Id, ItemType.IronOre) == 0 && camp.Inventory[(int)ItemType.Log] == 1, "Payment or tax duplicated/lost");
            SimulationEngine clone = new SimulationEngine(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w, Json), Json));
            for (int i = 0; i < 110; i++) { e.Step(.1f); clone.Step(.1f); }
            Require(Hash(w) == Hash(clone.World) && w.Trades.Count == 1 && w.Settlements[0].Wood == 1, "Save reload charged again or did not finish the acquired input");
            Require(w.Validate(out string error), error);
        });
        Check("households / free gifts and insolvency preserve honest startup", () =>
        {
            SimulationEngine free = TradeFixture(EconomicPolicy.FreeDistribution); free.World.Buildings[0].PersonalStocks.RemoveAll(p => p.OwnerId == free.World.Units[0].Id); free.Step(.1f);
            Require(free.World.Trades.Count == 1 && free.World.Trades[0].Kind == TradeKind.Gift && free.World.Trades[0].PaymentAmount == 0 && free.World.Buildings[0].Inventory[(int)ItemType.Log] == 1, "Free gift did not deliver real input");
            SimulationEngine poor = TradeFixture(EconomicPolicy.Market); WorldState w = poor.World; w.Buildings[0].PersonalStocks.RemoveAll(p => p.OwnerId == w.Units[0].Id);
            int cell = w.Index(20,16); w.ResourceNodes[cell] = new ResourceNodeData {Type=ResourceType.Tree,Amount=4,Capacity=4}; w.Cells[cell].Vegetation = 1;
            for (int t=0;t<600 && w.Settlements[0].Wood==0;t++) poor.Step(.1f);
            Require(w.Settlements[0].Wood > 0 && w.Trades.All(t=>t.Kind != TradeKind.Barter), "Zero personal funds blocked communal startup or fabricated a payment");
        });
        Check("households / complete recipe reserves fuel and shares one barter budget", () =>
        {
            SimulationEngine e = TradeFixture(EconomicPolicy.Market); WorldState w=e.World; BuildingData kiln=w.Buildings[0]; UnitData buyer=w.Units[0], seller=w.Units[1]; kiln.Type=BuildingType.Kiln; kiln.PersonalStocks.Clear(); kiln.Inventory[(int)ItemType.Clay]=1;
            kiln.PersonalStocks.Add(new OwnedGoodsData{OwnerId=buyer.Id,Item=ItemType.Firewood,Amount=1}); kiln.PersonalStocks.Add(new OwnedGoodsData{OwnerId=buyer.Id,Item=ItemType.Log,Amount=1}); kiln.PersonalStocks.Add(new OwnedGoodsData{OwnerId=seller.Id,Item=ItemType.Clay,Amount=1});
            e.Step(.1f); Require(kiln.Inventory[(int)ItemType.Clay]==2 && kiln.Inventory[(int)ItemType.Firewood]==1 && w.Trades.Count(t=>t.Kind==TradeKind.Barter)==1 && w.Trades.First(t=>t.Kind==TradeKind.Barter).PaymentItem==ItemType.Log, "Recipe fuel was used as barter tender");
            for(int t=0;t<80;t++)e.Step(.1f); Require(kiln.Inventory[(int)ItemType.Brick]==2, "Fully funded brick recipe stalled");
            SimulationEngine insufficient=TradeFixture(EconomicPolicy.Market); WorldState z=insufficient.World; BuildingData k=z.Buildings[0]; k.Type=BuildingType.Kiln;k.PersonalStocks.Clear();
            k.PersonalStocks.Add(new OwnedGoodsData{OwnerId=z.Units[0].Id,Item=ItemType.IronOre,Amount=2}); k.PersonalStocks.Add(new OwnedGoodsData{OwnerId=z.Units[1].Id,Item=ItemType.Clay,Amount=2});k.PersonalStocks.Add(new OwnedGoodsData{OwnerId=z.Units[1].Id,Item=ItemType.Firewood,Amount=1});
            insufficient.Step(.1f);Require(z.Trades.Count==0 && k.Inventory[(int)ItemType.Clay]==0 && z.GetCitizenStock(z.Units[0].Id,ItemType.IronOre)==2,"Incomplete barter budget partly committed");
        });
        Check("households / combined load limit and legacy piles conserve every material", () =>
        {
            SimulationEngine source=ResourceFixture();WorldState w=source.World;UnitData u=w.Units[0];u.CargoKind=ItemType.IronOre;u.CargoAmount=3;u.CarriedFood=2.8f;u.CarriedWood=2;w.ResourceVersion=1;
            uint rng=w.RngState;int next=w.NextId;float x=u.X,y=u.Y; SimulationEngine e=new SimulationEngine(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w,Json),Json));w=e.World;u=w.Units[0];
            Require(SimulationEngine.CarriedLoad(u)<=1 && Math.Abs(SimulationEngine.CarriedLoad(u)+w.GroundStocks.Sum(g=>g.Amount)-7.8f)<.00001f && w.GroundStocks.All(g=>g.X==x&&g.Y==y),"Legacy overflow was removed or teleported");
            Require(w.ResourceVersion==3&&w.RngState==rng&&w.NextId==next,"Carry migration consumed IDs or RNG");string once=Hash(w);new SimulationEngine(w);Require(once==Hash(w),"Carry migration repeated");
            for(int t=0;t<250;t++){e.Step(.1f);Require(w.Units.All(a=>SimulationEngine.CarriedLoad(a)<=1.00001f),"Pickup exceeded total carrying capacity");}
            Require(w.GetSettlementStock(u.SettlementId,ItemType.IronOre)==3 && w.Settlements[0].Wood>=2,"Overflow was never physically collected in separate trips");
            Require(w.Validate(out string error),error);
        });
        Check("demography / sex migration deterministic and births require both adult sexes", () =>
        {
            SimulationEngine e=ResourceFixture();e.Spawn(SpeciesType.Human,16,16,1);WorldState w=e.World;w.Laws.Births=true;
            foreach(UnitData u in w.Units){u.X=16.5f;u.Y=16.5f;u.Sex=Sex.Female;u.Task=UnitTask.Idle;u.DecisionTimer=50;}w.Settlements[0].BirthTimer=20;e.Step(1);Require(w.Units.Count==2,"Single-sex adults produced a birth");
            w.Units[1].Sex=Sex.Male;e.Step(1);Require(w.Units.Count==3 && w.Units[2].IsChild && w.Units[2].Profession==UnitProfession.None,"Mixed adult parents did not create a child");
            w.DemographyVersion=0;uint rng=w.RngState;int next=w.NextId;SimulationEngine a=new SimulationEngine(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w,Json),Json));SimulationEngine b=new SimulationEngine(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w,Json),Json));
            Require(Hash(a.World)==Hash(b.World)&&a.World.RngState==rng&&a.World.NextId==next,"Sex migration changed RNG/IDs or was not deterministic");
        });
        Check("demography / child laws gate farming heavy cargo and private trade", () =>
        {
            SimulationEngine e=ResourceFixture();e.Spawn(SpeciesType.Human,16,16,1);WorldState w=e.World;UnitData child=w.Units[1];child.Age=13;child.X=16.5f;child.Y=16.5f;child.DecisionTimer=0;child.Fatigue=0;child.Hunger=0;SettlementData s=w.Settlements[0];s.Food=0;
            e.SetEconomicPolicy(s.Id,EconomicPolicy.FreeDistribution,0,ChildLaborPolicy.Forbidden,ResourceFocus.Balanced);for(int t=0;t<30;t++)e.Step(.1f);Require(child.Task!=UnitTask.Farm&&child.Task!=UnitTask.Gather&&child.Task!=UnitTask.Process&&child.CarriedFood==0&&child.HarvestAmount==0,"Forbidden child performed production");
            e.SetEconomicPolicy(s.Id,EconomicPolicy.FreeDistribution,0,ChildLaborPolicy.LightWork,ResourceFocus.Balanced);child.Task=UnitTask.Idle;child.Path.Clear();child.PathCursor=0;child.DecisionTimer=0;w.Buildings.First(b=>b.Type==BuildingType.Farm).HarvestReady=8;bool farmed=false,delivered=false;
            for(int t=0;t<120;t++){e.Step(.1f);farmed|=child.HarvestAmount>0;delivered|=farmed&&child.HarvestAmount==0&&(s.Food>0||w.GetAgriculturalStock(s.Id,AgriculturalProduct.Grain)>0);Require(SimulationEngine.CarriedLoad(child)<=1.00001f&&child.Task!=UnitTask.Gather&&child.Task!=UnitTask.Process,"Light work exceeded its scope");}Require(farmed&&delivered,"Permitted adolescent failed to harvest and physically deliver the crop");
            child.CargoKind=ItemType.IronOre;child.CargoAmount=1;child.CarriedFood=0;child.HarvestAmount=0;child.HarvestProduct=AgriculturalProduct.None;child.Task=UnitTask.Return;child.DecisionTimer=20;e.Step(.1f);Require(child.CargoAmount==0&&w.GroundStocks.Any(g=>g.Item==ItemType.IronOre&&g.Amount==1),"Child retained heavy legacy cargo");
            child.CarriedFood=.4f;child.Task=UnitTask.Farm;e.SetEconomicPolicy(s.Id,EconomicPolicy.FreeDistribution,0,ChildLaborPolicy.Forbidden,ResourceFocus.Balanced);Require(child.CarriedFood==0&&w.GroundStocks.Any(g=>g.IsFood&&Math.Abs(g.Amount-.4f)<.00001f),"Revoking child work lost carried food");
            child.Age=9;e.SetEconomicPolicy(s.Id,EconomicPolicy.Market,.3f,ChildLaborPolicy.LightWork,ResourceFocus.Metals);for(int t=0;t<20;t++)e.Step(.1f);Require(child.Task!=UnitTask.Farm&&child.Task!=UnitTask.Process&&w.Trades.All(t=>t.SellerId!=child.Id&&t.BuyerId!=child.Id),"Young child worked or traded");
        });
        Check("demography / learning needs a living nearby adult and affects adult output", () =>
        {
            SimulationEngine e=ResourceFixture();e.Spawn(SpeciesType.Human,16,16,1);WorldState w=e.World;UnitData child=w.Units[1], mentor=w.Units[0];child.Age=10;child.X=mentor.X=16.5f;child.Y=mentor.Y=16.5f;child.Task=UnitTask.Learn;child.WorkTimer=3;child.Path.Clear();child.PathCursor=0;child.DecisionTimer=50;mentor.DecisionTimer=50;
            for(int t=0;t<20;t++)e.Step(.1f);Require(child.Education>0,"Actual supervised learning did not advance");
            float learned=child.Education;mentor.X=28.5f;mentor.Y=28.5f;child.Task=UnitTask.Learn;child.WorkTimer=3;for(int t=0;t<20;t++)e.Step(.1f);Require(child.Education==learned,"Learning progressed without nearby mentor");
            SimulationEngine fast=ResourceFixture();UnitData adult=fast.World.Units[0];adult.Education=1;fast.World.Buildings[0].Inventory[(int)ItemType.Log]=1;for(int t=0;t<92;t++)fast.Step(.1f);Require(fast.World.Settlements[0].Wood==1,"Education has no actual work effect");
        });
        Check("households / private property death and destruction preserve explicit accounting", () =>
        {
            SimulationEngine e=TradeFixture(EconomicPolicy.Market);WorldState w=e.World;BuildingData b=w.Buildings[0];UnitData seller=w.Units[1];seller.Health=0;e.Step(.1f);
            Require(w.GetCitizenStock(seller.Id,ItemType.Log)==0&&b.Inventory[(int)ItemType.Log]==1,"Dead owner left inaccessible or duplicated lots");
            b.TaxStocks[(int)ItemType.SilverOre]=.2f;e.ApplyPower(PowerType.Meteor,b.X,b.Y,3);Require(Math.Abs(w.LostResources[(int)ItemType.SilverOre]-.2f)<.00001f&&w.LostResources[(int)ItemType.IronOre]==1,"Private or tax stock vanished on storage destruction");Require(w.Validate(out string error),error);
        });
        Check("households / ruler review explains real policies and missing legacy containers migrate", () =>
        {
            SimulationEngine e=ResourceFixture();WorldState w=e.World;SettlementData s=w.Settlements[0];UnitData ruler=w.GetUnit(s.RulerId);ruler.Generosity=0;ruler.Prudence=.1f;ruler.Ambition=.9f;s.NextPolicyReviewTick=w.Tick;e.Step(.1f);
            Require(s.EconomyPolicy==EconomicPolicy.Market&&s.TaxRate==.3f&&s.ChildLabor==ChildLaborPolicy.LightWork&&s.PolicyRulerId==ruler.Id&&s.LastDecreeReason.Length>0,"Ruler traits did not affect actual policy");
            s.Food=0;s.NextPolicyReviewTick=w.Tick;e.Step(.1f);Require(s.EconomyPolicy==EconomicPolicy.FreeDistribution&&s.TaxRate==0&&s.ResourceFocus==ResourceFocus.Balanced,"Food shortage did not change actual decree");
            ruler.Health=0;e.Step(.1f);Require(s.RulerId==0&&s.EconomyPolicy==EconomicPolicy.FreeDistribution&&s.TaxRate==0&&s.ChildLabor==ChildLaborPolicy.Forbidden,"No-ruler fallback contradicts its explanation");
            SimulationEngine old=ResourceFixture();WorldState legacy=old.World;legacy.ResourceVersion=0;legacy.EconomyVersion=0;legacy.Settlements[0].Stone=7;foreach(BuildingData b in legacy.Buildings){b.PersonalStocks=null;b.TaxStocks=null;}
            new SimulationEngine(legacy);Require(legacy.GetSettlementStock(legacy.Settlements[0].Id,ItemType.RawStone)==7&&legacy.Validate(out _),"Null fields from a legacy JSON broke physical stone migration");
            legacy.Units[0].CarriedFood=.6f;legacy.Units[0].CarriedWood=.6f;Require(!legacy.Validate(out _),"Combined overload was accepted despite individual loads under one");
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

    private static SimulationEngine TradeFixture(EconomicPolicy policy)
    {
        SimulationEngine e=ResourceFixture();e.Spawn(SpeciesType.Human,16,16,1);WorldState w=e.World;UnitData buyer=w.Units[0],seller=w.Units[1];
        seller.X=28.5f;seller.Y=28.5f;seller.DecisionTimer=1000;seller.Task=UnitTask.Idle;
        e.SetEconomicPolicy(buyer.SettlementId,policy,.2f,ChildLaborPolicy.Forbidden,ResourceFocus.Balanced);
        w.Buildings[0].PersonalStocks.Add(new OwnedGoodsData{OwnerId=seller.Id,Item=ItemType.Log,Amount=1});w.Buildings[0].PersonalStocks.Add(new OwnedGoodsData{OwnerId=buyer.Id,Item=ItemType.IronOre,Amount=1});return e;
    }

    private static SimulationEngine ResourceFixture()
    {
        SimulationEngine e = BehaviorWorld(); WorldState w = e.World;
        Array.Clear(w.ResourceNodes, 0, w.ResourceNodes.Length); Array.Clear(w.LostResources, 0, w.LostResources.Length);
        for (int i = 0; i < w.Cells.Length; i++) { w.Cells[i].Vegetation = 0; w.Cells[i].Fire = 0; }
        w.Laws.Births = false; w.Laws.Aging = false; w.Laws.VegetationGrowth = false; w.Laws.FireSpread = false;
        e.Spawn(SpeciesType.Human, 16, 16); w.Settlements[0].Food = 1000; w.Settlements[0].Wood = 0;
        UnitData u = w.Units[0]; u.X = 16.5f; u.Y = 16.5f; u.Hunger = 0; u.Fatigue = 0; u.SocialNeed = 0; u.DecisionTimer = 0; u.Profession = UnitProfession.Forester;
        return e;
    }

    private static BuildingData ResourceBuilding(WorldState world, BuildingType type, int x, int y)
    {
        BuildingData b = new BuildingData { Id = world.NextId++, SettlementId = world.Settlements[0].Id, X = x, Y = y, Type = type };
        world.Buildings.Add(b); world.Cells[world.Index(x, y)].BuildingId = b.Id; world.Cells[world.Index(x, y)].SettlementId = b.SettlementId; return b;
    }

    private static SimulationEngine BehaviorWorld()
    {
        WorldState world = WorldGenerator.Generate(711, 32, 32);
        for (int i = 0; i < world.Cells.Length; i++) { world.Cells[i].Terrain = TerrainType.Grass; world.Cells[i].Height = .55f; world.Cells[i].Vegetation = .7f; world.Cells[i].River = false; Hydrology.RefreshCell(world, i); }
        ResourceCatalog.Generate(world);
        world.Laws.Warfare = false;
        return new SimulationEngine(world);
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
        if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) return;
        Stopwatch watch = Stopwatch.StartNew();
        try { test(); Results.Add(new { name, passed = true, milliseconds = watch.Elapsed.TotalMilliseconds }); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failures++; Results.Add(new { name, passed = false, error = e.ToString(), milliseconds = watch.Elapsed.TotalMilliseconds }); Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }
}
