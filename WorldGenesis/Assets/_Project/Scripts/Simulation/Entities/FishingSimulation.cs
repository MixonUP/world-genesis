using System;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private void MigrateFishing()
        {
            if (World.FishingVersion == 1) return;
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData u = World.Units[i];
                u.FishingRodLevel = u.Age >= 16 && IsCivilized(u.Species) ? 1 : 0;
            }
            World.FishingVersion = 1;
        }
        public bool TryStartFishing(int unitId, int waterCell)
        {
            UnitData unit = World.GetUnit(unitId);
            SettlementData home = unit == null ? null : World.GetSettlement(unit.SettlementId);
            if (unit == null || unit.Health <= 0 || home == null || FishingCatalog.RodLevel(unit) == 0 || unit.HarvestAmount > 0 || CarrySpace(unit) < .9999f || !FishingCatalog.IsWater(World, waterCell) || World.Infrastructure[waterCell].Fish < 1) return false;
            int x = waterCell % World.Width, y = waterCell / World.Width;
            // Prefer the current bank, then nearby walkable banks. Every catch must have a return route.
            for (int pass = 0; pass < 2; pass++) for (int d = 0; d < 4; d++)
            {
                int bx = x + Dx[d], by = y + Dy[d];
                if (!World.IsWalkable(bx, by) || World.Cells[World.Index(bx, by)].Fire > .1f) continue;
                bool here = (int)unit.X == bx && (int)unit.Y == by;
                if (pass == 0 && !here || pass == 1 && here) continue;
                bool canDeliver = false;
                for (int i = 0; i < World.Buildings.Count && !canDeliver; i++)
                {
                    BuildingData b = World.Buildings[i];
                    if ((b.Type != BuildingType.Hall && b.Type != BuildingType.Granary) || !UsableBuilding(b, home.Id) || StorageCapacity.FreeAgriculture(b, AgriculturalProduct.Fish) < 1) continue;
                    canDeliver = navigation.Find(World.Index(bx, by), World.Index(b.X, b.Y), scratchPath);
                }
                if (!canDeliver || !SetTarget(unit, bx, by)) continue;
                ChangeTask(unit, UnitTask.Fish); unit.ResourceCell = waterCell;
                unit.WorkTimer = FishingCatalog.CastSeconds(FishingCatalog.RodLevel(unit)); unit.DecisionTimer = 2;
                return true;
            }
            return false;
        }
        private bool ChooseFishing(UnitData unit, SettlementData home)
        {
            if (home == null || FishingCatalog.RodLevel(unit) == 0 || unit.HarvestAmount > 0 || CarrySpace(unit) < .9999f || (unit.Id & 1) != 0) return false;
            int level = FishingCatalog.RodLevel(unit);
            FishSpeciesId needed = level == 3 && World.GetAgriculturalStock(home.Id, AgriculturalProduct.RiverPearl) < .5f ? FishSpeciesId.PearlCarp :
                level == 4 && World.GetAgriculturalStock(home.Id, AgriculturalProduct.AncientScale) < .25f ? FishSpeciesId.MoonTrout : FishSpeciesId.None;
            if (home.Food >= home.Population * 4 && needed == FishSpeciesId.None && !(unit.FishCaughtCount > 0 && level >= 3 && World.GetAgriculturalStock(home.Id, AgriculturalProduct.DelicacyFish) < 4)) return false;
            int ux = (int)unit.X, uy = (int)unit.Y;
            // Seek a locally available habitat for the next tool material before a food-only bank.
            // This is a destination preference, never a guaranteed rare catch or a global expedition.
            for (int pass = needed == FishSpeciesId.None ? 1 : 0; pass < 2; pass++)
            for (int radius = 1; radius <= 10; radius++)
            for (int oy = -radius; oy <= radius; oy++) for (int ox = -radius; ox <= radius; ox++)
            {
                if (Math.Max(Math.Abs(ox), Math.Abs(oy)) != radius) continue;
                int x = ux + ox, y = uy + oy;
                if (!World.InBounds(x, y) || DistanceSquared(x, y, home.X, home.Y) > 18 * 18) continue;
                int cell = World.Index(x, y);
                if (pass == 0 && !FishingCatalog.Eligible(World, cell, level, needed)) continue;
                if (TryStartFishing(unit.Id, cell)) return true;
            }
            return false;
        }
        private bool PerformFishing(UnitData unit, SettlementData home, float dt)
        {
            if (unit.Task != UnitTask.Fish) return false;
            // The check precedes the countdown and random roll; interruptions create no catch or material.
            if (!FishingCatalog.IsActivelyFishing(World, unit))
            { ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return true; }
            unit.WorkTimer -= dt * (World.HasTechnology(unit.SettlementId, "f06_river_fishing") ? 1.15f : 1);
            if (unit.WorkTimer > 0) return true;
            int cell = unit.ResourceCell, level = FishingCatalog.RodLevel(unit); float total = 0;
            for (int i = 0; i < FishingCatalog.All.Length; i++) if (FishingCatalog.Eligible(World, cell, level, FishingCatalog.All[i].Id)) total += FishingCatalog.All[i].Weight;
            if (total <= 0) { ChangeTask(unit, UnitTask.Idle); return true; }
            float roll = RandomValue() * total;
            FishDefinition caught = FishingCatalog.All[0];
            for (int i = 0; i < FishingCatalog.All.Length; i++)
            {
                FishDefinition fish = FishingCatalog.All[i]; if (!FishingCatalog.Eligible(World, cell, level, fish.Id)) continue;
                roll -= fish.Weight; if (roll <= 0) { caught = fish; break; }
            }
            World.Infrastructure[cell].Fish = Math.Max(0, World.Infrastructure[cell].Fish - 1);
            unit.HarvestProduct = caught.Food; unit.HarvestAmount = 1;
            unit.FishSpeciesId = caught.Id; unit.FishCaughtCount = Math.Min(int.MaxValue - 1, unit.FishCaughtCount) + 1;
            // Byproducts remain physically on the bank, never in a remote warehouse or an overfilled hand.
            if (caught.PearlYield > 0) DropAgriculturalLoot(unit, AgriculturalProduct.RiverPearl, caught.PearlYield);
            if (caught.ScaleYield > 0) DropAgriculturalLoot(unit, AgriculturalProduct.AncientScale, caught.ScaleYield);
            GainCraftExperience(unit, FishingCatalog.CastSeconds(level));
            if (caught.Rarity >= FishRarity.Rare) AddEvent(EventKind.Built, unit.X, unit.Y, unit.Name + ": улов — " + caught.Name);
            ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0;
            ChooseHarvestDelivery(unit, home);
            return true;
        }

        public bool TryStartFishingRod(int unitId)
        {
            UnitData unit = World.GetUnit(unitId); int level = FishingCatalog.RodLevel(unit);
            return level >= 1 && level < 5 && TryStartEquipment(unitId, level + 6);
        }
        private bool RodRecipe(UnitData unit, SettlementData home, int order, out float plank, out float fiber, out float hide)
        {
            int grade = order - 5; plank = fiber = hide = 0;
            if (grade < 2 || grade > 5 || FishingCatalog.RodLevel(unit) != grade - 1 || !World.HasTechnology(home.Id, "f06_river_fishing")) return false;
            plank = grade; fiber = grade; hide = Math.Max(0, grade - 2);
            if (grade >= 3 && (!World.HasTechnology(home.Id, "m02_carpentry") || !World.HasTechnology(home.Id, "t02_linen"))) return false;
            if (grade >= 4 && !World.HasTechnology(home.Id, "k08_apprenticeship")) return false;
            return grade < 5 || World.HasTechnology(home.Id, "k02_schools");
        }
        private static float RodPearls(int order) { return order == 9 ? .5f : 0; }
        private static float RodScales(int order) { return order == 10 ? .25f : 0; }
        private static float EquipmentSeconds(int order) { return order >= 7 ? Math.Min(12, 6 + (order - 6) * 2) : 8; }
    }
}
