using System;
using System.Linq;
using WorldGenesis.Simulation;
#if UNITY_5_3_OR_NEWER
using NUnit.Framework;
using UnityEngine;
#endif

namespace WorldGenesis.Tests
{
    // The same scenarios run against production core under .NET and Unity; only the serializer differs.
    public static class SocialScenarios
    {
        public static Func<WorldState, WorldState> Clone;
        public static Func<WorldState, string> Snapshot;
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static void Valid(WorldState w) { Require(w.Validate(out string error), error); }
        private static SimulationEngine Town(int count = 6)
        {
            WorldState w = WorldGenerator.Generate(731, 48, 48);
            for (int i = 0; i < w.Cells.Length; i++) { w.Cells[i].Terrain = TerrainType.Grass; w.Cells[i].Vegetation = .5f; w.Cells[i].River = false; }
            ResourceCatalog.Generate(w);
            w.Laws.Births = w.Laws.Aging = w.Laws.Warfare = false;
            var e = new SimulationEngine(w); e.Spawn(SpeciesType.Human, 14, 14, count); Freeze(e);
            return e;
        }
        private static void Freeze(SimulationEngine e)
        {
            foreach (UnitData u in e.World.Units) { u.Age = 25; u.Task = UnitTask.Idle; u.Path.Clear(); u.PathCursor = 0; u.DecisionTimer = 10000; u.Hunger = .1f; u.Fatigue = .1f; u.SocialNeed = .1f; u.Thirst = .1f; }
            foreach (SettlementData s in e.World.Settlements) { s.Government = GovernmentType.Feudal; s.NextElectionTick = 0; s.NextPolicyReviewTick = 100000; s.PolicyRulerId = s.RulerId; s.Food = 200; s.Wood = 0; }
        }
        private static void Steps(SimulationEngine e, int ticks) { for (int t = 0; t < ticks; t++) e.Step(.1f); }
        private static ReligionData Found(WorldState w, UnitData founder, int index)
        {
            ReligionData r = w.Religions[index]; r.FounderId = founder.Id; r.FoundedSettlementId = founder.SettlementId; r.FoundedTick = w.Tick;
            founder.FaithId = r.Id; founder.FaithCommitment = .9f; return r;
        }

        public static void LegacySocialMigrationConsumesNoRandomIdsOrGoods()
        {
            var e = Town(); WorldState w = Clone(e.World); w.SocialVersion = 0; w.Religions = null; w.SocialEvents = null; w.SocialDisputes = null;
            w.Units[0].Education = .43f; uint rng = w.RngState; int next = w.NextId; float food = w.Settlements[0].Food;
            var migrated = new SimulationEngine(w); Require(w.SocialVersion == 1 && w.Units[0].Thirst == .15f && w.Units[0].Education == .43f, "Social defaults reset old education or fail to hydrate migration");
            Require(w.RngState == rng && w.NextId == next && w.Settlements[0].Food == food, "Migration consumed gameplay random, IDs or goods");
            string first = Snapshot(w); new SimulationEngine(w); Require(Snapshot(w) == first, "Migration is not idempotent"); Valid(w);
        }
        public static void MoodUsesNeedsLawsAndActualHousing()
        {
            var e = Town(2); e.World.Settlements[0].TaxRate = 0; var good = e.World.Units[0]; var bad = e.World.Units[1];
            bad.Hunger = .9f; bad.Thirst = 1; bad.Fatigue = .9f; bad.SocialNeed = 1; bad.EventStress = 1; good.Mood = bad.Mood = .5f;
            Steps(e, 10);
            Require(good.Mood > bad.Mood && good.MoodInputs.Food > bad.MoodInputs.Food && good.MoodInputs.Water > bad.MoodInputs.Water && good.MoodInputs.Rest > bad.MoodInputs.Rest && good.MoodInputs.Social > bad.MoodInputs.Social && bad.MoodInputs.Stress > good.MoodInputs.Stress, "Need factors did not influence mood");
            Require(Math.Abs(good.MoodInputs.Housing - e.World.GetHousingComfort(good)) < .001f, "Housing factor detached from assigned building");
            float before = good.MoodInputs.Laws; e.World.Settlements[0].TaxRate = .3f; Steps(e, 10); Require(good.MoodInputs.Laws < before, "Tax burden ignored by law satisfaction"); Valid(e.World);
        }
        public static void RepublicCountsAdultsAndRequiresStrictMajority()
        {
            var e = Town(5); WorldState w = e.World; SettlementData s = w.Settlements[0]; s.Government = GovernmentType.Republic; s.NextElectionTick = 600;
            s.EconomyPolicy = EconomicPolicy.Market; s.TaxRate = 0; s.ChildLabor = ChildLaborPolicy.Forbidden; s.ResourceFocus = ResourceFocus.Balanced; s.ReligiousTolerance = .5f;
            UnitData ruler = w.GetUnit(s.RulerId); int changed = 0;
            foreach (UnitData u in w.Units) { u.Generosity = .1f; u.Prudence = 1; u.Ambition = 0; if (u != ruler && changed++ == 0) u.Generosity = 1; }
            UnitData child = w.Units.Last(u => u != ruler && u.Generosity < .5f); child.Age = 10; child.Profession = UnitProfession.None; child.Generosity = 1;
            bool passed = e.VoteOnLaws(s.Id, EconomicPolicy.FreeDistribution, 0, ChildLaborPolicy.Forbidden, ResourceFocus.Balanced, .5f);
            Require(!passed && s.LastParliament.Yes == 2 && s.LastParliament.No == 2 && s.LastParliament.AdultCount == 4 && s.LastParliament.Votes.All(v => v.UnitId != child.Id), "Tie or child vote passed");
            Require(s.EconomyPolicy == EconomicPolicy.Market, "Rejected proposal changed law");
            w.Units.First(u => u != child && u != ruler && u.Generosity < .5f).Generosity = 1;
            Require(e.VoteOnLaws(s.Id, EconomicPolicy.FreeDistribution, 0, ChildLaborPolicy.Forbidden, ResourceFocus.Balanced, .5f) && s.LastParliament.Yes == 3 && s.EconomyPolicy == EconomicPolicy.FreeDistribution, "Actual majority did not enact law"); Valid(w);
        }
        public static void ReligionsEmergeGraduallyAndCanExceedThree()
        {
            var e = Town(); e.Spawn(SpeciesType.Elf, 35, 12, 5); e.Spawn(SpeciesType.Dwarf, 12, 35, 5); e.Spawn(SpeciesType.Orc, 35, 35, 5); Freeze(e);
            Require(e.World.Religions.All(r => r.FounderId == 0), "Faith was founded before an event"); Steps(e, 590); Require(e.World.Religions.All(r => r.FounderId == 0), "Faith appeared before founding threshold"); Steps(e, 20);
            Require(e.World.Religions.Count(r => r.FounderId != 0) == 4 && e.World.SocialEvents.Count(x => x.Kind == SocialEventKind.ReligionFounded) == 4, "Fourth city could not found its own faith");
            foreach (ReligionData r in e.World.Religions) Require(e.World.GetUnit(r.FounderId).Age >= 16 && r.FoundedTick >= 600, "Founder is not an actual adult"); Valid(e.World);
        }
        public static void ConversionRequiresActualAdultConversation()
        {
            var e = Town(3); WorldState w = e.World; UnitData source = w.Units[0], target = w.Units[1], child = w.Units[2]; Found(w, source, 0);
            source.X = target.X = 15.5f; source.Y = target.Y = 15.5f;
            source.Task = UnitTask.Socialize; source.TargetId = target.Id; source.TargetCell = w.Index(15, 15); source.WorkTimer = 12;
            Steps(e, 65); Require(target.FaithId == source.FaithId && w.SocialEvents.Count(x => x.Kind == SocialEventKind.Conversion) == 1, "On-site repeated dialogue did not convert");
            child.Age = 10; child.Profession = UnitProfession.None; child.X = source.X; child.Y = source.Y; source.TargetId = child.Id; source.WorkTimer = 12; source.Task = UnitTask.Socialize;
            Steps(e, 65); Require(child.FaithId == 0, "Child was treated as adult proselytizing target"); Valid(w);
        }
        public static void ReligiousDifferenceAloneIsPeacefulAndCrisisBrawlIsBounded()
        {
            var e = Town(5); WorldState w = e.World; SettlementData s = w.Settlements[0]; UnitData a = w.Units[0], b = w.Units[1], child = w.Units[2];
            ReligionData r = Found(w, a, 0); Found(w, b, 1); a.X = b.X = child.X = 16.5f; a.Y = b.Y = child.Y = 16.5f; child.Age = 10; child.Profession = UnitProfession.None;
            w.Laws.Warfare = true; Steps(e, 310); Require(a.Health == a.MaxHealth && b.Health == b.MaxHealth && w.SocialDisputes.Count == 0, "Different faiths triggered unconditional fighting");
            r.Tolerance = .1f; r.Zeal = .9f; a.Ambition = .9f; a.Generosity = .1f; a.FaithCommitment = .95f; a.Mood = .1f; s.ReligiousTolerance = .1f; s.CrisisSeconds = 30;
            Steps(e, 10); Require(w.SocialDisputes.Count == 1, "Conditional intolerance crisis did not trigger local dispute");
            Steps(e, 1); Require(a.Health < a.MaxHealth || b.Health < b.MaxHealth, "Dispute had no actual health consequence"); Require(child.Task == UnitTask.Flee && child.Health == child.MaxHealth, "Child entered dispute as combatant");
            Steps(e, 65); Require(a.Health >= a.MaxHealth * .35f && b.Health >= b.MaxHealth * .35f && w.SocialDisputes.Count == 0, "Local brawl killed citizens or failed to expire"); Valid(w);
        }
        public static void ExtremeFanaticalCrisisCanCauseLethalAdultViolence()
        {
            var e = Town(5); WorldState w = e.World; SettlementData s = w.Settlements[0]; UnitData a = w.Units[0], b = w.Units[1];
            ReligionData faith = Found(w, a, 0); Found(w, b, 1); faith.Tolerance = .1f; faith.Zeal = .89f; a.FaithCommitment = .99f; a.Ambition = .9f; a.Generosity = .1f; a.Mood = .05f;
            a.X = b.X = 16.5f; a.Y = b.Y = 16.5f; s.ReligiousTolerance = .1f; w.Laws.Warfare = true; w.Tick = 300; s.CrisisSeconds = 50; Steps(e, 10);
            Require(w.SocialDisputes.Count == 1 && w.SocialDisputes[0].Lethal, "Extreme crisis did not escalate beyond ordinary brawl");
            b.Health = 1; a.AttackTimer = 0; e.Step(.1f); Require(w.GetUnit(b.Id) == null && s.WarDeaths == 0, "Lethal dispute was impossible or mislabeled external war"); Valid(w);
        }
        public static void SustainedMajorityCrisisChangesRegimeWithCooldown()
        {
            var e = Town(); WorldState w = e.World; SettlementData s = w.Settlements[0]; int ruler = s.RulerId; int nextId = w.NextId;
            for (int t = 0; t < 610; t++)
            {
                // Prepared political stress fixture: hold health so starvation death cannot remove the electorate.
                foreach (UnitData u in w.Units) { u.Health = u.MaxHealth; u.Mood = .05f; u.Trust = .05f; u.Hunger = .9f; u.Thirst = 1; u.Fatigue = 1; u.SocialNeed = 1; u.EventStress = 1; u.DecisionTimer = 10000; }
                e.Step(.1f); if (t < 599) Require(s.RevoltCount == 0, "Revolt ignored sustained crisis threshold");
            }
            Require(s.RevoltCount == 1 && s.Government == GovernmentType.Republic && s.RulerId != ruler && s.RevoltSupporters > s.Population / 2 && s.RevoltCooldownUntilTick > w.Tick, "Sustained majority did not replace government: count=" + s.RevoltCount + " government=" + s.Government + " ruler=" + s.RulerId + "/" + ruler + " crisis=" + s.CrisisSeconds + " adults=" + s.Population);
            Require(w.Units.Count == 6 && w.NextId == nextId, "Revolt duplicated people or allocated fake entities");
            Steps(e, 200); Require(s.RevoltCount == 1, "Cooldown failed"); Valid(w);
        }
        public static void LearningNeedsMentorAndMasterNeedsBothEducationAndPractice()
        {
            var e = Town(2); WorldState w = e.World; UnitData pupil = w.Units[0], mentor = w.Units[1];
            pupil.Education = .2f; mentor.Education = .7f; pupil.X = 16.5f; pupil.Y = 16.5f; mentor.X = 35.5f; mentor.Y = 35.5f; pupil.Task = UnitTask.Learn; pupil.WorkTimer = 20;
            Steps(e, 10); Require(pupil.Education == .2f, "Distant mentor trained without contact"); mentor.X = pupil.X; mentor.Y = pupil.Y;
            Steps(e, 10); Require(pupil.Education > .2f, "Actual nearby mentor failed to teach"); float low = e.GetLearningMultiplier(pupil); pupil.Education = .7f; Require(e.GetLearningMultiplier(pupil) > low, "Learning did not accelerate with education");
            pupil.LearningTalent = .99f; pupil.CraftSkill = .7999f; Require(!pupil.IsMaster, "Talent and school alone created master");
            BuildingData farm = w.Buildings.First(x => x.Type == BuildingType.Farm); farm.HarvestReady = 4; pupil.X = farm.X + .5f; pupil.Y = farm.Y + .5f; pupil.Task = UnitTask.Farm; pupil.TargetCell = w.Index(farm.X, farm.Y); pupil.CarriedFood = 0; pupil.WorkTimer = 0;
            Steps(e, 10); Require(pupil.IsMaster && pupil.MasteryAnnounced && w.SocialEvents.Count(x => x.Kind == SocialEventKind.Mastery) == 1, "Actual practice did not unlock or announce master");
            float skill = pupil.CraftSkill; pupil.Task = UnitTask.Idle; pupil.DecisionTimer = 10000; Steps(e, 10); Require(pupil.CraftSkill == skill, "Idle time generated craft experience"); Valid(w);
        }
        public static void WarDeathsReduceTrustOnlyForActualWarLoss()
        {
            var e = Town(2); e.Spawn(SpeciesType.Orc, 35, 35, 2); Freeze(e); WorldState w = e.World; UnitData attacker = w.Units[0], victim = w.Units[2], witness = w.Units[3]; SettlementData home = w.GetSettlement(victim.SettlementId);
            attacker.X = victim.X = 24.5f; attacker.Y = victim.Y = 24.5f; victim.Health = 1; attacker.AttackTimer = 0; float trust = witness.Trust;
            w.Laws.Warfare = true; e.SetDiplomacy(attacker.SettlementId, victim.SettlementId, true); e.Step(.1f);
            Require(home.WarDeaths == 1 && home.RecentWarLosses == 1 && witness.Trust < trust && w.SocialEvents.Count(x => x.Kind == SocialEventKind.WarLoss) == 1, "Military loss did not affect trust exactly once");
            Steps(e, 10); Require(home.WarDeaths == 1, "Corpse counted repeatedly"); e.ApplyPower(PowerType.Lightning, (int)witness.X, (int)witness.Y, 1); Require(home.WarDeaths == 1, "Power damage counted as war casualty"); Valid(w);
        }
        public static void WallsBurnAndTerrainPaintPreservesValidWallOwnership()
        {
            var e = Town(); WorldState w = e.World; SettlementData s = w.Settlements[0];
            var wall = new BuildingData { Id = w.NextId++, SettlementId = s.Id, X = 25, Y = 25, Type = BuildingType.WoodWall, Level = 1 };
            w.Buildings.Add(wall); w.Cells[w.Index(25, 25)].BuildingId = wall.Id; w.Cells[w.Index(25, 25)].SettlementId = s.Id; w.ResourceNodes[w.Index(25, 25)] = new ResourceNodeData();
            Require(!w.IsWalkable(25, 25), "Wall should block walking"); e.Paint(TerrainType.Grass, 25, 25, 0);
            Require(w.GetBuilding(wall.Id) != null && w.Cells[w.Index(25, 25)].SettlementId == s.Id, "Dry paint deleted valid wall ownership"); e.ApplyPower(PowerType.Fire, 25, 25, 1); Require(w.Cells[w.Index(25, 25)].Fire > 0, "Wall was immune to ignition");
            Steps(e, 10); Require(wall.Health < 100, "Burning wall took no damage"); e.ApplyPower(PowerType.Rain, 25, 25, 1); wall.Type = BuildingType.Gate; wall.GateOpen = false; e.ApplyPower(PowerType.Lightning, 25, 25, 1);
            Require(w.Cells[w.Index(25, 25)].Fire > 0, "Closed gate was immune to lightning ignition"); Valid(w);
        }
        public static void SocialValidationRejectsCorruptFaithMoodAndBallot()
        {
            var e = Town(); WorldState w = Clone(e.World); w.Units[0].Mood = float.NaN; Require(!w.Validate(out _), "NaN mood accepted"); w = Clone(e.World); w.Units[0].FaithId = 999; Require(!w.Validate(out _), "Unknown faith accepted");
            w = Clone(e.World); w.Settlements[0].LastParliament.Yes = 1; Require(!w.Validate(out _), "Forged vote tally accepted");
            w = Clone(e.World); w.Religions[1].Id = w.Religions[0].Id; Require(!w.Validate(out _), "Duplicate faith ID accepted");
        }
        public static void WorkersChooseReadyHarvestWithoutCrowdingAnEmptyField()
        {
            var e = Town(3); WorldState w = e.World; w.NaturalWildlife = false; SettlementData s = w.Settlements[0]; s.Wood = 200;
            BuildingData farm = w.Buildings.First(b => b.Type == BuildingType.Farm);
            UnitData first = w.Units[0], second = w.Units[1]; first.Profession = second.Profession = UnitProfession.Farmer;
            first.DecisionTimer = 0; farm.HarvestReady = 0; farm.CropGrowth = .1f; e.Step(.1f);
            Require(first.Task != UnitTask.Farm, "A growing field monopolized a worker despite having no harvest");
            e = Town(3); w = e.World; w.NaturalWildlife = false; s = w.Settlements[0]; s.Wood = 200;
            farm = w.Buildings.First(b => b.Type == BuildingType.Farm); first = w.Units[0]; second = w.Units[1]; first.Profession = second.Profession = UnitProfession.Farmer;
            first.DecisionTimer = second.DecisionTimer = 0; farm.HarvestReady = 1; farm.CropGrowth = 1;
            e.Step(.1f);
            Require(first.Task == UnitTask.Farm && first.TargetCell == w.Index(farm.X, farm.Y), "Ready crop was not selected");
            Require(second.Task != UnitTask.Farm, "Two bare-handed workers were assigned the same single remaining unit"); Valid(w);
        }
        public static void AdditionalFieldPrecedesExtraHousingAndConsumesRealPlanks()
        {
            var e = Town(9); WorldState w = e.World; w.NaturalWildlife = false; SettlementData s = w.Settlements[0];
            foreach (BuildingType type in new[] { BuildingType.Sawmill, BuildingType.House })
            {
                int x = type == BuildingType.House ? 20 : 22, y = 20;
                var b = new BuildingData { Id = w.NextId++, SettlementId = s.Id, Type = type, X = x, Y = y };
                w.Buildings.Add(b); w.Cells[w.Index(x,y)].BuildingId = b.Id; w.Cells[w.Index(x,y)].SettlementId = s.Id; w.ResourceNodes[w.Index(x,y)] = new ResourceNodeData();
            }
            s.Wood = 10; s.BuildTimer = 8; Steps(e, 10);
            Require(w.Buildings.Count(b => b.SettlementId == s.Id && b.Type == BuildingType.Farm) == 2, "Elective infrastructure or more housing starved the second crop field");
            Require(w.Buildings.Count(b => b.SettlementId == s.Id && b.Type == BuildingType.House) == 1 && Math.Abs(s.Wood) < .001f, "Field bypassed its physical ten-plank cost"); Valid(w);
        }
        public static void DistantSavedCarrierReturnsByRouteWithoutLosingGoods()
        {
            WorldState w = WorldGenerator.Generate(572, 128, 128);
            for (int i=0;i<w.Cells.Length;i++) { w.Cells[i].Terrain=TerrainType.Grass; w.Cells[i].Vegetation=.5f; }
            ResourceCatalog.Generate(w); w.Laws.Aging=w.Laws.Births=w.Laws.Warfare=false;
            var e=new SimulationEngine(w); e.Spawn(SpeciesType.Human,14,14,2); Freeze(e); w.NaturalWildlife=false;
            UnitData carrier=w.Units[0]; carrier.X=100.5f; carrier.Y=100.5f; carrier.HarvestProduct=AgriculturalProduct.Hide; carrier.HarvestAmount=1; carrier.DecisionTimer=0;
            BuildingData hall=w.Buildings.First(b=>b.Type==BuildingType.Hall); e.Step(.1f);
            Require(carrier.Task==UnitTask.Return && carrier.Path.Count>150 && carrier.PathCursor<carrier.Path.Count, "Old distant carrier failed to find a real return route");
            Require(carrier.X>99 && carrier.Y>99 && carrier.HarvestAmount==1, "Return teleported the worker or goods");
            for(int t=0;t<2500 && carrier.HarvestAmount>0;t++) e.Step(.1f);
            Require(carrier.HarvestAmount==0 && hall.AgriculturalStock[(int)AgriculturalProduct.Hide]==1, "Physical distant delivery lost or duplicated cargo"); Valid(w);
        }
        public static void SocialSaveContinuesAllStateDeterministically()
        {
            var a = Town(20); foreach (UnitData u in a.World.Units) u.DecisionTimer = 0; Steps(a, 650); Valid(a.World);
            var b = new SimulationEngine(Clone(a.World)); for (int i = 0; i < 900; i++) { a.Step(.1f); b.Step(.1f); if (i % 100 == 0) { Valid(a.World); Valid(b.World); } }
            Require(Snapshot(a.World) == Snapshot(b.World), "Social continuation depends on hidden state or loses saved data");
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class SocialSimulationTests
    {
        [SetUp] public void SetSerializer() { SocialScenarios.Clone = w => JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w)); SocialScenarios.Snapshot = w => JsonUtility.ToJson(w); }
        [Test] public void LegacyMigration() { SocialScenarios.LegacySocialMigrationConsumesNoRandomIdsOrGoods(); }
        [Test] public void NeedDrivenMood() { SocialScenarios.MoodUsesNeedsLawsAndActualHousing(); }
        [Test] public void MajorityAndTieVoting() { SocialScenarios.RepublicCountsAdultsAndRequiresStrictMajority(); }
        [Test] public void GradualProceduralReligions() { SocialScenarios.ReligionsEmergeGraduallyAndCanExceedThree(); }
        [Test] public void RealConversationConversion() { SocialScenarios.ConversionRequiresActualAdultConversation(); }
        [Test] public void BoundedAdultDispute() { SocialScenarios.ReligiousDifferenceAloneIsPeacefulAndCrisisBrawlIsBounded(); }
        [Test] public void ConditionalLethalFanaticalViolence() { SocialScenarios.ExtremeFanaticalCrisisCanCauseLethalAdultViolence(); }
        [Test] public void SustainedRevolt() { SocialScenarios.SustainedMajorityCrisisChangesRegimeWithCooldown(); }
        [Test] public void LearningAndMastery() { SocialScenarios.LearningNeedsMentorAndMasterNeedsBothEducationAndPractice(); }
        [Test] public void WarLossTrust() { SocialScenarios.WarDeathsReduceTrustOnlyForActualWarLoss(); }
        [Test] public void CombustibleWallsAndGates() { SocialScenarios.WallsBurnAndTerrainPaintPreservesValidWallOwnership(); }
        [Test] public void RejectCorruptSocialState() { SocialScenarios.SocialValidationRejectsCorruptFaithMoodAndBallot(); }
        [Test] public void AvailableHarvestAndWorkerCapacity() { SocialScenarios.WorkersChooseReadyHarvestWithoutCrowdingAnEmptyField(); }
        [Test] public void RealSecondFieldPriority() { SocialScenarios.AdditionalFieldPrecedesExtraHousingAndConsumesRealPlanks(); }
        [Test] public void RecoverDistantCarrierByWalking() { SocialScenarios.DistantSavedCarrierReturnsByRouteWithoutLosingGoods(); }
        [Test] public void JsonUtilitySocialContinuation() { SocialScenarios.SocialSaveContinuesAllStateDeterministically(); }
    }
#endif
}
