using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private void MigrateRemains()
        {
            if (World.RemainsVersion == 1) return;
            if (World.Remains == null) World.Remains = new List<RemainsData>();
            World.RemainsVersion = 1;
        }

        // Before death releases indoor/ship links; these coordinates must not be a former shore job.
        private void RecordRemains(UnitData unit)
        {
            if (unit == null || unit.Health > 0) return;
            float x = unit.X, y = unit.Y;
            ShipData ship = World.GetShip(unit.ShipId);
            if (ship != null) { x = ship.X; y = ship.Y; }
            else if (unit.InsideBuildingId != 0)
            {
                BuildingData building = World.GetBuilding(unit.InsideBuildingId);
                int exit = building == null ? -1 : FindBuildingExit(building, true);
                if (exit >= 0) { x = exit % World.Width + .5f; y = exit / World.Width + .5f; }
            }
            if (x < 0 || y < 0 || x >= World.Width || y >= World.Height) return;
            int cell = World.Index((int)x, (int)y);
            bool water = World.Infrastructure[cell].BridgeLevel == 0 &&
                (World.Cells[cell].Terrain == TerrainType.DeepWater || World.Cells[cell].Terrain == TerrainType.ShallowWater);
            byte direction = (byte)(unit.Id % 4);
            if (unit.Path != null && unit.PathCursor < unit.Path.Count)
            {
                int target = unit.Path[unit.PathCursor]; float dx = target % World.Width + .5f - unit.X, dy = target / World.Width + .5f - unit.Y;
                if (Math.Abs(dx) + Math.Abs(dy) > .00001f) direction = (byte)(Math.Abs(dy) > Math.Abs(dx) ? (dy > 0 ? 3 : 0) : (dx > 0 ? 2 : 1));
            }
            // The removed unit's ID is a provenance link, not a newly allocated world entity.
            if (World.Remains.Count >= RemainsRules.MaximumCount) World.Remains.RemoveAt(0);
            World.Remains.Add(new RemainsData { SourceUnitId = unit.Id, Species = unit.Species, Child = IsCivilized(unit.Species) && unit.IsChild,
                X = x, Y = y, Direction = direction, DeathTick = World.Tick, WaterDeath = water, RemainingSeconds = RemainsRules.Lifetime(water) });
        }

        // Run before this tick's new deaths, so a newly created record receives its full lifetime.
        private void UpdateRemains(float dt)
        {
            for (int i = World.Remains.Count - 1; i >= 0; i--)
            {
                RemainsData remains = World.Remains[i]; remains.RemainingSeconds = Math.Max(0, remains.RemainingSeconds - dt);
                if (remains.RemainingSeconds <= .001f) World.Remains.RemoveAt(i);
            }
        }
    }
}
