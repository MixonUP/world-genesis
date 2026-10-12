using System;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    // Original pixel recipes. Crop stages are selected from actual CropGrowth, never a UI clock.
    public static class AgricultureArt
    {
        public const int Size = 256, Frame = 32, Pasture = 20, Brewery = 21, Wagon = 22, ProductFirst = 23;
        public static Rect Uv(int sprite) { return new Rect((sprite % 8 * 32 + .25f) / Size, (sprite / 8 * 32 + .25f) / Size, 31.5f / Size, 31.5f / Size); }
        public static int FieldSprite(CropType crop, float growth) { return Mathf.Clamp((int)crop, 0, 4) * 4 + Mathf.Clamp((int)(growth * 4), 0, 3); }
        public static int ProductSprite(AgriculturalProduct product) { return ProductFirst + Mathf.Clamp((int)product - 1, 0, 11); }
        public static string CropName(CropType crop)
        {
            switch (crop) { case CropType.Flax: return "Лён"; case CropType.BerryOrchard: return "Ягодный сад"; case CropType.HerbGarden: return "Лекарственный сад"; case CropType.Hay: return "Сенокос"; default: return "Пшеница"; }
        }
        public static string ProductName(AgriculturalProduct product)
        {
            switch (product) { case AgriculturalProduct.Grain: return "Зерно"; case AgriculturalProduct.Fiber: return "Волокно"; case AgriculturalProduct.Berries: return "Ягоды"; case AgriculturalProduct.Herbs: return "Травы"; case AgriculturalProduct.Hay: return "Сено"; case AgriculturalProduct.Meat: return "Мясо"; case AgriculturalProduct.Hide: return "Шкура"; case AgriculturalProduct.Milk: return "Молоко"; case AgriculturalProduct.Egg: return "Яйца"; case AgriculturalProduct.RareHorn: return "Редкий рог"; case AgriculturalProduct.RarePelt: return "Редкий мех"; case AgriculturalProduct.Ale: return "Эль"; default: return "Нет"; }
        }
        public static Texture2D CreateAtlas()
        {
            Color32[] pixels = new Color32[Size * Size]; Painter p = new Painter(pixels);
            Color soil = PixelArt.Hex("796446"), furrow = PixelArt.Hex("9a8056"), leaf = PixelArt.Hex("6e9253"), gold = PixelArt.Hex("d1b75d");
            Color wood = PixelArt.Hex("96764d"), pale = PixelArt.Hex("ceb78a"), dark = PixelArt.Hex("4e5147");
            for (int crop = 0; crop < 5; crop++) for (int stage = 0; stage < 4; stage++)
            {
                int s = crop * 4 + stage;
                p.Rect(s, 2, 3, 28, 22, soil); p.Rect(s, 3, 4, 26, 20, furrow);
                for (int y = 6; y < 25; y += 6) p.Rect(s, 4, y, 24, 2, soil);
                if (crop == 2)
                {
                    for (int row = 0; row < 2; row++) for (int col = 0; col < 3; col++)
                    {
                        int x = 7 + col * 9, y = 8 + row * 11, radius = 2 + stage;
                        p.Rect(s, x, y - 2, 1, 5, wood); p.Ellipse(s, x, y + 2, radius, radius, leaf); p.Ellipse(s, x - 1, y + 3, Math.Max(1, radius - 2), 2, PixelArt.Hex("91a65b"));
                        if (stage == 3) { p.Dot(s, x - 2, y + 3, PixelArt.Hex("b66270")); p.Dot(s, x + 2, y + 1, PixelArt.Hex("d28a77")); }
                    }
                }
                else for (int row = 0; row < 3; row++) for (int col = 0; col < 5; col++)
                {
                    int x = 5 + col * 5 + (row % 2), y = 6 + row * 6, h = 1 + stage * 2;
                    Color tip = crop == 0 ? gold : crop == 1 ? PixelArt.Hex("9bbbc5") : crop == 3 ? PixelArt.Hex("b39bbe") : PixelArt.Hex("b6ba71");
                    p.Rect(s, x, y, 1, h, leaf); p.Line(s, x - 1, y + h / 2, x + 1, y + h / 2 + 1, leaf);
                    if (stage >= 2) { p.Rect(s, x - 1, y + h - 1, 3, crop == 0 ? 3 : 1, tip); if (crop == 4) p.Line(s, x + 2, y, x, y + h, tip); }
                }
                p.Rect(s, 1, 3, 1, 23, pale); p.Rect(s, 30, 3, 1, 23, wood);
            }
            // An enclosure has an actual visible opening. Animals are separate simulated units.
            p.Rect(Pasture, 3, 4, 26, 20, PixelArt.Hex("668256"));
            for (int i = 4; i <= 28; i += 6) { p.Rect(Pasture, i, 6, 2, 21, wood); p.Rect(Pasture, 3, i - 1, 2, 5, wood); p.Rect(Pasture, 28, i - 1, 2, 5, wood); }
            p.Rect(Pasture, 3, 21, 27, 2, pale); p.Rect(Pasture, 3, 25, 27, 2, pale); p.Rect(Pasture, 3, 7, 8, 2, pale); p.Rect(Pasture, 21, 7, 9, 2, pale);
            p.Rect(Pasture, 7, 11, 9, 3, wood); p.Rect(Pasture, 8, 13, 7, 2, PixelArt.Hex("8eada2"));
            // Brewery: copper kettle, brick chimney and empty barrel fittings; no painted stock count.
            p.Rect(Brewery, 4, 5, 22, 17, PixelArt.Hex("aaa284")); p.Rect(Brewery, 6, 5, 6, 12, dark); p.Rect(Brewery, 16, 12, 6, 5, PixelArt.Hex("6c9090"));
            for (int y = 21; y <= 29; y++) p.Rect(Brewery, 3 + (y - 21), y, 25 - (y - 21) * 2, 1, y % 3 == 0 ? PixelArt.Hex("bd8253") : PixelArt.Hex("925d43"));
            p.Rect(Brewery, 22, 24, 4, 8, PixelArt.Hex("9a7660")); p.Ellipse(Brewery, 25, 9, 6, 6, PixelArt.Hex("b28c59")); p.Rect(Brewery, 20, 7, 11, 2, dark); p.Rect(Brewery, 20, 11, 11, 2, dark); p.Ellipse(Brewery, 25, 15, 5, 2, PixelArt.Hex("d4b474"));
            // Empty trade wagon. Goods are drawn only when a real cargo record is present.
            p.Rect(Wagon, 5, 8, 19, 13, wood); p.Rect(Wagon, 6, 10, 17, 9, PixelArt.Hex("625845")); p.Rect(Wagon, 5, 19, 20, 3, pale);
            p.Rect(Wagon, 4, 8, 21, 2, pale); p.Rect(Wagon, 23, 9, 9, 2, wood);
            for (int x = 7; x < 24; x += 5) p.Rect(Wagon, x, 10, 1, 9, pale);
            p.Ellipse(Wagon, 8, 6, 4, 4, dark); p.Ellipse(Wagon, 21, 6, 4, 4, dark); p.Rect(Wagon, 7, 3, 2, 7, pale); p.Rect(Wagon, 18, 5, 7, 2, pale); p.Dot(Wagon, 8, 6, wood);
            for (int product = 1; product <= 12; product++) PaintProduct(p, ProductFirst + product - 1, (AgriculturalProduct)product);
            Texture2D atlas = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "World Genesis • authored farms and goods", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            atlas.SetPixels32(pixels); atlas.Apply(false, true); return atlas;
        }
        private static void PaintProduct(Painter p, int s, AgriculturalProduct product)
        {
            Color gold = PixelArt.Hex("d9b969"), leaf = PixelArt.Hex("719059"), cloth = PixelArt.Hex("b8b397"), ink = PixelArt.Ink;
            switch (product)
            {
                case AgriculturalProduct.Grain: case AgriculturalProduct.Fiber: case AgriculturalProduct.Hay: case AgriculturalProduct.Herbs:
                    for (int i = 0; i < 7; i++) { int x = 8 + i * 2; p.Line(s, 13 + i % 3, 7, x, 25 - i % 3, product == AgriculturalProduct.Hay || product == AgriculturalProduct.Grain ? gold : leaf); p.Ellipse(s, x, 23 - i % 3, 2, 3, product == AgriculturalProduct.Fiber ? cloth : product == AgriculturalProduct.Herbs ? PixelArt.Hex("a78db0") : gold); }
                    p.Rect(s, 11, 11, 11, 3, PixelArt.Hex("a47550")); break;
                case AgriculturalProduct.Berries:
                    p.Ellipse(s, 16, 13, 10, 5, leaf); for (int i = 0; i < 7; i++) p.Ellipse(s, 8 + i * 3, 13 + i % 3 * 3, 3, 3, i % 2 == 0 ? PixelArt.Hex("b45f74") : PixelArt.Hex("824c6f")); break;
                case AgriculturalProduct.Meat:
                    p.Ellipse(s, 15, 16, 9, 7, PixelArt.Hex("ac6260")); p.Ellipse(s, 15, 17, 6, 4, PixelArt.Hex("d89a86")); p.Line(s, 20, 13, 27, 7, cloth); p.Ellipse(s, 27, 7, 3, 2, cloth); break;
                case AgriculturalProduct.Hide: case AgriculturalProduct.RarePelt:
                    Color hide = product == AgriculturalProduct.Hide ? PixelArt.Hex("a57a54") : PixelArt.Hex("687e90"); p.Ellipse(s, 16, 16, 9, 10, hide); p.Rect(s, 5, 6, 5, 5, hide); p.Rect(s, 23, 6, 5, 5, hide); p.Rect(s, 5, 23, 5, 4, hide); p.Rect(s, 23, 23, 5, 4, hide); p.Line(s, 16, 8, 16, 25, Color.Lerp(hide, cloth, .4f)); break;
                case AgriculturalProduct.Milk:
                    p.Ellipse(s, 16, 15, 7, 10, cloth); p.Rect(s, 11, 24, 10, 3, PixelArt.Hex("909f9c")); p.Rect(s, 22, 15, 4, 7, cloth); p.Rect(s, 12, 13, 8, 7, PixelArt.Hex("e1ded0")); break;
                case AgriculturalProduct.Egg:
                    p.Ellipse(s, 12, 14, 5, 7, PixelArt.Hex("e4ddbf")); p.Ellipse(s, 22, 17, 5, 7, PixelArt.Hex("d4c59e")); break;
                case AgriculturalProduct.RareHorn:
                    p.Line(s, 12, 6, 18, 20, PixelArt.Hex("90bfb6")); p.Line(s, 13, 6, 19, 20, PixelArt.Hex("cadccd")); p.Line(s, 18, 20, 17, 28, cloth); p.Line(s, 15, 14, 23, 19, cloth); p.Line(s, 17, 21, 10, 25, cloth); break;
                case AgriculturalProduct.Ale:
                    p.Rect(s, 9, 7, 13, 18, PixelArt.Hex("a17d50")); p.Rect(s, 9, 10, 13, 2, ink); p.Rect(s, 9, 21, 13, 2, ink); p.Ellipse(s, 15, 25, 7, 2, PixelArt.Hex("dacda5")); p.Rect(s, 21, 12, 5, 10, PixelArt.Hex("a17d50")); p.Rect(s, 22, 14, 2, 6, Color.clear); break;
            }
        }
        private sealed class Painter
        {
            private readonly Color32[] pixels; public Painter(Color32[] value) { pixels = value; }
            public void Dot(int s, int x, int y, Color color) { if ((uint)x < 32 && (uint)y < 32) pixels[(s / 8 * 32 + y) * Size + s % 8 * 32 + x] = color; }
            public void Rect(int s, int x, int y, int w, int h, Color c) { for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++) Dot(s, xx, yy, c); }
            public void Ellipse(int s, int cx, int cy, int rx, int ry, Color c) { for (int y = -ry; y <= ry; y++) for (int x = -rx; x <= rx; x++) if ((float)x * x / (rx * rx) + (float)y * y / (ry * ry) <= 1) Dot(s, cx + x, cy + y, c); }
            public void Line(int s, int x0, int y0, int x1, int y1, Color c) { int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, e = dx + dy; while (true) { Dot(s, x0, y0, c); if (x0 == x1 && y0 == y1) break; int ee = e * 2; if (ee >= dy) { e += dy; x0 += sx; } if (ee <= dx) { e += dx; y0 += sy; } } }
        }
    }
}
