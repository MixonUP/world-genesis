namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        public const long RepublicElectionIntervalTicks = 600;

        private void MigratePolitics()
        {
            if (World.PoliticsVersion != 0) return;
            for (int i = 0; i < World.Settlements.Count; i++) InitializeGovernment(World.Settlements[i]);
            World.PoliticsVersion = 1;
            UpdatePolitics();
            World.Revision++;
        }

        private void InitializeGovernment(SettlementData settlement)
        {
            // Independent random stream: founding and legacy migration never consume gameplay RNG.
            settlement.Government = (GovernmentType)(1 + StableRandom.Hash(World.Seed, settlement.Id, 0, 4000) % 3);
            settlement.GovernmentSinceTick = World.Tick;
            settlement.NextElectionTick = settlement.Government == GovernmentType.Republic ? NextElectionAfter(World.Tick) : 0;
            settlement.RulerId = 0;
        }

        private static long NextElectionAfter(long tick)
        {
            return tick <= long.MaxValue - RepublicElectionIntervalTicks ? tick + RepublicElectionIntervalTicks : long.MaxValue;
        }

        private static bool EligibleRuler(UnitData unit, int settlementId)
        {
            return unit != null && unit.Health > 0 && unit.Age >= 16 && IsCivilized(unit.Species) && unit.SettlementId == settlementId;
        }

        private void UpdatePolitics()
        {
            for (int i = 0; i < World.Settlements.Count; i++)
            {
                SettlementData settlement = World.Settlements[i];
                bool election = settlement.Government == GovernmentType.Republic && World.Tick >= settlement.NextElectionTick;
                if (!election && EligibleRuler(World.GetUnit(settlement.RulerId), settlement.Id)) continue;
                settlement.RulerId = SelectRuler(settlement);
                if (election) settlement.NextElectionTick = NextElectionAfter(World.Tick);
            }
        }

        private int SelectRuler(SettlementData settlement)
        {
            UnitData best = null;
            uint bestVote = 0;
            long electionRound = (World.Tick - settlement.GovernmentSinceTick) / RepublicElectionIntervalTicks;
            uint ballotStream = 4001u ^ (uint)electionRound ^ (uint)(electionRound >> 32);
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData candidate = World.Units[i];
                if (!EligibleRuler(candidate, settlement.Id)) continue;
                if (settlement.Government == GovernmentType.Republic)
                {
                    // A deterministic abstract ballot, not a simulation of individual political opinions.
                    uint vote = StableRandom.Hash(World.Seed, settlement.Id, candidate.Id, ballotStream);
                    if (best == null || vote > bestVote || (vote == bestVote && candidate.Id < best.Id)) { best = candidate; bestVote = vote; }
                }
                else
                {
                    bool preferBlessed = settlement.Government == GovernmentType.Theocracy;
                    if (best == null || (preferBlessed && candidate.Blessed && !best.Blessed) || ((!preferBlessed || candidate.Blessed == best.Blessed) && (candidate.Age > best.Age || (candidate.Age == best.Age && candidate.Id < best.Id)))) best = candidate;
                }
            }
            return best == null ? 0 : best.Id;
        }
    }
}
