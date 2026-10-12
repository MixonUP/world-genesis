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

        public bool Find(int start, int goal, List<int> result, int budget = 4096)
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
    }
}
