using System;
using System.Linq;
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
            SettlementData home = engine.World.Settlements[0];
            // Keep passive production at its soft cap; only the isolated meal may change this stock.
            home.Food = 80; float foodBefore = home.Food;
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
            engine.SetDiplomacy(human.SettlementId, orc.SettlementId, true);
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
        public void LegacyEnemyCacheCannotCaptureAPeacefulAbandonedTown()
        {
            SimulationEngine engine = new SimulationEngine(FlatWorld()); engine.SetLaw(WorldLaw.Births, false);
            engine.Spawn(SpeciesType.Human, 10, 10, 8); engine.Spawn(SpeciesType.Orc, 36, 36, 1);
            WorldState world = engine.World; SettlementData home = world.Settlements[0], abandoned = world.Settlements[1];
            home.EnemyId = abandoned.Id; abandoned.EnemyId = home.Id; // Old implicit war cache, with no declared relation.
            foreach (UnitData unit in world.Units) if (unit.SettlementId == abandoned.Id) unit.Health = 0;
            engine.Step(.1f);
            UnitData visitor = world.Units[0]; visitor.X = abandoned.X + .5f; visitor.Y = abandoned.Y + .5f; visitor.Task = UnitTask.Idle; visitor.Path.Clear(); visitor.PathCursor = 0; visitor.TargetCell = -1; visitor.DecisionTimer = 10;
            WorldState restored = JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(world));
            SimulationEngine resumed = new SimulationEngine(restored); resumed.Step(1);
            Assert.AreEqual(abandoned.Id, restored.Cells[restored.Index(abandoned.X, abandoned.Y)].SettlementId);
            Assert.AreEqual(0, restored.Relations.Count); Assert.AreEqual(0, restored.GetSettlement(home.Id).EnemyId); Assert.AreEqual(0, restored.GetSettlement(abandoned.Id).EnemyId);
            foreach (BuildingData building in restored.Buildings) if (building.X == abandoned.X && building.Y == abandoned.Y) Assert.AreEqual(abandoned.Id, building.SettlementId);
            resumed.SetDiplomacy(home.Id, abandoned.Id, true); resumed.Step(1);
            Assert.AreEqual(abandoned.Id, restored.Cells[restored.Index(abandoned.X, abandoned.Y)].SettlementId, "A lone unarmed visitor cannot capture a town even after war is declared; 0.10 requires an equipped occupation party.");
            Assert.AreEqual(0, restored.GetSettlement(abandoned.Id).SovereignSettlementId);
            Assert.AreEqual(0, restored.GetSettlement(abandoned.Id).OccupationSeconds);
            Assert.IsTrue(restored.Validate(out string error), error);
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
            // Stock above the passive reserve isolates the actual meal transaction.
            SimulationEngine engine = new SimulationEngine(FlatWorld());
            engine.Spawn(SpeciesType.Human, 24, 24);
            UnitData unit = engine.World.Units[0]; SettlementData home = engine.World.Settlements[0];
            unit.X = 24.5f; unit.Y = 24.5f; unit.Hunger = .8f; unit.Fatigue = 0; unit.DecisionTimer = 0; home.Food = 80;
            engine.Step(.1f);
            Assert.AreEqual(UnitTask.Eat, unit.Task); Assert.AreEqual(80, home.Food);
            Assert.Greater(unit.WorkTimer, 0); Assert.AreEqual(engine.World.Tick, unit.TaskStartedTick);
            engine.ApplyPower(PowerType.Fire, 24, 24, 1); engine.Step(.1f);
            Assert.AreEqual(UnitTask.Flee, unit.Task); Assert.AreEqual(80, home.Food);
            engine.ApplyPower(PowerType.Rain, 24, 24, 10);
            unit.X = 24.5f; unit.Y = 24.5f; unit.Path.Clear(); unit.PathCursor = 0; unit.Task = UnitTask.Idle; unit.DecisionTimer = 0;
            for (int t = 0; t < 14; t++) engine.Step(.1f);
            Assert.AreEqual(79, home.Food); Assert.Less(unit.Hunger, .5f);
        }

        [Test]
        public void FireInterruptsWorkAndEscapesWithoutLosingCarriedResources()
        {
            SimulationEngine engine = new SimulationEngine(FlatWorld());
            engine.Spawn(SpeciesType.Human, 24, 24);
            UnitData unit = engine.World.Units[0]; unit.X = 24.5f; unit.Y = 24.5f;
            unit.Task = UnitTask.Gather; unit.TargetCell = engine.World.Index(24, 24); unit.CarriedWood = 1; unit.DecisionTimer = 50;
            engine.ApplyPower(PowerType.Fire, 24, 24, 1); engine.Step(.1f);
            Assert.AreEqual(UnitTask.Flee, unit.Task); Assert.AreEqual(1, unit.CarriedWood); Assert.Greater(unit.Path.Count, 0);
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
                // Independent settlements need separate unclaimed founding sites.
                for (int species = 0; species < 4; species++) engine.Spawn((SpeciesType)species, 10 + (species % 2) * 28, 10 + (species / 2) * 28, 24);
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
            WorldState legacy = source.World; legacy.BehaviorVersion = 1; SetPreIndustryResourceFixture(legacy, 1); legacy.Tick = 50; legacy.SimulatedSeconds = 5;
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
            float storedLegacyWood = retired.CarriedWood; foreach (GroundStockData pile in w.GroundStocks) if (pile.Item == ItemType.Plank) storedLegacyWood += pile.Amount; Assert.AreEqual(2.5f, storedLegacyWood, .00001f); Assert.AreEqual(.6f, retired.CarriedFood); Assert.AreEqual(.8f, retired.Fatigue); Assert.AreEqual(.9f, retired.SocialNeed);
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

        [Test]
        public void TreeHarvestDeliveryAndCampfireProcessingConserveRealMaterial()
        {
            SimulationEngine engine = ResourceFixture(); WorldState w = engine.World; UnitData unit = w.Units[0]; SettlementData home = w.Settlements[0];
            int source = w.Index(28, 24); w.Cells[source].Terrain = TerrainType.Forest; w.Cells[source].Vegetation = 1;
            w.ResourceNodes[source] = new ResourceNodeData { Type = ResourceType.Tree, Amount = 3, Capacity = 3 };
            bool carried = false, processed = false;
            for (int t = 0; t < 650 && home.Wood < 2.99f; t++)
            {
                engine.Step(.1f); carried |= unit.CargoAmount > 0; processed |= unit.Task == UnitTask.Process;
                float total = w.ResourceNodes[source].Amount + (unit.CargoKind == ItemType.Log ? unit.CargoAmount : 0) + w.GetSettlementStock(home.Id, ItemType.Log) + w.GetCitizenStock(unit.Id, ItemType.Log) + home.Wood;
                Assert.AreEqual(3, total, .002f); Assert.IsTrue(w.Validate(out string error), error);
            }
            Assert.IsTrue(carried && processed); Assert.AreEqual(3, home.Wood, .002f); Assert.AreEqual(0, w.ResourceNodes[source].Amount); Assert.AreEqual(0, w.Cells[source].Vegetation);
            foreach (BuildingData building in w.Buildings) Assert.AreNotEqual(BuildingType.Sawmill, building.Type);
        }

        [Test]
        public void ActualWorkshopRatesAndFuelControlProcessingTransactions()
        {
            SimulationEngine slow = ResourceFixture(), fast = ResourceFixture();
            BuildingData camp = slow.World.Buildings[0], mill = fast.World.Buildings[0]; CloseResourceFixture(fast.World, mill, BuildingType.Sawmill);
            camp.Inventory[(int)ItemType.Log] = 1; mill.Inventory[(int)ItemType.Log] = 1;
            // Both clocks include the mill's real door transition before its three-second job.
            for (int t = 0; t < 40; t++) { slow.Step(.1f); fast.Step(.1f); }
            Assert.AreEqual(0, slow.World.Settlements[0].Wood); Assert.AreEqual(1, fast.World.Settlements[0].Wood); Assert.AreEqual(1, camp.Inventory[(int)ItemType.Log]); Assert.Greater(camp.ProcessProgress, 2.9f);
            for (int t = 40; t < 100; t++) slow.Step(.1f);
            Assert.AreEqual(1, slow.World.Settlements[0].Wood); Assert.AreEqual(0, camp.Inventory[(int)ItemType.Log]);
            SimulationEngine stone = ResourceFixture(); BuildingData works = stone.World.Buildings[0]; CloseResourceFixture(stone.World, works, BuildingType.Stoneworks); works.Inventory[(int)ItemType.RawStone] = 4;
            for (int t = 0; t < 60 && works.Inventory[(int)ItemType.StoneBlock] == 0; t++) stone.Step(.1f);
            Assert.AreEqual(1, works.Inventory[(int)ItemType.StoneBlock]); Assert.AreEqual(2, works.Inventory[(int)ItemType.RawStone]); works.ActiveRecipe = RecipeType.StoneTile;
            // Completion releases the previous job; the next one includes a fresh doorway transition.
            for (int t = 0; t < 70 && works.Inventory[(int)ItemType.StoneTile] == 0; t++) stone.Step(.1f);
            Assert.AreEqual(2, works.Inventory[(int)ItemType.StoneTile]); Assert.AreEqual(0, works.Inventory[(int)ItemType.RawStone]);
            SimulationEngine clay = ResourceFixture(); BuildingData kiln = clay.World.Buildings[0]; CloseResourceFixture(clay.World, kiln, BuildingType.Kiln); kiln.Inventory[(int)ItemType.Clay] = 2;
            for (int t = 0; t < 30; t++) clay.Step(.1f);
            Assert.AreEqual(0, kiln.Inventory[(int)ItemType.Brick]); Assert.AreEqual(2, kiln.Inventory[(int)ItemType.Clay]);
            UnitData worker = clay.World.Units[0]; worker.CarriedFood = 0; worker.DecisionTimer = 0; worker.Task = UnitTask.Idle; worker.Path.Clear(); worker.PathCursor = 0; worker.InsideBuildingId = worker.EnteringBuildingId = 0; worker.BuildingInteractionTimer = 0; worker.X = 24.5f; worker.Y = 23.5f; kiln.Inventory[(int)ItemType.Firewood] = 1;
            for (int t = 0; t < 90 && kiln.Inventory[(int)ItemType.Brick] == 0; t++) clay.Step(.1f);
            Assert.AreEqual(2, kiln.Inventory[(int)ItemType.Brick]); Assert.AreEqual(0, kiln.Inventory[(int)ItemType.Clay]); Assert.AreEqual(0, kiln.Inventory[(int)ItemType.Firewood]);
        }

        [Test]
        public void SeparateWorkshopReceivesStoneThroughARealHaulingTrip()
        {
            SimulationEngine engine = ResourceFixture(); WorldState w = engine.World; w.Settlements[0].Wood = 100;
            BuildingData camp = w.Buildings[0], works = ResourceBuilding(w, BuildingType.Stoneworks, 28, 24); camp.Inventory[(int)ItemType.RawStone] = 2;
            UnitData unit = w.Units[0]; unit.X = 21.5f; bool pickup = false, transit = false;
            for (int t = 0; t < 300 && works.Inventory[(int)ItemType.StoneBlock] == 0; t++)
            {
                engine.Step(.1f); pickup |= unit.Task == UnitTask.Haul; transit |= unit.CargoKind == ItemType.RawStone && unit.CargoAmount > 0;
                float total = camp.Inventory[(int)ItemType.RawStone] + works.Inventory[(int)ItemType.RawStone] + (unit.CargoKind == ItemType.RawStone ? unit.CargoAmount : 0) + works.Inventory[(int)ItemType.StoneBlock] * 2;
                Assert.AreEqual(2, total, .001f); Assert.IsTrue(w.Validate(out string error), error);
            }
            Assert.IsTrue(pickup && transit); Assert.AreEqual(1, works.Inventory[(int)ItemType.StoneBlock]);
        }

        [Test]
        public void ResourceCargoCannotTeleportAcrossWaterAndResumesAfterBridgeRepair()
        {
            SimulationEngine engine = ResourceFixture(); WorldState w = engine.World; UnitData unit = w.Units[0]; SettlementData home = w.Settlements[0];
            for (int y = 0; y < 48; y++) engine.Paint(TerrainType.DeepWater, 26, y, 0);
            unit.X = 29.5f; unit.Y = 24.5f; unit.CargoKind = ItemType.Log; unit.CargoAmount = 1;
            for (int t = 0; t < 100; t++) engine.Step(.1f);
            Assert.AreEqual(1, unit.CargoAmount); Assert.AreEqual(0, w.GetSettlementStock(home.Id, ItemType.Log)); Assert.AreEqual(0, home.Wood); Assert.Greater(unit.X, 26);
            engine.Paint(TerrainType.Grass, 26, 24, 0);
            for (int t = 0; t < 250 && w.GetSettlementStock(home.Id, ItemType.Log) == 0; t++) engine.Step(.1f);
            Assert.Greater(w.GetSettlementStock(home.Id, ItemType.Log), 0); Assert.AreEqual(0, unit.CargoAmount); Assert.IsTrue(w.Validate(out string error), error);
        }

        [Test]
        public void AllDepositKindsRegrowOnTheirSavedTimersOnlyOnCompatibleTerrain()
        {
            WorldState w = WorldGenerator.Generate(51, 16, 16); Array.Clear(w.ResourceNodes, 0, w.ResourceNodes.Length);
            for (int i = 0; i < w.Cells.Length; i++) { w.Cells[i].Terrain = TerrainType.Grass; w.Cells[i].Vegetation = 0; w.Cells[i].Fire = 0; }
            SimulationEngine engine = new SimulationEngine(w);
            for (int kind = 1; kind <= 11; kind++) w.ResourceNodes[w.Index(kind, 5)] = new ResourceNodeData { Type = (ResourceType)kind, Capacity = 6, RegrowTicksRemaining = ResourceCatalog.GetRegrowTicks((ResourceType)kind) };
            int clay = w.Index((int)ResourceType.Clay, 5); w.Cells[clay].Terrain = TerrainType.DeepWater;
            for (int t = 1; t <= 6000; t++)
            {
                engine.Step(.1f);
                for (int kind = 1; kind <= 11; kind++) if (kind != (int)ResourceType.Clay) Assert.AreEqual(t >= ResourceCatalog.GetRegrowTicks((ResourceType)kind) ? 6 : 0, w.ResourceNodes[w.Index(kind, 5)].Amount);
            }
            Assert.AreEqual(0, w.ResourceNodes[clay].Amount); Assert.AreEqual(1200, w.ResourceNodes[clay].RegrowTicksRemaining);
            w.Cells[clay].Terrain = TerrainType.Grass; for (int t = 0; t < 1200; t++) engine.Step(.1f);
            Assert.AreEqual(6, w.ResourceNodes[clay].Amount); Assert.AreEqual(1, w.Cells[w.Index(1, 5)].Vegetation); Assert.IsTrue(w.Validate(out string error), error);
        }

        [Test]
        public void WorkshopReassignmentAndFireInterruptionReleaseSlotsWithoutDuplicatingInputs()
        {
            SimulationEngine engine = ResourceFixture(); WorldState w = engine.World; UnitData unit = w.Units[0];
            BuildingData first = ResourceBuilding(w, BuildingType.Sawmill, 32, 24), second = ResourceBuilding(w, BuildingType.Sawmill, 26, 24);
            first.Inventory[(int)ItemType.Log] = 1; second.Inventory[(int)ItemType.Log] = 1;
            engine.Step(.1f); Assert.AreEqual(first.Id, unit.TargetBuildingId); Assert.AreEqual(UnitTask.Process, unit.Task);
            w.Cells[w.Index(first.X, first.Y)].Fire = .3f; unit.DecisionTimer = 0;
            engine.Step(.1f); Assert.AreEqual(second.Id, unit.TargetBuildingId); Assert.AreEqual(0, first.WorkerId); Assert.AreEqual(unit.Id, second.WorkerId); Assert.IsTrue(w.Validate(out string beforeError), beforeError);
            engine.ApplyPower(PowerType.Rain, first.X, first.Y, 1); for (int t = 0; t < 100 && second.ProcessProgress == 0; t++) engine.Step(.1f);
            float progress = second.ProcessProgress, logs = second.Inventory[(int)ItemType.Log]; Assert.Greater(progress, 0); Assert.Less(progress, 3);
            engine.ApplyPower(PowerType.Fire, (int)unit.X, (int)unit.Y, 1); engine.Step(.1f);
            Assert.AreEqual(UnitTask.Flee, unit.Task); Assert.AreEqual(0, second.WorkerId); Assert.AreEqual(progress, second.ProcessProgress); Assert.AreEqual(logs, second.Inventory[(int)ItemType.Log]); Assert.IsTrue(w.Validate(out string error), error);
        }

        [Test]
        public void DeathAndBuildingDestructionRecordCargoAndInventoryLosses()
        {
            SimulationEngine engine = ResourceFixture(); WorldState w = engine.World; UnitData unit = w.Units[0]; BuildingData camp = w.Buildings[0];
            unit.CargoKind = ItemType.IronOre; unit.CargoAmount = 1; camp.Inventory[(int)ItemType.Log] = 4;
            engine.ApplyPower(PowerType.Meteor, 24, 24, 3);
            Assert.AreEqual(0, w.Units.Count); Assert.IsNull(w.GetBuilding(camp.Id)); Assert.AreEqual(1, w.LostResources[(int)ItemType.IronOre]); Assert.AreEqual(4, w.LostResources[(int)ItemType.Log]); Assert.IsTrue(w.Validate(out string error), error);
        }

        [Test]
        public void EveryRawMaterialIsHarvestedIntoCargoAndPhysicallyDelivered()
        {
            for (int kind = 1; kind <= 11; kind++)
            {
                SimulationEngine engine = ResourceFixture(); WorldState w = engine.World; SettlementData home = w.Settlements[0]; home.Wood = 100; w.Buildings[0].Inventory[(int)ItemType.Firewood] = 10;
                ResourceType type = (ResourceType)kind; ItemType item = ResourceCatalog.GetOutput(type); int cell = w.Index(28, 24);
                w.ResourceNodes[cell] = new ResourceNodeData { Type = type, Capacity = 3, Amount = 3 }; w.Cells[cell].Vegetation = type == ResourceType.Tree ? 1 : 0;
                bool carried = false;
                for (int t = 0; t < 350 && w.GetSettlementStock(home.Id, item) + w.GetCitizenStock(w.Units[0].Id, item) < 2.99f; t++) { engine.Step(.1f); carried |= w.Units[0].CargoKind == item && w.Units[0].CargoAmount > 0; }
                Assert.IsTrue(carried, type.ToString()); Assert.GreaterOrEqual(w.GetSettlementStock(home.Id, item) + w.GetCitizenStock(w.Units[0].Id, item), 2.99f, type.ToString()); Assert.AreEqual(0, w.ResourceNodes[cell].Amount); Assert.IsTrue(w.Validate(out string error), error);
            }
            // An interrupted harvest can leave a tiny positive remainder; it must remain mineable so renewal can begin.
            SimulationEngine remainder = ResourceFixture(); int tail = remainder.World.Index(28, 24);
            remainder.World.ResourceNodes[tail] = new ResourceNodeData { Type = ResourceType.Stone, Amount = .005f, Capacity = 3 };
            for (int t = 0; t < 250; t++) remainder.Step(.1f);
            Assert.AreEqual(0, remainder.World.ResourceNodes[tail].Amount);
            Assert.Greater(remainder.World.ResourceNodes[tail].RegrowTicksRemaining, 0);
            Assert.AreEqual(.005f, remainder.World.GetSettlementStock(remainder.World.Settlements[0].Id, ItemType.RawStone) + remainder.World.GetCitizenStock(remainder.World.Units[0].Id, ItemType.RawStone), .00001f);
        }

        [Test]
        public void ZeroPlankFoundingBootstrapsCampfireSawmillStoneAndBrickProduction()
        {
            SimulationEngine engine = ResourceFixture(); WorldState w = engine.World; SettlementData home = w.Settlements[0]; engine.Spawn(SpeciesType.Human, 24, 24, 3);
            foreach (UnitData unit in w.Units) { unit.Profession = UnitProfession.Forester; unit.Fatigue = 0; unit.Hunger = 0; unit.SocialNeed = 0; unit.DecisionTimer = 0; }
            int tree = w.Index(28, 24), stone = w.Index(28, 27), clay = w.Index(28, 30);
            w.ResourceNodes[tree] = new ResourceNodeData { Type = ResourceType.Tree, Amount = 60, Capacity = 60 }; w.Cells[tree].Vegetation = 1;
            w.ResourceNodes[stone] = new ResourceNodeData { Type = ResourceType.Stone, Amount = 12, Capacity = 12 }; w.ResourceNodes[clay] = new ResourceNodeData { Type = ResourceType.Clay, Amount = 12, Capacity = 12 };
            Assert.AreEqual(0, home.Wood); long firstMill = 0;
            for (int t = 0; t < 6000; t++)
            {
                engine.Step(.1f); if (firstMill == 0) foreach (BuildingData building in w.Buildings) if (building.Type == BuildingType.Sawmill) firstMill = w.Tick;
                if (t % 100 == 0) Assert.IsTrue(w.Validate(out string error), error);
                if (w.GetSettlementStock(home.Id, ItemType.StoneBlock) > 0 && w.GetSettlementStock(home.Id, ItemType.Brick) > 0) break;
            }
            Assert.GreaterOrEqual(firstMill, 1000); Assert.Greater(w.GetSettlementStock(home.Id, ItemType.StoneBlock), 0); Assert.Greater(w.GetSettlementStock(home.Id, ItemType.Brick), 0);
        }

        [Test]
        public void BarterTransfersExistingGoodsAndNaturalTaxOnceAtTheWorkplace()
        {
            SimulationEngine e = HouseholdTradeFixture(EconomicPolicy.Market); WorldState w = e.World; BuildingData camp = w.Buildings[0]; UnitData buyer = w.Units[0], seller = w.Units[1];
            buyer.X = 21.5f; buyer.Y = 16.5f; e.Step(.1f);
            Assert.IsTrue(w.Trades.Count == 0 && camp.Inventory[(int)ItemType.Log] == 0, "A remote worker acquired goods");
            for (int i = 0; i < 60 && w.Trades.Count == 0; i++) e.Step(.1f);
            Assert.IsTrue(w.Trades.Count == 1 && w.Trades[0].Kind == TradeKind.Barter && w.Trades[0].PaymentItem == ItemType.IronOre, "No genuine on-site barter");
            Assert.IsTrue(Math.Abs(w.GetCitizenStock(seller.Id, ItemType.IronOre) - .8f) < .00001f && Math.Abs(w.GetTreasuryStock(buyer.SettlementId, ItemType.IronOre) - .2f) < .00001f && w.GetCitizenStock(buyer.Id, ItemType.IronOre) == 0 && camp.Inventory[(int)ItemType.Log] == 1, "Payment or tax duplicated/lost");
            SimulationEngine clone = new SimulationEngine(JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)));
            for (int i = 0; i < 110; i++) { e.Step(.1f); clone.Step(.1f); }
            Assert.IsTrue(JsonUtility.ToJson(w) == JsonUtility.ToJson(clone.World) && w.Trades.Count == 1 && w.Settlements[0].Wood == 1, "Save reload charged again or did not finish the acquired input");
            Assert.IsTrue(w.Validate(out string error), error);
        }

        [Test]
        public void FreeGiftsAndInsolvencyPreserveCommunalStartup()
        {
            SimulationEngine free = HouseholdTradeFixture(EconomicPolicy.FreeDistribution); free.World.Buildings[0].PersonalStocks.RemoveAll(p => p.OwnerId == free.World.Units[0].Id); free.Step(.1f);
            Assert.IsTrue(free.World.Trades.Count == 1 && free.World.Trades[0].Kind == TradeKind.Gift && free.World.Trades[0].PaymentAmount == 0 && free.World.Buildings[0].Inventory[(int)ItemType.Log] == 1, "Free gift did not deliver real input");
            SimulationEngine poor = HouseholdTradeFixture(EconomicPolicy.Market); WorldState w = poor.World; w.Buildings[0].PersonalStocks.RemoveAll(p => p.OwnerId == w.Units[0].Id);
            int cell = w.Index(20,16); w.ResourceNodes[cell] = new ResourceNodeData {Type=ResourceType.Tree,Amount=4,Capacity=4}; w.Cells[cell].Vegetation = 1;
            for (int t=0;t<600 && w.Settlements[0].Wood==0;t++) poor.Step(.1f);
            Assert.IsTrue(w.Settlements[0].Wood > 0 && w.Trades.All(t=>t.Kind != TradeKind.Barter), "Zero personal funds blocked communal startup or fabricated a payment");
        }

        [Test]
        public void RecipeAcquisitionReservesFuelAndUsesOneSharedBarterBudget()
        {
            SimulationEngine e = HouseholdTradeFixture(EconomicPolicy.Market); WorldState w=e.World; BuildingData kiln=w.Buildings[0]; UnitData buyer=w.Units[0], seller=w.Units[1]; CloseResourceFixture(w, kiln, BuildingType.Kiln); kiln.PersonalStocks.Clear(); kiln.Inventory[(int)ItemType.Clay]=1;
            kiln.PersonalStocks.Add(new OwnedGoodsData{OwnerId=buyer.Id,Item=ItemType.Firewood,Amount=1}); kiln.PersonalStocks.Add(new OwnedGoodsData{OwnerId=buyer.Id,Item=ItemType.Log,Amount=1}); kiln.PersonalStocks.Add(new OwnedGoodsData{OwnerId=seller.Id,Item=ItemType.Clay,Amount=1});
            for (int t=0;t<20 && w.Trades.Count==0;t++) e.Step(.1f); Assert.IsTrue(kiln.Inventory[(int)ItemType.Clay]==2 && kiln.Inventory[(int)ItemType.Firewood]==1 && w.Trades.Count(t=>t.Kind==TradeKind.Barter)==1 && w.Trades.First(t=>t.Kind==TradeKind.Barter).PaymentItem==ItemType.Log, "Recipe fuel was used as barter tender");
            for(int t=0;t<80;t++)e.Step(.1f); Assert.IsTrue(kiln.Inventory[(int)ItemType.Brick]==2, "Fully funded brick recipe stalled");
            SimulationEngine insufficient=HouseholdTradeFixture(EconomicPolicy.Market); WorldState z=insufficient.World; BuildingData k=z.Buildings[0]; CloseResourceFixture(z, k, BuildingType.Kiln);k.PersonalStocks.Clear();
            k.PersonalStocks.Add(new OwnedGoodsData{OwnerId=z.Units[0].Id,Item=ItemType.IronOre,Amount=2}); k.PersonalStocks.Add(new OwnedGoodsData{OwnerId=z.Units[1].Id,Item=ItemType.Clay,Amount=2});k.PersonalStocks.Add(new OwnedGoodsData{OwnerId=z.Units[1].Id,Item=ItemType.Firewood,Amount=1});
            for (int t=0;t<20;t++) insufficient.Step(.1f);Assert.IsTrue(z.Trades.Count==0 && k.Inventory[(int)ItemType.Clay]==0 && z.GetCitizenStock(z.Units[0].Id,ItemType.IronOre)==2,"Incomplete barter budget partly committed");
        }

        [Test]
        public void LegacyCombinedLoadIsPreservedInLocalPilesAndCollectedOneUnitAtATime()
        {
            SimulationEngine source=HouseholdFixture();WorldState w=source.World;UnitData u=w.Units[0];u.CargoKind=ItemType.IronOre;u.CargoAmount=3;u.CarriedFood=2.8f;u.CarriedWood=2;SetPreIndustryResourceFixture(w,1);
            uint rng=w.RngState;int next=w.NextId;float x=u.X,y=u.Y; SimulationEngine e=new SimulationEngine(JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)));w=e.World;u=w.Units[0];
            Assert.IsTrue(SimulationEngine.CarriedLoad(u)<=1 && Math.Abs(SimulationEngine.CarriedLoad(u)+w.GroundStocks.Sum(g=>g.Amount)-7.8f)<.00001f && w.GroundStocks.All(g=>g.X==x&&g.Y==y),"Legacy overflow was removed or teleported");
            Assert.IsTrue(w.ResourceVersion==4&&w.RngState==rng&&w.NextId==next,"Carry migration consumed IDs or RNG");string once=JsonUtility.ToJson(w);new SimulationEngine(w);Assert.IsTrue(once==JsonUtility.ToJson(w),"Carry migration repeated");
            for(int t=0;t<250;t++){e.Step(.1f);Assert.IsTrue(w.Units.All(a=>SimulationEngine.CarriedLoad(a)<=1.00001f),"Pickup exceeded total carrying capacity");}
            Assert.IsTrue(w.GetSettlementStock(u.SettlementId,ItemType.IronOre)==3 && w.Settlements[0].Wood>=2,"Overflow was never physically collected in separate trips");
            Assert.IsTrue(w.Validate(out string error),error);
        }

        [Test]
        public void SexMigrationIsDeterministicAndBirthsNeedBothAdultSexes()
        {
            SimulationEngine e=HouseholdFixture();e.Spawn(SpeciesType.Human,16,16,1);WorldState w=e.World;w.Laws.Births=true;
            foreach(UnitData u in w.Units){u.X=16.5f;u.Y=16.5f;u.Sex=Sex.Female;u.Task=UnitTask.Idle;u.DecisionTimer=50;}w.Settlements[0].BirthTimer=20;e.Step(1);Assert.IsTrue(w.Units.Count==2,"Single-sex adults produced a birth");
            w.Units[1].Sex=Sex.Male;e.Step(1);Assert.IsTrue(w.Units.Count==3 && w.Units[2].IsChild && w.Units[2].Profession==UnitProfession.None,"Mixed adult parents did not create a child");
            w.DemographyVersion=0;uint rng=w.RngState;int next=w.NextId;SimulationEngine a=new SimulationEngine(JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)));SimulationEngine b=new SimulationEngine(JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)));
            Assert.IsTrue(JsonUtility.ToJson(a.World)==JsonUtility.ToJson(b.World)&&a.World.RngState==rng&&a.World.NextId==next,"Sex migration changed RNG/IDs or was not deterministic");
        }

        [Test]
        public void ChildLaborLawRestrictsFarmingHeavyCargoAndPrivateTrade()
        {
            SimulationEngine e=HouseholdFixture();e.Spawn(SpeciesType.Human,16,16,1);WorldState w=e.World;UnitData child=w.Units[1];child.Age=13;child.X=16.5f;child.Y=16.5f;child.DecisionTimer=0;child.Fatigue=0;child.Hunger=0;SettlementData s=w.Settlements[0];s.Food=0;
            e.SetEconomicPolicy(s.Id,EconomicPolicy.FreeDistribution,0,ChildLaborPolicy.Forbidden,ResourceFocus.Balanced);for(int t=0;t<30;t++)e.Step(.1f);Assert.IsTrue(child.Task!=UnitTask.Farm&&child.Task!=UnitTask.Gather&&child.Task!=UnitTask.Process&&child.CarriedFood==0&&child.HarvestAmount==0,"Forbidden child performed production");
            e.SetEconomicPolicy(s.Id,EconomicPolicy.FreeDistribution,0,ChildLaborPolicy.LightWork,ResourceFocus.Balanced);child.Task=UnitTask.Idle;child.Path.Clear();child.PathCursor=0;child.DecisionTimer=0;w.Buildings.First(b=>b.Type==BuildingType.Farm).HarvestReady=8;bool farmed=false,delivered=false;
            for(int t=0;t<120;t++){e.Step(.1f);farmed|=child.HarvestAmount>0;delivered|=farmed&&child.HarvestAmount==0&&(s.Food>0||w.GetAgriculturalStock(s.Id,AgriculturalProduct.Grain)>0);Assert.IsTrue(SimulationEngine.CarriedLoad(child)<=1.00001f&&child.Task!=UnitTask.Gather&&child.Task!=UnitTask.Process,"Light work exceeded its scope");}Assert.IsTrue(farmed&&delivered,"Permitted adolescent failed to harvest and physically deliver the crop");
            child.CargoKind=ItemType.IronOre;child.CargoAmount=1;child.CarriedFood=0;child.HarvestAmount=0;child.HarvestProduct=AgriculturalProduct.None;child.Task=UnitTask.Return;child.DecisionTimer=20;e.Step(.1f);Assert.IsTrue(child.CargoAmount==0&&w.GroundStocks.Any(g=>g.Item==ItemType.IronOre&&g.Amount==1),"Child retained heavy legacy cargo");
            child.CarriedFood=.4f;child.Task=UnitTask.Farm;e.SetEconomicPolicy(s.Id,EconomicPolicy.FreeDistribution,0,ChildLaborPolicy.Forbidden,ResourceFocus.Balanced);Assert.IsTrue(child.CarriedFood==0&&w.GroundStocks.Any(g=>g.IsFood&&Math.Abs(g.Amount-.4f)<.00001f),"Revoking child work lost carried food");
            child.Age=9;e.SetEconomicPolicy(s.Id,EconomicPolicy.Market,.3f,ChildLaborPolicy.LightWork,ResourceFocus.Metals);for(int t=0;t<20;t++)e.Step(.1f);Assert.IsTrue(child.Task!=UnitTask.Farm&&child.Task!=UnitTask.Process&&w.Trades.All(t=>t.SellerId!=child.Id&&t.BuyerId!=child.Id),"Young child worked or traded");
        }

        [Test]
        public void LearningNeedsALivingNearbyAdultAndImprovesAdultWork()
        {
            SimulationEngine e=HouseholdFixture();e.Spawn(SpeciesType.Human,16,16,1);WorldState w=e.World;UnitData child=w.Units[1], mentor=w.Units[0];child.Age=10;child.X=mentor.X=16.5f;child.Y=mentor.Y=16.5f;child.Task=UnitTask.Learn;child.WorkTimer=3;child.Path.Clear();child.PathCursor=0;child.DecisionTimer=50;mentor.DecisionTimer=50;
            for(int t=0;t<20;t++)e.Step(.1f);Assert.IsTrue(child.Education>0,"Actual supervised learning did not advance");
            float learned=child.Education;mentor.X=28.5f;mentor.Y=28.5f;child.Task=UnitTask.Learn;child.WorkTimer=3;for(int t=0;t<20;t++)e.Step(.1f);Assert.IsTrue(child.Education==learned,"Learning progressed without nearby mentor");
            SimulationEngine fast=HouseholdFixture();UnitData adult=fast.World.Units[0];adult.Education=1;fast.World.Buildings[0].Inventory[(int)ItemType.Log]=1;for(int t=0;t<92;t++)fast.Step(.1f);Assert.IsTrue(fast.World.Settlements[0].Wood==1,"Education has no actual work effect");
        }

        [Test]
        public void PrivateGoodsAndNaturalTaxHaveExplicitDeathAndDestructionOutcomes()
        {
            SimulationEngine e=HouseholdTradeFixture(EconomicPolicy.Market);WorldState w=e.World;BuildingData b=w.Buildings[0];UnitData seller=w.Units[1];seller.Health=0;e.Step(.1f);
            Assert.IsTrue(w.GetCitizenStock(seller.Id,ItemType.Log)==0&&b.Inventory[(int)ItemType.Log]==1,"Dead owner left inaccessible or duplicated lots");
            b.TaxStocks[(int)ItemType.SilverOre]=.2f;e.ApplyPower(PowerType.Meteor,b.X,b.Y,3);Assert.IsTrue(Math.Abs(w.LostResources[(int)ItemType.SilverOre]-.2f)<.00001f&&w.LostResources[(int)ItemType.IronOre]==1,"Private or tax stock vanished on storage destruction");Assert.IsTrue(w.Validate(out string error),error);
        }

        [Test]
        public void RulerReviewExplainsActualPolicyAndNullLegacyContainersMigrate()
        {
            SimulationEngine e=HouseholdFixture();WorldState w=e.World;SettlementData s=w.Settlements[0];UnitData ruler=w.GetUnit(s.RulerId);ruler.Generosity=0;ruler.Prudence=.1f;ruler.Ambition=.9f;s.NextPolicyReviewTick=w.Tick;e.Step(.1f);
            Assert.IsTrue(s.EconomyPolicy==EconomicPolicy.Market&&s.TaxRate==.3f&&s.ChildLabor==ChildLaborPolicy.LightWork&&s.PolicyRulerId==ruler.Id&&s.LastDecreeReason.Length>0);
            s.Food=0;s.NextPolicyReviewTick=w.Tick;e.Step(.1f);Assert.IsTrue(s.EconomyPolicy==EconomicPolicy.FreeDistribution&&s.TaxRate==0&&s.ResourceFocus==ResourceFocus.Balanced);
            ruler.Health=0;e.Step(.1f);Assert.IsTrue(s.RulerId==0&&s.EconomyPolicy==EconomicPolicy.FreeDistribution&&s.TaxRate==0&&s.ChildLabor==ChildLaborPolicy.Forbidden);
            SimulationEngine old=HouseholdFixture();WorldState legacy=old.World;SetPreIndustryResourceFixture(legacy,0);legacy.EconomyVersion=0;legacy.Settlements[0].Stone=7;foreach(BuildingData b in legacy.Buildings){b.PersonalStocks=null;b.TaxStocks=null;}
            new SimulationEngine(legacy);Assert.AreEqual(7,legacy.GetSettlementStock(legacy.Settlements[0].Id,ItemType.RawStone));Assert.IsTrue(legacy.Validate(out string error),error);
            legacy.Units[0].CarriedFood=.6f;legacy.Units[0].CarriedWood=.6f;Assert.IsFalse(legacy.Validate(out _));
        }

        private static void SetPreIndustryResourceFixture(WorldState world, int version)
        {
            Assert.AreEqual(0, world.IndustryBatchesCompleted, "Legacy fixture must not discard real production");
            world.ResourceVersion = version; world.IndustryVersion = 0; world.ProducedItems = world.MintIssued = null;
            world.LostResources = world.LostResources.Take(17).ToArray();
            foreach (BuildingData building in world.Buildings)
            {
                building.Inventory = building.Inventory.Take(17).ToArray();
                if (building.TaxStocks != null) building.TaxStocks = building.TaxStocks.Take(17).ToArray();
                if (version == 0)
                {
                    // Original resource-less saves predate construction invoices entirely.
                    Assert.IsTrue(building.PaidConstructionMaterials.All(amount => amount == 0), "Cannot erase real construction payment in a legacy fixture");
                    building.ConstructionLedgerVersion = 0; building.PaidConstructionMaterials = null;
                }
                else building.PaidConstructionMaterials = building.PaidConstructionMaterials.Take(17).ToArray();
            }
        }

        private static SimulationEngine HouseholdTradeFixture(EconomicPolicy policy)
        {
            SimulationEngine e=HouseholdFixture();e.Spawn(SpeciesType.Human,16,16,1);WorldState w=e.World;UnitData buyer=w.Units[0],seller=w.Units[1];
            seller.X=28.5f;seller.Y=28.5f;seller.DecisionTimer=1000;seller.Task=UnitTask.Idle;
            e.SetEconomicPolicy(buyer.SettlementId,policy,.2f,ChildLaborPolicy.Forbidden,ResourceFocus.Balanced);
            w.Buildings[0].PersonalStocks.Add(new OwnedGoodsData{OwnerId=seller.Id,Item=ItemType.Log,Amount=1});w.Buildings[0].PersonalStocks.Add(new OwnedGoodsData{OwnerId=buyer.Id,Item=ItemType.IronOre,Amount=1});return e;
        }

        private static SimulationEngine HouseholdFixture()
        {
            WorldState w=WorldGenerator.Generate(711,32,32);
            for(int i=0;i<w.Cells.Length;i++){w.Cells[i].Terrain=TerrainType.Grass;w.Cells[i].Height=.55f;w.Cells[i].Vegetation=0;w.Cells[i].River=false;Hydrology.RefreshCell(w,i);}
            ResourceCatalog.Generate(w);Array.Clear(w.ResourceNodes,0,w.ResourceNodes.Length);
            w.Laws.Warfare=false;w.Laws.Births=false;w.Laws.Aging=false;w.Laws.VegetationGrowth=false;w.Laws.FireSpread=false;
            SimulationEngine e=new SimulationEngine(w);e.Spawn(SpeciesType.Human,16,16);w.Settlements[0].Food=1000;w.Settlements[0].Wood=0;
            UnitData u=w.Units[0];u.X=16.5f;u.Y=16.5f;u.Hunger=0;u.Fatigue=0;u.SocialNeed=0;u.DecisionTimer=0;u.Profession=UnitProfession.Forester;return e;
        }

        private static SimulationEngine ResourceFixture()
        {
            WorldState world = FlatWorld(); Array.Clear(world.ResourceNodes, 0, world.ResourceNodes.Length); Array.Clear(world.LostResources, 0, world.LostResources.Length);
            for (int i = 0; i < world.Cells.Length; i++) { world.Cells[i].Vegetation = 0; world.Cells[i].Fire = 0; }
            world.Laws.Warfare = false; world.Laws.Births = false; world.Laws.Aging = false; world.Laws.VegetationGrowth = false; world.Laws.FireSpread = false;
            SimulationEngine engine = new SimulationEngine(world); engine.Spawn(SpeciesType.Human, 24, 24); world.Settlements[0].Food = 1000; world.Settlements[0].Wood = 0;
            UnitData unit = world.Units[0]; unit.X = 24.5f; unit.Y = 24.5f; unit.Hunger = 0; unit.Fatigue = 0; unit.SocialNeed = 0; unit.DecisionTimer = 0; unit.Profession = UnitProfession.Forester; return engine;
        }

        private static BuildingData ResourceBuilding(WorldState world, BuildingType type, int x, int y)
        {
            BuildingData building = new BuildingData { Id = world.NextId++, SettlementId = world.Settlements[0].Id, X = x, Y = y, Type = type, EntranceCell = BuildingAccess.IsEnclosed(type) ? world.Index(x, y - 1) : -1 };
            world.Buildings.Add(building); world.Cells[world.Index(x, y)].BuildingId = building.Id; world.Cells[world.Index(x, y)].SettlementId = building.SettlementId; return building;
        }

        private static void CloseResourceFixture(WorldState world, BuildingData building, BuildingType type)
        {
            // Authored workshop replaces the fixture's open camp; its worker starts at the real door.
            building.Type = type; building.EntranceCell = world.Index(building.X, building.Y - 1);
            UnitData worker = world.Units[0]; worker.X = building.X + .5f; worker.Y = building.Y - .5f;
            worker.InsideBuildingId = worker.EnteringBuildingId = 0; worker.BuildingInteractionTimer = 0;
            worker.Path.Clear(); worker.PathCursor = 0; world.NavigationRevision++;
        }

        private static WorldState FlatWorld()
        {
            WorldState world = WorldGenerator.Generate(711, 48, 48);
            for (int i = 0; i < world.Cells.Length; i++)
            {
                world.Cells[i].Terrain = TerrainType.Grass;
                world.Cells[i].Height = .55f; world.Cells[i].Vegetation = .7f;
                world.Cells[i].River = false; Hydrology.RefreshCell(world, i);
            }
            ResourceCatalog.Generate(world);
            return world;
        }
    }
}
