using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    [Serializable]
    public sealed class TransportProjectData
    {
        public int Id, SettlementId, BankCell, WorkerId;
        public int[] Cells = new int[0];
        public byte Level, Axis;
        public bool Bridge;
        public ItemType Material;
        public float Required, Delivered, WorkSeconds;
        public long LastProgressTick;
    }
    public sealed partial class UnitData { public int TransportProjectId; }
    public sealed partial class WorldState
    {
        public int TransportVersion;
        public List<TransportProjectData> TransportProjects = new List<TransportProjectData>();

        public float GetTerrainSpeed(int cell)
        {
            if (cell < 0 || cell >= Cells.Length) return 0;
            TileData t = Cells[cell];
            float snowSpeed = 1 - .3f * t.SnowCover;
            InfrastructureCell c = Infrastructure == null ? default(InfrastructureCell) : Infrastructure[cell];
            if (c.BridgeLevel > 0) return (c.BridgeLevel == 1 ? 1.1f : 1.4f) * snowSpeed;
            if (t.Terrain == TerrainType.Mountain) return 0;
            if (t.Terrain == TerrainType.DeepWater || t.Terrain == TerrainType.ShallowWater)
            {
                if (t.WaterKind == WaterKind.Ocean || t.WaterDepth <= 0 || t.WaterDepth > .6f || t.CurrentStrength > .65f) return 0;
                return Math.Max(.12f, .38f - t.WaterDepth * .22f - t.CurrentStrength * .12f);
            }
            if (c.RoadLevel > 0) return (c.RoadLevel == 1 ? 1.08f : c.RoadLevel == 2 ? 1.2f : c.RoadLevel == 3 ? 1.35f : 1.55f) * snowSpeed;
            float speed = t.Terrain == TerrainType.Swamp ? .32f : t.Terrain == TerrainType.Hills ? .72f : t.Terrain == TerrainType.Ravine ? .28f : t.Terrain == TerrainType.Highlands ? .8f : t.Terrain == TerrainType.Forest ? .66f : t.Terrain == TerrainType.Sand ? .7f : t.Terrain == TerrainType.Desert ? .62f : t.Terrain == TerrainType.Snow ? .5f : 1;
            if (c.Stream > .01f) speed *= .65f;
            return speed * snowSpeed;
        }
        public bool CanStep(int from, int to)
        {
            if (from < 0 || to < 0 || from >= Cells.Length || to >= Cells.Length || !IsWalkable(to % Width, to / Width)) return false;
            if (Math.Abs(from % Width - to % Width) + Math.Abs(from / Width - to / Width) != 1) return false;
            if (Infrastructure != null && (Infrastructure[from].BridgeLevel > 0 || Infrastructure[to].BridgeLevel > 0)) return true;
            return (Cells[from].Terrain != TerrainType.Ravine && Cells[to].Terrain != TerrainType.Ravine) || Math.Abs(Cells[from].Height - Cells[to].Height) <= .12f;
        }
        public float GetStepCost(int from, int to)
        {
            float a = GetTerrainSpeed(from), b = GetTerrainSpeed(to);
            if (b <= 0) return float.PositiveInfinity;
            // Half a cell on each surface. Height is normalized world elevation, not metres.
            float cost = .5f / Math.Max(.12f, a) + .5f / b;
            if (Infrastructure == null || (Infrastructure[from].BridgeLevel == 0 && Infrastructure[to].BridgeLevel == 0))
                cost *= 1 + Math.Max(0, Cells[to].Height - Cells[from].Height) * 8;
            return cost;
        }
        public string GetTraversalBlockReason(int cell)
        {
            if (cell < 0 || cell >= Cells.Length) return "Вне мира";
            if (IsWalkable(cell % Width, cell / Width)) return "";
            TileData t = Cells[cell];
            if (t.Terrain == TerrainType.Mountain) return "Непроходимая гора";
            if (t.WaterKind == WaterKind.Ocean) return "Море — нужна водная переправа";
            if (t.WaterDepth > .6f) return "Слишком глубоко — нужен мост";
            if (t.CurrentStrength > .65f) return "Быстрое течение — нужен мост";
            return "Проход перекрыт";
        }
        private bool ValidateTransport(out string error)
        {
            error = null;
            if (TransportVersion < 0 || TransportVersion > 1) { error = "Unsupported transport version."; return false; }
            if (TransportVersion == 0) return true;
            if (TransportProjects == null || TransportProjects.Count > Settlements.Count) { error = "Invalid transport projects."; return false; }
            HashSet<int> towns = new HashSet<int>(); HashSet<int> ids = new HashSet<int>();
            foreach (TransportProjectData p in TransportProjects)
            {
                if (p == null || p.Id <= 0 || p.Id >= NextId || !ids.Add(p.Id) || GetUnit(p.Id) != null || GetBuilding(p.Id) != null || GetSettlement(p.Id) != null || !towns.Add(p.SettlementId) || GetSettlement(p.SettlementId) == null || p.BankCell < 0 || p.BankCell >= Cells.Length || p.Cells == null || p.Cells.Length < 1 || p.Cells.Length > 9 || p.Axis > 1 || p.Level < (p.Bridge ? 1 : 2) || p.Level > (p.Bridge ? 2 : 4) || (int)p.Material <= 0 || (int)p.Material >= ResourceCatalog.ItemCount || !Stock(p.Required) || p.Required <= 0 || !Stock(p.Delivered) || p.Delivered > p.Required || !Stock(p.WorkSeconds) || p.WorkSeconds > 1000 || p.LastProgressTick < 0 || p.LastProgressTick > Tick)
                { error = "Invalid transport construction."; return false; }
                ItemType material = p.Bridge ? (p.Level == 1 ? ItemType.Plank : ItemType.StoneBlock) : p.Level == 2 ? ItemType.Plank : p.Level == 3 ? ItemType.RawStone : ItemType.StoneTile;
                float required = p.Bridge ? p.Cells.Length * (p.Level == 1 ? 4 : 3) : p.Level == 2 ? .25f : 1;
                if (p.Material != material || p.Required != required || (!p.Bridge && (p.Cells.Length != 1 || p.Cells[0] != p.BankCell || p.Axis != 0)) || (p.Bridge && p.Cells.Length > (p.Level == 1 ? 5 : 9)))
                { error = "Transport bill or geometry does not match the recipe."; return false; }
                for (int n = 0; n < p.Cells.Length; n++)
                {
                    int x = p.BankCell % Width + (p.Bridge && p.Axis == 0 ? n + 1 : 0), y = p.BankCell / Width + (p.Bridge && p.Axis == 1 ? n + 1 : 0);
                    if (!InBounds(x,y) || p.Cells[n] != Index(x,y)) { error = "Transport span is not contiguous along its declared axis."; return false; }
                }
                if (p.Bridge && !InBounds(p.BankCell % Width + (p.Axis == 0 ? p.Cells.Length + 1 : 0), p.BankCell / Width + (p.Axis == 1 ? p.Cells.Length + 1 : 0))) { error = "Transport far bank is outside the world."; return false; }
                UnitData worker = GetUnit(p.WorkerId);
                if (p.WorkerId != 0 && (worker == null || worker.Health <= 0 || worker.Age < 16 || !SimulationEngine.IsCivilized(worker.Species) || worker.SettlementId != p.SettlementId || worker.TransportProjectId != p.Id || worker.Task != UnitTask.BuildTransport)) { error = "Invalid transport worker."; return false; }
            }
            foreach (UnitData u in Units) if (u.TransportProjectId != 0)
            {
                TransportProjectData p = TransportProjects.Find(candidate => candidate.Id == u.TransportProjectId);
                if (p == null || p.WorkerId != u.Id || u.Task != UnitTask.BuildTransport) { error = "Unknown or nonreciprocal transport assignment."; return false; }
            }
            return true;
        }
    }
}
