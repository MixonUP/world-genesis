using System;
using UnityEngine;

namespace WorldGenesis.Runtime
{
    // Original project-authored 32px recipes. These slots share the existing point atlas.
    public static class SocietyArt
    {
        public const int HouseOne = 97, HouseTwo = 98, HouseThree = 99, HouseFour = 100, HouseFive = 101;
        public const int Well = 102, Warehouse = 103, Shop = 104, WallHorizontal = 105, WallVertical = 106;
        public const int GateOpen = 107, GateClosed = 108, GateVerticalOpen = 109, GateVerticalClosed = 110;
        public const int FishFirst = 111, FishSecond = 112, FishThird = 113, WaterPail = 114, School = 115, WellEmpty = 116;
        public static int HouseSprite(int level) { return HouseOne + Math.Max(1, Math.Min(5, level)) - 1; }

        public static void PaintAtlas(Color32[] pixels, int width)
        {
            Painter p = new Painter(pixels, width);
            Color ink = PixelArt.Ink, wood = PixelArt.Hex("815b3f"), woodLight = PixelArt.Hex("c09560"), stone = PixelArt.Hex("849492"), stoneLight = PixelArt.Hex("bfccc0");
            Color straw = PixelArt.Hex("d2b476"), plaster = PixelArt.Hex("e0ce9f"), blue = PixelArt.Hex("4d757d"), blueLight = PixelArt.Hex("83a39c");
            Color shadow = new Color(.04f, .09f, .12f, .32f);
            for (int level = 1; level <= 5; level++)
            {
                int s = HouseSprite(level); p.Ellipse(s, 16, 4, 14, 3, shadow);
                int floor = level <= 2 ? 5 : 4;
                p.Rect(s, 5, floor, 23, level <= 2 ? 12 : 18, level == 1 ? PixelArt.Hex("b28f59") : level == 2 ? wood : plaster);
                p.Rect(s, 5, floor, 23, 3, level <= 2 ? wood : stone);
                if (level == 1)
                {
                    p.Poly(s, new[] { 2, 16, 9, 27, 22, 27, 30, 16 }, straw);
                    for (int x = 7; x < 27; x += 4) p.Line(s, x, 17, x + (x < 16 ? 3 : -3), 25, woodLight);
                    p.Line(s, 4, 16, 29, 16, wood); p.Rect(s, 13, 5, 6, 10, ink); p.Rect(s, 8, 9, 3, 4, wood);
                }
                else
                {
                    Color roof = level == 2 ? PixelArt.Hex("637d64") : level == 3 ? PixelArt.Hex("b3664e") : level == 4 ? PixelArt.Hex("98584b") : blue;
                    p.Poly(s, new[] { 2, 18, 13, 30, 20, 30, 31, 18 }, roof);
                    p.Line(s, 3, 18, 30, 18, ink);
                    for (int y = 21; y <= 27; y += 3) p.Line(s, 13 - (30 - y), y, 20 + (30 - y), y, level == 5 ? blueLight : Color.Lerp(roof, straw, .22f));
                    p.Rect(s, 22, 25, 4, 6, level == 2 ? wood : stone); p.Rect(s, 21, 30, 6, 1, stoneLight);
                    p.Rect(s, 13, 5, 6, 10, ink); p.Rect(s, 14, 5, 4, 9, wood); p.Dot(s, 17, 9, straw);
                    if (level == 2)
                    {
                        p.Rect(s, 5, 6, 2, 13, woodLight); p.Rect(s, 25, 6, 2, 13, woodLight); p.Line(s, 5, 11, 27, 11, woodLight);
                        Window(p, s, 8, 12, 3, 4, straw); Window(p, s, 21, 12, 3, 4, straw);
                    }
                    if (level == 3)
                    {
                        for (int y = 7; y <= 17; y += 4) { p.Line(s, 5, y, 11, y, stone); p.Line(s, 21, y, 27, y, stone); }
                        Window(p, s, 8, 11, 4, 5, blue); Window(p, s, 21, 11, 4, 5, blue);
                    }
                    if (level >= 4)
                    {
                        p.Line(s, 5, 13, 27, 13, wood); Window(p, s, 8, 14, 4, 4, blue); Window(p, s, 20, 14, 4, 4, blue);
                        Window(p, s, 8, 7, 3, 4, blue); Window(p, s, 22, 7, 3, 4, blue);
                        p.Rect(s, 4, 4, 25, 2, stoneLight);
                    }
                    if (level == 5)
                    {
                        p.Poly(s, new[] { 11, 23, 16, 28, 21, 23 }, stoneLight); p.Rect(s, 12, 21, 8, 3, plaster); Window(p, s, 14, 22, 4, 3, blue);
                        p.Rect(s, 2, 5, 3, 12, stone); p.Rect(s, 28, 5, 3, 12, stone); p.Line(s, 8, 12, 24, 12, woodLight);
                        for (int x = 8; x <= 24; x += 4) p.Rect(s, x, 10, 1, 3, woodLight);
                        p.Rect(s, 2, 16, 4, 2, stoneLight); p.Rect(s, 27, 16, 4, 2, stoneLight);
                    }
                }
            }
            // Well: stone curb, dark shaft, water and a visible windlass bucket.
            p.Ellipse(Well, 16, 5, 13, 4, shadow); p.Ellipse(Well, 16, 10, 11, 6, stone); p.Rect(Well, 5, 10, 23, 6, stone);
            p.Ellipse(Well, 16, 16, 11, 6, stoneLight); p.Ellipse(Well, 16, 16, 8, 4, ink); p.Ellipse(Well, 16, 15, 6, 2, PixelArt.Hex("457e8b"));
            for (int x = 7; x < 28; x += 6) p.Line(Well, x, 8, x - 1, 13, PixelArt.Hex("647d82"));
            p.Rect(Well, 7, 14, 3, 16, wood); p.Rect(Well, 24, 14, 3, 16, wood); p.Rect(Well, 7, 25, 20, 3, woodLight);
            p.Line(Well, 18, 27, 18, 17, straw); p.Rect(Well, 15, 15, 6, 5, woodLight); p.Line(Well, 15, 19, 20, 19, stoneLight); p.Rect(Well, 27, 22, 2, 5, woodLight);
            Color32 waterColor = PixelArt.Hex("457e8b");
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                Color32 c = pixels[(Well / 8 * 32 + y) * width + Well % 8 * 32 + x];
                p.Dot(WellEmpty, x, y, c.r == waterColor.r && c.g == waterColor.g && c.b == waterColor.b && c.a == waterColor.a ? ink : (Color)c);
            }
            // Warehouse: broad doors and a hoist; stock parcels are added from actual inventories.
            p.Ellipse(Warehouse, 16, 4, 15, 4, shadow); p.Rect(Warehouse, 3, 5, 27, 18, wood); p.Rect(Warehouse, 3, 5, 27, 3, stone);
            for (int x = 5; x <= 29; x += 4) p.Line(Warehouse, x, 8, x, 22, woodLight);
            p.Rect(Warehouse, 10, 6, 14, 15, ink); p.Rect(Warehouse, 7, 7, 4, 13, woodLight); p.Rect(Warehouse, 23, 7, 4, 13, woodLight);
            p.Poly(Warehouse, new[] { 0, 22, 9, 31, 24, 31, 32, 22 }, blue); p.Line(Warehouse, 2, 23, 30, 23, blueLight);
            p.Rect(Warehouse, 15, 21, 3, 8, woodLight); p.Line(Warehouse, 17, 23, 20, 19, straw); p.Rect(Warehouse, 3, 4, 27, 2, stoneLight);
            // Shop: red/cream cloth awning over an empty counter; no invented inventory.
            p.Ellipse(Shop, 16, 4, 15, 4, shadow); p.Rect(Shop, 5, 7, 22, 17, plaster); p.Rect(Shop, 8, 11, 17, 8, ink);
            p.Rect(Shop, 4, 5, 3, 15, wood); p.Rect(Shop, 27, 5, 3, 15, wood); p.Rect(Shop, 3, 7, 28, 4, woodLight);
            p.Poly(Shop, new[] { 2, 20, 7, 28, 25, 28, 31, 20 }, PixelArt.Hex("bf705a"));
            for (int x = 5; x < 29; x += 8) p.Poly(Shop, new[] { x - 2, 20, x + 1, 27, x + 4, 27, x + 4, 20 }, plaster);
            for (int x = 3; x < 30; x += 4) p.Rect(Shop, x, 18, 4, 3, (x % 8 == 3) ? plaster : PixelArt.Hex("a95d4b"));
            // School: broad window, green roof and an open-book sign, no phantom students.
            p.Ellipse(School, 16, 4, 15, 4, shadow); p.Rect(School, 4, 5, 26, 18, plaster); p.Rect(School, 4, 5, 26, 3, stone);
            p.Poly(School, new[] { 1, 22, 10, 30, 25, 30, 32, 22 }, PixelArt.Hex("526f62")); p.Line(School, 3, 23, 30, 23, PixelArt.Hex("94ac82"));
            p.Rect(School, 20, 5, 6, 13, wood); Window(p, School, 7, 11, 10, 7, blue);
            p.Rect(School, 11, 23, 5, 5, straw); p.Rect(School, 17, 23, 5, 5, plaster); p.Line(School, 16, 22, 16, 28, wood);
            // Fences and gate variants occupy one real cell rather than an oversized building.
            for (int x = 1; x < 32; x += 5) { p.Rect(WallHorizontal, x, 6, 4, 15, wood); p.Poly(WallHorizontal, new[] { x, 21, x + 2, 26, x + 4, 21 }, woodLight); p.Line(WallHorizontal, x + 1, 7, x + 1, 21, woodLight); }
            p.Rect(WallHorizontal, 0, 10, 32, 3, PixelArt.Hex("5b4335")); p.Rect(WallHorizontal, 0, 18, 32, 2, PixelArt.Hex("5b4335"));
            for (int y = 0; y < 32; y += 5) { p.Rect(WallVertical, 11, y, 9, 4, wood); p.Rect(WallVertical, 11, y + 2, 9, 2, woodLight); } p.Rect(WallVertical, 10, 0, 2, 32, PixelArt.Hex("5b4335"));
            Gate(p, GateOpen, false, false, wood, woodLight); Gate(p, GateClosed, true, false, wood, woodLight);
            Gate(p, GateVerticalOpen, false, true, wood, woodLight); Gate(p, GateVerticalClosed, true, true, wood, woodLight);
            for (int phase = 0; phase < 3; phase++)
            {
                int s = FishFirst + phase;
                p.Poly(s, new[] { 5, 13, 2, 9 + phase, 2, 21 - phase, 8, 18 }, PixelArt.Hex("6eb1af"));
                p.Ellipse(s, 16, 15, 10, 4, PixelArt.Hex("9ec2b6")); p.Line(s, 9, 17, 21, 17, PixelArt.Hex("d3ded0"));
                p.Poly(s, new[] { 12, 19, 16, 23, 19, 18 }, PixelArt.Hex("729ba4")); p.Dot(s, 23, 16, ink); p.Dot(s, 12, 14, PixelArt.Hex("789ca2"));
            }
            p.Poly(WaterPail, new[] { 7, 20, 25, 20, 23, 7, 9, 7 }, woodLight); p.Rect(WaterPail, 8, 18, 16, 3, PixelArt.Hex("69b2b6"));
            p.Line(WaterPail, 10, 21, 12, 26, stoneLight); p.Line(WaterPail, 12, 26, 21, 26, stoneLight); p.Line(WaterPail, 21, 26, 24, 21, stoneLight);
        }
        private static void Window(Painter p, int s, int x, int y, int w, int h, Color glass)
        { p.Rect(s, x - 1, y - 1, w + 2, h + 2, PixelArt.Ink); p.Rect(s, x, y, w, h, glass); p.Line(s, x + w / 2, y, x + w / 2, y + h - 1, PixelArt.Hex("d5c390")); }
        private static void Gate(Painter p, int s, bool closed, bool vertical, Color wood, Color light)
        {
            if (vertical)
            {
                p.Rect(s, 9, 0, 12, 7, wood); p.Rect(s, 9, 26, 12, 6, wood); p.Rect(s, 10, 1, 3, 6, light); p.Rect(s, 10, 26, 3, 6, light);
                if (closed) { p.Rect(s, 12, 7, 7, 19, wood); p.Line(s, 13, 8, 17, 24, light); }
                else { p.Rect(s, 3, 7, 10, 3, wood); p.Rect(s, 18, 24, 11, 3, wood); }
            }
            else
            {
                p.Rect(s, 1, 6, 6, 22, wood); p.Rect(s, 25, 6, 6, 22, wood); p.Rect(s, 2, 7, 2, 20, light); p.Rect(s, 26, 7, 2, 20, light); p.Rect(s, 4, 26, 24, 4, light);
                if (closed) { p.Rect(s, 7, 7, 18, 18, wood); p.Line(s, 8, 8, 24, 23, light); p.Line(s, 24, 8, 8, 23, light); }
                else { p.Poly(s, new[] { 7, 7, 7, 25, 11, 23, 11, 9 }, wood); p.Poly(s, new[] { 25, 7, 25, 25, 21, 23, 21, 9 }, wood); }
            }
        }
        private sealed class Painter
        {
            private readonly Color32[] pixels; private readonly int width;
            public Painter(Color32[] pixels, int width) { this.pixels = pixels; this.width = width; }
            public void Dot(int s, int x, int y, Color c) { if ((uint)x < 32 && (uint)y < 32) pixels[(s / 8 * 32 + y) * width + s % 8 * 32 + x] = c; }
            public void Rect(int s, int x, int y, int w, int h, Color c) { for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++) Dot(s, xx, yy, c); }
            public void Ellipse(int s, int cx, int cy, int rx, int ry, Color c) { for (int y = -ry; y <= ry; y++) for (int x = -rx; x <= rx; x++) if ((float)x * x / (rx * rx) + (float)y * y / (ry * ry) <= 1) Dot(s, cx + x, cy + y, c); }
            public void Line(int s, int x0, int y0, int x1, int y1, Color c) { int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, e = dx + dy; while (true) { Dot(s, x0, y0, c); if (x0 == x1 && y0 == y1) break; int ee = e * 2; if (ee >= dy) { e += dy; x0 += sx; } if (ee <= dx) { e += dx; y0 += sy; } } }
            public void Poly(int s, int[] xy, Color c) { for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { bool inside = false; for (int a = 0, b = xy.Length - 2; a < xy.Length; b = a, a += 2) if ((xy[a + 1] > y) != (xy[b + 1] > y) && x < (float)(xy[b] - xy[a]) * (y - xy[a + 1]) / (xy[b + 1] - xy[a + 1]) + xy[a]) inside = !inside; if (inside) Dot(s, x, y, c); } }
        }
    }
}
