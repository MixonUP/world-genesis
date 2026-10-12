using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public enum WaterKind { None = 0, Ocean = 1, Lake = 2, Stream = 3, River = 4, MountainRiver = 5, Estuary = 6 }

    // Saves use named fields, never raw struct bytes or interop offsets.
#pragma warning disable CS0282
    public partial struct TileData
    {
        public WaterKind WaterKind;
        public float WaterDepth, CurrentStrength;
        public byte ChannelWidth;
    }
#pragma warning restore CS0282

    public sealed partial class WorldState
    {
        public int HydrologyVersion;
        public bool ValidateHydrology(out string error)
        {
            error = null;
            if (HydrologyVersion < 0 || HydrologyVersion > 1) { error = "Unsupported hydrology version."; return false; }
            if (HydrologyVersion == 0) return true;
            if (Cells == null) { error = "Missing hydrology grid."; return false; }
            for (int i = 0; i < Cells.Length; i++)
            {
                TileData c = Cells[i];
                if ((int)c.WaterKind < 0 || (int)c.WaterKind > 6 || float.IsNaN(c.WaterDepth) || float.IsInfinity(c.WaterDepth) || c.WaterDepth < 0 || c.WaterDepth > 12
                    || float.IsNaN(c.CurrentStrength) || float.IsInfinity(c.CurrentStrength) || c.CurrentStrength < 0 || c.CurrentStrength > 1 || c.ChannelWidth > 9
                    || (c.WaterKind == WaterKind.None && (c.WaterDepth != 0 || c.CurrentStrength != 0 || c.ChannelWidth != 0))
                    || (c.WaterKind != WaterKind.None && c.WaterDepth <= 0))
                { error = "Invalid water properties at cell " + i + "."; return false; }
            }
            return true;
        }
    }

    /// <summary>Deterministic generation-time water geometry and game-scale depth/current heuristics, not fluid dynamics.</summary>
    public static class Hydrology
    {
        private static readonly int[] Dx = { -1, 0, 1, 0 }, Dy = { 0, -1, 0, 1 };
        private static bool Wet(TileData c) { return c.Terrain == TerrainType.DeepWater || c.Terrain == TerrainType.ShallowWater; }
        private static float Sea(WorldState w) { return w.Preset == WorldPreset.Archipelago ? .535f : .445f; }
        private static float Threshold(WorldState w) { return Math.Max(45, w.Cells.Length / 170f); }
        private static float Clamp(float value, float low, float high) { return Math.Max(low, Math.Min(high, value)); }
        private static float Slope(WorldState w, int i)
        {
            int p = w.Cells[i].DrainageParent;
            return p < 0 || p >= w.Cells.Length ? 0 : Math.Max(0, w.Cells[i].Height - w.Cells[p].Height);
        }

        /// <summary>Legacy migration never floods, carves, changes old River flags or draws gameplay randomness.</summary>
        public static void Initialize(WorldState w)
        {
            if (w == null) throw new ArgumentNullException(nameof(w));
            if (w.HydrologyVersion != 0) return;
            bool[] ocean = OceanMask(w); float sea = Sea(w), threshold = Threshold(w);
            for (int i = 0; i < w.Cells.Length; i++)
            {
                TileData c = w.Cells[i]; Clear(ref c);
                if (Wet(c))
                {
                    if (c.River) DescribeChannel(w, i, sea, threshold, false, ref c);
                    else if (ocean[i]) DescribeOcean(sea, ref c);
                    else DescribeLake(ref c);
                }
                w.Cells[i] = c;
            }
            w.HydrologyVersion = 1;
        }

        /// <summary>Call after a terrain edit. Updates only this cell's descriptors, never neighbouring geography.</summary>
        public static void RefreshCell(WorldState w, int index)
        {
            if (w == null || index < 0 || index >= w.Cells.Length) return;
            TileData c = w.Cells[index]; Clear(ref c);
            if (Wet(c))
            {
                if (c.River) DescribeChannel(w, index, Sea(w), Threshold(w), false, ref c);
                else if (c.Height > Sea(w) && c.RouteHeight - c.Height > .015f) DescribeLake(ref c);
                else DescribeOcean(Sea(w), ref c);
            }
            w.Cells[index] = c;
        }

        /// <summary>Run once after Priority-Flood on a new map, before resource placement.</summary>
        public static void Generate(WorldState w, float sea)
        {
            if (w == null) throw new ArgumentNullException(nameof(w));
            if (w.HydrologyVersion == 1) return;
            int count = w.Cells.Length, dryCount = 0;
            bool[] originalWater = new bool[count];
            for (int i = 0; i < count; i++)
            {
                originalWater[i] = Wet(w.Cells[i]); if (!originalWater[i]) dryCount++;
                TileData c = w.Cells[i]; Clear(ref c); c.River = false;
                if (originalWater[i]) DescribeOcean(sea, ref c);
                w.Cells[i] = c;
            }
            int[] queue = new int[count], seen = new int[count];
            int lakeBudget = Math.Max(8, dryCount / 6), lakeMaximum = Math.Max(8, dryCount / 8);
            // A filled depression is a connected component at a single spill surface.
            for (int start = 0; start < count; start++)
            {
                if (seen[start] != 0 || originalWater[start] || w.Cells[start].RouteHeight - w.Cells[start].Height <= .008f) continue;
                int head = 0, tail = 1; queue[0] = start; seen[start] = 1;
                float level = w.Cells[start].RouteHeight, deepest = 0;
                while (head < tail)
                {
                    int i = queue[head++]; deepest = Math.Max(deepest, level - w.Cells[i].Height);
                    int x = i % w.Width, y = i / w.Width;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + Dx[d], ny = y + Dy[d]; if (!w.InBounds(nx, ny)) continue; int n = w.Index(nx, ny);
                        if (seen[n] != 0 || originalWater[n] || Math.Abs(w.Cells[n].RouteHeight - level) > .00001f || level - w.Cells[n].Height <= .008f) continue;
                        seen[n] = 1; queue[tail++] = n;
                    }
                }
                if (tail < 4 || tail > lakeMaximum || tail > lakeBudget || deepest < .035f) continue;
                lakeBudget -= tail;
                for (int k = 0; k < tail; k++)
                {
                    int i = queue[k]; TileData c = w.Cells[i]; c.Terrain = TerrainType.ShallowWater; c.Vegetation = 0; c.Moisture = 1;
                    DescribeLake(ref c); w.Cells[i] = c;
                }
            }
            // Distance from the original ocean is deliberately bounded to four cells for estuaries.
            byte[] coast = new byte[count]; int qh = 0, qt = 0;
            for (int i = 0; i < count; i++) { coast[i] = originalWater[i] ? (byte)0 : (byte)255; if (originalWater[i]) queue[qt++] = i; }
            while (qh < qt)
            {
                int i = queue[qh++]; if (coast[i] >= 4) continue; int x = i % w.Width, y = i / w.Width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + Dx[d], ny = y + Dy[d]; if (!w.InBounds(nx, ny)) continue; int n = w.Index(nx, ny);
                    if (coast[n] != 255) continue; coast[n] = (byte)(coast[i] + 1); queue[qt++] = n;
                }
            }
            float threshold = Threshold(w); var channels = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (originalWater[i] || w.Cells[i].WaterKind == WaterKind.Lake || w.Cells[i].Flow <= threshold) continue;
                TileData c = w.Cells[i]; DescribeChannel(w, i, sea, threshold, coast[i] <= 3, ref c);
                c.Terrain = TerrainType.ShallowWater; c.River = true; c.Vegetation = 0; c.Moisture = 1; w.Cells[i] = c; channels.Add(i);
            }
            // High discharge gets first claim on banks; ties have a stable grid-index order.
            channels.Sort((a,b) => { int cmp = w.Cells[b].Flow.CompareTo(w.Cells[a].Flow); return cmp != 0 ? cmp : a.CompareTo(b); });
            int wideningBudget = Math.Max(4, dryCount / 12), stamp = 1;
            foreach (int center in channels)
            {
                TileData source = w.Cells[center]; int radius = source.ChannelWidth / 2;
                if (radius == 0 || wideningBudget == 0) continue;
                stamp++; qh = 0; qt = 1; queue[0] = center; seen[center] = stamp;
                int cx = center % w.Width, cy = center / w.Width;
                while (qh < qt)
                {
                    int i = queue[qh++], x = i % w.Width, y = i / w.Width;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + Dx[d], ny = y + Dy[d]; if (!w.InBounds(nx, ny) || Math.Abs(nx-cx)+Math.Abs(ny-cy)>radius) continue;
                        int n = w.Index(nx,ny); TileData bank = w.Cells[n];
                        if (seen[n] == stamp || originalWater[n] || bank.WaterKind == WaterKind.Lake || bank.Height > source.Height + .018f) continue;
                        seen[n] = stamp; queue[qt++] = n;
                        if (bank.WaterKind != WaterKind.None || wideningBudget == 0) continue;
                        float edge = (Math.Abs(nx-cx)+Math.Abs(ny-cy))/(float)(radius+1);
                        bank.WaterKind = source.WaterKind; bank.ChannelWidth = source.ChannelWidth;
                        bank.WaterDepth = Math.Max(.18f, source.WaterDepth * (1 - .55f * edge));
                        bank.CurrentStrength = source.CurrentStrength * (1 - .25f * edge);
                        bank.River = true; bank.Terrain = TerrainType.ShallowWater; bank.Vegetation = 0; bank.Moisture = 1;
                        w.Cells[n] = bank; wideningBudget--;
                    }
                }
            }
            for (int i = 0; i < count; i++)
            {
                if (w.Cells[i].WaterKind == WaterKind.None || w.Cells[i].WaterKind == WaterKind.Ocean) continue;
                int x = i % w.Width, y = i / w.Width;
                for (int d=0;d<4;d++) if(w.InBounds(x+Dx[d],y+Dy[d])) { int n=w.Index(x+Dx[d],y+Dy[d]); w.Cells[n].Moisture=Math.Max(.7f,w.Cells[n].Moisture); }
            }
            w.HydrologyVersion = 1;
        }

        private static void Clear(ref TileData c) { c.WaterKind = WaterKind.None; c.WaterDepth = c.CurrentStrength = 0; c.ChannelWidth = 0; }
        private static void DescribeOcean(float sea, ref TileData c)
        { c.WaterKind = WaterKind.Ocean; c.WaterDepth = Clamp((sea-c.Height)*28, c.Terrain==TerrainType.DeepWater ? 3 : .7f,12); c.CurrentStrength=.08f; c.ChannelWidth=0; }
        private static void DescribeLake(ref TileData c)
        { c.WaterKind=WaterKind.Lake; c.WaterDepth=Clamp((c.RouteHeight-c.Height)*32,.45f,8); c.CurrentStrength=.025f; c.ChannelWidth=0; }
        private static void DescribeChannel(WorldState w, int i, float sea, float threshold, bool mouth, ref TileData c)
        {
            float q = Math.Max(1,c.Flow/threshold), slope = Slope(w,i);
            bool mountain = c.Height > .68f || (c.Height > sea + .10f && slope > .023f);
            c.WaterKind = mouth ? WaterKind.Estuary : mountain ? WaterKind.MountainRiver : q < 2.5f ? WaterKind.Stream : WaterKind.River;
            int radius = q >= 20 ? 3 : q >= 7 ? 2 : q >= 2.5f ? 1 : 0;
            if (mouth) radius = Math.Min(4, radius+1); else if (mountain) radius = Math.Min(1,radius);
            c.ChannelWidth=(byte)(radius*2+1);
            c.WaterDepth=Clamp(.22f+.24f*(float)Math.Sqrt(q)+(mouth?.35f:0),.22f,3.5f);
            c.CurrentStrength=Clamp(.12f+2.6f*(float)Math.Sqrt(slope)+.055f*(float)Math.Log(1+q)+(mountain?.2f:0),.12f,.98f);
            if(mountain&&!mouth)c.CurrentStrength=Math.Max(.66f,c.CurrentStrength);
            if(mouth)c.CurrentStrength*=.6f;
        }
        private static bool[] OceanMask(WorldState w)
        {
            bool[] mask=new bool[w.Cells.Length]; int[] queue=new int[w.Cells.Length]; int head=0,tail=0;
            for(int y=0;y<w.Height;y++)for(int x=0;x<w.Width;x++)
            {
                if(x!=0&&y!=0&&x!=w.Width-1&&y!=w.Height-1)continue; int i=w.Index(x,y);
                if(!Wet(w.Cells[i])||w.Cells[i].River||mask[i])continue; mask[i]=true;queue[tail++]=i;
            }
            while(head<tail)
            {
                int i=queue[head++],x=i%w.Width,y=i/w.Width;
                for(int d=0;d<4;d++)
                {
                    int nx=x+Dx[d],ny=y+Dy[d];if(!w.InBounds(nx,ny))continue;int n=w.Index(nx,ny);
                    if(mask[n]||!Wet(w.Cells[n])||w.Cells[n].River)continue;mask[n]=true;queue[tail++]=n;
                }
            }
            return mask;
        }
    }
}
