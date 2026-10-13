using System;
using System.Collections.Generic;
using System.Reflection;
using WorldGenesis.Simulation;
#if UNITY_5_3_OR_NEWER
using NUnit.Framework;
using UnityEngine;
#endif
namespace WorldGenesis.Tests
{
    public static class SimulationPerformanceCorrectnessScenarios
    {
        public static Func<WorldState,WorldState> Clone;
        public static Func<WorldState,string> Snapshot;
        private static void Need(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
        private static readonly Type NavType=typeof(WorldState).Assembly.GetType("WorldGenesis.Simulation.GridNavigation",true);
        private static object Nav(WorldState w) {return Activator.CreateInstance(NavType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{w},null);}
        private static object Call(object nav,string name,params object[] values) {return NavType.GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(nav,values);}
        private static bool Find(object nav,int a,int b,List<int> path,int budget=4096,bool fire=false) {return (bool)Call(nav,"Find",a,b,path,budget,fire);}
        private static WorldState Flat()
        {
            WorldState w=new WorldState{Width=32,Height=32,Seed=417,RngState=417,Cells=new TileData[1024]};
            for(int i=0;i<w.Cells.Length;i++)w.Cells[i]=new TileData{Terrain=TerrainType.Grass,Height=.55f,RouteHeight=.55f,Temperature=.5f,Moisture=.5f,DrainageParent=-1,Flow=1};
            new SimulationEngine(w);return w;
        }
        private static void Wall(WorldState w) {for(int y=0;y<32;y++)w.Cells[w.Index(16,y)].Terrain=TerrainType.Mountain;}
        private static void EqualRoute(bool a,bool b,List<int> expected,List<int> actual,string message)
        {Need(a==b&&expected.Count==actual.Count,message+" outcome/length");for(int i=0;i<expected.Count;i++)Need(expected[i]==actual[i],message+" cell "+i);}
        public static void GeneratedMapsPreserveReferencePathsAndBudgets()
        {
            foreach(WorldPreset preset in Enum.GetValues(typeof(WorldPreset)))
            {
                WorldState w=WorldGenerator.Generate(1618033,48,48,preset);new SimulationEngine(w);object nav=Nav(w);ReferenceNavigation old=new ReferenceNavigation(w);var actual=new List<int>();var expected=new List<int>();
                Call(nav,"BeginUnitBatch");
                for(int i=0;i<180;i++)
                {int a=(i*37+51)%w.Cells.Length,b=(i*139+837)%w.Cells.Length;int budget=i%3==0?64:4096;bool fire=i%2==0;EqualRoute(old.Find(a,b,expected,budget,fire),Find(nav,a,b,actual,budget,fire),expected,actual,preset+" query"+i);}
                Call(nav,"EndUnitBatch");
            }
        }
        public static void DirectChangesBetweenQueriesNeverReuseSurfaceFacts()
        {
            WorldState w=Flat();object nav=Nav(w);var path=new List<int>();int a=w.Index(6,8),b=w.Index(24,8);Need(Find(nav,a,b,path),"Flat route");Wall(w);Need(!Find(nav,a,b,path),"New wall ignored");
            int gap=w.Index(16,8);w.Infrastructure[gap].BridgeLevel=1;Need(Find(nav,a,b,path),"New bridge ignored");w.Infrastructure[gap].BridgeLevel=0;Need(!Find(nav,a,b,path),"Removed bridge cached");
            w.Cells[gap].Terrain=TerrainType.Grass;Need(Find(nav,a,b,path),"Painted gap ignored");
        }
        public static void RevisionInvalidatesProvenDisconnectedComponents()
        {
            WorldState w=Flat();Wall(w);object nav=Nav(w);var path=new List<int>();int a=w.Index(6,8),b=w.Index(24,8);Call(nav,"BeginUnitBatch");Need(!Find(nav,a,b,path),"Wall route");Need(!Find(nav,b,a,path),"Reverse wall route");
            w.Infrastructure[w.Index(16,8)].BridgeLevel=1;w.NavigationRevision++;Need(Find(nav,a,b,path),"Bridge revision did not invalidate");w.Infrastructure[w.Index(16,8)].BridgeLevel=0;w.TerrainRevision++;Need(!Find(nav,a,b,path),"Terrain revision did not invalidate");Call(nav,"EndUnitBatch");
        }
        public static void InsufficientBudgetCannotDeclareDisconnected()
        {
            WorldState w=Flat();object nav=Nav(w);var path=new List<int>();int a=w.Index(1,1),b=w.Index(30,30);Call(nav,"BeginUnitBatch");for(int i=0;i<5;i++)Need(!Find(nav,a,b,path,1),"Small budget unexpectedly reached");Need(Find(nav,a,b,path,4096),"Budget failure poisoned connectivity");Call(nav,"EndUnitBatch");
        }
        public static void FireAvoidanceCannotPoisonGeometryConnectivity()
        {
            WorldState w=Flat();for(int y=0;y<32;y++)w.Cells[w.Index(16,y)].Fire=1;object nav=Nav(w);var path=new List<int>();int a=w.Index(6,8),b=w.Index(24,8);Call(nav,"BeginUnitBatch");for(int i=0;i<12;i++)Need(!Find(nav,a,b,path,4096,true),"Walked through fire");Need(Find(nav,a,b,path,4096,false),"Fire avoidance declared permanent disconnection");Call(nav,"EndUnitBatch");
        }
        public static void StrandedNonwalkableStartStillEscapes()
        {
            WorldState w=Flat();Wall(w);object nav=Nav(w);var path=new List<int>();Call(nav,"BeginUnitBatch");Need(!Find(nav,w.Index(6,8),w.Index(24,8),path),"Wall");Need(Find(nav,w.Index(16,8),w.Index(24,8),path),"Stranded start should step onto safe neighbour");Call(nav,"EndUnitBatch");
        }
        public static void NewBatchSeesUnversionedDirectTileMutation()
        {
            WorldState w=Flat();Wall(w);object nav=Nav(w);var path=new List<int>();int a=w.Index(6,8),b=w.Index(24,8);Call(nav,"BeginUnitBatch");Need(!Find(nav,a,b,path),"Wall");Call(nav,"EndUnitBatch");w.Cells[w.Index(16,8)].Terrain=TerrainType.Grass;Call(nav,"BeginUnitBatch");Need(Find(nav,a,b,path),"New pass reused stale topology");Call(nav,"EndUnitBatch");
        }
        public static void ActualGateAndDemolitionCommandsInvalidateBatch()
        {
            WorldState w=Flat();SimulationEngine e=new SimulationEngine(w);e.Spawn(SpeciesType.Human,7,8,4);Wall(w);int gap=w.Index(16,8);w.Cells[gap].Terrain=TerrainType.Grass;
            BuildingData gate=(BuildingData)typeof(SimulationEngine).GetMethod("AddBuilding",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(e,new object[]{w.Settlements[0],16,8,BuildingType.Gate});Need(gate!=null,"No gate");object nav=Nav(w);var path=new List<int>();int a=w.Index(6,8),b=w.Index(24,8);Call(nav,"BeginUnitBatch");Need(Find(nav,a,b,path),"Open gate");Need(e.SetGate(gate.Id,false),"Close command");Need(!Find(nav,a,b,path),"Closed gate cache");Need(e.SetGate(gate.Id,true),"Open command");Need(Find(nav,a,b,path),"Reopened gate cache");
            Need(e.SetGate(gate.Id,false),"Close again");Need(!Find(nav,a,b,path),"Reclosed gate");gate.Health=0;typeof(SimulationEngine).GetMethod("DestroyInvalidBuildings",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(e,null);Need(Find(nav,a,b,path),"Demolished wall cache");Call(nav,"EndUnitBatch");
        }
        public static void CachesAreAbsentFromSaveAndContinuationIsExact()
        {
            WorldState w=WorldGenerator.Generate(271828,48,48);SimulationEngine e=new SimulationEngine(w);int spawn=-1;for(int i=0;i<w.Cells.Length;i++)if(PlacementRules.IsDryGround(w,i%48,i/48)){spawn=i;break;}Need(spawn>=0,"No land");e.Spawn(SpeciesType.Human,spawn%48,spawn/48,16);for(int i=0;i<80;i++)e.Step(.1f);
            string before=Snapshot(w);object nav=Nav(w);var path=new List<int>();Call(nav,"BeginUnitBatch");for(int i=0;i<30;i++)Find(nav,(i*37)%w.Cells.Length,(i*127+40)%w.Cells.Length,path);Call(nav,"EndUnitBatch");Need(before==Snapshot(w),"Navigation mutated save or RNG");
            WorldState copied=Clone(w);SimulationEngine other=new SimulationEngine(copied);Need(Snapshot(w)==Snapshot(copied),"Constructor changed current save");for(int i=0;i<200;i++){e.Step(.1f);other.Step(.1f);}Need(w.Validate(out string err),err);Need(Snapshot(w)==Snapshot(copied),"Cached versus restored continuation diverged");
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class SimulationPerformanceCorrectnessTests
    {
        [SetUp] public void Serializer(){SimulationPerformanceCorrectnessScenarios.Clone=w=>JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w));SimulationPerformanceCorrectnessScenarios.Snapshot=w=>JsonUtility.ToJson(w);}
        [Test] public void GeneratedMapsPreserveReferencePathsAndBudgets(){SimulationPerformanceCorrectnessScenarios.GeneratedMapsPreserveReferencePathsAndBudgets();}
        [Test] public void DirectChangesBetweenQueriesNeverReuseSurfaceFacts(){SimulationPerformanceCorrectnessScenarios.DirectChangesBetweenQueriesNeverReuseSurfaceFacts();}
        [Test] public void RevisionInvalidatesProvenDisconnectedComponents(){SimulationPerformanceCorrectnessScenarios.RevisionInvalidatesProvenDisconnectedComponents();}
        [Test] public void InsufficientBudgetCannotDeclareDisconnected(){SimulationPerformanceCorrectnessScenarios.InsufficientBudgetCannotDeclareDisconnected();}
        [Test] public void FireAvoidanceCannotPoisonGeometryConnectivity(){SimulationPerformanceCorrectnessScenarios.FireAvoidanceCannotPoisonGeometryConnectivity();}
        [Test] public void StrandedNonwalkableStartStillEscapes(){SimulationPerformanceCorrectnessScenarios.StrandedNonwalkableStartStillEscapes();}
        [Test] public void NewBatchSeesUnversionedDirectTileMutation(){SimulationPerformanceCorrectnessScenarios.NewBatchSeesUnversionedDirectTileMutation();}
        [Test] public void ActualGateAndDemolitionCommandsInvalidateBatch(){SimulationPerformanceCorrectnessScenarios.ActualGateAndDemolitionCommandsInvalidateBatch();}
        [Test] public void CachesAreAbsentFromSaveAndContinuationIsExact(){SimulationPerformanceCorrectnessScenarios.CachesAreAbsentFromSaveAndContinuationIsExact();}
    }
#endif
    /// <summary>Bounded four-neighbour A*: minimizes travel time, with a conservative admissible heuristic.</summary>
    internal sealed class ReferenceNavigation
    {
        private readonly WorldState world;
        private readonly int[] visit, parents, queue;
        private int stamp, heapCount;
        private readonly float[] costs, priorities;
        private readonly int[] heap, positions;
        private static readonly int[] Dx = { -1, 0, 1, 0 };
        private static readonly int[] Dy = { 0, -1, 0, 1 };
        public ReferenceNavigation(WorldState state)
        {
            world = state;
            visit = new int[state.Cells.Length]; parents = new int[visit.Length]; queue = new int[visit.Length];
            costs = new float[visit.Length]; priorities = new float[visit.Length]; heap = new int[visit.Length]; positions = new int[visit.Length];
        }

        public bool Find(int start, int goal, List<int> result, int budget = 4096, bool avoidFire = false)
        {
            result.Clear();
            start = world.BuildingNavigationCell(start); goal = world.BuildingNavigationCell(goal);
            if (start < 0 || goal < 0 || start >= visit.Length || goal >= visit.Length || !world.IsWalkable(goal % world.Width, goal / world.Width)) return false;
            if (start == goal) return true;
            if (++stamp == int.MaxValue) { Array.Clear(visit, 0, visit.Length); stamp = 1; }
            heapCount = 0; visit[start] = stamp; costs[start] = 0; parents[start] = -1; positions[start] = -1;
            QueueNode(start, Heuristic(start, goal));
            int expanded = 0;
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
                    if (!world.CanStep(current, next) || (avoidFire && world.Cells[next].Fire > .15f)) continue;
                    float cost = costs[current] + world.GetStepCost(current, next);
                    if (visit[next] == stamp && cost >= costs[next] - .000001f) continue;
                    if (visit[next] != stamp) { visit[next] = stamp; positions[next] = -1; }
                    parents[next] = current; costs[next] = cost; QueueNode(next, cost + Heuristic(next, goal));
                }
            }
            return false;
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
