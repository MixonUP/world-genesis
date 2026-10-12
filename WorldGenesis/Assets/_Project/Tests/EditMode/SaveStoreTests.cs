using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using WorldGenesis.Runtime;
using WorldGenesis.Simulation;

namespace WorldGenesis.Tests
{
    public sealed class SaveStoreTests
    {
        private string directory, path;
        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "WorldGenesis-save-tests-" + Guid.NewGuid().ToString("N"));
            path = Path.Combine(directory, "world.json");
        }
        [TearDown]
        public void TearDown()
        {
            // This test owns exactly this freshly allocated temporary directory.
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        [Test]
        public void DiskRoundtripUsesActualUnitySerializerAndContinuesExactly()
        {
            SimulationEngine original = Create();
            for (int i = 0; i < 87; i++) original.Step(.1f);
            original.Step(.037f);
            Assert.IsTrue(SaveStore.TrySave(path, original.World, out string error), error);
            Assert.IsTrue(SaveStore.TryLoad(path, out WorldState loaded, out error), error);
            Assert.AreEqual(JsonUtility.ToJson(original.World), JsonUtility.ToJson(loaded));
            SimulationEngine continued = new SimulationEngine(loaded);
            for (int i = 0; i < 211; i++) { original.Step(.1f); continued.Step(.1f); }
            Assert.AreEqual(JsonUtility.ToJson(original.World), JsonUtility.ToJson(continued.World));
        }
        [Test]
        public void SecondSavePreservesBackupAndInvalidReplacementLeavesSlotIntact()
        {
            SimulationEngine engine = Create();
            Assert.IsTrue(SaveStore.TrySave(path, engine.World, out string error), error);
            string first = File.ReadAllText(path);
            engine.Step(1);
            Assert.IsTrue(SaveStore.TrySave(path, engine.World, out error), error);
            Assert.AreEqual(first, File.ReadAllText(path + ".bak"));
            string second = File.ReadAllText(path);
            engine.World.Units[0].X = float.NaN;
            Assert.IsFalse(SaveStore.TrySave(path, engine.World, out error));
            Assert.AreEqual(second, File.ReadAllText(path));
            Assert.IsTrue(SaveStore.TryLoad(path, out WorldState loaded, out error), error);
            Assert.AreEqual(10, loaded.Tick);
        }
        [TestCase("{unclosed")]
        [TestCase("{}")]
        [TestCase("{\"Schema\":999,\"Format\":\"WorldGenesis\"}")]
        public void MalformedOrUnsupportedFilesReturnNoCandidate(string body)
        {
            Directory.CreateDirectory(directory); File.WriteAllText(path, body);
            Assert.IsFalse(SaveStore.TryLoad(path, out WorldState candidate, out string error));
            Assert.IsNull(candidate); Assert.IsNotEmpty(error);
        }
        private static SimulationEngine Create()
        {
            SimulationEngine engine = new SimulationEngine(WorldGenerator.Generate(4128, 64, 64));
            engine.Paint(TerrainType.Grass, 32, 32, 12);
            engine.Spawn(SpeciesType.Human, 32, 32, 12);
            return engine;
        }
    }
}
