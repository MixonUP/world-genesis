using System;

namespace WorldGenesis.Simulation
{
    /// <summary>Economic days are independent of accelerated aging and research cycles.</summary>
    public static class SubsistenceRules
    {
        public const float DaySeconds = 600f;
        public const float FoodPerPersonPerDay = 1f;
        public const float WaterPerPersonPerDay = 1f;
        public const int CampfirePeople = 25;
        public const int ReserveDays = 2;
        public const float NeedReliefPerPortion = .4f;
        public const float ProductionPerSecond = CampfirePeople / DaySeconds;
        public const float FoodReserve = CampfirePeople * FoodPerPersonPerDay * ReserveDays;
        public const float WaterReserve = CampfirePeople * WaterPerPersonPerDay * ReserveDays;
        public const float HungerPerSecond = NeedReliefPerPortion * FoodPerPersonPerDay / DaySeconds;
        public const float ThirstPerSecond = NeedReliefPerPortion * WaterPerPersonPerDay / DaySeconds;

        public static float WaterCapacity(BuildingData building)
        {
            if (building == null) return 0;
            return building.Type == BuildingType.Hall ? WaterReserve : building.Type == BuildingType.Well ? 12 + 6 * building.Level : 0;
        }

        public static bool IsWorkingCampfire(WorldState world, BuildingData building)
        {
            if (world == null || building == null || building.Type != BuildingType.Hall || building.Health <= 0 ||
                world.GetCampfire(building.SettlementId) != building || !world.InBounds(building.X, building.Y)) return false;
            TileData tile = world.Cells[world.Index(building.X, building.Y)];
            return tile.BuildingId == building.Id && tile.Fire <= .1f && world.IsWalkable(building.X, building.Y);
        }
    }

    public sealed partial class WorldState
    {
        public int SubsistenceVersion;

        // A deterministic civic source, not one subsidy per duplicate Hall or per level.
        public BuildingData GetCampfire(int settlementId)
        {
            if (GetSettlement(settlementId) == null) return null;
            BuildingData selected = null;
            for (int i = 0; i < Buildings.Count; i++)
            {
                BuildingData b = Buildings[i];
                if (b.SettlementId == settlementId && b.Type == BuildingType.Hall && b.Health > 0 && (selected == null || b.Id < selected.Id)) selected = b;
            }
            return selected;
        }

        private bool ValidateSubsistence(out string error)
        {
            error = null;
            if (SubsistenceVersion < 0 || SubsistenceVersion > 1) { error = "Unsupported subsistence version."; return false; }
            for (int i = 0; i < Buildings.Count; i++)
                if (Buildings[i].Type == BuildingType.Hall && (!Stock(Buildings[i].WaterStored) || Buildings[i].WaterStored > SubsistenceRules.WaterReserve + .0001f || (SubsistenceVersion == 0 && Buildings[i].WaterStored != 0)))
                { error = "Invalid campfire water reserve."; return false; }
            return true;
        }
    }

    public sealed partial class SimulationEngine
    {
        private void UpdateCampfireSupply(float dt)
        {
            for (int i = 0; i < World.Settlements.Count; i++)
            {
                SettlementData town = World.Settlements[i];
                BuildingData campfire = World.GetCampfire(town.Id);
                if (!SubsistenceRules.IsWorkingCampfire(World, campfire)) continue;
                float produced = SubsistenceRules.ProductionPerSecond * dt;
                // Existing gathered food above this soft reserve is never clamped or destroyed.
                if (town.Food < SubsistenceRules.FoodReserve) town.Food = Math.Min(SubsistenceRules.FoodReserve, town.Food + produced * SubsistenceRules.FoodPerPersonPerDay);
                campfire.WaterStored = Math.Min(SubsistenceRules.WaterReserve, campfire.WaterStored + produced * SubsistenceRules.WaterPerPersonPerDay);
            }
        }

        private bool IsDrinkingSource(BuildingData source, int settlementId)
        {
            if (source == null || source.Health <= 0 || source.SettlementId != settlementId) return false;
            return source.Type == BuildingType.Well || SubsistenceRules.IsWorkingCampfire(World, source);
        }
    }
}
