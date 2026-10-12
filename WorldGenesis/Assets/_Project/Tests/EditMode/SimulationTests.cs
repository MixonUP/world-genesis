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
