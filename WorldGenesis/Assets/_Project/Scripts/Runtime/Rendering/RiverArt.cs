using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    /// <summary>Original water/bank recipes. Reads saved geography; never carves terrain or consumes world RNG.</summary>
    public static class RiverArt
    {
        private static readonly Color Sand = PixelArt.Hex("c0b17c"), WetSand = PixelArt.Hex("89856a");
        private static readonly Color Earth = PixelArt.Hex("76846b"), WetEarth = PixelArt.Hex("4f7165");
        private static readonly Color Gravel = PixelArt.Hex("9daba0"), PaleWater = PixelArt.Hex("a4ccc0");
        private static readonly Color RiverBed = PixelArt.Hex("88b2a0"), Foam = PixelArt.Hex("c9e4d7");

        // One-cell halo, including corners. All samples are in global coordinates: no chunk-local noise.
        public struct Bank
        {
            public int OppositeMask;
            public bool Water;
            public bool Sandy;
            public bool Rocky;
            public bool HasShore => OppositeMask != 0;
        }

        public static bool IsWater(TileData tile) => tile.Terrain == TerrainType.ShallowWater || tile.Terrain == TerrainType.DeepWater;

        public static Bank SampleBank(WorldState world, int x, int y)
        {
            TileData tile = world.Cells[world.Index(x, y)];
            Bank result = new Bank { Water = IsWater(tile), Sandy = tile.Terrain == TerrainType.Sand || tile.Terrain == TerrainType.Desert || tile.WaterKind == WaterKind.Ocean || tile.WaterKind == WaterKind.Estuary,
                Rocky = tile.Terrain == TerrainType.Mountain || tile.Terrain == TerrainType.Highlands || tile.WaterKind == WaterKind.MountainRiver };
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                if ((dx == 0 && dy == 0) || !world.InBounds(x + dx, y + dy)) continue;
                if (IsWater(world.Cells[world.Index(x + dx, y + dy)]) != result.Water)
                    result.OppositeMask |= 1 << ((dy + 1) * 3 + dx + 1);
            }
            return result;
        }

        public static float BankDistance(Bank bank, float px, float py)
        {
            float distance = 2;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                if ((bank.OppositeMask & (1 << ((dy + 1) * 3 + dx + 1))) == 0) continue;
                float ax = Mathf.Max(Mathf.Max(dx - px, 0), px - dx - 1), ay = Mathf.Max(Mathf.Max(dy - py, 0), py - dy - 1);
                distance = Mathf.Min(distance, Mathf.Sqrt(ax * ax + ay * ay));
            }
            return distance;
        }

        public static Color ShadePixel(TileData tile, Bank bank, int globalPixelX, int globalPixelY, int seed, Color ground)
        {
            uint grain = PixelArt.Hash(globalPixelX, globalPixelY, seed);
            float px = (globalPixelX % 8 + .5f) / 8f, py = (globalPixelY % 8 + .5f) / 8f;
            float distance = bank.HasShore ? BankDistance(bank, px, py) : 2;
            // Grain roughens the edge by less than one terrain pixel, without shifting the walkable cells.
            float edge = distance + ((grain >> 10) % 7 - 3f) * .012f;
            if (bank.Water)
            {
                Color water = TransportArt.WaterColor(tile);
                float broad = SmoothGrain(globalPixelX / 24f, globalPixelY / 24f, seed);
                water *= .95f + broad * .09f;
                if (edge < .8f)
                {
                    Color bed = bank.Sandy ? Color.Lerp(RiverBed, Sand, .24f) : bank.Rocky ? Color.Lerp(RiverBed, Gravel, .18f) : RiverBed;
                    float shallows = 1 - Mathf.SmoothStep(0, 1, edge / .8f);
                    water = Color.Lerp(water, bed, shallows * .62f);
                    // A thin translucent water lip, followed by a darker submerged bank shelf.
                    if (edge < .12f) water = Color.Lerp(water, PaleWater, .38f);
                    else if (edge < .25f) water = Color.Lerp(water, WetEarth, .10f);
                    if (edge > .14f && edge < .56f && grain % 61 == 0) water = Color.Lerp(water, Gravel, .30f);
                }
                water.a = 1; return water;
            }
            if (!bank.HasShore || edge > .47f) return ground;
            Color dry = bank.Sandy ? Sand : bank.Rocky ? Gravel : Earth;
            Color wet = bank.Sandy ? WetSand : bank.Rocky ? Color.Lerp(Gravel, WetEarth, .6f) : WetEarth;
            Color shore = edge < .17f ? wet : Color.Lerp(dry, ground, Mathf.SmoothStep(0, 1, (edge - .17f) / .30f));
            if (edge < .34f && grain % (bank.Rocky ? 9u : 23u) == 0) shore = Color.Lerp(Gravel, dry, .25f);
            shore = Color.Lerp(shore, new Color(.9f, .95f, .93f), Mathf.Clamp01(tile.SnowCover) * .7f);
            shore.a = 1; return shore;
        }

        private static float SmoothGrain(float x, float y, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float tx = Mathf.SmoothStep(0, 1, x - ix), ty = Mathf.SmoothStep(0, 1, y - iy);
            float a = PixelArt.Hash(ix, iy, seed) % 1024 / 1023f, b = PixelArt.Hash(ix + 1, iy, seed) % 1024 / 1023f;
            float c = PixelArt.Hash(ix, iy + 1, seed) % 1024 / 1023f, d = PixelArt.Hash(ix + 1, iy + 1, seed) % 1024 / 1023f;
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        public static bool TryDownstream(WorldState world, int index, out Vector2 direction)
        {
            direction = Vector2.zero;
            if (world == null || world.Cells == null || index < 0 || index >= world.Cells.Length) return false;
            TileData tile = world.Cells[index]; int parent = tile.DrainageParent;
            if (!IsWater(tile) || tile.CurrentStrength <= .01f || parent < 0 || parent >= world.Cells.Length || parent == index) return false;
            if (tile.WaterKind == WaterKind.Lake || tile.WaterKind == WaterKind.Ocean) return false;
            int dx = parent % world.Width - index % world.Width, dy = parent / world.Width - index / world.Width;
            // Reject stale or non-local parent links instead of making an invented current over a dry bank.
            if (Mathf.Abs(dx) + Mathf.Abs(dy) != 1 || !IsWater(world.Cells[parent])) return false;
            direction = new Vector2(dx, dy); return true;
        }

        public static void DrawSurface(WorldState world, Camera camera, QuadBatch batch, bool visible)
        {
            batch.Clear();
            if (!visible || camera == null || world == null) { batch.Submit(); return; }
            float height = camera.orthographicSize, width = height * camera.aspect;
            int minX = Mathf.Max(0, Mathf.FloorToInt(camera.transform.position.x - width - 1)), maxX = Mathf.Min(world.Width - 1, Mathf.CeilToInt(camera.transform.position.x + width + 1));
            int minY = Mathf.Max(0, Mathf.FloorToInt(camera.transform.position.y - height - 1)), maxY = Mathf.Min(world.Height - 1, Mathf.CeilToInt(camera.transform.position.y + height + 1));
            float time = world.Tick * SimulationEngine.TickSeconds;
            for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
            {
                int index = world.Index(x, y); TileData tile = world.Cells[index];
                if (!IsWater(tile) || (world.Infrastructure != null && world.Infrastructure[index].BridgeLevel > 0)) continue;
                uint hash = PixelArt.Hash(x, y, world.Seed);
                Vector2 center = new Vector2(x + .5f, y + .5f);
                if (TryDownstream(world, index, out Vector2 direction))
                {
                    // Two independently phased traces make fast water read as a travelling current.
                    if (hash % 3 == 0) continue;
                    Vector2 side = new Vector2(-direction.y, direction.x);
                    float speed = .18f + tile.CurrentStrength * .75f;
                    for (int lane = 0; lane < 2; lane++)
                    {
                        float phase = Mathf.Repeat(time * speed + (hash % 997) / 997f + lane * .53f, 1);
                        float fade = Mathf.Sin(phase * Mathf.PI);
                        float length = .16f + tile.CurrentStrength * .26f;
                        Vector2 p = center + direction * ((phase - .5f) * .58f) + side * (lane == 0 ? -.17f : .16f);
                        Color tint = Foam; tint.a = (.18f + tile.CurrentStrength * .26f) * fade;
                        batch.OrientedSprite(p, length, .022f + tile.CurrentStrength * .013f, Mathf.Atan2(direction.y, direction.x), 12, tint);
                        if (tile.CurrentStrength > .65f)
                        {
                            tint.a *= .65f;
                            batch.OrientedSprite(p - direction * .11f + side * .045f, length * .45f, .021f, Mathf.Atan2(direction.y, direction.x), 12, tint);
                        }
                    }
                }
                else if (hash % 7 == 0)
                {
                    // A lake/sea ripple expands and fades in place; no invented downstream direction.
                    float phase = Mathf.Repeat(time * .21f + hash % 101 / 101f, 1);
                    float spread = .06f + phase * .16f;
                    Color tint = Foam; tint.a = Mathf.Sin(phase * Mathf.PI) * .20f;
                    batch.OrientedSprite(center + Vector2.up * spread, .15f + phase * .18f, .022f, 0, 12, tint);
                    tint.a *= .6f;
                    batch.OrientedSprite(center - Vector2.up * spread, .22f + phase * .2f, .018f, 0, 12, tint);
                }
            }
            batch.Submit();
        }
    }
}
