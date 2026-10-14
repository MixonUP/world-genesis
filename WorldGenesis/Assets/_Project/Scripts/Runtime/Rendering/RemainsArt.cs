using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    // Original pixel recipes: fallen bones, not recoloured living-character frames.
    public static class RemainsArt
    {
        public const int Frame = 32, Width = 256, Height = 96;
        public enum Shape { Humanoid, Quadruped, Bird, Antlered, Horned, Equine }
        public static Shape ShapeFor(SpeciesType species)
        {
            if ((int)species < 4) return Shape.Humanoid;
            if (species == SpeciesType.Chicken) return Shape.Bird;
            if (species == SpeciesType.Deer || species == SpeciesType.SpiritStag) return Shape.Antlered;
            if (species == SpeciesType.Cow || species == SpeciesType.Goat || species == SpeciesType.Sheep) return Shape.Horned;
            if (species == SpeciesType.Horse || species == SpeciesType.Donkey) return Shape.Equine;
            return Shape.Quadruped;
        }
        public static int Glyph(RemainsData remains) { return (int)ShapeFor(remains.Species) * 4 + remains.Direction; }
        public static Rect Uv(int glyph) { return new Rect((glyph % 8 * Frame + .25f) / Width, (glyph / 8 * Frame + .25f) / Height, 31.5f / Width, 31.5f / Height); }
        public static float Alpha(RemainsData remains) { return Mathf.Clamp01(remains.RemainingSeconds / RemainsRules.FadeSeconds(remains.WaterDeath)) * (remains.WaterDeath ? .65f : 1); }
        public static Vector2 Size(RemainsData remains)
        {
            float size = (int)remains.Species < 4 ? (remains.Species == SpeciesType.Orc ? 1.28f : remains.Species == SpeciesType.Dwarf ? 1.04f : 1.17f) : WildlifeArt.AnimalSize(remains.Species) * .8f;
            if (remains.Child) size *= .68f;
            return new Vector2(size, size * .66f);
        }
        public static Texture2D CreateAtlas(bool readable = false)
        {
            Color32[] atlas = new Color32[Width * Height];
            for (int kind = 0; kind < 6; kind++)
            {
                var p = new Bones();
                if (kind == 0) p.Humanoid(); else if (kind == 2) p.Bird(); else p.Animal((Shape)kind);
                p.Outline();
                for (int direction = 0; direction < 4; direction++)
                {
                    // Corpses lie on the ground: rotating the whole skeleton is intentional.
                    int turns = direction == 2 ? 0 : direction == 3 ? 1 : direction == 1 ? 2 : 3;
                    int glyph = kind * 4 + direction;
                    for (int y = 0; y < Frame; y++) for (int x = 0; x < Frame; x++)
                    {
                        int tx = x, ty = y;
                        for (int t = 0; t < turns; t++) { int old = tx; tx = Frame - 1 - ty; ty = old; }
                        atlas[(glyph / 8 * Frame + ty) * Width + glyph % 8 * Frame + tx] = p.Pixels[y * Frame + x];
                    }
                }
            }
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { name = "Ground skeletons · original pixel recipes", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(atlas); texture.Apply(false, !readable); return texture;
        }
        private sealed class Bones
        {
            public readonly Color32[] Pixels = new Color32[Frame * Frame];
            private static readonly Color32 Bone = PixelArt.Hex("ded3ad"), Light = PixelArt.Hex("f3e7c6"), Shade = PixelArt.Hex("ac9b78"), Ink = PixelArt.Hex("514c40");
            private void Dot(int x, int y, Color32 color) { if (x >= 0 && x < Frame && y >= 0 && y < Frame) Pixels[y * Frame + x] = color; }
            private void Line(int x0, int y0, int x1, int y1, Color32 color)
            {
                int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, error = dx + dy;
                while (true) { Dot(x0, y0, color); if (x0 == x1 && y0 == y1) break; int twice = 2 * error; if (twice >= dy) { error += dy; x0 += sx; } if (twice <= dx) { error += dx; y0 += sy; } }
            }
            private void Limb(int x0, int y0, int x1, int y1, int x2, int y2)
            { Line(x0, y0, x1, y1, Bone); Line(x1, y1, x2, y2, Light); Dot(x1 + 1, y1, Shade); Dot(x2 + 1, y2, Bone); }
            private void Skull(int x, int y, bool longFace)
            {
                for (int iy = -2; iy <= 2; iy++) for (int ix = -2; ix <= 2; ix++) if (ix * ix + iy * iy <= 7) Dot(x + ix, y + iy, iy > 0 ? Light : Bone);
                Dot(x + 1, y + 1, Ink); Dot(x + 1, y - 1, Ink);
                Line(x + 2, y, x + (longFace ? 5 : 3), y, Shade); Dot(x + (longFace ? 4 : 2), y - 1, Light);
            }
            private void Ribs(int start, int end, int y)
            {
                Line(start - 2, y, end + 1, y, Light);
                for (int x = start; x <= end; x += 2) { Line(x, y + 1, x + 1, y + 3, Bone); Dot(x + 2, y + 3, Light); Line(x, y - 1, x + 1, y - 3, Shade); Dot(x + 2, y - 3, Bone); }
            }
            public void Humanoid()
            {
                Ribs(15, 19, 16); Skull(24, 16, false);
                Limb(20, 18, 18, 22, 22, 24); Limb(20, 14, 22, 10, 19, 8);
                Line(12, 13, 12, 19, Bone); Dot(13, 13, Shade); Dot(13, 19, Light);
                Limb(12, 18, 7, 20, 3, 17); Limb(12, 14, 8, 11, 3, 12);
                Dot(5, 24, Bone); Dot(7, 6, Shade);
            }
            public void Animal(Shape kind)
            {
                Ribs(11, 19, 16); Skull(23, 17, true);
                Limb(11, 18, 9, 23, 6, 24); Limb(11, 14, 7, 11, 4, 12);
                Limb(20, 18, 20, 23, 24, 25); Limb(20, 14, 23, 10, 26, 10);
                Line(7, 16, 4, 18, Shade); Dot(3, 19, Bone); Dot(2, 20, Shade);
                if (kind == Shape.Antlered)
                {
                    Line(22, 19, 23, 27, Light); Line(23, 23, 19, 25, Bone); Line(23, 25, 26, 28, Bone);
                    Line(22, 14, 23, 7, Bone); Line(23, 10, 20, 6, Bone); Line(23, 9, 26, 7, Light);
                }
                if (kind == Shape.Horned) { Line(22, 19, 21, 23, Light); Line(21, 23, 24, 24, Bone); Line(22, 14, 21, 10, Bone); Dot(22, 9, Light); }
                if (kind == Shape.Equine) { Line(21, 17, 23, 20, Bone); Skull(24, 20, true); Dot(24, 24, Bone); }
            }
            public void Bird()
            {
                Ribs(14, 18, 16); Skull(22, 17, false); Line(25, 17, 28, 16, Shade);
                Limb(17, 18, 13, 24, 7, 25); Line(13, 23, 7, 22, Bone); Line(12, 24, 8, 27, Shade);
                Limb(17, 14, 13, 9, 7, 6); Line(13, 10, 7, 10, Bone); Line(12, 8, 8, 5, Shade);
                Limb(12, 17, 8, 18, 5, 20); Limb(12, 15, 8, 14, 5, 12);
            }
            public void Outline()
            {
                Color32[] source = (Color32[])Pixels.Clone();
                for (int y = 1; y < Frame - 1; y++) for (int x = 1; x < Frame - 1; x++)
                    if (source[y * Frame + x].a == 0 && (source[y * Frame + x - 1].a > 0 || source[y * Frame + x + 1].a > 0 || source[(y - 1) * Frame + x].a > 0 || source[(y + 1) * Frame + x].a > 0)) Dot(x, y, Ink);
            }
        }
    }
}
