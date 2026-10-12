using System;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private void CleanupSocialDisputes()
        {
            if (World.SocialDisputes == null) return;
            for (int i = World.SocialDisputes.Count - 1; i >= 0; i--)
            {
                SocialDisputeData d = World.SocialDisputes[i]; UnitData a = World.GetUnit(d.InstigatorId), b = World.GetUnit(d.TargetId);
                if (!World.Laws.Warfare || World.Tick >= d.UntilTick || a == null || b == null || a.Health <= (d.Lethal ? 0 : a.MaxHealth * .35f) || b.Health <= (d.Lethal ? 0 : b.MaxHealth * .35f) || a.Age < 16 || b.Age < 16 || a.SettlementId != b.SettlementId) World.SocialDisputes.RemoveAt(i);
            }
        }
        private bool IsSocialDispute(int first, int second)
        {
            for (int i = 0; i < World.SocialDisputes.Count; i++) { SocialDisputeData d = World.SocialDisputes[i]; if ((d.InstigatorId == first && d.TargetId == second) || (d.InstigatorId == second && d.TargetId == first)) return true; }
            return false;
        }
        private bool IsLethalSocialDispute(int first, int second)
        {
            for (int i = 0; i < World.SocialDisputes.Count; i++) { SocialDisputeData d = World.SocialDisputes[i]; if (d.Lethal && ((d.InstigatorId == first && d.TargetId == second) || (d.InstigatorId == second && d.TargetId == first))) return true; }
            return false;
        }
        private UnitData FindDisputeEnemy(UnitData unit)
        {
            if (!World.Laws.Warfare || !IsCivilized(unit.Species)) return null;
            for (int i = 0; i < World.SocialDisputes.Count; i++)
            {
                SocialDisputeData d = World.SocialDisputes[i];
                int otherId = d.InstigatorId == unit.Id ? d.TargetId : d.TargetId == unit.Id ? d.InstigatorId : 0;
                if (unit.Age < 16) otherId = d.InstigatorId; // A child may flee a nearby brawl but never enters it as a combatant.
                UnitData other = World.GetUnit(otherId);
                if (other != null && other.Health > 0 && DistanceSquared(unit.X, unit.Y, other.X, other.Y) < 9) return other;
            }
            return null;
        }
        private void TryStartSocialDispute(SettlementData home)
        {
            if (!World.Laws.Warfare || home.ReligiousTolerance >= .25f || home.CrisisSeconds < 20 || World.Tick - home.LastDisputeTick < 300 || World.SocialDisputes.Count >= 64) return;
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData first = World.Units[i]; ReligionData faith = World.GetReligion(first.FaithId);
                if (!EligibleRuler(first, home.Id) || first.Health <= first.MaxHealth * .5f || first.Mood >= .35f || first.FaithCommitment < .8f || first.Ambition < .65f || first.Generosity > .35f || faith == null || faith.Tolerance > .4f || faith.Zeal < .6f) continue;
                for (int n = 0; n < World.Units.Count; n++)
                {
                    UnitData second = World.Units[n];
                    if (second.Id == first.Id || !EligibleRuler(second, home.Id) || second.Health <= second.MaxHealth * .5f || second.FaithId == 0 || second.FaithId == first.FaithId || DistanceSquared(first.X, first.Y, second.X, second.Y) > 4 || IsSocialDispute(first.Id, second.Id)) continue;
                    bool lethal = home.CrisisSeconds >= 45 && home.ReligiousTolerance <= .2f && faith.Zeal >= .82f && first.FaithCommitment >= .95f && first.Mood < .15f;
                    World.SocialDisputes.Add(new SocialDisputeData { InstigatorId = first.Id, TargetId = second.Id, UntilTick = World.Tick + 60, Lethal = lethal }); home.LastDisputeTick = World.Tick;
                    first.EventStress = Math.Min(1, first.EventStress + .25f); second.EventStress = Math.Min(1, second.EventStress + .3f);
                    AddSocialEvent(SocialEventKind.Dispute, home, first.Id, second.Id, "На фоне кризиса и нетерпимости спор " + first.Name + " и " + second.Name + (lethal ? " перерос в опасное фанатичное насилие." : " перерос в локальную драку."));
                    return;
                }
            }
        }
        private float LawPreference(UnitData voter, EconomicPolicy policy, float tax, ChildLaborPolicy child, ResourceFocus focus, float tolerance, SettlementData home)
        {
            bool wantsFree = voter.Generosity >= .5f || (home.Food < home.Population * 2 && voter.Prudence >= .4f);
            float score = (policy == EconomicPolicy.FreeDistribution) == wantsFree ? .6f : 0;
            score += (.3f - tax) * (.5f + voter.Generosity);
            bool supportsChildWork = home.Food < home.Population * 2 && voter.Prudence > .5f || voter.Ambition > .7f && voter.Generosity < .3f;
            if ((child == ChildLaborPolicy.LightWork) == supportsChildWork) score += .3f;
            ResourceFocus preference = voter.Prudence > .5f ? ResourceFocus.Balanced : (ResourceFocus)(StableRandom.Hash(World.Seed, voter.Id, 0, 6500) % 4);
            if (focus == preference) score += .1f;
            float desiredTolerance = voter.Generosity * .7f + voter.Prudence * .3f;
            score += (1 - Math.Abs(tolerance - desiredTolerance)) * .3f;
            return score;
        }
        public bool VoteOnLaws(int settlementId, EconomicPolicy policy, float taxRate, ChildLaborPolicy childLabor, ResourceFocus focus, float tolerance)
        {
            SettlementData s = World.GetSettlement(settlementId);
            if (s == null || s.Government != GovernmentType.Republic || (int)policy < 0 || (int)policy > 1 || float.IsNaN(taxRate) || taxRate < 0 || taxRate > .3f || (int)childLabor < 0 || (int)childLabor > 1 || (int)focus < 0 || (int)focus > 3 || float.IsNaN(tolerance) || tolerance < 0 || tolerance > 1) throw new ArgumentOutOfRangeException(nameof(settlementId));
            ParliamentDecisionData decision = new ParliamentDecisionData { Tick = World.Tick, RulerId = s.RulerId, EconomyPolicy = policy, TaxRate = taxRate, ChildLabor = childLabor, ResourceFocus = focus, ReligiousTolerance = tolerance };
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData voter = World.Units[i]; if (!EligibleRuler(voter, s.Id)) continue;
                float proposed = LawPreference(voter, policy, taxRate, childLabor, focus, tolerance, s);
                float existing = LawPreference(voter, s.EconomyPolicy, s.TaxRate, s.ChildLabor, s.ResourceFocus, s.ReligiousTolerance, s);
                bool supports = voter.Id == s.RulerId || proposed > existing + .0001f;
                decision.Votes.Add(new ParliamentVoteData { UnitId = voter.Id, Supports = supports }); if (supports) decision.Yes++; else decision.No++;
            }
            decision.AdultCount = decision.Votes.Count; decision.Passed = decision.Yes > decision.No && decision.AdultCount > 0; s.LastParliament = decision;
            if (decision.Passed)
            {
                s.EconomyPolicy = policy; s.TaxRate = taxRate; s.ChildLabor = childLabor; s.ResourceFocus = focus; s.ReligiousTolerance = tolerance;
                for (int i = 0; i < World.Units.Count; i++) if (World.Units[i].SettlementId == s.Id) EnforceChildWorkLaw(World.Units[i], s);
            }
            AddSocialEvent(SocialEventKind.Ballot, s, s.RulerId, 0, "Собрание взрослых: " + decision.Yes + " за, " + decision.No + " против. Законы " + (decision.Passed ? "приняты." : "не изменены."));
            World.Revision++; return decision.Passed;
        }
        private bool ResolveSocialPolicyProposal(SettlementData home, EconomicPolicy policy, float tax, ChildLaborPolicy child, ResourceFocus focus, float tolerance)
        {
            if (World.SocialVersion == 1 && home.Government == GovernmentType.Republic && home.RulerId != 0)
                return VoteOnLaws(home.Id, policy, tax, child, focus, tolerance);
            home.EconomyPolicy = policy; home.TaxRate = tax; home.ChildLabor = child; home.ResourceFocus = focus; home.ReligiousTolerance = tolerance; return true;
        }
    }
}
