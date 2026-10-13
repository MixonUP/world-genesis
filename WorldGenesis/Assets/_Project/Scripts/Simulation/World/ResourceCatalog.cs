using System;

namespace WorldGenesis.Simulation
{
    public static class ResourceCatalog
    {
        public const int LegacyItemCount = 17, SaltItemCount = 18, ItemCount = 45;
        public const float CarryCapacity = 1, StorageCapacity = 120;
        private static readonly int[] Regrow = { 0, 900, 1500, 3000, 2700, 6000, 4800, 2400, 1200, 900, 1800, 3600, 9000 };
        private static readonly ItemType[] Outputs = { ItemType.None, ItemType.Log, ItemType.RawStone, ItemType.IronOre, ItemType.CopperOre, ItemType.GoldOre, ItemType.SilverOre, ItemType.Coal, ItemType.Clay, ItemType.Sand, ItemType.Limestone, ItemType.TinOre, ItemType.Salt };
        public static int GetRegrowTicks(ResourceType type) { return (int)type >= 0 && (int)type < Regrow.Length ? Regrow[(int)type] : 0; }
        public static ItemType GetOutput(ResourceType type) { return (int)type >= 0 && (int)type < Outputs.Length ? Outputs[(int)type] : ItemType.None; }
        public static bool IsCompatible(ResourceType type, TileData tile)
        {
            if (type == ResourceType.None || tile.Terrain == TerrainType.DeepWater || tile.Terrain == TerrainType.ShallowWater || tile.Terrain == TerrainType.Mountain) return false;
            if (type == ResourceType.Tree) return tile.Terrain == TerrainType.Forest || tile.Terrain == TerrainType.Grass;
            if (type == ResourceType.Salt) return !tile.River && (tile.Terrain == TerrainType.Sand || tile.Terrain == TerrainType.Desert || tile.Terrain == TerrainType.Grass || tile.Terrain == TerrainType.Highlands);
            return true;
        }
        public static float HarvestPerSecond(ResourceType type) { return type == ResourceType.Salt ? .4f : type == ResourceType.Tree ? .75f : type == ResourceType.Clay || type == ResourceType.Sand ? .65f : type == ResourceType.Stone ? .55f : .45f; }
        public static float StockTarget(ItemType item, int population)
        {
            if ((int)item >= SaltItemCount) return IndustryCatalog.TargetStock(item, population);
            switch (item)
            {
                case ItemType.None: return 0;
                case ItemType.Plank: return 16 + population * 2;
                case ItemType.Log: return Math.Max(6, population);
                case ItemType.Firewood: return Math.Max(4, population * .4f);
                case ItemType.RawStone: return Math.Max(8, population);
                case ItemType.Clay: return Math.Max(6, population * .5f);
                case ItemType.StoneBlock: case ItemType.Brick: return Math.Max(6, population * .5f);
                case ItemType.StoneTile: return 4;
                case ItemType.GoldOre: case ItemType.SilverOre: return 2;
                case ItemType.Salt: return Math.Max(3, population * .15f);
                default: return 3;
            }
        }
        public static bool CanProcess(BuildingType building, RecipeType recipe)
        {
            return ((building == BuildingType.Hall || building == BuildingType.Sawmill) && (recipe == RecipeType.Plank || recipe == RecipeType.Firewood)) || (building == BuildingType.Stoneworks && (recipe == RecipeType.StoneBlock || recipe == RecipeType.StoneTile)) || (building == BuildingType.Kiln && recipe == RecipeType.Brick);
        }
        public static float ProcessingSeconds(BuildingType building, RecipeType recipe)
        {
            if (recipe == RecipeType.Plank) return building == BuildingType.Sawmill ? 3 : 10;
            if (recipe == RecipeType.Firewood) return building == BuildingType.Sawmill ? 3 : 6;
            if (recipe == RecipeType.StoneBlock) return 5;
            if (recipe == RecipeType.StoneTile) return 6;
            return recipe == RecipeType.Brick ? 8 : 0;
        }
        public static ItemType Input(RecipeType recipe) { return recipe == RecipeType.Plank || recipe == RecipeType.Firewood ? ItemType.Log : recipe == RecipeType.Brick ? ItemType.Clay : ItemType.RawStone; }
        public static float InputAmount(RecipeType recipe) { return recipe == RecipeType.Plank || recipe == RecipeType.Firewood ? 1 : 2; }
        public static ItemType Output(RecipeType recipe) { return recipe == RecipeType.Plank ? ItemType.Plank : recipe == RecipeType.Firewood ? ItemType.Firewood : recipe == RecipeType.StoneBlock ? ItemType.StoneBlock : recipe == RecipeType.StoneTile ? ItemType.StoneTile : recipe == RecipeType.Brick ? ItemType.Brick : ItemType.None; }
        public static float OutputAmount(RecipeType recipe) { return recipe == RecipeType.Firewood || recipe == RecipeType.StoneTile || recipe == RecipeType.Brick ? 2 : 1; }

        public static void Generate(WorldState world)
        {
            world.ResourceNodes = new ResourceNodeData[world.Cells.Length];
            world.LostResources = new float[ItemCount];
            for (int i = 0; i < world.Cells.Length; i++) world.ResourceNodes[i] = GenerateCell(world, i);
            world.ResourceVersion = 4;
            world.ResourceRevision = 1;
        }

        public static ResourceNodeData GenerateCell(WorldState world, int cell)
        {
            int x = cell % world.Width, y = cell / world.Width, regionX = x / 16, regionY = y / 16;
            TileData tile = world.Cells[cell];
            if (!world.IsWalkable(x, y)) return default(ResourceNodeData);
            uint regional = StableRandom.Hash(world.Seed, regionX, regionY, 5100);
            int centerX = regionX * 16 + 3 + (int)(regional % 10), centerY = regionY * 16 + 3 + (int)((regional >> 8) % 10);
            int radius = 2 + (int)((regional >> 16) % 4), dx = x - centerX, dy = y - centerY;
            float noise = StableRandom.Coordinate(world.Seed, x, y, 5101);
            ResourceType type = ResourceType.None;
            if (dx * dx + dy * dy <= radius * radius && noise < .76f)
            {
                int choice = (int)((regional >> 20) % 16);
                type = choice == 15 && SaltHabitat(world, x, y) ? ResourceType.Salt : choice < 3 ? ResourceType.Stone : choice < 5 ? ResourceType.Clay : choice < 7 ? ResourceType.Coal : choice == 7 ? ResourceType.IronOre : choice == 8 ? ResourceType.CopperOre : choice == 9 ? ResourceType.GoldOre : choice == 10 ? ResourceType.SilverOre : choice == 11 ? ResourceType.Tin : choice < 14 ? ResourceType.Limestone : ResourceType.Sand;
            }
            if (type == ResourceType.None && tile.Vegetation > .35f && (tile.Terrain == TerrainType.Forest ? noise < .55f : tile.Terrain == TerrainType.Grass && noise < .055f)) type = ResourceType.Tree;
            if (type == ResourceType.None && noise > .978f) type = ResourceType.Stone;
            if (type == ResourceType.None || !IsCompatible(type, tile)) return default(ResourceNodeData);
            float capacity = type == ResourceType.Salt ? 4 + StableRandom.Hash(world.Seed, x, y, 5104) % 5 : type == ResourceType.Tree ? 3 + StableRandom.Hash(world.Seed, x, y, 5102) % 3 : 6 + StableRandom.Hash(world.Seed, x, y, 5103) % 7;
            return new ResourceNodeData { Type = type, Capacity = capacity, Amount = type == ResourceType.Tree ? capacity * tile.Vegetation : capacity };
        }
        public static bool SaltHabitat(WorldState world, int x, int y)
        {
            if (!world.InBounds(x, y)) return false;
            TileData tile = world.Cells[world.Index(x, y)];
            if (!IsCompatible(ResourceType.Salt, tile)) return false;
            if (tile.Terrain == TerrainType.Desert || tile.Terrain == TerrainType.Highlands || tile.Moisture <= .28f) return true;
            for (int oy = -2; oy <= 2; oy++) for (int ox = -2; ox <= 2; ox++)
            {
                if (!world.InBounds(x + ox, y + oy)) continue;
                TileData neighbor = world.Cells[world.Index(x + ox, y + oy)];
                if (!neighbor.River && (neighbor.Terrain == TerrainType.DeepWater || neighbor.Terrain == TerrainType.ShallowWater)) return true;
            }
            return false;
        }
    }
}
