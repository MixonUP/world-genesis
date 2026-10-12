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
    public static class RulerBoundaryScenarios
    {
        public static Func<WorldState, WorldState> Clone;
        public static Func<WorldState, string> Snapshot;
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        private static void Near(float expected, float actual, string message) { Require(Math.Abs(expected-actual)<.0001f, message+": "+actual+" expected "+expected); }
        private static object Call(SimulationEngine e, string method, params object[] args) => typeof(SimulationEngine).GetMethod(method, BindingFlags.Instance|BindingFlags.NonPublic).Invoke(e,args);
        private static SimulationEngine Town()
        {
            var w=new WorldState { Width=32,Height=32,Seed=9031,RngState=87,Cells=new TileData[1024],NaturalWildlife=false };
            for(int i=0;i<w.Cells.Length;i++)w.Cells[i]=new TileData{Terrain=TerrainType.Grass,Height=.55f,Moisture=.6f,Temperature=.6f,Vegetation=.5f,DrainageParent=-1};
            var e=new SimulationEngine(w);e.Spawn(SpeciesType.Human,8,8,4);w.Laws.Births=w.Laws.Aging=w.Laws.Warfare=false;
            var s=w.Settlements[0];s.Wood=8;s.Food=200;s.NextPlanningTick=s.NextPolicyReviewTick=100000;
            foreach(var u in w.Units){u.Age=25;u.DecisionTimer=10000;u.Task=UnitTask.Idle;u.Path.Clear();u.PathCursor=0;u.Hunger=u.Thirst=u.Fatigue=u.SocialNeed=.05f;}
            w.Buildings.First(b=>b.Type==BuildingType.Hall).Inventory[(int)ItemType.RawStone]=2;
            Require(w.Validate(out string error),error);return e;
        }
        private static void FillGround(WorldState w,int count)
        {
            for(int i=0;i<count;i++)w.GroundStocks.Add(new GroundStockData{Item=ItemType.Salt,Amount=.01f,X=i%w.Width+.5f,Y=i/w.Width%w.Height+.5f});
        }
        private static float Ground(WorldState w,ItemType item)=>w.GroundStocks.Where(g=>!g.IsFood&&g.Item==item).Sum(g=>g.Amount);
        public static void CatastropheCrossesFormerPileLimitWithExactRecoveryAndSaveContinuation()
        {
            var e=Town();var w=e.World;var s=w.Settlements[0];
            Require(e.TryBuildInfrastructure(s.Id,BuildingType.Well,12,12),"Actual paid well was not built");
            var b=w.Buildings.Single(candidate=>candidate.Type==BuildingType.Well);
            Near(8,b.PaidConstructionMaterials[(int)ItemType.Plank],"paid planks");Near(2,b.PaidConstructionMaterials[(int)ItemType.RawStone],"paid stone");
            FillGround(w,29999);Require(w.Validate(out string beforeError),beforeError);
            e.ApplyPower(PowerType.Lightning,12,12,1);e.ApplyPower(PowerType.Lightning,12,12,1);
            Require(w.GetBuilding(b.Id)==null&&w.GroundStocks.Count==30001,"Destruction did not create the two real recovery piles beyond the former count limit");
            Require(b.ConstructionRecoveryPercent>=10&&b.ConstructionRecoveryPercent<=50,"Recovery roll outside the recorded10..50% contract");
            Near(8*b.ConstructionRecoveryPercent*.01f,Ground(w,ItemType.Plank),"actual recovered plank share");
            Near(2*b.ConstructionRecoveryPercent*.01f,Ground(w,ItemType.RawStone),"actual recovered stone share");
            Require(w.Validate(out string error),error);string once=Snapshot(w);Call(e,"SalvageConstructionMaterials",b);Require(Snapshot(w)==once,"Repeated destruction minted a second recovery");
            var loaded=new SimulationEngine(Clone(w));for(int i=0;i<100;i++){e.Step(.1f);loaded.Step(.1f);}
            Require(Snapshot(w)==Snapshot(loaded.World),"Large physical salvage ledger diverged after loading");Require(w.Validate(out error),error);
        }
        public static void LargerLedgerStillRejectsNonFiniteNegativeAndUnknownOwnerRecords()
        {
            var e=Town();var w=e.World;FillGround(w,30001);Require(w.Validate(out string error),error);
            var pile=w.GroundStocks[30000];pile.Amount=float.NaN;Require(!w.Validate(out _),"NaN was admitted while removing the arbitrary count limit");
            pile.Amount=-1;Require(!w.Validate(out _),"Negative amount was admitted");pile.Amount=.1f;pile.OwnerId=w.NextId+50;Require(!w.Validate(out _),"Unknown private owner was admitted");
            pile.OwnerId=0;Require(w.Validate(out error),error);
        }
        public static void NullLegacyInvoiceRecordsOnlyActualUpgradeBeforeRecovery()
        {
            var e=Town();var w=e.World;var s=w.Settlements[0];foreach(var tech in TechnologyCatalog.All)s.UnlockedTechnologies.Add(tech.Id);
            Require(e.TryBuildInfrastructure(s.Id,BuildingType.School,12,12),"School fixture failed");var b=w.Buildings.Single(candidate=>candidate.Type==BuildingType.School);
            // An authentic old file has no historical payment array; only a later paid upgrade is knowable.
            b.ConstructionLedgerVersion=0;b.PaidConstructionMaterials=null;b.ConstructionRecoveryPercent=0;s.Wood=6;
            Require(w.Validate(out string error),error);Require(e.TryUpgradeBuilding(b.Id),"Old null invoice cannot upgrade");
            Near(6,b.PaidConstructionMaterials[(int)ItemType.Plank],"Unknown original cost entered the invoice");Near(0,s.Wood,"upgrade debit");
            b.Health=0;Call(e,"DestroyInvalidBuildings");Near(6*b.ConstructionRecoveryPercent*.01f,Ground(w,ItemType.Plank),"legacy upgrade recovery");Require(w.Validate(out error),error);
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class RulerBoundaryTests
    {
        [SetUp] public void Serialization(){RulerBoundaryScenarios.Clone=w=>JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w));RulerBoundaryScenarios.Snapshot=w=>JsonUtility.ToJson(w);}
        [Test] public void CatastropheCrossesFormerPileLimit(){RulerBoundaryScenarios.CatastropheCrossesFormerPileLimitWithExactRecoveryAndSaveContinuation();}
        [Test] public void LargeLedgerStillValidatesEveryRecord(){RulerBoundaryScenarios.LargerLedgerStillRejectsNonFiniteNegativeAndUnknownOwnerRecords();}
        [Test] public void NullLegacyInvoiceOnlyRecordsUpgrade(){RulerBoundaryScenarios.NullLegacyInvoiceRecordsOnlyActualUpgradeBeforeRecovery();}
    }
#endif
}
