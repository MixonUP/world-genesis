using System;
using System.Collections.Generic;
using System.Linq;
using WorldGenesis.Simulation;
#if UNITY_5_3_OR_NEWER
using NUnit.Framework;
using UnityEngine;
#endif

namespace WorldGenesis.Tests
{
    public static class BiomeScenarios
    {
        public static Func<WorldState, WorldState> Clone;
        public static Func<WorldState, string> Snapshot;
        private static void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        private static TileData Land(float temperature, float moisture, float height = .55f, TerrainType terrain = TerrainType.Grass)
        { return new TileData { Temperature = temperature, Moisture = moisture, Height = height, Terrain = terrain }; }
        public static void ClimateExtremesCoverAllTenBiomesWithoutChangingTerrainIds()
        {
            Need((int)TerrainType.DeepWater == 0 && (int)TerrainType.Mountain == 7 && (int)TerrainType.Highlands == 11 && BiomeCatalog.Count == 10, "terrain IDs shifted");
            TileData[] cells = {
                new TileData { Terrain = TerrainType.DeepWater, WaterKind = WaterKind.Ocean }, new TileData { Terrain = TerrainType.ShallowWater, WaterKind = WaterKind.Lake },
                Land(.55f,.50f), Land(.55f,.7f), Land(.4f,.7f), Land(.2f,.5f), Land(.8f,.5f), Land(.7f,.2f), Land(.8f,.8f), Land(.5f,.5f,.8f) };
            for (int i = 0; i < cells.Length; i++) { Need((int)BiomeCatalog.GenerateCell(cells[i]) == i, "climate sample " + i); Need(BiomeCatalog.Name((BiomeType)i) != "Неизвестный биом", "missing biome label"); }
        }
        public static void WaterAndMountainOverridesRespectHydrologyAndExplicitTerrain()
        {
            foreach (WaterKind kind in new[] { WaterKind.Lake, WaterKind.Stream, WaterKind.River, WaterKind.MountainRiver })
                Need(BiomeCatalog.GenerateCell(new TileData { Terrain = TerrainType.ShallowWater, WaterKind = kind, Height = .9f, Temperature = .1f }) == BiomeType.Freshwater, "fresh water classification");
            Need(BiomeCatalog.GenerateCell(new TileData { Terrain = TerrainType.ShallowWater, WaterKind = WaterKind.Estuary, River = true }) == BiomeType.Ocean, "estuary classified as fresh");
            Need(BiomeCatalog.GenerateCell(Land(.8f,.9f,.5f,TerrainType.Mountain)) == BiomeType.Mountain, "painted mountain");
            Need(BiomeCatalog.GenerateCell(Land(.8f,.9f,.5f,TerrainType.Snow)) == BiomeType.Tundra, "painted snow");
            Need(BiomeCatalog.GenerateCell(Land(.2f,.9f,.5f,TerrainType.Desert)) == BiomeType.Desert, "painted desert");
        }
        public static void GenerationAcrossSeedsIsStableClusteredAndHasClimateVariety()
        {
            var seen = new HashSet<BiomeType>(); int equalNeighbors = 0, edges = 0;
            foreach (WorldPreset preset in Enum.GetValues(typeof(WorldPreset)))
            for (int seed = 1; seed <= 8; seed++)
            {
                WorldState a = WorldGenerator.Generate(seed * 7907, 96, 96, preset), b = WorldGenerator.Generate(seed * 7907, 96, 96, preset);
                Need(a.BiomeVersion == 1, "generator biome hook missing"); Need(a.Cells.Select(t => t.Biome).SequenceEqual(b.Cells.Select(t => t.Biome)), "biomes differ for same generation");
                foreach (TileData t in a.Cells) seen.Add((BiomeType)t.Biome);
                for (int y = 0; y < a.Height; y++) for (int x = 1; x < a.Width; x++) { edges++; if (a.Cells[a.Index(x,y)].Biome == a.Cells[a.Index(x-1,y)].Biome) equalNeighbors++; }
                Need(a.Validate(out string error), error);
            }
            Need(seen.Count == 10, "seed matrix missing biome: " + string.Join(",", Enum.GetValues(typeof(BiomeType)).Cast<BiomeType>().Where(b => !seen.Contains(b))));
            Need(equalNeighbors > edges * .75f, "biomes are noisy per-cell confetti");
        }
        public static void LegacyBiomeMigrationPreservesResourcesTerrainRngAndEntityIds()
        {
            WorldState w = WorldGenerator.Generate(17713,64,64); new SimulationEngine(w); w.BiomeVersion = 0;
            for (int i = 0; i < w.Cells.Length; i++) w.Cells[i].Biome = 0;
            w = Clone(w); uint rng = w.RngState; int next = w.NextId; TileData[] cells = (TileData[])w.Cells.Clone(); ResourceNodeData[] nodes = (ResourceNodeData[])w.ResourceNodes.Clone();
            Need(w.Validate(out string before), before); new SimulationEngine(w);
            Need(w.BiomeVersion == 1 && w.RngState == rng && w.NextId == next, "biome migration used RNG/IDs"); Need(nodes.SequenceEqual(w.ResourceNodes), "biome migration changed deposits");
            for (int i = 0; i < cells.Length; i++) { cells[i].Biome = w.Cells[i].Biome; Need(cells[i].Equals(w.Cells[i]), "migration changed terrain climate or links"); }
            string once = Snapshot(w); new SimulationEngine(w); Need(Snapshot(w) == once, "second migration changed saved state");
        }
        public static void ChangingWeatherMoistureDoesNotReclassifySavedBiomesOnReload()
        {
            WorldState w = WorldGenerator.Generate(913,64,64); new SimulationEngine(w); byte[] original = w.Cells.Select(c => c.Biome).ToArray();
            for (int i = 0; i < w.Cells.Length; i++) w.Cells[i].Moisture = (i & 1) == 0 ? 0 : 1;
            BiomeCatalog.Generate(w); Need(original.SequenceEqual(w.Cells.Select(c => c.Biome)), "Generate rerolled established biomes");
            WorldState copy = Clone(w); new SimulationEngine(copy); Need(original.SequenceEqual(copy.Cells.Select(c => c.Biome)), "rain/drought changed biome through reload"); Need(copy.Validate(out string error), error);
        }
        public static void TemporaryWaterDepthDoesNotTurnLandBiomeIntoWaterBiome()
        {
            WorldState w = WorldGenerator.Generate(313,32,32); new SimulationEngine(w); int cell = Array.FindIndex(w.Cells, t => t.WaterDepth == 0 && t.Terrain == TerrainType.Grass);
            Need(cell >= 0, "fixture missing dry grass"); byte original = w.Cells[cell].Biome; w.Cells[cell].WaterDepth = .7f; w.Cells[cell].WaterKind = WaterKind.River; w.Cells[cell].CurrentStrength = .2f; w.Cells[cell].Moisture = 1;
            WorldState copy = Clone(w); new SimulationEngine(copy); Need(copy.Cells[cell].Biome == original && copy.GetBiome(cell) != BiomeType.Freshwater, "temporary flood reclassified land");
        }
        public static void ExplicitTerrainRefreshIsLocalAndPaintHookIsActive()
        {
            WorldState w = WorldGenerator.Generate(1217,32,32); SimulationEngine e = new SimulationEngine(w); byte[] original = w.Cells.Select(c => c.Biome).ToArray(); uint rng = w.RngState; int cell = w.Index(16,16);
            e.Paint(TerrainType.Desert,16,16,0); Need(w.GetBiome(cell) == BiomeType.Desert, "terrain paint did not refresh biome");
            for (int i = 0; i < original.Length; i++) if (i != cell) Need(original[i] == w.Cells[i].Biome, "single-cell paint changed another biome");
            Need(w.RngState == rng, "biome paint changed RNG"); Need(w.Validate(out string error), error);
        }
        public static void UnknownBiomeVersionOrIdIsRejectedBeforeWorldReplacement()
        {
            WorldState w = WorldGenerator.Generate(311,32,32); w.BiomeVersion = 2; Need(!w.Validate(out _), "future biome version accepted"); w.BiomeVersion = 1; w.Cells[0].Biome = 255; Need(!w.Validate(out _), "unknown biome accepted");
            bool rejected = false; try { new SimulationEngine(w); } catch (ArgumentException) { rejected = true; } Need(rejected, "engine replaced invalid biome world");
        }
        public static void SaveContinuationPreservesBiomeLabelsAndDeterministicState()
        {
            SimulationEngine first = new SimulationEngine(WorldGenerator.Generate(619,32,32)); WorldState copy = Clone(first.World); SimulationEngine second = new SimulationEngine(copy); byte[] original = copy.Cells.Select(c => c.Biome).ToArray();
            for (int i = 0; i < 200; i++) { first.Step(.1f); second.Step(.1f); }
            Need(Snapshot(first.World) == Snapshot(second.World), "biome save continuation diverged"); Need(original.SequenceEqual(copy.Cells.Select(c => c.Biome)), "ordinary ticks changed biomes"); Need(copy.Validate(out string error), error);
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class BiomeTests
    {
        [SetUp] public void Serializers() { BiomeScenarios.Clone = w => JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)); BiomeScenarios.Snapshot = w => JsonUtility.ToJson(w); }
        [TestCase("ClimateExtremesCoverAllTenBiomesWithoutChangingTerrainIds")]
        [TestCase("WaterAndMountainOverridesRespectHydrologyAndExplicitTerrain")]
        [TestCase("GenerationAcrossSeedsIsStableClusteredAndHasClimateVariety")]
        [TestCase("LegacyBiomeMigrationPreservesResourcesTerrainRngAndEntityIds")]
        [TestCase("ChangingWeatherMoistureDoesNotReclassifySavedBiomesOnReload")]
        [TestCase("TemporaryWaterDepthDoesNotTurnLandBiomeIntoWaterBiome")]
        [TestCase("ExplicitTerrainRefreshIsLocalAndPaintHookIsActive")]
        [TestCase("UnknownBiomeVersionOrIdIsRejectedBeforeWorldReplacement")]
        [TestCase("SaveContinuationPreservesBiomeLabelsAndDeterministicState")]
        public void Scenario(string name) { typeof(BiomeScenarios).GetMethod(name).Invoke(null, null); }
    }
#endif
}
