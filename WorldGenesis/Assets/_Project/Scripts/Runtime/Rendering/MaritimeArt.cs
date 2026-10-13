using System;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    // Original, deterministic pixel recipes. Hull fittings are painted here; people,
    // provisions and cargo are separate layers derived only from the actual manifest.
    public static class MaritimeArt
    {
        public const int Width = 512, Height = 768, Frame = 64;
        private static readonly Color Ink = PixelArt.Hex("293a42"), Wood = PixelArt.Hex("94704d"), Light = PixelArt.Hex("d6b77e"), Dark = PixelArt.Hex("594936"), Sail = PixelArt.Hex("eee3b7"), Stone = PixelArt.Hex("91a59d");
        private static readonly Color[] Colors = { PixelArt.Hex("aa8b51"), PixelArt.Hex("769b89"), PixelArt.Hex("548b93"), PixelArt.Hex("9b764c"), PixelArt.Hex("497eae"), PixelArt.Hex("ac5147"), PixelArt.Hex("568c65"), PixelArt.Hex("c6974e"), PixelArt.Hex("8972ac") };
        public static bool SupportsBuilding(BuildingType type) { return type >= BuildingType.Port && type <= BuildingType.HarborWarehouse; }
        public static bool IsVisible(ShipData ship) { return ship != null && ship.Health > 0 && ship.Phase != ShipPhase.Abroad && ship.Phase != ShipPhase.Departed && ship.Phase != ShipPhase.Sunk; }
        public static int Direction(ShipData ship) { return (Mathf.RoundToInt(Mathf.Atan2(ship.HeadingY, ship.HeadingX) / (Mathf.PI / 4)) + 8) % 8; }
        public static Vector2 Heading(ShipData ship) { Vector2 v = new Vector2(ship.HeadingX, ship.HeadingY); return v.sqrMagnitude > .001f ? v.normalized : Vector2.up; }
        private static readonly float[] Sizes = { 2.1f, 2.75f, 3.3f, 3.55f, 3.9f, 4.1f, 4.2f, 4.8f, 5.4f };
        public static float ShipSize(ShipKind kind) { return Sizes[Mathf.Clamp((int)kind, 0, Sizes.Length - 1)]; }
        private static Rect Uv(int index) { return new Rect((index % 8 * Frame + .25f) / Width, (index / 8 * Frame + .25f) / Height, 63.5f / Width, 63.5f / Height); }
        public static Rect UvShip(ShipKind kind, int direction) { return Uv((int)kind * 8 + (direction % 8 + 8) % 8); }
        public static Rect UvBuilding(BuildingType type, int level)
        {
            if (!SupportsBuilding(type)) return default(Rect);
            int k = (int)type - (int)BuildingType.Port;
            return Uv(72 + k * 5 + Mathf.Clamp(level, 1, k == 3 ? 3 : 5) - 1);
        }
        public static void DrawBuilding(QuadBatch batch, BuildingData building)
        {
            if (building.Health <= 0 || !SupportsBuilding(building.Type)) return;
            float size = building.Type == BuildingType.Shipyard ? 3.8f : building.Type == BuildingType.HarborWarehouse ? 3.4f : 3.1f;
            batch.SetFootDepth(building.Y + .25f);
            batch.Add(building.X + .5f - size / 2, building.Y - .18f, size, size, UvBuilding(building.Type, building.Level), Color.white);
        }
        public static void DrawJetty(QuadBatch ground, WorldState world, BuildingData building)
        {
            if (building == null || building.Health <= 0 || (building.Type != BuildingType.Port && building.Type != BuildingType.Shipyard) || building.BerthCell < 0 || building.BerthCell >= world.Cells.Length) return;
            int dx = building.BerthCell % world.Width - building.X, dy = building.BerthCell / world.Width - building.Y;
            int length = Math.Abs(dx) + Math.Abs(dy);
            if (length < 1 || length > 2 || (dx != 0 && dy != 0)) return;
            for (int step = 1; step <= length; step++)
                if (!MaritimeCatalog.IsWater(world, world.Index(building.X + Math.Sign(dx) * step, building.Y + Math.Sign(dy) * step), ShipKind.FishingBoat)) return;
            // The dock ends at the actual berth; it does not move the hull or create a land route.
            Vector2 direction = new Vector2(Math.Sign(dx), Math.Sign(dy)), side = new Vector2(-direction.y, direction.x);
            Vector2 origin = new Vector2(building.X + .5f, building.Y + .5f), from = origin + direction * .25f, to = origin + direction * (length - .4f);
            float angle = Mathf.Atan2(direction.y, direction.x), span = Vector2.Distance(from, to);
            ground.OrientedSprite((from + to) * .5f, span + .08f, .46f, angle, 12, Dark);
            int planks = Mathf.Max(2, Mathf.CeilToInt(span / .15f));
            for (int i = 0; i < planks; i++)
            {
                Vector2 plank = Vector2.Lerp(from, to, (i + .5f) / planks);
                ground.OrientedSprite(plank, span / planks - .025f, .38f, angle, 12, i % 3 == 0 ? Light : Wood);
            }
            for (int i = 0; i < 2; i++) for (int edge = -1; edge <= 1; edge += 2)
            {
                Vector2 post = (i == 0 ? from : to) + side * (edge * .23f);
                ground.Sprite(post.x, post.y - .065f, .12f, .13f, 12, Ink);
                ground.Sprite(post.x, post.y - .025f, .075f, .07f, 12, Light);
            }
        }
        public static bool IsShipwrightWorking(WorldState world, UnitData unit)
        {
            if (unit == null || unit.Health <= 0 || unit.IsChild || unit.Task != UnitTask.Shipbuild || unit.PathCursor < unit.Path.Count || SimulationEngine.CarriedLoad(unit) > .001f) return false;
            ShipConstructionData order = world.GetShipOrder(unit.MaritimeOrderId); BuildingData yard = order == null ? null : world.GetBuilding(order.ShipyardId);
            if (order == null || order.WorkerId != unit.Id || order.Complete || order.Cancelled || yard == null || !BuildingAccess.IsUsable(world, yard) || Math.Abs(unit.X - yard.X - .5f) > .4f || Math.Abs(unit.Y - yard.Y - .5f) > .4f) return false;
            foreach (ItemAmount input in MaritimeCatalog.Get(order.Kind).Inputs) if (order.Materials[(int)input.Item] + .00001f < input.Amount) return false;
            return order.Progress > 0 && order.Progress < MaritimeCatalog.Get(order.Kind).Seconds;
        }
        public static void DrawShipyardWork(QuadBatch batch, WorldState world, BuildingData yard)
        {
            if (yard.Type != BuildingType.Shipyard || yard.Health <= 0) return;
            ShipConstructionData order = world.GetShipOrder(yard.MaritimeOrderId);
            if (order == null || order.Complete || order.Cancelled || order.Progress <= 0) return;
            batch.SetFootDepth(yard.Y + .25f, WorldDepth.Front);
            float progress = Mathf.Clamp01(order.Progress / MaritimeCatalog.Get(order.Kind).Seconds);
            Vector2 center = new Vector2(yard.X + .68f, yard.Y + .56f);
            batch.Sprite(center.x, center.y, 1.05f, .065f, 12, Light);
            int ribs = Mathf.CeilToInt(progress * 7);
            for (int i = 0; i < ribs; i++) batch.Sprite(center.x - .40f + i * .13f, center.y - .1f, .045f, .23f + Mathf.Sin(i / 6f * Mathf.PI) * .13f, 12, Wood);
            if (IsShipwrightWorking(world, world.GetUnit(order.WorkerId)))
            {
                float phase = Mathf.Repeat(world.Tick * SimulationEngine.TickSeconds * 1.4f, 1);
                for (int i = 0; i < 3; i++) batch.Sprite(center.x + phase * .3f + i * .06f, center.y + .3f * Mathf.Sin(phase * Mathf.PI) - phase * .12f, .035f, .035f, 12, new Color(.82f, .65f, .38f, 1 - phase));
            }
        }
        public static void DrawShip(QuadBatch batch, ShipData ship)
        {
            if (!IsVisible(ship)) return;
            float size = ShipSize(ship.Kind);
            batch.SetFootDepth(ship.Y);
            batch.Add(ship.X - size * .5f, ship.Y - size * .3125f, size, size, UvShip(ship.Kind, Direction(ship)), ship.Phase == ShipPhase.Stranded ? new Color(.87f, .86f, .76f) : Color.white);
        }
        public static bool HasMovingWake(ShipData ship) { return IsVisible(ship) && ship.Path != null && ship.PathCursor < ship.Path.Count && ship.Phase != ShipPhase.Docked; }
        public static void DrawWake(QuadBatch batch, WorldState world, ShipData ship)
        {
            if (!HasMovingWake(ship)) return;
            Vector2 heading = Heading(ship), side = new Vector2(-heading.y, heading.x), center = new Vector2(ship.X, ship.Y);
            float size = ShipSize(ship.Kind), clock = world.Tick * SimulationEngine.TickSeconds;
            batch.SetFootDepth(ship.Y, WorldDepth.Back);
            for (int n = 0; n < 4; n++)
            {
                float p = Mathf.Repeat(clock * .65f + n * .25f, 1);
                Vector2 at = center - heading * (size * .27f + p * size * .48f);
                Color foam = new Color(.74f, .91f, .86f, .43f * (1 - p));
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector2 point = at + side * sign * (.12f + p * size * .19f);
                    batch.OrientedSprite(point, .10f + p * .18f, .045f, Mathf.Atan2(side.y, side.x) + sign * .25f, 12, foam);
                }
            }
        }
        // A bounded visual sample, never an invented crew count. The inspector reports exact quantities.
        public static int VisibleCrew(WorldState world, ShipData ship)
        {
            int count = 0;
            foreach (int id in ship.CrewIds) { UnitData u = world.GetUnit(id); if (u != null && u.Health > 0 && u.ShipId == ship.Id) count++; }
            return Math.Min(6, count);
        }
        public static void DrawManifest(QuadBatch batch, WorldState world, ShipData ship)
        {
            if (!IsVisible(ship)) return;
            batch.SetFootDepth(ship.Y, WorldDepth.Front);
            float size = ShipSize(ship.Kind), pixel = size / Frame;
            Vector2 heading = Heading(ship), side = new Vector2(-heading.y, heading.x), center = new Vector2(ship.X, ship.Y + pixel * 3);
            float goods = 0; foreach (float v in ship.Cargo) goods += v; foreach (float v in ship.AgriculturalCargo) goods += v;
            int crates = goods > .001f ? Mathf.Clamp(Mathf.CeilToInt(goods / MaritimeCatalog.Get(ship.Kind).Capacity * 6), 1, 4) : 0;
            for (int i = 0; i < crates; i++)
            {
                Vector2 at = center + heading * (i / 2 * .13f * size - .10f * size) + side * ((i % 2 == 0 ? -1 : 1) * size * .09f);
                Block(batch, at, pixel * 5, pixel * 4, Dark); Block(batch, at + Vector2.up * pixel, pixel * 4, pixel * 3, Wood);
                Block(batch, at + Vector2.up * pixel, pixel, pixel * 3, Light);
            }
            if (ship.Food > .001f) Barrel(batch, center - heading * size * .18f - side * size * .09f, pixel, PixelArt.Hex("aa8051"));
            if (ship.Water > .001f) Barrel(batch, center - heading * size * .18f + side * size * .09f, pixel, PixelArt.Hex("629da0"));
            int drawn = 0;
            foreach (int id in ship.CrewIds)
            {
                UnitData unit = world.GetUnit(id); if (unit == null || unit.Health <= 0 || unit.ShipId != ship.Id || drawn >= 6) continue;
                Vector2 at = center + heading * ((drawn / 2) * size * .105f - size * .10f) + side * ((drawn % 2 == 0 ? -1 : 1) * size * .14f);
                Color coat = id == ship.CaptainUnitId ? PixelArt.Gold : PixelArt.Hex("809d99");
                Block(batch, at, pixel * 3, pixel * 4, Ink); Block(batch, at + Vector2.up * pixel, pixel * 2, pixel * 3, coat);
                Block(batch, at + Vector2.up * pixel * 4, pixel * 2, pixel * 2, unit.Species == SpeciesType.Orc ? PixelArt.Hex("899664") : PixelArt.Hex("c6a47c"));
                drawn++;
            }
        }
        public static void DrawAttack(QuadBatch batch, WorldState world, ShipData ship)
        {
            if (!IsVisible(ship) || ship.LastAttackTick <= 0 || world.Tick < ship.LastAttackTick || world.Tick - ship.LastAttackTick > 5) return;
            ShipData target = world.GetShip(ship.TargetShipId); if (!IsVisible(target)) return;
            float t = (world.Tick - ship.LastAttackTick) / 5f;
            Vector2 from = new Vector2(ship.X, ship.Y) + Heading(ship) * ShipSize(ship.Kind) * .25f, to = new Vector2(target.X, target.Y);
            Vector2 at = Vector2.Lerp(from, to, t) + Vector2.up * Mathf.Sin(t * Mathf.PI) * .45f;
            batch.SetFootDepth(Mathf.Min(ship.Y, target.Y) - .5f, WorldDepth.Front);
            Vector2 d = to - from; batch.OrientedSprite(at, .27f, .05f, Mathf.Atan2(d.y, d.x), 12, Light);
        }
        private static void Block(QuadBatch b, Vector2 p, float w, float h, Color c) { b.Sprite(p.x, p.y, w, h, 12, c); }
        private static void Barrel(QuadBatch b, Vector2 p, float pixel, Color c) { Block(b, p, pixel * 4, pixel * 5, Ink); Block(b, p + Vector2.up * pixel, pixel * 3, pixel * 3, c); Block(b, p + Vector2.up * pixel * 2, pixel * 4, pixel, Light); }

        public static Texture2D CreateAtlas(bool keepReadable = false)
        {
            Painter p = new Painter();
            for (int kind = 0; kind < 9; kind++) for (int direction = 0; direction < 8; direction++) { p.Frame(kind * 8 + direction); PaintShip(p, kind, direction); }
            for (int type = 0; type < 4; type++) for (int grade = 1; grade <= (type == 3 ? 3 : 5); grade++) { p.Frame(72 + type * 5 + grade - 1); PaintBuilding(p, type, grade); }
            Texture2D atlas = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { name = "Original maritime hulls and harbors", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            atlas.SetPixels32(p.Pixels); atlas.Apply(false, !keepReadable); return atlas;
        }
        private static void PaintShip(Painter p, int kind, int direction)
        {
            float a = direction * Mathf.PI / 4, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
            float halfLength = kind == 0 ? 19 : 23, halfWidth = kind == 0 ? 6 : kind == 5 ? 8 : 10;
            // Local longitudinal coordinate points at the bow; projection keeps mast vertical.
            for (int y = 1; y < 49; y++) for (int x = 2; x < 62; x++)
            {
                float dx = x - 32, dy = (y - 20) / .62f;
                float along = dx * cs + dy * sn, lateral = -dx * sn + dy * cs;
                float width = halfWidth * (along > halfLength * .45f ? (halfLength - along) / (halfLength * .55f) : along < -halfLength * .65f ? .72f : 1);
                if (along < -halfLength || along > halfLength || Mathf.Abs(lateral) > width) continue;
                float edge = width - Mathf.Abs(lateral);
                Color c = edge < 1.1f || along < -halfLength + 1 ? Ink : edge < 2.8f ? Colors[kind] : edge < 3.8f ? Light : (Mathf.FloorToInt(along + 50) % 5 == 0 ? Dark : Wood);
                p.Dot(x, y, c);
                if (edge < 1.2f && y < 20) p.Dot(x, y - 1, Ink);
            }
            Vector2 stern = Project(-halfLength + 4, 0, cs, sn);
            p.Line((int)stern.x, (int)stern.y, (int)stern.x, (int)stern.y + 3, Light);
            if (kind == 0)
            {
                for (int z = -9; z <= 9; z += 9)
                { Vector2 l = Project(z, -5, cs, sn), r = Project(z, 5, cs, sn); p.Line((int)l.x, (int)l.y, (int)r.x, (int)r.y, Light); }
                Vector2 o = Project(-6, -11, cs, sn), end = Project(5, 10, cs, sn); p.Line((int)o.x, (int)o.y, (int)end.x, (int)end.y, Dark); p.Rect((int)o.x - 1, (int)o.y, 3, 2, Light);
            }
            else
            {
                Vector2 basePoint = Project(kind >= 6 ? 2 : 5, 0, cs, sn); int mx = Mathf.RoundToInt(basePoint.x), my = Mathf.RoundToInt(basePoint.y);
                p.Line(mx - 1, my, mx - 1, my + 27, Ink); p.Line(mx, my, mx, my + 27, Dark);
                int sailWidth = kind == 5 ? 13 : 16;
                for (int yy = 0; yy < 18; yy++)
                {
                    int width = kind == 1 ? (18 - yy) * sailWidth / 18 : sailWidth - Math.Abs(yy - 8) / 4;
                    p.Rect(mx - width / 2, my + 8 + yy, width, 1, yy % 6 == 0 ? Light : Sail);
                    if ((kind == 5 && yy > 3 && yy < 14) || kind >= 6 && yy > 7 && yy < 11) p.Rect(mx - 2, my + 8 + yy, 4, 1, Colors[kind]);
                }
                p.Line(mx - 9, my + 25, mx + 9, my + 25, Dark); p.Rect(mx, my + 28, 7, 3, Colors[kind]); p.Dot(mx + 6, my + 28, Light);
                if (kind >= 7)
                {
                    Vector2 back = Project(-12, 0, cs, sn); int bx = (int)back.x, by = (int)back.y;
                    p.Line(bx, by, bx, by + 20, Dark); p.Rect(bx - 5, by + 9, 11, 10, Sail); p.Rect(bx - 5, by + 15, 11, 2, Colors[kind]);
                }
                if (kind == 2 || kind == 1)
                { Vector2 net = Project(-9, -halfWidth + 3, cs, sn); for (int z = 0; z < 7; z += 2) { p.Line((int)net.x - 3 + z, (int)net.y, (int)net.x - 3 + z, (int)net.y + 5, Stone); p.Line((int)net.x - 3, (int)net.y + z, (int)net.x + 3, (int)net.y + z, Stone); } }
                if (kind == 5)
                { Vector2 bow = Project(16, 0, cs, sn); p.Rect((int)bow.x - 2, (int)bow.y + 1, 5, 4, Dark); p.Line((int)bow.x - 4, (int)bow.y + 3, (int)bow.x + 4, (int)bow.y + 3, Light); }
            }
        }
        private static Vector2 Project(float along, float side, float c, float s) { return new Vector2(32 + along * c - side * s, 20 + (along * s + side * c) * .62f); }
        private static void PaintBuilding(Painter p, int type, int grade)
        {
            Color roof = PixelArt.Hex(type == 1 ? "717a63" : type == 2 ? "517d8a" : type == 3 ? "778894" : "9a7150");
            p.Rect(5, 5, 54, 10, Ink); p.Rect(6, 7, 52, 9, grade > 2 ? Stone : Wood);
            for (int x = 9; x < 59; x += 7) p.Rect(x, 7, 1, 9, Dark);
            if (type == 1)
            {
                // Open slipway and gantry, without a completed hull painted into the workshop.
                p.Rect(8, 16, 12, 24, Dark); p.Rect(10, 17, 8, 22, Wood); p.Rect(10, 40, 41, 4, Light);
                p.Rect(48, 14, 4, 29, Dark); p.Rect(33, 22, 1, 20, Light); p.Rect(30, 21, 7, 2, Ink);
                for (int y = 13; y < 29; y += 5) p.Rect(22, y, 23, 2, Light);
                p.Rect(7, 42, 16, 4 + grade, roof); p.Rect(53, 12, 3, 11, Stone);
            }
            else
            {
                int top = type == 2 ? 42 : type == 3 ? 34 : 31;
                p.Rect(12, 15, 40, top - 14, Ink); p.Rect(14, 16, 36, top - 15, grade > 2 ? Stone : Light);
                p.Rect(16, 17, 3, top - 17, Dark); p.Rect(45, 17, 3, top - 17, Dark);
                p.Rect(27, 15, type == 3 ? 15 : 9, 14, Dark); p.Rect(29, 16, type == 3 ? 11 : 5, 11, Ink);
                for (int side = 0; side < 2; side++) { p.Rect(19 + side * 21, 27, 5, 6, Ink); p.Rect(20 + side * 21, 29, 3, 3, PixelArt.Hex("cbbf88")); }
                for (int y = 0; y < 11; y++) p.Rect(8 + y, top + y, 48 - y * 2, 1, y % 3 == 0 ? Ink : roof);
                p.Rect(18, top + 10, 28, 2, Light);
                if (type == 2) { p.Rect(29, 50, 3, 11, Dark); p.Rect(32, 57, 11, 4, PixelArt.Teal); }
                if (type == 0) { p.Rect(4, 11, 4, 28, Dark); p.Rect(6, 37, 16, 3, Dark); p.Rect(20, 25, 1, 12, Light); p.Rect(18, 23, 5, 2, Stone); }
                if (type == 3) { p.Rect(28, 34, 12, 7, Dark); p.Rect(30, 36, 8, 3, Light); }
            }
            for (int i = 0; i < grade; i++) p.Rect(21 + i * 5, 8, 3, 2, PixelArt.Gold);
            if (grade > 1) { p.Rect(7, 17, 3, 12, Stone); p.Rect(53, 17, 3, 12, Stone); }
            if (grade > 3) { p.Rect(8, 29, 3, 13, Stone); p.Rect(53, 29, 3, 13, Stone); }
        }
        private sealed class Painter
        {
            public readonly Color32[] Pixels = new Color32[Width * Height]; private int ox, oy;
            public void Frame(int index) { ox = index % 8 * 64; oy = index / 8 * 64; }
            public void Dot(int x, int y, Color c) { if (x >= 0 && x < 64 && y >= 0 && y < 64) Pixels[(oy + y) * Width + ox + x] = c; }
            public void Rect(int x, int y, int w, int h, Color c) { for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) Dot(i, j, c); }
            public void Line(int x, int y, int endX, int endY, Color c) { int n = Math.Max(Math.Abs(endX - x), Math.Abs(endY - y)); for (int i = 0; i <= n; i++) Dot(n == 0 ? x : Mathf.RoundToInt(Mathf.Lerp(x, endX, (float)i / n)), n == 0 ? y : Mathf.RoundToInt(Mathf.Lerp(y, endY, (float)i / n)), c); }
        }
    }
}
