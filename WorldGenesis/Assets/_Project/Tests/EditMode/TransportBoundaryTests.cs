using System;
using System.Linq;
using System.Reflection;
using WorldGenesis.Simulation;
#if UNITY_5_3_OR_NEWER
using NUnit.Framework;
using UnityEngine;
#endif

namespace WorldGenesis.Tests
{
    public static class TransportBoundaryScenarios
    {
        public static Func<WorldState,WorldState> Clone;
        public static Func<WorldState,string> Snapshot;
        private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        private static object Call(SimulationEngine e,string name,params object[] args){return typeof(SimulationEngine).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(e,args);}
        private static SimulationEngine Town()
        {
            var w=new WorldState{Width=40,Height=40,Cells=new TileData[1600],Seed=97,RngState=13,NaturalWildlife=false};
            for(int i=0;i<w.Cells.Length;i++)w.Cells[i]=new TileData{Terrain=TerrainType.Grass,Height=.55f,Vegetation=.5f,Moisture=.5f,DrainageParent=-1};
            var e=new SimulationEngine(w);e.Spawn(SpeciesType.Human,20,20,4);w.Laws.Aging=w.Laws.Births=w.Laws.Warfare=false;
            var s=w.Settlements[0];s.Wood=40;s.Food=200;s.NextPolicyReviewTick=100000;
            foreach(var t in TechnologyCatalog.All)s.UnlockedTechnologies.Add(t.Id);
            foreach(var u in w.Units){u.Age=25;u.Hunger=u.Thirst=u.Fatigue=.05f;u.SocialNeed=.05f;u.DecisionTimer=10000;u.Task=UnitTask.Idle;}
            return e;
        }
        private static int River(WorldState w,int x,int y)
        {
            int i=w.Index(x,y);w.Cells[i].Terrain=TerrainType.ShallowWater;w.Cells[i].Height=.5f;w.Cells[i].River=true;
            w.Cells[i].WaterKind=WaterKind.River;w.Cells[i].WaterDepth=1.2f;w.Cells[i].CurrentStrength=.3f;w.Cells[i].ChannelWidth=1;return i;
        }
        private static void Steps(SimulationEngine e,int ticks){for(int i=0;i<ticks;i++)e.Step(.1f);}
        public static void WoodenBridgeBurnsAndInvalidatesSavedPath()
        {
            var e=Town();var w=e.World;var s=w.Settlements[0];int cell=River(w,25,20);
            w.Infrastructure[cell].BridgeLevel=1;w.Infrastructure[cell].BridgeOwnerId=s.Id;w.Infrastructure[cell].BridgeAxis=0;
            int revision=w.NavigationRevision;e.ApplyPower(PowerType.Fire,25,20,0);Require(w.Cells[cell].Fire>0,"Bridge never ignited");
            Steps(e,80);
            Require(w.Infrastructure[cell].BridgeLevel==0,"Wooden bridge survives the complete fire and remains traversable");
            Require(w.NavigationRevision>revision&&!w.IsWalkable(25,20),"Burned span did not invalidate navigation");Require(w.Validate(out string error),error);
        }
        public static void IsolatedBankCannotReserveAllTownConstructionForever()
        {
            var e=Town();var w=e.World;var s=w.Settlements[0];River(w,25,20);
            Require(e.TryPlanBridge(s.Id,24,20,0,false),"Fixture bridge plan rejected");var p=w.TransportProjects[0];p.Delivered=2;s.Wood-=2;
            for(int y=0;y<w.Height;y++)e.Paint(TerrainType.DeepWater,23,y,0);
            foreach(var u in w.Units)u.DecisionTimer=0;
            Require(!(bool)Call(e,"SetTarget",w.Units[0],24,20),"Barrier did not isolate the site");
            Steps(e,900);
            Require(w.TransportProjects.Count==0,"Unreachable site permanently blocks every automatic house/field through HasTransportProject");
            Require(w.GroundStocks.Any(g=>g.Item==ItemType.Plank&&Math.Abs(g.Amount-2)<.001f&&g.X==24.5f&&g.Y==20.5f),"Cancellation lost or teleported already delivered stock");Require(w.Validate(out string error),error);
        }
        public static void EditedOceanSpanCancelsBeforeItCanBeConstructed()
        {
            var e=Town();var w=e.World;var s=w.Settlements[0];int cell=River(w,25,20);
            Require(e.TryPlanBridge(s.Id,24,20,0,false),"Fixture bridge plan rejected");var p=w.TransportProjects[0];p.Delivered=2;s.Wood-=2;
            var worker=w.Units[0];Call(e,"ChooseTransportWork",worker,s);worker.X=24.5f;worker.Y=20.5f;worker.Path.Clear();worker.PathCursor=0;
            p.Delivered=p.Required;s.Wood-=2;p.WorkSeconds=4; // Final work tick is poised to commit immediately after the edit.
            e.Paint(TerrainType.DeepWater,25,20,0);e.Step(.1f);
            Require(w.Cells[cell].WaterKind==WaterKind.Ocean&&w.Cells[cell].WaterDepth>2.5f,"Fixture did not change bridge eligibility");
            Require(w.TransportProjects.Count==0,"Project ignores its own ocean/depth/current requirements after a terrain edit");
            Require(w.GroundStocks.Any(g=>g.Item==ItemType.Plank&&Math.Abs(g.Amount-4)<.001f),"Invalid-span cancellation lost delivered wood");Require(w.Infrastructure[cell].BridgeLevel==0,"Final work tick committed an invalid span before cleanup");Require(w.Validate(out string error),error);
        }
        public static void InvalidProjectGeometryAndWorkerBacklinksAreRejected()
        {
            var e=Town();var w=e.World;var s=w.Settlements[0];River(w,25,20);Require(e.TryPlanBridge(s.Id,24,20,0,false),"Fixture bridge plan rejected");
            WorldState bad=Clone(w);bad.TransportProjects[0].Cells=new[]{bad.Index(25,20),bad.Index(25,20)};
            Require(!bad.Validate(out _),"Save accepts duplicate bridge cells and an impossible material bill");
            bad=Clone(w);bad.TransportProjects[0].WorkerId=bad.Units[0].Id;bad.Units[1].TransportProjectId=bad.TransportProjects[0].Id;
            Require(!bad.Validate(out _),"Save accepts a project worker with no reciprocal assignment and another worker claiming it");
        }
        public static void WorkerDeathPreservesDeliveredStockAndAllowsReplacement()
        {
            var e=Town();var w=e.World;var s=w.Settlements[0];River(w,25,20);Require(e.TryPlanBridge(s.Id,24,20,0,false),"Fixture bridge plan rejected");
            var p=w.TransportProjects[0];p.Delivered=2;s.Wood-=2;var dead=w.Units[0];Call(e,"ChooseTransportWork",dead,s);dead.Health=0;e.Step(.1f);
            Require(p.Delivered==2&&p.WorkerId==0&&w.GetUnit(dead.Id)==null,"Dead worker retained claim or deleted site inventory");
            var successor=w.Units.First();Require((bool)Call(e,"ChooseTransportWork",successor,s),"No living replacement can take over");
            var loaded=new SimulationEngine(Clone(w));for(int i=0;i<200;i++){e.Step(.1f);loaded.Step(.1f);}
            Require(Snapshot(w)==Snapshot(loaded.World),"Replacement construction diverges after save");Require(w.Validate(out string error),error);
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class TransportBoundaryTests
    {
        [SetUp]public void Serializer(){TransportBoundaryScenarios.Clone=w=>JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w));TransportBoundaryScenarios.Snapshot=w=>JsonUtility.ToJson(w);}
        [Test]public void BridgeFire(){TransportBoundaryScenarios.WoodenBridgeBurnsAndInvalidatesSavedPath();}
        [Test]public void UnreachableConstructionTimeout(){TransportBoundaryScenarios.IsolatedBankCannotReserveAllTownConstructionForever();}
        [Test]public void ChangedSpanEligibility(){TransportBoundaryScenarios.EditedOceanSpanCancelsBeforeItCanBeConstructed();}
        [Test]public void MalformedProjectSave(){TransportBoundaryScenarios.InvalidProjectGeometryAndWorkerBacklinksAreRejected();}
        [Test]public void WorkerDeathAndSave(){TransportBoundaryScenarios.WorkerDeathPreservesDeliveredStockAndAllowsReplacement();}
    }
#endif
}
