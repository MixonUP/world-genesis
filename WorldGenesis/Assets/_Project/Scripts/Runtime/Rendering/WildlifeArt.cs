using System;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    // Independent, project-authored animal/transport atlas; no source game art is copied.
    // Thirteen species: four actual views and a phase-aligned four-step gait.
    public static class WildlifeArt
    {
        public const int Width = 512, Height = 576, Frame = 32, Columns = Width / Frame, FramesPerAnimal = 16, AnimalsFirst = 64;
        public const int FemaleDeerFirst = AnimalsFirst + 13 * FramesPerAnimal, BasketSmall = 48, BasketMedium = 49, BasketLarge = 50;
        public const int SaddleBags = 51, Saddle = 52, FoodBundle = 53, HuntingSpear = 54;
        public enum AnimalDirection { South, West, East, North }
        private static readonly Color Shadow = new Color(.04f, .08f, .11f, .26f);
        private static readonly string[] SpeciesNames = { "Человек", "Эльф", "Гном", "Орк", "Овца", "Волк", "Олень", "Кабан", "Лиса", "Медведь", "Лютоволк", "Дух-олень", "Коза", "Корова", "Курица", "Осёл", "Лошадь" };
        public static string SpeciesName(SpeciesType species) { int i = (int)species; return WildlifeCatalog.IsAnimal(species) ? WildlifeCatalog.Name(species) : i >= 0 && i < SpeciesNames.Length ? SpeciesNames[i] : "Существо"; }
        public static AnimalDirection DirectionFor(Vector2 direction)
        { return Mathf.Abs(direction.y) > Mathf.Abs(direction.x) ? (direction.y > 0 ? AnimalDirection.North : AnimalDirection.South) : (direction.x < 0 ? AnimalDirection.West : AnimalDirection.East); }
        public static int SpriteFor(SpeciesType species, Sex sex, AnimalDirection direction, int phase)
        {
            if (!WildlifeCatalog.IsAnimal(species)) return -1;
            int first = species == SpeciesType.Deer && sex == Sex.Female ? FemaleDeerFirst : AnimalsFirst + ((int)species - 4) * FramesPerAnimal;
            return first + (int)direction * 4 + Mathf.Clamp(phase, 0, 3);
        }
        public static int AnimalSprite(UnitData unit, long tick, Vector2 direction = default(Vector2))
        {
            bool moving = unit.Path != null && unit.PathCursor < unit.Path.Count;
            int tempo = unit.Task == UnitTask.Flee ? 2 : unit.Species == SpeciesType.Bear || unit.Species == SpeciesType.Cow ? 4 : 3;
            return SpriteFor(unit.Species, unit.Sex, DirectionFor(direction), moving ? (int)((tick / tempo + (uint)unit.Id) % 4) : 0);
        }
        public static float AnimalSize(SpeciesType species)
        {
            switch (species) { case SpeciesType.Bear: return 2.6f; case SpeciesType.DireWolf: return 2.15f; case SpeciesType.SpiritStag: return 2.45f; case SpeciesType.Horse: return 1.8f; case SpeciesType.Donkey: return 1.6f; case SpeciesType.Deer: return 2.05f; case SpeciesType.Cow: return 2.25f; case SpeciesType.Boar: return 1.6f; case SpeciesType.Chicken: return .78f; case SpeciesType.Fox: return 1.25f; case SpeciesType.Goat: return 1.5f; case SpeciesType.Wolf: return 1.55f; case SpeciesType.Sheep: return 1.35f; default: return 1.2f; }
        }
        public static Rect Uv(int sprite) { return new Rect((sprite % Columns * Frame + .25f) / Width, (sprite / Columns * Frame + .25f) / Height, 31.5f / Width, 31.5f / Height); }
        public static Texture2D CreateAtlas(bool keepReadable = false)
        {
            Color32[] pixels = new Color32[Width * Height]; Painter p = new Painter(pixels);
            for (int species = 4; species <= 16; species++) for (int sex = 0; sex < (species == (int)SpeciesType.Deer ? 2 : 1); sex++) for (int phase = 0; phase < 4; phase++)
            {
                int family = species == 4 ? 11 : species == 5 ? 12 : species - 6;
                int east = SpriteFor((SpeciesType)species, (Sex)sex, AnimalDirection.East, phase);
                DrawAnimal(p, east, family, phase, (Sex)sex == Sex.Male);
                p.Outline(east); p.Mirror(east, SpriteFor((SpeciesType)species, (Sex)sex, AnimalDirection.West, phase));
                for (int back = 0; back < 2; back++)
                {
                    int frame = SpriteFor((SpeciesType)species, (Sex)sex, back == 0 ? AnimalDirection.South : AnimalDirection.North, phase);
                    DrawEndView(p, frame, family, phase, (Sex)sex == Sex.Male, back != 0); p.Outline(frame);
                }
            }
            Color wood = PixelArt.Hex("9e724a"), light = PixelArt.Hex("d6b378"), leather = PixelArt.Hex("704d39");
            for (int grade = 0; grade < 3; grade++)
            {
                int s = BasketSmall + grade, h = 11 + grade * 3, w = 12 + grade * 3;
                p.Poly(s, new[] { 16 - w / 2, 7 + h, 16 + w / 2, 7 + h, 15 + w / 2, 7, 17 - w / 2, 7 }, wood);
                for (int y = 9; y < 7 + h; y += 3) p.Line(s, 18 - w / 2, y, 14 + w / 2, y, light);
                for (int x = 19 - w / 2; x < 15 + w / 2; x += 4) p.Line(s, x, 8, x - 1, 6 + h, leather);
                p.Rect(s, 16 - w / 2, 6 + h, w, 2, light); p.Line(s, 13, 7, 12, 10 + h, leather); p.Line(s, 20, 7, 19, 10 + h, leather);
                p.Line(s, 12, 10 + h, 19, 10 + h, leather);
                if (grade == 2) { p.Rect(s, 7, 9, 3, 11, leather); p.Rect(s, 22, 9, 3, 11, leather); }
            }
            p.Rect(SaddleBags, 4, 8, 10, 13, leather); p.Rect(SaddleBags, 18, 8, 10, 13, leather); p.Rect(SaddleBags, 7, 20, 18, 3, leather);
            p.Rect(SaddleBags, 5, 16, 8, 4, wood); p.Rect(SaddleBags, 19, 16, 8, 4, wood); p.Rect(SaddleBags, 8, 15, 2, 5, light); p.Rect(SaddleBags, 22, 15, 2, 5, light);
            p.Ellipse(Saddle, 16, 16, 11, 4, leather); p.Rect(Saddle, 8, 17, 4, 6, wood); p.Rect(Saddle, 23, 17, 3, 5, wood); p.Line(Saddle, 12, 16, 12, 7, leather);
            // Un-equipped one-unit food cargo: loose produce in a tied cloth, not a free basket.
            p.Poly(FoodBundle, new[] { 6, 14, 10, 7, 24, 7, 28, 14, 18, 20 }, PixelArt.Hex("799579"));
            p.Ellipse(FoodBundle, 13, 16, 5, 5, PixelArt.Hex("c46b4e")); p.Ellipse(FoodBundle, 21, 17, 4, 5, PixelArt.Hex("d8bd6d")); p.Rect(FoodBundle, 12, 20, 2, 3, PixelArt.Hex("527b59"));
            p.Line(FoodBundle, 8, 12, 25, 12, PixelArt.Hex("bcb68c"));
            p.Line(HuntingSpear, 7, 3, 22, 25, wood); p.Line(HuntingSpear, 8, 3, 23, 25, light);
            p.Poly(HuntingSpear, new[] { 20, 23, 24, 30, 26, 23, 23, 21 }, PixelArt.Hex("b9c1b4")); p.Line(HuntingSpear, 22, 21, 24, 24, leather);
            Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { name = "World Genesis • four-view wildlife and equipment", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels); texture.Apply(false, !keepReadable); return texture;
        }
        private static void DrawAnimal(Painter p, int s, int family, int phase, bool antlers)
        {
            int step = phase == 0 ? -1 : phase == 1 ? 2 : phase == 2 ? 1 : -2, rise = phase == 1 || phase == 3 ? 1 : 0;
            Color ink = PixelArt.Ink;
            p.Ellipse(s, 16, 4, family == 8 ? 7 : 13, 3, Shadow);
            if (family == 11)
            {
                Color wool = PixelArt.Hex("dfd8b8"), woolShade = PixelArt.Hex("aaa990"), face = PixelArt.Hex("786858");
                for (int leg = 0; leg < 4; leg++)
                {
                    bool far = leg % 2 == 0;
                    int swing = far ? step : -step, x = (leg < 2 ? 9 : 21) + swing, foot = 3 + Math.Max(0, swing);
                    Color legColor = far ? Color.Lerp(face, ink, .35f) : face;
                    p.Rect(s, x, foot + 1, 2, 11 - foot, legColor); p.Rect(s, x, foot, 3, 2, ink);
                }
                p.Ellipse(s, 14, 13 + rise, 11, 7, woolShade);
                for (int tuft = 0; tuft < 5; tuft++) p.Ellipse(s, 6 + tuft * 4, 16 + rise + tuft % 2, 4, 4, wool);
                p.Ellipse(s, 15, 12 + rise, 8, 4, wool); p.Ellipse(s, 26, 15 + rise, 4, 6, face);
                p.Rect(s, 23, 20 + rise, 6, 2, wool); p.Rect(s, 24, 18 + rise, 2, 4, face); p.Dot(s, 28, 16 + rise, PixelArt.Ink);
                p.Line(s, 7, 13 + rise, 9, 11 + rise, woolShade); p.Line(s, 15, 14 + rise, 17, 12 + rise, woolShade); return;
            }
            if (family == 8)
            {
                Color feather = PixelArt.Hex("e0d6b4");
                p.Line(s, 13, 8, 12 + step, 4, PixelArt.Hex("cb9d54")); p.Line(s, 20, 8, 20 - step, 4, PixelArt.Hex("cb9d54"));
                p.Ellipse(s, 15, 12 + rise, 8, 6, feather); p.Poly(s, new[] { 8, 11 + rise, 3, 21 + rise, 12, 16 + rise }, PixelArt.Hex("88978d"));
                p.Ellipse(s, 22, 19 + rise, 4, 5, feather); p.Rect(s, 19, 24 + rise, 6, 3, PixelArt.Hex("ad5247")); p.Rect(s, 24, 17 + rise, 5, 2, PixelArt.Hex("d7ac59"));
                p.Dot(s, 24, 20 + rise, ink); p.Ellipse(s, 14, 12 + rise, 5, 3, PixelArt.Hex("b3ac90")); return;
            }
            Color body = BodyColor(family);
            Color highlight = Color.Lerp(body, PixelArt.Hex("ede0b8"), .24f), dark = Color.Lerp(body, ink, .4f);
            bool small = family == 2 || family == 6;
            bool heavy = family == 1 || family == 3 || family == 7;
            int low = heavy ? 7 : 9, top = family == 3 ? 20 : heavy ? 18 : 17;
            int legHeight = low - 3;
            for (int leg = 0; leg < 4; leg++)
            {
                int x = (leg < 2 ? 9 : 22) + (leg % 2 == 0 ? step : -step / 2);
                Color color = leg % 2 == 0 ? dark : body;
                p.Rect(s, x, 4, heavy ? 3 : 2, legHeight + 3, color); p.Rect(s, x, 3, heavy ? 4 : 3, 2, dark);
            }
            p.Ellipse(s, 15, (low + top) / 2 + rise, heavy ? 12 : small ? 10 : 11, (top - low) / 2 + 1, body);
            p.Ellipse(s, 14, (low + top) / 2 + 2 + rise, heavy ? 10 : 8, 2, highlight);
            if (family == 0 || family == 5 || family == 9 || family == 10)
            {
                p.Poly(s, new[] { 20, 10 + rise, 20, 22 + rise, 24, 25 + rise, 27, 22 + rise, 25, 12 + rise }, body);
                p.Ellipse(s, 26, 23 + rise, 5, 3, body); p.Rect(s, 28, 20 + rise, 3, 3, dark); p.Dot(s, 27, 24 + rise, ink);
                p.Rect(s, 22, 26 + rise, 2, family == 9 ? 6 : 3, dark); p.Rect(s, 26, 26 + rise, 2, family == 9 ? 5 : 3, body);
                if (family == 10 || family == 9) { p.Line(s, 22, 26 + rise, 20, 16 + rise, dark); p.Line(s, 5, 15 + rise, 2, 7 + step / 2, dark); p.Line(s, 6, 15 + rise, 3, 6 + step / 2, dark); }
                else { p.Rect(s, 3, 14 + rise, 3, 3, highlight); p.Rect(s, 20, 9 + rise, 3, 8, highlight); }
                if ((family == 0 && antlers) || family == 5)
                {
                    Color horn = family == 5 ? PixelArt.Hex("81b9b0") : PixelArt.Hex("d6bc88");
                    p.Line(s, 23, 26 + rise, 18, 30, horn); p.Line(s, 26, 26 + rise, 27, 31, horn);
                    p.Line(s, 20, 28, 20, 31, horn); p.Line(s, 27, 29, 31, 30, horn);
                }
                if (family == 5) { p.Line(s, 8, 17 + rise, 20, 17 + rise, PixelArt.Hex("83bdb0")); p.Dot(s, 12, 14 + rise, PixelArt.Hex("a6cabc")); }
            }
            else
            {
                int headY = family == 3 ? 15 : 13;
                p.Ellipse(s, 25, headY + rise, family == 3 ? 6 : 5, 4, body); p.Rect(s, 28, headY - 2 + rise, 4, 3, dark); p.Dot(s, 27, headY + 1 + rise, ink);
                p.Rect(s, 22, headY + 4 + rise, 3, family == 3 ? 2 : 4, dark);
                if (family == 1) { p.Rect(s, 29, 12 + rise, 3, 3, PixelArt.Hex("b8957d")); p.Line(s, 27, 10 + rise, 28, 14 + rise, PixelArt.Hex("e5d7ad")); p.Line(s, 9, top + rise, 21, top + 2 + rise, dark); }
                if (family == 2)
                {
                    p.Poly(s, new[] { 7, 12 + rise, 1, 9 + rise, 1, 15 + step / 2, 7, 17 + rise, 12, 13 + rise }, body);
                    p.Rect(s, 1, 10 + rise, 3, 4, PixelArt.Hex("e4d9b8")); p.Rect(s, 22, 10 + rise, 7, 2, PixelArt.Hex("e4d9b8"));
                }
                if (family == 3) { p.Ellipse(s, 11, 17 + rise, 8, 5, body); p.Rect(s, 25, 13 + rise, 5, 3, PixelArt.Hex("a98b67")); }
                if (family == 4) { p.Line(s, 3, 16 + rise, 8, 12 + rise, dark); p.Poly(s, new[] { 10, 17 + rise, 13, 21 + rise, 15, 18 + rise, 18, 21 + rise, 21, 16 + rise }, dark); p.Rect(s, 28, 11 + rise, 3, 1, PixelArt.Hex("d9dabe")); p.Dot(s, 26, 15 + rise, PixelArt.Hex("c5b37a")); }
                if (family == 12) { p.Poly(s, new[] { 8, 15 + rise, 2, 18 + rise, 3, 13 + step / 2, 9, 10 + rise }, dark); p.Poly(s, new[] { 21, 16 + rise, 22, 22 + rise, 26, 17 + rise }, body); p.Rect(s, 25, 10 + rise, 5, 2, highlight); p.Dot(s, 27, 15 + rise, PixelArt.Hex("d7bb7a")); }
                if (family == 6) { p.Line(s, 23, 18 + rise, 20, 24 + rise, dark); p.Line(s, 26, 18 + rise, 24, 24 + rise, dark); p.Rect(s, 26, 7 + rise, 2, 4, highlight); p.Rect(s, 4, 14 + rise, 3, 5, body); }
                if (family == 7) { p.Ellipse(s, 11, 14 + rise, 5, 3, dark); p.Rect(s, 19, 8 + rise, 4, 7, dark); p.Line(s, 23, 18 + rise, 20, 21 + rise, PixelArt.Hex("d4b788")); p.Line(s, 27, 18 + rise, 30, 20 + rise, PixelArt.Hex("d4b788")); p.Line(s, 3, 15 + rise, 2, 7, dark); }
            }
        }
        private static Color BodyColor(int family)
        { return PixelArt.Hex(family == 0 ? "a77e50" : family == 1 ? "705b49" : family == 2 ? "bd784c" : family == 3 ? "695344" : family == 4 ? "465765" : family == 5 ? "d2ddc8" : family == 6 ? "bdb7a0" : family == 7 ? "e0d9bd" : family == 8 ? "ded5b4" : family == 9 ? "92958a" : family == 11 ? "dfd8b8" : family == 12 ? "7e898b" : "a67954"); }
        private static void DrawEndView(Painter p, int s, int family, int phase, bool antlers, bool back)
        {
            int step = phase == 0 ? -1 : phase == 1 ? 2 : phase == 2 ? 1 : -2, rise = phase == 1 || phase == 3 ? 1 : 0;
            Color body = BodyColor(family), ink = PixelArt.Ink, light = Color.Lerp(body, PixelArt.Hex("eee2b8"), .28f), dark = Color.Lerp(body, ink, .4f);
            Color horn = family == 5 ? PixelArt.Hex("82bdb2") : PixelArt.Hex("d6bd90");
            bool heavy = family == 1 || family == 3 || family == 7 || family == 11;
            bool tall = family == 0 || family == 5 || family == 9 || family == 10;
            int width = heavy ? 9 : family == 8 ? 5 : 6, centerY = tall ? 13 : 14;
            p.Ellipse(s, 16, 4, width + 1, 3, Shadow);
            if (family == 8)
            {
                Color beak = PixelArt.Hex("d3a351"), comb = PixelArt.Hex("b6554d");
                p.Line(s, 13, 8, 12, 4 + Math.Max(0, step), beak); p.Line(s, 19, 8, 20, 4 + Math.Max(0, -step), beak);
                p.Ellipse(s, 16, 13 + rise, 6, 8, body); p.Ellipse(s, 12, 13 + rise, 3, 5, dark); p.Ellipse(s, 20, 13 + rise, 3, 5, light);
                if (back) { p.Poly(s, new[] { 12, 13, 11, 25 + rise, 15, 22 + rise, 18, 26 + rise, 21, 14 }, PixelArt.Hex("7e948a")); p.Rect(s, 15, 25 + rise, 3, 3, comb); }
                else { p.Ellipse(s, 16, 22 + rise, 4, 5, body); p.Rect(s, 15, 25 + rise, 3, 4, comb); p.Dot(s, 13, 22 + rise, ink); p.Dot(s, 19, 22 + rise, ink); p.Rect(s, 15, 19 + rise, 3, 2, beak); p.Rect(s, 16, 17 + rise, 2, 2, comb); }
                return;
            }
            // Alternating near/far hooves remain attached to a compact body in both axial views.
            for (int leg = 0; leg < 4; leg++)
            {
                int x = 16 + (leg % 2 == 0 ? -width + 2 : width - 3), foot = 3 + (leg < 2 ? 3 : 0) + Math.Max(0, leg % 2 == 0 ? step : -step);
                p.Rect(s, x, foot, heavy ? 3 : 2, centerY - foot, leg < 2 ? dark : body); p.Rect(s, x, foot, 3, 2, dark);
            }
            p.Ellipse(s, 16, centerY + rise, width, heavy ? 8 : 7, body);
            p.Ellipse(s, 14, centerY + 3 + rise, Math.Max(3, width - 2), 3, light);
            if (family == 11) for (int tuft = 0; tuft < 5; tuft++) p.Ellipse(s, 10 + tuft * 3, 17 + rise + tuft % 2, 3, 4, light);
            if (family == 7) { p.Ellipse(s, 11, 16 + rise, 3, 4, dark); p.Ellipse(s, 21, 12 + rise, 3, 3, dark); }
            if (family == 1 || family == 4) for (int y = 15; y < 22; y += 2) { p.Rect(s, 15, y + rise, 3, 2, dark); p.Dot(s, 14, y + rise, dark); }
            int headY = tall ? 24 : family == 3 ? 20 : 19, headWidth = family == 3 ? 6 : family == 1 || family == 7 ? 5 : 4;
            if (tall) p.Rect(s, 13, 13 + rise, 6, 12, body);
            p.Ellipse(s, 16, headY + rise, headWidth, tall ? 4 : 5, body);
            int ear = family == 9 ? 6 : family == 2 || family == 4 || family == 12 ? 4 : 2;
            p.Line(s, 16 - headWidth, headY + 3 + rise, 15 - headWidth, Math.Min(30, headY + ear + rise + 3), dark);
            p.Line(s, 16 + headWidth, headY + 3 + rise, 17 + headWidth, Math.Min(30, headY + ear + rise + 3), dark);
            if (family == 3) { p.Ellipse(s, 11, 24 + rise, 2, 2, dark); p.Ellipse(s, 21, 24 + rise, 2, 2, dark); }
            if ((family == 0 && antlers) || family == 5)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    p.Line(s, 16 + side * 3, 26 + rise, 16 + side * 8, 30, horn);
                    p.Line(s, 16 + side * 6, 29, 16 + side * 6, 31, horn); p.Line(s, 16 + side * 7, 29, 16 + side * 11, 29, horn);
                }
            }
            if (family == 6 || family == 7) for (int side = -1; side <= 1; side += 2) p.Line(s, 16 + side * 3, headY + 3 + rise, 16 + side * (family == 6 ? 5 : 8), headY + 7 + rise, horn);
            if (back)
            {
                p.Ellipse(s, 16, headY + rise, headWidth - 1, 3, dark);
                if (family == 9 || family == 10) { p.Rect(s, 15, 14 + rise, 2, 12, dark); p.Line(s, 16, 12 + rise, 17 + step / 2, 5, dark); p.Rect(s, 16 + step / 2, 4, 3, 4, dark); }
                else if (family == 2 || family == 4 || family == 12) { p.Ellipse(s, 16 + step / 2, 9 + rise, family == 2 ? 4 : 3, 6, body); p.Ellipse(s, 16 + step / 2, 5 + rise, 2, 2, family == 2 ? light : dark); }
                else { p.Rect(s, 15, 8 + rise, 3, family == 7 ? 7 : 4, dark); if (family == 0 || family == 5) p.Rect(s, 14, 10 + rise, 4, 3, light); }
            }
            else
            {
                p.Ellipse(s, 16, headY - 3 + rise, headWidth - 1, 3, family == 1 ? PixelArt.Hex("af8775") : family == 3 ? PixelArt.Hex("ab906c") : light);
                p.Dot(s, 16 - headWidth + 1, headY + rise, ink); p.Dot(s, 16 + headWidth - 1, headY + rise, ink);
                p.Rect(s, 15, headY - 4 + rise, 3, 2, dark);
                if (family == 1) { p.Line(s, 12, headY - 4 + rise, 11, headY - 1 + rise, horn); p.Line(s, 20, headY - 4 + rise, 21, headY - 1 + rise, horn); }
                if (family == 6) p.Poly(s, new[] { 14, 15 + rise, 18, 15 + rise, 16, 10 + rise }, light);
                if (family == 4) { p.Dot(s, 12, headY + rise, PixelArt.Hex("d4b76f")); p.Dot(s, 20, headY + rise, PixelArt.Hex("d4b76f")); }
            }
            if (family == 5) { p.Dot(s, 12, 16 + rise, horn); p.Dot(s, 20, 14 + rise, horn); p.Line(s, 15, 18 + rise, 17, 18 + rise, horn); }
        }
        private sealed class Painter
        {
            private readonly Color32[] pixels;
            public Painter(Color32[] pixels) { this.pixels = pixels; }
            private int Index(int s, int x, int y) { return (s / Columns * Frame + y) * Width + s % Columns * Frame + x; }
            public void Dot(int s, int x, int y, Color c) { if ((uint)x < 32 && (uint)y < 32) pixels[Index(s, x, y)] = c; }
            public void Mirror(int source, int target) { for (int y = 0; y < Frame; y++) for (int x = 0; x < Frame; x++) pixels[Index(target, x, y)] = pixels[Index(source, Frame - 1 - x, y)]; }
            public void Outline(int sprite)
            {
                Color32[] cell = new Color32[Frame * Frame]; for (int y = 0; y < Frame; y++) for (int x = 0; x < Frame; x++) cell[y * Frame + x] = pixels[Index(sprite, x, y)];
                Color ink = PixelArt.Hex("3e423a");
                for (int y = 0; y < Frame; y++) for (int x = 0; x < Frame; x++)
                {
                    if (cell[y * Frame + x].a >= 200) continue;
                    if (x > 0 && cell[y * Frame + x - 1].a >= 200 || x < Frame - 1 && cell[y * Frame + x + 1].a >= 200 || y > 0 && cell[(y - 1) * Frame + x].a >= 200 || y < Frame - 1 && cell[(y + 1) * Frame + x].a >= 200) Dot(sprite, x, y, ink);
                }
            }
            public void Rect(int s, int x, int y, int w, int h, Color c) { for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++) Dot(s, xx, yy, c); }
            public void Ellipse(int s, int cx, int cy, int rx, int ry, Color c) { for (int y = -ry; y <= ry; y++) for (int x = -rx; x <= rx; x++) if ((float)x * x / (rx * rx) + (float)y * y / (ry * ry) <= 1) Dot(s, cx + x, cy + y, c); }
            public void Line(int s, int x0, int y0, int x1, int y1, Color c) { int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, e = dx + dy; while (true) { Dot(s, x0, y0, c); if (x0 == x1 && y0 == y1) break; int ee = e * 2; if (ee >= dy) { e += dy; x0 += sx; } if (ee <= dx) { e += dx; y0 += sy; } } }
            public void Poly(int s, int[] xy, Color c) { for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { bool inside = false; for (int a = 0, b = xy.Length - 2; a < xy.Length; b = a, a += 2) if ((xy[a + 1] > y) != (xy[b + 1] > y) && x < (float)(xy[b] - xy[a]) * (y - xy[a + 1]) / (xy[b + 1] - xy[a + 1]) + xy[a]) inside = !inside; if (inside) Dot(s, x, y, c); } }
        }
    }
}
