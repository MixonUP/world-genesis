using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    /// <summary>All gameplay mutations live here. Fixed 100 ms ticks; render and audio do not consume this RNG.</summary>
    public sealed partial class SimulationEngine
    {
        public const float TickSeconds = .1f;
        public WorldState World { get; private set; }
        private readonly GridNavigation navigation;
        private readonly float[] nextFire;
        private readonly int[] spatialHeads;
        private int[] spatialNext = new int[256];
        private readonly List<int> scratchPath = new List<int>();
        private readonly Dictionary<int, int> reservations = new Dictionary<int, int>();
        private static readonly int[] Dx = { -1, 0, 1, 0 };
        private static readonly int[] Dy = { 0, -1, 0, 1 };
        private static readonly string[] TownRoots = { "Зеленолесье", "Речной край", "Солнечный дол", "Каменный бор", "Лунная гавань", "Ветроград", "Ясный холм", "Листопад", "Медная долина", "Алый утёс" };
        private static readonly string[] GivenNames = { "Эрин", "Торин", "Лиора", "Арен", "Мира", "Горон", "Нора", "Ильмар", "Тара", "Орин", "Селия", "Борн" };

        public SimulationEngine(WorldState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            string error;
            if (!state.Validate(out error)) throw new ArgumentException(error, nameof(state));
            bool legacyCarrying = state.ResourceVersion < 2;
            World = state; navigation = new GridNavigation(state);
            nextFire = new float[state.Cells.Length]; spatialHeads = new int[state.Cells.Length];
            MigrateBehavior();
            MigratePolitics();
            MigrateResources();
            MigrateDemography(); MigrateCivilEconomy(); MigrateCarrying(legacyCarrying);
            MigrateDevelopment(); Hydrology.Initialize(World); BiomeCatalog.Generate(World); MigrateTransport(); MigrateSocial(); MigrateAgriculture(); MigrateFishing(); MigrateIndustry(); MigrateWildlife(); MigrateCaravans(); MigrateWeather(); World.SubsistenceVersion = 1; MigrateBuildingAccess(); MigrateMaritime(); MigrateMarket();
            MigrateExpansion(); MigrateConquest(); MigrateRemains();
        }

        public void Step(float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0 || dt > 10) throw new ArgumentOutOfRangeException(nameof(dt));
            World.StepRemainder += dt;
            while (World.StepRemainder + .000001f >= TickSeconds)
            {
                World.StepRemainder = Math.Max(0, World.StepRemainder - TickSeconds);
                Tick();
            }
        }

        public void SetLaw(WorldLaw law, bool enabled)
        {
            switch (law)
            {
                case WorldLaw.Births: World.Laws.Births = enabled; break;
                case WorldLaw.Warfare: World.Laws.Warfare = enabled; break;
                case WorldLaw.Aging: World.Laws.Aging = enabled; break;
                case WorldLaw.VegetationGrowth: World.Laws.VegetationGrowth = enabled; break;
                case WorldLaw.FireSpread: World.Laws.FireSpread = enabled; break;
                default: throw new ArgumentOutOfRangeException(nameof(law));
            }
            if (law == WorldLaw.Warfare && !enabled)
            {
                for (int i = 0; i < World.Settlements.Count; i++) World.Settlements[i].EnemyId = 0;
                for (int i = 0; i < World.Units.Count; i++)
                {
                    UnitData unit = World.Units[i];
                    if (unit.Task != UnitTask.Fight || !IsCivilized(unit.Species)) continue;
                    ChangeTask(unit, UnitTask.Idle); unit.TargetId = 0; unit.Path.Clear(); unit.PathCursor = 0; unit.DecisionTimer = 0;
                }
            }
            World.Revision++;
        }

        public void SetDiplomacy(int firstId, int secondId, bool war)
        {
            firstId = World.SovereignOf(firstId); secondId = World.SovereignOf(secondId);
            SettlementData first = World.GetSettlement(firstId), second = World.GetSettlement(secondId);
            if (first == null || second == null || firstId == secondId) throw new ArgumentException("Diplomacy requires two existing settlements.");
            int low = Math.Min(firstId, secondId), high = Math.Max(firstId, secondId);
            SettlementRelation relation = null;
            for (int i = 0; i < World.Relations.Count; i++)
                if (World.Relations[i].FirstId == low && World.Relations[i].SecondId == high) { relation = World.Relations[i]; break; }
            if (relation == null) { relation = new SettlementRelation { FirstId = low, SecondId = high }; World.Relations.Add(relation); }
            relation.AtWar = war;
            if (war) { first.EnemyId = second.Id; second.EnemyId = first.Id; }
            else
            {
                if (first.EnemyId == secondId) first.EnemyId = 0;
                if (second.EnemyId == firstId) second.EnemyId = 0;
                for (int i = 0; i < World.Units.Count; i++)
                {
                    UnitData unit = World.Units[i];
                    if ((unit.SettlementId != firstId && unit.SettlementId != secondId) || unit.Task != UnitTask.Fight) continue;
                    unit.TargetId = 0; unit.Path.Clear(); unit.PathCursor = 0; ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0;
                }
            }
            AddEvent(EventKind.War, first.X + .5f, first.Y + .5f, first.Name + " и " + second.Name + (war ? ": объявлена война" : ": заключён мир"));
            World.Revision++;
        }

        private void Tick()
        {
            World.Tick++; World.SimulatedSeconds += TickSeconds;
            UpdateRemains(TickSeconds);
            CleanupSocialDisputes();
            BuildSpatialIndex();
            reservations.Clear();
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData unit = World.Units[i];
                if (unit.Task == UnitTask.Gather && unit.TargetCell >= 0 && !reservations.ContainsKey(unit.TargetCell)) reservations.Add(unit.TargetCell, unit.Id);
            }
            UpdateWeather(TickSeconds);
            if (World.Tick % 5 == 0) UpdateEnvironment(.5f);
            UpdateResources();
            UpdateCampfireSupply(TickSeconds);
            if (World.Tick % 10 == 0) UpdateMarkets();
            UpdateMaritime(TickSeconds);
            navigation.BeginUnitBatch();
            try { for (int i = 0; i < World.Units.Count; i++) UpdateUnit(World.Units[i], TickSeconds); }
            finally { navigation.EndUnitBatch(); }
            RemoveDead(); UpdateTransport();
            if (World.Tick % 10 == 0) { UpdateSettlements(1f); UpdateDevelopment(); UpdateAgriculture(); UpdateWildlife(); UpdateCaravans(); UpdateSettlementExpansion(); UpdateConquest(1f); }
            UpdatePolitics();
            if (World.Tick % 300 == 0) UpdateRulerPlanning();
            UpdateEconomicPolicies();
            if (World.Tick % 10 == 0) UpdateSocial(1f);
            World.Revision++;
        }

        public void Spawn(SpeciesType species, int x, int y, int count = 1)
        {
            if ((int)species < 0 || (int)species > 16 || count < 0 || count > 1000) throw new ArgumentOutOfRangeException(nameof(species));
            if (!World.IsWalkable(x, y) || count == 0) return;
            SettlementData settlement = null;
            if (IsCivilized(species))
            {
                for (int i = 0; i < World.Settlements.Count; i++)
                {
                    SettlementData candidate = World.Settlements[i];
                    if (candidate.Species != species || DistanceSquared(x, y, candidate.X, candidate.Y) > 24 * 24) continue;
                    if (navigation.Find(World.Index(x, y), World.Index(candidate.X, candidate.Y), scratchPath)) { settlement = candidate; break; }
                }
                if (settlement == null) settlement = FoundSettlement(species, x, y);
                if (settlement == null) return;
            }
            int pack = 0;
            for (int n = 0; n < count && World.Units.Count < 10000; n++)
            {
                int ux = x, uy = y;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    int nx = x + RandomRange(-3, 4), ny = y + RandomRange(-3, 4);
                    if (World.IsWalkable(nx, ny) && navigation.Find(World.Index(x, y), World.Index(nx, ny), scratchPath, 64)) { ux = nx; uy = ny; break; }
                }
                AddUnit(species, ux, uy, settlement == null ? 0 : settlement.Id, false);
                if (WildlifeCatalog.IsAnimal(species)) { UnitData animal = World.Units[World.Units.Count - 1]; if (pack == 0) pack = animal.Id; animal.PackId = animal.PackLeaderId = pack; }
            }
            if (settlement != null) { RecountSettlements(); UpdatePolitics(); UpdateEconomicPolicies(); }
            World.Revision++;
        }

        public void Paint(TerrainType terrain, int x, int y, int radius)
        {
            if ((int)terrain < 0 || (int)terrain > 11) throw new ArgumentOutOfRangeException(nameof(terrain));
            radius = Math.Max(0, Math.Min(32, radius));
            if (x < -radius || y < -radius || x >= World.Width + radius || y >= World.Height + radius) return;
            bool changed = false;
            for (int cy = Math.Max(0, y - radius); cy <= Math.Min(World.Height - 1, y + radius); cy++)
            for (int cx = Math.Max(0, x - radius); cx <= Math.Min(World.Width - 1, x + radius); cx++)
            {
                if (DistanceSquared(cx, cy, x, y) > radius * radius) continue;
                int i = World.Index(cx, cy);
                ClearFloodAt(i);
                ref TileData tile = ref World.Cells[i];
                tile.Terrain = terrain; tile.Fire = 0; tile.River = false; tile.SnowCover = 0;
                tile.Height = terrain == TerrainType.DeepWater ? .2f : terrain == TerrainType.ShallowWater ? .4f : terrain == TerrainType.Mountain ? .85f : .58f;
                tile.Vegetation = terrain == TerrainType.Forest ? 1 : terrain == TerrainType.Grass ? .5f : terrain == TerrainType.Swamp ? .65f : terrain == TerrainType.Hills ? .25f : .1f;
                Hydrology.RefreshCell(World, i); BiomeCatalog.RefreshCell(World, i); ClearTransportCell(i);
                if (!World.IsTerrainWalkable(cx, cy)) tile.SettlementId = 0;
                RefreshPaintedResource(i);
                changed = true;
            }
            if (!changed) return;
            DestroyInvalidBuildings();
            World.TerrainRevision++; World.NavigationRevision++; World.Revision++;
        }

        public void ApplyPower(PowerType power, int x, int y, int radius)
        {
            if ((int)power < 0 || (int)power > 6) throw new ArgumentOutOfRangeException(nameof(power));
            radius = Math.Max(1, Math.Min(32, radius));
            if (!World.InBounds(x, y)) return;
            int effectiveRadius = power == PowerType.Meteor ? Math.Max(3, radius) : radius;
            float damage = power == PowerType.Meteor ? 150 : power == PowerType.Lightning ? 85 : 0;
            for (int cy = Math.Max(0, y - effectiveRadius); cy <= Math.Min(World.Height - 1, y + effectiveRadius); cy++)
            for (int cx = Math.Max(0, x - effectiveRadius); cx <= Math.Min(World.Width - 1, x + effectiveRadius); cx++)
            {
                float distance = DistanceSquared(cx, cy, x, y);
                if (distance > effectiveRadius * effectiveRadius) continue;
                ref TileData tile = ref World.Cells[World.Index(cx, cy)];
                if (power == PowerType.Rain) { tile.Fire = 0; tile.Moisture = Math.Min(1, tile.Moisture + .25f); if (World.IsWalkable(cx, cy) && tile.Terrain != TerrainType.Desert) tile.Vegetation = Math.Min(1, tile.Vegetation + .12f); }
                else if (power == PowerType.Fire && World.IsTerrainWalkable(cx, cy)) tile.Fire = 1;
                else if (power == PowerType.Lightning && World.IsTerrainWalkable(cx, cy)) tile.Fire = .8f;
                else if (power == PowerType.Meteor)
                {
                    ClearFloodAt(World.Index(cx, cy));
                    tile.Vegetation = 0; tile.River = false; tile.SnowCover = 0;
                    if (distance <= effectiveRadius * effectiveRadius * .32f) { tile.Terrain = TerrainType.ShallowWater; tile.Height = .38f; tile.Fire = 0; tile.SettlementId = 0; }
                    else if (World.IsTerrainWalkable(cx, cy)) { tile.Terrain = TerrainType.Sand; tile.Fire = 1; }
                    Hydrology.RefreshCell(World, World.Index(cx, cy)); BiomeCatalog.RefreshCell(World, World.Index(cx, cy)); ClearTransportCell(World.Index(cx, cy));
                }
            }
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData unit = World.Units[i];
                if (DistanceSquared(unit.X, unit.Y, x + .5f, y + .5f) > (effectiveRadius + 1) * (effectiveRadius + 1)) continue;
                if (power == PowerType.Heal) { unit.Health = unit.MaxHealth; unit.Infected = false; unit.Hunger = Math.Max(0, unit.Hunger - .3f); }
                else if (power == PowerType.Bless) { if (!unit.Blessed) { unit.Blessed = true; unit.MaxHealth *= 1.5f; unit.Health = unit.MaxHealth; } }
                else if (power == PowerType.Plague) unit.Infected = true;
                else if (damage > 0) unit.Health = Math.Max(0, unit.Health - damage);
            }
            if (damage > 0)
            {
                for (int i = 0; i < World.Buildings.Count; i++)
                {
                    BuildingData b = World.Buildings[i];
                    if (DistanceSquared(b.X, b.Y, x, y) <= effectiveRadius * effectiveRadius) b.Health = Math.Max(0, b.Health - damage);
                }
            }
            ApplyMaritimePower(power, x, y, effectiveRadius);
            AddEvent((EventKind)(int)power, x + .5f, y + .5f, PowerName(power));
            DestroyInvalidBuildings(); RemoveDead(); UpdateTransport(); RecountSettlements();
            UpdatePolitics();
            World.TerrainRevision++; if (power == PowerType.Meteor) World.NavigationRevision++;
            World.Revision++;
        }

        private void BuildSpatialIndex()
        {
            for (int i = 0; i < spatialHeads.Length; i++) spatialHeads[i] = -1;
            if (spatialNext.Length < World.Units.Count) spatialNext = new int[Math.Max(spatialNext.Length * 2, World.Units.Count)];
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData u = World.Units[i]; if (u.InsideBuildingId != 0 || u.ShipId != 0) { spatialNext[i] = -1; continue; } int cell = World.Index((int)u.X, (int)u.Y);
                spatialNext[i] = spatialHeads[cell]; spatialHeads[cell] = i;
            }
        }

        private void UpdateEnvironment(float dt)
        {
            bool changed = false;
            for (int i = 0; i < World.Cells.Length; i++)
            {
                TileData tile = World.Cells[i];
                float fire = tile.Fire;
                if (fire > 0)
                {
                    World.Cells[i].Vegetation = Math.Max(0, tile.Vegetation - fire * .10f * dt);
                    fire = Math.Max(0, fire - (tile.Vegetation < .05f && tile.BuildingId == 0 ? .40f : .045f + tile.Moisture * .05f) * dt);
                    changed = true;
                }
                else if (World.Laws.FireSpread && (tile.Vegetation > .12f || tile.BuildingId != 0 || World.Infrastructure[i].BridgeLevel == 1))
                {
                    int x = i % World.Width, y = i / World.Width;
                    float neighbours = 0;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + Dx[d], ny = y + Dy[d];
                        if (World.InBounds(nx, ny)) neighbours += World.Cells[World.Index(nx, ny)].Fire;
                    }
                    if (neighbours > 0 && RandomValue() < 1 - Math.Exp(-neighbours * .3f * (1 - tile.Moisture * .7f) * dt)) { fire = 1; changed = true; }
                }
                nextFire[i] = fire;
                if (World.ResourceNodes[i].Type != ResourceType.Tree && World.Laws.VegetationGrowth && fire == 0 && tile.Vegetation > .001f && tile.Vegetation < .99f && (tile.Terrain == TerrainType.Grass || tile.Terrain == TerrainType.Forest))
                {
                    float capacity = tile.Terrain == TerrainType.Forest ? 1 : .65f;
                    float v = World.Cells[i].Vegetation;
                    World.Cells[i].Vegetation = capacity * v / (v + (capacity - v) * (float)Math.Exp(-.012f * World.GetWeatherGrowthMultiplier(i) * dt));
                    changed = true;
                }
            }
            for (int i = 0; i < World.Cells.Length; i++) World.Cells[i].Fire = nextFire[i];
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData b = World.Buildings[i]; b.Health = Math.Max(0, b.Health - World.Cells[World.Index(b.X, b.Y)].Fire * 10 * dt);
            }
            DestroyInvalidBuildings();
            if (changed) World.TerrainRevision++;
        }

        private void UpdateUnit(UnitData unit, float dt)
        {
            if (unit.Health <= 0) return;
            if (UpdateMaritimeUnit(unit, dt)) return;
            int x = (int)unit.X, y = (int)unit.Y;
            if (World.Laws.Aging) unit.Age += dt * AgingRules.RatePerSecond(unit.Species);
            if (IsCivilized(unit.Species) && unit.Age >= 16 && unit.Profession == UnitProfession.None) unit.Profession = ProfessionFor(unit.Id);
            if (IsCivilized(unit.Species) && unit.Age >= 16 && unit.FishingRodLevel == 0) unit.FishingRodLevel = 1;
            unit.Hunger = Math.Min(1, unit.Hunger + dt * (IsCivilized(unit.Species) ? SubsistenceRules.HungerPerSecond : .004f));
            if (IsCivilized(unit.Species)) unit.Thirst = Math.Min(1, unit.Thirst + dt * SubsistenceRules.ThirstPerSecond);
            if (unit.Task != UnitTask.Rest)
            {
                float exertion = unit.Task == UnitTask.Flee || unit.Task == UnitTask.Fight ? .012f : unit.Task == UnitTask.Gather || unit.Task == UnitTask.Farm || unit.Task == UnitTask.Process || unit.Task == UnitTask.Produce ? .006f : unit.PathCursor < unit.Path.Count ? .003f : .0015f;
                unit.Fatigue = Math.Min(1, unit.Fatigue + dt * exertion * IndustryCatalog.ClothingFatigueMultiplier(unit.Clothes));
            }
            if (IsCivilized(unit.Species)) unit.SocialNeed = Math.Min(1, unit.SocialNeed + dt * .0025f);
            unit.AttackTimer = Math.Max(0, unit.AttackTimer - dt);
            unit.DecisionTimer -= dt;
            TileData tile = World.Cells[World.Index(x, y)];
            unit.Health = Math.Max(0, unit.Health - tile.Fire * dt * 15);
            if (HandleIndoorSafety(unit, dt)) return;
            x = (int)unit.X; y = (int)unit.Y; tile = World.Cells[World.Index(x, y)];
            if (unit.InsideBuildingId == 0 && !World.IsWalkable(x, y))
            {
                // A changed coastline never teleports inhabitants. They seek an adjacent shore and take drowning damage.
                ChangeTask(unit, UnitTask.Flee); ClearRoute(unit);
                unit.Health = Math.Max(0, unit.Health - dt * 18);
                for (int d = 0; d < 4; d++) if (World.IsWalkable(x + Dx[d], y + Dy[d])) { MoveToward(unit, x + Dx[d] + .5f, y + Dy[d] + .5f, dt * .7f); break; }
                return;
            }
            if (unit.Infected) { unit.Health = Math.Max(0, unit.Health - dt * 1.1f); if (World.Tick % 30 == unit.Id % 30) SpreadDisease(unit); }
            if (unit.Hunger > .9f) unit.Health = Math.Max(0, unit.Health - dt * 5);
            if (World.Laws.Aging && unit.Age > (unit.Species == SpeciesType.Elf ? 180 : IsCivilized(unit.Species) ? 85 : 25)) unit.Health = Math.Max(0, unit.Health - dt * 1.5f);
            if (unit.Health <= 0) return;
            if (UpdateExpansionUnit(unit, dt)) return;
            SettlementData home = World.GetSettlement(unit.SettlementId);
            EnforceChildWorkLaw(unit, home);
            UnitData enemy = unit.InsideBuildingId != 0 ? null : FindEnemy(unit, WildlifeCatalog.IsPredator(unit.Species) ? 6 : 2) ?? FindDisputeEnemy(unit) ?? HuntTarget(unit);
            UnitData predator = WildlifeCatalog.IsAnimal(unit.Species) && !WildlifeCatalog.IsPredator(unit.Species) ? FindPredator(unit) : null;
            bool threatened = predator != null || (enemy != null && IsCivilized(unit.Species) && (unit.Age < 16 || unit.Health < unit.MaxHealth * .35f));
            bool hazard = tile.Fire > .1f || NearbyFire(x, y) > .5f;
            if (hazard || threatened)
            {
                UnitData threat = predator ?? enemy;
                EscapeDanger(unit, threat);
            }
            else if (unit.Task == UnitTask.Flee && unit.PathCursor < unit.Path.Count)
            {
                // Complete a committed escape route before choosing another job.
            }
            else if (enemy != null && (IsCivilized(unit.Species) || WildlifeCatalog.IsPredator(unit.Species)))
            {
                bool hunting = unit.Task == UnitTask.Hunt && HuntEligible(unit, enemy);
                bool enteringCombat = unit.Task != UnitTask.Fight && !hunting;
                ChangeTask(unit, hunting ? UnitTask.Hunt : UnitTask.Fight); unit.TargetId = enemy.Id;
                if (enteringCombat) { ClearRoute(unit); unit.DecisionTimer = 0; }
                // The crafted hunting spear can be thrown within three tiles; no firearm or armour is implied.
                if (DistanceSquared(unit.X, unit.Y, enemy.X, enemy.Y) <= (hunting ? 9f : 2.2f) && unit.AttackTimer <= 0)
                {
                    ClearRoute(unit);
                    float damage = WildlifeCatalog.IsAnimal(unit.Species) ? WildlifeCatalog.Damage(unit.Species) : unit.Species == SpeciesType.Orc ? 12 : 7;
                    if (hunting && unit.HuntingToolLevel > 0) damage += 5;
                    if (unit.WarWeapon != ItemType.None) damage += unit.WarWeapon == ItemType.Sword ? 10 : 7;
                    UnitData combatMount = World.GetUnit(unit.MountId);
                    if (unit.HuntingToolLevel > 0 && !unit.HasWagon && combatMount != null && combatMount.Health > 0 && combatMount.IsDomestic && combatMount.HandlerId == unit.Id && (combatMount.Species == SpeciesType.Horse || combatMount.Species == SpeciesType.SpiritStag) && DistanceSquared(unit.X, unit.Y, combatMount.X, combatMount.Y) <= 1.96f) damage *= 1.2f;
                    bool dispute = IsSocialDispute(unit.Id, enemy.Id);
                    damage *= 1 + Math.Min(.2f, Math.Max(0, World.Cells[World.Index((int)unit.X, (int)unit.Y)].Height - World.Cells[World.Index((int)enemy.X, (int)enemy.Y)].Height) * 2);
                    enemy.Health = Math.Max(dispute && !IsLethalSocialDispute(unit.Id, enemy.Id) ? enemy.MaxHealth * .35f : 0, enemy.Health - damage * (unit.Blessed ? 1.5f : 1)); unit.AttackTimer = .8f;
                    enemy.EventStress = Math.Min(1, enemy.EventStress + .08f);
                    if (hunting) GainCraftExperience(unit, .8f);
                    if (enemy.Health == 0) RecordWarLoss(enemy, unit);
                    if (WildlifeCatalog.IsPredator(unit.Species) && enemy.Health == 0) unit.Hunger = 0;
                }
                else if (unit.DecisionTimer <= 0) { SetTarget(unit, (int)enemy.X, (int)enemy.Y); unit.DecisionTimer = 1; }
            }
            else if (NeedsImmediateDecision(unit) || (!CommittedActivity(unit) && unit.DecisionTimer <= 0) || ((unit.Task == UnitTask.Fight || unit.Task == UnitTask.Hunt) && unit.TargetId != 0) || unit.Task == UnitTask.Flee)
            {
                unit.TargetId = 0; Decide(unit, home); unit.DecisionTimer = 2f + RandomValue() * 2f;
            }
            if (unit.PathCursor < unit.Path.Count)
            {
                if (unit.PathRevision != World.NavigationRevision) { ClearRoute(unit); ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return; }
                int target = unit.Path[unit.PathCursor];
                int tx = target % World.Width, ty = target / World.Width;
                if (!World.CanStep(World.Index((int)unit.X, (int)unit.Y), target) && World.Index((int)unit.X, (int)unit.Y) != target || (unit.Task != UnitTask.Flee && World.Cells[target].Fire > .15f)) { ClearRoute(unit); ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return; }
                float speed = WildlifeCatalog.IsAnimal(unit.Species) ? WildlifeCatalog.Speed(unit.Species) : unit.Species == SpeciesType.Elf ? 1.7f : unit.Species == SpeciesType.Dwarf ? 1.1f : 1.45f;
                if (unit.Task == UnitTask.Flee) speed *= 1.45f;
                else speed *= 1 - unit.Fatigue * .25f;
                int oldCell = World.Index((int)unit.X, (int)unit.Y);
                speed *= 1 / World.GetStepCost(oldCell, target);
                speed *= 1 - .25f * unit.Intoxication;
                UnitData mount = World.GetUnit(unit.MountId);
                if (mount != null && mount.Health > 0 && mount.HandlerId == unit.Id && mount.IsDomestic && DistanceSquared(unit.X, unit.Y, mount.X, mount.Y) <= 1.96f) speed *= unit.HasWagon ? 1.1f : 1.35f;
                if (MoveToward(unit, tx + .5f, ty + .5f, dt * speed)) unit.PathCursor++;
                int entered = World.Index((int)unit.X, (int)unit.Y);
                if (entered != oldCell) RecordFootstep(unit, entered);
            }
            else if (!PerformBuildingEntry(unit, dt) && !PerformBuildingVisit(unit, dt) && !PerformWarActivity(unit, home) && !PerformMaritimeActivity(unit, home, dt) && !PerformMarketActivity(unit, home, dt) && !PerformManufacturedUse(unit, home) && !PerformTransportActivity(unit, home, dt) && !PerformCaravanActivity(unit, home, dt) && !PerformAgriDelivery(unit, home, dt) && !PerformWaterActivity(unit, home, dt) && !PerformNeedsActivity(unit, home, dt) && !PerformChildActivity(unit, home, dt) && home != null && (unit.Age >= 16 || LightWorkAllowed(unit, home)))
            {
                float workDt = dt * GetWorkMultiplier(unit);
                if (!PerformProduction(unit, home, workDt) && !PerformFishing(unit, home, workDt)) Work(unit, home, workDt);
            }
        }

        private void Decide(UnitData unit, SettlementData home)
        {
            if (ChooseWildlifeActivity(unit)) return;
            if (unit.CarriedWater > 0 && unit.Task != UnitTask.Brew && ChooseWater(unit, home)) return;
            if (unit.Thirst > .55f && ChooseWater(unit, home)) return;
            if (unit.Hunger > .50f && ChooseFood(unit, home)) return;
            if (unit.Fatigue > .72f && ChooseRest(unit, home)) return;
            if (ChooseWarActivity(unit, home)) return;
            if (ChooseManufacturedGoods(unit, home)) return;
            if (ChooseTransportWork(unit, home)) return;
            if (home != null && unit.Age >= 16)
            {
                if (unit.CaravanId != 0 && TryChooseCaravan(unit, home)) return;
                if (ChooseHarvestDelivery(unit, home)) return;
                if (unit.CargoAmount > 0 && ChooseCargoDelivery(unit, home)) return;
                if (CarriedLoad(unit) >= CarryCapacityFor(unit) - .001f || ((unit.CarriedFood > 0 || unit.CarriedWood > 0) && unit.Task != UnitTask.Farm && unit.Task != UnitTask.Gather))
                {
                    if (SetTarget(unit, home.X, home.Y)) { ChangeTask(unit, UnitTask.Return); return; }
                }
                if (unit.SocialNeed > .70f && unit.Hunger < .5f && ChooseSocial(unit, home)) return;
                if (ChooseLoot(unit, home) || ChooseGroundStock(unit)) return;
                if (TryChooseProductionOrder(unit, home) || TryChooseMarket(unit, home) || ChooseMaritimeWork(unit, home)) return;
                if (TryChooseCaravan(unit, home) || ChooseAgriculturalWork(unit, home) || ChooseHunt(unit, home)) return;
                if (ChooseProduction(unit, home) || ChooseFurniture(unit, home)) return;
                if (ChooseAdultLearning(unit, home)) return;
                if (unit.Profession == UnitProfession.Forester && home.Food >= home.Population * 2 && ChooseIndustry(unit, home)) return;
                float foodNeed = WorldGenerator.Clamp01(1 - home.Food / (home.Population * 5f + 1));
                float woodNeed = WorldGenerator.Clamp01(1 - home.Wood / (home.Population * 3f + 20));
                float farmScore = .35f + foodNeed * .55f + (unit.Profession == UnitProfession.Farmer ? .3f : 0);
                float woodScore = .10f + woodNeed * .6f + (unit.Profession == UnitProfession.Forester ? .35f : 0);
                if (woodScore >= farmScore && ChooseIndustry(unit, home)) return;
                if (ChooseFishing(unit, home)) return;
                BuildingData farm = FindFarm(home.Id, unit.Id);
                if (farm != null && SetTarget(unit, farm.X, farm.Y)) { ChangeTask(unit, UnitTask.Farm); return; }
                if (ChooseIndustry(unit, home)) return;
                if (unit.CarriedFood > 0 || unit.CarriedWood > 0)
                {
                    if (SetTarget(unit, home.X, home.Y)) { ChangeTask(unit, UnitTask.Return); return; }
                }
            }
            else if (IsCivilized(unit.Species) && home != null)
            {
                if (LightWorkAllowed(unit, home))
                {
                    if (ChooseHarvestDelivery(unit, home)) return;
                    if (unit.CargoAmount > 0 && ChooseCargoDelivery(unit, home)) return;
                    if ((unit.CarriedFood > 0 || unit.CarriedWood > 0) && SetTarget(unit, home.X, home.Y)) { ChangeTask(unit, UnitTask.Return); return; }
                    BuildingData farm = FindFarm(home.Id, unit.Id);
                    if (home.Food < home.Population * 5 && farm != null && SetTarget(unit, farm.X, farm.Y)) { ChangeTask(unit, UnitTask.Farm); return; }
                }
                if (ChooseChildActivity(unit, home)) return;
            }
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int range = IsCivilized(unit.Species) && unit.Age < 16 ? 3 : 7;
                // Leisure stays near the settlement; repeated idle decisions must not become an unbounded random walk.
                int x = (home == null ? (int)unit.X : home.X) + RandomRange(-range, range + 1), y = (home == null ? (int)unit.Y : home.Y) + RandomRange(-range, range + 1);
                if (SetTarget(unit, x, y)) { ChangeTask(unit, UnitTask.Wander); return; }
            }
            ClearRoute(unit); ChangeTask(unit, UnitTask.Idle);
        }

        private void Work(UnitData unit, SettlementData home, float dt)
        {
            if (PerformAgriculturalWork(unit, home, dt)) return;
            if (PerformAgriculture(unit, home, dt)) return;
            if (WorkResources(unit, home, dt)) return;
            // Civilian Fight is a local emergency response. Siege jobs require future equipped military recruitment.
            if (unit.Task == UnitTask.Return && DistanceSquared(unit.X, unit.Y, home.X + .5f, home.Y + .5f) < 2)
            { home.Wood += unit.CarriedWood; home.Food += unit.CarriedFood; unit.CarriedFood = 0; unit.CarriedWood = 0; unit.DecisionTimer = 0; }
        }

        private void UpdateSettlements(float dt)
        {
            RecountSettlements();
            for (int i = 0; i < World.Settlements.Count; i++)
            {
                SettlementData s = World.Settlements[i];
                // EnemyId is only a cached choice; old implicit-war saves can contain it without a declared war.
                if (s.EnemyId != 0 && !AtWar(s.Id, s.EnemyId)) s.EnemyId = 0;
                if (s.Population == 0) continue;
                s.BuildTimer += dt; s.BirthTimer += dt;
                if (World.Laws.Warfare && s.Population >= 8)
                {
                    SettlementData enemy = World.GetSettlement(s.EnemyId);
                    // Empty towns use the same armed presence and occupation timer as inhabited towns.
                    if (enemy == null || enemy.Population == 0)
                    {
                        s.EnemyId = 0;
                        for (int n = 0; n < World.Settlements.Count; n++)
                        {
                            SettlementData candidate = World.Settlements[n];
                            if (candidate.Id != s.Id && AtWar(s.Id, candidate.Id) && candidate.Population > 0 && DistanceSquared(candidate.X, candidate.Y, s.X, s.Y) < 60 * 60)
                            { s.EnemyId = candidate.Id; AddEvent(EventKind.War, s.X + .5f, s.Y + .5f, s.Name + " объявляет войну «" + candidate.Name + "»"); break; }
                        }
                    }
                }
                else if (!World.Laws.Warfare) s.EnemyId = 0;
                bool needsField = CountBuildings(s.Id, BuildingType.Farm) * 5 < s.Population
                    && CountBuildings(s.Id, BuildingType.Sawmill) > 0 && CountBuildings(s.Id, BuildingType.House) > 0;
                if (!HasTransportProject(s.Id) && s.BuildTimer >= 8 && (needsField || !TryDevelopSettlement(s)) && s.Wood >= 10)
                {
                    BuildingType type = needsField ? BuildingType.Farm
                        : s.Population >= 3 && CountBuildings(s.Id, BuildingType.Sawmill) == 0 ? BuildingType.Sawmill
                        : s.Population >= 3 && CountBuildings(s.Id, BuildingType.House) == 0 ? BuildingType.House
                        : s.Population >= 4 && CountBuildings(s.Id, BuildingType.Stoneworks) == 0 && World.GetSettlementStock(s.Id, ItemType.RawStone) >= 2 ? BuildingType.Stoneworks
                        : s.Population >= 4 && CountBuildings(s.Id, BuildingType.Kiln) == 0 && World.GetSettlementStock(s.Id, ItemType.Clay) >= 2 ? BuildingType.Kiln
                        : s.Housing < s.Population + 4 ? BuildingType.House : CountBuildings(s.Id, BuildingType.Farm) * 5 < s.Population ? BuildingType.Farm : s.Buildings < 18 ? BuildingType.House : BuildingType.Tower;
                    int site = FindBuildingSite(s);
                    if (site >= 0 && CountCivilBuildings(s.Id) < ExpansionCatalog.BuildingLimit(s)) { s.Wood -= 10; s.BuildTimer = 0; RecordConstructionCost(AddBuilding(s, site % World.Width, site / World.Width, type), ItemType.Plank, 10); ClaimTerritory(s, Math.Min(ExpansionCatalog.BuildingRadius(s), 5 + s.Buildings)); }
                }
                if (World.Laws.Births && s.BirthTimer >= 10 && s.Population < s.Housing && s.Food >= 8 && World.Units.Count < 10000)
                {
                    bool female = false, male = false;
                    for (int u = 0; u < World.Units.Count; u++)
                    {
                        UnitData parent = World.Units[u];
                        if (parent.SettlementId == s.Id && parent.Age >= 16 && parent.Health > 0 && DistanceSquared(parent.X, parent.Y, s.X + .5f, s.Y + .5f) < 9) { if (parent.Sex == Sex.Female) female = true; else male = true; }
                    }
                    if (female && male && World.IsWalkable(s.X, s.Y))
                    { s.Food -= 8; s.BirthTimer = 0; AddUnit(s.Species, s.X, s.Y, s.Id, true); s.Population++; AddEvent(EventKind.Birth, s.X + .5f, s.Y + .5f, "Рождение в «" + s.Name + "»"); }
                }
            }
        }

        private SettlementData FoundSettlement(SpeciesType species, int x, int y)
        {
            int seat = ChooseSettlementSeat(x, y);
            if (seat < 0 || !PlacementRules.CanPlaceBuilding(World, BuildingType.Hall, seat % World.Width, seat / World.Width)) return null;
            x = seat % World.Width; y = seat / World.Width;
            SettlementData s = new SettlementData { Id = World.NextId++, Species = species, X = x, Y = y, Food = 28, Wood = 0, Stone = 0, ColorIndex = World.Settlements.Count % 8, Name = TownRoots[RandomRange(0, TownRoots.Length)] + " " + (World.Settlements.Count + 1) };
            InitializeGovernment(s);
            World.Settlements.Add(s);
            InitializeDevelopmentSettlement(s); InitializeSocialSettlement(s); InitializeExpansionSettlement(s);
            // The player's creation power supplies the founding hall and one farm explicitly.
            if (World.Cells[World.Index(x, y)].BuildingId == 0) AddBuilding(s, x, y, BuildingType.Hall);
            int site = FindBuildingSite(s);
            if (site >= 0) AddBuilding(s, site % World.Width, site / World.Width, BuildingType.Farm);
            ClaimTerritory(s, 6);
            AddEvent(EventKind.Founded, x + .5f, y + .5f, "Основано поселение «" + s.Name + "»");
            return s;
        }

        private void AddUnit(SpeciesType species, int x, int y, int settlementId, bool child)
        {
            float health = WildlifeCatalog.IsAnimal(species) ? WildlifeCatalog.MaxHealth(species) : species == SpeciesType.Dwarf ? 130 : species == SpeciesType.Orc ? 120 : 100;
            UnitData unit = new UnitData { Id = World.NextId++, Species = species, X = x + .5f, Y = y + .5f, SettlementId = settlementId, Health = health, MaxHealth = health, Age = child ? 0 : IsCivilized(species) ? 18 + RandomValue() * 20 : 2 + RandomValue() * 5, Hunger = .05f, Name = GivenNames[RandomRange(0, GivenNames.Length)], DecisionTimer = RandomValue() * 2f, TaskStartedTick = World.Tick };
            unit.Profession = !child && IsCivilized(species) ? ProfessionFor(unit.Id) : UnitProfession.None;
            InitializeDemography(unit);
            InitializeSocialUnit(unit);
            unit.FishingRodLevel = !child && IsCivilized(species) ? 1 : 0;
            if (WildlifeCatalog.IsAnimal(species)) { unit.PackId = unit.PackLeaderId = unit.Id; unit.Name = WildlifeCatalog.Name(species); }
            unit.Fatigue = StableRandom.Coordinate(World.Seed, unit.Id, 0, 3001) * .4f;
            unit.SocialNeed = IsCivilized(species) ? StableRandom.Coordinate(World.Seed, unit.Id, 0, 3002) * .4f : 0;
            World.Units.Add(unit);
        }

        private BuildingData AddBuilding(SettlementData s, int x, int y, BuildingType type)
        {
            if (!PlacementRules.CanPlaceBuilding(World, type, x, y)) return null;
            BuildingData b = new BuildingData { Id = World.NextId++, SettlementId = s.Id, X = x, Y = y, Type = type, ConstructionLedgerVersion = 1 };
            InitializeDevelopmentBuilding(b);
            InitializeAgricultureBuilding(b);
            World.Buildings.Add(b);
            World.Cells[World.Index(x, y)].BuildingId = b.Id;
            World.Cells[World.Index(x, y)].SettlementId = s.Id;
            World.Cells[World.Index(x, y)].Vegetation = 0;
            ClearNodeForBuilding(World.Index(x, y));
            InitializeBuildingAccess(b); InitializeMaritimeBuilding(b);
            if (BuildingAccess.IsEnclosed(type)) World.NavigationRevision++;
            s.Buildings++; s.Housing += HousingCapacity(b);
            AddEvent(EventKind.Built, x + .5f, y + .5f, s.Name + ": " + (type == BuildingType.House ? "построен дом" : type == BuildingType.Farm ? "разбито поле" : type == BuildingType.Tower ? "построена башня" : type == BuildingType.Sawmill ? "построена лесопилка" : type == BuildingType.Stoneworks ? "построена каменоломня" : type == BuildingType.Kiln ? "построена кирпичная печь" : type == BuildingType.Well ? "вырыт колодец" : type == BuildingType.Warehouse ? "построен склад" : type == BuildingType.Granary ? "построен амбар" : type == BuildingType.Shop ? "открыта лавка" : type == BuildingType.WoodWall ? "поставлена деревянная стена" : type == BuildingType.Gate ? "поставлены ворота" : type == BuildingType.School ? "построена школа" : "разведён костёр"));
            World.TerrainRevision++;
            return b;
        }

        private int FindBuildingSite(SettlementData s)
        {
            int offset = RandomRange(0, 4);
            for (int radius = 2; radius <= ExpansionCatalog.BuildingRadius(s); radius++)
            for (int side = 0; side < 4; side++)
            for (int t = -radius; t <= radius; t += 2)
            {
                int d = (side + offset) % 4;
                int x = s.X + (d == 0 ? radius : d == 1 ? -radius : t);
                int y = s.Y + (d == 2 ? radius : d == 3 ? -radius : t);
                if (!PlacementRules.IsDryGround(World, x, y) || !World.IsWalkable(x, y)) continue;
                TileData cell = World.Cells[World.Index(x, y)];
                if (cell.BuildingId != 0 || cell.Fire > 0 || (cell.SettlementId != 0 && cell.SettlementId != s.Id)) continue;
                bool tooClose = false;
                for (int i = 0; i < World.Buildings.Count; i++) if (DistanceSquared(x, y, World.Buildings[i].X, World.Buildings[i].Y) < 4 || World.Buildings[i].EntranceCell == World.Index(x,y)) { tooClose = true; break; }
                if (!tooClose) foreach (UnitData resident in World.Units) if ((int)resident.X == x && (int)resident.Y == y) { tooClose = true; break; }
                if (!tooClose && navigation.Find(World.Index(s.X, s.Y), World.Index(x, y), scratchPath)) return World.Index(x, y);
            }
            return -1;
        }

        private void ClaimTerritory(SettlementData s, int radius)
        {
            if (!World.IsWalkable(s.X, s.Y)) return;
            Queue<int> queue = new Queue<int>(); HashSet<int> seen = new HashSet<int>();
            int start = World.Index(s.X, s.Y); queue.Enqueue(start); seen.Add(start);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue(), x = cell % World.Width, y = cell / World.Width;
                if (World.Cells[cell].SettlementId == 0 || World.Cells[cell].SettlementId == s.Id) World.Cells[cell].SettlementId = s.Id;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + Dx[d], ny = y + Dy[d];
                    if (!World.IsWalkable(nx, ny) || DistanceSquared(nx, ny, s.X, s.Y) > radius * radius) continue;
                    int next = World.Index(nx, ny);
                    if (seen.Add(next) && (World.Cells[next].SettlementId == 0 || World.Cells[next].SettlementId == s.Id)) queue.Enqueue(next);
                }
            }
            World.TerrainRevision++;
        }

        private void RecountSettlements()
        {
            for (int i = 0; i < World.Settlements.Count; i++) { World.Settlements[i].Population = 0; World.Settlements[i].Buildings = 0; World.Settlements[i].Housing = 0; }
            for (int i = 0; i < World.Units.Count; i++) { SettlementData s = World.GetSettlement(World.Units[i].SettlementId); if (s != null && World.Units[i].Health > 0) s.Population++; }
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData b = World.Buildings[i]; SettlementData s = World.GetSettlement(b.SettlementId);
                if (s != null) { s.Buildings++; s.Housing += HousingCapacity(b); }
            }
        }

        private void DestroyInvalidBuildings()
        {
            bool removed = false;
            for (int i = World.Buildings.Count - 1; i >= 0; i--)
            {
                BuildingData b = World.Buildings[i];
                if (b.Health > 0 && (PlacementRules.IsDryGround(World, b.X, b.Y) || World.IsTemporaryFlood(World.Index(b.X, b.Y)))) continue;
                OnMaritimeBuildingRemoved(b); EvacuateRemovedBuilding(b);
                SalvageConstructionMaterials(b);
                DestroyIndustryBuilding(b);
                DestroyResourceStorage(b);
                LoseAgriculturalStorage(b);
                World.Cells[World.Index(b.X, b.Y)].BuildingId = 0;
                World.Buildings.RemoveAt(i); World.TerrainRevision++; removed = true;
            }
            if (removed) { AssignHomesAndOwners(); CleanupDomesticLinks(); World.NavigationRevision++; }
        }

        private void RemoveDead()
        {
            HashSet<int> removedIds = null;
            for (int i = World.Units.Count - 1; i >= 0; i--)
            {
                UnitData unit = World.Units[i];
                if (unit.Health > 0) continue;
                RecordRemains(unit); ReleaseExpansionOnDeath(unit);
                ReleaseMarket(unit); ReleaseMaritimeUnit(unit); ReleaseIndoorDeath(unit); DropWarEquipment(unit); ReleaseCaravanOnDeath(unit); LoseManufacturedEquipment(unit); LoseCargo(unit);
                DropAgriculturalCargo(unit); DropAnimalLoot(unit);
                if (removedIds == null) removedIds = new HashSet<int>();
                removedIds.Add(unit.Id);
                AddEvent(EventKind.Death, unit.X, unit.Y, unit.Name + " погибает");
                if (WildlifeCatalog.IsAnimal(unit.Species)) World.WildlifeDeaths++;
                World.Units.RemoveAt(i);
            }
            if (removedIds == null) return;
            AssignHomesAndOwners(); CleanupSocialDisputes(); CleanupDomesticLinks(); NormalizeWildlifePacks();
            for (int n = 0; n < World.Units.Count; n++) EnforceEquipment(World.Units[n]);
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData survivor = World.Units[i];
                if (!removedIds.Contains(survivor.TargetId)) continue;
                ChangeTask(survivor, UnitTask.Idle);
                survivor.TargetId = 0; survivor.DecisionTimer = 0;
                survivor.Path.Clear(); survivor.PathCursor = 0;
            }
        }

        private void CaptureAbandonedTerritory(SettlementData victor, SettlementData defeated)
        {
            bool present = false;
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData soldier = World.Units[i];
                if (soldier.SettlementId == victor.Id && soldier.Health > 0 && DistanceSquared(soldier.X, soldier.Y, defeated.X + .5f, defeated.Y + .5f) < 9) { present = true; break; }
            }
            if (!present) return;
            bool acquired = false;
            for (int i = 0; i < World.Cells.Length; i++)
                if (World.Cells[i].SettlementId == defeated.Id) { World.Cells[i].SettlementId = victor.Id; acquired = true; }
            for (int i = 0; i < World.Buildings.Count; i++)
                if (World.Buildings[i].SettlementId == defeated.Id)
                {
                    BuildingData b = World.Buildings[i];
                    for (int j = 0; j < b.PersonalStocks.Count; j++) b.Inventory[(int)b.PersonalStocks[j].Item] += b.PersonalStocks[j].Amount;
                    b.PersonalStocks.Clear(); b.SettlementId = victor.Id;
                }
            if (!acquired) return;
            victor.Food += defeated.Food; victor.Wood += defeated.Wood; victor.Stone += defeated.Stone;
            defeated.Food = 0; defeated.Wood = 0; defeated.Stone = 0;
            AddEvent(EventKind.War, defeated.X + .5f, defeated.Y + .5f, victor.Name + " занимает покинутое поселение «" + defeated.Name + "»");
            World.TerrainRevision++;
        }

        private UnitData FindEnemy(UnitData unit, int radius)
        {
            int x = (int)unit.X, y = (int)unit.Y;
            UnitData best = null; float closest = float.MaxValue;
            for (int cy = Math.Max(0, y - radius); cy <= Math.Min(World.Height - 1, y + radius); cy++)
            for (int cx = Math.Max(0, x - radius); cx <= Math.Min(World.Width - 1, x + radius); cx++)
            for (int n = spatialHeads[World.Index(cx, cy)]; n >= 0; n = spatialNext[n])
            {
                UnitData other = World.Units[n];
                if (other.Id == unit.Id || other.Health <= 0 || other.InsideBuildingId != 0 || other.ShipId != 0) continue;
                bool predator = PredatorWants(unit, other);
                bool defense = IsCivilized(unit.Species) && WildlifeCatalog.IsPredator(other.Species) && (other.Species == SpeciesType.Wolf || other.TargetId == unit.Id || PredatorWants(other, unit));
                bool war = IsCivilized(unit.Species) && IsCivilized(other.Species) && other.Age >= 16 && AtWar(unit.SettlementId, other.SettlementId);
                if (!predator && !defense && !war) continue;
                float distance = DistanceSquared(unit.X, unit.Y, other.X, other.Y);
                if (distance < closest) { closest = distance; best = other; }
            }
            return best;
        }

        private void SpreadDisease(UnitData source)
        {
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData other = World.Units[i];
                if (!other.Infected && DistanceSquared(source.X, source.Y, other.X, other.Y) < 4 && RandomValue() < .25f) other.Infected = true;
            }
        }

        private BuildingData FindFarm(int settlementId, int unitId)
        {
            UnitData worker = World.GetUnit(unitId);
            if (worker == null || CarrySpace(worker) <= .001f) return null;
            BuildingData selected = null; float best = float.MaxValue;
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData farm = World.Buildings[i];
                if (farm.SettlementId != settlementId || farm.Type != BuildingType.Farm || farm.HarvestReady <= .001f || !UsableBuilding(farm, settlementId)) continue;
                AgriculturalProduct product = CropProduct(farm.FarmCrop);
                float stock = World.GetAgriculturalStock(settlementId, product);
                SettlementData home = World.GetSettlement(settlementId);
                if ((product == AgriculturalProduct.Herbs && stock >= 12)
                    || (product == AgriculturalProduct.Fiber && stock >= (home?.Population ?? 0) * 3 + 12)
                    || (product == AgriculturalProduct.Hay && stock >= (CountBuildings(settlementId, BuildingType.Pasture) == 0 ? 20 : 60))) continue;
                // Each already assigned worker reserves only what they can actually carry from this harvest.
                float committed = 0;
                int cell = World.Index(farm.X, farm.Y);
                for (int u = 0; u < World.Units.Count; u++)
                {
                    UnitData other = World.Units[u];
                    if (other.Id != unitId && other.Health > 0 && other.Task == UnitTask.Farm && other.TargetCell == cell)
                        committed += CarrySpace(other);
                }
                if (committed >= farm.HarvestReady) continue;
                float distance = DistanceSquared(worker.X, worker.Y, farm.X + .5f, farm.Y + .5f);
                if (distance < best) { best = distance; selected = farm; }
            }
            return selected;
        }

        private int CountBuildings(int settlementId, BuildingType type)
        {
            int count = 0;
            for (int i = 0; i < World.Buildings.Count; i++) if (World.Buildings[i].SettlementId == settlementId && World.Buildings[i].Type == type) count++;
            return count;
        }

        private bool HasWorkingFarm(int settlementId, int cell)
        {
            int buildingId = World.Cells[cell].BuildingId;
            if (buildingId == 0) return false;
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                BuildingData building = World.Buildings[i];
                if (building.Id == buildingId) return building.Type == BuildingType.Farm && building.SettlementId == settlementId && building.Health > 0;
            }
            return false;
        }

        private bool SetTarget(UnitData unit, int x, int y)
        {
            if (unit.ExpeditionId != 0) return false;
            if (unit.ShipId != 0 || !World.InBounds(x,y)) return false;
            int requested = World.Index(x,y);
            BuildingData targetBuilding = World.GetBuilding(World.Cells[requested].BuildingId);
            bool indoors = targetBuilding != null && BuildingAccess.IsEnclosed(targetBuilding.Type) && World.BuildingAccessVersion != 0;
            if (indoors && (!BuildingAccess.MayEnter(World,unit,targetBuilding) || !BuildingAccess.IsUsable(World,targetBuilding))) return false;
            if (indoors && unit.InsideBuildingId == targetBuilding.Id) { ClearRoute(unit);unit.TargetCell=requested;return true; }
            int start = World.Index((int)unit.X, (int)unit.Y), goal = World.BuildingNavigationCell(requested);
            if (unit.InsideBuildingId != 0)
            { BuildingData previous = World.GetBuilding(unit.InsideBuildingId); start = previous == null ? -1 : FindBuildingExit(previous,true); }
            if (start < 0 || goal < 0 || !World.IsWalkable(goal % World.Width, goal / World.Width)) return false;
            if (!navigation.Find(start, goal, scratchPath, 4096, true))
            {
                // A long return from an old save or an interrupted expedition is not an unreachable home.
                // Expand only for an owned destination, using the same reusable BFS arrays and real route.
                BuildingData destination = targetBuilding;
                if (World.Cells.Length <= 4096 || destination == null || unit.SettlementId == 0 || destination.SettlementId != unit.SettlementId
                    || !navigation.Find(start, goal, scratchPath, World.Cells.Length, true)) return false;
            }
            if (unit.InsideBuildingId != 0) PlaceOutsideBuilding(unit,start);
            if (unit.TargetCell != World.Index(x, y)) ReleaseReservation(unit);
            unit.Path.Clear(); unit.Path.AddRange(scratchPath);
            // A grid route may already end in this cell while the resident is still
            // away from its centre. Queue the final approach instead of repeatedly
            // accepting an empty route outside a doorway's entry distance.
            if (indoors && unit.Path.Count == 0 && DistanceSquared(unit.X, unit.Y, goal % World.Width + .5f, goal / World.Width + .5f) > .0225f) unit.Path.Add(goal);
            unit.PathCursor = 0; unit.PathRevision = World.NavigationRevision; unit.TargetCell = World.Index(x, y);
            unit.EnteringBuildingId = indoors ? targetBuilding.Id : 0; unit.BuildingInteractionTimer = 0;
            return true;
        }

        private static bool MoveToward(UnitData unit, float x, float y, float distance)
        {
            float dx = x - unit.X, dy = y - unit.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length <= distance) { unit.X = x; unit.Y = y; return true; }
            unit.X += dx / length * distance; unit.Y += dy / length * distance; return false;
        }

        private void AddEvent(EventKind kind, float x, float y, string text)
        {
            World.Events.Add(new WorldEvent { Id = World.NextId++, Kind = kind, X = x, Y = y, Tick = World.Tick, Text = text });
            if (World.Events.Count > 128) World.Events.RemoveAt(0);
        }
        private float RandomValue() { return StableRandom.Value(ref World.RngState); }
        private bool AtWar(int firstId, int secondId)
        {
            firstId = World.SovereignOf(firstId); secondId = World.SovereignOf(secondId);
            if (!World.Laws.Warfare || firstId == secondId || firstId == 0 || secondId == 0) return false;
            int low = Math.Min(firstId, secondId), high = Math.Max(firstId, secondId);
            for (int i = 0; i < World.Relations.Count; i++)
                if (World.Relations[i].FirstId == low && World.Relations[i].SecondId == high) return World.Relations[i].AtWar;
            return false; // A different species or a resource deficit is not an implicit declaration of war.
        }
        private int RandomRange(int minimum, int maximum) { return StableRandom.Range(ref World.RngState, minimum, maximum); }
        public static bool IsCivilized(SpeciesType species) { return species >= SpeciesType.Human && species <= SpeciesType.Orc; }
        private static float DistanceSquared(float x, float y, float tx, float ty) { float dx = x - tx, dy = y - ty; return dx * dx + dy * dy; }
        private static string PowerName(PowerType power)
        {
            switch (power) { case PowerType.Fire: return "Пламя охватывает землю"; case PowerType.Rain: return "Живительный дождь"; case PowerType.Lightning: return "Удар молнии"; case PowerType.Meteor: return "Падение метеора"; case PowerType.Heal: return "Исцеление"; case PowerType.Bless: return "Благословение"; default: return "Распространяется чума"; }
        }
    }
}
