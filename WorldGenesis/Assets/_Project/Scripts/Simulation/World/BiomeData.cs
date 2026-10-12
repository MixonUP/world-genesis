using System;

namespace WorldGenesis.Simulation
{
    // A persistent ecological/visual layer; terrain IDs and navigation remain independent.
    public enum BiomeType : byte
    {
        Ocean = 0, Freshwater = 1, Grassland = 2, TemperateForest = 3, Taiga = 4,
        Tundra = 5, Savanna = 6, Desert = 7, TropicalWetland = 8, Mountain = 9
    }
#pragma warning disable CS0282
    public partial struct TileData { public byte Biome; }
#pragma warning restore CS0282
    public sealed partial class WorldState
    {
        public int BiomeVersion;
        public BiomeType GetBiome(int cell)
        {
            if (Cells == null || cell < 0 || cell >= Cells.Length) throw new ArgumentOutOfRangeException(nameof(cell));
            if (BiomeVersion == 0) return BiomeCatalog.GenerateCell(Cells[cell]);
            if (BiomeVersion != 1 || Cells[cell].Biome >= BiomeCatalog.Count) throw new InvalidOperationException("Invalid biome state.");
            return (BiomeType)Cells[cell].Biome;
        }
        public bool ValidateBiomes(out string error)
        {
            error = null;
            if (BiomeVersion < 0 || BiomeVersion > 1) { error = "Unsupported biome version."; return false; }
            if (BiomeVersion == 0) return true;
            if (Cells == null) { error = "Missing biome cells."; return false; }
            for (int i = 0; i < Cells.Length; i++) if (Cells[i].Biome >= BiomeCatalog.Count)
            { error = "Invalid biome at cell " + i + "."; return false; }
            // Do not reclassify against mutable moisture or temporary flood depth.
            return true;
        }
    }
    public static class BiomeCatalog
    {
        public const int Count = 10;
        public static string Name(BiomeType biome)
        {
            switch (biome)
            {
                case BiomeType.Ocean: return "Море";
                case BiomeType.Freshwater: return "Пресные воды";
                case BiomeType.Grassland: return "Луга и степи";
                case BiomeType.TemperateForest: return "Умеренный лес";
                case BiomeType.Taiga: return "Тайга";
                case BiomeType.Tundra: return "Тундра";
                case BiomeType.Savanna: return "Саванна";
                case BiomeType.Desert: return "Пустыня";
                case BiomeType.TropicalWetland: return "Влажные тропики";
                case BiomeType.Mountain: return "Горный пояс";
                default: return "Неизвестный биом";
            }
        }
        // Game-calibrated normalized H/M/T thresholds, not Whittaker's measured climate envelopes.
        public static BiomeType GenerateCell(TileData tile)
        {
            if (tile.Terrain == TerrainType.DeepWater || tile.Terrain == TerrainType.ShallowWater)
                return tile.WaterKind == WaterKind.Ocean || tile.WaterKind == WaterKind.Estuary || tile.WaterKind == WaterKind.None && !tile.River ? BiomeType.Ocean : BiomeType.Freshwater;
            if (tile.Terrain == TerrainType.Mountain || tile.Height >= .74f) return BiomeType.Mountain;
            if (tile.Terrain == TerrainType.Desert) return BiomeType.Desert;
            if (tile.Terrain == TerrainType.Snow || tile.Temperature < .30f) return BiomeType.Tundra;
            if (tile.Temperature < .45f && tile.Moisture >= .42f) return BiomeType.Taiga;
            if (tile.Moisture < .34f) return BiomeType.Desert;
            if (tile.Temperature >= .65f && (tile.Terrain == TerrainType.Swamp || tile.Moisture >= .65f)) return BiomeType.TropicalWetland;
            if (tile.Temperature >= .65f && tile.Moisture < .58f) return BiomeType.Savanna;
            if (tile.Terrain == TerrainType.Forest || tile.Moisture >= .55f) return BiomeType.TemperateForest;
            return BiomeType.Grassland;
        }
        public static void Generate(WorldState world)
        {
            if (world == null || world.Cells == null) throw new ArgumentNullException(nameof(world));
            if (world.BiomeVersion == 1) return;
            if (world.BiomeVersion != 0) throw new ArgumentException("Unsupported biome version.", nameof(world));
            for (int i = 0; i < world.Cells.Length; i++) world.Cells[i].Biome = (byte)GenerateCell(world.Cells[i]);
            world.BiomeVersion = 1; world.TerrainRevision++;
        }
        // Only explicit lasting terrain edits (paint/meteor), never a weather tick or temporary flood.
        public static void RefreshCell(WorldState world, int cell)
        {
            if (world == null || world.Cells == null || cell < 0 || cell >= world.Cells.Length) throw new ArgumentOutOfRangeException(nameof(cell));
            if (world.BiomeVersion == 0) Generate(world);
            if (world.BiomeVersion != 1) throw new ArgumentException("Unsupported biome version.", nameof(world));
            byte biome = (byte)GenerateCell(world.Cells[cell]);
            if (biome == world.Cells[cell].Biome) return;
            world.Cells[cell].Biome = biome; world.TerrainRevision++;
        }
    }
}
