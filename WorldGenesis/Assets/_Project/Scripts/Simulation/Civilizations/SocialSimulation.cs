using System;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        public const float RevoltCrisisSeconds = 60;
        public const long RevoltCooldownTicks = 1200;
        private static readonly string[] FaithNames = { "Хранители рассвета", "Путь тихих рек", "Круг тёплого камня", "Завет звёздных ветвей", "Голос янтарной луны", "Песнь семи ручьёв" };
        private static readonly string[] DeityNames = { "Аэлора", "Тирвен", "Омбраэль", "Сайрен", "Луэта", "Велорин" };
        private static readonly string[] Doctrines = { "Беречь очаг и делиться опытом", "Искать согласие и чистую воду", "Чтить труд и смену времён года", "Помнить предков и помогать соседям", "Искать мудрость в звёздах", "Хранить обещания и заботиться о детях" };
        private readonly System.Collections.Generic.Dictionary<int, int> socialFaithTally = new System.Collections.Generic.Dictionary<int, int>();
        private ReligionData CreateFaithDefinition()
        {
            int id = World.NextFaithId++; uint hash = StableRandom.Hash(World.Seed, id, 0, 6300);
            ReligionData faith = new ReligionData { Id = id, Name = FaithNames[hash % 6] + " " + id, Deity = DeityNames[(hash >> 8) % 6], Doctrine = Doctrines[(hash >> 16) % 6], Tolerance = .15f + StableRandom.Coordinate(World.Seed, id, 0, 6301) * .7f, Zeal = .3f + StableRandom.Coordinate(World.Seed, id, 0, 6302) * .6f };
            World.Religions.Add(faith); return faith;
        }

        private void MigrateSocial()
        {
            if (World.SocialVersion == 1) return;
            World.Religions = new System.Collections.Generic.List<ReligionData>();
            World.SocialEvents = new System.Collections.Generic.List<SocialEventData>();
            World.SocialDisputes = new System.Collections.Generic.List<SocialDisputeData>();
            World.NextFaithId = 1;
            for (int i = 0; i < 3; i++) CreateFaithDefinition();
            for (int i = 0; i < World.Settlements.Count; i++) InitializeSocialSettlement(World.Settlements[i]);
            for (int i = 0; i < World.Units.Count; i++) InitializeSocialUnit(World.Units[i]);
            World.SocialVersion = 1; World.Revision++;
        }
        private void InitializeSocialSettlement(SettlementData s)
        {
            s.AverageMood = .65f; s.AverageTrust = .6f; s.Discontent = .365f; s.ReligiousTolerance = .6f;
            s.LastParliament = new ParliamentDecisionData();
        }
        private void InitializeSocialUnit(UnitData u)
        {
            u.Thirst = .15f; u.Mood = .65f; u.Trust = .6f; u.EventStress = 0;
            u.LearningTalent = StableRandom.Coordinate(World.Seed, u.Id, 0, 6303);
            u.CraftSkill = 0; u.FaithId = u.PendingFaithId = 0; u.FaithExposure = 0; u.FaithCommitment = 0;
            u.MoodInputs = new MoodFactors { Food = 1 - u.Hunger, Water = .85f, Housing = .5f, Rest = 1 - u.Fatigue, Social = 1 - u.SocialNeed, Laws = .6f, Safety = 1, Target = .65f };
            SettlementData home = World.GetSettlement(u.SettlementId);
            if (u.Age < 16 && home != null && home.MajorityFaithId != 0) { u.FaithId = home.MajorityFaithId; u.FaithCommitment = .25f; }
        }
        private void AddSocialEvent(SocialEventKind kind, SettlementData home, int unitId, int otherId, string text)
        {
            if (home == null) return;
            World.SocialEvents.Add(new SocialEventData { Kind = kind, Tick = World.Tick, SettlementId = home.Id, UnitId = unitId, OtherId = otherId, X = home.X + .5f, Y = home.Y + .5f, Text = text });
            if (World.SocialEvents.Count > 128) World.SocialEvents.RemoveAt(0);
        }
        private float LawWellbeing(UnitData unit, SettlementData home)
        {
            if (home == null) return .5f;
            float value = .75f - home.TaxRate * .7f;
            value += home.EconomyPolicy == EconomicPolicy.FreeDistribution ? (unit.Generosity - .5f) * .16f : (.5f - unit.Generosity) * .16f;
            if (home.ChildLabor == ChildLaborPolicy.LightWork) value -= unit.Age < 16 ? .15f : unit.Generosity * .12f;
            if (unit.FaithId != 0 && home.MajorityFaithId != 0 && unit.FaithId != home.MajorityFaithId) value -= (1 - home.ReligiousTolerance) * .3f;
            return WorldGenerator.Clamp01(value);
        }
        private void UpdateSocial(float dt)
        {
            CleanupSocialDisputes();
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData u = World.Units[i]; if (!IsCivilized(u.Species) || u.Health <= 0) continue;
                SettlementData home = World.GetSettlement(u.SettlementId);
                u.EventStress = Math.Max(0, u.EventStress - dt * .008f);
                float fire = World.Cells[World.Index((int)u.X, (int)u.Y)].Fire;
                if (fire > .1f) u.EventStress = Math.Max(u.EventStress, fire * .6f);
                MoodFactors m = new MoodFactors { Food = 1 - u.Hunger, Water = .7f * (1 - u.Thirst) + .3f * World.GetWaterAccess(u), Housing = World.GetHousingComfort(u), Rest = 1 - u.Fatigue, Social = 1 - u.SocialNeed, Laws = LawWellbeing(u, home), Safety = Math.Min(1, u.Health / u.MaxHealth) * (u.Task == UnitTask.Flee || u.Task == UnitTask.Fight ? .4f : 1), Stress = u.EventStress };
                m.Target = WorldGenerator.Clamp01(m.Food * .28f + m.Water * .12f + m.Housing * .14f + m.Rest * .13f + m.Social * .1f + m.Laws * .12f + m.Safety * .11f - m.Stress * .3f);
                u.MoodInputs = m; u.Mood += (m.Target - u.Mood) * Math.Min(1, dt * .08f);
                float loss = home == null ? 0 : home.RecentWarLosses / Math.Max(1, home.Population);
                float trustTarget = WorldGenerator.Clamp01(.15f + u.Mood * .75f + (m.Laws - .5f) * .15f - loss * 1.5f);
                u.Trust += (trustTarget - u.Trust) * Math.Min(1, dt * .035f);
            }
            for (int i = 0; i < World.Settlements.Count; i++)
            {
                SettlementData s = World.Settlements[i]; float mood = 0, trust = 0; int adults = 0, unhappy = 0, count = 0;
                socialFaithTally.Clear();
                for (int n = 0; n < World.Units.Count; n++)
                {
                    UnitData u = World.Units[n]; if (u.SettlementId != s.Id || u.Health <= 0 || !IsCivilized(u.Species)) continue;
                    count++; mood += u.Mood; trust += u.Trust;
                    if (u.FaithId != 0) { socialFaithTally.TryGetValue(u.FaithId, out int believers); socialFaithTally[u.FaithId] = believers + 1; }
                    if (u.Age >= 16) { adults++; if (u.Mood < .35f && u.Trust < .4f) unhappy++; }
                }
                s.AverageMood = count == 0 ? .5f : mood / count; s.AverageTrust = count == 0 ? .5f : trust / count;
                s.Discontent = WorldGenerator.Clamp01((1 - s.AverageMood) * .65f + (1 - s.AverageTrust) * .35f);
                s.MajorityFaithId = 0; int largestFaith = 0;
                for (int f = 0; f < World.Religions.Count; f++) { int faithId = World.Religions[f].Id; if (socialFaithTally.TryGetValue(faithId, out int believers) && (believers > largestFaith || (believers == largestFaith && faithId < s.MajorityFaithId))) { largestFaith = believers; s.MajorityFaithId = faithId; } }
                bool crisis = adults >= 4 && unhappy > adults / 2;
                s.CrisisSeconds = crisis ? Math.Min(600, s.CrisisSeconds + dt) : Math.Max(0, s.CrisisSeconds - dt * 2);
                s.SocialState = s.CrisisSeconds >= 20 ? SocialState.Protest : s.Discontent > .5f ? SocialState.Discontent : SocialState.Stable;
                s.RecentWarLosses = Math.Max(0, s.RecentWarLosses - dt * .01f);
                if (crisis && s.CrisisSeconds >= RevoltCrisisSeconds && World.Tick >= s.RevoltCooldownUntilTick) ResolveRevolt(s, adults, unhappy);
                TryFoundReligion(s);
                TryStartSocialDispute(s);
            }
        }
        private void TryFoundReligion(SettlementData home)
        {
            if (home.Population < 4 || World.Tick - home.GovernmentSinceTick < 600 || home.MajorityFaithId != 0) return;
            UnitData founder = null;
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData u = World.Units[i]; if (!EligibleRuler(u, home.Id) || u.FaithId != 0) continue;
                if (founder == null || u.Education > founder.Education || (u.Education == founder.Education && u.Id < founder.Id)) founder = u;
            }
            if (founder == null) return;
            ReligionData faith = null; int start = World.Religions.Count == 0 ? 0 : (int)(StableRandom.Hash(World.Seed, home.Id, 0, 6400) % (uint)World.Religions.Count);
            for (int i = 0; i < World.Religions.Count; i++) { ReligionData candidate = World.Religions[(start + i) % World.Religions.Count]; if (candidate.FounderId == 0) { faith = candidate; break; } }
            if (faith == null && World.Religions.Count < 64) faith = CreateFaithDefinition();
            if (faith == null) return;
            faith.FounderId = founder.Id; faith.FoundedSettlementId = home.Id; faith.FoundedTick = World.Tick;
            founder.FaithId = faith.Id; founder.FaithCommitment = .9f; home.MajorityFaithId = faith.Id;
            AddSocialEvent(SocialEventKind.ReligionFounded, home, founder.Id, 0, founder.Name + " основывает учение «" + faith.Name + "», почитая " + faith.Deity + ".");
        }
        private void ShareFaith(UnitData first, UnitData second, float dt)
        {
            if (first == null || second == null || first.Age < 16 || second.Age < 16 || first.Health <= 0 || second.Health <= 0 || first.SettlementId != second.SettlementId || DistanceSquared(first.X, first.Y, second.X, second.Y) > 4) return;
            UnitData source = first.FaithCommitment >= second.FaithCommitment ? first : second, target = source == first ? second : first;
            if (source.FaithId == 0 || source.FaithCommitment < .55f) return;
            source.FaithCommitment = Math.Min(1, source.FaithCommitment + dt * .003f);
            if (source.FaithId == target.FaithId) return;
            if (target.PendingFaithId != source.FaithId) { target.PendingFaithId = source.FaithId; target.FaithExposure = 0; }
            target.FaithExposure = Math.Min(1, target.FaithExposure + dt * .12f * (.5f + source.FaithCommitment) * (1 - target.FaithCommitment));
            if (target.FaithExposure < 1) return;
            target.FaithId = source.FaithId; target.FaithCommitment = .6f; target.FaithExposure = 0; target.PendingFaithId = 0;
            ReligionData faith = World.GetReligion(target.FaithId);
            AddSocialEvent(SocialEventKind.Conversion, World.GetSettlement(target.SettlementId), target.Id, source.Id, target.Name + " после бесед принимает учение «" + faith.Name + "».");
        }
        private void ResolveRevolt(SettlementData home, int adults, int supporters)
        {
            UnitData successor = null; int oldRuler = home.RulerId;
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData u = World.Units[i]; if (!EligibleRuler(u, home.Id) || u.Id == oldRuler || u.Mood >= .35f || u.Trust >= .4f) continue;
                if (successor == null || u.Education + u.Prudence > successor.Education + successor.Prudence || (u.Education + u.Prudence == successor.Education + successor.Prudence && u.Id < successor.Id)) successor = u;
            }
            if (successor == null || supporters <= adults / 2) return;
            home.Government = home.Government == GovernmentType.Republic ? (home.MajorityFaithId != 0 && successor.FaithCommitment > .7f ? GovernmentType.Theocracy : GovernmentType.Feudal) : GovernmentType.Republic;
            home.GovernmentSinceTick = World.Tick; home.NextElectionTick = home.Government == GovernmentType.Republic ? NextElectionAfter(World.Tick) : 0;
            home.RulerId = successor.Id; home.RevoltCount++; home.RevoltSupporters = supporters; home.CrisisSeconds = 0; home.RevoltCooldownUntilTick = World.Tick + RevoltCooldownTicks; home.SocialState = SocialState.Discontent;
            for (int i = 0; i < World.Units.Count; i++) if (World.Units[i].SettlementId == home.Id && World.Units[i].Age >= 16) World.Units[i].Trust = Math.Min(1, World.Units[i].Trust + .2f);
            home.NextPolicyReviewTick = World.Tick;
            AddSocialEvent(SocialEventKind.Revolt, home, successor.Id, oldRuler, "После длительного кризиса " + supporters + " из " + adults + " взрослых поддержали смену строя. Новый правитель: " + successor.Name + ".");
        }
        private void RecordWarLoss(UnitData victim, UnitData attacker)
        {
            if (victim == null || attacker == null || victim.Health > 0 || !IsCivilized(victim.Species) || !AtWar(victim.SettlementId, attacker.SettlementId)) return;
            SettlementData home = World.GetSettlement(victim.SettlementId); if (home == null) return;
            home.WarDeaths++; home.RecentWarLosses++;
            for (int i = 0; i < World.Units.Count; i++) if (World.Units[i].SettlementId == home.Id) { World.Units[i].Trust = Math.Max(0, World.Units[i].Trust - .08f); World.Units[i].EventStress = Math.Min(1, World.Units[i].EventStress + .12f); }
            AddSocialEvent(SocialEventKind.WarLoss, home, victim.Id, attacker.Id, "Гибель " + victim.Name + " в войне подрывает доверие к правителю.");
        }
    }
}
