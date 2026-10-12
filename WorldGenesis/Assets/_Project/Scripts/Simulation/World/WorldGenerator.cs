using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    /// <summary>Project-owned seeded value noise, climate heuristic and Priority-Flood routing.</summary>
    public static class WorldGenerator
    {
        public static WorldState Generate(int seed, int width = 128, int height = 128)
        {
            return Generate(seed, width, height, WorldPreset.Island);
        }

        public static WorldState Generate(int seed, int width, int height, WorldPreset preset)
        {
            if (width < 16 || height < 16 || width > 512 || height > 512) throw new ArgumentOutOfRangeException("World dimensions must be between 16 and 512.");
            if ((int)preset < 0 || (int)preset > 2) throw new ArgumentOutOfRangeException(nameof(preset));
            WorldState world = new WorldState { Width = width, Height = height, Seed = seed, Preset = preset, BehaviorVersion = 2, Cells = new TileData[width * height], RngState = StableRandom.Hash(seed, 13, 71, 0xA1B2C3D4u) | 1u };
            float scale = Math.Min(width, height);
            float sea = preset == WorldPreset.Archipelago ? .535f : .445f;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float warpX = Noise(seed + 53, x / scale * 2f, y / scale * 2f) * scale * .055f;
                float warpY = Noise(seed + 139, x / scale * 2f, y / scale * 2f) * scale * .055f;
                float frequency = preset == WorldPreset.Archipelago ? 5.4f : 2.8f;
                float elevation = .5f + .5f * Fbm(seed, (x + warpX) / scale * frequency, (y + warpY) / scale * frequency);
                float ridge = 1f - Math.Abs(Noise(seed + 211, x / scale * 6f, y / scale * 6f));
                float h = .13f + .72f * elevation + .19f * ridge * ridge;
                float u = 2f * x / (width - 1) - 1f;
                float v = 2f * y / (height - 1) - 1f;
                if (preset != WorldPreset.Continent)
                {
                    float t = Clamp01((Math.Max(Math.Abs(u), Math.Abs(v)) - .53f) / .47f);
                    h = Lerp(h, .05f, Fade(t));
                }
                float moisture = Clamp01(.56f + .42f * Fbm(seed + 311, x / scale * 4f, y / scale * 4f));
                float temperature = Clamp01(.92f - .58f * Math.Abs(v) - .7f * Math.Max(h - sea, 0) + .20f * Noise(seed + 401, x / scale * 3f, y / scale * 3f));
                TerrainType terrain;
                if (h < sea - .09f) terrain = TerrainType.DeepWater;
                else if (h < sea) terrain = TerrainType.ShallowWater;
                else if (h < sea + .025f) terrain = TerrainType.Sand;
                else if (h > .72f) terrain = TerrainType.Mountain;
                else if (temperature < .33f) terrain = TerrainType.Snow;
                else if (temperature > .63f && moisture < .51f) terrain = TerrainType.Desert;
                else if (moisture > .54f) terrain = TerrainType.Forest;
                else terrain = TerrainType.Grass;
                float vegetation = terrain == TerrainType.Forest ? .70f + .30f * StableRandom.Coordinate(seed, x, y, 701) : terrain == TerrainType.Grass ? .22f + .35f * StableRandom.Coordinate(seed, x, y, 701) : terrain == TerrainType.Snow ? .08f : 0f;
                world.Cells[world.Index(x, y)] = new TileData { Height = h, Moisture = moisture, Temperature = temperature, Terrain = terrain, Vegetation = vegetation, DrainageParent = -1 };
            }
            RouteDrainage(world, sea);
            ResourceCatalog.Generate(world);
            world.Revision = 1;
            world.TerrainRevision = 1;
            return world;
        }

        /// <summary>Preserves actual elevation; RouteHeight is the filled routing surface, not replacement terrain.</summary>
        private static void RouteDrainage(WorldState world, float sea)
        {
            int count = world.Cells.Length;
            bool[] seen = new bool[count];
            int[] order = new int[count];
            int discovered = 0;
            MinHeap heap = new MinHeap(count);
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                if (x != 0 && y != 0 && x != world.Width - 1 && y != world.Height - 1) continue;
                int i = world.Index(x, y);
                seen[i] = true;
                world.Cells[i].RouteHeight = world.Cells[i].Height;
                world.Cells[i].Flow = 1;
                order[discovered++] = i;
                heap.Push(i, world.Cells[i].RouteHeight);
            }
            int[] dx = { -1, 0, 1, 0 };
            int[] dy = { 0, -1, 0, 1 };
            while (heap.Count > 0)
            {
                int i = heap.Pop();
                int x = i % world.Width, y = i / world.Width;
                for (int direction = 0; direction < 4; direction++)
                {
                    int nx = x + dx[direction], ny = y + dy[direction];
                    if (!world.InBounds(nx, ny)) continue;
                    int n = world.Index(nx, ny);
                    if (seen[n]) continue;
                    seen[n] = true;
                    world.Cells[n].DrainageParent = i;
                    world.Cells[n].RouteHeight = Math.Max(world.Cells[n].Height, world.Cells[i].RouteHeight);
                    world.Cells[n].Flow = 1;
                    order[discovered++] = n;
                    heap.Push(n, world.Cells[n].RouteHeight);
                }
            }
            for (int k = count - 1; k >= 0; k--)
            {
                int i = order[k], parent = world.Cells[i].DrainageParent;
                if (parent >= 0) world.Cells[parent].Flow += world.Cells[i].Flow;
            }
            float threshold = Math.Max(45, count / 170f);
            for (int i = 0; i < count; i++)
            {
                TileData cell = world.Cells[i];
                bool river = cell.Height > sea + .012f && cell.Flow > threshold;
                bool lake = cell.Height > sea && cell.RouteHeight - cell.Height > .055f && cell.Flow > 8;
                if (!river && !lake) continue;
                world.Cells[i].River = river;
                world.Cells[i].Terrain = TerrainType.ShallowWater;
                world.Cells[i].Vegetation = 0;
                world.Cells[i].Moisture = 1;
                int x = i % world.Width, y = i / world.Width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d], ny = y + dy[d];
                    if (world.InBounds(nx, ny)) world.Cells[world.Index(nx, ny)].Moisture = Math.Max(.7f, world.Cells[world.Index(nx, ny)].Moisture);
                }
            }
        }

        public static float Fbm(int seed, float x, float y)
        {
            float total = 0, amplitude = 1, norm = 0;
            for (int octave = 0; octave < 4; octave++)
            {
                total += Noise(unchecked(seed + octave * 1013), x, y) * amplitude;
                norm += amplitude; amplitude *= .5f; x *= 2; y *= 2;
            }
            return total / norm;
        }

        public static float Noise(int seed, float x, float y)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            float tx = Fade(x - ix), ty = Fade(y - iy);
            float a = StableRandom.Coordinate(seed, ix, iy) * 2 - 1;
            float b = StableRandom.Coordinate(seed, ix + 1, iy) * 2 - 1;
            float c = StableRandom.Coordinate(seed, ix, iy + 1) * 2 - 1;
            float d = StableRandom.Coordinate(seed, ix + 1, iy + 1) * 2 - 1;
            return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
        }

        public static float Clamp01(float value) { return Math.Max(0, Math.Min(1, value)); }
        private static float Fade(float t) { return t * t * t * (t * (t * 6 - 15) + 10); }
        private static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        private sealed class MinHeap
        {
            private readonly List<Entry> entries;
            private struct Entry { public int Index; public float Height; }
            public int Count { get { return entries.Count; } }
            public MinHeap(int capacity) { entries = new List<Entry>(capacity); }
            private static bool Less(Entry a, Entry b) { return a.Height < b.Height || (a.Height == b.Height && a.Index < b.Index); }
            public void Push(int index, float height)
            {
                Entry e = new Entry { Index = index, Height = height };
                entries.Add(e);
                int i = entries.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (!Less(e, entries[parent])) break;
                    entries[i] = entries[parent]; i = parent;
                }
                entries[i] = e;
            }
            public int Pop()
            {
                int result = entries[0].Index;
                Entry last = entries[entries.Count - 1];
                entries.RemoveAt(entries.Count - 1);
                if (entries.Count == 0) return result;
                int i = 0;
                while (i * 2 + 1 < entries.Count)
                {
                    int child = i * 2 + 1;
                    if (child + 1 < entries.Count && Less(entries[child + 1], entries[child])) child++;
                    if (!Less(entries[child], last)) break;
                    entries[i] = entries[child]; i = child;
                }
                entries[i] = last;
                return result;
            }
        }
    }
}
