using System;

namespace WorldGenesis.Simulation
{
    public sealed partial class BuildingData { public int EntranceCell = -1; }
    public sealed partial class UnitData
    {
        public int InsideBuildingId, EnteringBuildingId;
        public float BuildingInteractionTimer;
    }
    public static class BuildingAccess
    {
        public const float EntrySeconds = .3f;
        public static bool IsEnclosed(BuildingType type)
        { return type != BuildingType.Hall && type != BuildingType.Farm && type != BuildingType.Pasture && type != BuildingType.Well && type != BuildingType.WoodWall && type != BuildingType.Gate && type != BuildingType.Port && type != BuildingType.Shipyard; }
        public static int Capacity(BuildingData b)
        { return b == null || !IsEnclosed(b.Type) ? 0 : b.Type == BuildingType.House ? 4 + Math.Max(0, b.Level - 1) * 2 : 4 + Math.Max(1, b.Level); }
        public static int Occupants(WorldState world, BuildingData b)
        { int count = 0; if (world != null && b != null) foreach (UnitData u in world.Units) if (u.Health > 0 && u.InsideBuildingId == b.Id) count++; return count; }
        public static bool IsInside(UnitData u, BuildingData b) { return u != null && b != null && u.InsideBuildingId == b.Id; }
        public static bool MayEnter(WorldState world, UnitData u, BuildingData b)
        {
            if (u == null || b == null || u.Health <= 0 || !SimulationEngine.IsCivilized(u.Species)) return false;
            if (u.SettlementId == b.SettlementId) return true;
            CaravanData c = world.GetCaravan(u.CaravanId);
            if (c == null || c.OriginId != u.SettlementId || c.DestinationId != b.SettlementId || c.MarketBuildingId != b.Id) return false;
            foreach (SettlementRelation relation in world.Relations) if (relation.AtWar && ((relation.FirstId == c.OriginId && relation.SecondId == c.DestinationId) || (relation.SecondId == c.OriginId && relation.FirstId == c.DestinationId))) return false;
            return true;
        }
        public static int EntranceCell(WorldState world, BuildingData b)
        { return world == null || b == null || !IsEnclosed(b.Type) ? -1 : b.EntranceCell; }
        public static bool DoorAvailable(WorldState world, BuildingData b, bool safe = true)
        { int cell = EntranceCell(world,b); return cell >= 0 && cell < world.Cells.Length && world.IsWalkable(cell % world.Width, cell / world.Width) && (!safe || world.Cells[cell].Fire <= .1f); }
        public static bool IsUsable(WorldState world, BuildingData b)
        { return b != null && b.Health > 0 && PlacementRules.IsDryGround(world,b.X,b.Y) && world.Cells[world.Index(b.X,b.Y)].Fire <= .1f && (!IsEnclosed(b.Type) || world.BuildingAccessVersion == 0 || DoorAvailable(world,b)); }
        public static string WaitingReason(WorldState world, BuildingData b)
        {
            if (b == null || !IsEnclosed(b.Type)) return "Открытая площадка";
            if (!IsUsable(world,b)) return "Вход недоступен или постройка опасна";
            return Occupants(world,b) >= Capacity(b) ? "Внутри нет свободного места" : "Вход свободен";
        }
    }
    public sealed partial class WorldState
    {
        public int BuildingAccessVersion;
        public int BuildingNavigationCell(int cell)
        {
            if (cell < 0 || cell >= Cells.Length || BuildingAccessVersion == 0) return cell;
            BuildingData b = GetBuilding(Cells[cell].BuildingId);
            return b != null && BuildingAccess.IsEnclosed(b.Type) ? (BuildingAccess.IsUsable(this,b) ? b.EntranceCell : -1) : cell;
        }
        private bool ValidateBuildingAccess(out string error)
        {
            error = null;
            if (BuildingAccessVersion < 0 || BuildingAccessVersion > 1) { error = "Unsupported building access version."; return false; }
            if (BuildingAccessVersion == 0)
            {
                foreach (UnitData u in Units) if (u.InsideBuildingId != 0 || u.EnteringBuildingId != 0 || u.BuildingInteractionTimer != 0 || u.Task == UnitTask.VisitBuilding) { error = "Legacy world contains indoor state."; return false; }
                return true;
            }
            foreach (BuildingData b in Buildings)
            {
                if (!BuildingAccess.IsEnclosed(b.Type)) { if (b.EntranceCell != -1) { error = "Open site has an indoor entrance."; return false; } continue; }
                if (b.EntranceCell < 0 || b.EntranceCell >= Cells.Length || Math.Abs(b.EntranceCell % Width - b.X) + Math.Abs(b.EntranceCell / Width - b.Y) != 1 || BuildingAccess.Occupants(this,b) > BuildingAccess.Capacity(b))
                { error = "Invalid building entrance or occupancy."; return false; }
            }
            foreach (UnitData u in Units)
            {
                if (u.InsideBuildingId < 0 || u.EnteringBuildingId < 0 || !Stock(u.BuildingInteractionTimer) || u.BuildingInteractionTimer > BuildingAccess.EntrySeconds + .001f || (u.InsideBuildingId != 0 && u.EnteringBuildingId != 0)) { error = "Invalid indoor transition."; return false; }
                if (u.InsideBuildingId == 0 && u.EnteringBuildingId == 0 && u.BuildingInteractionTimer != 0) { error = "Indoor timer without transition."; return false; }
                int id = u.InsideBuildingId != 0 ? u.InsideBuildingId : u.EnteringBuildingId;
                if (u.InsideBuildingId == 0)
                {
                    BuildingData standing = GetBuilding(Cells[Index((int)u.X,(int)u.Y)].BuildingId);
                    if (standing != null && BuildingAccess.IsEnclosed(standing.Type)) { error = "Exterior unit overlaps a building wall."; return false; }
                }
                if (id == 0) continue;
                BuildingData b = GetBuilding(id);
                if (b == null || !BuildingAccess.IsEnclosed(b.Type) || u.Health <= 0 || !SimulationEngine.IsCivilized(u.Species)) { error = "Invalid indoor resident or destination."; return false; }
                if (u.InsideBuildingId != 0 && (Math.Abs(u.X-b.X-.5f) > .001f || Math.Abs(u.Y-b.Y-.5f) > .001f || u.PathCursor < u.Path.Count || u.BuildingInteractionTimer != 0)) { error = "Indoor resident has an exterior position or route."; return false; }
                if (u.EnteringBuildingId != 0 && u.BuildingInteractionTimer > 0 && (Math.Abs(u.X-(b.EntranceCell%Width+.5f)) > .15f || Math.Abs(u.Y-(b.EntranceCell/Width+.5f)) > .15f)) { error = "Remote building entry."; return false; }
            }
            return true;
        }
    }
}
