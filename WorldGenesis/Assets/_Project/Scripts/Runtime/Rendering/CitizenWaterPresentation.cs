using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    // A visual waterline only. Traversal, flood depth and the citizen's position remain authoritative.
    public static class CitizenWaterPresentation
    {
        public struct State
        {
            public float Fraction, SubmergedCutPixels, VerticalOffset;
            public Vector2 SurfacePosition;
            public bool Wet => Fraction > .001f;
            public Vector2 Offset => new Vector2(0, VerticalOffset);
        }

        public static State Resolve(WorldState world, UnitData unit, bool mounted = false)
        {
            State result = default(State);
            if (unit == null) return result;
            float pixel = OriginalCitizenAtlas.RaceScale(unit.Species) / OriginalCitizenAtlas.PixelsPerUnit;
            result.SurfacePosition = new Vector2(unit.X, unit.Y - 2 * pixel);
            if (mounted || !CitizenSprites.IsCivilized(unit.Species) || world == null || world.Cells == null ||
                float.IsNaN(unit.X) || float.IsInfinity(unit.X) || float.IsNaN(unit.Y) || float.IsInfinity(unit.Y)) return result;
            int x = Mathf.FloorToInt(unit.X), y = Mathf.FloorToInt(unit.Y);
            if (!WetCell(world, x, y)) return result;
            TileData tile = world.Cells[world.Index(x, y)];
            // Entering the first quarter of a water cell lowers the body continuously; dry ground
            // and bridge decks never inherit a neighbouring tile's immersion.
            float edge = .25f;
            if (!WetCell(world, x - 1, y)) edge = Mathf.Min(edge, unit.X - x);
            if (!WetCell(world, x + 1, y)) edge = Mathf.Min(edge, x + 1 - unit.X);
            if (!WetCell(world, x, y - 1)) edge = Mathf.Min(edge, unit.Y - y);
            if (!WetCell(world, x, y + 1)) edge = Mathf.Min(edge, y + 1 - unit.Y);
            result.Fraction = Mathf.SmoothStep(0, 1, Mathf.Clamp01(edge / .25f)) * Mathf.Clamp01(tile.WaterDepth / .35f);
            // The 21-pixel child body has a shorter leg section than the 28-pixel adult.
            // Six pixels leave the jacket visible under long rear-view hair at the waistline.
            result.SubmergedCutPixels = (unit.IsChild ? 6 : 11) * result.Fraction;
            result.VerticalOffset = -result.SubmergedCutPixels * pixel;
            return result;
        }

        private static bool WetCell(WorldState world, int x, int y)
        {
            if (!world.InBounds(x, y)) return false;
            int cell = world.Index(x, y);
            if (world.Infrastructure != null && cell < world.Infrastructure.Length && world.Infrastructure[cell].BridgeLevel > 0) return false;
            TileData tile = world.Cells[cell];
            return (tile.Terrain == TerrainType.ShallowWater || tile.Terrain == TerrainType.DeepWater) && tile.WaterDepth > 0;
        }

        // Clip geometry and its texture together: stretching an uncut sprite would preserve the legs.
        public static bool Clip(ref Rect geometry, ref Rect uv, float waterline)
        {
            if (geometry.yMax <= waterline || geometry.height <= 0) return false;
            if (geometry.yMin >= waterline) return true;
            float removed = (waterline - geometry.yMin) / geometry.height;
            uv = new Rect(uv.x, uv.y + uv.height * removed, uv.width, uv.height * (1 - removed));
            geometry = new Rect(geometry.x, waterline, geometry.width, geometry.height * (1 - removed));
            return true;
        }

        public static void DrawWake(QuadBatch batch, WorldState world, UnitData unit, Vector2 surfacePosition, State state)
        {
            if (!state.Wet) return;
            float clock = world.Tick * SimulationEngine.TickSeconds;
            float pulse = Mathf.Sin(clock * 3.4f + unit.Id * .73f);
            float scale = OriginalCitizenAtlas.RaceScale(unit.Species) * (unit.IsChild ? .76f : 1);
            float radius = (.25f + pulse * .018f) * scale;
            Color shadow = new Color(.15f, .46f, .56f, .48f * state.Fraction);
            Color foam = new Color(.70f, .91f, .87f, .73f * state.Fraction);
            // Two open ellipses put the dark rear waterline behind a small bright front crescent.
            Arc(batch, surfacePosition, radius, radius * .27f, .08f, Mathf.PI - .08f, shadow, .036f);
            Arc(batch, surfacePosition + Vector2.down * .022f, radius + .035f, radius * .32f, Mathf.PI + .14f, Mathf.PI * 2 - .14f, foam, .031f);
        }

        private static void Arc(QuadBatch batch, Vector2 center, float rx, float ry, float start, float end, Color color, float thickness)
        {
            Vector2 previous = center + new Vector2(Mathf.Cos(start) * rx, Mathf.Sin(start) * ry);
            for (int i = 1; i <= 7; i++)
            {
                float angle = Mathf.Lerp(start, end, i / 7f);
                Vector2 next = center + new Vector2(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry);
                Vector2 delta = next - previous;
                batch.OrientedSprite((previous + next) * .5f, delta.magnitude + .006f, thickness, Mathf.Atan2(delta.y, delta.x), 12, color);
                previous = next;
            }
        }
    }
}
