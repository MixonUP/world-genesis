namespace WorldGenesis.Simulation
{
    /// <summary>Age progression in game-years per simulation second. Applies only to future ticks, never rewrites saved ages.</summary>
    public static class AgingRules
    {
        public const float ProgressMultiplier = .5f;
        public const float CivilianBaseRate = .1f;

        public static float RatePerSecond(SpeciesType species)
        {
            return (WildlifeCatalog.IsAnimal(species) ? WildlifeCatalog.AgeRate : CivilianBaseRate) * ProgressMultiplier;
        }
    }
}
