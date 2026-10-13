using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    public static class FishingPresentation
    {
        public struct Rig
        {
            public Vector2 Hand, Wrist, Bend, Tip, LineMiddle, Bobber;
            public float Thickness, Pixel;
            public int Grade;
            public Color Wood, Binding, FloatColor, Skin;
        }

        // Grip pixels measured on the normalized original idle frame (1), not estimated from
        // the outer sprite rectangle. Race, adult sex, then South/West/East/North.
        private static readonly Vector2[] HandPixels = {
            new Vector2(22,11), new Vector2(12,10), new Vector2(19,10), new Vector2(10,10),
            new Vector2(22,11), new Vector2(11,10), new Vector2(19,11), new Vector2(10,11),
            new Vector2(21,10), new Vector2(11,10), new Vector2(20,10), new Vector2(10,11),
            new Vector2(21,10), new Vector2(11,10), new Vector2(20,9), new Vector2(10,11),
            new Vector2(23,11), new Vector2(9,11), new Vector2(23,11), new Vector2(8,11),
            new Vector2(22,11), new Vector2(15,11), new Vector2(20,10), new Vector2(8,11),
            new Vector2(22,11), new Vector2(10,11), new Vector2(22,11), new Vector2(9,11),
            new Vector2(22,11), new Vector2(10,10), new Vector2(21,11), new Vector2(10,11)
        };

        public static bool IsFishing(WorldState world, UnitData unit)
        { return FishingCatalog.IsActivelyFishing(world, unit); }

        // Public geometry is also consumed by tests; the production draw below uses this exact rig.
        public static Rig BuildRig(WorldState world, UnitData unit, Vector2 position, Vector2 facing)
        {
            int grade = Mathf.Clamp(FishingCatalog.RodLevel(unit), 1, 5);
            float clock = world.Tick * SimulationEngine.TickSeconds;
            float phase = clock * 2.1f + unit.Id * .83f;
            CitizenSprites.Pose pose = CitizenSprites.ResolvePose(world, unit, facing);
            float scale = OriginalCitizenAtlas.RaceScale(unit.Species);
            float pixel = scale / OriginalCitizenAtlas.PixelsPerUnit;
            Vector2 anchor = HandPixels[(Mathf.Clamp((int)unit.Species, 0, 3) * 2 + (unit.Sex == Sex.Female ? 1 : 0)) * 4 + (int)pose.Direction];
            Vector2 wrist = position + new Vector2(anchor.x + .5f - 16, anchor.y + .5f - 2) * pixel;
            if (anchor.y >= 11) wrist += pose.UpperOffset;
            Vector2 hand = wrist;
            CitizenWaterPresentation.State waterState = CitizenWaterPresentation.Resolve(world, unit);
            if (waterState.Wet)
            {
                float surface = waterState.SurfacePosition.y + position.y - unit.Y - waterState.VerticalOffset;
                hand.y = Mathf.Max(hand.y, surface + pixel * .7f);
                wrist.y = Mathf.Max(wrist.y, surface);
            }
            int cell = unit.ResourceCell;
            Vector2 water = new Vector2(cell % world.Width + .5f, cell / world.Width + .5f);
            Vector2 direction = water - new Vector2(unit.X, unit.Y);
            if (direction.sqrMagnitude < .001f) direction = facing;
            direction.Normalize();
            float length = (.73f + .035f * grade) * scale;
            Vector2 tip = hand + direction * length + Vector2.up * (.38f * scale + Mathf.Sin(phase) * .028f);
            Vector2 bend = Vector2.Lerp(hand, tip, .56f) + Vector2.up * (.085f + grade * .006f) * scale;
            // Cast further into this resource cell so a north-facing citizen's tall sprite
            // cannot cover the float at the adjacent cell centre. Drift stays inside the tile.
            Vector2 bobber = water + direction * .28f + new Vector2(Mathf.Sin(phase * .63f) * .028f, Mathf.Sin(phase * 1.4f) * .027f);
            Color wood = grade == 1 ? PixelArt.Hex("a68a55") : grade == 2 ? PixelArt.Hex("d5ad66") : grade == 3 ? PixelArt.Hex("776447") : grade == 4 ? PixelArt.Hex("594774") : PixelArt.Hex("375f70");
            Color binding = grade <= 2 ? PixelArt.Hex("e5d0a2") : grade == 3 ? PixelArt.Hex("bdc7c6") : grade == 4 ? PixelArt.Hex("dbb36f") : PixelArt.Hex("e4cc70");
            return new Rig
            {
                Hand = hand, Wrist = wrist, Bend = bend, Tip = tip, Bobber = bobber, Pixel = pixel,
                LineMiddle = Vector2.Lerp(tip, bobber, .5f) + Vector2.down * .055f,
                Grade = grade, Thickness = .035f + grade * .003f,
                Wood = wood, Binding = binding,
                Skin = unit.Species == SpeciesType.Orc ? PixelArt.Hex("99a34d") : unit.Species == SpeciesType.Elf ? PixelArt.Hex("ddab80") : unit.Species == SpeciesType.Dwarf ? PixelArt.Hex("d99161") : PixelArt.Hex("d69970"),
                FloatColor = grade >= 4 ? PixelArt.Hex("cf9dee") : PixelArt.Hex("e78569")
            };
        }

        public static void Draw(QuadBatch batch, WorldState world, UnitData unit, Vector2 presentationPosition, Vector2 facing)
        {
            if (!IsFishing(world, unit)) return;
            Rig rig = BuildRig(world, unit, presentationPosition, facing);
            Color line = new Color(.89f, .92f, .81f, .85f);
            Segment(batch, rig.Tip, rig.LineMiddle, .014f, line);
            Segment(batch, rig.LineMiddle, rig.Bobber, .014f, line);
            Segment(batch, rig.Hand, rig.Bend, rig.Thickness, rig.Wood);
            Segment(batch, rig.Bend, rig.Tip, rig.Thickness * .72f, rig.Wood);
            Vector2 shaft = (rig.Bend - rig.Hand).normalized;
            Segment(batch, rig.Hand - shaft * .035f, rig.Hand + shaft * .12f, .063f, PixelArt.Hex("564836"));
            int bindings = rig.Grade == 1 ? 1 : rig.Grade == 2 ? 2 : 3;
            for (int i = 0; i < bindings; i++)
            {
                Vector2 at = Vector2.Lerp(rig.Hand, rig.Bend, .26f + i * .27f);
                Segment(batch, at - shaft * .018f, at + shaft * .018f, rig.Thickness + .024f, rig.Binding);
            }
            if (rig.Grade >= 3)
            {
                Vector2 reel = rig.Hand + shaft * .16f + new Vector2(shaft.y, -shaft.x) * .065f;
                batch.Sprite(reel.x, reel.y - .052f, .105f, .105f, 12, rig.Binding);
                batch.Sprite(reel.x, reel.y - .029f, .058f, .058f, 12, PixelArt.Hex("534845"));
            }
            if (rig.Grade >= 4)
            {
                Vector2 jewel = Vector2.Lerp(rig.Bend, rig.Tip, .35f);
                batch.OrientedSprite(jewel, .086f, .086f, Mathf.PI * .25f, 12, rig.Grade == 5 ? PixelArt.Hex("82e5d4") : PixelArt.Hex("d3a4ee"));
            }
            // A tiny grip sits over the handle. Wading lifts it just above the waterline,
            // with a short forearm connection instead of a floating fishing pole.
            if ((rig.Hand - rig.Wrist).sqrMagnitude > .00001f) Segment(batch, rig.Wrist, rig.Hand, rig.Pixel * 1.3f, rig.Skin);
            batch.Sprite(rig.Hand.x, rig.Hand.y - rig.Pixel * .65f, rig.Pixel * 1.5f, rig.Pixel * 1.5f, 12, rig.Skin);
            batch.Sprite(rig.Bobber.x, rig.Bobber.y - .016f, .15f, .025f, 12, new Color(.75f, .96f, .95f, .45f));
            batch.Sprite(rig.Bobber.x, rig.Bobber.y, .062f, .095f, 12, rig.FloatColor);
            batch.Sprite(rig.Bobber.x, rig.Bobber.y + .052f, .062f, .043f, 12, PixelArt.Hex("f2e8c7"));
        }

        private static void Segment(QuadBatch batch, Vector2 from, Vector2 to, float thickness, Color color)
        {
            Vector2 delta = to - from;
            batch.OrientedSprite((from + to) * .5f, delta.magnitude, thickness, Mathf.Atan2(delta.y, delta.x), 12, color);
        }
    }
}
