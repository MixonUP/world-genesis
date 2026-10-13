using System;

namespace WorldGenesis.Simulation
{
    public enum FishSpeciesId { None, Minnow, Crucian, Roach, Perch, Trout, Pike, PearlCarp, MoonTrout, AncientSturgeon, StarMarlin }
    public enum FishRarity { Common, Uncommon, Rare, Epic, Legendary }

    public sealed class FishDefinition
    {
        public readonly FishSpeciesId Id;
        public readonly string Name;
        public readonly FishRarity Rarity;
        public readonly int MinimumRodLevel;
        public readonly float Weight, PearlYield, ScaleYield;
        public readonly AgriculturalProduct Food;
        public FishDefinition(FishSpeciesId id, string name, FishRarity rarity, int rod, float weight,
            AgriculturalProduct food, float pearls = 0, float scales = 0)
        { Id = id; Name = name; Rarity = rarity; MinimumRodLevel = rod; Weight = weight; Food = food; PearlYield = pearls; ScaleYield = scales; }
    }

    public sealed partial class UnitData
    {
        // Zero is uninitialized primitive gear in legacy data, never a manufactured upgrade.
        public int FishingRodLevel;
        public FishSpeciesId FishSpeciesId;
        public int FishCaughtCount;
    }

    public sealed partial class WorldState
    {
        public int FishingVersion;
        public bool ValidateFishing(out string error)
        {
            error = null;
            if (FishingVersion < 0 || FishingVersion > 1) { error = "Unsupported fishing version."; return false; }
            for (int i = 0; i < Units.Count; i++)
            {
                UnitData u = Units[i];
                if (u.FishingRodLevel < 0 || u.FishingRodLevel > 5 || (int)u.FishSpeciesId < 0 || (int)u.FishSpeciesId > 10 || u.FishCaughtCount < 0)
                { error = "Invalid fishing equipment or catch history."; return false; }
                if (FishingVersion == 0 && (u.FishingRodLevel > 1 || u.FishSpeciesId != FishSpeciesId.None || u.FishCaughtCount != 0))
                { error = "Legacy fishing data cannot contain manufactured rods or catches."; return false; }
                if ((u.Age < 16 || (int)u.Species > 3) && (u.FishingRodLevel > 1 || u.FishCaughtCount != 0 || u.FishSpeciesId != FishSpeciesId.None))
                { error = "Child or animal cannot own advanced fishing equipment or catch history."; return false; }
                FishDefinition last = FishingCatalog.Get(u.FishSpeciesId);
                if (last != null && (u.FishCaughtCount == 0 || last.MinimumRodLevel > FishingCatalog.RodLevel(u)))
                { error = "Catch history exceeds fishing equipment."; return false; }
                if (u.FishCaughtCount > 0 && last == null) { error = "Missing fish identity."; return false; }
            }
            return true;
        }
    }

    public static class FishingCatalog
    {
        public const int AgriculturalProductCount = 17;
        public static readonly FishDefinition[] All = {
            new FishDefinition(FishSpeciesId.Minnow, "Малёк", FishRarity.Common, 1, 32, AgriculturalProduct.Fish),
            new FishDefinition(FishSpeciesId.Crucian, "Карась", FishRarity.Common, 1, 20, AgriculturalProduct.Fish),
            new FishDefinition(FishSpeciesId.Roach, "Плотва", FishRarity.Common, 1, 18, AgriculturalProduct.Fish),
            new FishDefinition(FishSpeciesId.Perch, "Окунь", FishRarity.Uncommon, 2, 16, AgriculturalProduct.Fish),
            new FishDefinition(FishSpeciesId.Trout, "Форель", FishRarity.Uncommon, 2, 14, AgriculturalProduct.Fish),
            new FishDefinition(FishSpeciesId.Pike, "Щука", FishRarity.Uncommon, 3, 12, AgriculturalProduct.Fish),
            new FishDefinition(FishSpeciesId.PearlCarp, "Жемчужный карп", FishRarity.Rare, 3, 6, AgriculturalProduct.DelicacyFish, .25f),
            new FishDefinition(FishSpeciesId.MoonTrout, "Лунная форель", FishRarity.Epic, 4, 4, AgriculturalProduct.DelicacyFish, 0, .25f),
            new FishDefinition(FishSpeciesId.AncientSturgeon, "Древний осётр", FishRarity.Legendary, 5, .8f, AgriculturalProduct.DelicacyFish, .5f, .5f),
            new FishDefinition(FishSpeciesId.StarMarlin, "Звёздный марлин", FishRarity.Legendary, 5, .6f, AgriculturalProduct.DelicacyFish, .5f, .25f)
        };
        public static FishDefinition Get(FishSpeciesId species)
        { int n = (int)species - 1; return n >= 0 && n < All.Length ? All[n] : null; }
        public static int RodLevel(UnitData unit)
        { return unit == null || unit.Age < 16 || (int)unit.Species < 0 || (int)unit.Species > 3 ? 0 : Math.Max(1, Math.Min(5, unit.FishingRodLevel)); }
        public static string RodName(int level)
        {
            switch (level) { case 1: return "Примитивная снасть"; case 2: return "Деревянная удочка"; case 3: return "Гибкая льняная удочка"; case 4: return "Жемчужная удочка"; case 5: return "Удочка древней чешуи"; default: return "Нет снасти"; }
        }
        public static string ProductName(AgriculturalProduct product)
        {
            switch (product) { case AgriculturalProduct.Fish: return "Рыба"; case AgriculturalProduct.DelicacyFish: return "Деликатесная рыба"; case AgriculturalProduct.RiverPearl: return "Речной жемчуг"; case AgriculturalProduct.AncientScale: return "Древняя чешуя"; default: return product.ToString(); }
        }
        public static float CastSeconds(int level) { return 5.5f - .5f * Math.Max(1, Math.Min(5, level)); }
        public static bool IsWater(WorldState world, int cell)
        {
            if (world == null || cell < 0 || cell >= world.Cells.Length || world.Infrastructure == null) return false;
            TerrainType t = world.Cells[cell].Terrain;
            return t == TerrainType.ShallowWater || t == TerrainType.DeepWater;
        }
        public static bool Eligible(WorldState world, int cell, int rodLevel, FishSpeciesId species)
        {
            FishDefinition fish = Get(species);
            if (fish == null || rodLevel < fish.MinimumRodLevel || rodLevel > 5 || !IsWater(world, cell)) return false;
            TileData t = world.Cells[cell];
            bool ocean = t.WaterKind == WaterKind.Ocean || t.WaterKind == WaterKind.Estuary || world.GetBiome(cell) == BiomeType.Ocean && !t.River;
            bool fresh = !ocean;
            switch (species)
            {
                case FishSpeciesId.Minnow: return true;
                case FishSpeciesId.Crucian: return fresh && t.CurrentStrength <= .55f;
                case FishSpeciesId.Roach: return fresh;
                case FishSpeciesId.Perch: return t.Temperature >= .30f;
                case FishSpeciesId.Trout: return fresh && (t.CurrentStrength >= .25f || t.Temperature <= .45f);
                case FishSpeciesId.Pike: return fresh && t.CurrentStrength < .7f;
                case FishSpeciesId.PearlCarp: return fresh && (t.WaterKind == WaterKind.Lake || t.WaterKind == WaterKind.River) && t.Moisture >= .45f && t.Temperature >= .35f;
                case FishSpeciesId.MoonTrout: return fresh && (t.WaterKind == WaterKind.MountainRiver || t.WaterKind == WaterKind.River) && t.Temperature < .5f;
                case FishSpeciesId.AncientSturgeon: return fresh && (t.WaterKind == WaterKind.Lake || t.WaterKind == WaterKind.River) && t.WaterDepth >= .6f && t.Temperature < .65f;
                case FishSpeciesId.StarMarlin: return ocean && t.WaterDepth >= .6f && t.Temperature >= .55f;
                default: return false;
            }
        }
        public static bool IsActivelyFishing(WorldState world, UnitData unit)
        {
            if (unit == null || unit.Health <= 0 || RodLevel(unit) == 0 || unit.Task != UnitTask.Fish || unit.WorkTimer <= 0 || unit.PathCursor < unit.Path.Count || unit.HarvestAmount > 0 || SimulationEngine.CarryCapacityFor(unit) - SimulationEngine.CarriedLoad(unit) < .9999f || !IsWater(world, unit.ResourceCell) || world.Infrastructure[unit.ResourceCell].Fish < 1) return false;
            float dx = unit.X - (unit.ResourceCell % world.Width + .5f), dy = unit.Y - (unit.ResourceCell / world.Width + .5f);
            return dx * dx + dy * dy <= 2 && world.IsWalkable((int)unit.X, (int)unit.Y) && world.Cells[world.Index((int)unit.X, (int)unit.Y)].Fire <= .1f;
        }
    }
}
