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
            World = state; navigation = new GridNavigation(state);
            nextFire = new float[state.Cells.Length]; spatialHeads = new int[state.Cells.Length];
            MigrateBehavior();
            MigratePolitics();
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
            BuildSpatialIndex();
            reservations.Clear();
            for (int i = 0; i < World.Units.Count; i++)
            {
                UnitData unit = World.Units[i];
                if (unit.Task == UnitTask.Gather && unit.TargetCell >= 0 && !reservations.ContainsKey(unit.TargetCell)) reservations.Add(unit.TargetCell, unit.Id);
            }
            if (World.Tick % 5 == 0) UpdateEnvironment(.5f);
            for (int i = 0; i < World.Units.Count; i++) UpdateUnit(World.Units[i], TickSeconds);
            RemoveDead();
            if (World.Tick % 10 == 0) UpdateSettlements(1f);
            UpdatePolitics();
            World.Revision++;
        }

        public void Spawn(SpeciesType species, int x, int y, int count = 1)
        {
            if ((int)species < 0 || (int)species > 5 || count < 0 || count > 1000) throw new ArgumentOutOfRangeException(nameof(species));
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
            }
            for (int n = 0; n < count && World.Units.Count < 10000; n++)
            {
                int ux = x, uy = y;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    int nx = x + RandomRange(-3, 4), ny = y + RandomRange(-3, 4);
                    if (World.IsWalkable(nx, ny) && navigation.Find(World.Index(x, y), World.Index(nx, ny), scratchPath, 64)) { ux = nx; uy = ny; break; }
                }
                AddUnit(species, ux, uy, settlement == null ? 0 : settlement.Id, false);
            }
            if (settlement != null) { RecountSettlements(); UpdatePolitics(); }
            World.Revision++;
        }

        public void Paint(TerrainType terrain, int x, int y, int radius)
        {
            if ((int)terrain < 0 || (int)terrain > 7) throw new ArgumentOutOfRangeException(nameof(terrain));
            radius = Math.Max(0, Math.Min(32, radius));
            if (x < -radius || y < -radius || x >= World.Width + radius || y >= World.Height + radius) return;
            bool changed = false;
            for (int cy = Math.Max(0, y - radius); cy <= Math.Min(World.Height - 1, y + radius); cy++)
            for (int cx = Math.Max(0, x - radius); cx <= Math.Min(World.Width - 1, x + radius); cx++)
            {
                if (DistanceSquared(cx, cy, x, y) > radius * radius) continue;
                int i = World.Index(cx, cy);
                ref TileData tile = ref World.Cells[i];
                tile.Terrain = terrain; tile.Fire = 0; tile.River = false;
                tile.Height = terrain == TerrainType.DeepWater ? .2f : terrain == TerrainType.ShallowWater ? .4f : terrain == TerrainType.Mountain ? .85f : .58f;
                tile.Vegetation = terrain == TerrainType.Forest ? 1 : terrain == TerrainType.Grass ? .5f : 0;
                if (!World.IsWalkable(cx, cy)) tile.SettlementId = 0;
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
                else if (power == PowerType.Fire && World.IsWalkable(cx, cy)) tile.Fire = 1;
                else if (power == PowerType.Lightning && World.IsWalkable(cx, cy)) tile.Fire = .8f;
                else if (power == PowerType.Meteor)
                {
                    tile.Vegetation = 0; tile.River = false;
                    if (distance <= effectiveRadius * effectiveRadius * .32f) { tile.Terrain = TerrainType.ShallowWater; tile.Height = .38f; tile.Fire = 0; tile.SettlementId = 0; }
                    else if (World.IsWalkable(cx, cy)) { tile.Terrain = TerrainType.Sand; tile.Fire = 1; }
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
            AddEvent((EventKind)(int)power, x + .5f, y + .5f, PowerName(power));
            DestroyInvalidBuildings(); RemoveDead(); RecountSettlements();
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
                UnitData u = World.Units[i]; int cell = World.Index((int)u.X, (int)u.Y);
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
                    fire = Math.Max(0, fire - (tile.Vegetation < .05f ? .40f : .045f + tile.Moisture * .05f) * dt);
                    changed = true;
                }
                else if (World.Laws.FireSpread && tile.Vegetation > .12f)
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
                if (World.Laws.VegetationGrowth && fire == 0 && tile.Vegetation > .001f && tile.Vegetation < .99f && (tile.Terrain == TerrainType.Grass || tile.Terrain == TerrainType.Forest))
                {
                    float capacity = tile.Terrain == TerrainType.Forest ? 1 : .65f;
                    float v = World.Cells[i].Vegetation;
                    World.Cells[i].Vegetation = capacity * v / (v + (capacity - v) * (float)Math.Exp(-.012f * dt));
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
            int x = (int)unit.X, y = (int)unit.Y;
            if (World.Laws.Aging) unit.Age += dt * .1f;
            if (IsCivilized(unit.Species) && unit.Age >= 16 && unit.Profession == UnitProfession.None) unit.Profession = ProfessionFor(unit.Id);
            unit.Hunger = Math.Min(1, unit.Hunger + dt * (IsCivilized(unit.Species) ? .005f : .004f));
            if (unit.Task != UnitTask.Rest)
            {
                float exertion = unit.Task == UnitTask.Flee || unit.Task == UnitTask.Fight ? .012f : unit.Task == UnitTask.Gather || unit.Task == UnitTask.Farm ? .006f : unit.PathCursor < unit.Path.Count ? .003f : .0015f;
                unit.Fatigue = Math.Min(1, unit.Fatigue + dt * exertion);
            }
            if (IsCivilized(unit.Species)) unit.SocialNeed = Math.Min(1, unit.SocialNeed + dt * .0025f);
            unit.AttackTimer = Math.Max(0, unit.AttackTimer - dt);
            unit.DecisionTimer -= dt;
            TileData tile = World.Cells[World.Index(x, y)];
            unit.Health = Math.Max(0, unit.Health - tile.Fire * dt * 15);
            if (!World.IsWalkable(x, y))
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
            SettlementData home = World.GetSettlement(unit.SettlementId);
            UnitData enemy = FindEnemy(unit, 2);
            UnitData predator = unit.Species == SpeciesType.Sheep ? FindPredator(unit) : null;
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
            else if (enemy != null && unit.Species != SpeciesType.Sheep)
            {
                bool enteringCombat = unit.Task != UnitTask.Fight;
                ChangeTask(unit, UnitTask.Fight); unit.TargetId = enemy.Id;
                if (enteringCombat) { ClearRoute(unit); unit.DecisionTimer = 0; }
                if (DistanceSquared(unit.X, unit.Y, enemy.X, enemy.Y) <= 2.2f && unit.AttackTimer <= 0)
                {
                    ClearRoute(unit);
                    float damage = unit.Species == SpeciesType.Orc ? 12 : unit.Species == SpeciesType.Wolf ? 9 : 7;
                    enemy.Health = Math.Max(0, enemy.Health - damage * (unit.Blessed ? 1.5f : 1)); unit.AttackTimer = .8f;
                    if (unit.Species == SpeciesType.Wolf && enemy.Health == 0) unit.Hunger = 0;
                }
                else if (unit.DecisionTimer <= 0) { SetTarget(unit, (int)enemy.X, (int)enemy.Y); unit.DecisionTimer = 1; }
            }
            else if (NeedsImmediateDecision(unit) || (!CommittedActivity(unit) && unit.DecisionTimer <= 0) || (unit.Task == UnitTask.Fight && unit.TargetId != 0) || unit.Task == UnitTask.Flee)
            {
                unit.TargetId = 0; Decide(unit, home); unit.DecisionTimer = 2f + RandomValue() * 2f;
            }
            if (unit.PathCursor < unit.Path.Count)
            {
                if (unit.PathRevision != World.NavigationRevision) { ClearRoute(unit); ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return; }
                int target = unit.Path[unit.PathCursor];
                int tx = target % World.Width, ty = target / World.Width;
                if (!World.IsWalkable(tx, ty) || (unit.Task != UnitTask.Flee && World.Cells[target].Fire > .15f)) { ClearRoute(unit); ChangeTask(unit, UnitTask.Idle); unit.DecisionTimer = 0; return; }
                float speed = unit.Species == SpeciesType.Elf ? 1.7f : unit.Species == SpeciesType.Wolf ? 2.2f : unit.Species == SpeciesType.Dwarf ? 1.1f : 1.45f;
                if (unit.Task == UnitTask.Flee) speed *= 1.45f;
                else speed *= 1 - unit.Fatigue * .25f;
                if (MoveToward(unit, tx + .5f, ty + .5f, dt * speed)) unit.PathCursor++;
            }
            else if (!PerformNeedsActivity(unit, home, dt) && home != null && unit.Age >= 16) Work(unit, home, dt);
        }

        private void Decide(UnitData unit, SettlementData home)
        {
            if (unit.Hunger > .50f && ChooseFood(unit, home)) return;
            if (unit.Fatigue > .72f && ChooseRest(unit, home)) return;
            if (home != null && unit.Age >= 16)
            {
                if (unit.CarriedWood >= 2.8f || unit.CarriedFood >= 2.8f || ((unit.CarriedFood > 0 || unit.CarriedWood > 0) && unit.Task != UnitTask.Farm && unit.Task != UnitTask.Gather))
                {
                    if (SetTarget(unit, home.X, home.Y)) { ChangeTask(unit, UnitTask.Return); return; }
                }
                if (unit.SocialNeed > .70f && unit.Hunger < .5f && ChooseSocial(unit, home)) return;
                float foodNeed = WorldGenerator.Clamp01(1 - home.Food / (home.Population * 5f + 1));
                float woodNeed = WorldGenerator.Clamp01(1 - home.Wood / (home.Population * 3f + 20));
                float farmScore = .35f + foodNeed * .55f + (unit.Profession == UnitProfession.Farmer ? .3f : 0);
                float woodScore = .10f + woodNeed * .6f + (unit.Profession == UnitProfession.Forester ? .35f : 0);
                if (woodScore >= farmScore && ChooseGather(unit)) return;
                BuildingData farm = FindFarm(home.Id, unit.Id);
                if (farm != null && SetTarget(unit, farm.X, farm.Y)) { ChangeTask(unit, UnitTask.Farm); return; }
                if (ChooseGather(unit)) return;
                if (unit.CarriedFood > 0 || unit.CarriedWood > 0)
                {
                    if (SetTarget(unit, home.X, home.Y)) { ChangeTask(unit, UnitTask.Return); return; }
                }
            }
            else if (IsCivilized(unit.Species) && home != null && unit.SocialNeed > .5f && ChooseSocial(unit, home)) return;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int range = IsCivilized(unit.Species) && unit.Age < 16 ? 3 : 7;
                int x = (int)unit.X + RandomRange(-range, range + 1), y = (int)unit.Y + RandomRange(-range, range + 1);
                if (SetTarget(unit, x, y)) { ChangeTask(unit, UnitTask.Wander); return; }
            }
            ClearRoute(unit); ChangeTask(unit, UnitTask.Idle);
        }

        private void Work(UnitData unit, SettlementData home, float dt)
        {
            // Civilian Fight is a local emergency response. Siege jobs require future equipped military recruitment.
            if (unit.Task == UnitTask.Return && DistanceSquared(unit.X, unit.Y, home.X + .5f, home.Y + .5f) < 2)
            { home.Wood += unit.CarriedWood; home.Food += unit.CarriedFood; unit.CarriedFood = 0; unit.CarriedWood = 0; unit.DecisionTimer = 0; }
            else if (unit.Task == UnitTask.Gather && unit.TargetCell >= 0)
            {
                ref TileData tile = ref World.Cells[unit.TargetCell];
                if (tile.Vegetation > .02f && tile.Fire <= 0 && unit.CarriedWood < 3)
                {
                    float removed = Math.Min(tile.Vegetation, .075f * dt);
                    tile.Vegetation -= removed; unit.CarriedWood += removed * 12; World.TerrainRevision++;
                }
                else unit.DecisionTimer = 0;
            }
            else if (unit.Task == UnitTask.Farm && unit.TargetCell >= 0 && HasWorkingFarm(home.Id, unit.TargetCell) && World.Cells[unit.TargetCell].Fire <= 0)
            {
                unit.CarriedFood += dt * .42f;
                if (unit.CarriedFood >= 3) unit.DecisionTimer = 0;
            }
        }

        private void UpdateSettlements(float dt)
        {
            RecountSettlements();
            for (int i = 0; i < World.Settlements.Count; i++)
            {
                SettlementData s = World.Settlements[i];
                if (s.Population == 0) continue;
                s.BuildTimer += dt; s.BirthTimer += dt;
                if (World.Laws.Warfare && s.Population >= 8)
                {
                    SettlementData enemy = World.GetSettlement(s.EnemyId);
                    if (enemy != null && enemy.Population == 0) CaptureAbandonedTerritory(s, enemy);
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
                if (s.BuildTimer >= 8 && s.Wood >= 10)
                {
                    BuildingType type = s.Housing < s.Population + 4 ? BuildingType.House : CountBuildings(s.Id, BuildingType.Farm) * 5 < s.Population ? BuildingType.Farm : s.Buildings < 18 ? BuildingType.House : BuildingType.Tower;
                    int site = FindBuildingSite(s);
                    if (site >= 0 && s.Buildings < 24) { s.Wood -= 10; s.BuildTimer = 0; AddBuilding(s, site % World.Width, site / World.Width, type); ClaimTerritory(s, Math.Min(14, 5 + s.Buildings)); }
                }
                if (World.Laws.Births && s.BirthTimer >= 10 && s.Population < s.Housing && s.Food >= 8 && World.Units.Count < 10000)
                {
                    int adults = 0;
                    for (int u = 0; u < World.Units.Count; u++)
                    {
                        UnitData parent = World.Units[u];
                        if (parent.SettlementId == s.Id && parent.Age >= 16 && parent.Health > 0 && DistanceSquared(parent.X, parent.Y, s.X + .5f, s.Y + .5f) < 9) adults++;
                    }
                    if (adults >= 2 && World.IsWalkable(s.X, s.Y))
                    { s.Food -= 8; s.BirthTimer = 0; AddUnit(s.Species, s.X, s.Y, s.Id, true); s.Population++; AddEvent(EventKind.Birth, s.X + .5f, s.Y + .5f, "Рождение в «" + s.Name + "»"); }
                }
            }
        }

        private SettlementData FoundSettlement(SpeciesType species, int x, int y)
        {
            SettlementData s = new SettlementData { Id = World.NextId++, Species = species, X = x, Y = y, Food = 28, Wood = 18, Stone = 0, ColorIndex = World.Settlements.Count % 8, Name = TownRoots[RandomRange(0, TownRoots.Length)] + " " + (World.Settlements.Count + 1) };
            InitializeGovernment(s);
            World.Settlements.Add(s);
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
            float health = species == SpeciesType.Dwarf ? 130 : species == SpeciesType.Orc ? 120 : species == SpeciesType.Sheep ? 35 : species == SpeciesType.Wolf ? 65 : 100;
            UnitData unit = new UnitData { Id = World.NextId++, Species = species, X = x + .5f, Y = y + .5f, SettlementId = settlementId, Health = health, MaxHealth = health, Age = child ? 0 : IsCivilized(species) ? 18 + RandomValue() * 20 : 2 + RandomValue() * 5, Hunger = .05f, Name = GivenNames[RandomRange(0, GivenNames.Length)], DecisionTimer = RandomValue() * 2f, TaskStartedTick = World.Tick };
            unit.Profession = !child && IsCivilized(species) ? ProfessionFor(unit.Id) : UnitProfession.None;
            unit.Fatigue = StableRandom.Coordinate(World.Seed, unit.Id, 0, 3001) * .4f;
            unit.SocialNeed = IsCivilized(species) ? StableRandom.Coordinate(World.Seed, unit.Id, 0, 3002) * .4f : 0;
            World.Units.Add(unit);
        }

        private void AddBuilding(SettlementData s, int x, int y, BuildingType type)
        {
            BuildingData b = new BuildingData { Id = World.NextId++, SettlementId = s.Id, X = x, Y = y, Type = type };
            World.Buildings.Add(b);
            World.Cells[World.Index(x, y)].BuildingId = b.Id;
            World.Cells[World.Index(x, y)].SettlementId = s.Id;
            World.Cells[World.Index(x, y)].Vegetation = 0;
            s.Buildings++; s.Housing += type == BuildingType.Hall ? 8 : type == BuildingType.House ? 5 : 0;
            AddEvent(EventKind.Built, x + .5f, y + .5f, s.Name + ": " + (type == BuildingType.House ? "построен дом" : type == BuildingType.Farm ? "разбито поле" : type == BuildingType.Tower ? "построена башня" : "возведена ратуша"));
            World.TerrainRevision++;
        }

        private int FindBuildingSite(SettlementData s)
        {
            int offset = RandomRange(0, 4);
            for (int radius = 2; radius <= 13; radius++)
            for (int side = 0; side < 4; side++)
            for (int t = -radius; t <= radius; t += 2)
            {
                int d = (side + offset) % 4;
                int x = s.X + (d == 0 ? radius : d == 1 ? -radius : t);
                int y = s.Y + (d == 2 ? radius : d == 3 ? -radius : t);
                if (!World.IsWalkable(x, y)) continue;
                TileData cell = World.Cells[World.Index(x, y)];
                if (cell.BuildingId != 0 || cell.Fire > 0 || (cell.SettlementId != 0 && cell.SettlementId != s.Id)) continue;
                bool tooClose = false;
                for (int i = 0; i < World.Buildings.Count; i++) if (DistanceSquared(x, y, World.Buildings[i].X, World.Buildings[i].Y) < 4) { tooClose = true; break; }
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
                if (s != null) { s.Buildings++; s.Housing += b.Type == BuildingType.Hall ? 8 : b.Type == BuildingType.House ? 5 : 0; }
            }
        }

        private void DestroyInvalidBuildings()
        {
            for (int i = World.Buildings.Count - 1; i >= 0; i--)
            {
                BuildingData b = World.Buildings[i];
                if (b.Health > 0 && World.IsWalkable(b.X, b.Y)) continue;
                World.Cells[World.Index(b.X, b.Y)].BuildingId = 0;
                World.Buildings.RemoveAt(i); World.TerrainRevision++;
            }
        }

        private void RemoveDead()
        {
            HashSet<int> removedIds = null;
            for (int i = World.Units.Count - 1; i >= 0; i--)
            {
                UnitData unit = World.Units[i];
                if (unit.Health > 0) continue;
                if (removedIds == null) removedIds = new HashSet<int>();
                removedIds.Add(unit.Id);
                AddEvent(EventKind.Death, unit.X, unit.Y, unit.Name + " погибает");
                World.Units.RemoveAt(i);
            }
            if (removedIds == null) return;
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
                if (World.Buildings[i].SettlementId == defeated.Id) World.Buildings[i].SettlementId = victor.Id;
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
                if (other.Id == unit.Id || other.Health <= 0) continue;
                bool predator = unit.Species == SpeciesType.Wolf && other.Species == SpeciesType.Sheep;
                bool defense = IsCivilized(unit.Species) && other.Species == SpeciesType.Wolf;
                bool war = IsCivilized(unit.Species) && IsCivilized(other.Species) && AtWar(unit.SettlementId, other.SettlementId);
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

        private int FindResource(UnitData unit, int radius)
        {
            int x = (int)unit.X, y = (int)unit.Y;
            for (int r = 0; r <= radius; r++)
            for (int oy = -r; oy <= r; oy++)
            for (int ox = -r; ox <= r; ox++)
            {
                if (Math.Abs(ox) != r && Math.Abs(oy) != r) continue;
                int cx = x + ox, cy = y + oy;
                if (!World.IsWalkable(cx, cy)) continue;
                int cell = World.Index(cx, cy);
                if (World.Cells[cell].Vegetation > .35f && World.Cells[cell].Fire == 0 && World.Cells[cell].BuildingId == 0 && !reservations.ContainsKey(cell) && navigation.Find(World.Index(x, y), cell, scratchPath, 1024, true)) return cell;
            }
            return -1;
        }

        private BuildingData FindFarm(int settlementId, int unitId)
        {
            int count = CountBuildings(settlementId, BuildingType.Farm);
            if (count == 0) return null;
            int target = unitId % count;
            for (int i = 0; i < World.Buildings.Count; i++)
                if (World.Buildings[i].SettlementId == settlementId && World.Buildings[i].Type == BuildingType.Farm && target-- == 0) return World.Buildings[i];
            return null;
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
            if (!World.IsWalkable(x, y)) return false;
            if (!navigation.Find(World.Index((int)unit.X, (int)unit.Y), World.Index(x, y), scratchPath, 4096, true)) return false;
            if (unit.TargetCell != World.Index(x, y)) ReleaseReservation(unit);
            unit.Path.Clear(); unit.Path.AddRange(scratchPath); unit.PathCursor = 0; unit.PathRevision = World.NavigationRevision; unit.TargetCell = World.Index(x, y);
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
            if (!World.Laws.Warfare || firstId == secondId || firstId == 0 || secondId == 0) return false;
            int low = Math.Min(firstId, secondId), high = Math.Max(firstId, secondId);
            for (int i = 0; i < World.Relations.Count; i++)
                if (World.Relations[i].FirstId == low && World.Relations[i].SecondId == high) return World.Relations[i].AtWar;
            SettlementData first = World.GetSettlement(firstId), second = World.GetSettlement(secondId);
            return first != null && second != null && first.Species != second.Species;
        }
        private int RandomRange(int minimum, int maximum) { return StableRandom.Range(ref World.RngState, minimum, maximum); }
        private static bool IsCivilized(SpeciesType species) { return (int)species <= (int)SpeciesType.Orc; }
        private static float DistanceSquared(float x, float y, float tx, float ty) { float dx = x - tx, dy = y - ty; return dx * dx + dy * dy; }
        private static string PowerName(PowerType power)
        {
            switch (power) { case PowerType.Fire: return "Пламя охватывает землю"; case PowerType.Rain: return "Живительный дождь"; case PowerType.Lightning: return "Удар молнии"; case PowerType.Meteor: return "Падение метеора"; case PowerType.Heal: return "Исцеление"; case PowerType.Bless: return "Благословение"; default: return "Распространяется чума"; }
        }
    }
}
