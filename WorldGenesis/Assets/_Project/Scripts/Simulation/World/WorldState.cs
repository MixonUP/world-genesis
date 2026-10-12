using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public enum TerrainType { DeepWater, ShallowWater, Sand, Grass, Forest, Desert, Snow, Mountain }
    public enum SpeciesType { Human, Elf, Dwarf, Orc, Sheep, Wolf, Deer, Boar, Fox, Bear, DireWolf, SpiritStag, Goat, Cow, Chicken, Donkey, Horse }
    public enum PowerType { Fire, Rain, Lightning, Meteor, Heal, Bless, Plague }
    public enum BuildingType { Hall, House, Farm, Tower, Sawmill, Stoneworks, Kiln, Well, Warehouse, Shop, WoodWall, Gate, School, Pasture, Brewery }
    public enum UnitTask { Idle, Wander, Gather, Farm, Return, Fight, Flee, Eat, Rest, Socialize, Patrol, Process, Haul, Play, Learn, Drink, Fish, Caravan, Hunt, Tend, Brew, CraftEquipment }
    public enum Sex { Female, Male }
    public enum UnitProfession { None, Farmer, Forester, Guard, Hunter, Herder, Merchant, Carpenter }
    public enum GovernmentType { Unset, Feudal, Republic, Theocracy }
    public enum EventKind { Fire, Rain, Lightning, Meteor, Heal, Bless, Plague, Birth, Death, Founded, Built, War }
    public enum WorldPreset { Island, Archipelago, Continent }
    public enum WorldLaw { Births, Warfare, Aging, VegetationGrowth, FireSpread }

    [Serializable]
    public sealed class SettlementRelation
    {
        public int FirstId, SecondId;
        public bool AtWar;
    }

    [Serializable]
    public partial struct TileData
    {
        public float Height, Moisture, Temperature, Vegetation, Fire;
        public TerrainType Terrain;
        public int SettlementId, BuildingId;
        public bool River;
        public int DrainageParent;
        public float Flow, RouteHeight;
    }

    [Serializable]
    public sealed partial class UnitData
    {
        public int Id;
        public SpeciesType Species;
        public Sex Sex;
        public bool IsChild { get { return Age < 16; } }
        public float Education, Prudence, Ambition, Generosity;
        public float X, Y, Health, MaxHealth, Age, Hunger;
        public int SettlementId;
        public string Name;
        public UnitTask Task;
        public UnitProfession Profession;
        public float Fatigue, SocialNeed;
        public long TaskStartedTick;
        public bool Blessed, Infected;
        public float DecisionTimer, WorkTimer, AttackTimer, CarriedFood, CarriedWood;
        public int TargetId, TargetCell = -1, PathCursor, PathRevision;
        public List<int> Path = new List<int>();
        public ItemType CargoKind;
        public ItemType PickupKind;
        public float CargoAmount;
        public int ResourceCell = -1, SourceBuildingId, TargetBuildingId;
        public int CargoOwnerId;
        public int GroundStockIndex = -1;
    }

    [Serializable]
    public sealed partial class SettlementData
    {
        public int Id;
        public string Name;
        public SpeciesType Species;
        public int X, Y, Population, Housing, Buildings, ColorIndex;
        public float Food, Wood, Stone, BuildTimer, BirthTimer;
        public int EnemyId;
        public GovernmentType Government;
        public int RulerId;
        public long GovernmentSinceTick, NextElectionTick;
        public EconomicPolicy EconomyPolicy;
        public ChildLaborPolicy ChildLabor;
        public ResourceFocus ResourceFocus;
        public float TaxRate;
        public int PolicyRulerId;
        public long NextPolicyReviewTick, LastDecreeTick;
        public string LastDecreeReason = "";
    }

    [Serializable]
    public sealed partial class BuildingData
    {
        public int Id, SettlementId, X, Y;
        public BuildingType Type;
        public float Health = 100f;
        public float[] Inventory = new float[ResourceCatalog.ItemCount];
        public int WorkerId;
        public RecipeType ActiveRecipe;
        public float ProcessProgress;
        public List<OwnedGoodsData> PersonalStocks = new List<OwnedGoodsData>();
        public float[] TaxStocks = new float[ResourceCatalog.ItemCount];
    }

    [Serializable]
    public sealed class WorldEvent
    {
        public int Id;
        public EventKind Kind;
        public float X, Y;
        public long Tick;
        public string Text;
    }

    [Serializable]
    public sealed class WorldLaws
    {
        public bool Births = true;
        public bool Warfare = true;
        public bool Aging = true;
        public bool VegetationGrowth = true;
        public bool FireSpread = true;
    }

    [Serializable]
    public sealed partial class WorldState
    {
        public int SchemaVersion = 1, GeneratorVersion = 1, ContentVersion = 1;
        public int BehaviorVersion;
        public int PoliticsVersion;
        public int DemographyVersion, EconomyVersion;
        public int Width, Height, Seed, Revision, TerrainRevision, NavigationRevision, NextId = 1;
        public long Tick;
        public float SimulatedSeconds, StepRemainder;
        public uint RngState;
        public WorldPreset Preset;
        public TileData[] Cells;
        public List<UnitData> Units = new List<UnitData>();
        public List<SettlementData> Settlements = new List<SettlementData>();
        public List<BuildingData> Buildings = new List<BuildingData>();
        public List<WorldEvent> Events = new List<WorldEvent>();
        public List<SettlementRelation> Relations = new List<SettlementRelation>();
        public WorldLaws Laws = new WorldLaws();

        public bool InBounds(int x, int y) { return x >= 0 && y >= 0 && x < Width && y < Height; }
        public int Index(int x, int y) { return y * Width + x; }
        public bool IsWalkable(int x, int y)
        {
            if (!IsTerrainWalkable(x, y)) return false;
            int buildingId = Cells[Index(x, y)].BuildingId;
            if (buildingId == 0) return true;
            BuildingData building = GetBuilding(buildingId);
            return building == null || (building.Type != BuildingType.WoodWall && (building.Type != BuildingType.Gate || building.GateOpen));
        }
        public SettlementData GetSettlement(int id)
        {
            for (int i = 0; i < Settlements.Count; i++) if (Settlements[i].Id == id) return Settlements[i];
            return null;
        }
        public UnitData GetUnit(int id)
        {
            for (int i = 0; i < Units.Count; i++) if (Units[i].Id == id) return Units[i];
            return null;
        }
        public bool Validate(out string error)
        {
            error = null;
            if (SchemaVersion != 1 || GeneratorVersion != 1 || ContentVersion != 1 || BehaviorVersion < 0 || BehaviorVersion > 2 || PoliticsVersion < 0 || PoliticsVersion > 1) error = "Unsupported world version.";
            else if (Width < 16 || Height < 16 || Width > 512 || Height > 512 || Cells == null || Cells.Length != Width * Height) error = "Invalid world dimensions.";
            else if (Units == null || Settlements == null || Buildings == null || Events == null || Relations == null || Laws == null || RngState == 0 || Tick < 0 || !Finite(SimulatedSeconds) || SimulatedSeconds < 0 || !UnitRange(StepRemainder) || StepRemainder >= .10001f || (int)Preset < 0 || (int)Preset > 2 || Units.Count > 10000 || Settlements.Count > 10000 || Buildings.Count > Cells.Length || Events.Count > 128 || Relations.Count > 100000) error = "Invalid world state.";
            if (error != null) return false;
            HashSet<int> ids = new HashSet<int>();
            int maximumId = 0;
            for (int i = 0; i < Cells.Length; i++)
            {
                TileData c = Cells[i];
                if ((int)c.Terrain < 0 || (int)c.Terrain > 7 || !Finite(c.Height) || !UnitRange(c.Moisture) || !UnitRange(c.Temperature) || !UnitRange(c.Vegetation) || !UnitRange(c.Fire) || !Finite(c.Flow) || c.Flow < 0 || !Finite(c.RouteHeight) || c.DrainageParent < -1 || c.DrainageParent >= Cells.Length)
                { error = "Invalid terrain cell."; return false; }
            }
            for (int i = 0; i < Settlements.Count; i++)
            {
                SettlementData s = Settlements[i];
                if (s == null || !AddId(s.Id, ids, ref maximumId) || !InBounds(s.X, s.Y) || (int)s.Species < 0 || (int)s.Species > 3 || !Stock(s.Food) || !Stock(s.Wood) || !Stock(s.Stone) || !Stock(s.BirthTimer) || !Stock(s.BuildTimer) || s.ColorIndex < 0 || s.ColorIndex > 10000 || s.Population < 0 || s.Housing < 0 || s.Buildings < 0 || s.Name == null || s.Name.Length > 256)
                { error = "Invalid settlement."; return false; }
            }
            for (int i = 0; i < Settlements.Count; i++)
                if (Settlements[i].EnemyId != 0 && (Settlements[i].EnemyId == Settlements[i].Id || GetSettlement(Settlements[i].EnemyId) == null)) { error = "Invalid diplomatic reference."; return false; }
            HashSet<long> relationPairs = new HashSet<long>();
            for (int i = 0; i < Relations.Count; i++)
            {
                SettlementRelation relation = Relations[i];
                if (relation == null || relation.FirstId >= relation.SecondId || GetSettlement(relation.FirstId) == null || GetSettlement(relation.SecondId) == null || !relationPairs.Add(((long)relation.FirstId << 32) | (uint)relation.SecondId)) { error = "Invalid diplomatic relationship."; return false; }
            }
            for (int i = 0; i < Units.Count; i++)
            {
                UnitData u = Units[i];
                if (u == null || !AddId(u.Id, ids, ref maximumId) || !Finite(u.X) || !Finite(u.Y) || u.X < 0 || u.Y < 0 || u.X >= Width || u.Y >= Height || !Stock(u.MaxHealth) || u.MaxHealth <= 0 || !Stock(u.Health) || !Stock(u.Age) || !Stock(u.Hunger) || u.Hunger > 1 || (int)u.Species < 0 || (int)u.Species > 16 || u.Path == null || u.Path.Count > Cells.Length || u.PathCursor < 0 || u.PathCursor > u.Path.Count || (u.SettlementId != 0 && GetSettlement(u.SettlementId) == null) || !Finite(u.DecisionTimer) || !Stock(u.WorkTimer) || !Finite(u.AttackTimer) || !Stock(u.CarriedFood) || !Stock(u.CarriedWood) || (int)u.Task < 0 || (int)u.Task > 21 || u.TargetCell < -1 || u.TargetCell >= Cells.Length || u.Name == null || u.Name.Length > 256 || (int)u.Profession < 0 || (int)u.Profession > 7 || !UnitRange(u.Fatigue) || !UnitRange(u.SocialNeed) || u.TaskStartedTick < 0 || u.TaskStartedTick > Tick || u.Health > u.MaxHealth)
                { error = "Invalid unit."; return false; }
                if (BehaviorVersion >= 2 && u.Profession == UnitProfession.Guard)
                { error = "Military recruitment requires research and equipment that are not implemented yet."; return false; }
                for (int p = 0; p < u.Path.Count; p++) if (u.Path[p] < 0 || u.Path[p] >= Cells.Length) { error = "Invalid navigation path."; return false; }
            }
            for (int i = 0; i < Settlements.Count; i++)
            {
                SettlementData s = Settlements[i];
                if ((int)s.Government < 0 || (int)s.Government > 3 || s.RulerId < 0 || s.GovernmentSinceTick < 0 || s.GovernmentSinceTick > Tick || s.NextElectionTick < 0 || (PoliticsVersion == 1 && s.Government == GovernmentType.Unset))
                { error = "Invalid settlement government."; return false; }
                if ((s.Government != GovernmentType.Republic && s.NextElectionTick != 0) || (s.Government == GovernmentType.Republic && (s.NextElectionTick < Tick || s.NextElectionTick - Tick > SimulationEngine.RepublicElectionIntervalTicks || s.NextElectionTick <= s.GovernmentSinceTick)))
                { error = "Invalid election schedule."; return false; }
                if (s.RulerId == 0) continue;
                UnitData ruler = GetUnit(s.RulerId);
                if (ruler == null || ruler.Health <= 0 || ruler.Age < 16 || (int)ruler.Species > (int)SpeciesType.Orc || ruler.SettlementId != s.Id || s.Government == GovernmentType.Unset)
                { error = "Invalid settlement ruler."; return false; }
            }
            HashSet<int> buildingIds = new HashSet<int>();
            for (int i = 0; i < Buildings.Count; i++)
            {
                BuildingData b = Buildings[i];
                if (b == null || !AddId(b.Id, ids, ref maximumId) || !InBounds(b.X, b.Y) || GetSettlement(b.SettlementId) == null || !Stock(b.Health) || b.Health > 100 || (int)b.Type < 0 || (int)b.Type > 14 || Cells[Index(b.X, b.Y)].BuildingId != b.Id)
                { error = "Invalid building."; return false; }
                buildingIds.Add(b.Id);
            }
            for (int i = 0; i < Cells.Length; i++)
            {
                if (Cells[i].SettlementId != 0 && GetSettlement(Cells[i].SettlementId) == null) { error = "Invalid territory owner."; return false; }
                if (Cells[i].BuildingId != 0)
                {
                    if (!buildingIds.Remove(Cells[i].BuildingId)) { error = "Invalid or duplicate building reference."; return false; }
                }
            }
            for (int i = 0; i < Events.Count; i++)
            {
                WorldEvent e = Events[i];
                if (e == null || !AddId(e.Id, ids, ref maximumId) || !Finite(e.X) || !Finite(e.Y) || e.Tick < 0 || e.Tick > Tick || (int)e.Kind < 0 || (int)e.Kind > 11) { error = "Invalid world event."; return false; }
            }
            if (NextId <= maximumId) { error = "Invalid next entity identifier."; return false; }
            return ValidateResources(out error) && ValidateCivilEconomy(out error) && ValidateDevelopment(out error) && ValidateSocial(out error) && ValidateAgriculture(out error) && ValidateWildlife(out error) && ValidateCaravans(out error);
        }
        private static bool AddId(int id, HashSet<int> ids, ref int maximum) { maximum = Math.Max(id, maximum); return id > 0 && ids.Add(id); }
        private static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
        private static bool Stock(float v) { return Finite(v) && v >= 0; }
        private static bool UnitRange(float v) { return Stock(v) && v <= 1; }
    }

    public static class StableRandom
    {
        public static uint Hash(int seed, int x, int y, uint stream = 0)
        {
            unchecked
            {
                uint h = (uint)seed ^ (uint)x * 0x9E3779B9u ^ (uint)y * 0x85EBCA6Bu ^ stream;
                h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; return h ^ (h >> 16);
            }
        }
        public static float Coordinate(int seed, int x, int y, uint stream = 0) { return (Hash(seed, x, y, stream) >> 8) * (1f / 16777216f); }
        public static uint Next(ref uint state)
        {
            if (state == 0) state = 0x6D2B79F5u;
            state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state;
        }
        public static float Value(ref uint state) { return (Next(ref state) >> 8) * (1f / 16777216f); }
        public static int Range(ref uint state, int minimum, int maximum) { return minimum + (int)(Value(ref state) * (maximum - minimum)); }
    }
}
