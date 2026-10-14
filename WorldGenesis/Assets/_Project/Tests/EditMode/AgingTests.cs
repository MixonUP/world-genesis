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
    public static class AgingScenarios
    {
        public static Func<WorldState,WorldState> Clone;
        public static Func<WorldState,string> Snapshot;
        private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        private static void Near(float expected,float actual,string message){Need(Math.Abs(expected-actual)<.003f,message+": "+actual+" != "+expected);}
        private static object Call(SimulationEngine e,string name,params object[] args){return typeof(SimulationEngine).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(e,args);}
        private static WorldState Raw()
        {
            WorldState w=new WorldState{Seed=14010,RngState=14010,Width=32,Height=32,Cells=new TileData[1024],NaturalWildlife=false};
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)w.Cells[w.Index(x,y)]=new TileData{Terrain=x<6?TerrainType.DeepWater:TerrainType.Grass,Height=x<6?.15f:.55f,RouteHeight=x<6?.15f:.55f,Moisture=.5f,Temperature=.5f,DrainageParent=-1,WaterKind=x<6?WaterKind.Ocean:WaterKind.None,WaterDepth=x<6?2:0};return w;
        }
        private static SimulationEngine World(SpeciesType species=SpeciesType.Human,int count=2)
        {
            SimulationEngine e=new SimulationEngine(Raw());e.World.Laws.Births=e.World.Laws.Warfare=e.World.Laws.FireSpread=false;e.World.Weather.Automatic=false;e.World.NextWandererTick=long.MaxValue/2;e.Spawn(species,12,12,count);
            foreach(UnitData u in e.World.Units){u.Age=WildlifeCatalog.IsAnimal(species)?2:25;u.Hunger=u.Thirst=u.Fatigue=u.SocialNeed=0;u.Task=UnitTask.Idle;u.DecisionTimer=10000;u.Path.Clear();u.PathCursor=0;}
            return e;
        }
        private static void Valid(WorldState w){Need(w.Validate(out string error),error);}
        private static void Step(SimulationEngine e,int ticks){for(int i=0;i<ticks;i++)e.Step(.1f);Valid(e.World);}
        private static BuildingData AddPort(SimulationEngine e,int y){return(BuildingData)Call(e,"AddBuilding",e.World.Settlements[0],6,y,BuildingType.Port);}
        private static ShipData Sailing(SimulationEngine e)
        {
            BuildingData first=AddPort(e,12),last=AddPort(e,26);Need(first!=null&&last!=null,"No fixture ports");
            ShipData ship=(ShipData)Call(e,"CreateShip",ShipKind.Transport,e.World.Settlements[0].Id,first.BerthCell);ship.HomePortId=ship.DockedPortId=ship.DestinationPortId=first.Id;ship.Phase=ShipPhase.Docked;ship.Food=ship.Water=20;
            foreach(UnitData u in e.World.Units.Take(2)){u.ShipId=ship.Id;u.X=ship.X;u.Y=ship.Y;u.InsideBuildingId=u.EnteringBuildingId=0;u.Task=UnitTask.Sail;u.Profession=ship.CaptainUnitId==0?UnitProfession.Captain:UnitProfession.Sailor;if(ship.CaptainUnitId==0)ship.CaptainUnitId=u.Id;ship.CrewIds.Add(u.Id);}
            Need(e.TrySailToPort(ship.Id,last.Id),"Sea route rejected");Need(ship.Phase==ShipPhase.Sailing,"Not at sea");Valid(e.World);return ship;
        }
        public static void AllSpeciesRatesAreExactlyHalfOfPreviousRates()
        {foreach(SpeciesType species in Enum.GetValues(typeof(SpeciesType))){float previous=WildlifeCatalog.IsAnimal(species)?WildlifeCatalog.AgeRate:.1f;Need(AgingRules.RatePerSecond(species)==previous*.5f,"Species rate "+species);}Near(.05f,AgingRules.RatePerSecond(SpeciesType.Human),"Citizen rate");Near(.0125f,AgingRules.RatePerSecond(SpeciesType.Sheep),"Animal rate");}
        public static void ActualTicksSlowEveryCivilizedRace()
        {foreach(SpeciesType species in new[]{SpeciesType.Human,SpeciesType.Elf,SpeciesType.Dwarf,SpeciesType.Orc}){var e=World(species);int id=e.World.Units[0].Id;Step(e,100);Near(25.5f,e.World.GetUnit(id).Age,"Future citizen age "+species);Need(e.World.Tick==100,"Tick skipped");}}
        public static void ActualTicksSlowAllAnimalSpecies()
        {foreach(SpeciesType species in Enum.GetValues(typeof(SpeciesType))){if(!WildlifeCatalog.IsAnimal(species))continue;var e=World(species,1);int id=e.World.Units[0].Id;Step(e,100);Near(2.125f,e.World.GetUnit(id).Age,"Future animal age "+species);}}
        public static void DisabledLawFreezesAgeWithoutFreezingWorld()
        {foreach(SpeciesType species in new[]{SpeciesType.Human,SpeciesType.Sheep}){var e=World(species,1);e.SetLaw(WorldLaw.Aging,false);float age=e.World.Units[0].Age;Step(e,100);Need(e.World.Units[0].Age==age,"Disabled aging changed age");Need(e.World.Tick==100&&e.World.Units[0].Hunger>0,"Whole simulation frozen");}}
        public static void CaptainAndSailorAtSeaAgeOnceAtHalfRate()
        {var e=World();ShipData s=Sailing(e);float x=s.X,y=s.Y;Step(e,30);Need(s.Phase==ShipPhase.Sailing,"Fixture finished voyage too soon");Need(s.X!=x||s.Y!=y,"Ship never moved");foreach(int id in s.CrewIds)Near(25.15f,e.World.GetUnit(id).Age,"Sea aging or double tick");}
        public static void DisabledLawAlsoFreezesCrewWhileProvisionsAreConsumed()
        {var e=World();ShipData s=Sailing(e);e.SetLaw(WorldLaw.Aging,false);float food=s.Food;Step(e,30);foreach(int id in s.CrewIds)Need(e.World.GetUnit(id).Age==25,"Sea law ignored");Need(s.Food<food,"Sea simulation frozen");}
        public static void ForeignMarinersUseTheSameFutureRate()
        {var e=World();BuildingData p=AddPort(e,12);Need(e.TryInviteWanderer(p.Id,ShipKind.WandererSmall),"Foreign ship rejected");ShipData ship=e.World.Ships[0];var before=ship.CrewIds.ToDictionary(id=>id,id=>e.World.GetUnit(id).Age);Step(e,10);foreach(var pair in before)Near(pair.Value+.05f,e.World.GetUnit(pair.Key).Age,"Foreign sailor rate");}
        public static void OldAndCurrentSavesRetainAgeAndContinueIdentically()
        {
            WorldState old=Raw();old.NextId=2;old.Units.Add(new UnitData{Id=1,Species=SpeciesType.Human,X=12.5f,Y=12.5f,Age=84.25f,Health=100,MaxHealth=100,Name="Legacy",Task=UnitTask.Idle});Valid(old);var legacy=new SimulationEngine(old);Need(old.Units[0].Age==84.25f,"Legacy age rescaled");
            var e=World();ShipData ship=Sailing(e);Step(e,13);string before=Snapshot(e.World);var second=new SimulationEngine(Clone(e.World));Need(before==Snapshot(second.World),"Current save changed on load");Step(e,40);Step(second,40);Need(Snapshot(e.World)==Snapshot(second.World),"Age continuation diverged");
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class AgingTests
    {
        [SetUp] public void Serializer(){AgingScenarios.Clone=w=>JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w));AgingScenarios.Snapshot=w=>JsonUtility.ToJson(w);}
        [Test] public void SpeciesRates(){AgingScenarios.AllSpeciesRatesAreExactlyHalfOfPreviousRates();}
        [Test] public void Citizens(){AgingScenarios.ActualTicksSlowEveryCivilizedRace();}
        [Test] public void Animals(){AgingScenarios.ActualTicksSlowAllAnimalSpecies();}
        [Test] public void Law(){AgingScenarios.DisabledLawFreezesAgeWithoutFreezingWorld();}
        [Test] public void Sea(){AgingScenarios.CaptainAndSailorAtSeaAgeOnceAtHalfRate();}
        [Test] public void SeaLaw(){AgingScenarios.DisabledLawAlsoFreezesCrewWhileProvisionsAreConsumed();}
        [Test] public void ForeignCrew(){AgingScenarios.ForeignMarinersUseTheSameFutureRate();}
        [Test] public void Save(){AgingScenarios.OldAndCurrentSavesRetainAgeAndContinueIdentically();}
    }
#endif
}
