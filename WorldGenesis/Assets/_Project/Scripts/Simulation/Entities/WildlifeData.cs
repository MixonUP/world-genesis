using System;

namespace WorldGenesis.Simulation
{
    public static class WildlifeCatalog
    {
        public const float AgeRate=.025f;
        public const int ImmigrationSeconds=120;
        public static float MaturityAge(SpeciesType species){return species==SpeciesType.Bear||species==SpeciesType.Horse||species==SpeciesType.Cow||species==SpeciesType.SpiritStag?4:2;}
        public static float BreedingInterval(SpeciesType species,bool domestic)
        {return species==SpeciesType.Chicken?(domestic?180:240):IsRare(species)?600:species==SpeciesType.Bear||species==SpeciesType.Horse||species==SpeciesType.Cow||species==SpeciesType.Donkey?(domestic?420:600):IsPredator(species)?480:domestic?300:360;}
        public static float MaximumBreedingSeconds(SpeciesType species,bool domestic){return BreedingInterval(species,domestic);}

        public static bool IsAnimal(SpeciesType s) { return s >= SpeciesType.Sheep && s <= SpeciesType.Horse; }
        public static bool IsPredator(SpeciesType s) { return s == SpeciesType.Wolf || s == SpeciesType.Fox || s == SpeciesType.Bear || s == SpeciesType.DireWolf; }
        public static bool IsRare(SpeciesType s) { return s == SpeciesType.DireWolf || s == SpeciesType.SpiritStag; }
        public static float MaxHealth(SpeciesType s)
        {
            switch (s) { case SpeciesType.Sheep: return 35; case SpeciesType.Wolf: return 65; case SpeciesType.Deer: return 55; case SpeciesType.Boar: return 90; case SpeciesType.Fox: return 30; case SpeciesType.Bear: return 220; case SpeciesType.DireWolf: return 180; case SpeciesType.SpiritStag: return 160; case SpeciesType.Goat: return 45; case SpeciesType.Cow: return 110; case SpeciesType.Chicken: return 16; case SpeciesType.Donkey: return 100; case SpeciesType.Horse: return 125; default: return 100; }
        }
        public static float Speed(SpeciesType s)
        {
            switch (s) { case SpeciesType.Wolf: return 2.2f; case SpeciesType.DireWolf: return 2.5f; case SpeciesType.Deer: return 2.4f; case SpeciesType.SpiritStag: return 2.5f; case SpeciesType.Horse: return 2.7f; case SpeciesType.Fox: return 2.1f; case SpeciesType.Cow: return 1.1f; case SpeciesType.Bear: return 1.6f; case SpeciesType.Donkey: return 1.8f; default: return 1.45f; }
        }
        public static float Damage(SpeciesType s)
        {
            switch (s) { case SpeciesType.Wolf: return 9; case SpeciesType.DireWolf: return 21; case SpeciesType.Bear: return 25; case SpeciesType.Boar: return 11; case SpeciesType.SpiritStag: return 17; case SpeciesType.Fox: return 5; default: return 4; }
        }
        public static string Name(SpeciesType s)
        {
            switch (s) { case SpeciesType.Sheep: return "Овца"; case SpeciesType.Wolf: return "Волк"; case SpeciesType.Deer: return "Олень"; case SpeciesType.Boar: return "Кабан"; case SpeciesType.Fox: return "Лиса"; case SpeciesType.Bear: return "Медведь"; case SpeciesType.DireWolf: return "Сумрачный волк"; case SpeciesType.SpiritStag: return "Лунный олень"; case SpeciesType.Goat: return "Коза"; case SpeciesType.Cow: return "Корова"; case SpeciesType.Chicken: return "Курица"; case SpeciesType.Donkey: return "Осёл"; case SpeciesType.Horse: return "Лошадь"; default: return s.ToString(); }
        }
    }
    public sealed partial class UnitData
    {
        public int PackId, PackLeaderId;
        public float BreedingSeconds;
        public long NextBreedingTick;
    }
    public sealed partial class WorldState
    {
        public int WildlifeVersion;
        public int WildlifeHabitatCapacity;
        public long NextWildlifeHabitatTick,WildlifeBirths,DomesticAnimalBirths,WildlifeImmigrations,WildlifeDeaths;
        public long NextWildlifeSpawnTick;
        public bool NaturalWildlife = true;
        private bool ValidateWildlife(out string error)
        {
            error = null;
            if (WildlifeVersion < 0 || WildlifeVersion > 2 || NextWildlifeSpawnTick < 0) { error = "Invalid wildlife version or clock."; return false; }
            if (WildlifeVersion == 0) return true;
            if(WildlifeVersion>=2&&(WildlifeHabitatCapacity<0||WildlifeHabitatCapacity>128||NextWildlifeHabitatTick<0||WildlifeBirths<0||DomesticAnimalBirths<0||WildlifeImmigrations<0||WildlifeDeaths<0)){error="Invalid ecology accounting.";return false;}
            for (int i = 0; i < Units.Count; i++)
            {
                UnitData u = Units[i];
                if (!Stock(u.BreedingSeconds) || u.BreedingSeconds > (WildlifeVersion<2?(u.IsDomestic?180:120):WildlifeCatalog.MaximumBreedingSeconds(u.Species,u.IsDomestic)) || u.NextBreedingTick<0 || u.PackId < 0 || u.PackId >= NextId || u.PackLeaderId < 0 || u.PackLeaderId >= NextId || (!WildlifeCatalog.IsAnimal(u.Species) && (u.PackId != 0 || u.PackLeaderId != 0))) { error = "Invalid herd state."; return false; }
                UnitData leader = GetUnit(u.PackLeaderId);
                if (u.PackLeaderId != 0 && (leader == null || leader.Health <= 0 || leader.Species != u.Species || leader.PackId != u.PackId || u.PackId == 0)) { error = "Invalid living pack leader."; return false; }
            }
            return true;
        }
    }
}
