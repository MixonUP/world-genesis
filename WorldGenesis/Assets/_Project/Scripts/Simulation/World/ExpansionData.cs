using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public enum SettlementExpeditionPhase { Gathering, Travelling, Founding, Returning, Completed, Cancelled }

    [Serializable]
    public sealed class SettlementExpeditionData
    {
        public int Id, SourceSettlementId, SourceBuildingId, TargetCell, FoundedSettlementId;
        public SettlementExpeditionPhase Phase;
        public List<int> MemberIds = new List<int>();
        public long StartedTick, PhaseStartedTick, LastProgressTick;
        public float FoundingProgress, FoodConsumed, WaterConsumed, PlanksSpent;
        public int DepartedMembers;
        public string LastReason = "";
    }

    public sealed partial class UnitData
    {
        public int ExpeditionId;
        public bool ExpeditionLoaded, ExpeditionResting;
    }
    public sealed partial class SettlementData
    {
        public int TerritoryRadius, ParentSettlementId;
        public long NextExpansionTick, NextColonizationTick;
    }

    public static class ExpansionCatalog
    {
        public const int Members = 6, MinRemainingResidents = 12, PlanksRequired = 4;
        public const int MinSettlementDistance = 28, MaxRouteCells = 60;
        public const float FoundingSeconds = 6, FoodRequired = 1, WaterRequired = 1;
        public static bool Active(SettlementExpeditionData expedition)
        { return expedition != null && expedition.Phase != SettlementExpeditionPhase.Completed && expedition.Phase != SettlementExpeditionPhase.Cancelled; }
        public static int BuildingRadius(SettlementData settlement)
        { return settlement == null ? 13 : Math.Max(13, Math.Min(24, settlement.TerritoryRadius)); }
        public static int BuildingLimit(SettlementData settlement)
        { return 24 + Math.Max(0, BuildingRadius(settlement) - 13) * 2; }
        public static string PhaseName(SettlementExpeditionPhase phase)
        {
            switch (phase)
            {
                case SettlementExpeditionPhase.Gathering: return "Сбор припасов";
                case SettlementExpeditionPhase.Travelling: return "Переход к новому городу";
                case SettlementExpeditionPhase.Founding: return "Устройство костра";
                case SettlementExpeditionPhase.Returning: return "Возвращение";
                case SettlementExpeditionPhase.Completed: return "Поселение основано";
                default: return "Переселение отменено";
            }
        }
    }

    public sealed partial class WorldState
    {
        public int ExpansionVersion, NextExpeditionId = 1, SettlementsFoundedByExpeditions;
        public List<SettlementExpeditionData> SettlementExpeditions = new List<SettlementExpeditionData>();
        public SettlementExpeditionData GetSettlementExpedition(int id)
        {
            if (SettlementExpeditions != null) foreach (SettlementExpeditionData expedition in SettlementExpeditions)
                if (expedition.Id == id) return expedition;
            return null;
        }
        private bool ValidateExpansion(out string error)
        {
            error = null;
            if (ExpansionVersion < 0 || ExpansionVersion > 1) { error = "Unsupported expansion version."; return false; }
            if (ExpansionVersion == 0)
            {
                if (SettlementExpeditions != null && SettlementExpeditions.Count != 0 || SettlementsFoundedByExpeditions != 0)
                { error = "Legacy world contains settlement expeditions."; return false; }
                foreach (UnitData unit in Units) if (unit.ExpeditionId != 0 || unit.ExpeditionLoaded || unit.ExpeditionResting)
                { error = "Legacy world contains a settler order."; return false; }
                foreach (SettlementData town in Settlements) if (town.ParentSettlementId != 0 || town.TerritoryRadius != 0 || town.NextExpansionTick != 0 || town.NextColonizationTick != 0)
                { error = "Legacy world contains expansion state."; return false; }
                return true;
            }
            if (NextExpeditionId < 1 || SettlementsFoundedByExpeditions < 0 || SettlementExpeditions == null || SettlementExpeditions.Count > 256)
            { error = "Invalid settlement expedition collection."; return false; }
            foreach (SettlementData town in Settlements)
                if (town.TerritoryRadius < 6 || town.TerritoryRadius > 24 || town.NextExpansionTick < 0 || town.NextColonizationTick < 0 || town.ParentSettlementId < 0 || town.ParentSettlementId == town.Id || town.ParentSettlementId != 0 && GetSettlement(town.ParentSettlementId) == null)
                { error = "Invalid settlement expansion state."; return false; }
            var ids = new HashSet<int>(); var members = new HashSet<int>();
            foreach (SettlementExpeditionData expedition in SettlementExpeditions)
            {
                if (expedition == null || expedition.Id <= 0 || expedition.Id >= NextExpeditionId || !ids.Add(expedition.Id) || GetSettlement(expedition.SourceSettlementId) == null || expedition.SourceBuildingId <= 0 || expedition.TargetCell < 0 || expedition.TargetCell >= Cells.Length || (int)expedition.Phase < 0 || (int)expedition.Phase > 5 || expedition.MemberIds == null || expedition.MemberIds.Count > ExpansionCatalog.Members || expedition.StartedTick < 0 || expedition.StartedTick > Tick || expedition.PhaseStartedTick < expedition.StartedTick || expedition.PhaseStartedTick > Tick || expedition.LastProgressTick < expedition.StartedTick || expedition.LastProgressTick > Tick || !Stock(expedition.FoundingProgress) || expedition.FoundingProgress > ExpansionCatalog.FoundingSeconds + .001f || !Stock(expedition.FoodConsumed) || !Stock(expedition.WaterConsumed) || !Stock(expedition.PlanksSpent) || expedition.PlanksSpent > ExpansionCatalog.PlanksRequired || expedition.DepartedMembers < 0 || expedition.DepartedMembers > ExpansionCatalog.Members || expedition.LastReason == null || expedition.LastReason.Length > 256)
                { error = "Invalid settlement expedition."; return false; }
                bool active = ExpansionCatalog.Active(expedition);
                if (active && expedition.MemberIds.Count == 0 || !active && expedition.MemberIds.Count != 0 || expedition.Phase == SettlementExpeditionPhase.Completed && (expedition.PlanksSpent != ExpansionCatalog.PlanksRequired || GetSettlement(expedition.FoundedSettlementId) == null) || expedition.Phase != SettlementExpeditionPhase.Completed && (expedition.FoundedSettlementId != 0 || expedition.PlanksSpent != 0))
                { error = "Invalid expedition completion state."; return false; }
                var local = new HashSet<int>();
                foreach (int id in expedition.MemberIds)
                {
                    UnitData unit = GetUnit(id);
                    if (!local.Add(id) || !members.Add(id) || unit == null || unit.ExpeditionId != expedition.Id || unit.SettlementId != expedition.SourceSettlementId || unit.Age < 16 || (int)unit.Species > 3 || unit.ShipId != 0 || unit.CaravanId != 0 || unit.InsideBuildingId != 0 || unit.EnteringBuildingId != 0)
                    { error = "Invalid settler membership."; return false; }
                }
            }
            foreach (UnitData unit in Units)
                if (unit.ExpeditionId < 0 || unit.ExpeditionId != 0 && !members.Contains(unit.Id) || unit.ExpeditionId == 0 && (unit.ExpeditionLoaded || unit.ExpeditionResting))
                { error = "Invalid settler reference."; return false; }
            return true;
        }
    }
}
