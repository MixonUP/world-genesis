using NUnit.Framework;
using UnityEngine;
using WorldGenesis.Runtime;
using WorldGenesis.Simulation;

namespace WorldGenesis.Tests
{
    public sealed class SimulationPacerTests
    {
        [TestCase(20)] [TestCase(50)] [TestCase(100)]
        public void FastClockExecutesRequestedFixedTicksWhenBudgetAllows(int speed)
        {
            SimulationPacer pacer = new SimulationPacer(() => 0d); int ticks = 0;
            for (int frame = 0; frame < 60; frame++) pacer.Advance(1d / 60, speed, () => ticks++);
            Assert.AreEqual(speed * 10, ticks); Assert.That(pacer.ActualMultiplier, Is.EqualTo(speed).Within(.21));
            Assert.AreEqual(0, pacer.LimitedRequestedSeconds); Assert.IsFalse(pacer.BudgetLimited);
        }

        [Test]
        public void ExpensiveTicksKeepFrameBoundedAndDoNotInventElapsedSimulation()
        {
            double time = 0; int ticks = 0; SimulationPacer pacer = new SimulationPacer(() => time);
            for (int frame = 0; frame < 120; frame++)
            {
                int work = pacer.Advance(1d / 60, 100, () => { ticks++; time += 7; });
                Assert.LessOrEqual(work, 2); Assert.LessOrEqual(pacer.LastWorkMilliseconds, 14);
                Assert.LessOrEqual(pacer.PendingSeconds, 25);
            }
            Assert.AreEqual(240, ticks); Assert.IsTrue(pacer.BudgetLimited);
            Assert.Greater(pacer.LimitedRequestedSeconds, 100);
            Assert.That(pacer.ActualMultiplier, Is.EqualTo(12).Within(.01));
            pacer.Reset(); Assert.AreEqual(0, pacer.PendingSeconds); Assert.AreEqual(0, pacer.ActualMultiplier);
            Assert.AreEqual(0, pacer.Advance(.016, 1, () => ticks++), "No fast-mode debt on resuming ordinary speed.");
        }

        [Test]
        public void AcceleratedTicksProduceTheSameSavedWorldAsOrdinaryTicks()
        {
            WorldState initial = WorldGenerator.Generate(9193, 48, 48, WorldPreset.Island);
            SimulationEngine fast = new SimulationEngine(JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(initial)));
            SimulationEngine normal = new SimulationEngine(JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(initial)));
            SimulationPacer pacer = new SimulationPacer(() => 0d);
            int ticks = 0;
            for (int frame = 0; frame < 8; frame++) ticks += pacer.Advance(.016, 100, () => fast.Step(.1f));
            for (int i = 0; i < ticks; i++) normal.Step(.1f);
            Assert.AreEqual(128, ticks);
            Assert.AreEqual(JsonUtility.ToJson(normal.World), JsonUtility.ToJson(fast.World));
            Assert.IsTrue(fast.World.Validate(out string error), error);
        }
    }
}
