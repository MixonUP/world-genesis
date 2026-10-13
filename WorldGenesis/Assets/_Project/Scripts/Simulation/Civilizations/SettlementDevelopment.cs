using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private readonly Dictionary<int, int> occupiedBeds = new Dictionary<int, int>();

        private void MigrateDevelopment()
        {
            if (World.DevelopmentVersion == 1) return;
            World.Infrastructure = new InfrastructureCell[World.Cells.Length];
            float threshold = Math.Max(45, World.Cells.Length / 170f);
            for (int i = 0; i < World.Cells.Length; i++)
            {
                TileData tile = World.Cells[i];
                bool water = tile.Terrain == TerrainType.ShallowWater || tile.Terrain == TerrainType.DeepWater;
                float stream = !water && tile.Terrain != TerrainType.Mountain && tile.Flow >= threshold * .26f && tile.Moisture >= .4f
                    ? Math.Min(1, tile.Flow / threshold) : 0;
                float fish = water ? 2 + StableRandom.Coordinate(World.Seed, i, 0, 7101) * 4 : 0;
                World.Infrastructure[i] = new InfrastructureCell { Stream = stream, Fish = fish, FishCapacity = fish };
            }
            for (int i = 0; i < World.Buildings.Count; i++) InitializeDevelopmentBuilding(World.Buildings[i]);
            for (int i = 0; i < World.Settlements.Count; i++) InitializeDevelopmentSettlement(World.Settlements[i]);
            World.DevelopmentVersion = 1;
            AssignHomesAndOwners(); World.TerrainRevision++;
        }

        private void InitializeDevelopmentSettlement(SettlementData settlement)
        {
            settlement.UnlockedTechnologies = new List<string>(); settlement.ActiveTechnology = "";
            settlement.ResearchProgress = settlement.ResearchRate = 0; settlement.DevelopmentSinceTick = World.Tick;
        }
        private void InitializeDevelopmentBuilding(BuildingData building)
        {
            building.Level = 1; building.GateOpen = true; building.Ownership = BuildingOwnership.Public;
            building.OwnerId = 0; building.WaterStored = 0; // New wells fill from groundwater over time.
            World.NavigationRevision++;
        }
        public static int HousingCapacity(BuildingData b)
        {
            return b.Type == BuildingType.Hall ? 8 : b.Type == BuildingType.House ? 3 + Math.Max(1, b.Level) * 2 : 0;
        }
        private int CountCivilBuildings(int settlementId)
        {
            int count = 0;
            for (int i = 0; i < World.Buildings.Count; i++)
                if (World.Buildings[i].SettlementId == settlementId && World.Buildings[i].Type != BuildingType.WoodWall && World.Buildings[i].Type != BuildingType.Gate) count++;
            return count;
        }
        private void AssignHomesAndOwners()
        {
            occupiedBeds.Clear();
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData b = World.Buildings[i]; UnitData owner = World.GetUnit(b.OwnerId);
                if (owner == null || owner.Health <= 0 || owner.Age < 16 || owner.SettlementId != b.SettlementId)
                { b.OwnerId = 0; b.Ownership = BuildingOwnership.Public; }
            }
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData u = World.Units[i];
                if (!IsCivilized(u.Species) || u.Health <= 0) { u.HomeBuildingId = 0; continue; }
                int occupied;
                u.HomeBuildingId = 0;
                for (int pass = 0; pass < 2 && u.HomeBuildingId == 0; pass++)
                for (int b = 0; b < World.Buildings.Count; b++)
                {
                    BuildingData home = World.Buildings[b];
                    if (home.SettlementId != u.SettlementId || home.Health <= 0 || home.Type != (pass == 0 ? BuildingType.House : BuildingType.Hall)) continue;
                    occupiedBeds.TryGetValue(home.Id, out occupied);
                    if (occupied >= HousingCapacity(home)) continue;
                    u.HomeBuildingId = home.Id; occupiedBeds[home.Id] = occupied + 1; break;
                }
            }
            // Legal ownership is distinct from residence and from the owner of each stored lot.
            for (int b = 0; b < World.Buildings.Count; b++)
            {
                BuildingData building = World.Buildings[b];
                if (building.OwnerId != 0 || (building.Type != BuildingType.House && building.Type != BuildingType.Shop)) continue;
                for (int i = 0; i < World.Units.Count; i++)
                {
                    UnitData u = World.Units[i];
                    if (u.Health <= 0 || u.Age < 16 || u.SettlementId != building.SettlementId || (building.Type == BuildingType.House && u.HomeBuildingId != building.Id)) continue;
                    building.OwnerId = u.Id; building.Ownership = BuildingOwnership.Private; break;
                }
            }
        }

        private void UpdateDevelopment()
        {
            if (World.Tick % 10 != 0) return;
            AssignHomesAndOwners();
            for (int i = 0; i < World.Infrastructure.Length; i++)
            {
                InfrastructureCell c = World.Infrastructure[i]; TileData t = World.Cells[i];
                bool water = t.Terrain == TerrainType.DeepWater || t.Terrain == TerrainType.ShallowWater;
                if (!water) { c.Fish = c.FishCapacity = 0; }
                else
                {
                    if (c.FishCapacity == 0) c.FishCapacity = 2 + StableRandom.Coordinate(World.Seed, i, 0, 7101) * 4;
                    c.Fish = Math.Min(c.FishCapacity, c.Fish + .012f * Math.Max(.25f, c.Fish / c.FishCapacity));
                }
                if ((!World.IsTerrainWalkable(i % World.Width, i / World.Width) || t.Fire > .1f) && c.RoadLevel > 0)
                { c.RoadLevel = 0; c.RoadOwnerId = 0; World.TerrainRevision++; }
                if (water || t.Terrain == TerrainType.Mountain || (t.BuildingId != 0 && World.GetBuilding(t.BuildingId)?.Type != BuildingType.Well)) c.Stream = 0;
                World.Infrastructure[i] = c;
            }
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData b = World.Buildings[i];
                if (b.Type == BuildingType.Well && b.Health > 0)
                {
                    float moisture = World.Cells[World.Index(b.X, b.Y)].Moisture;
                    float technology = World.HasTechnology(b.SettlementId, "c02_clean_water") ? 1.2f : 1;
                    if (World.HasTechnology(b.SettlementId, "c06_stream_management")) technology *= 1.25f;
                    b.WaterStored = Math.Min(12 + 6 * b.Level, b.WaterStored + (.08f + moisture * .18f) * b.Level * technology);
                }
            }
            for (int i = 0; i < World.Settlements.Count; i++) UpdateResearch(World.Settlements[i]);
        }

        public static double DiminishedContribution(double effectiveCount, double weight)
        {
            return effectiveCount <= 0 ? 0 : weight * (1 - Math.Pow(.88, effectiveCount)) / .12;
        }
        private void UpdateResearch(SettlementData s)
        {
            if (s.UnlockedTechnologies == null) InitializeDevelopmentSettlement(s);
            double ordinary = 0, crafts = 0; float quality = 0; int count = 0;
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData u = World.Units[i];
                if (u.SettlementId != s.Id || u.Age < 16 || u.Health <= 0) continue;
                double effective = Math.Max(.1, 1 - .5 * u.Hunger - .3 * u.Thirst);
                if (u.Task == UnitTask.Process || u.Task == UnitTask.Produce || u.Task == UnitTask.CraftEquipment) crafts += effective; else ordinary += effective;
                quality += (1 + u.Education * .6f) * (.65f + .35f * u.Mood); count++;
            }
            // Points per second. No inactive scholar/priest roles are fabricated.
            s.ResearchRate = count == 0 ? 0 : (float)((DiminishedContribution(ordinary, .1) + DiminishedContribution(crafts, 2)) / 10) * quality / count;
            if (count == 0) return;
            if (s.ActiveTechnology.Length == 0)
            {
                TechnologyDefinition chosen = null; float priority = float.NegativeInfinity;
                for (int i = 0; i < TechnologyCatalog.All.Length; i++)
                {
                    TechnologyDefinition candidate = TechnologyCatalog.All[i];
                    if (s.UnlockedTechnologies.Contains(candidate.Id) || !ResearchReady(s, candidate)) continue;
                    float score = Math.Max(IndustryResearchPriority(s, candidate), MarketResearchPriority(s, candidate)) + 1f / (i + 1);
                    if (score > priority) { chosen = candidate; priority = score; }
                }
                if (chosen != null) { s.ActiveTechnology = chosen.Id; s.ResearchProgress = 0; }
            }
            TechnologyDefinition tech = TechnologyCatalog.Get(s.ActiveTechnology);
            if (tech == null) return;
            s.ResearchProgress = Math.Min(tech.Cost, s.ResearchProgress + s.ResearchRate);
            if (s.ResearchProgress < tech.Cost) return;
            s.UnlockedTechnologies.Add(tech.Id); s.ActiveTechnology = ""; s.ResearchProgress = 0;
            AddEvent(EventKind.Built, s.X + .5f, s.Y + .5f, s.Name + ": изучено «" + tech.Name + "»");
        }
        private static bool ResearchReady(SettlementData s, TechnologyDefinition tech)
        {
            for (int i = 0; i < tech.Prerequisites.Length; i++) if (!s.UnlockedTechnologies.Contains(tech.Prerequisites[i])) return false;
            return true;
        }

        private float FreePublicStock(int settlementId, ItemType item)
        {
            if (item == ItemType.Plank) return World.GetSettlement(settlementId)?.Wood ?? 0;
            float result = 0;
            for (int i = 0; i < World.Buildings.Count; i++)
                if (World.Buildings[i].SettlementId == settlementId && World.Buildings[i].Health > 0) result += AvailableForHaul(World.Buildings[i], item);
            return result;
        }
        private void SpendPublicStock(SettlementData s, ItemType item, float amount)
        {
            if (item == ItemType.Plank) { s.Wood = Math.Max(0, s.Wood - amount); return; }
            for (int i = 0; i < World.Buildings.Count && amount > .00001f; i++)
            {
                BuildingData b = World.Buildings[i]; if (b.SettlementId != s.Id || b.Health <= 0) continue;
                float spent = Math.Min(amount, AvailableForHaul(b, item)); b.Inventory[(int)item] -= spent; amount -= spent;
            }
            World.ResourceRevision++;
        }
        private string RequiredTechnology(BuildingType type)
        {
            if (IndustryCatalog.IsWorkshop(type)) return IndustryCatalog.RequiredTechnology(type);
            switch (type)
            {
                case BuildingType.Well: return ""; // A basic well precedes improved clean-water engineering.
                case BuildingType.Warehouse: case BuildingType.Granary: return "f01_preservation";
                case BuildingType.Shop: return "l01_local_trade";
                case BuildingType.WoodWall: case BuildingType.Gate: return "d10_wooden_defenses";
                case BuildingType.School: return "k01_teaching";
                case BuildingType.Pasture: return "f07_husbandry";
                case BuildingType.Brewery: return "f08_brewing";
                case BuildingType.Port: case BuildingType.HarborWarehouse: return "n01_boats";
                case BuildingType.Shipyard: return "n02_shipbuilding";
                case BuildingType.PortAuthority: return "n03_navigation";
                default: return "";
            }
        }
        public bool TryBuildInfrastructure(int settlementId, BuildingType type, int x, int y)
        {
            SettlementData s = World.GetSettlement(settlementId);
            if (s == null || HasTransportProject(s.Id) || s.Population <= 0 || (int)type < 7 || (int)type > 26 || !PlacementRules.CanPlaceBuilding(World,type,x,y)) return false;
            if (!CanBuildMaritimeSite(s, type, x, y)) return false;
            int cell = World.Index(x, y);
            if (World.Cells[cell].BuildingId != 0 || World.Cells[cell].Fire > 0 || (World.Cells[cell].SettlementId != 0 && World.Cells[cell].SettlementId != s.Id) || DistanceSquared(x, y, s.X, s.Y) > 18 * 18) return false;
            if (BuildingAccess.IsEnclosed(type))
            {
                int doorway=ChooseBuildingEntrance(x,y);
                if(doorway<0 || !World.IsWalkable(doorway%World.Width,doorway/World.Width))return false;
                foreach(UnitData u in World.Units) if((int)u.X==x && (int)u.Y==y)return false;
            }
            string requiredTechnology = RequiredTechnology(type);
            if (requiredTechnology.Length > 0 && !World.HasTechnology(s.Id, requiredTechnology)) return false;
            float wood = type == BuildingType.WoodWall ? 2 : type == BuildingType.Gate ? 4 : 8;
            float stone = type == BuildingType.Smelter || type == BuildingType.Smithy || type == BuildingType.Mint ? 3 : type == BuildingType.Well ? 2 : 0;
            if (type >= BuildingType.Port) { wood = type == BuildingType.Port ? 12 : type == BuildingType.HarborWarehouse ? 16 : 20; stone = type == BuildingType.Port ? 4 : 8; }
            if (s.Wood < wood || FreePublicStock(s.Id, ItemType.RawStone) < stone || !navigation.Find(World.Index(s.X, s.Y), cell, scratchPath)) return false;
            // Never wall over a person or directly seal a occupied work site.
            if (type == BuildingType.WoodWall)
                for (int i = 0; i < World.Units.Count; i++) if ((int)World.Units[i].X == x && (int)World.Units[i].Y == y) return false;
            SpendPublicStock(s, ItemType.Plank, wood); SpendPublicStock(s, ItemType.RawStone, stone);
            BuildingData built = AddBuilding(s, x, y, type);
            if (type == BuildingType.Port || type == BuildingType.Shipyard) built.BerthCell = FindMaritimeConstructionBerth(x, y);
            RecordConstructionCost(built, ItemType.Plank, wood); RecordConstructionCost(built, ItemType.RawStone, stone);
            World.NavigationRevision++; World.TerrainRevision++;
            return true;
        }
        public bool TryUpgradeBuilding(int buildingId)
        {
            BuildingData b = World.GetBuilding(buildingId);
            if (b == null || b.Health <= 0 || b.Level >= StorageCapacity.MaximumLevel(b.Type) || b.Type == BuildingType.WoodWall || b.Type == BuildingType.Gate) return false;
            SettlementData s = World.GetSettlement(b.SettlementId);
            string[] ids = { "m10_comfort_housing", "m11_town_houses", "m12_stone_houses", "m13_estates" };
            string upgradeTechnology = b.Type == BuildingType.HarborWarehouse ? (b.Level == 1 ? "n02_shipbuilding" : "n03_navigation") : ids[b.Level - 1];
            if (s == null || !World.HasTechnology(s.Id, upgradeTechnology)) return false;
            float wood = 6 * b.Level, masonry = b.Level >= 2 ? 2 * b.Level : 0;
            ItemType material = b.Level >= 3 ? ItemType.StoneBlock : ItemType.Brick;
            if (s.Wood < wood || FreePublicStock(s.Id, material) < masonry) return false;
            SpendPublicStock(s, ItemType.Plank, wood); SpendPublicStock(s, material, masonry);
            RecordConstructionCost(b, ItemType.Plank, wood); RecordConstructionCost(b, material, masonry);
            b.Level++; World.TerrainRevision++; RecountSettlements();
            return true;
        }
        public bool TryBuildRoad(int settlementId, int x, int y, bool paved)
        { return TryPlanRoad(settlementId, x, y, (byte)(paved ? 4 : 2)); }
        public bool SetGate(int buildingId, bool open)
        {
            BuildingData b = World.GetBuilding(buildingId);
            if (b == null || b.Type != BuildingType.Gate || b.Health <= 0) return false;
            if (!open) for (int i = 0; i < World.Units.Count; i++) if ((int)World.Units[i].X == b.X && (int)World.Units[i].Y == b.Y) return false;
            if (b.GateOpen == open) return true;
            b.GateOpen = open; World.NavigationRevision++; World.TerrainRevision++; return true;
        }
        private bool TryDevelopSettlement(SettlementData s)
        {
            if (s.BuildTimer < 8 || s.Population < 3) return false;
            if (TryDevelopIndustry(s)) return true;
            if (TryDevelopMaritimeInfrastructure(s)) return true;
            BuildingType[] priorities = { BuildingType.Well, BuildingType.Granary, BuildingType.Warehouse, BuildingType.Shop, BuildingType.Pasture, BuildingType.School, BuildingType.Brewery };
            // Preserve the founding processing loop and shelter before elective projects.
            if (CountBuildings(s.Id, BuildingType.Sawmill) == 0 || CountBuildings(s.Id, BuildingType.House) == 0) return false;
            if ((CountBuildings(s.Id, BuildingType.Stoneworks) == 0 && World.GetSettlementStock(s.Id, ItemType.RawStone) >= 2) || (CountBuildings(s.Id, BuildingType.Kiln) == 0 && World.GetSettlementStock(s.Id, ItemType.Clay) >= 2)) return false;
            for (int i = 0; i < priorities.Length; i++)
            {
                BuildingType type = priorities[i];
                string requiredTechnology = RequiredTechnology(type);
                if (CountBuildings(s.Id, type) != 0 || (requiredTechnology.Length > 0 && !World.HasTechnology(s.Id, requiredTechnology))) continue;
                int site = FindBuildingSite(s);
                if (site >= 0 && TryBuildInfrastructure(s.Id, type, site % World.Width, site / World.Width)) { s.BuildTimer = 0; return true; }
            }
            if (s.Food > s.Population * 2 && s.Housing >= s.Population)
            {
                for (int i = 0; i < World.Buildings.Count; i++)
                {
                    BuildingData store = World.Buildings[i];
                    if (store.SettlementId == s.Id && StorageCapacity.IsDepot(store) && StorageCapacity.TotalUsed(store) >= StorageCapacity.Capacity(store) * .75f && TryUpgradeBuilding(store.Id)) { s.BuildTimer = 0; return true; }
                }
                for (int i = 0; i < World.Buildings.Count; i++)
                    if (World.Buildings[i].SettlementId == s.Id && World.Buildings[i].Type == BuildingType.House && TryUpgradeBuilding(World.Buildings[i].Id)) { s.BuildTimer = 0; return true; }
                if (World.HasTechnology(s.Id, "l06_paths"))
                {
                    for (int b = 0; b < World.Buildings.Count; b++)
                    {
                        BuildingData target = World.Buildings[b]; if (target.SettlementId != s.Id || target.Type == BuildingType.WoodWall) continue;
                        if (!navigation.Find(World.Index(s.X, s.Y), World.Index(target.X, target.Y), scratchPath)) continue;
                        for (int p = 0; p < scratchPath.Count; p++)
                            if (TryBuildRoad(s.Id, scratchPath[p] % World.Width, scratchPath[p] / World.Width, World.HasTechnology(s.Id, "l07_paved_roads"))) { s.BuildTimer = 0; return true; }
                    }
                }
                if (World.HasTechnology(s.Id, "d10_wooden_defenses") && s.Wood > 15)
                {
                    int radius = 7;
                    for (int side = 0; side < 4; side++) for (int t = -radius; t <= radius; t++)
                    {
                        int x = s.X + (side < 2 ? (side == 0 ? -radius : radius) : t);
                        int y = s.Y + (side >= 2 ? (side == 2 ? -radius : radius) : t);
                        BuildingType type = Math.Abs(t) <= 1 ? BuildingType.Gate : BuildingType.WoodWall;
                        if (TryBuildInfrastructure(s.Id, type, x, y)) { s.BuildTimer = 0; return true; }
                    }
                }
            }
            return false;
        }

        private bool ChooseWater(UnitData unit, SettlementData home)
        {
            if (!IsCivilized(unit.Species)) return false;
            if (unit.CarriedWater > .00001f) { ClearRoute(unit); ChangeTask(unit, UnitTask.Drink); unit.TargetBuildingId = 0; unit.TargetCell = World.Index((int)unit.X, (int)unit.Y); unit.WorkTimer = 2; return true; }
            // Public wells and the one civic campfire share finite water at the source.
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData well = World.Buildings[i];
                if (!IsDrinkingSource(well, unit.SettlementId) || well.WaterStored < 1 || !SetTarget(unit, well.X, well.Y)) continue;
                ChangeTask(unit, UnitTask.Drink); unit.TargetBuildingId = well.Id; unit.WorkTimer = 2; return true;
            }
            int ux = (int)unit.X, uy = (int)unit.Y;
            for (int radius = 0; radius <= 18; radius++)
            for (int oy = -radius; oy <= radius; oy++) for (int ox = -radius; ox <= radius; ox++)
            {
                if (Math.Max(Math.Abs(ox), Math.Abs(oy)) != radius) continue;
                int x = ux + ox, y = uy + oy; if (!World.IsWalkable(x, y)) continue;
                bool fresh = World.IsFreshWater(World.Index(x, y));
                for (int d = 0; d < 4 && !fresh; d++) if (World.InBounds(x + Dx[d], y + Dy[d])) fresh = World.IsFreshWater(World.Index(x + Dx[d], y + Dy[d]));
                if (!fresh || !SetTarget(unit, x, y)) continue;
                ChangeTask(unit, UnitTask.Drink); unit.TargetBuildingId = 0; unit.WorkTimer = 2; return true;
            }
            return false;
        }
        private bool PerformWaterActivity(UnitData unit, SettlementData home, float dt)
        {
            if (unit.Task != UnitTask.Drink) return false;
            bool carried = unit.CarriedWater > .00001f;
            bool available = carried; BuildingData well = World.GetBuilding(unit.TargetBuildingId);
            if (IsDrinkingSource(well, unit.SettlementId) && well.WaterStored >= 1 && DistanceSquared(unit.X, unit.Y, well.X + .5f, well.Y + .5f) < 2) available = true;
            if (well == null && !carried)
            {
                int x = (int)unit.X, y = (int)unit.Y; available = World.IsFreshWater(World.Index(x, y));
                for (int d = 0; d < 4 && !available; d++) if (World.InBounds(x + Dx[d], y + Dy[d])) available = World.IsFreshWater(World.Index(x + Dx[d], y + Dy[d]));
            }
            if (!available) { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 1; return true; }
            unit.WorkTimer -= dt;
            if (unit.WorkTimer > 0) return true;
            float drunk = carried ? Math.Min(1, unit.CarriedWater) : 1;
            if (carried) unit.CarriedWater -= drunk;
            else if (well != null) well.WaterStored -= drunk;
            unit.Thirst = Math.Max(0, unit.Thirst - SubsistenceRules.NeedReliefPerPortion * drunk); ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return true;
        }
    }
}
