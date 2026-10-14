using System;

namespace WorldGenesis.Simulation
{
    public sealed partial class UnitData
    {
        public int WarTargetSettlementId, WarArmoryId;
        public long WarOrderSinceTick;
        public ItemType WarWeapon;
        public bool WarReturning;
    }
    public sealed partial class SettlementData
    {
        // Local people, jobs and inventories retain their settlement identity.
        public int SovereignSettlementId, OccupierSettlementId, ConquestCount;
        public float OccupationSeconds;
        public long NextWarPartyTick;
    }
    public sealed partial class WorldState
    {
        public int ConquestVersion;
        public int SovereignOf(int settlementId)
        {
            SettlementData s = GetSettlement(settlementId);
            return s == null ? 0 : s.SovereignSettlementId == 0 ? s.Id : s.SovereignSettlementId;
        }
        public bool ValidateConquest(out string error)
        {
            error = null;
            if (ConquestVersion < 0 || ConquestVersion > 1) { error = "Unsupported conquest version."; return false; }
            foreach (SettlementData s in Settlements)
            {
                SettlementData owner = GetSettlement(s.SovereignSettlementId);
                if (s.SovereignSettlementId != 0 && (owner == null || owner.Id == s.Id || owner.SovereignSettlementId != 0) || s.OccupierSettlementId != 0 && GetSettlement(s.OccupierSettlementId) == null || !Stock(s.OccupationSeconds) || s.OccupationSeconds > 20 || s.NextWarPartyTick < 0 || s.ConquestCount < 0)
                { error = "Invalid conquest ownership or occupation."; return false; }
            }
            foreach (UnitData u in Units)
            {
                if (u.WarTargetSettlementId != 0 && (GetSettlement(u.WarTargetSettlementId) == null || !SimulationEngine.IsCivilized(u.Species) || u.Age < 16 || u.SettlementId == 0) || u.WarArmoryId < 0 || u.WarOrderSinceTick < 0 || u.WarOrderSinceTick > Tick || u.WarWeapon != ItemType.None && u.WarWeapon != ItemType.Sword && u.WarWeapon != ItemType.Spear || u.WarWeapon != ItemType.None && (u.Age < 16 || !SimulationEngine.IsCivilized(u.Species)))
                { error = "Invalid expedition soldier."; return false; }
            }
            return true;
        }
    }
}
