using System;
namespace WorldGenesis.Simulation
{
    public sealed class ShipDefinition
    {
        public readonly ShipKind Kind; public readonly string Name, Technology;
        public readonly float Capacity, Speed, Health, Seconds; public readonly int MinCrew, MaxCrew, PassengerCapacity, FishingGrade;
        public readonly ItemAmount[] Inputs;
        public ShipDefinition(ShipKind kind,string name,string tech,float capacity,float speed,float health,int crew,int maxCrew,int passengers,int fishing,float seconds,ItemAmount[] inputs)
        { Kind=kind;Name=name;Technology=tech;Capacity=capacity;Speed=speed;Health=health;MinCrew=crew;MaxCrew=maxCrew;PassengerCapacity=passengers;FishingGrade=fishing;Seconds=seconds;Inputs=inputs; }
    }
    public static class MaritimeCatalog
    {
        private static ItemAmount M(ItemType k,float n) {return new ItemAmount(k,n);}
        public static readonly ShipDefinition[] All={
            new ShipDefinition(ShipKind.FishingBoat,"Рыбацкая лодка","n01_boats",40,1.1f,70,1,2,0,1,18,new[]{M(ItemType.Plank,8),M(ItemType.Rope,2)}),
            new ShipDefinition(ShipKind.FishingCutter,"Рыбацкий катер","n02_shipbuilding",150,1.4f,140,2,4,0,3,30,new[]{M(ItemType.Plank,18),M(ItemType.Rope,4),M(ItemType.LinenCloth,3),M(ItemType.IronIngot,2)}),
            new ShipDefinition(ShipKind.FishingTrawler,"Морской рыболов","n04_deepsea",400,1.6f,230,3,5,0,5,45,new[]{M(ItemType.Plank,30),M(ItemType.Rope,7),M(ItemType.LinenCloth,6),M(ItemType.IronIngot,6)}),
            new ShipDefinition(ShipKind.Transport,"Транспортное судно","n03_navigation",600,1.2f,210,2,5,16,0,38,new[]{M(ItemType.Plank,24),M(ItemType.Rope,5),M(ItemType.LinenCloth,4),M(ItemType.IronIngot,4)}),
            new ShipDefinition(ShipKind.Merchant,"Торговое судно","n05_overseas_trade",1200,1.45f,260,3,6,4,0,48,new[]{M(ItemType.Plank,36),M(ItemType.Rope,8),M(ItemType.LinenCloth,8),M(ItemType.IronIngot,8)}),
            new ShipDefinition(ShipKind.Warship,"Боевой корабль","n06_naval_warfare",500,1.7f,420,3,8,4,0,65,new[]{M(ItemType.Plank,42),M(ItemType.Rope,10),M(ItemType.LinenCloth,8),M(ItemType.IronIngot,12),M(ItemType.Ballista,1)}),
            new ShipDefinition(ShipKind.WandererSmall,"Малый странник","",5000,1.3f,220,3,5,4,0,0,Array.Empty<ItemAmount>()),
            new ShipDefinition(ShipKind.WandererMedium,"Странник","",10000,1.15f,340,4,6,6,0,0,Array.Empty<ItemAmount>()),
            new ShipDefinition(ShipKind.WandererLarge,"Великий странник","",20000,1,500,5,8,8,0,0,Array.Empty<ItemAmount>())
        };
        public static ShipDefinition Get(ShipKind kind) {int i=(int)kind;return i>=0&&i<All.Length?All[i]:null;}
        public static bool IsForeign(ShipKind kind) {return kind>=ShipKind.WandererSmall&&kind<=ShipKind.WandererLarge;}
        public static float UsedHold(ShipData s) { if(s==null)return 0;double n=s.Food+s.Water;foreach(float v in s.Cargo)n+=v;foreach(float v in s.AgriculturalCargo)n+=v;return(float)n; }
        public static float FreeHold(ShipData s) {return s==null?0:Math.Max(0,Get(s.Kind).Capacity-UsedHold(s));}
        public static int People(ShipData s) {return s==null?0:s.CrewIds.Count+s.PassengerIds.Count+s.PrisonerIds.Count;}
        public static string PhaseName(ShipPhase p) {string[] n={"У причала","В пути","Ловит рыбу","Уходит в море","За рубежом","Возвращается","Морской бой","Захвачен","Застрял","Затонул","Ушёл"};return(int)p>=0&&(int)p<n.Length?n[(int)p]:"?";}
        public static bool IsWater(WorldState w,int cell,ShipKind kind)
        { if(w==null||cell<0||cell>=w.Cells.Length)return false;TileData t=w.Cells[cell];if(t.BuildingId!=0)return false;return t.Terrain==TerrainType.DeepWater || t.Terrain==TerrainType.ShallowWater&&(kind==ShipKind.FishingBoat||t.WaterDepth>=.35f); }
    }
}
