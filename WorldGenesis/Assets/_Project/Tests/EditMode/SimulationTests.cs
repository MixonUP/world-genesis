using System;
using NUnit.Framework;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Tests
{
    public sealed class SimulationTests
    {
        [TestCase(WorldPreset.Island)]
        [TestCase(WorldPreset.Archipelago)]
        [TestCase(WorldPreset.Continent)]
        public void GeneratorIsReproducibleAndDrainageConservesMass(WorldPreset preset)
        {
            for (int seed = 0; seed < 20; seed++)
            {
                WorldState a = WorldGenerator.Generate(seed * 797, 64, 48, preset);
                WorldState b = WorldGenerator.Generate(seed * 797, 64, 48, preset);
                Assert.AreEqual(JsonUtility.ToJson(a), JsonUtility.ToJson(b), "Seed " + seed);
                Assert.IsTrue(a.Validate(out string error), error);
                float total = 0;
                for (int i = 0; i < a.Cells.Length; i++)
                {
                    if (a.Cells[i].DrainageParent == -1) total += a.Cells[i].Flow;
                    int n = i, steps = 0;
                    while (a.Cells[n].DrainageParent >= 0)
                    {
                        int p = a.Cells[n].DrainageParent;
                        Assert.GreaterOrEqual(a.Cells[n].RouteHeight, a.Cells[p].RouteHeight);
                        n = p;
                        if (++steps > a.Cells.Length) Assert.Fail("Drainage cycle");
                    }
                    Assert.IsTrue(n % a.Width == 0 || n % a.Width == a.Width - 1 || n / a.Width == 0 || n / a.Width == a.Height - 1);
                }
                Assert.AreEqual(a.Cells.Length, total);
                if (preset != WorldPreset.Continent)
                    for (int y = 0; y < a.Height; y++) Assert.AreEqual(TerrainType.DeepWater, a.Cells[a.Index(0, y)].Terrain);
            }
        }

        [Test]
        public void SaveLoadContinuationPreservesGameplayAndRandomStream()
        {
            WorldState world = FlatWorld();
            SimulationEngine first = new SimulationEngine(world);
            first.Spawn(SpeciesType.Human, 22, 22, 12);
            first.Spawn(SpeciesType.Sheep, 35, 28, 8);
            for (int t = 0; t < 370; t++) first.Step(.1f);
            first.ApplyPower(PowerType.Fire, 26, 24, 3);
            first.Step(.04f);
            WorldState restored = JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(first.World));
            SimulationEngine second = new SimulationEngine(restored);
            for (int t = 0; t < 300; t++) { first.Step(.1f); second.Step(.1f); }
            Assert.AreEqual(JsonUtility.ToJson(first.World), JsonUtility.ToJson(second.World));
        }

        [Test]
        public void WaterBarrierCannotBeCrossedByWanderingUnits()
        {
            SimulationEngine engine = new SimulationEngine(FlatWorld());
            for (int y = 0; y < 48; y++) engine.Paint(TerrainType.DeepWater, 24, y, 0);
            engine.Spawn(SpeciesType.Sheep, 20, 24, 15);
            for (int t = 0; t < 1000; t++) engine.Step(.1f);
            Assert.Greater(engine.World.Units.Count, 0);
            foreach (UnitData unit in engine.World.Units) Assert.Less(unit.X, 24, "Unit crossed a continuous sea barrier");
        }

        [Test]
        public void EquivalentFrameDurationsProduceIdenticalFixedTicks()
        {
            SimulationEngine a = new SimulationEngine(FlatWorld());
            SimulationEngine b = new SimulationEngine(FlatWorld());
            a.Spawn(SpeciesType.Human, 24, 24, 10); b.Spawn(SpeciesType.Human, 24, 24, 10);
            for (int frame = 0; frame < 200; frame++) { a.Step(.1f); b.Step(.03f); b.Step(.07f); }
            Assert.AreEqual(200, a.World.Tick); Assert.AreEqual(a.World.Tick, b.World.Tick);
            Assert.AreEqual(JsonUtility.ToJson(a.World), JsonUtility.ToJson(b.World));
        }

        [Test]
        public void EditingLandInvalidatesAnAlreadyPlannedRoute()
        {
            SimulationEngine engine = new SimulationEngine(FlatWorld());
            engine.Spawn(SpeciesType.Sheep, 20, 24);
            UnitData sheep = engine.World.Units[0]; sheep.X = 20.5f; sheep.Y = 24.5f;
            sheep.Task = UnitTask.Wander; sheep.DecisionTimer = 100;
            for (int x = 21; x < 30; x++) sheep.Path.Add(engine.World.Index(x, 24));
            sheep.PathRevision = engine.World.NavigationRevision;
            for (int y = 0; y < 48; y++) engine.Paint(TerrainType.DeepWater, 24, y, 0);
            for (int t = 0; t < 300; t++) engine.Step(.1f);
            Assert.Less(sheep.X, 24);
            Assert.IsTrue(engine.World.IsWalkable((int)sheep.X, (int)sheep.Y));
        }

        [Test]
        public void ASeparatedResidentCannotConsumeFoodAcrossWater()
        {
            SimulationEngine engine = new SimulationEngine(FlatWorld());
            engine.Spawn(SpeciesType.Human, 15, 24);
            UnitData resident = engine.World.Units[0]; resident.X = 25.5f; resident.Y = 24.5f; resident.Hunger = .8f;
            for (int y = 0; y < 48; y++) engine.Paint(TerrainType.DeepWater, 21, y, 0);
            SettlementData home = engine.World.Settlements[0]; float foodBefore = home.Food;
            engine.Step(.1f);
            Assert.AreEqual(foodBefore, home.Food, "A disconnected stockpile cannot deliver food by proximity alone");
            Assert.Greater(resident.X, 21);
        }

        [Test]
        public void RainExtinguishesAndDestructivePowersChangeRealState()
        {
            SimulationEngine engine = new SimulationEngine(FlatWorld());
            engine.Spawn(SpeciesType.Human, 24, 24, 1);
            UnitData unit = engine.World.Units[0]; unit.X = 24.5f; unit.Y = 24.5f;
            engine.ApplyPower(PowerType.Fire, 24, 24, 3);
            engine.Step(.1f);
            Assert.Less(unit.Health, unit.MaxHealth);
            engine.ApplyPower(PowerType.Rain, 24, 24, 4);
            Assert.AreEqual(0, engine.World.Cells[engine.World.Index(24, 24)].Fire);
            engine.ApplyPower(PowerType.Heal, 24, 24, 3);
            Assert.AreEqual(unit.MaxHealth, unit.Health);
            engine.ApplyPower(PowerType.Bless, 24, 24, 3);
            float blessedHealth = unit.MaxHealth;
            engine.ApplyPower(PowerType.Bless, 24, 24, 3);
            Assert.AreEqual(blessedHealth, unit.MaxHealth, "Blessing must not stack unboundedly");
            engine.ApplyPower(PowerType.Meteor, 24, 24, 3);
            Assert.AreEqual(TerrainType.ShallowWater, engine.World.Cells[engine.World.Index(24, 24)].Terrain);
            Assert.AreEqual(0, engine.World.Units.Count);
            Assert.IsTrue(engine.World.Validate(out string error), error);
        }

        [Test]
        public void SettlersWorkBuildAndHaveChildrenWhileAnEmptyTownCannotRepopulate()
        {
            SimulationEngine engine = new SimulationEngine(FlatWorld());
            engine.World.Laws.Warfare = false;
            engine.Spawn(SpeciesType.Human, 24, 24, 12);
            int buildings = engine.World.Buildings.Count;
            for (int t = 0; t < 1800; t++) engine.Step(.1f);
            Assert.Greater(engine.World.Buildings.Count, buildings);
            Assert.Greater(engine.World.Units.Count, 12);
            SettlementData town = engine.World.Settlements[0];
            Assert.GreaterOrEqual(town.Food, 0); Assert.GreaterOrEqual(town.Wood, 0);
            engine.World.Units.Clear();
            for (int t = 0; t < 400; t++) engine.Step(.1f);
            Assert.AreEqual(0, engine.World.Units.Count, "A population counter must not invent parents");
        }

        [Test]
        public void MalformedSavesAndInvalidInputsAreRejectedBeforeMutation()
        {
            WorldState world = FlatWorld(); SimulationEngine engine = new SimulationEngine(world);
            engine.Spawn(SpeciesType.Human, 24, 24, 1);
            world.Settlements[0].ColorIndex = int.MinValue;
            Assert.IsFalse(world.Validate(out _));
            Assert.Throws<ArgumentException>(() => new SimulationEngine(world));
            world.Settlements[0].ColorIndex = 0;
            world.Units[0].X = float.NaN;
            Assert.IsFalse(world.Validate(out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.Step(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => WorldGenerator.Generate(1, int.MaxValue, 48));
        }

        [Test]
        public void ExplicitPeaceStopsNewAttacksAndSurvivesSave()
        {
            SimulationEngine engine = new SimulationEngine(FlatWorld());
            engine.Spawn(SpeciesType.Human, 20, 24, 1); engine.Spawn(SpeciesType.Orc, 29, 24, 1);
            UnitData human = engine.World.Units[0], orc = engine.World.Units[1];
            human.X = 24.5f; human.Y = 24.5f; orc.X = 25.5f; orc.Y = 24.5f;
            engine.Step(.1f);
            Assert.Less(human.Health, human.MaxHealth); Assert.Less(orc.Health, orc.MaxHealth);
            engine.SetDiplomacy(human.SettlementId, orc.SettlementId, false);
            float humanHealth = human.Health, orcHealth = orc.Health;
            engine.Step(1);
            Assert.AreEqual(humanHealth, human.Health); Assert.AreEqual(orcHealth, orc.Health);
            WorldState loaded = JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(engine.World));
            Assert.IsTrue(loaded.Validate(out string error), error);
            Assert.AreEqual(1, loaded.Relations.Count); Assert.IsFalse(loaded.Relations[0].AtWar);
        }

        [Test]
        public void PausedStepAndOffWorldBrushAreHarmless()
        {
            SimulationEngine engine = new SimulationEngine(FlatWorld());
            string before = JsonUtility.ToJson(engine.World);
            engine.Step(0);
            engine.Paint(TerrainType.DeepWater, int.MinValue, int.MinValue, 3);
            engine.Spawn(SpeciesType.Orc, -4, -4, 10);
            engine.ApplyPower(PowerType.Meteor, int.MaxValue, 0, 10);
            Assert.AreEqual(before, JsonUtility.ToJson(engine.World));
        }

        [Test]
        public void FoodIsConsumedOnceAfterEatingFinishesAndNotWhenInterrupted()
        {
            SimulationEngine engine = new SimulationEngine(FlatWorld());
            engine.Spawn(SpeciesType.Human, 24, 24);
            UnitData unit = engine.World.Units[0]; SettlementData home = engine.World.Settlements[0];
            unit.X = 24.5f; unit.Y = 24.5f; unit.Hunger = .8f; unit.Fatigue = 0; unit.DecisionTimer = 0; home.Food = 20;
            engine.Step(.1f);
            Assert.AreEqual(UnitTask.Eat, unit.Task); Assert.AreEqual(20, home.Food);
            Assert.Greater(unit.WorkTimer, 0); Assert.AreEqual(engine.World.Tick, unit.TaskStartedTick);
            engine.ApplyPower(PowerType.Fire, 24, 24, 1); engine.Step(.1f);
            Assert.AreEqual(UnitTask.Flee, unit.Task); Assert.AreEqual(20, home.Food);
            engine.ApplyPower(PowerType.Rain, 24, 24, 10);
            unit.X = 24.5f; unit.Y = 24.5f; unit.Path.Clear(); unit.PathCursor = 0; unit.Task = UnitTask.Idle; unit.DecisionTimer = 0;
            for (int t = 0; t < 14; t++) engine.Step(.1f);
            Assert.AreEqual(19, home.Food); Assert.Less(unit.Hunger, .5f);
        }

        [Test]
        public void FireInterruptsWorkAndEscapesWithoutLosingCarriedResources()
        {
            SimulationEngine engine = new SimulationEngine(FlatWorld());
            engine.Spawn(SpeciesType.Human, 24, 24);
            UnitData unit = engine.World.Units[0]; unit.X = 24.5f; unit.Y = 24.5f;
            unit.Task = UnitTask.Gather; unit.TargetCell = engine.World.Index(24, 24); unit.CarriedWood = 2; unit.DecisionTimer = 50;
            engine.ApplyPower(PowerType.Fire, 24, 24, 1); engine.Step(.1f);
            Assert.AreEqual(UnitTask.Flee, unit.Task); Assert.AreEqual(2, unit.CarriedWood); Assert.Greater(unit.Path.Count, 0);
            for (int t = 0; t < 35; t++) engine.Step(.1f);
            Assert.Greater(unit.Health, 0);
            Assert.Less(engine.World.Cells[engine.World.Index((int)unit.X, (int)unit.Y)].Fire, .1f);
            Assert.IsTrue(engine.World.IsWalkable((int)unit.X, (int)unit.Y));
        }

        [Test]
        public void SheepFleesAWolfAndExhaustedCitizensRest()
        {
            SimulationEngine engine = new SimulationEngine(FlatWorld());
            engine.Spawn(SpeciesType.Sheep, 20, 24); engine.Spawn(SpeciesType.Wolf, 23, 24);
            UnitData sheep = engine.World.Units[0], wolf = engine.World.Units[1];
            sheep.X = 20.5f; sheep.Y = 24.5f; wolf.X = 23.5f; wolf.Y = 24.5f; wolf.DecisionTimer = 100;
            for (int t = 0; t < 10; t++) engine.Step(.1f);
            Assert.AreEqual(UnitTask.Flee, sheep.Task); Assert.Greater(Math.Abs(sheep.X - 23.5f) + Math.Abs(sheep.Y - 24.5f), 3);
            engine.Spawn(SpeciesType.Human, 10, 10); UnitData citizen = engine.World.Units[2];
            citizen.X = 10.5f; citizen.Y = 10.5f; citizen.Fatigue = .95f; citizen.DecisionTimer = 0; citizen.Hunger = .1f;
            engine.Step(.1f); Assert.AreEqual(UnitTask.Rest, citizen.Task);
            for (int t = 0; t < 100; t++) engine.Step(.1f);
            Assert.Less(citizen.Fatigue, .4f);
            engine.ApplyPower(PowerType.Fire, (int)citizen.X, (int)citizen.Y, 1); engine.Step(.1f);
            Assert.AreEqual(UnitTask.Flee, citizen.Task);
        }

        [Test]
        public void SocialContactRequiresALiveNearbyPartner()
        {
            SimulationEngine engine = new SimulationEngine(FlatWorld()); engine.Spawn(SpeciesType.Human, 24, 24, 2);
            UnitData a = engine.World.Units[0], b = engine.World.Units[1];
            a.X = 24.5f; a.Y = 24.5f; b.X = 25.5f; b.Y = 24.5f;
            a.SocialNeed = .9f; a.Fatigue = 0; a.DecisionTimer = 0;
            b.Task = UnitTask.Rest; b.Fatigue = .7f; b.TargetCell = engine.World.Index(25, 24); b.DecisionTimer = 100;
            engine.Step(.1f); Assert.AreEqual(UnitTask.Socialize, a.Task);
            for (int i = 0; i < 20; i++) engine.Step(.1f);
            Assert.Less(a.SocialNeed, .8f);
            b.Health = 0; engine.Step(.1f); engine.Step(.1f);
            Assert.AreNotEqual(b.Id, a.TargetId); Assert.AreNotEqual(UnitTask.Socialize, a.Task);
        }

        [Test]
        public void OriginalV1WithoutBehaviorFieldsMigratesWithoutConsumingRandomness()
        {
            SimulationEngine original = new SimulationEngine(FlatWorld()); original.Spawn(SpeciesType.Human, 24, 24, 12);
            string legacy = System.Text.RegularExpressions.Regex.Replace(JsonUtility.ToJson(original.World), "\"(?:BehaviorVersion|Profession|Fatigue|SocialNeed|TaskStartedTick)\":-?[0-9]+(?:\\.[0-9]+)?(?:[eE][+-]?[0-9]+)?,?", "");
            WorldState loaded = JsonUtility.FromJson<WorldState>(legacy);
            Assert.AreEqual(0, loaded.BehaviorVersion); Assert.IsTrue(loaded.Validate(out string error), error);
            uint rng = loaded.RngState;
            SimulationEngine migrated = new SimulationEngine(loaded);
            Assert.AreEqual(2, migrated.World.BehaviorVersion); Assert.AreEqual(rng, loaded.RngState);
            foreach (UnitData unit in loaded.Units) { Assert.AreNotEqual(UnitProfession.None, unit.Profession); Assert.AreEqual(0, unit.Fatigue); }
            string once = JsonUtility.ToJson(loaded); new SimulationEngine(loaded);
            Assert.AreEqual(once, JsonUtility.ToJson(loaded), "Migration must be idempotent");
            loaded.Units[0].Fatigue = float.NaN; Assert.IsFalse(loaded.Validate(out _));
        }

        [Test]
        public void SaveContinuationPreservesUnfinishedEatingAndRest()
        {
            SimulationEngine a = new SimulationEngine(FlatWorld()); a.Spawn(SpeciesType.Human, 24, 24, 2);
            UnitData hungry = a.World.Units[0], tired = a.World.Units[1];
            hungry.X = 24.5f; hungry.Y = 24.5f; hungry.Hunger = .8f; hungry.DecisionTimer = 0;
            tired.X = 24.5f; tired.Y = 24.5f; tired.Fatigue = .95f; tired.DecisionTimer = 0;
            a.Step(.1f); Assert.AreEqual(UnitTask.Eat, hungry.Task); Assert.AreEqual(UnitTask.Rest, tired.Task);
            SimulationEngine b = new SimulationEngine(JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(a.World)));
            for (int t = 0; t < 150; t++) { a.Step(.1f); b.Step(.1f); }
            Assert.AreEqual(JsonUtility.ToJson(a.World), JsonUtility.ToJson(b.World));
        }

        [Test]
        public void FoundingSpawningAndMaturingNeverAssignMilitaryProfessions()
        {
            for (int seed = 0; seed < 12; seed++)
            {
                WorldState world = FlatWorld(); world.Seed = seed; world.Laws.Warfare = false;
                SimulationEngine engine = new SimulationEngine(world);
                for (int species = 0; species < 4; species++) engine.Spawn((SpeciesType)species, 24, 24, 24);
                Assert.AreEqual(96, world.Units.Count);
                foreach (UnitData unit in world.Units) Assert.That(unit.Profession, Is.EqualTo(UnitProfession.Farmer).Or.EqualTo(UnitProfession.Forester));
                for (int i = 0; i < 12; i++) { world.Units[i].Age = 15.99f; world.Units[i].Profession = UnitProfession.None; }
                for (int t = 0; t < 3; t++) engine.Step(.1f);
                foreach (UnitData unit in world.Units) Assert.That(unit.Profession, Is.EqualTo(UnitProfession.Farmer).Or.EqualTo(UnitProfession.Forester));
                Assert.AreEqual(2, world.BehaviorVersion); Assert.IsTrue(world.Validate(out string error), error);
            }
        }

        [Test]
        public void ExperimentalGuardsDemobilizeWithoutLosingNeedsCargoOrRandomState()
        {
            SimulationEngine source = new SimulationEngine(FlatWorld()); source.Spawn(SpeciesType.Human, 24, 24, 3);
            WorldState legacy = source.World; legacy.BehaviorVersion = 1; legacy.Tick = 50; legacy.SimulatedSeconds = 5;
            UnitData patrol = legacy.Units[0], soldier = legacy.Units[1], civilian = legacy.Units[2];
            patrol.Profession = UnitProfession.Guard; patrol.Task = UnitTask.Patrol; patrol.TargetCell = legacy.Index(25, 24); patrol.Path.Add(patrol.TargetCell); patrol.CarriedWood = 2.5f; patrol.CarriedFood = .6f; patrol.Fatigue = .8f; patrol.SocialNeed = .9f;
            soldier.Profession = UnitProfession.Guard; soldier.Task = UnitTask.Fight; soldier.TargetId = civilian.Id; soldier.TargetCell = legacy.Index(24, 24); soldier.Path.Add(soldier.TargetCell);
            civilian.Profession = UnitProfession.Farmer; civilian.Task = UnitTask.Rest; civilian.Fatigue = .6f; civilian.TaskStartedTick = 7;
            Assert.IsTrue(legacy.Validate(out string beforeError), beforeError);
            uint rng = legacy.RngState; int nextId = legacy.NextId; float food = legacy.Settlements[0].Food;
            SimulationEngine migrated = new SimulationEngine(JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(legacy)));
            WorldState w = migrated.World; UnitData retired = w.GetUnit(patrol.Id), retiredSoldier = w.GetUnit(soldier.Id), unchanged = w.GetUnit(civilian.Id);
            Assert.AreEqual(2, w.BehaviorVersion); Assert.AreEqual(rng, w.RngState); Assert.AreEqual(nextId, w.NextId); Assert.AreEqual(50, w.Tick); Assert.AreEqual(food, w.Settlements[0].Food);
            Assert.AreNotEqual(UnitProfession.Guard, retired.Profession); Assert.AreNotEqual(UnitProfession.Guard, retiredSoldier.Profession);
            Assert.AreEqual(UnitTask.Idle, retired.Task); Assert.AreEqual(UnitTask.Idle, retiredSoldier.Task); Assert.AreEqual(0, retired.Path.Count); Assert.AreEqual(0, retiredSoldier.TargetId); Assert.AreEqual(0, retiredSoldier.Path.Count);
            Assert.AreEqual(2.5f, retired.CarriedWood); Assert.AreEqual(.6f, retired.CarriedFood); Assert.AreEqual(.8f, retired.Fatigue); Assert.AreEqual(.9f, retired.SocialNeed);
            Assert.AreEqual(UnitTask.Rest, unchanged.Task); Assert.AreEqual(.6f, unchanged.Fatigue); Assert.AreEqual(7, unchanged.TaskStartedTick);
            string once = JsonUtility.ToJson(w); new SimulationEngine(w); Assert.AreEqual(once, JsonUtility.ToJson(w));
            SimulationEngine clone = new SimulationEngine(JsonUtility.FromJson<WorldState>(once));
            for (int t = 0; t < 50; t++) { migrated.Step(.1f); clone.Step(.1f); }
            Assert.AreEqual(JsonUtility.ToJson(w), JsonUtility.ToJson(clone.World));
            retired.Profession = UnitProfession.Guard; Assert.IsFalse(w.Validate(out _), "Current saves cannot claim an unequipped military office");
        }

        [Test]
        public void CivilianMilitiaRespondsLocallyAndReturnsToItsWorkingProfession()
        {
            SimulationEngine engine = new SimulationEngine(FlatWorld()); engine.Spawn(SpeciesType.Human, 10, 10); engine.Spawn(SpeciesType.Orc, 38, 38);
            SettlementData home = engine.World.Settlements[0], foeHome = engine.World.Settlements[1]; engine.SetDiplomacy(home.Id, foeHome.Id, true);
            UnitData citizen = engine.World.Units[0], foe = engine.World.Units[1]; citizen.Profession = UnitProfession.Farmer; citizen.Fatigue = 0; citizen.DecisionTimer = 0;
            for (int t = 0; t < 30; t++) engine.Step(.1f);
            Assert.AreNotEqual(UnitTask.Fight, citizen.Task); Assert.AreNotEqual(UnitTask.Patrol, citizen.Task);
            Assert.Less(Math.Abs(citizen.X - home.X) + Math.Abs(citizen.Y - home.Y), 16, "War alone must not send civilians to the enemy city");
            foe.X = citizen.X + 1; foe.Y = citizen.Y; foe.Task = UnitTask.Fight; foe.TargetId = citizen.Id; foe.AttackTimer = 0; foe.DecisionTimer = 0; foe.Path.Clear(); foe.PathCursor = 0;
            float civilianHealth = citizen.Health, foeHealth = foe.Health;
            engine.Step(.1f);
            Assert.AreEqual(UnitTask.Fight, citizen.Task); Assert.AreEqual(UnitProfession.Farmer, citizen.Profession);
            Assert.Less(citizen.Health, civilianHealth); Assert.Less(foe.Health, foeHealth);
            foreach (BuildingData building in engine.World.Buildings) Assert.AreEqual(100, building.Health, "Civilian defense must not damage buildings");
            foe.Health = 0; engine.Step(.1f); engine.Step(.1f);
            Assert.AreNotEqual(UnitTask.Fight, citizen.Task); Assert.AreNotEqual(UnitTask.Patrol, citizen.Task); Assert.AreEqual(UnitProfession.Farmer, citizen.Profession);
            Assert.IsTrue(engine.World.Validate(out string error), error);
        }

        [Test]
        public void ChildrenAndSeverelyWoundedCiviliansFleeInsteadOfJoiningMilitia()
        {
            for (int scenario = 0; scenario < 2; scenario++)
            {
                SimulationEngine engine = new SimulationEngine(FlatWorld()); engine.Spawn(SpeciesType.Human, 10, 10); engine.Spawn(SpeciesType.Orc, 38, 38);
                engine.SetDiplomacy(engine.World.Settlements[0].Id, engine.World.Settlements[1].Id, true);
                UnitData citizen = engine.World.Units[0], foe = engine.World.Units[1];
                citizen.X = 10.5f; citizen.Y = 10.5f; citizen.Age = scenario == 0 ? 12 : 30; citizen.Health = scenario == 0 ? 100 : 20; citizen.Profession = scenario == 0 ? UnitProfession.None : UnitProfession.Forester;
                foe.X = 11.5f; foe.Y = 10.5f; foe.Task = UnitTask.Fight; foe.TargetId = citizen.Id;
                engine.Step(.1f);
                Assert.AreEqual(UnitTask.Flee, citizen.Task); Assert.AreNotEqual(UnitProfession.Guard, citizen.Profession); Assert.Greater(citizen.Path.Count, 0);
                Assert.IsTrue(engine.World.Validate(out string error), error);
            }
        }

        [Test]
        public void GovernmentsAreSeededAndAssignedOnlyOnceWithRealRulers()
        {
            var seen = new System.Collections.Generic.HashSet<GovernmentType>();
            for (int seed = 0; seed < 32; seed++)
            {
                WorldState a = FlatWorld(), b = FlatWorld(); a.Seed = seed; b.Seed = seed;
                SimulationEngine first = new SimulationEngine(a), second = new SimulationEngine(b);
                first.Spawn(SpeciesType.Human, 24, 24, 5); second.Spawn(SpeciesType.Human, 24, 24, 5);
                SettlementData s = a.Settlements[0]; seen.Add(s.Government);
                Assert.AreNotEqual(GovernmentType.Unset, s.Government);
                Assert.AreEqual(JsonUtility.ToJson(a), JsonUtility.ToJson(b));
                UnitData ruler = a.GetUnit(s.RulerId);
                Assert.IsNotNull(ruler); Assert.AreEqual(s.Id, ruler.SettlementId); Assert.GreaterOrEqual(ruler.Age, 16);
                GovernmentType once = s.Government; int rulerOnce = s.RulerId;
                first.Spawn(SpeciesType.Human, 24, 24, 2);
                Assert.AreEqual(once, s.Government); Assert.AreEqual(rulerOnce, s.RulerId);
                Assert.IsTrue(a.Validate(out string error), error);
            }
            Assert.AreEqual(3, seen.Count);
        }

        [Test]
        public void SuccessionUsesEligibleCitizensAndClearsDeadRulersImmediatelyAfterPowers()
        {
            SimulationEngine engine = new SimulationEngine(FlatWorld()); engine.Spawn(SpeciesType.Human, 24, 24, 3);
            SettlementData s = engine.World.Settlements[0]; s.Government = GovernmentType.Feudal; s.NextElectionTick = 0; s.RulerId = 0;
            UnitData young = engine.World.Units[0], middle = engine.World.Units[1], oldest = engine.World.Units[2];
            young.Age = 18; middle.Age = 30; oldest.Age = 50;
            engine.Step(.1f); Assert.AreEqual(oldest.Id, s.RulerId);
            oldest.Health = 0; engine.Step(.1f); Assert.AreEqual(middle.Id, s.RulerId);
            s.Government = GovernmentType.Theocracy; s.RulerId = 0; young.Blessed = true;
            engine.Step(.1f); Assert.AreEqual(young.Id, s.RulerId);
            foreach (UnitData unit in engine.World.Units) { unit.X = 24.5f; unit.Y = 24.5f; unit.Health = 10; }
            engine.ApplyPower(PowerType.Lightning, 24, 24, 1);
            Assert.AreEqual(0, engine.World.Units.Count); Assert.AreEqual(0, s.RulerId);
            Assert.IsTrue(engine.World.Validate(out string error), error);
        }

        [Test]
        public void ChildrenReceiveNoOfficeUntilMatureAndForeignRulersAreReplaced()
        {
            SimulationEngine engine = new SimulationEngine(FlatWorld()); engine.Spawn(SpeciesType.Human, 10, 10, 2); engine.Spawn(SpeciesType.Elf, 38, 38, 1);
            SettlementData home = engine.World.Settlements[0], other = engine.World.Settlements[1];
            home.RulerId = 0;
            foreach (UnitData unit in engine.World.Units) if (unit.SettlementId == home.Id) unit.Age = 10;
            engine.Step(.1f); Assert.AreEqual(0, home.RulerId);
            UnitData child = engine.World.Units[0]; child.Age = 15.99f;
            for (int t = 0; t < 3; t++) engine.Step(.1f);
            Assert.AreEqual(child.Id, home.RulerId);
            child.SettlementId = other.Id;
            engine.Step(.1f); Assert.AreEqual(0, home.RulerId);
            Assert.IsTrue(engine.World.Validate(out string error), error);
        }

        [Test]
        public void RepublicElectionOccursAtTheSavedDeadlineAndContinuesAfterLoad()
        {
            SimulationEngine a = new SimulationEngine(FlatWorld()); a.Spawn(SpeciesType.Human, 24, 24, 8);
            a.SetLaw(WorldLaw.Births, false); a.SetLaw(WorldLaw.Aging, false);
            SettlementData s = a.World.Settlements[0]; s.Government = GovernmentType.Republic; s.GovernmentSinceTick = 0; s.NextElectionTick = 600; s.Food = 10000;
            int incumbent = s.RulerId;
            for (int t = 0; t < 599; t++) a.Step(.1f);
            Assert.AreEqual(incumbent, s.RulerId); Assert.AreEqual(600, s.NextElectionTick);
            SimulationEngine b = new SimulationEngine(JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(a.World)));
            a.Step(.1f); b.Step(.1f);
            Assert.AreEqual(1200, s.NextElectionTick); Assert.IsNotNull(a.World.GetUnit(s.RulerId));
            Assert.AreEqual(JsonUtility.ToJson(a.World), JsonUtility.ToJson(b.World));
            for (int t = 0; t < 30; t++) { a.Step(.1f); b.Step(.1f); }
            Assert.AreEqual(JsonUtility.ToJson(a.World), JsonUtility.ToJson(b.World));
        }

        [Test]
        public void LegacyPoliticsMigratesWithoutRandomDrawsAndRejectsInvalidLinksAndSchedules()
        {
            SimulationEngine original = new SimulationEngine(FlatWorld()); original.Spawn(SpeciesType.Human, 24, 24, 5);
            string legacy = System.Text.RegularExpressions.Regex.Replace(JsonUtility.ToJson(original.World), ",?\"(?:PoliticsVersion|Government|RulerId|GovernmentSinceTick|NextElectionTick)\":-?[0-9]+", "");
            WorldState loaded = JsonUtility.FromJson<WorldState>(legacy);
            Assert.AreEqual(0, loaded.PoliticsVersion); Assert.IsTrue(loaded.Validate(out string error), error);
            uint rng = loaded.RngState; int nextId = loaded.NextId; new SimulationEngine(loaded);
            Assert.AreEqual(rng, loaded.RngState); Assert.AreEqual(nextId, loaded.NextId); Assert.AreEqual(1, loaded.PoliticsVersion);
            Assert.AreNotEqual(0, loaded.Settlements[0].RulerId);
            string once = JsonUtility.ToJson(loaded); new SimulationEngine(loaded); Assert.AreEqual(once, JsonUtility.ToJson(loaded));
            SettlementData s = loaded.Settlements[0]; int ruler = s.RulerId;
            s.RulerId = int.MaxValue; Assert.IsFalse(loaded.Validate(out _)); s.RulerId = ruler;
            s.Government = (GovernmentType)99; Assert.IsFalse(loaded.Validate(out _));
            s.Government = GovernmentType.Republic; s.NextElectionTick = loaded.Tick + 601; Assert.IsFalse(loaded.Validate(out _));
            s.NextElectionTick = loaded.Tick + 600; s.GovernmentSinceTick = loaded.Tick + 1; Assert.IsFalse(loaded.Validate(out _));
        }

        private static WorldState FlatWorld()
        {
            WorldState world = WorldGenerator.Generate(711, 48, 48);
            for (int i = 0; i < world.Cells.Length; i++)
            {
                world.Cells[i].Terrain = TerrainType.Grass;
                world.Cells[i].Height = .55f; world.Cells[i].Vegetation = .7f;
                world.Cells[i].River = false;
            }
            return world;
        }
    }
}
