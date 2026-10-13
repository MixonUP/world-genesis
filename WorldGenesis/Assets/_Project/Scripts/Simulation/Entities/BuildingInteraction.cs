using System;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private bool AtBuilding(UnitData u, BuildingData b)
        { return b != null && (!BuildingAccess.IsEnclosed(b.Type) || World.BuildingAccessVersion == 0 ? DistanceSquared(u.X,u.Y,b.X+.5f,b.Y+.5f) < 2 : u.InsideBuildingId == b.Id); }
        private int ChooseBuildingEntrance(int x, int y)
        {
            // The south door is canonical; constrained plots use a stable clockwise fallback.
            int[] dx = { 0, 1, 0, -1 }, dy = { -1, 0, 1, 0 };
            int fallback = -1;
            for (int n = 0; n < 4; n++)
            {
                int px = x + dx[n], py = y + dy[n]; if (!World.InBounds(px,py)) continue;
                int cell=World.Index(px,py); if (fallback < 0) fallback=cell;
                if (World.IsWalkable(px,py) && World.Cells[cell].Fire <= .1f) return cell;
            }
            return fallback;
        }
        private void InitializeBuildingAccess(BuildingData b)
        { b.EntranceCell=BuildingAccess.IsEnclosed(b.Type) ? ChooseBuildingEntrance(b.X,b.Y) : -1; }
        private void MigrateBuildingAccess()
        {
            if (World.BuildingAccessVersion == 1) return;
            World.BuildingAccessVersion=1;
            foreach (BuildingData b in World.Buildings) InitializeBuildingAccess(b);
            World.NavigationRevision++;
            foreach (UnitData u in World.Units)
            {
                int goal=u.TargetCell;
                BuildingData b=World.GetBuilding(World.Cells[World.Index((int)u.X,(int)u.Y)].BuildingId);
                ClearRoute(u);
                if (b != null && BuildingAccess.IsEnclosed(b.Type))
                {
                    if (BuildingAccess.MayEnter(World,u,b) && BuildingAccess.Occupants(World,b) < BuildingAccess.Capacity(b))
                    { u.InsideBuildingId=b.Id; u.X=b.X+.5f; u.Y=b.Y+.5f; }
                    else
                    {
                        // Legacy people could stand on every building. Repair that old overlap
                        // deterministically; this relocation is migration only, never gameplay travel.
                        int exit=FindBuildingExit(b,true);
                        if (exit < 0) for(int i=0;i<World.Cells.Length;i++) if(World.IsWalkable(i%World.Width,i/World.Width)) { exit=i; break; }
                        if (exit>=0) { u.X=exit%World.Width+.5f;u.Y=exit/World.Width+.5f; }
                    }
                }
                if(goal>=0 && goal<World.Cells.Length) SetTarget(u,goal%World.Width,goal/World.Width);
            }
        }
        private int FindBuildingExit(BuildingData b, bool emergency)
        {
            if (BuildingAccess.DoorAvailable(World,b)) return b.EntranceCell;
            if (!emergency) return -1;
            for(int d=0;d<4;d++)
            {
                int x=b.X+Dx[d],y=b.Y+Dy[d];
                if(World.IsWalkable(x,y) && World.Cells[World.Index(x,y)].Fire<=.1f) return World.Index(x,y);
            }
            return -1;
        }
        private void PlaceOutsideBuilding(UnitData u, int cell)
        { u.InsideBuildingId=u.EnteringBuildingId=0;u.BuildingInteractionTimer=0;u.X=cell%World.Width+.5f;u.Y=cell/World.Width+.5f;ClearRoute(u); }
        public bool TryExitBuilding(int unitId)
        {
            UnitData u=World.GetUnit(unitId); if(u==null || u.Health<=0 || u.InsideBuildingId==0) return false;
            BuildingData b=World.GetBuilding(u.InsideBuildingId); if(b==null) return false;
            int cell=FindBuildingExit(b,true); if(cell<0)return false;
            PlaceOutsideBuilding(u,cell);ChangeTask(u,UnitTask.Idle);u.DecisionTimer=1;World.Revision++;return true;
        }
        public bool TryEnterBuilding(int unitId,int buildingId)
        {
            UnitData u=World.GetUnit(unitId);BuildingData b=World.GetBuilding(buildingId);
            if(!BuildingAccess.MayEnter(World,u,b) || !BuildingAccess.IsEnclosed(b.Type) || !BuildingAccess.IsUsable(World,b) || !SetTarget(u,b.X,b.Y))return false;
            ChangeTask(u,UnitTask.VisitBuilding);u.TargetBuildingId=b.Id;u.WorkTimer=4;u.DecisionTimer=1;return true;
        }
        private static bool IndoorTask(UnitTask task)
        {
            return task==UnitTask.Produce || task==UnitTask.Process || task==UnitTask.Rest || task==UnitTask.Learn || task==UnitTask.Haul || task==UnitTask.Return || task==UnitTask.Brew || task==UnitTask.CraftEquipment || task==UnitTask.EquipGoods || task==UnitTask.InstallFurniture || task==UnitTask.Caravan || task==UnitTask.Drink || task==UnitTask.VisitBuilding;
        }
        private bool HandleIndoorSafety(UnitData u,float dt)
        {
            if(u.InsideBuildingId==0)return false;
            BuildingData b=World.GetBuilding(u.InsideBuildingId);
            if(b==null){u.InsideBuildingId=0;return false;}
            int cell=World.Index(b.X,b.Y);
            bool flooded=!World.IsTerrainWalkable(b.X,b.Y);
            bool unsafeSite=b.Health<=0 || flooded || World.Cells[cell].Fire>.1f || !BuildingAccess.MayEnter(World,u,b);
            if(unsafeSite)
            {
                int exit=FindBuildingExit(b,true);
                ChangeTask(u,UnitTask.Flee);u.DecisionTimer=0;
                if(exit>=0){PlaceOutsideBuilding(u,exit);return false;}
                if(flooded)u.Health=Math.Max(0,u.Health-dt*18);
                return true;
            }
            if(!IndoorTask(u.Task))
            {
                int exit=FindBuildingExit(b,true);
                if(exit>=0){PlaceOutsideBuilding(u,exit);return false;}
                // A blocked door is a real trapped resident; needs and fire still tick.
                return true;
            }
            return false;
        }
        private bool PerformBuildingEntry(UnitData u,float dt)
        {
            if(u.EnteringBuildingId==0)return false;
            BuildingData b=World.GetBuilding(u.EnteringBuildingId);
            if(!IndoorTask(u.Task) || !BuildingAccess.MayEnter(World,u,b) || !BuildingAccess.IsUsable(World,b))
            { u.EnteringBuildingId=0;u.BuildingInteractionTimer=0;ChangeTask(u,UnitTask.Idle);u.DecisionTimer=0;return true; }
            int door=b.EntranceCell;
            if(DistanceSquared(u.X,u.Y,door%World.Width+.5f,door/World.Width+.5f)>.0225f)
            { if(!SetTarget(u,b.X,b.Y)){ChangeTask(u,UnitTask.Idle);u.DecisionTimer=0;} return true; }
            if(BuildingAccess.Occupants(World,b)>=BuildingAccess.Capacity(b))return true;
            u.BuildingInteractionTimer=Math.Min(BuildingAccess.EntrySeconds,u.BuildingInteractionTimer+dt);
            if(u.BuildingInteractionTimer+.00001f<BuildingAccess.EntrySeconds)return true;
            u.InsideBuildingId=b.Id;u.EnteringBuildingId=0;u.BuildingInteractionTimer=0;u.X=b.X+.5f;u.Y=b.Y+.5f;ClearRoute(u);World.Revision++;
            return false;
        }
        private bool PerformBuildingVisit(UnitData u,float dt)
        {
            if(u.Task!=UnitTask.VisitBuilding)return false;
            if(u.InsideBuildingId!=u.TargetBuildingId){ChangeTask(u,UnitTask.Idle);return true;}
            BuildingData b=World.GetBuilding(u.InsideBuildingId);
            if(u.WorkTimer>=4 && b != null && b.Type==BuildingType.Brewery && u.Age>=16 && u.SocialNeed>.5f && u.Intoxication<.12f && b.AgriculturalStock[(int)AgriculturalProduct.Ale]>=1)
            { b.AgriculturalStock[(int)AgriculturalProduct.Ale]--;u.Intoxication=Math.Min(1,u.Intoxication+.35f);u.AlcoholHabit=Math.Min(1,u.AlcoholHabit+.08f);u.SocialNeed=Math.Max(0,u.SocialNeed-.4f);u.EventStress=Math.Max(0,u.EventStress-.08f); }
            u.WorkTimer=Math.Max(0,u.WorkTimer-dt);
            if(u.WorkTimer<=0)TryExitBuilding(u.Id);
            return true;
        }
        private void EvacuateRemovedBuilding(BuildingData b)
        {
            int exit=FindBuildingExit(b,true);
            if(exit<0)exit=World.Index(b.X,b.Y); // Wall is being removed in this same command.
            foreach(UnitData u in World.Units)
            {
                if(u.InsideBuildingId==b.Id) { PlaceOutsideBuilding(u,exit);ChangeTask(u,UnitTask.Idle);u.DecisionTimer=0; }
                else if(u.EnteringBuildingId==b.Id) { u.EnteringBuildingId=0;u.BuildingInteractionTimer=0;ClearRoute(u);ChangeTask(u,UnitTask.Idle);u.DecisionTimer=0; }
            }
        }
        private void ReleaseIndoorDeath(UnitData u)
        {
            BuildingData b=World.GetBuilding(u.InsideBuildingId);
            if(b!=null){int exit=FindBuildingExit(b,true);if(exit>=0)PlaceOutsideBuilding(u,exit);}
            u.InsideBuildingId=u.EnteringBuildingId=0;u.BuildingInteractionTimer=0;
        }
    }
}
