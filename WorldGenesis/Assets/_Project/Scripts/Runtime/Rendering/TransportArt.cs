using System;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    // Original pixel recipes. Water geometry and bridge existence always come from WorldState.
    public static class TransportArt
    {
        public const int Size = 128, Frame = 32;
        public const int WoodBridge = 0, StoneBridge = 2, FlowFirst = 4, WagonSide = 8, WagonVertical = 9, VerticalFlowFirst = 12;
        private static readonly Color OceanShallow = PixelArt.Hex("528e9c"), OceanDeep = PixelArt.Hex("24465f");
        private static readonly Color LakeShallow = PixelArt.Hex("659b9b"), LakeDeep = PixelArt.Hex("365e7d");
        private static readonly Color RiverShallow = PixelArt.Hex("78aaa2"), RiverDeep = PixelArt.Hex("3d7289");
        private static readonly Color EstuaryShallow = PixelArt.Hex("799c92");
        private static readonly Color[] Roads = { Color.clear, PixelArt.Hex("9a9169"), PixelArt.Hex("ae9267"), PixelArt.Hex("9d9d8c"), PixelArt.Hex("b5b5a3") };
        public static Rect Uv(int sprite) { return new Rect((sprite % 4 * Frame + .25f) / Size, (sprite / 4 * Frame + .25f) / Size, 31.5f / Size, 31.5f / Size); }
        public static int BridgeSprite(InfrastructureCell cell) { return (cell.BridgeLevel > 1 ? StoneBridge : WoodBridge) + (cell.BridgeAxis == 1 ? 1 : 0); }
        public static Color WaterColor(TileData cell)
        {
            float depth = Mathf.Clamp01(cell.WaterDepth / 6f);
            switch (cell.WaterKind)
            {
                case WaterKind.Ocean: return Color.Lerp(OceanShallow, OceanDeep, depth);
                case WaterKind.Lake: return Color.Lerp(LakeShallow, LakeDeep, depth);
                case WaterKind.Estuary: return Color.Lerp(EstuaryShallow, RiverDeep, depth);
                default: return Color.Lerp(RiverShallow, RiverDeep, depth);
            }
        }
        public static Color RoadColor(int level, int x, int y, uint grain)
        {
            Color color = Roads[Mathf.Clamp(level, 0, 4)];
            if (level == 4 && (y % 3 == 0 || (x + (y / 3) % 2 * 2) % 4 == 0)) color *= .81f;
            else if (level == 3 && grain % 3 == 0) color *= grain % 2 == 0 ? 1.14f : .84f;
            else if (grain % 7 == 0) color *= .93f;
            color.a = 1; return color;
        }
        public static string WaterName(WaterKind kind)
        {
            switch (kind) { case WaterKind.Ocean: return "Океан"; case WaterKind.Lake: return "Озеро"; case WaterKind.Stream: return "Ручей"; case WaterKind.River: return "Река"; case WaterKind.MountainRiver: return "Горная река"; case WaterKind.Estuary: return "Устье реки"; default: return "Суша"; }
        }
        public static string RoadName(int level) { switch (level) { case 1: return "Протоптанная тропа"; case 2: return "Грунтовая дорога"; case 3: return "Гравийная дорога"; case 4: return "Мощёная дорога"; default: return "Нет дороги"; } }
        public static Texture2D CreateAtlas()
        {
            Color32[] pixels = new Color32[Size * Size]; Painter p = new Painter(pixels);
            Color wood = PixelArt.Hex("987448"), light = PixelArt.Hex("d1b680"), ink = PixelArt.Hex("394b49"), stone = PixelArt.Hex("909e9a"), pale = PixelArt.Hex("c6cbb5");
            for (int material = 0; material < 2; material++) for (int axis = 0; axis < 2; axis++)
            {
                int s = material * 2 + axis; p.Transpose = axis == 1;
                // Seamless along the span; cross-planks/stone courses and two side rails.
                p.Rect(s, 0, 4, 32, 24, ink); p.Rect(s, 0, 7, 32, 18, material == 0 ? wood : stone);
                for (int x = 0; x < 32; x += material == 0 ? 5 : 8)
                {
                    p.Rect(s, x, 7, 1, 18, material == 0 ? light : pale);
                    if (material != 0) p.Rect(s, x + 1, 15, 7, 1, ink);
                }
                p.Rect(s, 0, 4, 32, 2, material == 0 ? wood : stone); p.Rect(s, 0, 25, 32, 2, material == 0 ? light : pale);
                for (int x = 3; x < 32; x += 12) { p.Rect(s, x, 3, 2, 5, material == 0 ? light : pale); p.Rect(s, x, 23, 2, 5, material == 0 ? light : pale); }
            }
            for (int vertical = 0; vertical < 2; vertical++) for (int phase = 0; phase < 4; phase++)
            {
                p.Transpose = vertical != 0; int s = (vertical != 0 ? VerticalFlowFirst : FlowFirst) + phase;
                // Four short broken ripples, deliberately quiet on the strategic map.
                p.Rect(s, 4 + phase * 2, 11, 9, 1, Color.white); p.Rect(s, 12 + phase, 18, 7, 1, Color.white);
                p.Rect(s, 20 - phase, 23, 4, 1, Color.white);
            }
            p.Transpose = false;
            // Side-view wagon: a low open bed leaves room for a separately rendered real parcel.
            p.Rect(WagonSide, 3, 10, 24, 12, ink); p.Rect(WagonSide, 4, 12, 22, 9, wood); p.Rect(WagonSide, 5, 17, 20, 4, PixelArt.Hex("5e5748"));
            p.Rect(WagonSide, 3, 21, 24, 2, light); p.Rect(WagonSide, 3, 11, 24, 2, light);
            for (int x = 5; x <= 24; x += 6) p.Rect(WagonSide, x, 13, 2, 8, light);
            for (int x = 8; x <= 23; x += 15)
            {
                p.Ellipse(WagonSide, x, 8, 5, 5, ink); p.Ellipse(WagonSide, x, 8, 3, 3, light); p.Ellipse(WagonSide, x, 8, 2, 2, wood);
                p.Rect(WagonSide, x - 4, 8, 9, 1, ink); p.Rect(WagonSide, x, 4, 1, 9, ink); p.Dot(WagonSide, x, 8, pale);
            }
            // A separate overhead silhouette avoids rotating the side-view wheels into the air.
            p.Rect(WagonVertical, 5, 4, 4, 9, ink); p.Rect(WagonVertical, 23, 4, 4, 9, ink); p.Rect(WagonVertical, 5, 21, 4, 8, ink); p.Rect(WagonVertical, 23, 21, 4, 8, ink);
            p.Rect(WagonVertical, 8, 5, 16, 23, ink); p.Rect(WagonVertical, 10, 7, 12, 19, wood); p.Rect(WagonVertical, 11, 9, 10, 15, PixelArt.Hex("5e5748"));
            p.Rect(WagonVertical, 8, 6, 2, 22, light); p.Rect(WagonVertical, 22, 6, 2, 22, light); p.Rect(WagonVertical, 8, 6, 16, 2, light); p.Rect(WagonVertical, 8, 26, 16, 2, light);
            Texture2D atlas = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "World Genesis • authored water and transport", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            atlas.SetPixels32(pixels); atlas.Apply(false, true); return atlas;
        }
        private sealed class Painter
        {
            private readonly Color32[] pixels; public bool Transpose; public Painter(Color32[] value) { pixels = value; }
            public void Dot(int sprite, int x, int y, Color color) { if ((uint)x >= 32 || (uint)y >= 32) return; if (Transpose) { int swap = x; x = y; y = swap; } pixels[(sprite / 4 * 32 + y) * Size + sprite % 4 * 32 + x] = color; }
            public void Rect(int s, int x, int y, int w, int h, Color c) { for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++) Dot(s, xx, yy, c); }
            public void Ellipse(int s, int cx, int cy, int rx, int ry, Color c) { for (int y = -ry; y <= ry; y++) for (int x = -rx; x <= rx; x++) if ((float)x * x / (rx * rx) + (float)y * y / (ry * ry) <= 1) Dot(s, cx + x, cy + y, c); }
        }
    }
}
