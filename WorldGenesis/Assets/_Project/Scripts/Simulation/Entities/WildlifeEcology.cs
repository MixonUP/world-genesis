using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private readonly Dictionary<int, int> packLeaders = new Dictionary<int, int>();
        private readonly int[] wildlifeCounts = new int[17];
        private void MigrateWildlife()
        {
            if(World.WildlifeVersion==2)return;
            bool oldest=World.WildlifeVersion==0;
            for(int i=0;i<World.Units.Count;i++)
            {
                UnitData u=World.Units[i];if(!WildlifeCatalog.IsAnimal(u.Species))continue;
                if(oldest){u.PackId=u.PackLeaderId=u.Id;u.BreedingSeconds=0;u.Name=WildlifeCatalog.Name(u.Species);}
                float interval=WildlifeCatalog.BreedingInterval(u.Species,u.IsDomestic);
                u.BreedingSeconds=Math.Min(interval*.5f,u.BreedingSeconds);
                u.NextBreedingTick=World.Tick+(long)(interval*10);
            }
            World.WildlifeVersion=2;World.NextWildlifeSpawnTick=World.Tick+(oldest?200:WildlifeCatalog.ImmigrationSeconds*10);
            RefreshWildlifeHabitat();
        }
        private bool WildlifeForageCell(int cell)
        {
            TileData t=World.Cells[cell];return PlacementRules.IsDryGround(World,cell%World.Width,cell/World.Width)
                &&t.Fire<=0&&t.BuildingId==0&&t.SettlementId==0&&t.Vegetation>=.15f&&t.Terrain!=TerrainType.Desert&&t.Terrain!=TerrainType.Snow;
        }
        private void RefreshWildlifeHabitat()
        {
            float habitat=0;for(int i=0;i<World.Cells.Length;i++)if(WildlifeForageCell(i))habitat+=World.Cells[i].Vegetation;
            World.WildlifeHabitatCapacity=Math.Max(0,Math.Min(128,(int)(habitat/70f)));
            World.NextWildlifeHabitatTick=World.Tick+300;
        }
        private bool WildlifeHabitatAllows(SpeciesType species,int x,int y,int addition,bool consume=false)
        {
            float forage=0;int nearby=0,predators=0,prey=0;
            for(int cy=Math.Max(0,y-6);cy<=Math.Min(World.Height-1,y+6);cy++)
                for(int cx=Math.Max(0,x-6);cx<=Math.Min(World.Width-1,x+6);cx++){int cell=World.Index(cx,cy);if(WildlifeForageCell(cell))forage+=World.Cells[cell].Vegetation;}
            foreach(UnitData a in World.Units)
            {
                if(!WildlifeCatalog.IsAnimal(a.Species)||a.Health<=0||DistanceSquared(x+.5f,y+.5f,a.X,a.Y)>64)continue;
                nearby++;if(WildlifeCatalog.IsPredator(a.Species))predators++;else prey++;
            }
            int localCapacity=Math.Min(12,(int)(forage/12));
            if(nearby+addition>localCapacity)return false;
            if(WildlifeCatalog.IsPredator(species))return prey>=4*(predators+addition);
            if(forage<4)return false;
            if(consume)
            {
                float remaining=.5f;
                for(int radius=0;radius<=6&&remaining>.00001f;radius++)
                    for(int cy=Math.Max(0,y-radius);cy<=Math.Min(World.Height-1,y+radius)&&remaining>.00001f;cy++)
                        for(int cx=Math.Max(0,x-radius);cx<=Math.Min(World.Width-1,x+radius)&&remaining>.00001f;cx++)
                        {int cell=World.Index(cx,cy);if(!WildlifeForageCell(cell))continue;TileData t=World.Cells[cell];float take=Math.Min(remaining,Math.Max(0,t.Vegetation-.1f));t.Vegetation-=take;remaining-=take;World.Cells[cell]=t;}
                World.TerrainRevision++;
            }
            return true;
        }
        private bool BreedingHealthy(UnitData u)
        {
            return PlacementRules.IsDryGround(World,(int)u.X,(int)u.Y)&&World.Cells[World.Index((int)u.X,(int)u.Y)].Fire<=0&&u.Health>=u.MaxHealth*.7f&&u.Age>=WildlifeCatalog.MaturityAge(u.Species)&&u.Hunger<.4f
                &&u.Task!=UnitTask.Fight&&u.Task!=UnitTask.Flee&&u.HandlerId==0&&World.Tick>=u.NextBreedingTick;
        }
        private bool AnimalPairReachable(UnitData first,UnitData second)
        {return navigation.Find(World.Index((int)first.X,(int)first.Y),World.Index((int)second.X,(int)second.Y),scratchPath,128)&&scratchPath.Count<=8;}
        private void FinishAnimalBirth(UnitData mother,UnitData father,bool domestic)
        {
            float interval=WildlifeCatalog.BreedingInterval(mother.Species,domestic);
            mother.BreedingSeconds=father.BreedingSeconds=0;mother.NextBreedingTick=father.NextBreedingTick=World.Tick+(long)(interval*10);
            mother.Hunger=Math.Min(1,mother.Hunger+.08f);father.Hunger=Math.Min(1,father.Hunger+.04f);
            if(domestic)World.DomesticAnimalBirths++;else World.WildlifeBirths++;
        }
        private void NormalizeWildlifePacks()
        {
            packLeaders.Clear();
            for(int i=0;i<World.Units.Count;i++)
            {
                UnitData u=World.Units[i];if(!WildlifeCatalog.IsAnimal(u.Species)||u.Health<=0||u.PackId==0)continue;
                u.BreedingSeconds=Math.Min(WildlifeCatalog.MaximumBreedingSeconds(u.Species,u.IsDomestic),u.BreedingSeconds);
                if(!packLeaders.TryGetValue(u.PackId,out int leader)||u.Id<leader)packLeaders[u.PackId]=u.Id;
            }
            for(int i=0;i<World.Units.Count;i++){UnitData u=World.Units[i];if(WildlifeCatalog.IsAnimal(u.Species))u.PackLeaderId=u.PackId!=0&&packLeaders.TryGetValue(u.PackId,out int leader)?leader:0;}
        }
        private void UpdateWildlife()
        {
            NormalizeWildlifePacks();if(World.Tick>=World.NextWildlifeHabitatTick)RefreshWildlifeHabitat();
            Array.Clear(wildlifeCounts,0,wildlifeCounts.Length);int total=0;
            for(int i=0;i<World.Units.Count;i++)if(WildlifeCatalog.IsAnimal(World.Units[i].Species)&&World.Units[i].Health>0&&!World.Units[i].IsDomestic){wildlifeCounts[(int)World.Units[i].Species]++;total++;}
            int limit=World.WildlifeHabitatCapacity;
            if(World.NaturalWildlife&&World.Tick>=World.NextWildlifeSpawnTick)
            {
                World.NextWildlifeSpawnTick=World.Tick+WildlifeCatalog.ImmigrationSeconds*10;
                if(total<limit&&World.Units.Count<9998)
                {
                    int round=(int)(World.Tick/(WildlifeCatalog.ImmigrationSeconds*10));
                    for(int attempt=0;attempt<24;attempt++)
                    {
                        int cell=(int)(StableRandom.Hash(World.Seed,round,attempt,7000)%(uint)World.Cells.Length);TileData tile=World.Cells[cell];int x=cell%World.Width,y=cell/World.Width;
                        if(tile.Terrain!=TerrainType.Forest||!WildlifeForageCell(cell))continue;
                        uint roll=StableRandom.Hash(World.Seed,round,cell,7001)%100;
                        SpeciesType species=roll<2?SpeciesType.DireWolf:roll<4?SpeciesType.SpiritStag:roll<11?SpeciesType.Bear:roll<21?SpeciesType.Wolf:roll<30?SpeciesType.Fox:roll<44?SpeciesType.Boar:roll<65?SpeciesType.Deer:roll<75?SpeciesType.Goat:roll<84?SpeciesType.Chicken:roll<90?SpeciesType.Cow:roll<95?SpeciesType.Donkey:SpeciesType.Horse;
                        if(WildlifeCatalog.IsRare(species)&&wildlifeCounts[(int)species]>=2)continue;
                        int count=Math.Min(limit-total,WildlifeCatalog.IsRare(species)||WildlifeCatalog.IsPredator(species)?1:2);
                        if(!WildlifeHabitatAllows(species,x,y,count))continue;
                        int before=World.Units.Count;Spawn(species,x,y,count);int added=World.Units.Count-before;
                        World.WildlifeImmigrations+=added;total+=added;wildlifeCounts[(int)species]+=added;break;
                    }
                }
            }
            if(!World.Laws.Births||total>=limit||World.Units.Count>=9999)return;
            int original=World.Units.Count;
            for(int i=0;i<original&&total<limit&&World.Units.Count<10000;i++)
            {
                UnitData mother=World.Units[i];if(!WildlifeCatalog.IsAnimal(mother.Species)||mother.IsDomestic||WildlifeCatalog.IsRare(mother.Species)||mother.Sex!=Sex.Female)continue;
                float interval=WildlifeCatalog.BreedingInterval(mother.Species,false);
                if(mother.Health<mother.MaxHealth*.7f||mother.Hunger>.4f||mother.Age<WildlifeCatalog.MaturityAge(mother.Species))continue;
                mother.BreedingSeconds=Math.Min(interval,mother.BreedingSeconds+1);
                if(mother.BreedingSeconds<interval||!BreedingHealthy(mother)||wildlifeCounts[(int)mother.Species]>=Math.Max(4,limit/3))continue;
                UnitData father=null;
                for(int n=0;n<original;n++){UnitData a=World.Units[n];if(a.Species==mother.Species&&a.Sex==Sex.Male&&!a.IsDomestic&&BreedingHealthy(a)&&DistanceSquared(a.X,a.Y,mother.X,mother.Y)<25){father=a;break;}}
                if(father==null||!AnimalPairReachable(mother,father)||!WildlifeHabitatAllows(mother.Species,(int)mother.X,(int)mother.Y,1,true))continue;
                AddUnit(mother.Species,(int)mother.X,(int)mother.Y,0,true);UnitData baby=World.Units[World.Units.Count-1];baby.PackId=mother.PackId;baby.PackLeaderId=mother.PackLeaderId;
                FinishAnimalBirth(mother,father,false);wildlifeCounts[(int)mother.Species]++;total++;AddEvent(EventKind.Birth,baby.X,baby.Y,WildlifeCatalog.Name(baby.Species)+": появился детёныш");
            }
        }
        private bool PredatorWants(UnitData predator, UnitData prey)
        {
            if (!WildlifeCatalog.IsPredator(predator.Species) || predator.IsDomestic || prey.IsDomestic && prey.HandlerId == predator.Id || prey.Id == predator.Id || prey.Health <= 0) return false;
            if (IsCivilized(prey.Species)) return prey.Age >= 16 && ((prey.TargetId == predator.Id && (prey.Task == UnitTask.Fight || prey.Task == UnitTask.Hunt)) || ((predator.Species == SpeciesType.Bear || predator.Species == SpeciesType.DireWolf) && predator.Hunger > .35f));
            if (!WildlifeCatalog.IsAnimal(prey.Species) || WildlifeCatalog.IsPredator(prey.Species) || WildlifeCatalog.IsRare(prey.Species)) return false;
            return predator.Species == SpeciesType.Fox ? prey.Species == SpeciesType.Chicken : predator.Species == SpeciesType.Wolf ? prey.MaxHealth <= 65 : prey.MaxHealth <= 150;
        }
        private bool ChooseWildlifeActivity(UnitData unit)
        {
            if (!WildlifeCatalog.IsAnimal(unit.Species)) return false;
            if (!WildlifeCatalog.IsPredator(unit.Species) && unit.Hunger > .3f && ChooseFood(unit, null)) return true;
            if (unit.Fatigue > .72f && ChooseRest(unit, null)) return true;
            UnitData handler = unit.IsDomestic ? World.GetUnit(unit.HandlerId) : null;
            if (handler != null && handler.Health > 0 && handler.Age >= 16 && handler.SettlementId == unit.AnimalOwnerId && DistanceSquared(unit.X, unit.Y, handler.X, handler.Y) > .7f && SetTarget(unit, (int)handler.X, (int)handler.Y))
            { ChangeTask(unit, UnitTask.Wander); return true; }
            if (unit.IsDomestic && handler == null)
            {
                BuildingData pasture = World.GetBuilding(unit.PastureId);
                if (pasture != null && pasture.Health > 0 && pasture.SettlementId == unit.AnimalOwnerId)
                {
                    if (DistanceSquared(unit.X, unit.Y, pasture.X + .5f, pasture.Y + .5f) > 9 && SetTarget(unit, pasture.X, pasture.Y)) { ChangeTask(unit, UnitTask.Wander); return true; }
                    if (unit.Hunger < .3f) { ClearRoute(unit); ChangeTask(unit, UnitTask.Idle); return true; }
                }
            }
            UnitData leader = World.GetUnit(unit.PackLeaderId);
            if (leader != null && leader.Id != unit.Id && DistanceSquared(unit.X, unit.Y, leader.X, leader.Y) > 16 && SetTarget(unit, (int)leader.X, (int)leader.Y)) { ChangeTask(unit, UnitTask.Wander); return true; }
            return false;
        }
        private bool HuntEligible(UnitData hunter, UnitData prey)
        {
            if (hunter.Age < 16 || hunter.HuntingToolLevel < 1 || hunter.Health < hunter.MaxHealth * .65f || prey == null || prey.Health <= 0 || prey.IsDomestic || !WildlifeCatalog.IsAnimal(prey.Species) || WildlifeCatalog.IsRare(prey.Species)) return false;
            return prey.Species == SpeciesType.Sheep || prey.Species == SpeciesType.Deer || prey.Species == SpeciesType.Chicken || prey.Species == SpeciesType.Fox || prey.Species == SpeciesType.Goat;
        }
        private UnitData HuntTarget(UnitData unit)
        {
            if (unit.Task != UnitTask.Hunt) return null;
            UnitData target = World.GetUnit(unit.TargetId); return HuntEligible(unit, target) && DistanceSquared(unit.X, unit.Y, target.X, target.Y) < 24 * 24 ? target : null;
        }
        private bool ChooseHunt(UnitData unit, SettlementData home)
        {
            if (unit.Age < 16 || unit.Profession != UnitProfession.Hunter || unit.HuntingToolLevel < 1 || unit.Hunger > .65f || CarriedLoad(unit) > 0 || home.Food >= home.Population * 6) return false;
            UnitData best = null; float closest = 18 * 18;
            for (int i = 0; i < World.Units.Count; i++) { UnitData prey = World.Units[i]; float d = DistanceSquared(unit.X, unit.Y, prey.X, prey.Y); if (d < closest && HuntEligible(unit, prey)) { best = prey; closest = d; } }
            if (best == null || !SetTarget(unit, (int)best.X, (int)best.Y)) return false;
            ChangeTask(unit, UnitTask.Hunt); unit.TargetId = best.Id; return true;
        }
        private void DropAnimalLoot(UnitData animal)
        {
            if (!WildlifeCatalog.IsAnimal(animal.Species)) return;
            float meat = animal.Species == SpeciesType.Chicken ? 2 : Math.Max(3, (float)Math.Floor(animal.MaxHealth / 15));
            // A predator consumes one meat unit from its kill; the rest is a physical on-site drop.
            bool eaten = false; for (int i = 0; i < World.Units.Count; i++) { UnitData killer = World.Units[i]; if (WildlifeCatalog.IsPredator(killer.Species) && killer.TargetId == animal.Id && killer.Hunger == 0 && DistanceSquared(killer.X, killer.Y, animal.X, animal.Y) <= 2.2f) { eaten = true; break; } }
            DropAgriculturalLoot(animal, AgriculturalProduct.Meat, Math.Max(0, meat - (eaten ? 1 : 0)));
            if (animal.Species != SpeciesType.Chicken) DropAgriculturalLoot(animal, AgriculturalProduct.Hide, Math.Max(1, (float)Math.Floor(animal.MaxHealth / 60)));
            if (animal.Species == SpeciesType.SpiritStag) DropAgriculturalLoot(animal, AgriculturalProduct.RareHorn, 1);
            if (animal.Species == SpeciesType.DireWolf) DropAgriculturalLoot(animal, AgriculturalProduct.RarePelt, 1);
        }
    }
}
