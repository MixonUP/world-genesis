using System;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private static readonly string[] FemaleNames = { "Лиора", "Мира", "Нора", "Тара", "Селия", "Илла", "Эрин", "Айна" };
        private static readonly string[] MaleNames = { "Торин", "Арен", "Горон", "Ильмар", "Орин", "Борн", "Дарен", "Ронан" };

        private void InitializeDemography(UnitData unit)
        {
            unit.Sex = (Sex)(StableRandom.Hash(World.Seed, unit.Id, 0, 6000) % 2);
            unit.Prudence = StableRandom.Coordinate(World.Seed, unit.Id, 0, 6001);
            unit.Ambition = StableRandom.Coordinate(World.Seed, unit.Id, 0, 6002);
            unit.Generosity = StableRandom.Coordinate(World.Seed, unit.Id, 0, 6003);
            string[] names = unit.Sex == Sex.Female ? FemaleNames : MaleNames;
            unit.Name = names[StableRandom.Hash(World.Seed, unit.Id, 0, 6004) % (uint)names.Length];
        }
        private void MigrateDemography()
        {
            if (World.DemographyVersion == 1) return;
            for (int i = 0; i < World.Units.Count; i++) { InitializeDemography(World.Units[i]); World.Units[i].Education = 0; }
            World.DemographyVersion = 1; World.Revision++;
        }
        public static float CarriedLoad(UnitData unit) { return unit.CargoAmount + unit.CarriedFood + unit.CarriedWood; }
        private static float CarrySpace(UnitData unit) { return Math.Max(0, ResourceCatalog.CarryCapacity - CarriedLoad(unit)); }
        private static bool LightWorkAllowed(UnitData unit, SettlementData home) { return unit.Age >= 12 && unit.Age < 16 && home != null && home.ChildLabor == ChildLaborPolicy.LightWork; }

        private void PutOnGround(UnitData unit, ItemType item, float amount, bool food)
        {
            if (amount <= 0) return;
            int owner = unit.Age >= 16 ? unit.Id : 0;
            for (int i = 0; i < World.GroundStocks.Count; i++)
            {
                GroundStockData g = World.GroundStocks[i];
                if (g.OwnerId == owner && g.Item == item && g.IsFood == food && g.X == unit.X && g.Y == unit.Y) { g.Amount += amount; return; }
            }
            World.GroundStocks.Add(new GroundStockData { OwnerId = owner, Item = item, Amount = amount, X = unit.X, Y = unit.Y, IsFood = food });
        }
        private void MigrateCarrying(bool legacy)
        {
            if (!legacy) return;
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData u = World.Units[i]; float space = ResourceCatalog.CarryCapacity;
                float held = Math.Min(space, u.CargoAmount); PutOnGround(u, u.CargoKind, u.CargoAmount - held, false); u.CargoAmount = held; space -= held;
                held = Math.Min(space, u.CarriedFood); PutOnGround(u, ItemType.None, u.CarriedFood - held, true); u.CarriedFood = held; space -= held;
                held = Math.Min(space, u.CarriedWood); PutOnGround(u, ItemType.Plank, u.CarriedWood - held, false); u.CarriedWood = held;
                if (u.CargoAmount == 0) u.CargoKind = ItemType.None;
                u.CargoOwnerId = 0; u.GroundStockIndex = -1;
            }
            World.ResourceVersion = 2; World.ResourceRevision++; World.Revision++;
        }
        private void DropCarrying(UnitData u)
        {
            PutOnGround(u, u.CargoKind, u.CargoAmount, false); PutOnGround(u, ItemType.None, u.CarriedFood, true); PutOnGround(u, ItemType.Plank, u.CarriedWood, false);
            u.CargoAmount = u.CarriedFood = u.CarriedWood = 0; u.CargoKind = ItemType.None; u.CargoOwnerId = 0;
            World.ResourceRevision++;
        }
        private void EnforceChildWorkLaw(UnitData unit, SettlementData home)
        {
            if (!IsCivilized(unit.Species) || unit.Age >= 16) return;
            unit.Profession = UnitProfession.None;
            bool light = LightWorkAllowed(unit, home);
            bool heavy = unit.CargoAmount > 0 && unit.CargoKind != ItemType.Firewood;
            bool forbiddenHaul = unit.Task == UnitTask.Haul && unit.PickupKind != ItemType.Firewood;
            bool forbidden = heavy || forbiddenHaul || unit.Task == UnitTask.Gather || unit.Task == UnitTask.Process || unit.Task == UnitTask.Fight || unit.Task == UnitTask.Patrol || (!light && (unit.Task == UnitTask.Farm || unit.Task == UnitTask.Haul || unit.Task == UnitTask.Return));
            if ((!light || heavy) && CarriedLoad(unit) > 0) DropCarrying(unit);
            if (forbidden) { ChangeTask(unit, UnitTask.Idle); ClearRoute(unit); unit.GroundStockIndex = -1; unit.DecisionTimer = 0; }
        }
        private bool ChooseGroundStock(UnitData unit)
        {
            if (CarriedLoad(unit) > 0 || unit.Age < 16) return false;
            for (int i = 0; i < World.GroundStocks.Count; i++)
            {
                GroundStockData g = World.GroundStocks[i];
                if (g.Amount <= .000001f || (g.OwnerId != 0 && g.OwnerId != unit.Id) || DistanceSquared(unit.X, unit.Y, g.X, g.Y) > 18 * 18 || !SetTarget(unit, (int)g.X, (int)g.Y)) continue;
                ChangeTask(unit, UnitTask.Haul); unit.GroundStockIndex = i; unit.SourceBuildingId = unit.TargetBuildingId = 0; return true;
            }
            return false;
        }
        private bool PickUpGroundStock(UnitData unit)
        {
            if (unit.Task != UnitTask.Haul || unit.GroundStockIndex < 0) return false;
            int index = unit.GroundStockIndex;
            if (index < World.GroundStocks.Count)
            {
                GroundStockData g = World.GroundStocks[index];
                if ((g.OwnerId == 0 || g.OwnerId == unit.Id) && DistanceSquared(unit.X, unit.Y, g.X, g.Y) <= 2 && unit.Age >= 16)
                {
                    float amount = Math.Min(CarrySpace(unit), g.Amount);
                    if (g.IsFood) unit.CarriedFood += amount;
                    else if (g.Item == ItemType.Plank) unit.CarriedWood += amount;
                    else { unit.CargoKind = g.Item; unit.CargoAmount += amount; unit.CargoOwnerId = 0; }
                    g.Amount -= amount; World.ResourceRevision++;
                }
            }
            unit.GroundStockIndex = -1; ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return true;
        }
        private bool ChooseChildActivity(UnitData unit, SettlementData home)
        {
            if (home == null) return false;
            bool learn = (StableRandom.Hash(World.Seed, unit.Id, (int)(World.Tick / 80), 6200) & 1) == 0;
            int x = home.X, y = home.Y;
            if (!learn) { x += (int)(StableRandom.Hash(World.Seed, unit.Id, (int)(World.Tick / 80), 6201) % 7) - 3; y += (int)(StableRandom.Hash(World.Seed, unit.Id, (int)(World.Tick / 80), 6202) % 7) - 3; }
            if (!SetTarget(unit, x, y)) return false;
            ChangeTask(unit, learn ? UnitTask.Learn : UnitTask.Play); unit.WorkTimer = 4; return true;
        }
        private bool PerformChildActivity(UnitData unit, SettlementData home, float dt)
        {
            if (unit.Task != UnitTask.Play && unit.Task != UnitTask.Learn) return false;
            if (unit.Age >= 16 || home == null) { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return true; }
            unit.WorkTimer = Math.Max(0, unit.WorkTimer - dt);
            unit.SocialNeed = Math.Max(0, unit.SocialNeed - dt * .03f);
            if (unit.Task == UnitTask.Learn)
                for (int i = 0; i < World.Units.Count; i++)
                {
                    UnitData mentor = World.Units[i];
                    if (mentor.Age >= 16 && mentor.Health > 0 && mentor.SettlementId == home.Id && mentor.Task != UnitTask.Fight && mentor.Task != UnitTask.Flee && DistanceSquared(unit.X, unit.Y, mentor.X, mentor.Y) < 9) { unit.Education = Math.Min(1, unit.Education + dt * .002f); break; }
                }
            if (unit.WorkTimer <= 0) { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; }
            return true;
        }
    }
}
