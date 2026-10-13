using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private int FindMaritimeConstructionBerth(int x, int y) { return FindBerth(x, y); }

        private bool MaritimeLandConnection(int x, int y, BuildingData anchor, int distance, int pathLimit)
        {
            if (anchor == null || !BuildingAccess.IsUsable(World, anchor) || Math.Abs(x - anchor.X) + Math.Abs(y - anchor.Y) > distance) return false;
            return MaritimeLandPath(World.Index(x, y), World.Index(anchor.X, anchor.Y), pathLimit);
        }

        private bool CanBuildMaritimeSite(SettlementData home, BuildingType type, int x, int y)
        {
            if(!PlacementRules.CanPlaceBuilding(World,type,x,y))return false;
            if (type < BuildingType.Port) return true;
            if (type == BuildingType.Port || type == BuildingType.Shipyard) return FindMaritimeConstructionBerth(x, y) >= 0;
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData port = World.Buildings[i];
                if (port.SettlementId != home.Id || port.Type != BuildingType.Port || port.Health <= 0) continue;
                if (MaritimeLandConnection(x, y, port, type == BuildingType.PortAuthority ? 6 : 3, type == BuildingType.PortAuthority ? 12 : 6)) return true;
                if (type != BuildingType.HarborWarehouse) continue;
                List<BuildingData> sections = GetHarborWarehouses(port.Id);
                for (int w = 0; w < sections.Count; w++)
                    if (MaritimeLandConnection(x, y, sections[w], 3, 6)) return true;
            }
            return false;
        }

        private bool TryDevelopMaritimeInfrastructure(SettlementData home)
        {
            if (World.Tick < home.NextMaritimeConstructionTick || home.Population < 8 || home.Food < home.Population || home.Housing < home.Population || CountBuildings(home.Id, BuildingType.Sawmill) == 0 || CountBuildings(home.Id, BuildingType.House) == 0) return false;
            home.NextMaritimeConstructionTick = World.Tick + 300;
            BuildingType[] sequence = { BuildingType.Port, BuildingType.HarborWarehouse, BuildingType.Shipyard, BuildingType.PortAuthority };
            for (int t = 0; t < sequence.Length; t++)
            {
                BuildingType type = sequence[t];
                if (CountBuildings(home.Id, type) > 0) continue;
                if (!World.HasTechnology(home.Id, RequiredTechnology(type))) return false;
                // Bounded coastal search; optional maritime projects never consume survival reserves.
                for (int radius = 2; radius <= 18; radius++)
                    for (int dy = -radius; dy <= radius; dy++)
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
                            int x = home.X + dx, y = home.Y + dy;
                            if (!PlacementRules.CanPlaceBuilding(World,type,x,y)) continue;
                            if (TryBuildInfrastructure(home.Id, type, x, y)) { home.BuildTimer = 0; return true; }
                        }
                return false;
            }
            return false;
        }
    }
    public sealed partial class SettlementData { public long NextMaritimeConstructionTick; }
}
