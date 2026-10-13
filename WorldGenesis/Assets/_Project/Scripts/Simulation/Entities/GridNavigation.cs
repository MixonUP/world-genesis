using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    /// <summary>Bounded four-neighbour A*: minimizes travel time, with a conservative admissible heuristic.</summary>
    internal sealed class GridNavigation
    {
        private readonly WorldState world;
        private readonly int[] visit, parents, queue;
        private int stamp, heapCount;
        private readonly float[] costs, priorities;
        private readonly int[] heap, positions;
        // A search is synchronous: these surface facts are valid only until that Find ends.
        // No revision/RNG/save dependence, so direct public tile mutations are visible on the next query.
        private readonly int[] surfaceStamp;
        private readonly bool[] walkable;
        private readonly float[] terrainSpeed;
        private readonly int[] components;
        private bool batchActive, componentsReady;
        private int componentSerial, batchFailedExpansions, componentNavigationRevision, componentTerrainRevision;
        private static readonly int[] Dx = { -1, 0, 1, 0 };
        private static readonly int[] Dy = { 0, -1, 0, 1 };
        public GridNavigation(WorldState state)
        {
            world = state;
            visit = new int[state.Cells.Length]; parents = new int[visit.Length]; queue = new int[visit.Length];
            costs = new float[visit.Length]; priorities = new float[visit.Length]; heap = new int[visit.Length]; positions = new int[visit.Length];
            surfaceStamp = new int[visit.Length]; walkable = new bool[visit.Length];
            terrainSpeed = new float[visit.Length]; components = new int[visit.Length];
        }

        public bool Find(int start, int goal, List<int> result, int budget = 4096, bool avoidFire = false)
        {
            result.Clear();
            start = world.BuildingNavigationCell(start); goal = world.BuildingNavigationCell(goal);
            if (start < 0 || goal < 0 || start >= visit.Length || goal >= visit.Length || !world.IsWalkable(goal % world.Width, goal / world.Width)) return false;
            if (start == goal) return true;
            if (DisconnectedInBatch(start, goal)) return false;
            if (++stamp == int.MaxValue) { Array.Clear(visit, 0, visit.Length); Array.Clear(surfaceStamp, 0, surfaceStamp.Length); stamp = 1; }
            heapCount = 0; visit[start] = stamp; costs[start] = 0; parents[start] = -1; positions[start] = -1;
            QueueNode(start, Heuristic(start, goal));
            int expanded = 0, reachedCount = 1; queue[0] = start;
            while (heapCount > 0 && expanded++ < budget)
            {
                int current = PopNode();
                if (current == goal)
                {
                    int cursor = goal;
                    while (cursor != start) { result.Add(cursor); cursor = parents[cursor]; }
                    result.Reverse(); return true;
                }
                int x = current % world.Width, y = current / world.Width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + Dx[d], ny = y + Dy[d];
                    if (!world.InBounds(nx, ny)) continue;
                    int next = world.Index(nx, ny);
                    if (!CanStepDuringSearch(current, next) || (avoidFire && world.Cells[next].Fire > .15f)) continue;
                    float cost = costs[current] + StepCostDuringSearch(current, next);
                    if (visit[next] == stamp && cost >= costs[next] - .000001f) continue;
                    if (visit[next] != stamp) { visit[next] = stamp; positions[next] = -1; queue[reachedCount++] = next; }
                    parents[next] = current; costs[next] = cost; QueueNode(next, cost + Heuristic(next, goal));
                }
            }
            // Exhausting the heap (not the budget) proves the complete reachable component.
            // Reuse only geometry-only failures; avoidFire removes directed destinations from the graph.
            if (batchActive && heapCount == 0 && !avoidFire && world.IsWalkable(start % world.Width, start / world.Width))
            {
                int component = ++componentSerial;
                for (int n = 0; n < reachedCount; n++) components[queue[n]] = component;
            }
            if (batchActive && (batchFailedExpansions += Math.Min(expanded, budget)) >= 4096 && components[start] == 0
                && world.IsWalkable(start % world.Width, start / world.Width)) DiscoverComponent(start);
            return false;
        }
        // Only the engine's synchronous unit pass opts into shared topology. Standalone Find remains
        // fully responsive to direct public data edits. A new pass discards all topology knowledge.
        internal void BeginUnitBatch() { batchActive = true; componentsReady = false; batchFailedExpansions = 0; }
        internal void EndUnitBatch() { batchActive = false; componentsReady = false; }
        private bool DisconnectedInBatch(int start, int goal)
        {
            if (!batchActive) return false;
            if (!componentsReady || componentNavigationRevision != world.NavigationRevision || componentTerrainRevision != world.TerrainRevision)
            {
                Array.Clear(components, 0, components.Length); componentSerial = 0;
                componentNavigationRevision = world.NavigationRevision; componentTerrainRevision = world.TerrainRevision; componentsReady = true;
            }
            // Completed failures supply labels; expensive repeated failures may discover a full component.
            // Between walkable cells the ravine/bridge edge predicate is reciprocal. Stranded starts
            // remain on the original directed search, so escape from a newly flooded tile is preserved.
            if (components[start] == components[goal] || !world.IsWalkable(start % world.Width, start / world.Width)) return false;
            return components[start] != 0 || components[goal] != 0;
        }
        private void DiscoverComponent(int start)
        {
            // Repeated expensive failed searches warrant one bounded-by-map connectivity pass for this
            // component. This never supplies a route or changes the original A* budget/outcome.
            int component = ++componentSerial, head = 0, tail = 1;
            components[start] = component; queue[0] = start;
            while (head < tail)
            {
                int cell = queue[head++], x = cell % world.Width, y = cell / world.Width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + Dx[d], ny = y + Dy[d]; if (!world.InBounds(nx, ny)) continue;
                    int next = world.Index(nx, ny);
                    if (components[next] != 0 || !world.CanStep(cell, next)) continue;
                    components[next] = component; queue[tail++] = next;
                }
            }
        }
        private void ReadSurface(int cell)
        {
            if (surfaceStamp[cell] == stamp) return;
            surfaceStamp[cell] = stamp;
            walkable[cell] = world.IsWalkable(cell % world.Width, cell / world.Width);
            terrainSpeed[cell] = world.GetTerrainSpeed(cell);
        }
        private bool CanStepDuringSearch(int from, int to)
        {
            // Callers enumerate in-bounds four-neighbours, preserving CanStep's adjacency contract.
            ReadSurface(to);
            if (!walkable[to]) return false;
            if (world.Infrastructure != null && (world.Infrastructure[from].BridgeLevel > 0 || world.Infrastructure[to].BridgeLevel > 0)) return true;
            return (world.Cells[from].Terrain != TerrainType.Ravine && world.Cells[to].Terrain != TerrainType.Ravine)
                || Math.Abs(world.Cells[from].Height - world.Cells[to].Height) <= .12f;
        }
        private float StepCostDuringSearch(int from, int to)
        {
            ReadSurface(from); ReadSurface(to);
            // Preserve GetStepCost's exact arithmetic sequence. Caching .5/speed would add
            // an intermediate float-array rounding boundary and can change Mono's A* tie order.
            float a = terrainSpeed[from], b = terrainSpeed[to];
            if (b <= 0) return float.PositiveInfinity;
            float cost = .5f / Math.Max(.12f, a) + .5f / b;
            if (world.Infrastructure == null || (world.Infrastructure[from].BridgeLevel == 0 && world.Infrastructure[to].BridgeLevel == 0))
                cost *= 1 + Math.Max(0, world.Cells[to].Height - world.Cells[from].Height) * 8;
            return cost;
        }
        private float Heuristic(int from, int to)
        { return .5f * (Math.Abs(from % world.Width - to % world.Width) + Math.Abs(from / world.Width - to / world.Width)); }
        private bool Less(int a, int b) { return priorities[a] < priorities[b] || (priorities[a] == priorities[b] && a < b); }
        private void QueueNode(int node, float priority)
        {
            priorities[node] = priority; int p = positions[node];
            if (p < 0) { p = heapCount++; heap[p] = node; }
            while (p > 0)
            {
                int parent = (p - 1) / 2; if (!Less(node, heap[parent])) break;
                heap[p] = heap[parent]; positions[heap[p]] = p; p = parent;
            }
            heap[p] = node; positions[node] = p;
        }
        private int PopNode()
        {
            int first = heap[0], last = heap[--heapCount]; positions[first] = -1;
            if (heapCount == 0) return first;
            int p = 0;
            while (p * 2 + 1 < heapCount)
            {
                int child = p * 2 + 1;
                if (child + 1 < heapCount && Less(heap[child + 1], heap[child])) child++;
                if (!Less(heap[child], last)) break;
                heap[p] = heap[child]; positions[heap[p]] = p; p = child;
            }
            heap[p] = last; positions[last] = p; return first;
        }

        public int Escape(int start, float threatX, float threatY, bool hasThreat, List<int> result)
        {
            result.Clear();
            if (++stamp == int.MaxValue) { Array.Clear(visit, 0, visit.Length); Array.Clear(surfaceStamp, 0, surfaceStamp.Length); stamp = 1; }
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
                    if (!world.CanStep(current, next)) continue;
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
