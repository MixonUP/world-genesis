using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public enum ShipKind { FishingBoat, FishingCutter, FishingTrawler, Transport, Merchant, Warship, WandererSmall, WandererMedium, WandererLarge }
    public enum ShipPhase { Docked, Sailing, Fishing, Outbound, Abroad, Inbound, Attacking, Captured, Stranded, Sunk, Departed }
    public enum MaritimeProvision { None, Food, Water }
    public enum DockOrderKind { Supply, Crew, Passenger, TradeMaterial, TradeAgriculture }
    [Serializable] public sealed class ShipData
    {
        public int Id, OwnerSettlementId, HomePortId, DockedPortId, DestinationPortId, CaptainUnitId, TargetShipId;
        public ShipKind Kind;
        public ShipPhase Phase;
        public float X, Y, HeadingX, HeadingY = 1, Health, MaxHealth, ActionTimer, AbroadTimer, Food, Water;
        public float[] Cargo = new float[ResourceCatalog.ItemCount];
        public float[] AgriculturalCargo = new float[FishingCatalog.AgriculturalProductCount];
        public List<int> CrewIds = new List<int>(), PassengerIds = new List<int>(), PrisonerIds = new List<int>();
        public List<int> Path = new List<int>();
        public int PathCursor, RouteRevision, FishingCell = -1, OceanExitCell = -1;
        public long LastAttackTick, LastChangeTick;
        public bool AttemptCapture, Aggressor, AutoFishing;
        public string LastReason = "";
        public float[] ForeignOffer = new float[ResourceCatalog.ItemCount];
        public float[] ForeignAgricultureOffer = new float[FishingCatalog.AgriculturalProductCount];
        public int FishCaught, CompletedVoyages;
    }
    [Serializable] public sealed class ShipConstructionData
    {
        public int Id, ShipyardId, WorkerId, ShipId;
        public ShipKind Kind;
        public float Progress;
        public bool Complete, Cancelled;
        public float[] Materials = new float[ResourceCatalog.ItemCount];
        public string LastReason = "";
    }
    [Serializable] public sealed class DockOrderData
    {
        public int Id, WorkerId, BuildingId, ShipId, SourceBuildingId;
        public DockOrderKind Kind;
        public ItemType Item;
        public AgriculturalProduct Product;
        public MaritimeProvision Provision;
        public float Remaining, Progress;
        public bool Captain, Guard, ShipSells, Complete, Cancelled;
    }
    public sealed partial class BuildingData { public int BerthCell = -1, MaritimeOrderId; public float PortFoodStored; }
    public sealed partial class UnitData
    {
        public int ShipId, MaritimeOrderId;
        public long MaritimeShoreLeaveUntilTick;
        public bool IsForeignMariner;
        public ItemType NavalWeapon, NavalArmor;
    }
    public sealed partial class SettlementData { public long NextMaritimeTick; }
    public sealed partial class WorldState
    {
        public int MaritimeVersion, NextShipId = 1, NextMaritimeOrderId = 1;
        public long NextWandererTick;
        public List<ShipData> Ships = new List<ShipData>();
        public List<ShipConstructionData> ShipOrders = new List<ShipConstructionData>();
        public List<DockOrderData> DockOrders = new List<DockOrderData>();
        public float[] MaritimeImports = new float[ResourceCatalog.ItemCount], MaritimeExports = new float[ResourceCatalog.ItemCount];
        public float[] MaritimeAgricultureImports = new float[FishingCatalog.AgriculturalProductCount], MaritimeAgricultureExports = new float[FishingCatalog.AgriculturalProductCount];
        public float MaritimeFoodImported, MaritimeWaterImported, MaritimeFoodExported, MaritimeWaterExported, MaritimeFoodConsumed, MaritimeWaterConsumed, MaritimeFoodLost, MaritimeWaterLost;
        public int ForeignCrewArrived, ForeignCrewDeparted, ForeignCrewDead, ShipsBuilt, ShipsSunk, ShipsCaptured;
        public ShipData GetShip(int id) { if (Ships != null) foreach (ShipData s in Ships) if (s.Id == id) return s; return null; }
        public ShipConstructionData GetShipOrder(int id) { if (ShipOrders != null) foreach (ShipConstructionData o in ShipOrders) if (o.Id == id) return o; return null; }
        public DockOrderData GetDockOrder(int id) { if (DockOrders != null) foreach (DockOrderData o in DockOrders) if (o.Id == id) return o; return null; }
        private bool MaritimeArray(float[] a, int size) { if (a == null || a.Length != size || a[0] != 0) return false; foreach (float v in a) if (!Stock(v)) return false; return true; }
        private bool ValidateMaritime(out string error)
        {
            error = null;
            if (MaritimeVersion < 0 || MaritimeVersion > 1) { error = "Unsupported maritime version."; return false; }
            if (MaritimeVersion == 0)
            {
                if(Ships!=null&&Ships.Count!=0||ShipOrders!=null&&ShipOrders.Count!=0||DockOrders!=null&&DockOrders.Count!=0){error="Legacy world contains maritime records.";return false;}
                foreach(UnitData u in Units)if(u.ShipId!=0||u.MaritimeOrderId!=0||u.IsForeignMariner||u.NavalWeapon!=ItemType.None||u.NavalArmor!=ItemType.None){error="Legacy world contains sailors.";return false;}
                return true;
            }
            if (Ships == null || Ships.Count > 256 || ShipOrders == null || ShipOrders.Count > 512 || DockOrders == null || DockOrders.Count > 1024 || NextShipId < 1 || NextMaritimeOrderId < 1 || NextWandererTick < 0 || !MaritimeArray(MaritimeImports,ResourceCatalog.ItemCount) || !MaritimeArray(MaritimeExports,ResourceCatalog.ItemCount) || !MaritimeArray(MaritimeAgricultureImports,FishingCatalog.AgriculturalProductCount) || !MaritimeArray(MaritimeAgricultureExports,FishingCatalog.AgriculturalProductCount)) { error = "Invalid maritime registry or ledger."; return false; }
            foreach (float v in new[]{MaritimeFoodImported,MaritimeWaterImported,MaritimeFoodExported,MaritimeWaterExported,MaritimeFoodConsumed,MaritimeWaterConsumed,MaritimeFoodLost,MaritimeWaterLost}) if (!Stock(v)) { error = "Invalid maritime provision ledger."; return false; }
            if (ForeignCrewArrived < 0 || ForeignCrewDeparted < 0 || ForeignCrewDead < 0 || ShipsBuilt < 0 || ShipsSunk < 0 || ShipsCaptured < 0) { error = "Invalid maritime counters."; return false; }
            foreach(SettlementData town in Settlements)if(town.NextMaritimeTick<0||town.NextMaritimeConstructionTick<0){error="Invalid maritime planning time.";return false;}
            var shipIds = new HashSet<int>(); var people = new HashSet<int>(); var orderIds = new HashSet<int>();
            foreach (ShipData s in Ships)
            {
                ShipDefinition d = s == null ? null : MaritimeCatalog.Get(s.Kind);
                if (d == null || s.Id <= 0 || s.Id >= NextShipId || !shipIds.Add(s.Id) || (int)s.Phase < 0 || (int)s.Phase > 10 || (s.OwnerSettlementId != 0 && GetSettlement(s.OwnerSettlementId) == null) || !Finite(s.X) || !Finite(s.Y) || s.X < 0 || s.Y < 0 || s.X >= Width || s.Y >= Height || !Finite(s.HeadingX) || !Finite(s.HeadingY) || !Stock(s.Health) || !Stock(s.MaxHealth) || s.MaxHealth <= 0 || s.Health > s.MaxHealth || !Stock(s.Food) || !Stock(s.Water) || !Stock(s.ActionTimer) || !Stock(s.AbroadTimer) || !MaritimeArray(s.Cargo,ResourceCatalog.ItemCount) || !MaritimeArray(s.AgriculturalCargo,FishingCatalog.AgriculturalProductCount) || !MaritimeArray(s.ForeignOffer,ResourceCatalog.ItemCount) || !MaritimeArray(s.ForeignAgricultureOffer,FishingCatalog.AgriculturalProductCount) || MaritimeCatalog.UsedHold(s) > d.Capacity + .01f || s.CrewIds == null || s.PassengerIds == null || s.PrisonerIds == null || s.CrewIds.Count > d.MaxCrew || s.PassengerIds.Count > d.PassengerCapacity || s.PrisonerIds.Count > 64 || s.Path == null || s.Path.Count > Cells.Length || s.PathCursor < 0 || s.PathCursor > s.Path.Count || s.LastReason == null || s.LastReason.Length > 512 || s.LastAttackTick < 0 || s.LastAttackTick > Tick || s.LastChangeTick < 0 || s.LastChangeTick > Tick || s.FishingCell < -1 || s.FishingCell >= Cells.Length || s.OceanExitCell < -1 || s.OceanExitCell >= Cells.Length)
                { error = "Invalid ship state or hold."; return false; }
                int previous=-1;foreach (int c in s.Path) {if (c < 0 || c >= Cells.Length || previous>=0&&Math.Abs(previous%Width-c%Width)+Math.Abs(previous/Width-c/Width)!=1) { error = "Invalid maritime path."; return false; }previous=c;}
                if ((s.Phase == ShipPhase.Sunk || s.Phase == ShipPhase.Departed) && (s.CrewIds.Count + s.PassengerIds.Count + s.PrisonerIds.Count != 0 || s.CaptainUnitId != 0 || MaritimeCatalog.UsedHold(s) != 0)) { error = "Retired ship still has people or goods."; return false; }
                if (s.CaptainUnitId != 0 && !s.CrewIds.Contains(s.CaptainUnitId)) { error = "Captain not aboard."; return false; }
                foreach (List<int> list in new[]{s.CrewIds,s.PassengerIds,s.PrisonerIds}) foreach (int id in list)
                {
                    UnitData u = GetUnit(id);
                    if (u == null || u.Health <= 0 || u.ShipId != s.Id || !people.Add(id) || !SimulationEngine.IsCivilized(u.Species) || u.InsideBuildingId != 0 || u.EnteringBuildingId != 0 || u.Task != UnitTask.Sail || u.Path.Count != 0 || Math.Abs(u.X-s.X)>.001f || Math.Abs(u.Y-s.Y)>.001f || (list == s.CrewIds && (u.Age < 16 || u.SettlementId != s.OwnerSettlementId)) || (id == s.CaptainUnitId && u.Profession != UnitProfession.Captain)) { error = "Invalid vessel manifest or crew identity."; return false; }
                }
                if (s.DockedPortId != 0 && (GetBuilding(s.DockedPortId) == null || s.Phase != ShipPhase.Docked || GetBuilding(s.DockedPortId).Type!=BuildingType.Port || Math.Abs(s.X-(GetBuilding(s.DockedPortId).BerthCell%Width+.5f))>.001f || Math.Abs(s.Y-(GetBuilding(s.DockedPortId).BerthCell/Width+.5f))>.001f)) { error = "Invalid ship dock."; return false; }
            }
            foreach (ShipConstructionData o in ShipOrders)
            {
                if (o == null || o.Id < 1 || o.Id >= NextMaritimeOrderId || !orderIds.Add(o.Id) || MaritimeCatalog.Get(o.Kind) == null || MaritimeCatalog.IsForeign(o.Kind) || !MaritimeArray(o.Materials,ResourceCatalog.ItemCount) || !Stock(o.Progress) || o.Progress > MaritimeCatalog.Get(o.Kind).Seconds+.001f || o.LastReason == null || (!o.Complete && !o.Cancelled && GetBuilding(o.ShipyardId) == null) || (o.Complete && GetShip(o.ShipId) == null)) { error = "Invalid shipbuilding order."; return false; }
                for(int i=1;i<o.Materials.Length;i++){float allowed=0;foreach(ItemAmount m in MaritimeCatalog.Get(o.Kind).Inputs)if((int)m.Item==i)allowed=m.Amount;if(o.Materials[i]>allowed+.00001f||(o.Complete||o.Cancelled)&&o.Materials[i]>.00001f){error="Invalid shipbuilding material escrow.";return false;}}
                if (o.WorkerId != 0) { UnitData u=GetUnit(o.WorkerId); if(u==null || u.MaritimeOrderId!=o.Id || u.Task!=UnitTask.Shipbuild || u.Age<16 || u.Health<=0) {error="Invalid shipwright.";return false;} }
            }
            var workers=new HashSet<int>();foreach (DockOrderData o in DockOrders)
            {
                if (o==null || o.Id<1 || o.Id>=NextMaritimeOrderId || !orderIds.Add(o.Id) || (int)o.Kind<0 || (int)o.Kind>4 || !Stock(o.Remaining) || !Stock(o.Progress) || o.Progress>2.001f || (int)o.Item<0 || (int)o.Item>=ResourceCatalog.ItemCount || (int)o.Product<0 || (int)o.Product>=FishingCatalog.AgriculturalProductCount || (int)o.Provision<0 || (int)o.Provision>2 || (!o.Complete&&!o.Cancelled&&(GetBuilding(o.BuildingId)==null || o.WorkerId==0))) { error="Invalid dock order.";return false; }
                if(!o.Complete&&!o.Cancelled){UnitData worker=GetUnit(o.WorkerId);if(worker==null||worker.Health<=0||worker.MaritimeOrderId!=o.Id||worker.Task!=UnitTask.Dockwork||!workers.Add(worker.Id)||worker.Age<16&&o.Kind!=DockOrderKind.Passenger){error="Invalid dock worker.";return false;}}
            }
            foreach (BuildingData b in Buildings) if (!Stock(b.PortFoodStored) || b.BerthCell < -1 || b.BerthCell >= Cells.Length || b.Type==BuildingType.HarborWarehouse&&(b.Level<1||b.Level>3)) { error="Invalid harbor store or berth.";return false; }
            foreach (UnitData u in Units)
            {
                if (u.MaritimeShoreLeaveUntilTick<0 || u.ShipId < 0 || (u.ShipId!=0&&!people.Contains(u.Id)) || u.MaritimeOrderId<0 || (u.MaritimeOrderId!=0&&!orderIds.Contains(u.MaritimeOrderId)) || (u.NavalWeapon!=ItemType.None&&u.NavalWeapon!=ItemType.Sword&&u.NavalWeapon!=ItemType.Spear) || (u.NavalArmor!=ItemType.None&&u.NavalArmor!=ItemType.Armor) || (u.Age<16&&(u.NavalWeapon!=ItemType.None||u.NavalArmor!=ItemType.None))) { error="Invalid maritime citizen link or equipment.";return false; }
            }
            return true;
        }
    }
}
