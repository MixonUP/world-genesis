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
        Check("behavior / fire interrupts work and preserves carried resources", () =>
        {
            SimulationEngine e = BehaviorWorld(); e.Spawn(SpeciesType.Human, 16, 16);
            UnitData u = e.World.Units[0]; u.X = 16.5f; u.Y = 16.5f; u.Task = UnitTask.Gather; u.TargetCell = e.World.Index(16, 16); u.CarriedWood = 2; u.DecisionTimer = 50;
            e.ApplyPower(PowerType.Fire, 16, 16, 1); e.Step(.1f);
            Require(u.Task == UnitTask.Flee && u.CarriedWood == 2 && u.Path.Count > 0, "Fire did not interrupt work safely");
            for (int i = 0; i < 35; i++) e.Step(.1f);
            Require(u.Health > 0 && e.World.Cells[e.World.Index((int)u.X, (int)u.Y)].Fire < .1f, "Escape did not reach safety");
            Require(e.World.Validate(out string error), error);
        });
        Check("behavior / eating is a single interruptible resource transaction", () =>
        {
            SimulationEngine e = BehaviorWorld(); e.Spawn(SpeciesType.Human, 16, 16);
            UnitData u = e.World.Units[0]; SettlementData home = e.World.Settlements[0];
            u.X = 16.5f; u.Y = 16.5f; u.Hunger = .8f; u.Fatigue = 0; u.DecisionTimer = 0; home.Food = 20;
            e.Step(.1f); Require(u.Task == UnitTask.Eat && home.Food == 20 && u.WorkTimer > 0, "Eating was instant or not selected");
            e.ApplyPower(PowerType.Fire, 16, 16, 1); e.Step(.1f);
            Require(u.Task == UnitTask.Flee && home.Food == 20, "Interrupted meal consumed stock");
            e.ApplyPower(PowerType.Rain, 16, 16, 10);
            u.X = 16.5f; u.Y = 16.5f; u.Task = UnitTask.Idle; u.Path.Clear(); u.PathCursor = 0; u.DecisionTimer = 0;
            for (int i = 0; i < 14; i++) e.Step(.1f);
            Require(home.Food == 19 && u.Hunger < .5f, "Meal was not exactly one stock transaction");
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
                for (int species = 0; species < 4; species++) e.Spawn((SpeciesType)species, 16, 16, 24);
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
            WorldState legacy = source.World; legacy.BehaviorVersion = 1; legacy.Tick = 50; legacy.SimulatedSeconds = 5;
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
            Require(retired.CarriedWood == 2.5f && retired.CarriedFood == .6f && retired.Fatigue == .8f && retired.SocialNeed == .9f && unchanged.Task == UnitTask.Rest && unchanged.Fatigue == .6f && unchanged.TaskStartedTick == 7, "Unrelated saved state lost");
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

    private static SimulationEngine BehaviorWorld()
    {
        WorldState world = WorldGenerator.Generate(711, 32, 32);
        for (int i = 0; i < world.Cells.Length; i++) { world.Cells[i].Terrain = TerrainType.Grass; world.Cells[i].Height = .55f; world.Cells[i].Vegetation = .7f; world.Cells[i].River = false; }
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
        Stopwatch watch = Stopwatch.StartNew();
        try { test(); Results.Add(new { name, passed = true, milliseconds = watch.Elapsed.TotalMilliseconds }); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failures++; Results.Add(new { name, passed = false, error = e.ToString(), milliseconds = watch.Elapsed.TotalMilliseconds }); Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }
}
