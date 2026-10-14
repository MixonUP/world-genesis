using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using WorldGenesis.Simulation;
#if UNITY_5_3_OR_NEWER
using NUnit.Framework;
using UnityEngine;
#endif
namespace WorldGenesis.Tests
{
 public static class ConquestScenarios
 {
  public static Func<WorldState,WorldState> Clone;
  public static Func<WorldState,string> Snapshot;
  static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
  static object Call(SimulationEngine e,string name,params object[] args){var m=typeof(SimulationEngine).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic);Need(m!=null,"Missing "+name);return m.Invoke(e,args);}
  static void Valid(WorldState w){Need(w.Validate(out string error),error);}
  static SimulationEngine Fixture(bool technology=true)
  {
   var w=new WorldState{Seed=10010,RngState=10010,Width=64,Height=64,Cells=new TileData[4096],NaturalWildlife=false};
   for(int i=0;i<w.Cells.Length;i++)w.Cells[i]=new TileData{Terrain=TerrainType.Grass,Height=.55f,RouteHeight=.55f,Moisture=.6f,Temperature=.5f,DrainageParent=-1};
   var e=new SimulationEngine(w);e.Spawn(SpeciesType.Human,10,12,14);e.Spawn(SpeciesType.Orc,48,12,8);Need(w.Settlements.Count==2,"Fixture towns");
   w.Laws.Aging=w.Laws.Births=w.Laws.FireSpread=false;w.Laws.Warfare=true;w.Weather.Automatic=false;w.NextWandererTick=long.MaxValue/2;
   foreach(var s in w.Settlements){s.NextExpansionTick=s.NextColonizationTick=s.NextWarPartyTick=long.MaxValue/2;s.Food=50;}
   w.Settlements[0].NextWarPartyTick=0;
   foreach(var u in w.Units){u.Age=25;u.Hunger=u.Thirst=u.Fatigue=u.SocialNeed=0;u.Task=UnitTask.Idle;u.DecisionTimer=10000;u.Path.Clear();u.PathCursor=0;}
   if(technology)foreach(var t in TechnologyCatalog.All)w.Settlements[0].UnlockedTechnologies.Add(t.Id);
   e.SetDiplomacy(w.Settlements[0].Id,w.Settlements[1].Id,true);
   Valid(w);return e;
  }
  static BuildingData Armory(SimulationEngine e,int count=6){var b=(BuildingData)Call(e,"AddBuilding",e.World.Settlements[0],16,12,BuildingType.Smithy);Need(b!=null,"Fixture armory");b.Inventory[(int)ItemType.Spear]=count;return b;}
  static List<UnitData> Party(SimulationEngine e){Need(e.TryStartWarParty(e.World.Settlements[0].Id,e.World.Settlements[1].Id),"Party rejected");return e.World.Units.Where(u=>u.WarTargetSettlementId!=0).ToList();}
  static float Weapons(WorldState w){return w.Buildings.Sum(b=>b.Inventory[(int)ItemType.Spear]+b.Inventory[(int)ItemType.Sword])+w.Units.Count(u=>u.WarWeapon!=ItemType.None)+w.GroundStocks.Where(g=>g.Item==ItemType.Spear||g.Item==ItemType.Sword).Sum(g=>g.Amount);}
  static void Workers(SimulationEngine e,IEnumerable<UnitData> units,int ticks)
  {for(int i=0;i<ticks;i++){e.World.Tick++;e.World.SimulatedSeconds+=.1f;Call(e,"BuildSpatialIndex");foreach(var u in units)Call(e,"UpdateUnit",u,.1f);if(i%10==0)Valid(e.World);}}
  static List<UnitData> Occupiers(SimulationEngine e,int count=3)
  {
   var a=Armory(e);var party=Party(e).Take(count).ToList();var target=e.World.Settlements[1];
   foreach(var u in e.World.Units.Where(u=>u.SettlementId==target.Id)){u.X=48.5f;u.Y=45.5f;}
   foreach(var u in party){a.Inventory[(int)ItemType.Spear]--;u.WarWeapon=ItemType.Spear;u.X=target.X+.5f;u.Y=target.Y+.5f;u.Path.Clear();u.PathCursor=0;u.Task=UnitTask.WarMarch;u.DecisionTimer=10000;}
   return party;
  }
  public static void PeaceTechnologyAndRealEquipmentGateParty()
  {
   var e=Fixture(false);var a=Armory(e);var s=e.World.Settlements;
   Need(!e.TryStartWarParty(s[0].Id,s[1].Id),"Technology bypass");foreach(var t in TechnologyCatalog.All)s[0].UnlockedTechnologies.Add(t.Id);
   a.Inventory[(int)ItemType.Spear]=2;Need(!e.TryStartWarParty(s[0].Id,s[1].Id),"Missing third weapon bypass");a.Inventory[(int)ItemType.Spear]=6;
   e.SetDiplomacy(s[0].Id,s[1].Id,false);Need(!e.TryStartWarParty(s[0].Id,s[1].Id),"Peace bypass");e.SetDiplomacy(s[0].Id,s[1].Id,true);Party(e);Need(Weapons(e.World)==6,"Recruitment minted weapons");Need(e.World.Units.All(u=>u.WarWeapon==ItemType.None),"Remote equip");Valid(e.World);
  }
  public static void ChildrenRulerAndOccupiedJobsAreNotDrafted()
  {
   var e=Fixture();Armory(e);var home=e.World.Settlements[0];var adults=e.World.Units.Where(u=>u.SettlementId==home.Id&&u.Id!=home.RulerId).ToList();
   adults[0].Age=12;adults[1].Hunger=.8f;adults[2].CarriedFood=1;var p=Party(e);
   Need(p.All(u=>u.Id!=home.RulerId&&u!=adults[0]&&u!=adults[1]&&u!=adults[2]),"Unsafe draft");Need(p.Count>=3&&home.Population-p.Count>=8,"Town depopulated");
  }
  public static void RealDoorApproachPicksAndReturnsExactlyOneWeapon()
  {
   var e=Fixture();var a=Armory(e);var party=Party(e);var u=party[0];float initialX=u.X;
   for(int i=0;i<700&&u.WarWeapon==ItemType.None;i++)Workers(e,new[]{u},1);
   Need(u.WarWeapon==ItemType.Spear&&u.InsideBuildingId==a.Id,"No physical indoor pickup");Need(u.X!=initialX,"Remote pickup");Need(a.Inventory[(int)ItemType.Spear]==5&&Weapons(e.World)==6,"Weapon debit");
   e.SetDiplomacy(e.World.Settlements[0].Id,e.World.Settlements[1].Id,false);u.DecisionTimer=0;
   for(int i=0;i<700&&u.WarWeapon!=ItemType.None;i++)Workers(e,new[]{u},1);
   Need(u.WarWeapon==ItemType.None&&u.WarTargetSettlementId==0,"Peace return did not finish");Need(a.Inventory[(int)ItemType.Spear]==6&&Weapons(e.World)==6,"Return duplication");Valid(e.World);
  }
  public static void FullArmoryReturnDropsPhysicalWeaponWithoutOverflow()
  {
   var e=Fixture();var a=Armory(e);var u=Party(e)[0];a.Inventory[(int)ItemType.Spear]--;u.WarWeapon=ItemType.Spear;u.WarReturning=true;u.InsideBuildingId=a.Id;u.X=a.X+.5f;u.Y=a.Y+.5f;
   a.Inventory[(int)ItemType.RawStone]=StorageCapacity.Capacity(a)-StorageCapacity.MaterialUsed(a);float total=Weapons(e.World);
   Call(e,"PerformWarActivity",u,e.World.Settlements[0]);Need(StorageCapacity.Overflow(a)==0,"Return overflow");Need(Weapons(e.World)==total&&u.WarWeapon==ItemType.None,"Lost returned weapon");Need(e.World.GroundStocks.Any(g=>g.Item==ItemType.Spear&&g.X==u.X&&g.Y==u.Y),"No physical full-store fallback");Valid(e.World);
  }
  public static void DeathDropsEquipmentExactlyOnce()
  {
   var e=Fixture();Armory(e);var u=Party(e)[0];var a=e.World.GetBuilding(u.WarArmoryId);a.Inventory[(int)ItemType.Spear]--;u.WarWeapon=ItemType.Spear;float total=Weapons(e.World);u.Health=0;Call(e,"RemoveDead");Call(e,"RemoveDead");
   Need(e.World.GetUnit(u.Id)==null&&Weapons(e.World)==total,"Death lost or duplicated weapon");Valid(e.World);
  }
  public static void OccupationRequiresThreeArmedAndTwentySeconds()
  {
   var e=Fixture();var p=Occupiers(e);var target=e.World.Settlements[1];Call(e,"UpdateConquest",19f);Need(target.SovereignSettlementId==0&&target.OccupationSeconds==19,"Early capture");
   p[2].WarReturning=true;Call(e,"UpdateConquest",1f);Need(target.OccupationSeconds==17,"Understrength occupation continued");p[2].WarReturning=false;Call(e,"UpdateConquest",3f);Need(target.SovereignSettlementId==e.World.Settlements[0].Id&&target.ConquestCount==1,"Capture not completed");Valid(e.World);
  }
  public static void ALocalDefenderBlocksCapture()
  {
   var e=Fixture();Occupiers(e);var target=e.World.Settlements[1];var defender=e.World.Units.First(u=>u.SettlementId==target.Id);defender.X=target.X+.5f;defender.Y=target.Y+.5f;Call(e,"UpdateConquest",20f);Need(target.SovereignSettlementId==0&&target.OccupationSeconds==0,"Defender ignored");
  }
  public static void CaptureKeepsPeopleBuildingsInventoriesAndRealmPeace()
  {
   var e=Fixture();Occupiers(e);var target=e.World.Settlements[1];int people=e.World.Units.Count,buildings=e.World.Buildings.Count;var townIds=e.World.Units.Select(u=>u.SettlementId).ToArray();float weapons=Weapons(e.World);var hall=e.World.Buildings.First(b=>b.SettlementId==target.Id&&b.Type==BuildingType.Hall);hall.Inventory[(int)ItemType.IronOre]=7;
   Call(e,"UpdateConquest",20f);Need(e.World.Units.Count==people&&e.World.Buildings.Count==buildings,"Capture destroyed settlement");Need(townIds.SequenceEqual(e.World.Units.Select(u=>u.SettlementId)),"Local identities rewritten");Need(hall.Inventory[(int)ItemType.IronOre]==7&&Weapons(e.World)==weapons,"Captured inventory lost");Need(!(bool)Call(e,"AtWar",e.World.Settlements[0].Id,target.Id),"Captured realm still at war");Need(!e.TryStartWarParty(e.World.Settlements[0].Id,target.Id),"Friendly recapture");Valid(e.World);
  }
  public static void SaveDuringApproachAndOccupationContinuesIdentically()
  {
   var e=Fixture();Armory(e);Party(e);e.Step(.1f);var clone=new SimulationEngine(Clone(e.World));Need(Snapshot(e.World)==Snapshot(clone.World),"Constructor mutated war save");for(int i=0;i<120;i++){e.Step(.1f);clone.Step(.1f);}Need(Snapshot(e.World)==Snapshot(clone.World),"Approach diverged");Valid(e.World);
   e=Fixture();Occupiers(e);Call(e,"UpdateConquest",13f);clone=new SimulationEngine(Clone(e.World));Call(e,"UpdateConquest",7f);Call(clone,"UpdateConquest",7f);Need(Snapshot(e.World)==Snapshot(clone.World),"Occupation load diverged");Valid(e.World);
  }
  public static SimulationEngine NativeShowcase()
  {
   var e=Fixture();Armory(e);Party(e);var target=e.World.Settlements[1];
   // Controlled readiness: defenders are stationed outside the occupation radius, not deleted.
   foreach(var u in e.World.Units.Where(u=>u.SettlementId==target.Id)){u.X=48.5f;u.Y=45.5f;}
   Valid(e.World);return e;
  }
  public static void FullStepArmyCapturesTownWithoutTeleport()
  {
   var e=NativeShowcase();var target=e.World.Settlements[1];bool picked=false,marching=false;
   for(int i=0;i<1800&&target.ConquestCount==0;i++)
   {
    e.Step(.1f);picked|=e.World.Units.Count(u=>u.WarWeapon!=ItemType.None)>=3;
    marching|=e.World.Units.Count(u=>u.WarWeapon!=ItemType.None&&u.X>25&&u.X<43)>=3;
    if(i%10==0)Valid(e.World);
   }
   Need(picked&&marching,"No physical equipment/march");Need(target.ConquestCount==1,"Full-Step occupation never completed");Need(Weapons(e.World)==6,"Full-Step weapon conservation");Valid(e.World);
  }
  public static void CompetingRealmsDoNotPoolOccupationStrength()
  {
   var e=Fixture();Occupiers(e);var target=e.World.Settlements[1];e.Spawn(SpeciesType.Elf,48,48,3);var third=e.World.Settlements[2];e.SetDiplomacy(third.Id,target.Id,true);var a=e.World.Buildings.First(b=>b.Type==BuildingType.Smithy);
   foreach(var u in e.World.Units.Where(u=>u.SettlementId==third.Id)){u.Age=25;u.WarTargetSettlementId=target.Id;u.WarWeapon=ItemType.Spear;u.X=target.X+.5f;u.Y=target.Y+.5f;a.Inventory[(int)ItemType.Spear]--;}
   Call(e,"UpdateConquest",20f);Need(target.ConquestCount==0&&target.OccupationSeconds==0,"Competing realms pooled capture");Valid(e.World);
  }
  public static void DormantOccupationDecaysAndAutomaticRecruitmentStillRuns()
  {
   var e=Fixture();Armory(e);var home=e.World.Settlements[0];var target=e.World.Settlements[1];target.OccupierSettlementId=home.Id;target.OccupationSeconds=3;e.World.Laws.Warfare=false;Call(e,"UpdateConquest",1f);Need(target.OccupationSeconds==1,"Disabled-law occupation frozen");Call(e,"UpdateConquest",1f);Need(target.OccupationSeconds==0&&target.OccupierSettlementId==0,"Stale occupation owner");
   e.World.Laws.Warfare=true;home.EnemyId=target.Id;e.World.Tick=300;Call(e,"UpdateConquest",1f);Need(e.World.Units.Count(u=>u.WarTargetSettlementId==target.Id)>=3,"Inactive scan optimization disabled auto recruitment");Valid(e.World);
  }
  public static void CorruptOccupationOrSovereignCycleIsRejected()
  {var e=Fixture();var s=e.World.Settlements;s[0].OccupationSeconds=float.NaN;Need(!e.World.Validate(out _),"NaN occupation accepted");s[0].OccupationSeconds=0;s[0].SovereignSettlementId=s[1].Id;s[1].SovereignSettlementId=s[0].Id;Need(!e.World.Validate(out _),"Sovereign cycle accepted");}
 }
#if UNITY_5_3_OR_NEWER
 public sealed class ConquestTests
 {
  [SetUp]public void Serializer(){ConquestScenarios.Clone=w=>JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w));ConquestScenarios.Snapshot=w=>JsonUtility.ToJson(w);}
  [Test]public void Gates(){ConquestScenarios.PeaceTechnologyAndRealEquipmentGateParty();}
  [Test]public void Eligibility(){ConquestScenarios.ChildrenRulerAndOccupiedJobsAreNotDrafted();}
  [Test]public void PhysicalEquipment(){ConquestScenarios.RealDoorApproachPicksAndReturnsExactlyOneWeapon();}
  [Test]public void FullStorage(){ConquestScenarios.FullArmoryReturnDropsPhysicalWeaponWithoutOverflow();}
  [Test]public void Death(){ConquestScenarios.DeathDropsEquipmentExactlyOnce();}
  [Test]public void Occupation(){ConquestScenarios.OccupationRequiresThreeArmedAndTwentySeconds();}
  [Test]public void Defender(){ConquestScenarios.ALocalDefenderBlocksCapture();}
  [Test]public void CaptureConservation(){ConquestScenarios.CaptureKeepsPeopleBuildingsInventoriesAndRealmPeace();}
  [Test]public void Save(){ConquestScenarios.SaveDuringApproachAndOccupationContinuesIdentically();}
  [Test]public void FullStep(){ConquestScenarios.FullStepArmyCapturesTownWithoutTeleport();}
  [Test]public void Competing(){ConquestScenarios.CompetingRealmsDoNotPoolOccupationStrength();}
  [Test]public void Dormant(){ConquestScenarios.DormantOccupationDecaysAndAutomaticRecruitmentStillRuns();}
  [Test]public void Corrupt(){ConquestScenarios.CorruptOccupationOrSovereignCycleIsRejected();}
 }
#endif
}

