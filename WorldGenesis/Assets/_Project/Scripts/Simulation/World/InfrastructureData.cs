using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public enum BuildingOwnership { Public, Private }

    [Serializable]
    public struct InfrastructureCell
    {
        public float Stream, Fish, FishCapacity;
        public byte RoadLevel;
        public int RoadOwnerId;
        public float FootTraffic;
        public byte BridgeLevel, BridgeAxis;
        public int BridgeOwnerId;
    }

    public sealed partial class BuildingData
    {
        public int Level = 1;
        public BuildingOwnership Ownership;
        public int OwnerId;
        public bool GateOpen = true;
        public float WaterStored;
    }

    public sealed partial class UnitData
    {
        public int HomeBuildingId;
    }

    public sealed partial class SettlementData
    {
        public List<string> UnlockedTechnologies = new List<string>();
        public string ActiveTechnology = "";
        public float ResearchProgress, ResearchRate;
        public long DevelopmentSinceTick;
    }

    public sealed partial class WorldState
    {
        public int DevelopmentVersion;
        public InfrastructureCell[] Infrastructure;

        public bool HasTechnology(int settlementId, string id)
        {
            SettlementData settlement = GetSettlement(settlementId);
            return settlement != null && settlement.UnlockedTechnologies != null && settlement.UnlockedTechnologies.Contains(id);
        }

        public bool IsTerrainWalkable(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            return GetTerrainSpeed(Index(x, y)) > 0;
        }

        // Satisfaction derives from actual drinking, not proximity through an impassable wall.
        public float GetWaterAccess(UnitData unit) { return Math.Max(0, 1 - unit.Thirst); }
        public float GetHousingComfort(UnitData unit)
        {
            BuildingData house = GetBuilding(unit.HomeBuildingId);
            if (house == null || house.Health <= 0 || house.SettlementId != unit.SettlementId) return .08f;
            return Math.Min(1, (house.Type == BuildingType.Hall ? .25f : .32f + house.Level * .13f) + house.FurnitureCount * .1f);
        }
        public float GetHousingComfort(int settlementId)
        {
            float comfort = 0; int count = 0;
            for (int i = 0; i < Units.Count; i++)
                if (Units[i].SettlementId == settlementId && Units[i].Health > 0) { comfort += GetHousingComfort(Units[i]); count++; }
            return count == 0 ? 0 : comfort / count;
        }
        public float GetLearningSupport(int settlementId)
        {
            float support = 0;
            for (int i = 0; i < Buildings.Count; i++)
                if (Buildings[i].SettlementId == settlementId && Buildings[i].Type == BuildingType.School && Buildings[i].Health > 0)
                    support = Math.Max(support, .3f + Buildings[i].Level * .12f);
            return Math.Min(1, support);
        }
        public bool IsFreshWater(int cell)
        {
            if (cell < 0 || cell >= Cells.Length || Infrastructure == null) return false;
            TileData t = Cells[cell];
            return t.WaterKind == WaterKind.Lake || t.River || Infrastructure[cell].Stream > .01f ||
                (t.Terrain == TerrainType.ShallowWater && t.RouteHeight - t.Height > .055f && t.Flow > 8);
        }

        private bool ValidateDevelopment(out string error)
        {
            error = null;
            if (DevelopmentVersion < 0 || DevelopmentVersion > 1) { error = "Unsupported development version."; return false; }
            if (DevelopmentVersion == 0) return true;
            if (Infrastructure == null || Infrastructure.Length != Cells.Length) { error = "Missing infrastructure grid."; return false; }
            for (int i = 0; i < Infrastructure.Length; i++)
            {
                InfrastructureCell c = Infrastructure[i];
                if (!UnitRange(c.Stream) || !Stock(c.Fish) || !Stock(c.FishCapacity) || c.FishCapacity > 8 || c.Fish > c.FishCapacity + .0001f || c.RoadLevel > (TransportVersion == 0 ? 2 : 4) || (c.RoadLevel == 0 && c.RoadOwnerId != 0) || (c.RoadLevel > 1 && GetSettlement(c.RoadOwnerId) == null) || !Stock(c.FootTraffic) || c.FootTraffic > 100 || c.BridgeLevel > 2 || c.BridgeAxis > 1 || (c.BridgeLevel > 0 && GetSettlement(c.BridgeOwnerId) == null))
                { error = "Invalid water, fish or road state."; return false; }
            }
            for (int i = 0; i < Buildings.Count; i++)
            {
                BuildingData b = Buildings[i];
                if (b.Level < 1 || b.Level > 5 || (int)b.Ownership < 0 || (int)b.Ownership > 1 || b.OwnerId < 0 || !Stock(b.WaterStored) || b.WaterStored > SubsistenceRules.WaterCapacity(b) + .0001f || (b.Type != BuildingType.Well && b.Type != BuildingType.Hall && b.WaterStored != 0))
                { error = "Invalid building development."; return false; }
                UnitData owner = GetUnit(b.OwnerId);
                if ((b.Ownership == BuildingOwnership.Public && b.OwnerId != 0) || (b.Ownership == BuildingOwnership.Private && (owner == null || owner.Age < 16 || owner.Health <= 0 || owner.SettlementId != b.SettlementId)))
                { error = "Invalid property owner."; return false; }
            }
            for (int i = 0; i < Units.Count; i++)
            {
                UnitData u = Units[i]; BuildingData house = GetBuilding(u.HomeBuildingId);
                if (u.HomeBuildingId != 0 && (house == null || house.SettlementId != u.SettlementId || (house.Type != BuildingType.House && house.Type != BuildingType.Hall)))
                { error = "Invalid resident home."; return false; }
            }
            for (int i = 0; i < Settlements.Count; i++)
            {
                SettlementData s = Settlements[i];
                if (s.UnlockedTechnologies == null || s.UnlockedTechnologies.Count > TechnologyCatalog.All.Length || s.ActiveTechnology == null || !Stock(s.ResearchProgress) || !Stock(s.ResearchRate) || s.DevelopmentSinceTick < 0 || s.DevelopmentSinceTick > Tick)
                { error = "Invalid research state."; return false; }
                HashSet<string> seen = new HashSet<string>();
                for (int n = 0; n < s.UnlockedTechnologies.Count; n++)
                {
                    TechnologyDefinition tech = TechnologyCatalog.Get(s.UnlockedTechnologies[n]);
                    if (tech == null || !seen.Add(tech.Id)) { error = "Unknown or repeated technology."; return false; }
                    for (int p = 0; p < tech.Prerequisites.Length; p++) if (!s.UnlockedTechnologies.Contains(tech.Prerequisites[p])) { error = "Missing research prerequisite."; return false; }
                }
                if (s.ActiveTechnology.Length == 0) { if (s.ResearchProgress != 0) { error = "Research progress has no project."; return false; } }
                else
                {
                    TechnologyDefinition active = TechnologyCatalog.Get(s.ActiveTechnology);
                    if (active == null || seen.Contains(active.Id) || s.ResearchProgress > active.Cost) { error = "Invalid active technology."; return false; }
                    for (int p = 0; p < active.Prerequisites.Length; p++) if (!seen.Contains(active.Prerequisites[p])) { error = "Active research prerequisites not met."; return false; }
                }
            }
            return true;
        }
    }
}
