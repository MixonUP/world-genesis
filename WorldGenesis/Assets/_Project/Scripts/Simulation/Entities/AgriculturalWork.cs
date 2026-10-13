using System;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private bool EquipmentRecipe(UnitData u, SettlementData s, int order, out float plank, out float stone, out float fiber, out float hide)
        {
            plank = stone = fiber = hide = 0;
            if (u == null || s == null || u.Age < 16 || !IsCivilized(u.Species)) return false;
            switch (order)
            {
                case 1: fiber = 3; return u.BasketLevel == 0;
                case 2: fiber = 4; return u.BasketLevel == 1 && World.HasTechnology(s.Id, "t01_flax");
                case 3: fiber = 6; hide = 2; plank = 2; return u.BasketLevel == 2 && World.HasTechnology(s.Id, "t02_linen");
                case 4: plank = 2; stone = 1; return u.HuntingToolLevel == 0 && World.HasTechnology(s.Id, "m02_carpentry");
                case 5: plank = 12; fiber = 4; hide = 2; return !u.OwnsWagon && World.HasTechnology(s.Id, "l02_carts");
                case 6: fiber = 2; hide = 2; UnitData mount = World.GetUnit(u.MountId); return mount != null && mount.IsDomestic && mount.HandlerId == u.Id && !mount.HasSaddleBags && World.HasTechnology(s.Id, "f07_husbandry");
                case 7: case 8: case 9: case 10: return RodRecipe(u, s, order, out plank, out fiber, out hide);
                default: return false;
            }
        }
        private bool CanCraftEquipment(UnitData u, SettlementData s, int order)
        {
            return EquipmentRecipe(u, s, order, out float p, out float r, out float f, out float h) && CraftingPlanksAvailable(s) >= p && FreePublicStock(s.Id, ItemType.RawStone) >= r && World.GetAgriculturalStock(s.Id, AgriculturalProduct.Fiber) >= f && World.GetAgriculturalStock(s.Id, AgriculturalProduct.Hide) >= h && World.GetAgriculturalStock(s.Id, AgriculturalProduct.RiverPearl) >= RodPearls(order) && World.GetAgriculturalStock(s.Id, AgriculturalProduct.AncientScale) >= RodScales(order);
        }
        // A work order spends nothing until on-site completion. Competing workers recheck the full recipe atomically.
        public bool TryStartEquipment(int unitId, int order)
        {
            UnitData u = World.GetUnit(unitId); SettlementData s = u == null ? null : World.GetSettlement(u.SettlementId);
            if (!CanCraftEquipment(u, s, order) || CarriedLoad(u) > .0001f) return false;
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData b = World.Buildings[i];
                if (b.Type != (order == 1 ? BuildingType.Hall : BuildingType.Sawmill) || !UsableBuilding(b, s.Id) || !SetTarget(u, b.X, b.Y)) continue;
                ChangeTask(u, UnitTask.CraftEquipment); u.EquipmentOrder = order; u.EquipmentProgress = 0; u.TargetBuildingId = b.Id;
                u.Profession = UnitProfession.Carpenter; return true;
            }
            return false;
        }
        private bool ChooseAgriculturalWork(UnitData u, SettlementData s)
        {
            if (u.Age < 16 || !IsCivilized(u.Species) || CarriedLoad(u) > .0001f) return false;
            if (ChoosePastureProduce(u, s)) return true;
            if (u.FishCaughtCount >= 3 && u.Hunger < .5f && u.Fatigue < .72f && TryStartFishingRod(u.Id)) return true;
            bool equipmentTime = u.BasketLevel == 0 ? u.Hunger < .5f && u.Fatigue < .72f : s.Food > s.Population;
            if (equipmentTime && u.BasketLevel < 3 && TryStartEquipment(u.Id, u.BasketLevel + 1)) return true;
            if (u.Id % 8 == 0 && u.HuntingToolLevel == 0 && TryStartEquipment(u.Id, 4)) return true;
            if (u.MountId == 0 && u.Id % 5 == 0 && TryMountNearby(u)) { EnforceEquipment(u); }
            if (u.MountId != 0 && !u.OwnsWagon && u.Id % 3 == 0 && TryStartEquipment(u.Id, 5)) return true;
            if (u.MountId != 0 && !u.MountBags && TryStartEquipment(u.Id, 6)) return true;
            if (u.Id % 7 == 0 && ChooseHusbandry(u, s)) return true;
            if (u.Id % 9 == 0 && World.HasTechnology(s.Id, "f08_brewing") && s.Food > s.Population * 2)
            {
                for (int i = 0; i < World.Buildings.Count; i++)
                {
                    BuildingData b = World.Buildings[i];
                    if (b.Type == BuildingType.Brewery && UsableBuilding(b, s.Id) && World.GetAgriculturalStock(s.Id, AgriculturalProduct.Ale) < 12 && CanBrew(s, b) && SetTarget(u, b.X, b.Y)) { ChangeTask(u, UnitTask.Brew); u.TargetBuildingId = b.Id; return true; }
                }
            }
            // Occasional adults choose leisure at an actual stocked tavern/workshop. Children never consume alcohol.
            if (u.SocialNeed > .5f && u.Intoxication < .12f && u.Id % 4 == 0)
                for (int i = 0; i < World.Buildings.Count; i++)
                {
                    BuildingData b = World.Buildings[i];
                    if (b.Type != BuildingType.Brewery || !UsableBuilding(b, s.Id) || b.AgriculturalStock[(int)AgriculturalProduct.Ale] < 1) continue;
                    if (DistanceSquared(u.X, u.Y, b.X + .5f, b.Y + .5f) < 2)
                    { b.AgriculturalStock[(int)AgriculturalProduct.Ale]--; u.Intoxication = Math.Min(1, u.Intoxication + .35f); u.AlcoholHabit = Math.Min(1, u.AlcoholHabit + .08f); u.SocialNeed = Math.Max(0, u.SocialNeed - .4f); u.EventStress = Math.Max(0, u.EventStress - .08f); return false; }
                    if (SetTarget(u, b.X, b.Y)) { ChangeTask(u, UnitTask.Wander); return true; }
                }
            return false;
        }
        private bool PerformAgriculturalWork(UnitData u, SettlementData s, float dt)
        {
            if (u.Task == UnitTask.Tend) return PerformHusbandry(u, s, dt);
            if (u.Task != UnitTask.CraftEquipment && u.Task != UnitTask.Brew) return false;
            BuildingData b = World.GetBuilding(u.TargetBuildingId);
            if (u.Age < 16 || b == null || !UsableBuilding(b, s.Id)) { ChangeTask(u, UnitTask.Idle); return true; }
            if (u.Task == UnitTask.Brew)
            {
                if (b.Type != BuildingType.Brewery || !World.HasTechnology(s.Id, "f08_brewing")) { ChangeTask(u, UnitTask.Idle); return true; }
                if (!SupplyAgriculturalWorkshop(u, s, b, 0, 0, 0, 0, 2, 1, 1)) return true;
                if (AgriStorageSpace(b) < 2) { ChangeTask(u, UnitTask.Idle); return true; }
                u.WorkTimer += dt; b.BrewingProgress = Math.Min(12, u.WorkTimer);
                if (u.WorkTimer < 12) return true;
                b.BrewWaterStored = Math.Max(0, b.BrewWaterStored - 1);
                b.AgriculturalStock[(int)AgriculturalProduct.Grain] = Math.Max(0, b.AgriculturalStock[(int)AgriculturalProduct.Grain] - 2); b.Inventory[(int)ItemType.Firewood] = Math.Max(0, b.Inventory[(int)ItemType.Firewood] - 1);
                b.AgriculturalStock[(int)AgriculturalProduct.Ale] += 2; b.BrewingProgress = 0; GainCraftExperience(u, 12); ChangeTask(u, UnitTask.Idle); return true;
            }
            int order = u.EquipmentOrder;
            if (b.Type != (order == 1 ? BuildingType.Hall : BuildingType.Sawmill) || !EquipmentRecipe(u, s, order, out float plank, out float stone, out float fiber, out float hide)) { u.EquipmentOrder = 0; ChangeTask(u, UnitTask.Idle); return true; }
            if (!SupplyAgriculturalWorkshop(u, s, b, plank, stone, fiber, hide, 0, 0, 0, 0, 0, RodPearls(order), RodScales(order))) return true;
            float seconds = EquipmentSeconds(order);
            u.EquipmentProgress = Math.Min(seconds, u.EquipmentProgress + dt);
            if (u.EquipmentProgress < seconds) return true;
            b.Inventory[(int)ItemType.Plank] = Math.Max(0, b.Inventory[(int)ItemType.Plank] - plank); b.Inventory[(int)ItemType.RawStone] = Math.Max(0, b.Inventory[(int)ItemType.RawStone] - stone); b.AgriculturalStock[(int)AgriculturalProduct.Fiber] = Math.Max(0, b.AgriculturalStock[(int)AgriculturalProduct.Fiber] - fiber); b.AgriculturalStock[(int)AgriculturalProduct.Hide] = Math.Max(0, b.AgriculturalStock[(int)AgriculturalProduct.Hide] - hide);
            b.AgriculturalStock[(int)AgriculturalProduct.RiverPearl] = Math.Max(0, b.AgriculturalStock[(int)AgriculturalProduct.RiverPearl] - RodPearls(order));
            b.AgriculturalStock[(int)AgriculturalProduct.AncientScale] = Math.Max(0, b.AgriculturalStock[(int)AgriculturalProduct.AncientScale] - RodScales(order));
            if (order >= 7) u.FishingRodLevel = order - 5;
            else if (order <= 3) u.BasketLevel = order;
            else if (order == 4) { u.HuntingToolLevel = 1; u.Profession = UnitProfession.Hunter; }
            else if (order == 5) u.OwnsWagon = true;
            else if (order == 6) { World.GetUnit(u.MountId).HasSaddleBags = true; u.MountBags = true; }
            GainCraftExperience(u, seconds); u.EquipmentOrder = 0; u.EquipmentProgress = 0; EnforceEquipment(u); ChangeTask(u, UnitTask.Idle); u.DecisionTimer = 0; return true;
        }
        private BuildingData BrewingWell(SettlementData s, BuildingData brewery)
        {
            for (int i = 0; i < World.Buildings.Count; i++) { BuildingData b = World.Buildings[i]; if (b.Type == BuildingType.Well && UsableBuilding(b, s.Id) && b.WaterStored >= 1 && DistanceSquared(b.X, b.Y, brewery.X, brewery.Y) <= 36) return b; }
            return null;
        }
        private bool ChoosePastureProduce(UnitData u, SettlementData s)
        {
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData b = World.Buildings[i]; if (b.Type != BuildingType.Pasture || !UsableBuilding(b, s.Id)) continue;
                AgriculturalProduct product = b.AgriculturalStock[(int)AgriculturalProduct.Milk] >= .01f ? AgriculturalProduct.Milk : AgriculturalProduct.Egg;
                if (b.AgriculturalStock[(int)product] < .01f) continue;
                if (DistanceSquared(u.X, u.Y, b.X + .5f, b.Y + .5f) <= 2)
                {
                    float amount = Math.Min(CarrySpace(u), b.AgriculturalStock[(int)product]); b.AgriculturalStock[(int)product] -= amount; u.HarvestAmount = amount; u.HarvestProduct = product;
                    return ChooseHarvestDelivery(u, s);
                }
                if (SetTarget(u, b.X, b.Y)) { ChangeTask(u, UnitTask.Wander); return true; }
            }
            return false;
        }
        private bool CanBrew(SettlementData s, BuildingData b)
        {
            return World.GetAgriculturalStock(s.Id, AgriculturalProduct.Grain) >= 2 && FreePublicStock(s.Id, ItemType.Firewood) >= 1 && AgriStorageSpace(b) >= 2 && (b.BrewWaterStored >= 1 || BrewingWell(s, b) != null);
        }
    }
}
