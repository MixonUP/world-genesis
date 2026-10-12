using System;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        public float GetLearningMultiplier(UnitData unit)
        {
            if (unit == null) return 1;
            float support = World.GetLearningSupport(unit.SettlementId);
            return (.6f + unit.Education * .8f) * (.7f + unit.LearningTalent * .6f) * (1 + support * .5f);
        }
        public float GetWorkMultiplier(UnitData unit)
        {
            return (unit.Age >= 16 ? 1 + unit.Education * .1f + unit.CraftSkill * .1f + (unit.IsMaster ? .1f : 0) : .65f) * (1 - .45f * unit.Intoxication);
        }
        private void GainCraftExperience(UnitData unit, float workingSeconds)
        {
            if (unit.Age < 16 || workingSeconds <= 0 || (unit.Task != UnitTask.Process && unit.Task != UnitTask.Gather && unit.Task != UnitTask.Farm && unit.Task != UnitTask.Fish && unit.Task != UnitTask.CraftEquipment && unit.Task != UnitTask.Brew && unit.Task != UnitTask.Tend && unit.Task != UnitTask.Hunt)) return;
            unit.CraftSkill = Math.Min(1, unit.CraftSkill + workingSeconds * .0008f * (.5f + unit.LearningTalent) * (.5f + unit.Education));
            if (unit.IsMaster && !unit.MasteryAnnounced)
            { unit.MasteryAnnounced = true; AddSocialEvent(SocialEventKind.Mastery, World.GetSettlement(unit.SettlementId), unit.Id, 0, unit.Name + " после учёбы и практики становится мастером."); }
        }
        private bool ChooseAdultLearning(UnitData unit, SettlementData home)
        {
            if (unit.Age < 16 || unit.Education >= .95f || home.Food < home.Population * 4 || unit.Fatigue > .5f || unit.Hunger > .4f || CarriedLoad(unit) > 0 || StableRandom.Hash(World.Seed, unit.Id, (int)(World.Tick / 100), 6600) % 5 != 0) return false;
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData school = World.Buildings[i];
                if (school.SettlementId != home.Id || school.Type != BuildingType.School || school.Health <= 0 || World.Cells[World.Index(school.X, school.Y)].Fire > .1f || !SetTarget(unit, school.X, school.Y)) continue;
                ChangeTask(unit, UnitTask.Learn); unit.WorkTimer = 5; return true;
            }
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData mentor = World.Units[i];
                if (mentor.Id == unit.Id || !EligibleRuler(mentor, home.Id) || mentor.Education < unit.Education + .15f || DistanceSquared(unit.X, unit.Y, mentor.X, mentor.Y) > 36 || !SetTarget(unit, (int)mentor.X, (int)mentor.Y)) continue;
                ChangeTask(unit, UnitTask.Learn); unit.WorkTimer = 4; return true;
            }
            return false;
        }
        private bool HasLearningSource(UnitData unit, SettlementData home)
        {
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData school = World.Buildings[i];
                if (school.Type == BuildingType.School && school.SettlementId == home.Id && school.Health > 0 && World.Cells[World.Index(school.X, school.Y)].Fire <= .1f && DistanceSquared(unit.X, unit.Y, school.X + .5f, school.Y + .5f) < 2) return true;
            }
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData mentor = World.Units[i];
                if (mentor.Id != unit.Id && EligibleRuler(mentor, home.Id) && (unit.Age < 16 || mentor.Education >= unit.Education + .15f) && mentor.Task != UnitTask.Fight && mentor.Task != UnitTask.Flee && DistanceSquared(unit.X, unit.Y, mentor.X, mentor.Y) < 9) return true;
            }
            return false;
        }
    }
}
