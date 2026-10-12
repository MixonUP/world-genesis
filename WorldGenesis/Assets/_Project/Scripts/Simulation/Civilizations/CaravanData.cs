using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public enum CaravanPhase { Loading, Outbound, Returning, Completed, Cancelled }
    public enum CaravanMemberPhase { Loading, Ready, Outbound, AtMarket, Returning, Done }

    [Serializable]
    public sealed class CaravanMemberData
    {
        public int UnitId, SourceBuildingId;
        public CaravanMemberPhase Phase;
        public float ExportQuota, HireFoodPaid;
        public long BlockedSinceTick;
    }

    [Serializable]
    public sealed class CaravanData
    {
        public int Id, OriginId, DestinationId, LeaderId, MarketBuildingId;
        public ItemType ExportItem, ImportItem;
        public CaravanPhase Phase;
        public bool Aborted;
        public long StartedTick, LastChangeTick;
        public string LastReason = "";
        public List<CaravanMemberData> Members = new List<CaravanMemberData>();
    }

    [Serializable]
    public sealed class CaravanExchangeData
    {
        public int CaravanId, UnitId, BuildingId;
        public long Tick;
        public ItemType ExportItem, ImportItem;
        public float Amount, TaxAmount;
    }

    public sealed partial class UnitData { public int CaravanId; }
    public sealed partial class SettlementData { public long NextCaravanTick; }

    public sealed partial class WorldState
    {
        public int CaravanVersion, NextCaravanId = 1;
        public List<CaravanData> Caravans = new List<CaravanData>();
        public List<CaravanExchangeData> CaravanExchanges = new List<CaravanExchangeData>();

        public CaravanData GetCaravan(int id)
        {
            if (Caravans != null) for (int i = 0; i < Caravans.Count; i++) if (Caravans[i].Id == id) return Caravans[i];
            return null;
        }
        private bool ValidateCaravans(out string error)
        {
            error = null;
            if (CaravanVersion < 0 || CaravanVersion > 1) { error = "Unsupported caravan version."; return false; }
            if (CaravanVersion == 0) return true;
            if (Caravans == null || Caravans.Count > 64 || CaravanExchanges == null || CaravanExchanges.Count > 128 || NextCaravanId < 1)
            { error = "Invalid caravan records."; return false; }
            HashSet<int> ids = new HashSet<int>(), activeUnits = new HashSet<int>(), activeTowns = new HashSet<int>();
            for (int i = 0; i < Caravans.Count; i++)
            {
                CaravanData c = Caravans[i];
                if (c == null || c.Id <= 0 || c.Id >= NextCaravanId || !ids.Add(c.Id) || c.OriginId == c.DestinationId || GetSettlement(c.OriginId) == null || GetSettlement(c.DestinationId) == null || c.LeaderId <= 0 || c.LeaderId >= NextId || c.MarketBuildingId <= 0 || c.MarketBuildingId >= NextId || !SimulationEngine.TradeableItem(c.ExportItem) || !SimulationEngine.TradeableItem(c.ImportItem) || c.ExportItem == c.ImportItem || (int)c.Phase < 0 || (int)c.Phase > 4 || c.StartedTick < 0 || c.StartedTick > Tick || c.LastChangeTick < c.StartedTick || c.LastChangeTick > Tick || c.LastReason == null || c.LastReason.Length > 512 || c.Members == null || c.Members.Count < 1 || c.Members.Count > 4)
                { error = "Invalid caravan contract."; return false; }
                bool active = (int)c.Phase < (int)CaravanPhase.Completed;
                if (active && !activeTowns.Add(c.OriginId)) { error = "Two active caravans from one town."; return false; }
                HashSet<int> members = new HashSet<int>();
                for (int n = 0; n < c.Members.Count; n++)
                {
                    CaravanMemberData m = c.Members[n];
                    if (m == null || m.UnitId <= 0 || m.UnitId >= NextId || !members.Add(m.UnitId) || m.SourceBuildingId <= 0 || m.SourceBuildingId >= NextId || (int)m.Phase < 0 || (int)m.Phase > 5 || !Stock(m.ExportQuota) || m.ExportQuota <= 0 || m.ExportQuota > 150 || !Stock(m.HireFoodPaid) || (m.HireFoodPaid != 0 && m.HireFoodPaid != 1) || m.BlockedSinceTick < 0 || m.BlockedSinceTick > Tick || (!active && m.Phase != CaravanMemberPhase.Done))
                    { error = "Invalid caravan participant."; return false; }
                    if (m.Phase == CaravanMemberPhase.Done) continue;
                    if ((c.Phase == CaravanPhase.Loading && m.Phase != CaravanMemberPhase.Loading && m.Phase != CaravanMemberPhase.Ready) || (c.Phase == CaravanPhase.Outbound && m.Phase != CaravanMemberPhase.Outbound && m.Phase != CaravanMemberPhase.AtMarket) || (c.Phase == CaravanPhase.Returning && m.Phase != CaravanMemberPhase.Returning))
                    { error = "Caravan phase and participant phase disagree."; return false; }
                    UnitData u = GetUnit(m.UnitId);
                    if (!active || u == null || u.Health <= 0 || u.Age < 16 || (int)u.Species > (int)SpeciesType.Orc || u.SettlementId != c.OriginId || u.CaravanId != c.Id || !activeUnits.Add(u.Id) || (u.CargoAmount > 0 && u.CargoKind != c.ExportItem && u.CargoKind != c.ImportItem))
                    { error = "Invalid live caravan member."; return false; }
                }
                if (!members.Contains(c.LeaderId)) { error = "Caravan leader is not a participant."; return false; }
            }
            for (int i = 0; i < Units.Count; i++)
                if (Units[i].CaravanId < 0 || (Units[i].CaravanId != 0 && !activeUnits.Contains(Units[i].Id)))
                { error = "Dangling caravan membership."; return false; }
            for (int i = 0; i < Settlements.Count; i++)
                if (Settlements[i].NextCaravanTick < 0) { error = "Invalid caravan planning time."; return false; }
            for (int i = 0; i < CaravanExchanges.Count; i++)
            {
                CaravanExchangeData t = CaravanExchanges[i];
                if (t == null || t.CaravanId <= 0 || t.CaravanId >= NextCaravanId || t.UnitId <= 0 || t.UnitId >= NextId || t.BuildingId <= 0 || t.BuildingId >= NextId || t.Tick < 0 || t.Tick > Tick || !SimulationEngine.TradeableItem(t.ExportItem) || !SimulationEngine.TradeableItem(t.ImportItem) || t.ExportItem == t.ImportItem || !Stock(t.Amount) || t.Amount <= 0 || !Stock(t.TaxAmount) || t.TaxAmount > t.Amount * .30001f)
                { error = "Invalid caravan exchange receipt."; return false; }
            }
            return true;
        }
    }
}
