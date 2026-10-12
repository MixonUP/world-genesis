using System;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    // Original 32px recipes, one shared atlas. Rendering never creates simulation resources.
    public static class ResourceArt
    {
        public const int Stump = 32, Log = 33, FoodBasket = 34, StoneCargo = 35, OreCargo = 36;
        public const int Planks = 37, Blocks = 38, Tiles = 39, Bricks = 40;
        public const int Hearth = 41, Sawmill = 42, Stoneworks = 43, Kiln = 44, Firewood = 45;
        public const int FemaleElf = 88, FemaleDwarf = 89, FemaleOrc = 90;
        private const int DepositBase = 48;
        private static readonly Color Shadow = new Color(.055f, .10f, .12f, .34f);
        private static readonly Color[] Veins = {
            PixelArt.Hex("bdc5bb"), PixelArt.Hex("b65b4b"), PixelArt.Hex("53b8a1"),
            PixelArt.Hex("edc34e"), PixelArt.Hex("d8e6ee"), PixelArt.Hex("394456"),
            PixelArt.Hex("bd7759"), PixelArt.Hex("dfc68b"), PixelArt.Hex("e3d9b4"), PixelArt.Hex("8aabc0")
        };
        public static int MineralIndex(ResourceType type)
        {
            switch (type)
            {
                case ResourceType.Stone: return 0; case ResourceType.IronOre: return 1;
                case ResourceType.CopperOre: return 2; case ResourceType.GoldOre: return 3;
                case ResourceType.SilverOre: return 4; case ResourceType.Coal: return 5;
                case ResourceType.Clay: return 6; case ResourceType.Sand: return 7;
                case ResourceType.Limestone: return 8; case ResourceType.Tin: return 9;
                default: return -1;
            }
        }
        public static int NodeSprite(ResourceType type, float amount, float capacity)
        {
            if (type == ResourceType.Tree) return amount > 0 ? 0 : Stump;
            int mineral = MineralIndex(type); if (mineral < 0) return -1;
            int level = amount <= 0 ? 0 : amount < capacity * .34f ? 1 : amount < capacity * .7f ? 2 : 3;
            return DepositBase + mineral * 4 + level;
        }
        public static int VisualAmount(float amount, float capacity)
        { return amount <= 0 ? 0 : amount < capacity * .34f ? 1 : amount < capacity * .7f ? 2 : 3; }
        public static Color CargoColor(ItemType type)
        {
            switch (type)
            {
                case ItemType.IronOre: return Veins[1]; case ItemType.CopperOre: return Veins[2];
                case ItemType.GoldOre: return Veins[3]; case ItemType.SilverOre: return Veins[4];
                case ItemType.Coal: return Veins[5]; case ItemType.Clay: return Veins[6];
                case ItemType.Sand: return Veins[7]; case ItemType.Limestone: return Veins[8];
                case ItemType.TinOre: return Veins[9]; default: return Color.white;
            }
        }
        public static int CargoSprite(ItemType type)
        {
            switch (type)
            {
                case ItemType.Log: return Log; case ItemType.Plank: return Planks;
                case ItemType.Firewood: return Firewood; case ItemType.RawStone: return StoneCargo;
                case ItemType.StoneBlock: return Blocks; case ItemType.StoneTile: return Tiles;
                case ItemType.Brick: return Bricks; case ItemType.None: return -1; default: return OreCargo;
            }
        }
        public static int CitizenSprite(UnitData unit)
        {
            if ((int)unit.Species >= 1 && (int)unit.Species <= 3 && unit.IsChild) return 91 + ((int)unit.Species - 1) * 2 + (unit.Sex == Sex.Female ? 1 : 0);
            if ((int)unit.Species >= 1 && (int)unit.Species <= 3 && unit.Sex == Sex.Female) return FemaleElf + (int)unit.Species - 1;
            return 6 + (int)unit.Species;
        }
        public static string ResourceName(ResourceType type)
        {
            switch (type)
            {
                case ResourceType.Tree: return "Дерево"; case ResourceType.Stone: return "Каменная россыпь";
                case ResourceType.IronOre: return "Железная руда"; case ResourceType.CopperOre: return "Медная руда";
                case ResourceType.GoldOre: return "Золотая руда"; case ResourceType.SilverOre: return "Серебряная руда";
                case ResourceType.Coal: return "Уголь"; case ResourceType.Clay: return "Глина";
                case ResourceType.Sand: return "Песок"; case ResourceType.Limestone: return "Известняк";
                case ResourceType.Tin: return "Оловянная руда"; default: return "Нет ресурса";
            }
        }
        public static string ItemName(ItemType type)
        {
            switch (type)
            {
                case ItemType.Log: return "Брёвна"; case ItemType.Plank: return "Доски";
                case ItemType.Firewood: return "Дрова"; case ItemType.RawStone: return "Камень";
                case ItemType.StoneBlock: return "Каменные блоки"; case ItemType.StoneTile: return "Плитка";
                case ItemType.Clay: return "Глина"; case ItemType.Brick: return "Кирпич";
                case ItemType.IronOre: return "Железная руда"; case ItemType.CopperOre: return "Медная руда";
                case ItemType.GoldOre: return "Золотая руда"; case ItemType.SilverOre: return "Серебряная руда";
                case ItemType.Coal: return "Уголь"; case ItemType.Sand: return "Песок";
                case ItemType.Limestone: return "Известняк"; case ItemType.TinOre: return "Оловянная руда";
                default: return "Нет груза";
            }
        }
        public static string BuildingName(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.Hall: return "Общинный костёр"; case BuildingType.House: return "Жилище";
                case BuildingType.Farm: return "Поле"; case BuildingType.Tower: return "Дозорная башня";
                case BuildingType.Sawmill: return "Лесопилка"; case BuildingType.Stoneworks: return "Каменная мастерская";
                case BuildingType.Kiln: return "Кирпичная печь"; default: return type.ToString();
            }
        }
        public static string RecipeName(RecipeType recipe)
        {
            switch (recipe)
            {
                case RecipeType.Plank: return "Распил досок"; case RecipeType.Firewood: return "Колка дров";
                case RecipeType.StoneBlock: return "Тёска блоков"; case RecipeType.StoneTile: return "Резка плитки";
                case RecipeType.Brick: return "Обжиг кирпича"; default: return "Ожидает работу";
            }
        }

        public static void PaintAtlas(Color32[] pixels, int width)
        {
            Painter p = new Painter(pixels, width);
            Color bark = PixelArt.Hex("654634"), barkLight = PixelArt.Hex("9c7147"), cut = PixelArt.Hex("d7b376"), rim = PixelArt.Hex("efcf92");
            p.Ellipse(Stump, 16, 5, 10, 3, Shadow); p.Rect(Stump, 10, 6, 12, 8, bark); p.Rect(Stump, 13, 7, 3, 8, barkLight);
            p.Ellipse(Stump, 16, 14, 7, 3, cut); p.Ellipse(Stump, 16, 14, 4, 2, barkLight); p.Line(Stump, 13, 14, 19, 14, rim);
            p.Line(Stump, 10, 9, 6, 6, bark); p.Line(Stump, 22, 9, 26, 6, bark);
            // Log is drawn along X; its real movement direction rotates the quad, with cut end and rings.
            p.Ellipse(Log, 16, 11, 14, 4, Shadow); p.Rect(Log, 5, 11, 22, 10, bark); p.Rect(Log, 6, 17, 21, 3, barkLight);
            p.Line(Log, 7, 13, 24, 13, PixelArt.Hex("81583b")); p.Line(Log, 8, 20, 22, 20, cut);
            p.Ellipse(Log, 27, 16, 3, 5, cut); p.Ellipse(Log, 27, 16, 2, 3, barkLight); p.Line(Log, 27, 14, 27, 18, rim);
            p.Rect(Firewood, 4, 11, 22, 5, barkLight); p.Rect(Firewood, 7, 17, 22, 5, bark); p.Rect(Firewood, 5, 12, 3, 4, cut); p.Rect(Firewood, 26, 18, 3, 4, cut);
            p.Line(Firewood, 15, 10, 16, 24, PixelArt.Hex("ccb08c"));
            p.Ellipse(FoodBasket, 16, 11, 10, 3, Shadow); p.Polygon(FoodBasket, new[] { 6, 18, 26, 18, 23, 7, 9, 7 }, barkLight);
            for (int x = 9; x < 25; x += 4) p.Line(FoodBasket, x, 8, x, 18, cut);
            p.Ellipse(FoodBasket, 12, 20, 4, 4, PixelArt.Hex("a84f47")); p.Ellipse(FoodBasket, 20, 21, 4, 4, PixelArt.Hex("d9bb65")); p.Rect(FoodBasket, 7, 16, 19, 3, bark);
            p.Rock(StoneCargo, 16, 7, 11, 15, PixelArt.Hex("7f9395"), PixelArt.Hex("c0c9bb"), PixelArt.Hex("506772"));
            p.Rock(OreCargo, 15, 7, 11, 14, PixelArt.Hex("c5d0c8"), Color.white, PixelArt.Hex("637580"));
            p.Line(OreCargo, 12, 9, 13, 20, PixelArt.Hex("d9c09a")); p.Line(OreCargo, 20, 8, 21, 18, PixelArt.Hex("d9c09a"));
            for (int row = 0; row < 3; row++)
            {
                p.Rect(Planks, 4 + row % 2, 7 + row * 6, 25, 5, barkLight); p.Rect(Planks, 4 + row % 2, 10 + row * 6, 25, 2, cut); p.Line(Planks, 9, 9 + row * 6, 24, 9 + row * 6, bark);
                p.Rect(Blocks, 4 + row % 2 * 3, 6 + row * 7, 21, 6, PixelArt.Hex("677e87")); p.Rect(Blocks, 4 + row % 2 * 3, 9 + row * 7, 21, 3, PixelArt.Hex("bdc8be"));
                p.Rect(Tiles, 4 + row, 6 + row * 5, 24, 4, PixelArt.Hex("748f94")); p.Rect(Tiles, 4 + row, 8 + row * 5, 24, 2, PixelArt.Hex("d6d2b8"));
                p.Rect(Bricks, 3 + row % 2 * 2, 6 + row * 7, 25, 6, PixelArt.Hex("9c5141")); p.Rect(Bricks, 4 + row % 2 * 2, 9 + row * 7, 23, 3, PixelArt.Hex("cb8763")); p.Line(Bricks, 15, 6 + row * 7, 15, 12 + row * 7, PixelArt.Hex("653f39"));
            }
            // Founding camp: seating logs, a stone-ring hearth and a low supplies tarp.
            p.Ellipse(Hearth, 16, 5, 15, 4, Shadow);
            p.Rect(Hearth, 3, 5, 8, 3, bark); p.Rect(Hearth, 21, 7, 9, 3, barkLight);
            p.Rect(Hearth, 4, 18, 2, 10, bark); p.Rect(Hearth, 13, 18, 2, 10, bark);
            p.Polygon(Hearth, new[] { 2, 26, 7, 31, 17, 29, 14, 22 }, PixelArt.Hex("bc9864"));
            p.Line(Hearth, 3, 25, 14, 23, cut); p.Rect(Hearth, 5, 17, 7, 4, barkLight);
            p.Ellipse(Hearth, 19, 12, 8, 4, PixelArt.Hex("576971")); p.Ellipse(Hearth, 19, 13, 6, 3, PixelArt.Hex("9b9d87")); p.Ellipse(Hearth, 19, 13, 4, 2, bark);
            p.Line(Hearth, 15, 12, 23, 16, barkLight); p.Line(Hearth, 16, 16, 22, 12, cut);
            p.Polygon(Hearth, new[] { 15, 15, 16, 21, 19, 24, 20, 20, 23, 22, 23, 16, 20, 14 }, PixelArt.Hex("e87c41"));
            p.Polygon(Hearth, new[] { 17, 15, 18, 19, 20, 21, 20, 17, 22, 16, 20, 14 }, PixelArt.Hex("ffd38a"));
            // Open-sided saw shed: cutting bench, saw blade, stacked timber, an actual output rack.
            p.Ellipse(Sawmill, 16, 4, 15, 4, Shadow); p.Rect(Sawmill, 6, 6, 3, 18, bark); p.Rect(Sawmill, 24, 6, 3, 18, bark);
            p.Rect(Sawmill, 7, 7, 19, 8, PixelArt.Hex("8a6c48")); p.Rect(Sawmill, 5, 13, 24, 4, cut); p.Rect(Sawmill, 8, 14, 19, 2, barkLight);
            p.Rect(Sawmill, 7, 9, 20, 2, bark); p.Rect(Sawmill, 2, 3, 9, 3, barkLight); p.Rect(Sawmill, 2, 7, 7, 3, cut);
            p.Polygon(Sawmill, new[] { 2, 23, 9, 30, 26, 29, 31, 23 }, PixelArt.Hex("446d68")); p.Line(Sawmill, 4, 24, 29, 24, PixelArt.Hex("76a286"));
            p.Ellipse(Sawmill, 21, 17, 5, 5, PixelArt.Hex("657b80")); p.Ellipse(Sawmill, 21, 17, 3, 3, PixelArt.Hex("c7d5ca")); p.Dot(Sawmill, 21, 17, bark);
            for (int x = 17; x <= 25; x += 2) p.Dot(Sawmill, x, 21, PixelArt.Hex("e2e4ce"));
            // Stoneworks: pale cut blocks, a heavy workbench and timber lifting frame.
            p.Ellipse(Stoneworks, 16, 4, 15, 4, Shadow); p.Rect(Stoneworks, 4, 4, 3, 23, bark); p.Rect(Stoneworks, 4, 25, 21, 3, barkLight); p.Line(Stoneworks, 6, 22, 13, 27, cut);
            p.Line(Stoneworks, 22, 24, 22, 15, PixelArt.Hex("c6bb92")); p.Rect(Stoneworks, 18, 13, 9, 7, PixelArt.Hex("7e9397")); p.Rect(Stoneworks, 18, 17, 9, 3, PixelArt.Hex("ced4c1"));
            p.Rect(Stoneworks, 8, 6, 20, 5, PixelArt.Hex("516b75")); p.Rect(Stoneworks, 9, 10, 20, 3, PixelArt.Hex("bbc6bb")); p.Line(Stoneworks, 18, 6, 18, 13, PixelArt.Hex("4c616b"));
            p.Rect(Stoneworks, 7, 14, 8, 6, PixelArt.Hex("9dac9f")); p.Rect(Stoneworks, 8, 18, 7, 3, PixelArt.Hex("e0dac2")); p.Line(Stoneworks, 11, 21, 15, 25, barkLight); p.Rect(Stoneworks, 13, 24, 6, 2, PixelArt.Hex("577783"));
            // Kiln: brick dome, soot mouth, fire and chimney; no smelter is implied.
            p.Ellipse(Kiln, 16, 4, 14, 4, Shadow); p.Rect(Kiln, 22, 17, 5, 13, PixelArt.Hex("8b5043")); p.Rect(Kiln, 21, 28, 7, 3, PixelArt.Hex("c08765"));
            p.Ellipse(Kiln, 15, 14, 12, 10, PixelArt.Hex("934e3e")); p.Ellipse(Kiln, 13, 17, 9, 8, PixelArt.Hex("c07b58"));
            for (int y = 9; y <= 22; y += 5) p.Line(Kiln, 6, y, 23, y, PixelArt.Hex("9d614a"));
            p.Rect(Kiln, 11, 4, 11, 11, PixelArt.Ink); p.Ellipse(Kiln, 16, 14, 5, 4, PixelArt.Ink);
            for (int x = 2; x <= 8; x += 3) p.Rect(Kiln, x, 4, 3, 5, PixelArt.Hex("c0805d"));
            // Civilized non-human women retain the faction garment and species features;
            // braids and a wider apron create a distinct silhouette, not just a colour swap.
            for (int species = 1; species <= 3; species++)
            {
                int s = FemaleElf + species - 1;
                Color skin = PixelArt.Hex(species == 1 ? "d8caa7" : species == 2 ? "d1ad86" : "9bae70");
                Color hair = PixelArt.Hex(species == 1 ? "d4dfb8" : species == 2 ? "9c583b" : "4e4438");
                p.Ellipse(s, 16, 5, 6, 2, Shadow);
                p.Rect(s, 13, 5, 2, 4, PixelArt.Ink); p.Rect(s, 18, 5, 2, 4, PixelArt.Ink);
                p.Polygon(s, new[] { 13, 15, 20, 15, 23, 8, 10, 8 }, Color.white);
                p.Rect(s, 14, 9, 5, 6, PixelArt.Hex("daceaa")); p.Rect(s, 10, 11, 3, 4, skin); p.Rect(s, 20, 11, 3, 4, skin);
                p.Rect(s, 13, 15, 7, 7, skin); p.Rect(s, 12, 20, 9, 3, hair);
                p.Rect(s, 11, 15, 3, 7, hair); p.Rect(s, 10, 12, 3, 5, hair); p.Rect(s, 9, 10, 3, 3, hair);
                p.Dot(s, 11, 13, PixelArt.Gold); p.Dot(s, 18, 18, PixelArt.Ink);
                if (species == 1) { p.Dot(s, 10, 18, skin); p.Dot(s, 22, 18, skin); }
                if (species == 3) { p.Rect(s, 17, 16, 1, 2, PixelArt.Hex("e7dfb5")); p.Rect(s, 20, 16, 1, 2, PixelArt.Hex("e7dfb5")); }
                for (int female = 0; female < 2; female++)
                {
                    int child = 91 + (species - 1) * 2 + female;
                    p.Ellipse(child, 16, 5, 5, 2, Shadow);
                    p.Rect(child, 13, 6, 2, 3, PixelArt.Ink); p.Rect(child, 18, 6, 2, 3, PixelArt.Ink);
                    p.Rect(child, 12, 9, 9, 5, Color.white); p.Rect(child, 11, 10, 2, 3, skin); p.Rect(child, 21, 10, 2, 3, skin);
                    p.Rect(child, 12, 14, 10, 8, skin); p.Rect(child, 12, 20, 10, 3, hair); p.Dot(child, 19, 17, PixelArt.Ink);
                    if (species == 1) { p.Dot(child, 10, 17, skin); p.Dot(child, 23, 17, skin); }
                    if (female != 0) { p.Rect(child, 10, 14, 3, 8, hair); p.Rect(child, 22, 14, 3, 8, hair); p.Dot(child, 11, 14, PixelArt.Gold); p.Dot(child, 23, 14, PixelArt.Gold); }
                }
            }
            // Ten distinct minerals × four amounts. The empty scar remains, never a full fake pile.
            for (int kind = 0; kind < 10; kind++) for (int level = 0; level <= 3; level++)
            {
                int sprite = DepositBase + kind * 4 + level;
                p.Ellipse(sprite, 16, 6, 13, 4, Shadow);
                if (level == 0)
                {
                    p.Ellipse(sprite, 16, 8, 10, 3, PixelArt.Hex("6c766b")); p.Ellipse(sprite, 16, 9, 7, 2, PixelArt.Hex("465a55"));
                    p.Dot(sprite, 8, 9, Veins[kind] * .72f); p.Dot(sprite, 23, 8, Veins[kind] * .72f); continue;
                }
                bool soft = kind == 6 || kind == 7;
                Color baseRock = kind == 5 ? PixelArt.Hex("344552") : kind == 8 ? PixelArt.Hex("9eaa99") : PixelArt.Hex("71848b");
                Color lightRock = kind == 5 ? PixelArt.Hex("526874") : kind == 8 ? PixelArt.Hex("dcd9bb") : PixelArt.Hex("a0b0ac");
                if (soft) { baseRock = Veins[kind] * .78f; baseRock.a = 1; lightRock = Veins[kind]; }
                if (level >= 3) p.Rock(sprite, 10, 9, 8, 13, baseRock, lightRock, PixelArt.Hex("4e6570"));
                p.Rock(sprite, 17, 6, level == 1 ? 7 : 10, level == 1 ? 9 : 18, baseRock, lightRock, soft ? baseRock : PixelArt.Hex("4c6370"));
                if (level >= 2) p.Rock(sprite, 25, 5, 5, 9, baseRock, lightRock, PixelArt.Hex("516b70"));
                if (kind != 0 && !soft)
                {
                    p.Line(sprite, 15, 8, 18, level == 1 ? 13 : 18, Veins[kind]); p.Line(sprite, 18, level == 1 ? 13 : 18, 22, level == 1 ? 11 : 16, Veins[kind]);
                    if (level == 3) { p.Line(sprite, 7, 12, 11, 17, Veins[kind]); p.Dot(sprite, 9, 18, Color.Lerp(Veins[kind], Color.white, .35f)); }
                    p.Dot(sprite, 19, level == 1 ? 12 : 18, Color.Lerp(Veins[kind], Color.white, .4f));
                }
                else if (soft) { p.Line(sprite, 12, 9, 20, 10, Color.Lerp(Veins[kind], Color.white, .22f)); p.Dot(sprite, 23, 8, Veins[kind]); }
                p.Dot(sprite, 6, 6, lightRock); p.Dot(sprite, 28, 7, baseRock);
            }
        }

        private sealed class Painter
        {
            private readonly Color32[] pixels; private readonly int width;
            public Painter(Color32[] pixels, int width) { this.pixels = pixels; this.width = width; }
            public void Dot(int sprite, int x, int y, Color color) { if ((uint)x < 32 && (uint)y < 32) pixels[(sprite / 8 * 32 + y) * width + sprite % 8 * 32 + x] = color; }
            public void Rect(int s, int x, int y, int w, int h, Color c) { for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++) Dot(s, xx, yy, c); }
            public void Ellipse(int s, int cx, int cy, int rx, int ry, Color c) { for (int y = -ry; y <= ry; y++) for (int x = -rx; x <= rx; x++) if ((float)x * x / (rx * rx) + (float)y * y / (ry * ry) <= 1) Dot(s, cx + x, cy + y, c); }
            public void Line(int s, int x0, int y0, int x1, int y1, Color c)
            {
                int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, error = dx + dy;
                while (true) { Dot(s, x0, y0, c); if (x0 == x1 && y0 == y1) break; int e = error * 2; if (e >= dy) { error += dy; x0 += sx; } if (e <= dx) { error += dx; y0 += sy; } }
            }
            public void Polygon(int s, int[] xy, Color color)
            {
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                {
                    bool inside = false;
                    for (int a = 0, b = xy.Length - 2; a < xy.Length; b = a, a += 2)
                        if ((xy[a + 1] > y) != (xy[b + 1] > y) && x < (float)(xy[b] - xy[a]) * (y - xy[a + 1]) / (xy[b + 1] - xy[a + 1]) + xy[a]) inside = !inside;
                    if (inside) Dot(s, x, y, color);
                }
            }
            public void Rock(int s, int cx, int y, int radius, int height, Color face, Color light, Color dark)
            {
                Polygon(s, new[] { cx - radius, y + 2, cx - radius + 2, y + height - 3, cx, y + height, cx + radius - 2, y + height - 3, cx + radius, y + 3, cx + 3, y }, dark);
                Polygon(s, new[] { cx - radius + 1, y + 3, cx - radius + 3, y + height - 4, cx, y + height - 1, cx + 1, y + 4 }, face);
                Polygon(s, new[] { cx - radius + 3, y + height - 4, cx, y + height - 1, cx + radius - 3, y + height - 4, cx, y + height - 7 }, light);
            }
        }
    }
}
