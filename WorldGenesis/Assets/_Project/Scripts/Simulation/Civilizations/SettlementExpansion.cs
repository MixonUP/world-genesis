using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private void MigrateExpansion()
        {
            if (World.ExpansionVersion == 1) return;
            World.SettlementExpeditions = new List<SettlementExpeditionData>();
            World.NextExpeditionId = 1;
            foreach (SettlementData town in World.Settlements)
            {
                InitializeExpansionSettlement(town);
                // Preserve existing claims and derive only the future construction radius; never issue supplies.
                int radius = 6;
                for (int i = 0; i < World.Cells.Length; i++) if (World.Cells[i].SettlementId == town.Id)
                    radius = Math.Max(radius, (int)Math.Ceiling(Math.Sqrt(DistanceSquared(i % World.Width, i / World.Width, town.X, town.Y))));
                town.TerritoryRadius = Math.Min(24, radius);
            }
            World.ExpansionVersion = 1;
        }
        private void InitializeExpansionSettlement(SettlementData town)
        {
            town.TerritoryRadius = 6;
            town.NextExpansionTick = World.Tick + 300;
            town.NextColonizationTick = World.Tick + 1200;
        }

        private bool ExpansionSiteAvailable(SettlementData home, int cell)
        {
            if (home == null || cell < 0 || cell >= World.Cells.Length || !DrySeat(cell)) return false;
            int x = cell % World.Width, y = cell / World.Width;
            if (World.Cells[cell].Height < .45f) return false;
            foreach (SettlementData town in World.Settlements)
                if (DistanceSquared(x, y, town.X, town.Y) < ExpansionCatalog.MinSettlementDistance * ExpansionCatalog.MinSettlementDistance) return false;
            foreach (SettlementExpeditionData expedition in World.SettlementExpeditions)
                if (ExpansionCatalog.Active(expedition) && expedition.SourceSettlementId != home.Id && DistanceSquared(x, y, expedition.TargetCell % World.Width, expedition.TargetCell / World.Width) < 16 * 16) return false;
            int dry = 0;
            for (int oy = -2; oy <= 2; oy++) for (int ox = -2; ox <= 2; ox++)
                if (PlacementRules.IsDryGround(World, x + ox, y + oy) && World.Cells[World.Index(x + ox, y + oy)].SettlementId == 0) dry++;
            return dry >= 18;
        }
        private float ExpeditionRouteSeconds(List<int> route, SpeciesType species)
        {
            if (route.Count > ExpansionCatalog.MaxRouteCells) return float.PositiveInfinity;
            float cost = 0;
            for (int i = 1; i < route.Count; i++) cost += World.GetStepCost(route[i - 1], route[i]);
            // Allow fatigue, six seconds of construction and a generous provisioning reserve.
            float speed = (species == SpeciesType.Dwarf ? 1.1f : species == SpeciesType.Elf ? 1.7f : 1.45f) * .8f;
            return (cost + 1) / speed;
        }
        public int FindSettlementExpansionSite(int settlementId)
        {
            SettlementData home = World.GetSettlement(settlementId); BuildingData hall = World.GetCampfire(settlementId);
            if (home == null || !SubsistenceRules.IsWorkingCampfire(World, hall)) return -1;
            int best = -1; float bestScore = float.NegativeInfinity;
            // Bounded deterministic candidates, rather than a full-map path search every second.
            for (int oy = -48; oy <= 48; oy += 4) for (int ox = -48; ox <= 48; ox += 4)
            {
                int x = home.X + ox, y = home.Y + oy;
                if (!World.InBounds(x, y) || ox * ox + oy * oy < 28 * 28 || ox * ox + oy * oy > 48 * 48) continue;
                int cell = World.Index(x, y);
                if (!ExpansionSiteAvailable(home, cell)) continue;
                float score = World.Cells[cell].Height * 40 + World.Cells[cell].Vegetation * 3 - (Math.Abs(ox) + Math.Abs(oy)) * .07f;
                if (score < bestScore) continue;
                if (!navigation.Find(World.Index(hall.X, hall.Y), cell, scratchPath) || ExpeditionRouteSeconds(scratchPath, home.Species) > 65) continue;
                if (score > bestScore || score == bestScore && (best < 0 || cell < best)) { best = cell; bestScore = score; }
            }
            return best;
        }
        private bool AvailableSettler(UnitData unit, SettlementData home)
        {
            if (unit == null || unit.SettlementId != home.Id || unit.Species != home.Species || unit.Health < unit.MaxHealth * .8f || unit.Age < 16 || unit.Age > 60 || unit.Id == home.RulerId || unit.ExpeditionId != 0 || unit.WarTargetSettlementId != 0 || unit.ShipId != 0 || unit.IsForeignMariner || unit.MaritimeOrderId != 0 || unit.CaravanId != 0 || unit.MarketOrderId != 0 || unit.TransportProjectId != 0 || unit.InsideBuildingId != 0 || unit.EnteringBuildingId != 0 || unit.MountId != 0 || unit.HasWagon || unit.Infected || unit.Hunger >= .5f || unit.Thirst >= .5f || unit.Fatigue >= .5f || CarriedLoad(unit) > .00001f) return false;
            return unit.Task == UnitTask.Idle || unit.Task == UnitTask.Wander || unit.Task == UnitTask.Socialize || unit.Task == UnitTask.Rest || unit.Task == UnitTask.Gather || unit.Task == UnitTask.Farm;
        }
        public bool TryStartSettlementExpedition(int settlementId, int targetCell)
        {
            SettlementData home = World.GetSettlement(settlementId); BuildingData hall = World.GetCampfire(settlementId);
            if (home == null || home.EnemyId != 0 || !SubsistenceRules.IsWorkingCampfire(World, hall) || !ExpansionSiteAvailable(home, targetCell) || home.Wood < ExpansionCatalog.PlanksRequired || home.Food < Math.Max(ExpansionCatalog.FoodRequired, home.Population * .5f) || hall.WaterStored < ExpansionCatalog.WaterRequired) return false;
            foreach (SettlementExpeditionData active in World.SettlementExpeditions)
                if (ExpansionCatalog.Active(active) && active.SourceSettlementId == settlementId) return false;
            int population = 0;
            var candidates = new List<UnitData>();
            foreach (UnitData unit in World.Units)
            {
                if (unit.SettlementId == settlementId && unit.Health > 0) population++;
                if (AvailableSettler(unit, home) && navigation.Find(World.Index((int)unit.X, (int)unit.Y), World.Index(hall.X, hall.Y), scratchPath) && scratchPath.Count <= 28) candidates.Add(unit);
            }
            if (population < ExpansionCatalog.Members + ExpansionCatalog.MinRemainingResidents || candidates.Count < ExpansionCatalog.Members || !navigation.Find(World.Index(hall.X, hall.Y), targetCell, scratchPath) || ExpeditionRouteSeconds(scratchPath, home.Species) > 65) return false;
            candidates.Sort((a, b) => a.Id.CompareTo(b.Id));
            // A lasting community needs both sexes; nobody is created to complete the party.
            bool female = false, male = false;
            for (int i = 0; i < ExpansionCatalog.Members; i++) { female |= candidates[i].Sex == Sex.Female; male |= candidates[i].Sex == Sex.Male; }
            if (!female || !male)
            {
                int alternate = candidates.FindIndex(u => u.Sex == (!female ? Sex.Female : Sex.Male));
                if (alternate < 0) return false;
                UnitData swap = candidates[ExpansionCatalog.Members - 1]; candidates[ExpansionCatalog.Members - 1] = candidates[alternate]; candidates[alternate] = swap;
            }
            PruneSettlementExpeditions();
            if (World.SettlementExpeditions.Count >= 256) return false;
            var expedition = new SettlementExpeditionData { Id = World.NextExpeditionId++, SourceSettlementId = home.Id, SourceBuildingId = hall.Id, TargetCell = targetCell, StartedTick = World.Tick, PhaseStartedTick = World.Tick, LastProgressTick = World.Tick, Phase = SettlementExpeditionPhase.Gathering, LastReason = "Шесть жителей собирают доски, еду и воду у костра." };
            for (int i = 0; i < ExpansionCatalog.Members; i++)
            {
                UnitData unit = candidates[i]; ChangeTask(unit, UnitTask.Settle); ClearRoute(unit);
                unit.ExpeditionId = expedition.Id; unit.ExpeditionLoaded = false; unit.ExpeditionResting = false;
                expedition.MemberIds.Add(unit.Id);
            }
            World.SettlementExpeditions.Add(expedition);
            home.NextColonizationTick = World.Tick + 3600;
            World.Revision++;
            return true;
        }
        public bool CancelSettlementExpedition(int expeditionId)
        {
            SettlementExpeditionData expedition = World.GetSettlementExpedition(expeditionId);
            if (!ExpansionCatalog.Active(expedition)) return false;
            ReturnSettlementExpedition(expedition, "Переселенцы возвращают оставшийся груз домой.");
            return true;
        }
        private void ReturnSettlementExpedition(SettlementExpeditionData expedition, string reason)
        {
            if (!ExpansionCatalog.Active(expedition)) return;
            if (expedition.Phase != SettlementExpeditionPhase.Returning)
            {
                expedition.Phase = SettlementExpeditionPhase.Returning; expedition.PhaseStartedTick = expedition.LastProgressTick = World.Tick;
                foreach (int id in expedition.MemberIds) { UnitData unit = World.GetUnit(id); if (unit != null) ClearRoute(unit); }
            }
            expedition.LastReason = reason;
        }
        private void DetachSettler(UnitData unit, SettlementExpeditionData expedition)
        {
            if (expedition != null) expedition.MemberIds.Remove(unit.Id);
            unit.ExpeditionId = 0; unit.ExpeditionLoaded = unit.ExpeditionResting = false;
            ClearRoute(unit); ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0;
            if (expedition != null && expedition.MemberIds.Count == 0 && ExpansionCatalog.Active(expedition))
            { expedition.Phase = SettlementExpeditionPhase.Cancelled; expedition.PhaseStartedTick = World.Tick; }
        }
        private void ReleaseExpansionOnDeath(UnitData unit)
        {
            if (unit.ExpeditionId == 0) return;
            SettlementExpeditionData expedition = World.GetSettlementExpedition(unit.ExpeditionId);
            if (expedition != null) ReturnSettlementExpedition(expedition, "Потерян участник: основание отменено, выжившие возвращаются.");
            DetachSettler(unit, expedition);
            // Existing death handling records carried planks and drops food. No second cargo ledger here.
        }
        private void PruneSettlementExpeditions()
        {
            for (int i = World.SettlementExpeditions.Count - 1; i >= 0 && World.SettlementExpeditions.Count >= 128; i--)
                if (!ExpansionCatalog.Active(World.SettlementExpeditions[i])) World.SettlementExpeditions.RemoveAt(i);
        }

        private void UpdateSettlementExpansion()
        {
            foreach (SettlementExpeditionData expedition in World.SettlementExpeditions)
            {
                if (!ExpansionCatalog.Active(expedition)) continue;
                if (expedition.MemberIds.Count < ExpansionCatalog.Members && expedition.Phase != SettlementExpeditionPhase.Returning)
                    ReturnSettlementExpedition(expedition, "Недостаточно участников для оплаченного основания.");
                if (World.Tick - expedition.LastProgressTick > 200 && expedition.Phase != SettlementExpeditionPhase.Returning)
                    ReturnSettlementExpedition(expedition, "Путь или сбор припасов слишком долго недоступен.");
                if (expedition.Phase == SettlementExpeditionPhase.Gathering)
                {
                    bool loaded = true;
                    foreach (int id in expedition.MemberIds) { UnitData unit = World.GetUnit(id); if (unit == null || !unit.ExpeditionLoaded || !AtExpeditionCell(unit, World.Index(World.GetSettlement(expedition.SourceSettlementId).X, World.GetSettlement(expedition.SourceSettlementId).Y))) loaded = false; }
                    if (loaded && expedition.MemberIds.Count == ExpansionCatalog.Members)
                    {
                        expedition.Phase = SettlementExpeditionPhase.Travelling; expedition.PhaseStartedTick = expedition.LastProgressTick = World.Tick; expedition.DepartedMembers = expedition.MemberIds.Count;
                        expedition.LastReason = "Доски и припасы загружены: отряд идёт пешком.";
                        foreach (int id in expedition.MemberIds) ClearRoute(World.GetUnit(id));
                    }
                }
                if (expedition.Phase == SettlementExpeditionPhase.Travelling)
                {
                    bool arrived = true;
                    foreach (int id in expedition.MemberIds) { UnitData unit = World.GetUnit(id); if (unit == null || !AtExpeditionCell(unit, expedition.TargetCell)) arrived = false; }
                    if (arrived && expedition.MemberIds.Count == ExpansionCatalog.Members)
                    { expedition.Phase = SettlementExpeditionPhase.Founding; expedition.PhaseStartedTick = expedition.LastProgressTick = World.Tick; expedition.LastReason = "Все переселенцы прибыли и устраивают костёр."; }
                }
                if (expedition.Phase == SettlementExpeditionPhase.Returning && World.Tick - expedition.LastProgressTick > 200)
                {
                    // An island cut-off leaves living people and their loads where they really are.
                    // Release to ordinary survival AI; never move them across the broken route.
                    int[] stranded = expedition.MemberIds.ToArray();
                    foreach (int id in stranded) { UnitData unit = World.GetUnit(id); if (unit != null) DetachSettler(unit, expedition); }
                    expedition.LastReason = "Обратный путь закрыт; жители ищут спасение с оставшимся грузом.";
                }
            }
            for (int i = 0; i < World.Settlements.Count; i++)
            {
                SettlementData town = World.Settlements[i];
                if (World.Tick >= town.NextExpansionTick)
                {
                    town.NextExpansionTick = World.Tick + 300;
                    int targetRadius = Math.Min(24, 6 + town.Buildings / 2 + town.Population / 8);
                    if (town.Population > 0 && town.TerritoryRadius < targetRadius)
                    { town.TerritoryRadius++; ClaimTerritory(town, town.TerritoryRadius); }
                }
                if (World.Tick < town.NextColonizationTick) continue;
                town.NextColonizationTick = World.Tick + 600;
                if (town.Population < 24 || town.EnemyId != 0 || town.Food < town.Population || town.Wood < ExpansionCatalog.PlanksRequired) continue;
                bool active = false; foreach (SettlementExpeditionData expedition in World.SettlementExpeditions) if (ExpansionCatalog.Active(expedition) && expedition.SourceSettlementId == town.Id) { active = true; break; }
                if (active) continue;
                int cell = FindSettlementExpansionSite(town.Id);
                if (cell >= 0) TryStartSettlementExpedition(town.Id, cell);
            }
        }
        private bool AtExpeditionCell(UnitData unit, int cell)
        { return DistanceSquared(unit.X, unit.Y, cell % World.Width + .5f, cell / World.Width + .5f) < .01f; }
        private bool MoveSettler(UnitData unit, SettlementExpeditionData expedition, int destination, float dt)
        {
            if (AtExpeditionCell(unit, destination)) return true;
            if (unit.PathRevision != World.NavigationRevision || unit.TargetCell != destination || unit.PathCursor >= unit.Path.Count)
            {
                ClearRoute(unit);
                if (!navigation.Find(World.Index((int)unit.X, (int)unit.Y), destination, unit.Path)) return false;
                // A* reports an empty path when already in the destination cell. Reach its centre
                // by ordinary movement; spawning/previous jobs may leave an off-centre position.
                if (unit.Path.Count == 0 && !AtExpeditionCell(unit, destination)) unit.Path.Add(destination);
                unit.PathRevision = World.NavigationRevision; unit.TargetCell = destination; unit.PathCursor = 0;
            }
            while (unit.PathCursor < unit.Path.Count && AtExpeditionCell(unit, unit.Path[unit.PathCursor])) unit.PathCursor++;
            if (unit.PathCursor >= unit.Path.Count) return AtExpeditionCell(unit, destination);
            int from = World.Index((int)unit.X, (int)unit.Y), next = unit.Path[unit.PathCursor];
            if (from != next && !World.CanStep(from, next) || World.Cells[next].Fire > .1f)
            { ClearRoute(unit); return false; }
            float speed = (unit.Species == SpeciesType.Dwarf ? 1.1f : unit.Species == SpeciesType.Elf ? 1.7f : 1.45f) * (1 - unit.Fatigue * .25f) / World.GetStepCost(from, next);
            if (MoveToward(unit, next % World.Width + .5f, next / World.Width + .5f, dt * speed)) unit.PathCursor++;
            expedition.LastProgressTick = World.Tick;
            int entered = World.Index((int)unit.X, (int)unit.Y); if (entered != from) RecordFootstep(unit, entered);
            return AtExpeditionCell(unit, destination);
        }
        private void ConsumeExpeditionProvisions(UnitData unit, SettlementExpeditionData expedition, float dt)
        {
            float foodNeeded = Math.Min(unit.Hunger / SubsistenceRules.NeedReliefPerPortion, dt / SubsistenceRules.DaySeconds);
            float waterNeeded = Math.Min(unit.Thirst / SubsistenceRules.NeedReliefPerPortion, dt / SubsistenceRules.DaySeconds);
            foreach (int id in expedition.MemberIds)
            {
                UnitData carrier = World.GetUnit(id);
                if (carrier == null || carrier.Health <= 0 || DistanceSquared(unit.X, unit.Y, carrier.X, carrier.Y) > 9) continue;
                float food = Math.Min(carrier.CarriedFood, foodNeeded), water = Math.Min(carrier.CarriedWater, waterNeeded);
                carrier.CarriedFood -= food; carrier.CarriedWater -= water; foodNeeded -= food; waterNeeded -= water;
                expedition.FoodConsumed += food; expedition.WaterConsumed += water;
                unit.Hunger = Math.Max(0, unit.Hunger - food * SubsistenceRules.NeedReliefPerPortion);
                unit.Thirst = Math.Max(0, unit.Thirst - water * SubsistenceRules.NeedReliefPerPortion);
            }
        }
        private bool UpdateExpansionUnit(UnitData unit, float dt)
        {
            if (unit.ExpeditionId == 0) return false;
            SettlementExpeditionData expedition = World.GetSettlementExpedition(unit.ExpeditionId);
            if (!ExpansionCatalog.Active(expedition)) { DetachSettler(unit, expedition); return false; }
            SettlementData home = World.GetSettlement(expedition.SourceSettlementId);
            BuildingData source = World.GetBuilding(expedition.SourceBuildingId);
            if (unit.SettlementId != expedition.SourceSettlementId || unit.WarTargetSettlementId != 0)
            { ReturnSettlementExpedition(expedition, "Принадлежность или приказ участника изменился."); DetachSettler(unit, expedition); return false; }
            int x = (int)unit.X, y = (int)unit.Y;
            if (World.Cells[World.Index(x, y)].Fire > .1f || NearbyFire(x, y) > .5f || FindEnemy(unit, 2) != null || FindDisputeEnemy(unit) != null)
            { ReturnSettlementExpedition(expedition, "Опасность прервала переселение."); DetachSettler(unit, expedition); return false; }
            if (!SubsistenceRules.IsWorkingCampfire(World, source) || source.SettlementId != expedition.SourceSettlementId)
                ReturnSettlementExpedition(expedition, "Исходный костёр недоступен или разрушен.");
            if (expedition.Phase != SettlementExpeditionPhase.Returning && expedition.Phase != SettlementExpeditionPhase.Gathering && !ExpansionSiteAvailable(home, expedition.TargetCell))
                ReturnSettlementExpedition(expedition, "Площадка нового поселения больше недоступна.");
            ChangeTask(unit, UnitTask.Settle);
            if (expedition.Phase != SettlementExpeditionPhase.Gathering) ConsumeExpeditionProvisions(unit, expedition, dt);
            if (unit.Hunger > .8f || unit.Thirst > .8f)
            { ReturnSettlementExpedition(expedition, "Припасы не покрывают потребности: возврат к источникам."); DetachSettler(unit, expedition); return false; }
            if (unit.Fatigue > .72f) unit.ExpeditionResting = true;
            if (unit.ExpeditionResting)
            {
                unit.Fatigue = Math.Max(0, unit.Fatigue - dt * .08f);
                if (unit.Fatigue <= .25f) unit.ExpeditionResting = false;
                expedition.LastProgressTick = World.Tick;
                return true;
            }
            if (expedition.Phase == SettlementExpeditionPhase.Gathering)
            {
                if (source == null || !MoveSettler(unit, expedition, World.Index(source.X, source.Y), dt)) return true;
                if (unit.ExpeditionLoaded) return true;
                int slot = expedition.MemberIds.IndexOf(unit.Id);
                if (CarriedLoad(unit) > .00001f || CarryCapacityFor(unit) < 1 || slot < 0)
                { ReturnSettlementExpedition(expedition, "Нельзя безопасно загрузить участника."); return true; }
                if (slot < ExpansionCatalog.PlanksRequired)
                {
                    if (home.Wood < 1) { ReturnSettlementExpedition(expedition, "Доски израсходованы до погрузки."); return true; }
                    home.Wood -= 1; unit.CarriedWood = 1;
                }
                else
                {
                    if (home.Food < .5f || source.WaterStored < .5f) { ReturnSettlementExpedition(expedition, "Припасы израсходованы до погрузки."); return true; }
                    home.Food -= .5f; source.WaterStored -= .5f; unit.CarriedFood = unit.CarriedWater = .5f;
                }
                unit.ExpeditionLoaded = true; expedition.LastProgressTick = World.Tick; World.ResourceRevision++;
                return true;
            }
            if (expedition.Phase == SettlementExpeditionPhase.Returning)
            {
                if (!SubsistenceRules.IsWorkingCampfire(World, source)) { DetachSettler(unit, expedition); return false; }
                if (!MoveSettler(unit, expedition, World.Index(source.X, source.Y), dt)) return true;
                home.Wood += unit.CarriedWood; home.Food += unit.CarriedFood;
                unit.CarriedWood = unit.CarriedFood = 0;
                float water = Math.Min(unit.CarriedWater, Math.Max(0, SubsistenceRules.WaterCapacity(source) - source.WaterStored));
                source.WaterStored += water; unit.CarriedWater -= water;
                DetachSettler(unit, expedition); World.ResourceRevision++;
                return true;
            }
            if (expedition.Phase == SettlementExpeditionPhase.Travelling)
            {
                // Leave the shared gathering point in sequence so six actual walkers remain readable.
                if (World.Tick < expedition.PhaseStartedTick + expedition.MemberIds.IndexOf(unit.Id) * 8) return true;
                MoveSettler(unit, expedition, expedition.TargetCell, dt); return true;
            }
            if (expedition.Phase == SettlementExpeditionPhase.Founding)
            {
                if (!AtExpeditionCell(unit, expedition.TargetCell)) { ReturnSettlementExpedition(expedition, "Участник покинул место основания."); return true; }
                // Exactly one coordinator advances the shared work; six people do not multiply elapsed time.
                if (expedition.MemberIds.Count > 0 && unit.Id == expedition.MemberIds[0])
                {
                    expedition.FoundingProgress = Math.Min(ExpansionCatalog.FoundingSeconds, expedition.FoundingProgress + dt);
                    expedition.LastProgressTick = World.Tick;
                    if (expedition.FoundingProgress >= ExpansionCatalog.FoundingSeconds) CompleteSettlementExpedition(expedition);
                }
                return true;
            }
            return true;
        }
        private void CompleteSettlementExpedition(SettlementExpeditionData expedition)
        {
            SettlementData origin = World.GetSettlement(expedition.SourceSettlementId);
            if (expedition.MemberIds.Count != ExpansionCatalog.Members || !ExpansionSiteAvailable(origin, expedition.TargetCell))
            { ReturnSettlementExpedition(expedition, "Основание отменено: люди или площадка недоступны."); return; }
            float planks = 0;
            foreach (int id in expedition.MemberIds)
            {
                UnitData unit = World.GetUnit(id);
                if (unit == null || unit.Health <= 0 || !AtExpeditionCell(unit, expedition.TargetCell)) { ReturnSettlementExpedition(expedition, "Не все участники на площадке."); return; }
                planks += unit.CarriedWood;
            }
            if (planks + .00001f < ExpansionCatalog.PlanksRequired) { ReturnSettlementExpedition(expedition, "Не хватает привезённых досок."); return; }
            int x = expedition.TargetCell % World.Width, y = expedition.TargetCell / World.Width;
            var town = new SettlementData { Id = World.NextId++, Species = origin.Species, X = x, Y = y, ParentSettlementId = origin.Id, ColorIndex = World.Settlements.Count % 8, Name = TownRoots[(int)(StableRandom.Hash(World.Seed, expedition.Id, x, (uint)y) % (uint)TownRoots.Length)] + " " + (World.Settlements.Count + 1) };
            InitializeGovernment(town); World.Settlements.Add(town);
            InitializeDevelopmentSettlement(town); InitializeSocialSettlement(town); InitializeExpansionSettlement(town);
            BuildingData hall = AddBuilding(town, x, y, BuildingType.Hall);
            // Preflight above and synchronous placement guarantee this call; retain a defensive rollback.
            if (hall == null) { World.Settlements.Remove(town); World.NextId--; ReturnSettlementExpedition(expedition, "Площадка занята до установки костра."); return; }
            float remaining = ExpansionCatalog.PlanksRequired;
            int[] ids = expedition.MemberIds.ToArray();
            foreach (int id in ids)
            {
                UnitData unit = World.GetUnit(id);
                float paid = Math.Min(remaining, unit.CarriedWood); unit.CarriedWood -= paid; remaining -= paid;
                // Existing warehouse ownership is local to a town. Left-behind lots stay physically there,
                // becoming communal; equipped tools/clothing and carried goods travel with their owner.
                ReleasePrivateGoods(unit);
                unit.SettlementId = town.Id; unit.HomeBuildingId = 0;
                town.Food += unit.CarriedFood; unit.CarriedFood = 0;
                hall.WaterStored += unit.CarriedWater; unit.CarriedWater = 0;
                unit.ExpeditionId = 0; unit.ExpeditionLoaded = unit.ExpeditionResting = false;
                ClearRoute(unit); ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0;
            }
            RecordConstructionCost(hall, ItemType.Plank, ExpansionCatalog.PlanksRequired);
            expedition.PlanksSpent = ExpansionCatalog.PlanksRequired; expedition.FoundedSettlementId = town.Id;
            expedition.MemberIds.Clear(); expedition.Phase = SettlementExpeditionPhase.Completed; expedition.PhaseStartedTick = World.Tick;
            expedition.LastReason = "Шесть существующих жителей основали самостоятельное мирное поселение за четыре привезённые доски.";
            World.SettlementsFoundedByExpeditions++;
            ClaimTerritory(town, 6); AssignHomesAndOwners(); RecountSettlements(); UpdatePolitics(); UpdateEconomicPolicies();
            AddEvent(EventKind.Founded, x + .5f, y + .5f, "Переселенцы из «" + origin.Name + "» основали «" + town.Name + "»");
            World.ResourceRevision++; World.Revision++;
        }
    }
}
