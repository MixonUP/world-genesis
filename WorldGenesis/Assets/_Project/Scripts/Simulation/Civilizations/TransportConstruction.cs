using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private void MigrateTransport()
        {
            if (World.TransportVersion == 1) return;
            if (World.TransportProjects == null) World.TransportProjects = new List<TransportProjectData>();
            for (int i = 0; i < World.Infrastructure.Length; i++)
            {
                InfrastructureCell c = World.Infrastructure[i]; c.RoadLevel = (byte)(c.RoadLevel == 1 ? 2 : c.RoadLevel == 2 ? 4 : 0);
                World.Infrastructure[i] = c;
            }
            World.TransportVersion = 1; World.NavigationRevision++;
        }
        private void ClearTransportCell(int cell)
        {
            InfrastructureCell c = World.Infrastructure[cell];
            c.RoadLevel = c.BridgeLevel = c.BridgeAxis = 0; c.RoadOwnerId = c.BridgeOwnerId = 0; c.FootTraffic = 0;
            World.Infrastructure[cell] = c;
        }
        private void RecordFootstep(UnitData u, int cell)
        {
            if (!IsCivilized(u.Species) || World.Cells[cell].BuildingId != 0 || World.Cells[cell].WaterDepth > 0 || World.GetTerrainSpeed(cell) <= 0) return;
            InfrastructureCell c = World.Infrastructure[cell]; c.FootTraffic = Math.Min(100, c.FootTraffic + 1);
            if (c.RoadLevel == 0 && c.FootTraffic >= 8)
            { c.RoadLevel = 1; c.RoadOwnerId = u.SettlementId; World.TerrainRevision++; }
            World.Infrastructure[cell] = c;
        }
        private TransportProjectData TransportProject(int id)
        { for (int i = 0; i < World.TransportProjects.Count; i++) if (World.TransportProjects[i].Id == id) return World.TransportProjects[i]; return null; }
        private bool HasTransportProject(int town)
        { for (int i = 0; i < World.TransportProjects.Count; i++) if (World.TransportProjects[i].SettlementId == town) return true; return false; }
        public bool TryPlanRoad(int settlementId, int x, int y, byte level)
        {
            SettlementData s = World.GetSettlement(settlementId);
            if (s == null || s.Population < 1 || level < 2 || level > 4 || HasTransportProject(s.Id) || !World.IsWalkable(x, y)) return false;
            int cell = World.Index(x, y); TileData t = World.Cells[cell];
            string tech = level == 2 ? "l06_paths" : level == 3 ? "l09_gravel_roads" : "l07_paved_roads";
            ItemType material = level == 2 ? ItemType.Plank : level == 3 ? ItemType.RawStone : ItemType.StoneTile;
            float amount = level == 2 ? .25f : 1;
            if (!World.HasTechnology(s.Id, tech) || t.SettlementId != s.Id || t.BuildingId != 0 || t.Fire > 0 || t.WaterDepth > 0 || World.Infrastructure[cell].RoadLevel >= level || FreePublicStock(s.Id, material) < amount || !navigation.Find(World.Index(s.X, s.Y), cell, scratchPath)) return false;
            World.TransportProjects.Add(new TransportProjectData { Id = World.NextId++, SettlementId = s.Id, BankCell = cell, Cells = new[] { cell }, Level = level, Material = material, Required = amount, LastProgressTick = World.Tick });
            return true;
        }
        public bool TryPlanBridge(int settlementId, int bankX, int bankY, byte axis, bool stone)
        {
            SettlementData s = World.GetSettlement(settlementId);
            if (s == null || HasTransportProject(settlementId) || axis > 1 || !World.IsWalkable(bankX, bankY) || !World.HasTechnology(settlementId, stone ? "l10_stone_bridges" : "l11_wooden_bridges")) return false;
            int bank = World.Index(bankX, bankY);
            if (DistanceSquared(bankX, bankY, s.X, s.Y) > 24 * 24 || World.Cells[bank].WaterDepth > 0 || World.Cells[bank].Fire > 0 || !navigation.Find(World.Index(s.X, s.Y), bank, scratchPath)) return false;
            int dx = axis == 0 ? 1 : 0, dy = axis == 1 ? 1 : 0;
            List<int> cells = new List<int>(); bool farBank = false;
            for (int n = 1; n <= (stone ? 10 : 6); n++)
            {
                int x = bankX + dx * n, y = bankY + dy * n;
                if (!World.InBounds(x, y)) return false;
                int cell = World.Index(x, y); TileData t = World.Cells[cell];
                bool gap = t.WaterDepth > 0 || t.Terrain == TerrainType.Ravine;
                if (!gap) { farBank = World.IsWalkable(x, y) && t.Fire == 0; break; }
                if (t.WaterKind == WaterKind.Ocean || t.WaterDepth > (stone ? 6 : 2.5f) || t.CurrentStrength > (stone ? .95f : .8f) || t.BuildingId != 0 || t.Fire > 0 || World.Infrastructure[cell].BridgeLevel > 0) return false;
                cells.Add(cell);
            }
            if (!farBank || cells.Count == 0 || cells.Count > (stone ? 9 : 5)) return false;
            ItemType material = stone ? ItemType.StoneBlock : ItemType.Plank; float amount = cells.Count * (stone ? 3 : 4);
            if (FreePublicStock(s.Id, material) < amount) return false;
            World.TransportProjects.Add(new TransportProjectData { Id = World.NextId++, SettlementId = s.Id, BankCell = bank, Cells = cells.ToArray(), Level = (byte)(stone ? 2 : 1), Axis = axis, Bridge = true, Material = material, Required = amount, LastProgressTick = World.Tick });
            AddEvent(EventKind.Built, bankX + .5f, bankY + .5f, s.Name + ": жители готовят " + (stone ? "каменный мост" : "деревянный мост"));
            return true;
        }
        private bool ChooseTransportWork(UnitData u, SettlementData s)
        {
            if (s == null || u.Age < 16 || u.Hunger > .5f || u.Thirst > .6f || u.Fatigue > .7f || u.CaravanId != 0 || CarriedLoad(u) > .00001f) return false;
            foreach (TransportProjectData p in World.TransportProjects)
            {
                if (p.SettlementId != s.Id) continue;
                UnitData worker = World.GetUnit(p.WorkerId);
                if (worker != null && worker.Health > 0 && worker.Id != u.Id && worker.Task == UnitTask.BuildTransport) continue;
                if (!SetTarget(u, p.BankCell % World.Width, p.BankCell / World.Width)) continue;
                if (worker != null && worker.Id != u.Id && worker.TransportProjectId == p.Id) worker.TransportProjectId = 0;
                ChangeTask(u, UnitTask.BuildTransport); u.TransportProjectId = p.Id; p.WorkerId = u.Id; return true;
            }
            return false;
        }
        private bool PerformTransportActivity(UnitData u, SettlementData s, float dt)
        {
            if (u.Task != UnitTask.BuildTransport) return false;
            TransportProjectData p = TransportProject(u.TransportProjectId);
            if (p == null || s == null || p.WorkerId != u.Id) { u.TransportProjectId = 0; ChangeTask(u, UnitTask.Idle); return true; }
            // A terrain command can arrive immediately before the final work tick.
            if (!TransportSiteValid(p)) { CancelTransportProject(p); return true; }
            int bx = p.BankCell % World.Width, by = p.BankCell / World.Width;
            bool onsite = DistanceSquared(u.X, u.Y, bx + .5f, by + .5f) < .4f;
            if (u.CargoAmount > 0 && u.CargoKind == p.Material)
            {
                if (!onsite) { SetTarget(u, bx, by); return true; }
                float delivered = Math.Min(u.CargoAmount, Math.Max(0, p.Required - p.Delivered)); p.Delivered += delivered; u.CargoAmount -= delivered;
                if (delivered > 0) p.LastProgressTick = World.Tick;
                if (u.CargoAmount < .00001f) { u.CargoAmount = 0; u.CargoKind = ItemType.None; }
            }
            if (p.Delivered + .00001f < p.Required)
            {
                if (CarriedLoad(u) > .00001f) { ChangeTask(u, UnitTask.Idle); return true; }
                foreach (BuildingData source in World.Buildings)
                {
                    if (!UsableBuilding(source, s.Id)) continue;
                    float amount = AvailableForHaul(source, p.Material) + (p.Material == ItemType.Plank && source.Type == BuildingType.Hall ? s.Wood : 0);
                    if (amount <= .00001f || !navigation.Find(World.Index(source.X, source.Y), p.BankCell, scratchPath)) continue;
                    if (DistanceSquared(u.X, u.Y, source.X + .5f, source.Y + .5f) >= .4f)
                    { if (SetTarget(u, source.X, source.Y)) return true; continue; }
                    amount = Math.Min(amount, Math.Min(p.Required - p.Delivered, CarrySpace(u)));
                    float local = Math.Min(amount, AvailableForHaul(source, p.Material)); source.Inventory[(int)p.Material] -= local;
                    if (p.Material == ItemType.Plank && source.Type == BuildingType.Hall) s.Wood -= amount - local;
                    u.CargoKind = p.Material; u.CargoAmount = amount; u.CargoOwnerId = 0; World.ResourceRevision++;
                    p.LastProgressTick = World.Tick;
                    SetTarget(u, bx, by); return true;
                }
                ChangeTask(u, UnitTask.Idle); return true;
            }
            if (!onsite) { SetTarget(u, bx, by); return true; }
            p.WorkSeconds += dt * GetWorkMultiplier(u);
            p.LastProgressTick = World.Tick;
            if (p.WorkSeconds < (p.Bridge ? 4 * p.Cells.Length : 2 * p.Level)) return true;
            foreach (int cell in p.Cells)
            {
                InfrastructureCell c = World.Infrastructure[cell];
                if (p.Bridge) { c.BridgeLevel = p.Level; c.BridgeOwnerId = s.Id; c.BridgeAxis = p.Axis; }
                else { c.RoadLevel = p.Level; c.RoadOwnerId = s.Id; }
                World.Infrastructure[cell] = c;
            }
            AddEvent(EventKind.Built, bx + .5f, by + .5f, s.Name + (p.Bridge ? ": построен мост" : ": улучшена дорога"));
            World.TransportProjects.Remove(p);
            foreach (UnitData assigned in World.Units) if (assigned.TransportProjectId == p.Id) assigned.TransportProjectId = 0;
            ChangeTask(u, UnitTask.Idle);
            World.NavigationRevision++; World.TerrainRevision++; return true;
        }
        private void UpdateTransport()
        {
            // Destruction changes the same traversability used by routes and the people on the span.
            if (World.Tick % 10 == 0)
                for (int cell = 0; cell < World.Infrastructure.Length; cell++)
                {
                    InfrastructureCell c = World.Infrastructure[cell];
                    if (c.BridgeLevel != 1 || World.Cells[cell].Fire <= .4f) continue;
                    c.BridgeLevel = c.BridgeAxis = 0; c.BridgeOwnerId = 0; World.Infrastructure[cell] = c;
                    World.NavigationRevision++; World.TerrainRevision++;
                }
            for (int i = World.TransportProjects.Count - 1; i >= 0; i--)
            {
                TransportProjectData p = World.TransportProjects[i]; UnitData worker = World.GetUnit(p.WorkerId);
                if (worker == null || worker.Health <= 0 || worker.Task != UnitTask.BuildTransport || worker.TransportProjectId != p.Id)
                { if (worker != null && worker.TransportProjectId == p.Id) worker.TransportProjectId = 0; p.WorkerId = 0; }
                bool valid = TransportSiteValid(p);
                if (valid && World.Tick % 10 == 0)
                {
                    SettlementData s = World.GetSettlement(p.SettlementId);
                    bool reachable = s != null && s.Population > 0 && navigation.Find(World.Index(s.X,s.Y),p.BankCell,scratchPath,4096,true);
                    // Never reset progress merely because a route exists or a new worker was assigned.
                    if (!reachable && World.Tick - p.LastProgressTick >= 900) valid = false;
                }
                if (World.Tick - p.LastProgressTick >= 900) valid = false;
                if (valid) continue;
                CancelTransportProject(p);
            }
            if (World.Tick % 100 != 0) return;
            foreach (SettlementData s in World.Settlements)
            {
                if (HasTransportProject(s.Id) || s.Population < 3 || s.Food < s.Population || !World.HasTechnology(s.Id, "l06_paths")) continue;
                bool planned = false;
                // Bounded regional search: a connection between two dry banks creates an expansion route.
                if (World.HasTechnology(s.Id, "l11_wooden_bridges"))
                for (int y = Math.Max(1, s.Y - 16); y < Math.Min(World.Height - 1, s.Y + 17) && !planned; y++)
                for (int x = Math.Max(1, s.X - 16); x < Math.Min(World.Width - 1, s.X + 17) && !planned; x++)
                {
                    if (!World.IsWalkable(x, y) || World.Cells[World.Index(x, y)].WaterDepth > 0) continue;
                    for (byte axis = 0; axis < 2 && !planned; axis++)
                    {
                        int next = World.Index(x + (axis == 0 ? 1 : 0), y + (axis == 1 ? 1 : 0));
                        if (World.Cells[next].WaterDepth == 0 && World.Cells[next].Terrain != TerrainType.Ravine) continue;
                        planned = TryPlanBridge(s.Id, x, y, axis, World.HasTechnology(s.Id, "l10_stone_bridges"));
                    }
                }
                if (planned) continue;
                byte level = (byte)(World.HasTechnology(s.Id, "l07_paved_roads") ? 4 : World.HasTechnology(s.Id, "l09_gravel_roads") ? 3 : 2);
                for (int i = 0; i < World.Infrastructure.Length; i++)
                    if (World.Infrastructure[i].FootTraffic >= 8 && TryPlanRoad(s.Id, i % World.Width, i / World.Width, level)) break;
            }
        }

        private void CancelTransportProject(TransportProjectData p)
        {
            if (!World.TransportProjects.Remove(p)) return;
            if (p.Delivered > 0) World.GroundStocks.Add(new GroundStockData { Item = p.Material, Amount = p.Delivered, X = p.BankCell % World.Width + .5f, Y = p.BankCell / World.Width + .5f });
            foreach (UnitData u in World.Units)
                if (u.TransportProjectId == p.Id)
                {
                    u.TransportProjectId = 0;
                    if (u.Task == UnitTask.BuildTransport) { ChangeTask(u, UnitTask.Idle); ClearRoute(u); u.DecisionTimer = 0; }
                }
            AddEvent(EventKind.Built,p.BankCell%World.Width+.5f,p.BankCell/World.Width+.5f,"Строительство отменено; доставленные материалы оставлены у участка.");
            World.ResourceRevision++; World.Revision++;
        }

        private bool TransportSiteValid(TransportProjectData p)
        {
            if (!World.IsWalkable(p.BankCell % World.Width,p.BankCell / World.Width) || World.Cells[p.BankCell].Fire > 0 || World.Cells[p.BankCell].WaterDepth > 0) return false;
            foreach (int cell in p.Cells)
            {
                TileData t = World.Cells[cell];
                if (t.BuildingId != 0 || t.Fire > 0) return false;
                if (p.Bridge)
                {
                    if ((t.WaterDepth <= 0 && t.Terrain != TerrainType.Ravine) || t.WaterKind == WaterKind.Ocean || t.WaterDepth > (p.Level == 1 ? 2.5f : 6) || t.CurrentStrength > (p.Level == 1 ? .8f : .95f) || World.Infrastructure[cell].BridgeLevel > 0) return false;
                }
                else if (t.WaterDepth > 0 || !World.IsWalkable(cell % World.Width,cell / World.Width)) return false;
            }
            if (!p.Bridge) return true;
            int x=p.BankCell%World.Width+(p.Axis==0?p.Cells.Length+1:0), y=p.BankCell/World.Width+(p.Axis==1?p.Cells.Length+1:0);
            if(!World.InBounds(x,y)||!World.IsWalkable(x,y))return false;
            TileData bank=World.Cells[World.Index(x,y)];return bank.WaterDepth==0&&bank.Terrain!=TerrainType.Ravine&&bank.Fire==0;
        }
    }
}
