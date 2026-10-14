using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    [Serializable]
    public sealed class RemainsData
    {
        public int SourceUnitId;
        public SpeciesType Species;
        public bool Child, WaterDeath;
        public float X, Y, RemainingSeconds;
        public byte Direction;
        public long DeathTick;
    }

    public static class RemainsRules
    {
        public const int MaximumCount = 512;
        public const float LifetimeSeconds = SubsistenceRules.DaySeconds * .25f;
        public const float WaterLifetimeSeconds = 30f;
        public static float Lifetime(bool waterDeath) { return waterDeath ? WaterLifetimeSeconds : LifetimeSeconds; }
        public static float FadeSeconds(bool waterDeath) { return waterDeath ? 10f : 30f; }
    }

    public sealed partial class WorldState
    {
        public int RemainsVersion;
        public List<RemainsData> Remains = new List<RemainsData>();

        private bool ValidateRemains(out string error)
        {
            error = null;
            if (RemainsVersion < 0 || RemainsVersion > 1) { error = "Unsupported remains version."; return false; }
            if (RemainsVersion == 0)
            {
                if (Remains != null && Remains.Count != 0) { error = "Legacy world contains remains."; return false; }
                return true;
            }
            if (Remains == null || Remains.Count > RemainsRules.MaximumCount) { error = "Invalid remains collection."; return false; }
            var sources = new HashSet<int>();
            foreach (RemainsData remains in Remains)
            {
                if (remains == null || remains.SourceUnitId <= 0 || remains.SourceUnitId >= NextId || !sources.Add(remains.SourceUnitId) ||
                    (int)remains.Species < 0 || (int)remains.Species > (int)SpeciesType.Horse || (remains.Child && (int)remains.Species >= 4) ||
                    !Finite(remains.X) || !Finite(remains.Y) || remains.X < 0 || remains.Y < 0 || remains.X >= Width || remains.Y >= Height ||
                    !Finite(remains.RemainingSeconds) || remains.RemainingSeconds <= 0 || remains.RemainingSeconds > RemainsRules.Lifetime(remains.WaterDeath) ||
                    remains.DeathTick < 0 || remains.DeathTick > Tick || remains.Direction > 3 || GetUnit(remains.SourceUnitId) != null)
                { error = "Invalid remains record."; return false; }
            }
            return true;
        }
    }
}
