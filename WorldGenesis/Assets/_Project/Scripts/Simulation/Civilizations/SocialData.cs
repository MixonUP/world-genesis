using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public enum SocialEventKind { Conversion, Dispute, Revolt, Ballot, WarLoss, Mastery, ReligionFounded }
    public enum SocialState { Stable, Discontent, Protest }
    [Serializable]
    public struct MoodFactors
    {
        public float Food, Water, Housing, Rest, Social, Laws, Safety, Stress, Target;
    }
    [Serializable]
    public sealed class ReligionData
    {
        public int Id, FounderId, FoundedSettlementId;
        public string Name, Deity, Doctrine;
        public float Tolerance, Zeal;
        public long FoundedTick;
    }
    [Serializable]
    public sealed class ParliamentVoteData { public int UnitId; public bool Supports; }
    [Serializable]
    public sealed class ParliamentDecisionData
    {
        public long Tick;
        public int RulerId, AdultCount, Yes, No;
        public bool Passed;
        public EconomicPolicy EconomyPolicy;
        public ChildLaborPolicy ChildLabor;
        public ResourceFocus ResourceFocus;
        public float TaxRate, ReligiousTolerance;
        public List<ParliamentVoteData> Votes = new List<ParliamentVoteData>();
    }
    [Serializable]
    public sealed class SocialEventData
    {
        public SocialEventKind Kind;
        public long Tick;
        public int SettlementId, UnitId, OtherId;
        public float X, Y;
        public string Text;
    }
    [Serializable]
    public sealed class SocialDisputeData { public int InstigatorId, TargetId; public long UntilTick; public bool Lethal; }

    public sealed partial class UnitData
    {
        public float Thirst, Mood, Trust, EventStress, FaithCommitment, FaithExposure, LearningTalent, CraftSkill;
        public int FaithId, PendingFaithId;
        public bool MasteryAnnounced;
        public MoodFactors MoodInputs;
        public bool IsMaster { get { return Age >= 16 && LearningTalent >= .96f && Education >= .6f && CraftSkill >= .8f; } }
    }
    public sealed partial class SettlementData
    {
        public float AverageMood, AverageTrust, Discontent, CrisisSeconds, RecentWarLosses, ReligiousTolerance;
        public long RevoltCooldownUntilTick, LastDisputeTick;
        public int RevoltCount, WarDeaths, MajorityFaithId, RevoltSupporters;
        public SocialState SocialState;
        public ParliamentDecisionData LastParliament = new ParliamentDecisionData();
    }
    public sealed partial class WorldState
    {
        public int SocialVersion;
        public int NextFaithId = 1;
        public List<ReligionData> Religions = new List<ReligionData>();
        public List<SocialEventData> SocialEvents = new List<SocialEventData>();
        public List<SocialDisputeData> SocialDisputes = new List<SocialDisputeData>();
        public ReligionData GetReligion(int id)
        { if (Religions != null) for (int i = 0; i < Religions.Count; i++) if (Religions[i].Id == id) return Religions[i]; return null; }

        private bool ValidateSocial(out string error)
        {
            error = null;
            if (SocialVersion < 0 || SocialVersion > 1) { error = "Unsupported social version."; return false; }
            if (SocialVersion == 0) return true;
            if (Religions == null || Religions.Count > 64 || NextFaithId < 1 || SocialEvents == null || SocialEvents.Count > 128 || SocialDisputes == null || SocialDisputes.Count > 64) { error = "Invalid social records."; return false; }
            HashSet<int> religionIds = new HashSet<int>();
            for (int i = 0; i < Religions.Count; i++)
            {
                ReligionData r = Religions[i];
                if (r == null || r.Id <= 0 || r.Id >= NextFaithId || !religionIds.Add(r.Id) || r.Name == null || r.Name.Length > 128 || r.Deity == null || r.Deity.Length > 128 || r.Doctrine == null || r.Doctrine.Length > 256 || !UnitRange(r.Tolerance) || !UnitRange(r.Zeal) || r.FounderId < 0 || r.FounderId >= NextId || r.FoundedTick < 0 || r.FoundedTick > Tick || (r.FounderId == 0 ? r.FoundedSettlementId != 0 || r.FoundedTick != 0 : GetSettlement(r.FoundedSettlementId) == null)) { error = "Invalid fictional religion."; return false; }
            }
            for (int i = 0; i < Units.Count; i++)
            {
                UnitData u = Units[i]; ReligionData faith = GetReligion(u.FaithId);
                if (!UnitRange(u.Thirst) || !UnitRange(u.Mood) || !UnitRange(u.Trust) || !UnitRange(u.EventStress) || !UnitRange(u.FaithCommitment) || !UnitRange(u.FaithExposure) || !UnitRange(u.LearningTalent) || !UnitRange(u.CraftSkill) || (u.FaithId != 0 && (faith == null || faith.FounderId == 0)) || (u.PendingFaithId != 0 && GetReligion(u.PendingFaithId) == null)) { error = "Invalid citizen social state."; return false; }
                MoodFactors m = u.MoodInputs;
                if (!UnitRange(m.Food) || !UnitRange(m.Water) || !UnitRange(m.Housing) || !UnitRange(m.Rest) || !UnitRange(m.Social) || !UnitRange(m.Laws) || !UnitRange(m.Safety) || !UnitRange(m.Stress) || !UnitRange(m.Target)) { error = "Invalid mood factors."; return false; }
            }
            for (int i = 0; i < Settlements.Count; i++)
            {
                SettlementData s = Settlements[i]; ParliamentDecisionData p = s.LastParliament;
                if (!UnitRange(s.AverageMood) || !UnitRange(s.AverageTrust) || !UnitRange(s.Discontent) || !UnitRange(s.ReligiousTolerance) || !Stock(s.CrisisSeconds) || !Stock(s.RecentWarLosses) || s.RevoltCount < 0 || s.WarDeaths < 0 || s.RevoltSupporters < 0 || s.RevoltCooldownUntilTick < 0 || s.LastDisputeTick < 0 || s.LastDisputeTick > Tick || (int)s.SocialState < 0 || (int)s.SocialState > 2 || (s.MajorityFaithId != 0 && GetReligion(s.MajorityFaithId) == null)) { error = "Invalid settlement social state."; return false; }
                if (p == null || p.Votes == null || p.Votes.Count > 10000 || p.Tick < 0 || p.Tick > Tick || p.RulerId < 0 || p.RulerId >= NextId || p.AdultCount != p.Votes.Count || p.Yes < 0 || p.No < 0 || p.Yes + p.No != p.AdultCount || p.Passed != (p.Yes > p.No && p.AdultCount > 0) || (int)p.EconomyPolicy < 0 || (int)p.EconomyPolicy > 1 || !UnitRange(p.ReligiousTolerance) || !Finite(p.TaxRate) || p.TaxRate < 0 || p.TaxRate > .3f || (int)p.ChildLabor < 0 || (int)p.ChildLabor > 1 || (int)p.ResourceFocus < 0 || (int)p.ResourceFocus > 3) { error = "Invalid parliamentary record."; return false; }
                HashSet<int> voters = new HashSet<int>(); int yes = 0;
                for (int v = 0; v < p.Votes.Count; v++) { ParliamentVoteData vote = p.Votes[v]; if (vote == null || vote.UnitId <= 0 || vote.UnitId >= NextId || !voters.Add(vote.UnitId)) { error = "Invalid ballot voter."; return false; } if (vote.Supports) yes++; }
                if (yes != p.Yes) { error = "Ballot count does not match votes."; return false; }
            }
            for (int i = 0; i < SocialDisputes.Count; i++)
            {
                SocialDisputeData d = SocialDisputes[i]; UnitData a = d == null ? null : GetUnit(d.InstigatorId), b = d == null ? null : GetUnit(d.TargetId);
                if (d == null || a == null || b == null || a.Id == b.Id || a.Age < 16 || b.Age < 16 || a.Health <= 0 || b.Health <= 0 || a.SettlementId == 0 || a.SettlementId != b.SettlementId || d.UntilTick < Tick || d.UntilTick - Tick > 100) { error = "Invalid local adult dispute."; return false; }
            }
            for (int i = 0; i < SocialEvents.Count; i++)
            {
                SocialEventData e = SocialEvents[i];
                if (e == null || (int)e.Kind < 0 || (int)e.Kind > 6 || e.Tick < 0 || e.Tick > Tick || GetSettlement(e.SettlementId) == null || e.UnitId < 0 || e.UnitId >= NextId || e.OtherId < 0 || e.OtherId >= NextId || !Finite(e.X) || !Finite(e.Y) || e.Text == null || e.Text.Length > 512) { error = "Invalid social event."; return false; }
            }
            return true;
        }
    }
}
