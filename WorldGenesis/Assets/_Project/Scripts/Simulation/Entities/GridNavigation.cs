using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    /// <summary>Bounded four-neighbour BFS. Paths never cross blocked cells or diagonal corners.</summary>
    internal sealed class GridNavigation
    {
        private readonly WorldState world;
        private readonly int[] visit, parents, queue;
        private int stamp;
        private static readonly int[] Dx = { -1, 0, 1, 0 };
        private static readonly int[] Dy = { 0, -1, 0, 1 };
        public GridNavigation(WorldState state)
        {
            world = state;
            visit = new int[state.Cells.Length]; parents = new int[visit.Length]; queue = new int[visit.Length];
        }

        public bool Find(int start, int goal, List<int> result, int budget = 4096, bool avoidFire = false)
        {
            result.Clear();
            if (start < 0 || goal < 0 || start >= visit.Length || goal >= visit.Length || !world.IsWalkable(goal % world.Width, goal / world.Width)) return false;
            if (start == goal) return true;
            if (++stamp == int.MaxValue) { Array.Clear(visit, 0, visit.Length); stamp = 1; }
            int head = 0, tail = 1;
            queue[0] = start; visit[start] = stamp; parents[start] = -1;
            while (head < tail && head < budget)
            {
                int current = queue[head++];
                int x = current % world.Width, y = current / world.Width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + Dx[d], ny = y + Dy[d];
                    if (!world.IsWalkable(nx, ny)) continue;
                    int next = world.Index(nx, ny);
                    if (avoidFire && world.Cells[next].Fire > .15f) continue;
                    if (visit[next] == stamp) continue;
                    visit[next] = stamp; parents[next] = current; queue[tail++] = next;
                    if (next != goal) continue;
                    while (next != start) { result.Add(next); next = parents[next]; }
                    result.Reverse();
                    return true;
                }
            }
            return false;
        }

        public int Escape(int start, float threatX, float threatY, bool hasThreat, List<int> result)
        {
            result.Clear();
            if (++stamp == int.MaxValue) { Array.Clear(visit, 0, visit.Length); stamp = 1; }
            int sx = start % world.Width, sy = start / world.Width;
            int head = 0, tail = 1, best = start;
            float bestScore = EscapeScore(start, sx, sy, threatX, threatY, hasThreat);
            queue[0] = start; visit[start] = stamp; parents[start] = -1;
            while (head < tail && head < 256)
            {
                int current = queue[head++], x = current % world.Width, y = current / world.Width;
                float score = EscapeScore(current, sx, sy, threatX, threatY, hasThreat);
                if (score > bestScore + .001f) { bestScore = score; best = current; }
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + Dx[d], ny = y + Dy[d];
                    if (Math.Abs(nx - sx) + Math.Abs(ny - sy) > 8 || !world.IsWalkable(nx, ny)) continue;
                    int next = world.Index(nx, ny);
                    if (visit[next] == stamp) continue;
                    visit[next] = stamp; parents[next] = current; queue[tail++] = next;
                }
            }
            int cursor = best;
            while (cursor != start) { result.Add(cursor); cursor = parents[cursor]; }
            result.Reverse();
            return best;
        }

        private float EscapeScore(int index, int originX, int originY, float threatX, float threatY, bool hasThreat)
        {
            int x = index % world.Width, y = index / world.Width;
            float fire = world.Cells[index].Fire * 12;
            for (int d = 0; d < 4; d++) if (world.InBounds(x + Dx[d], y + Dy[d])) fire += world.Cells[world.Index(x + Dx[d], y + Dy[d])].Fire;
            float travel = Math.Abs(x - originX) + Math.Abs(y - originY);
            float dx = x + .5f - threatX, dy = y + .5f - threatY;
            float separation = hasThreat ? Math.Min(8, (float)Math.Sqrt(dx * dx + dy * dy)) : 0;
            return separation - fire - travel * .12f;
        }
    }
}
