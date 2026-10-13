using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public sealed partial class SettlementData
    {
        public long NextPlanningTick, LastPlanningTick;
        public string LastPlanningReason = "";
    }
    public sealed partial class WorldState
    {
        private bool ValidateRulerPlanning(out string error)
        {
            error = null;
            foreach (SettlementData s in Settlements)
                if (s.NextPlanningTick < 0 || s.LastPlanningTick < 0 || s.LastPlanningTick > Tick || (s.LastPlanningReason != null && s.LastPlanningReason.Length > 512))
                { error = "Invalid ruler planning record."; return false; }
            return true;
        }
    }
    public sealed partial class SimulationEngine
    {
        private bool DrySeat(int cell)
        {
            TileData t = World.Cells[cell];
            return PlacementRules.IsDryGround(World, cell % World.Width, cell / World.Width) && World.IsWalkable(cell % World.Width, cell / World.Width) && t.WaterDepth == 0 && t.Fire == 0 && t.BuildingId == 0 && t.SettlementId == 0 && t.Terrain != TerrainType.Swamp && t.Terrain != TerrainType.Ravine;
        }
        // Bounded flood fill: prefer elevation only inside the founder's reachable component.
        private int ChooseSettlementSeat(int x, int y)
        {
            if (!World.InBounds(x, y)) return -1;
            int start = World.Index(x, y); if (!World.IsWalkable(x, y)) return start;
            var distance = new Dictionary<int, int>(); var queue = new Queue<int>();
            distance[start] = 0; queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue(); if (distance[cell] >= 18) continue;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cell % World.Width + Dx[d], ny = cell / World.Width + Dy[d];
                    if (!World.InBounds(nx, ny) || DistanceSquared(nx, ny, x, y) > 12 * 12) continue;
                    int next = World.Index(nx, ny);
                    if (distance.ContainsKey(next) || !World.CanStep(cell, next) || World.Cells[next].WaterDepth > 0) continue;
                    distance[next] = distance[cell] + 1; queue.Enqueue(next);
                }
            }
            int best = DrySeat(start) ? start : -1, fallbackDistance = int.MaxValue; float score = float.NegativeInfinity;
            foreach (KeyValuePair<int, int> candidate in distance)
                if (!DrySeat(start) && DrySeat(candidate.Key) && DistanceSquared(candidate.Key % World.Width, candidate.Key / World.Width, x, y) <= 36 && (candidate.Value < fallbackDistance || candidate.Value == fallbackDistance && candidate.Key < best))
                { best = candidate.Key; fallbackDistance = candidate.Value; }
            foreach (KeyValuePair<int, int> candidate in distance)
            {
                int cell = candidate.Key, cx = cell % World.Width, cy = cell / World.Width;
                if (DistanceSquared(cx, cy, x, y) > 36 || !DrySeat(cell)) continue;
                bool water = false, food = false;
                foreach (KeyValuePair<int, int> site in distance)
                {
                    int sx = site.Key % World.Width, sy = site.Key / World.Width;
                    if (DistanceSquared(cx, cy, sx, sy) > 64) continue;
                    TileData t = World.Cells[site.Key];
                    if ((t.Terrain == TerrainType.Grass || t.Terrain == TerrainType.Forest) && t.Vegetation > .12f) food = true;
                    if (World.IsFreshWater(site.Key)) water = true;
                    for (int d = 0; d < 4 && !water; d++) if (World.InBounds(sx + Dx[d], sy + Dy[d])) water = World.IsFreshWater(World.Index(sx + Dx[d], sy + Dy[d]));
                    if (water && food) break;
                }
                if (!water || !food) continue;
                float value = World.Cells[cell].Height * 20 - candidate.Value * .15f;
                if (value > score || value == score && cell < best) { score = value; best = cell; }
            }
            // Dry worlds retain the requested site; the basic well remains the bootstrap escape.
            return best;
        }
        private void RecordPlanning(SettlementData s, string reason)
        {
            s.LastPlanningTick = World.Tick; s.NextPlanningTick = World.Tick + 300;
            s.LastPlanningReason = reason.Length > 512 ? reason.Substring(0, 512) : reason;
        }
        private bool CanDemolish(SettlementData s, BuildingData b)
        {
            if (s == null || b == null || b.SettlementId != s.Id || b.Health <= 0 || !World.IsTerrainWalkable(b.X, b.Y) || World.Cells[World.Index(b.X, b.Y)].Fire > 0) return false;
            if (b.Type == BuildingType.Hall || b.Type == BuildingType.Well || b.Type == BuildingType.Farm && CountBuildings(s.Id, BuildingType.Farm) <= 1) return false;
            // Water has no sealed ground-container representation; never silently erase it.
            if (b.BrewWaterStored > .00001f || World.GroundStocks.Count + ResourceCatalog.ItemCount * 3 + b.PersonalStocks.Count > 30000 || World.AgriculturalDrops.Count + 14 > 20000) return false;
            return true;
        }
        public bool TryDemolishBuilding(int settlementId, int buildingId, string reason)
        {
            SettlementData s = World.GetSettlement(settlementId); BuildingData b = World.GetBuilding(buildingId);
            if (s == null || World.Tick < s.NextPlanningTick || string.IsNullOrWhiteSpace(reason) || !EligibleRuler(World.GetUnit(s.RulerId), s.Id) || !CanDemolish(s, b)) return false;
            float x = b.X + .5f, y = b.Y + .5f;
            if (b.FurnitureCount > 0) { World.GroundStocks.Add(new GroundStockData { Item = ItemType.Furniture, Amount = b.FurnitureCount, X = x, Y = y }); b.FurnitureCount = 0; }
            for (int i = 1; i < ResourceCatalog.ItemCount; i++)
            {
                // The ruler releases tax goods to the public salvage pile; private title is kept separately.
                float amount = b.Inventory[i] + b.TaxStocks[i];
                if (amount > 0) World.GroundStocks.Add(new GroundStockData { Item = (ItemType)i, Amount = amount, X = x, Y = y });
                b.Inventory[i] = b.TaxStocks[i] = 0;
            }
            foreach (OwnedGoodsData lot in b.PersonalStocks)
                if (lot.Amount > 0) World.GroundStocks.Add(new GroundStockData { OwnerId = lot.OwnerId, Item = lot.Item, Amount = lot.Amount, X = x, Y = y });
            b.PersonalStocks.Clear();
            for (int i = 1; i < b.AgriculturalStock.Length; i++)
            {
                if (b.AgriculturalStock[i] > 0) World.AgriculturalDrops.Add(new AgriculturalDrop { OwnerSettlementId = s.Id, Product = (AgriculturalProduct)i, Amount = b.AgriculturalStock[i], X = x, Y = y });
                b.AgriculturalStock[i] = 0;
            }
            if (b.Type == BuildingType.Farm && b.HarvestReady > 0)
                World.AgriculturalDrops.Add(new AgriculturalDrop { OwnerSettlementId = s.Id, Product = CropProduct(b.FarmCrop), Amount = b.HarvestReady, X = x, Y = y });
            b.HarvestReady = 0;
            foreach (UnitData u in World.Units)
                if (u.HomeBuildingId == b.Id || u.Id == b.OwnerId)
                { u.Trust = Math.Max(0, u.Trust - .12f); u.Mood = Math.Max(0, u.Mood - .08f); u.EventStress = Math.Min(1, u.EventStress + .2f); }
            b.Health = 0; DestroyInvalidBuildings(); RecountSettlements();
            RecordPlanning(s, reason); AddEvent(EventKind.Built, x, y, s.Name + ": снос — " + s.LastPlanningReason);
            return true;
        }
        private bool HouseOnRoadLink(BuildingData b)
        {
            int x = b.X, y = b.Y;
            for (int axis = 0; axis < 2; axis++)
            {
                int dx = axis == 0 ? 1 : 0, dy = axis == 0 ? 0 : 1;
                if (!World.InBounds(x - dx, y - dy) || !World.InBounds(x + dx, y + dy)) continue;
                if (World.Infrastructure[World.Index(x - dx, y - dy)].RoadLevel >= 2 && World.Infrastructure[World.Index(x + dx, y + dy)].RoadLevel >= 2) return true;
            }
            return false;
        }
        private void UpdateRulerPlanning()
        {
            for (int i = 0; i < World.Settlements.Count; i++)
            {
                SettlementData s = World.Settlements[i]; UnitData ruler = World.GetUnit(s.RulerId);
                if (World.Tick < s.NextPlanningTick || !EligibleRuler(ruler, s.Id) || s.EnemyId != 0 || HasTransportProject(s.Id) || CountBuildings(s.Id, BuildingType.Sawmill) == 0 || s.Food < s.Population) continue;
                bool acted = false;
                for (int j = 0; j < World.Buildings.Count && !acted; j++)
                {
                    BuildingData b = World.Buildings[j];
                    if (b.SettlementId != s.Id || b.Type != BuildingType.House || !CanDemolish(s, b) || !navigation.Find(World.Index(s.X, s.Y), World.Index(b.X, b.Y), scratchPath)) continue;
                    bool spare = s.Housing - HousingCapacity(b) >= s.Population;
                    int x = b.X, y = b.Y;
                    if (spare && ruler.Prudence >= .5f && HouseOnRoadLink(b) && World.HasTechnology(s.Id, "l06_paths") && FreePublicStock(s.Id, ItemType.Plank) >= .25f)
                    { acted = TryDemolishBuilding(s.Id, b.Id, "Освободить разрыв между двумя дорогами; запас жилья достаточен."); if (acted) TryPlanRoad(s.Id, x, y, 2); }
                    else if (spare && ruler.Prudence >= .5f && CountBuildings(s.Id, BuildingType.School) == 0 && World.HasTechnology(s.Id, "k01_teaching") && s.Wood >= 8)
                    { acted = TryDemolishBuilding(s.Id, b.Id, "Заменить избыточный дом школой: обучение важнее пустующего жилья."); if (acted) TryBuildInfrastructure(s.Id, BuildingType.School, x, y); }
                    else if (ruler.Prudence < .25f && ruler.Ambition > .8f && s.Wood >= 18 && CountBuildings(s.Id, BuildingType.Tower) == 0 && World.HasTechnology(s.Id, "d10_wooden_defenses"))
                    { acted = TryDemolishBuilding(s.Id, b.Id, "Престижная башня вместо дома: правитель недооценил цену жилья."); if (acted) { SpendPublicStock(s, ItemType.Plank, 10); RecordConstructionCost(AddBuilding(s, x, y, BuildingType.Tower), ItemType.Plank, 10); } }
                }
                if (!acted) s.NextPlanningTick = World.Tick + 300;
            }
        }
    }
}
