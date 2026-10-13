using System;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private static bool Tameable(SpeciesType species)
        { return species == SpeciesType.Sheep || species == SpeciesType.Goat || species == SpeciesType.Cow || species == SpeciesType.Chicken || species == SpeciesType.Donkey || species == SpeciesType.Horse || species == SpeciesType.SpiritStag; }
        private int PasturePopulation(int pastureId)
        {
            int count = 0; for (int i = 0; i < World.Units.Count; i++) if (World.Units[i].Health > 0 && World.Units[i].IsDomestic && World.Units[i].PastureId == pastureId) count++; return count;
        }
        private bool ChooseHusbandry(UnitData u, SettlementData s)
        {
            if (!World.HasTechnology(s.Id, "f07_husbandry")) return false;
            for (int p = 0; p < World.Buildings.Count; p++)
            {
                BuildingData pasture = World.Buildings[p];
                if (pasture.Type != BuildingType.Pasture || !UsableBuilding(pasture, s.Id)) continue;
                int herd = PasturePopulation(pasture.Id);
                bool rareHerd = false;
                for (int n = 0; n < World.Units.Count; n++) if (World.Units[n].PastureId == pasture.Id && World.Units[n].Species == SpeciesType.SpiritStag) rareHerd = true;
                bool feedNeeded = (pasture.AgriculturalStock[(int)AgriculturalProduct.Hay] < 4 && World.GetAgriculturalStock(s.Id, AgriculturalProduct.Hay) > pasture.AgriculturalStock[(int)AgriculturalProduct.Hay]) || (rareHerd && pasture.AgriculturalStock[(int)AgriculturalProduct.Berries] < 10 && World.GetAgriculturalStock(s.Id, AgriculturalProduct.Berries) > pasture.AgriculturalStock[(int)AgriculturalProduct.Berries]);
                if (herd > 0 && feedNeeded && SetTarget(u, pasture.X, pasture.Y)) { ChangeTask(u, UnitTask.Tend); u.TargetId = 0; u.TargetBuildingId = pasture.Id; u.Profession = UnitProfession.Herder; return true; }
                if (herd >= 4 + 2 * pasture.Level) continue;
                for (int a = 0; a < World.Units.Count; a++)
                {
                    UnitData animal = World.Units[a];
                    if (animal.Health <= 0 || animal.IsDomestic || !Tameable(animal.Species) || DistanceSquared(animal.X, animal.Y, pasture.X, pasture.Y) > 24 * 24) continue;
                    bool rare = animal.Species == SpeciesType.SpiritStag; float amount = rare ? 10 : 1;
                    if (CarryCapacityFor(u) < amount || (rare && !World.HasTechnology(s.Id, "k08_apprenticeship"))) continue;
                    AgriculturalProduct feed = rare ? AgriculturalProduct.Berries : AgriculturalProduct.Hay;
                    for (int b = 0; b < World.Buildings.Count; b++)
                    {
                        BuildingData stock = World.Buildings[b];
                        if (!UsableBuilding(stock, s.Id) || stock.AgriculturalStock[(int)feed] < amount || !SetTarget(u, stock.X, stock.Y)) continue;
                        ChangeTask(u, UnitTask.Tend); u.TargetId = animal.Id; u.TargetBuildingId = pasture.Id; u.HusbandrySourceId = stock.Id; u.Profession = UnitProfession.Herder; return true;
                    }
                }
            }
            return false;
        }
        public bool TryTameAnimal(int unitId, int animalId, int pastureId)
        {
            UnitData u = World.GetUnit(unitId), a = World.GetUnit(animalId); BuildingData p = World.GetBuilding(pastureId);
            if (u == null || a == null || p == null || u.Age < 16 || !IsCivilized(u.Species) || !Tameable(a.Species) || a.Health <= 0 || a.IsDomestic || p.Type != BuildingType.Pasture || !UsableBuilding(p, u.SettlementId) || PasturePopulation(p.Id) >= 4 + 2 * p.Level || DistanceSquared(u.X, u.Y, a.X, a.Y) > 2 || !World.HasTechnology(u.SettlementId, "f07_husbandry")) return false;
            bool rare = a.Species == SpeciesType.SpiritStag; float amount = rare ? 10 : 1; AgriculturalProduct feed = rare ? AgriculturalProduct.Berries : AgriculturalProduct.Hay;
            if ((rare && !World.HasTechnology(u.SettlementId, "k08_apprenticeship")) || u.HarvestProduct != feed || u.HarvestAmount < amount) return false;
            u.HarvestAmount -= amount; if (u.HarvestAmount <= .00001f) { u.HarvestAmount = 0; u.HarvestProduct = AgriculturalProduct.None; }
            a.IsDomestic = true; a.AnimalOwnerId = u.SettlementId; a.PastureId = p.Id; a.PackId = a.Id; a.PackLeaderId = a.Id; a.BreedingSeconds = 0;
            a.Hunger = Math.Max(0, a.Hunger - .4f); p.HerdSpecies = (int)a.Species;
            ChangeTask(a, UnitTask.Idle); a.DecisionTimer = 0; GainCraftExperience(u, 6);
            AddEvent(EventKind.Built, a.X, a.Y, World.GetSettlement(u.SettlementId).Name + ": приручён «" + WildlifeCatalog.Name(a.Species) + "»"); return true;
        }
        private bool PerformHusbandry(UnitData u, SettlementData s, float dt)
        {
            UnitData animal = World.GetUnit(u.TargetId); BuildingData pasture = World.GetBuilding(u.TargetBuildingId);
            if (u.TargetId == 0 && pasture != null && pasture.Type == BuildingType.Pasture && UsableBuilding(pasture, s.Id) && u.Age >= 16)
            {
                bool feedRareHerd = false; for (int i = 0; i < World.Units.Count; i++) if (World.Units[i].PastureId == pasture.Id && World.Units[i].Species == SpeciesType.SpiritStag) feedRareHerd = true;
                if (SupplyAgriculturalWorkshop(u, s, pasture, 0, 0, 0, 0, 0, 0, 0, 4, feedRareHerd ? 10 : 0)) { GainCraftExperience(u, 2); ChangeTask(u, UnitTask.Idle); }
                return true;
            }
            if (animal == null || animal.IsDomestic || animal.Health <= 0 || pasture == null || !UsableBuilding(pasture, s.Id) || u.Age < 16) { ChangeTask(u, UnitTask.Idle); return true; }
            bool rare = animal.Species == SpeciesType.SpiritStag; AgriculturalProduct feed = rare ? AgriculturalProduct.Berries : AgriculturalProduct.Hay; float amount = rare ? 10 : 1;
            if (u.HarvestAmount == 0)
            {
                BuildingData stock = World.GetBuilding(u.HusbandrySourceId);
                if (stock == null || !UsableBuilding(stock, s.Id) || stock.AgriculturalStock[(int)feed] < amount || CarrySpace(u) < amount || DistanceSquared(u.X, u.Y, stock.X + .5f, stock.Y + .5f) > 2) { ChangeTask(u, UnitTask.Idle); return true; }
                stock.AgriculturalStock[(int)feed] -= amount; u.HarvestProduct = feed; u.HarvestAmount = amount;
            }
            if (DistanceSquared(u.X, u.Y, animal.X, animal.Y) <= 2)
            {
                u.WorkTimer += dt;
                if (u.WorkTimer >= 3) { TryTameAnimal(u.Id, animal.Id, pasture.Id); ChangeTask(u, UnitTask.Idle); u.DecisionTimer = 0; }
            }
            else if (!SetTarget(u, (int)animal.X, (int)animal.Y)) { ChangeTask(u, UnitTask.Idle); u.DecisionTimer = 0; }
            return true;
        }
        public bool TryMountNearby(UnitData u)
        {
            if (u == null || u.Age < 16 || !IsCivilized(u.Species) || u.MountId != 0 || !World.HasTechnology(u.SettlementId, "f07_husbandry")) return false;
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData a = World.Units[i];
                if (!a.IsDomestic || a.Health <= 0 || a.Age < 2 || a.AnimalOwnerId != u.SettlementId || a.HandlerId != 0 || (a.Species != SpeciesType.Donkey && a.Species != SpeciesType.Horse && a.Species != SpeciesType.SpiritStag) || DistanceSquared(a.X, a.Y, u.X, u.Y) > 2) continue;
                a.HandlerId = u.Id; u.MountId = a.Id; u.MountSpecies = a.Species; u.MountBags = a.HasSaddleBags; u.HasWagon = u.OwnsWagon && a.Species != SpeciesType.SpiritStag; return true;
            }
            return false;
        }
        private void UpdateDomesticAnimals()
        {
            CleanupDomesticLinks();
            int originalCount = World.Units.Count;
            for (int i = 0; i < originalCount; i++)
            {
                UnitData a = World.Units[i]; if (!a.IsDomestic || a.Health <= 0) continue;
                BuildingData p = World.GetBuilding(a.PastureId); SettlementData s = World.GetSettlement(a.AnimalOwnerId);
                if (p == null || p.Health <= 0 || s == null || s.Population <= 0)
                { a.IsDomestic = false; a.PastureId = a.AnimalOwnerId = a.HandlerId = 0; continue; }
                if(!PlacementRules.IsDryGround(World,p.X,p.Y))continue;
                if (a.HandlerId != 0 || DistanceSquared(a.X, a.Y, p.X + .5f, p.Y + .5f) > 16 || a.Age < WildlifeCatalog.MaturityAge(a.Species)) continue;
                a.AnimalProduceTimer = Math.Min(30, a.AnimalProduceTimer + 1);
                if (a.AnimalProduceTimer >= 30 && AgriStorageSpace(p) >= 1 && p.AgriculturalStock[(int)AgriculturalProduct.Hay] >= 1)
                {
                    p.AgriculturalStock[(int)AgriculturalProduct.Hay] -= 1;
                    a.AnimalProduceTimer = 0; a.Hunger = Math.Max(0, a.Hunger - .4f);
                    AgriculturalProduct output = a.Species == SpeciesType.Chicken ? AgriculturalProduct.Egg : (a.Species == SpeciesType.Cow || a.Species == SpeciesType.Goat) && a.Sex == Sex.Female ? AgriculturalProduct.Milk : AgriculturalProduct.None;
                    if (output != AgriculturalProduct.None) p.AgriculturalStock[(int)output] += 1;
                }
                if (!World.Laws.Births || a.Sex != Sex.Female || a.Hunger > .5f || a.Health < a.MaxHealth * .7f || PasturePopulation(p.Id) >= 4 + 2 * p.Level) continue;
                float interval=WildlifeCatalog.BreedingInterval(a.Species,true);
                a.BreedingSeconds=Math.Min(interval,a.BreedingSeconds+1);if(a.BreedingSeconds<interval||!BreedingHealthy(a)||World.Units.Count>=9999)continue;
                UnitData father=null;
                for (int m = 0; m < originalCount; m++) { UnitData other = World.Units[m]; if (other.IsDomestic && other.PastureId == p.Id && other.Species == a.Species && other.Sex == Sex.Male && BreedingHealthy(other) && other.HandlerId==0 && DistanceSquared(a.X, a.Y, other.X, other.Y) < 25) { father=other; break; } }
                AgriculturalProduct feed = a.Species == SpeciesType.SpiritStag ? AgriculturalProduct.Berries : AgriculturalProduct.Hay;
                float feedCost = a.Species == SpeciesType.SpiritStag ? 10 : 2;
                if (father==null || !AnimalPairReachable(a,father) || !PlacementRules.IsDryGround(World,p.X,p.Y) || p.AgriculturalStock[(int)feed] < feedCost) continue;
                p.AgriculturalStock[(int)feed] -= feedCost;
                AddUnit(a.Species, (int)a.X, (int)a.Y, 0, true); UnitData baby = World.Units[World.Units.Count - 1]; baby.IsDomestic = true; baby.AnimalOwnerId = s.Id; baby.PastureId = p.Id; baby.PackId = baby.Id; baby.PackLeaderId = baby.Id; FinishAnimalBirth(a,father,true);
                AddEvent(EventKind.Birth, baby.X, baby.Y, "Пастбище: появился детёныш «" + WildlifeCatalog.Name(a.Species) + "»");
            }
        }
        private void CleanupDomesticLinks()
        {
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData a = World.Units[i]; if (!a.IsDomestic) continue;
                BuildingData p = World.GetBuilding(a.PastureId); SettlementData s = World.GetSettlement(a.AnimalOwnerId);
                if (p == null || p.Type != BuildingType.Pasture || p.Health <= 0 || s == null || s.Population <= 0 || p.SettlementId != s.Id)
                { a.IsDomestic = false; a.PastureId = a.AnimalOwnerId = a.HandlerId = 0; a.BreedingSeconds=Math.Min(WildlifeCatalog.BreedingInterval(a.Species,false),a.BreedingSeconds); }
                else if (a.HandlerId != 0) { UnitData u = World.GetUnit(a.HandlerId); if (u == null || u.Health <= 0 || u.MountId != a.Id) a.HandlerId = 0; }
            }
            for (int i = 0; i < World.Units.Count; i++) EnforceEquipment(World.Units[i]);
        }
    }
}
