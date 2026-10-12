using System;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    public enum WardrobeAppearance { AdultMale, AdultFemale, Boy, Girl }
    public enum WardrobeFacing { South, West, East, North }

    public readonly struct WardrobeKey
    {
        public readonly bool IsValid;
        public readonly SpeciesType Race;
        public readonly WardrobeAppearance Appearance;
        public readonly UnitProfession Costume;

        public WardrobeKey(SpeciesType race, WardrobeAppearance appearance, UnitProfession costume)
        { IsValid = true; Race = race; Appearance = appearance; Costume = costume; }
    }

    // Original code-authored clothing. No source-body pixels or simulation state are modified.
    // Caller composes back -> body -> front once at atlas startup, with feet at (16,2).
    public static class CitizenWardrobe
    {
        public const int Size = 32;
        private static readonly Color32 Ink = new Color32(47, 44, 44, 255);
        private static readonly Color32 Seam = new Color32(78, 71, 62, 255);
        private static readonly Color32 Linen = new Color32(192, 181, 148, 255);
        private static readonly Color32 Light = new Color32(219, 207, 171, 255);
        private static readonly Color32 Vest = new Color32(80, 98, 86, 255);
        private static readonly Color32 VestLight = new Color32(115, 133, 109, 255);
        private static readonly Color32 Mantle = new Color32(99, 103, 76, 255);
        private static readonly Color32 Coat = new Color32(113, 94, 111, 255);
        private static readonly Color32 Apron = new Color32(143, 105, 77, 255);
        private static readonly Color32 Patch = new Color32(184, 145, 101, 255);

        public static WardrobeKey Resolve(UnitData unit)
        {
            if (unit == null || (int)unit.Species < 0 || (int)unit.Species > 3) return default(WardrobeKey);
            WardrobeAppearance appearance = unit.IsChild
                ? (unit.Sex == Sex.Female ? WardrobeAppearance.Girl : WardrobeAppearance.Boy)
                : (unit.Sex == Sex.Female ? WardrobeAppearance.AdultFemale : WardrobeAppearance.AdultMale);
            return new WardrobeKey(unit.Species, appearance, SafeCostume(appearance, unit.Profession));
        }

        // Output is a complete transparent 32x32 layer, bottom-left origin, never a body edit.
        // No allocations, clock reads, RNG calls, cargo, armour or weapons are made here.
        public static void Paint(Color32[] destination, SpeciesType race, WardrobeAppearance appearance,
            UnitProfession profession, WardrobeFacing facing, int walkFrame, bool frontLayer, bool isRuler = false, int headTop = -1)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (destination.Length != Size * Size) throw new ArgumentException("Wardrobe layer must be exactly 32x32.", nameof(destination));
            Array.Clear(destination, 0, destination.Length);
            if ((int)race < 0 || (int)race > 3 || (int)appearance < 0 || (int)appearance > 3 ||
                (int)facing < 0 || (int)facing > 3) return;

            bool child = appearance == WardrobeAppearance.Boy || appearance == WardrobeAppearance.Girl;
            UnitProfession costume = SafeCostume(appearance, profession);
            // Original bodies already have civilian outfits. Preserve those pixels, including
            // children's clothes, braids and dwarf beards, instead of painting a generic tunic.
            if (child || costume == UnitProfession.None) return;
            bool female = appearance == WardrobeAppearance.AdultFemale || appearance == WardrobeAppearance.Girl;
            bool side = facing == WardrobeFacing.East || facing == WardrobeFacing.West;
            bool north = facing == WardrobeFacing.North;
            int direction = facing == WardrobeFacing.West ? -1 : 1;
            int width = 5;
            if (race == SpeciesType.Elf) width--;
            if (race == SpeciesType.Dwarf || race == SpeciesType.Orc) width++;
            if (side) width = Math.Max(3, width - 2);
            int bottom = 8, top = 16;
            if (race == SpeciesType.Dwarf) top = female ? 15 : 13;
            int sway = (walkFrame & 3) == 1 ? 1 : (walkFrame & 3) == 3 ? -1 : 0;
            if (!frontLayer)
            {
                if (costume == UnitProfession.Hunter || costume == UnitProfession.Merchant)
                {
                    Color32 back = costume == UnitProfession.Hunter ? Mantle : Coat;
                    int end = costume == UnitProfession.Hunter ? bottom - 1 : bottom - 2;
                    for (int y = end; y <= top; y++)
                    {
                        int half = width + (top - y) / 6;
                        int shift = y <= bottom + 1 ? sway : 0;
                        Rect(destination, 16 - half + shift, y, 16 + half + shift, y, Ink);
                        Rect(destination, 17 - half + shift, y, 15 + half + shift, y, back);
                    }
                    // The hood is folded at the nape, below the face and ears.
                    if (costume == UnitProfession.Hunter)
                    { Rect(destination, 12, top, 20, top + 1, Seam); Rect(destination, 14, top + 1, 18, top + 2, Mantle); }
                }
                return;
            }

            switch (costume)
            {
                case UnitProfession.Farmer:
                    if (north) { Rect(destination, 18 - width, top - 3, 14 + width, top - 3, Linen); }
                    else
                    {
                        int apronWidth = Math.Max(2, width - 2);
                        // A narrow bib and stepped hem follow the torso instead of a full-width bar.
                        Rect(destination, 15, bottom + 2, 17, top - 3, Linen);
                        Rect(destination, 16 - apronWidth, bottom + 1, 16 + apronWidth, top - 4, Linen);
                        Rect(destination, 17 - apronWidth, bottom, 15 + apronWidth, bottom, Seam);
                        Pixel(destination, 16 - apronWidth, top - 4, Apron);
                        Pixel(destination, 16 + apronWidth, top - 4, Apron);
                    }
                    Hat(destination, child, race, side, true, headTop);
                    break;
                case UnitProfession.Forester:
                    // Squared cloth shoulders distinguish the vest from a plain tunic.
                    Rect(destination, 15 - width, top - 2, 17 + width, top - 1, Vest);
                    Rect(destination, 16 - width, bottom + 1, 13, top - 1, Vest);
                    Rect(destination, 19, bottom + 1, 16 + width, top - 1, Vest);
                    Rect(destination, 16 - width, top - 1, 16 + width, top - 1, VestLight);
                    if (north) Rect(destination, 16 - width + 1, bottom + 4, 16 + width - 1, bottom + 4, Linen);
                    else for (int y = bottom + 2; y < top; y++)
                        Pixel(destination, 16 + direction * ((y - bottom) / 3 - 1), y, Linen);
                    break;
                case UnitProfession.Hunter:
                    // A short asymmetric shoulder cape; no leather trophy or free spear.
                    Rect(destination, 16 - width, top - 3, 16 + width, top - 1, Mantle);
                    int capeX = north ? 16 - direction * (width - 1) : 16 + direction * (width - 1);
                    Rect(destination, capeX - 1, bottom + 1, capeX + 1, top - 2, Mantle);
                    Rect(destination, 16 - width + 1, bottom + 3, 16 + width - 1, bottom + 3, Seam);
                    break;
                case UnitProfession.Herder:
                    Rect(destination, 16 - width, bottom + 2, 16 + width, bottom + 3, Linen);
                    Rect(destination, 16 - width, top - 2, 16 + width, top - 1, VestLight);
                    if (!north) { Rect(destination, 12, bottom + 4, 13, top - 3, Light); Rect(destination, 19, bottom + 4, 20, top - 3, Light); }
                    Hat(destination, child, race, side, false, headTop);
                    break;
                case UnitProfession.Merchant:
                    Rect(destination, 16 - width, bottom - 1, 16 + width, top - 2, Coat);
                    Rect(destination, 16 - width + 1, bottom + 4, 16 + width - 1, bottom + 4, Linen);
                    if (!north)
                    {
                        Rect(destination, 15, bottom - 1, 17, top - 2, default(Color32));
                        Pixel(destination, 13, top - 2, Light); Pixel(destination, 19, top - 2, Light);
                    }
                    // Decorative tailoring only: no purse, coins or visible cargo.
                    break;
                case UnitProfession.Carpenter:
                    if (north)
                    {
                        Rect(destination, 17 - width, bottom - 1, 15 + width, bottom - 1, Apron);
                        for (int y = bottom + 3; y < top; y++)
                        { Pixel(destination, 12 + (y - bottom) / 2, y, Linen); Pixel(destination, 20 - (y - bottom) / 2, y, Linen); }
                    }
                    else
                    {
                        int apronWidth = Math.Max(2, width - 1);
                        Rect(destination, 16 - apronWidth, bottom - 1, 16 + apronWidth, top - 2, Apron);
                        Rect(destination, 16 - apronWidth, bottom - 1, 16 + apronWidth, bottom - 1, Seam);
                        // Flat fabric patches, not an inventory of tools.
                        Rect(destination, 12, bottom + 2, 14, bottom + 4, Patch);
                        Rect(destination, 18, bottom + 2, 20, bottom + 4, Patch);
                        Pixel(destination, 13, top - 1, Linen); Pixel(destination, 19, top - 1, Linen);
                    }
                    break;
            }

            // Optional office stitch; caller must derive isRuler from the real RulerId.
            // It is neither a crown nor a valuable item, and is never applied to children.
            if (isRuler && !north) { Pixel(destination, 18, top - 2, Light); Pixel(destination, 19, top - 3, Light); }
        }

        private static UnitProfession SafeCostume(WardrobeAppearance appearance, UnitProfession profession)
        {
            if (appearance == WardrobeAppearance.Boy || appearance == WardrobeAppearance.Girl ||
                profession == UnitProfession.Guard || (int)profession < 0 || (int)profession > 7) return UnitProfession.None;
            return profession;
        }

        // Startup-only query on the actual normalized body. A single hair pixel or an
        // outstretched hand must not lift the hat above the connected central head.
        public static int FindHeadTop(Color32[] body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (body.Length != Size * Size) throw new ArgumentException("Citizen body must be exactly 32x32.", nameof(body));
            for (int y = Size - 1; y >= 14; y--)
            {
                int count = 0;
                for (int x = 11; x <= 21; x++) if (body[y * Size + x].a >= 128) count++;
                if (count >= 3) return y;
            }
            return -1;
        }

        private static void Hat(Color32[] pixels, bool child, SpeciesType race, bool side, bool wide, int headTop)
        {
            if (child) return;
            // Overlap the upper two hair rows. A fixed y leaves a gap on shorter source
            // poses. The legacy standalone layer preview keeps its original safe anchor.
            int y = headTop >= 0 ? Math.Max(14, Math.Min(28, headTop - 2)) : race == SpeciesType.Dwarf ? 27 : 28;
            int half = wide ? (side ? 6 : 9) : (side ? 4 : 6);
            Rect(pixels, 16 - half, y, 16 + half, y, Ink);
            Rect(pixels, 17 - half, y + 1, 15 + half, y + 1, wide ? Linen : Vest);
            Rect(pixels, 12, y + 2, 20, y + 2, wide ? Linen : VestLight);
            Rect(pixels, 14, y + 3, 18, y + 3, wide ? Light : VestLight);
        }

        private static void Rect(Color32[] pixels, int x0, int y0, int x1, int y1, Color32 color)
        {
            for (int y = Math.Max(0, y0); y <= Math.Min(Size - 1, y1); y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(Size - 1, x1); x++) pixels[y * Size + x] = color;
        }
        private static void Pixel(Color32[] pixels, int x, int y, Color32 color)
        { if (x >= 0 && y >= 0 && x < Size && y < Size) pixels[y * Size + x] = color; }
    }
}

