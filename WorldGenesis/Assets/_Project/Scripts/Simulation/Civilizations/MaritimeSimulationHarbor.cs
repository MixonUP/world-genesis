using System;
using System.Collections.Generic;
namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private static bool MaritimeCoin(ItemType item)
        {return item==ItemType.CopperCoin||item==ItemType.SilverCoin||item==ItemType.GoldCoin;}
        private static float WholeMaritimeUnits(float amount){return (float)Math.Floor(Math.Max(0,amount));}
        private bool MaritimeDryCell(int cell)
        {
            if(cell<0||cell>=World.Cells.Length||!World.IsWalkable(cell%World.Width,cell/World.Width)||World.Cells[cell].Fire>.1f)return false;
            if(World.Infrastructure[cell].BridgeLevel>0)return true;
            TileData t=World.Cells[cell];return t.Terrain!=TerrainType.DeepWater&&t.Terrain!=TerrainType.ShallowWater&&t.WaterDepth<=.00001f;
        }
        public bool MaritimeLandPath(int fromCell,int toCell,int maxSteps=6)
        {
            if(maxSteps<0||maxSteps>64)return false;
            fromCell=World.BuildingNavigationCell(fromCell);toCell=World.BuildingNavigationCell(toCell);
            if(!MaritimeDryCell(fromCell)||!MaritimeDryCell(toCell))return false;
            if(fromCell==toCell)return true;
            // Bounded dry BFS: an earlier shorter ford must not hide a valid dry detour.
            var cells=new List<int>{fromCell};var depths=new List<int>{0};var visited=new HashSet<int>{fromCell};
            for(int q=0;q<cells.Count;q++)
            {
                int c=cells[q],depth=depths[q];if(depth>=maxSteps)continue;
                for(int d=0;d<4;d++)
                {
                    int x=c%World.Width+Dx[d],y=c/World.Width+Dy[d];if(!World.InBounds(x,y))continue;int n=World.Index(x,y);
                    if(visited.Contains(n)||!MaritimeDryCell(n)||!World.CanStep(c,n))continue;
                    if(n==toCell)return true;visited.Add(n);cells.Add(n);depths.Add(depth+1);
                }
            }
            return false;
        }
        public bool HasHarborAuthority(int portId)
        {
            BuildingData port=World.GetBuilding(portId);if(!UsablePort(port))return false;
            foreach(BuildingData b in World.Buildings)
                if(b.Type==BuildingType.PortAuthority&&b.SettlementId==port.SettlementId&&BuildingAccess.IsUsable(World,b)
                    &&MaritimeLandPath(World.Index(port.X,port.Y),World.Index(b.X,b.Y),12))return true;
            return false;
        }
        public float HarborStock(int portId,ItemType item)
        {if((int)item<=0||(int)item>=ResourceCatalog.ItemCount)return 0;float n=0;foreach(BuildingData b in GetHarborWarehouses(portId))n+=MaritimeCoin(item)?WholeMaritimeUnits(AvailableForHaul(b,item)):AvailableForHaul(b,item);return n;}
        public float HarborAgricultureStock(int portId,AgriculturalProduct product)
        {if((int)product<=0||(int)product>=FishingCatalog.AgriculturalProductCount)return 0;float n=0;foreach(BuildingData b in GetHarborWarehouses(portId))n+=Math.Max(0,b.AgriculturalStock[(int)product]-IndustryAgricultureReserved(b,product));return n;}
        public float HarborProvisionStock(int portId,MaritimeProvision provision)
        {float n=0;foreach(BuildingData b in GetHarborWarehouses(portId))n+=provision==MaritimeProvision.Food?b.PortFoodStored:provision==MaritimeProvision.Water?b.WaterStored:0;return n;}
        public bool TryTransferHarborCargo(int shipId,ItemType item,float quantity,bool toShip)
        {
            ShipData s=World.GetShip(shipId);if(s==null||s.Phase!=ShipPhase.Docked||!PositiveFinite(quantity)||(int)item<=0||(int)item>=ResourceCatalog.ItemCount)return false;
            List<BuildingData> parts=GetHarborWarehouses(s.DockedPortId);if(parts.Count==0||!ShipAtHarbor(s,parts[0])||s.OwnerSettlementId!=parts[0].SettlementId)return false;
            bool coin=MaritimeCoin(item);if(coin&&quantity!=WholeMaritimeUnits(quantity))return false;
            float available=0;foreach(BuildingData b in parts){float amount=toShip?AvailableForHaul(b,item):StorageCapacity.FreeMaterials(b);available+=coin?WholeMaritimeUnits(amount):amount;}
            if(available+.00001f<quantity||(toShip?MaritimeCatalog.FreeHold(s):s.Cargo[(int)item])+.00001f<quantity)return false;
            float left=quantity;foreach(BuildingData b in parts){float amount=toShip?AvailableForHaul(b,item):StorageCapacity.FreeMaterials(b);float take=Math.Min(left,coin?WholeMaritimeUnits(amount):amount);if(take<=.00001f)continue;if(toShip)b.Inventory[(int)item]-=take;else b.Inventory[(int)item]+=take;left-=take;if(left<=.00001f)break;}
            s.Cargo[(int)item]+=toShip?quantity:-quantity;World.ResourceRevision++;World.Revision++;return true;
        }
        public bool TryTransferHarborAgriculture(int shipId,AgriculturalProduct product,float quantity,bool toShip)
        {
            ShipData s=World.GetShip(shipId);if(s==null||s.Phase!=ShipPhase.Docked||!PositiveFinite(quantity)||(int)product<=0||(int)product>=FishingCatalog.AgriculturalProductCount)return false;
            List<BuildingData> parts=GetHarborWarehouses(s.DockedPortId);if(parts.Count==0||!ShipAtHarbor(s,parts[0])||s.OwnerSettlementId!=parts[0].SettlementId)return false;
            float available=0;foreach(BuildingData b in parts)available+=toShip?Math.Max(0,b.AgriculturalStock[(int)product]-IndustryAgricultureReserved(b,product)):StorageCapacity.FreeAgriculture(b,product);
            if(available+.00001f<quantity||(toShip?MaritimeCatalog.FreeHold(s):s.AgriculturalCargo[(int)product])+.00001f<quantity)return false;
            float left=quantity;foreach(BuildingData b in parts){float take=Math.Min(left,toShip?Math.Max(0,b.AgriculturalStock[(int)product]-IndustryAgricultureReserved(b,product)):StorageCapacity.FreeAgriculture(b,product));if(take<=.00001f)continue;b.AgriculturalStock[(int)product]+=toShip?-take:take;left-=take;if(left<=.00001f)break;}
            s.AgriculturalCargo[(int)product]+=toShip?quantity:-quantity;World.ResourceRevision++;World.Revision++;return true;
        }
        public bool TryTransferHarborProvisions(int shipId,float food,float water,bool toShip)
        {
            ShipData s=World.GetShip(shipId);if(s==null||s.Phase!=ShipPhase.Docked||!NonnegativeFinite(food)||!NonnegativeFinite(water)||food+water<=0)return false;
            List<BuildingData> parts=GetHarborWarehouses(s.DockedPortId);if(parts.Count==0||!ShipAtHarbor(s,parts[0])||s.OwnerSettlementId!=parts[0].SettlementId)return false;
            float free=0,availableFood=0,availableWater=0;foreach(BuildingData b in parts){free+=StorageCapacity.FreeMaterials(b);availableFood+=b.PortFoodStored;availableWater+=b.WaterStored;}
            if(toShip?(availableFood+.00001f<food||availableWater+.00001f<water||MaritimeCatalog.FreeHold(s)+.00001f<food+water):(s.Food+.00001f<food||s.Water+.00001f<water||free+.00001f<food+water))return false;
            float leftFood=food,leftWater=water;
            foreach(BuildingData b in parts){float f=Math.Min(leftFood,toShip?b.PortFoodStored:StorageCapacity.FreeMaterials(b));b.PortFoodStored+=toShip?-f:f;leftFood-=f;float w=Math.Min(leftWater,toShip?b.WaterStored:StorageCapacity.FreeMaterials(b));b.WaterStored+=toShip?-w:w;leftWater-=w;}
            s.Food+=toShip?food:-food;s.Water+=toShip?water:-water;World.Revision++;return true;
        }
        private void CleanupMaritimeHistory()
        {
            // Completed receipts are bounded; cumulative counters and live manifests remain authoritative.
            if(World.DockOrders.Count>256)for(int i=0;i<World.DockOrders.Count&&World.DockOrders.Count>192;){DockOrderData o=World.DockOrders[i];if(o.Complete||o.Cancelled)World.DockOrders.RemoveAt(i);else i++;}
            if(World.ShipOrders.Count>128)for(int i=0;i<World.ShipOrders.Count&&World.ShipOrders.Count>96;){ShipConstructionData o=World.ShipOrders[i];if(o.Complete||o.Cancelled)World.ShipOrders.RemoveAt(i);else i++;}
            if(World.Ships.Count>96)for(int i=0;i<World.Ships.Count&&World.Ships.Count>64;)
            {
                ShipData s=World.Ships[i];bool referenced=false;foreach(ShipData other in World.Ships)if(other.TargetShipId==s.Id)referenced=true;
                if((s.Phase==ShipPhase.Departed||s.Phase==ShipPhase.Sunk)&&!referenced)
                {for(int j=World.ShipOrders.Count-1;j>=0;j--)if(World.ShipOrders[j].ShipId==s.Id&&(World.ShipOrders[j].Complete||World.ShipOrders[j].Cancelled))World.ShipOrders.RemoveAt(j);World.Ships.RemoveAt(i);}else i++;
            }
        }
    }
}
