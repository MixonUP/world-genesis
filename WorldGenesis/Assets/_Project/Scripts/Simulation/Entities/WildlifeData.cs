using System;

namespace WorldGenesis.Simulation
{
    public static class WildlifeCatalog
    {
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
    }
    public sealed partial class WorldState
    {
        public int WildlifeVersion;
        public long NextWildlifeSpawnTick;
        public bool NaturalWildlife = true;
        private bool ValidateWildlife(out string error)
        {
            error = null;
            if (WildlifeVersion < 0 || WildlifeVersion > 1 || NextWildlifeSpawnTick < 0) { error = "Invalid wildlife version or clock."; return false; }
            if (WildlifeVersion == 0) return true;
            for (int i = 0; i < Units.Count; i++)
            {
                UnitData u = Units[i];
                if (!Stock(u.BreedingSeconds) || u.BreedingSeconds > (u.IsDomestic ? 180 : 120) || u.PackId < 0 || u.PackId >= NextId || u.PackLeaderId < 0 || u.PackLeaderId >= NextId || (!WildlifeCatalog.IsAnimal(u.Species) && (u.PackId != 0 || u.PackLeaderId != 0))) { error = "Invalid herd state."; return false; }
                UnitData leader = GetUnit(u.PackLeaderId);
                if (u.PackLeaderId != 0 && (leader == null || leader.Health <= 0 || leader.Species != u.Species || leader.PackId != u.PackId || u.PackId == 0)) { error = "Invalid living pack leader."; return false; }
            }
            return true;
        }
    }
}
