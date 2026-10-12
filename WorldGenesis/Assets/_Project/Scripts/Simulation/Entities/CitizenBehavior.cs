using System;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private void MigrateBehavior()
        {
            if (World.BehaviorVersion >= 2) return;
            bool initializeNeeds = World.BehaviorVersion == 0;
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData unit = World.Units[i];
                bool unconfirmedGuard = unit.Profession == UnitProfession.Guard;
                if (initializeNeeds || unconfirmedGuard)
                    unit.Profession = IsCivilized(unit.Species) && unit.Age >= 16 ? ProfessionFor(unit.Id) : UnitProfession.None;
                if (initializeNeeds) { unit.Fatigue = 0; unit.SocialNeed = 0; unit.TaskStartedTick = World.Tick; }
                // Earlier experimental guards had no research or equipment backing their office.
                // Retire those orders without deleting the person, needs or carried resources.
                if (unit.Task == UnitTask.Patrol || ((unconfirmedGuard || initializeNeeds) && unit.Task == UnitTask.Fight))
                {
                    ChangeTask(unit, UnitTask.Idle); ClearRoute(unit);
                    unit.TargetId = 0; unit.TargetCell = -1; unit.DecisionTimer = 0;
                }
            }
            World.BehaviorVersion = 2;
            World.Revision++;
        }

        private UnitProfession ProfessionFor(int id)
        {
            uint slot = StableRandom.Hash(World.Seed, id, 0, 3000) % 8;
            // Military recruitment is unavailable until real research and equipment inventories exist.
            return slot >= 1 && slot <= 3 ? UnitProfession.Forester : UnitProfession.Farmer;
        }

        private void ReleaseReservation(UnitData unit)
        {
            if (unit.Task == UnitTask.Gather && unit.TargetCell >= 0 && reservations.TryGetValue(unit.TargetCell, out int owner) && owner == unit.Id) reservations.Remove(unit.TargetCell);
        }

        private void ChangeTask(UnitData unit, UnitTask task)
        {
            if (unit.Task == task) return;
            ReleaseReservation(unit);
            unit.Task = task; unit.TaskStartedTick = World.Tick; unit.WorkTimer = 0;
            if (task != UnitTask.Fight && task != UnitTask.Socialize) unit.TargetId = 0;
        }

        private static void ClearRoute(UnitData unit) { unit.Path.Clear(); unit.PathCursor = 0; }

        private bool NeedsImmediateDecision(UnitData unit)
        {
            if (unit.Task == UnitTask.Idle || unit.Task == UnitTask.Flee || unit.DecisionTimer > 0) return false;
            if (unit.Hunger > .72f && unit.Task != UnitTask.Eat && unit.Species != SpeciesType.Wolf) return true;
            return unit.Fatigue > .88f && unit.Task != UnitTask.Rest && unit.Task != UnitTask.Eat;
        }

        private bool CommittedActivity(UnitData unit)
        {
            if (unit.Task == UnitTask.Eat || unit.Task == UnitTask.Rest || unit.Task == UnitTask.Socialize) return true;
            if ((unit.Task == UnitTask.Return || unit.Task == UnitTask.Patrol) && unit.PathCursor < unit.Path.Count) return true;
            if (unit.TargetCell < 0 || unit.TargetCell >= World.Cells.Length) return false;
            if (unit.Task == UnitTask.Gather) return unit.CarriedWood < 2.8f && World.Cells[unit.TargetCell].Vegetation > .02f;
            return unit.Task == UnitTask.Farm && unit.CarriedFood < 2.8f && HasWorkingFarm(unit.SettlementId, unit.TargetCell);
        }

        private float NearbyFire(int x, int y)
        {
            float maximum = 0;
            for (int d = 0; d < 4; d++)
                if (World.InBounds(x + Dx[d], y + Dy[d])) maximum = Math.Max(maximum, World.Cells[World.Index(x + Dx[d], y + Dy[d])].Fire);
            return maximum;
        }

        private UnitData FindPredator(UnitData unit)
        {
            UnitData nearest = null; float distance = 25;
            int x = (int)unit.X, y = (int)unit.Y;
            for (int cy = Math.Max(0, y - 4); cy <= Math.Min(World.Height - 1, y + 4); cy++)
            for (int cx = Math.Max(0, x - 4); cx <= Math.Min(World.Width - 1, x + 4); cx++)
            for (int n = spatialHeads[World.Index(cx, cy)]; n >= 0; n = spatialNext[n])
            {
                UnitData candidate = World.Units[n];
                if (candidate.Species != SpeciesType.Wolf || candidate.Health <= 0) continue;
                float squared = DistanceSquared(unit.X, unit.Y, candidate.X, candidate.Y);
                if (squared < distance) { distance = squared; nearest = candidate; }
            }
            return nearest;
        }

        private void EscapeDanger(UnitData unit, UnitData threat)
        {
            bool entering = unit.Task != UnitTask.Flee;
            ChangeTask(unit, UnitTask.Flee);
            if (!entering && unit.DecisionTimer > 0 && unit.PathCursor < unit.Path.Count && unit.PathRevision == World.NavigationRevision) return;
            int start = World.Index((int)unit.X, (int)unit.Y);
            int goal = navigation.Escape(start, threat == null ? unit.X : threat.X, threat == null ? unit.Y : threat.Y, threat != null, scratchPath);
            unit.Path.Clear(); unit.Path.AddRange(scratchPath); unit.PathCursor = 0;
            unit.TargetCell = goal; unit.PathRevision = World.NavigationRevision; unit.DecisionTimer = .8f;
        }

        private bool ChooseFood(UnitData unit, SettlementData home)
        {
            if (unit.Species == SpeciesType.Wolf) return false;
            int current = World.Index((int)unit.X, (int)unit.Y);
            bool carried = unit.CarriedFood >= 1;
            bool local = World.Cells[current].Vegetation >= .12f && World.Cells[current].Fire <= .1f;
            if (carried || (local && (home == null || unit.Hunger > .8f)))
            {
                ReleaseReservation(unit); ClearRoute(unit); unit.TargetCell = current;
                ChangeTask(unit, UnitTask.Eat); unit.WorkTimer = 1.2f; return true;
            }
            if (home != null && home.Food >= 1 && SetTarget(unit, home.X, home.Y))
            { ChangeTask(unit, UnitTask.Eat); unit.WorkTimer = 1.2f; return true; }
            int x = (int)unit.X, y = (int)unit.Y;
            for (int r = 0; r <= 8; r++)
            for (int oy = -r; oy <= r; oy++)
            for (int ox = -r; ox <= r; ox++)
            {
                if (Math.Abs(ox) != r && Math.Abs(oy) != r) continue;
                int nx = x + ox, ny = y + oy;
                if (!World.IsWalkable(nx, ny)) continue;
                TileData tile = World.Cells[World.Index(nx, ny)];
                if (tile.Vegetation < .12f || tile.Fire > .1f || !SetTarget(unit, nx, ny)) continue;
                ChangeTask(unit, UnitTask.Eat); unit.WorkTimer = 1.2f; return true;
            }
            return false;
        }

        private bool ChooseRest(UnitData unit, SettlementData home)
        {
            if (home != null)
            {
                int attempts = 0;
                for (int i = 0; i < World.Buildings.Count && attempts < 3; i++)
                {
                    BuildingData house = World.Buildings[i];
                    if (house.SettlementId != home.Id || (house.Type != BuildingType.House && house.Type != BuildingType.Hall) || house.Health <= 0 || World.Cells[World.Index(house.X, house.Y)].Fire > 0) continue;
                    attempts++;
                    if (SetTarget(unit, house.X, house.Y)) { ChangeTask(unit, UnitTask.Rest); return true; }
                }
            }
            int x = (int)unit.X, y = (int)unit.Y;
            if (World.Cells[World.Index(x, y)].Fire > 0 || NearbyFire(x, y) > .15f) return false;
            ReleaseReservation(unit); ClearRoute(unit); unit.TargetCell = World.Index(x, y);
            ChangeTask(unit, UnitTask.Rest); return true;
        }

        private bool ChooseGather(UnitData unit)
        {
            if (unit.CarriedWood >= 2.8f) return false;
            if (unit.Task == UnitTask.Gather && unit.TargetCell >= 0 && World.Cells[unit.TargetCell].Vegetation > .02f && World.Cells[unit.TargetCell].Fire == 0) return true;
            int resource = FindResource(unit, 12);
            if (resource < 0 || !SetTarget(unit, resource % World.Width, resource / World.Width)) return false;
            ChangeTask(unit, UnitTask.Gather); reservations[resource] = unit.Id; return true;
        }

        private bool ChooseSocial(UnitData unit, SettlementData home)
        {
            int x = (int)unit.X, y = (int)unit.Y;
            UnitData partner = null; float closest = 36;
            for (int cy = Math.Max(0, y - 5); cy <= Math.Min(World.Height - 1, y + 5); cy++)
            for (int cx = Math.Max(0, x - 5); cx <= Math.Min(World.Width - 1, x + 5); cx++)
            for (int n = spatialHeads[World.Index(cx, cy)]; n >= 0; n = spatialNext[n])
            {
                UnitData candidate = World.Units[n];
                if (candidate.Id == unit.Id || candidate.SettlementId != home.Id || candidate.Health <= 0 || candidate.Hunger > .7f || (candidate.Task != UnitTask.Idle && candidate.Task != UnitTask.Rest && candidate.Task != UnitTask.Socialize && candidate.Task != UnitTask.Wander)) continue;
                float distance = DistanceSquared(unit.X, unit.Y, candidate.X, candidate.Y);
                if (distance < closest) { closest = distance; partner = candidate; }
            }
            if (partner == null || !SetTarget(unit, (int)partner.X, (int)partner.Y)) return false;
            ChangeTask(unit, UnitTask.Socialize); unit.TargetId = partner.Id; unit.WorkTimer = 3; return true;
        }

        private bool PerformNeedsActivity(UnitData unit, SettlementData home, float dt)
        {
            if (unit.Task != UnitTask.Eat && unit.Task != UnitTask.Rest && unit.Task != UnitTask.Socialize) return false;
            if (unit.TargetCell >= 0 && DistanceSquared(unit.X, unit.Y, unit.TargetCell % World.Width + .5f, unit.TargetCell / World.Width + .5f) > 2)
            { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return true; }
            if (unit.Task == UnitTask.Rest)
            {
                unit.Fatigue = Math.Max(0, unit.Fatigue - dt * .065f);
                if (unit.Hunger < .5f && !unit.Infected) unit.Health = Math.Min(unit.MaxHealth, unit.Health + dt * .2f);
                if (unit.Fatigue <= .18f) { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; }
                return true;
            }
            if (unit.Task == UnitTask.Socialize)
            {
                UnitData partner = World.GetUnit(unit.TargetId);
                if (partner == null || partner.Health <= 0 || DistanceSquared(unit.X, unit.Y, partner.X, partner.Y) > 4 || partner.Task == UnitTask.Flee || partner.Task == UnitTask.Fight)
                { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 1; return true; }
                unit.SocialNeed = Math.Max(0, unit.SocialNeed - dt * .18f);
                partner.SocialNeed = Math.Max(0, partner.SocialNeed - dt * .08f);
                unit.WorkTimer = Math.Max(0, unit.WorkTimer - dt);
                if (unit.WorkTimer <= 0) { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; }
                return true;
            }
            unit.WorkTimer = Math.Max(0, unit.WorkTimer - dt);
            if (unit.WorkTimer > 0) return true;
            bool consumed = false;
            if (unit.CarriedFood >= 1) { unit.CarriedFood -= 1; consumed = true; }
            else if (home != null && home.Food >= 1 && DistanceSquared(unit.X, unit.Y, home.X + .5f, home.Y + .5f) < 2) { home.Food -= 1; consumed = true; }
            else
            {
                int cell = World.Index((int)unit.X, (int)unit.Y);
                if (World.Cells[cell].Vegetation >= .12f) { World.Cells[cell].Vegetation -= .08f; consumed = true; World.TerrainRevision++; }
            }
            if (consumed) { unit.Hunger = Math.Max(0, unit.Hunger - .4f); unit.Health = Math.Min(unit.MaxHealth, unit.Health + 2); }
            ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0;
            return true;
        }
    }
}
