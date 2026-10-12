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
    public static class WeatherScenarios
    {
        public static Func<WorldState,WorldState> Clone;
        public static Func<WorldState,string> Snapshot;
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        private static object Call(SimulationEngine e,string method,params object[] args)=>typeof(SimulationEngine).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(e,args);
        private static SimulationEngine Flat(int seed=193,float temperature=.6f)
        {
            var w=new WorldState{Width=48,Height=48,Seed=seed,RngState=(uint)(seed*13+1),Cells=new TileData[48*48],NaturalWildlife=false};
            for(int i=0;i<w.Cells.Length;i++)w.Cells[i]=new TileData{Terrain=TerrainType.Grass,Height=.55f,Moisture=.3f,Temperature=temperature,Vegetation=.5f,DrainageParent=-1};
            var e=new SimulationEngine(w);w.Weather.Automatic=false;w.Laws.Births=w.Laws.Aging=w.Laws.Warfare=w.Laws.FireSpread=false;
            for(int i=0;i<w.ResourceNodes.Length;i++)w.ResourceNodes[i]=default(ResourceNodeData);
            Require(w.Validate(out string error),error);return e;
        }
        private static void Step(SimulationEngine e,int ticks){for(int i=0;i<ticks;i++)e.Step(.1f);}
        private static void River(WorldState w)
        {
            for(int y=7;y<41;y++){int cell=w.Index(23,y);w.Cells[cell].Terrain=TerrainType.ShallowWater;w.Cells[cell].Height=.5f;w.Cells[cell].Vegetation=0;w.Cells[cell].WaterKind=WaterKind.River;w.Cells[cell].WaterDepth=1.1f;w.Cells[cell].CurrentStrength=.25f;w.Cells[cell].ChannelWidth=1;w.Cells[cell].River=true;}
            w.NavigationRevision++;
        }
        private static SettlementData Town(SimulationEngine e,int x=12,int y=24)
        {
            e.Spawn(SpeciesType.Human,x,y,4);var s=e.World.Settlements[0];s.Food=200;s.NextPlanningTick=s.NextPolicyReviewTick=100000;
            foreach(var u in e.World.Units){u.DecisionTimer=10000;u.Hunger=u.Thirst=u.Fatigue=u.SocialNeed=.05f;u.Task=UnitTask.Idle;u.Path.Clear();u.PathCursor=0;u.Age=25;}
            return s;
        }
        public static void LegacyWeatherMigrationPreservesCellsRngIdsAndTick()
        {
            var e=Flat();var legacy=Clone(e.World);legacy.WeatherVersion=0;legacy.Weather=null;legacy.FloodCells=null;
            uint rng=legacy.RngState;int id=legacy.NextId;long tick=legacy.Tick;var cells=(TileData[])legacy.Cells.Clone();
            Require(legacy.Validate(out string error),error);new SimulationEngine(legacy);
            Require(legacy.WeatherVersion==1&&legacy.Weather.Kind==WeatherKind.Clear&&legacy.FloodCells.Count==0,"Legacy weather migration missing");
            Require(legacy.RngState==rng&&legacy.NextId==id&&legacy.Tick==tick&&cells.SequenceEqual(legacy.Cells),"Migration mutated old geography or random stream");
            string once=Snapshot(legacy);new SimulationEngine(legacy);Require(Snapshot(legacy)==once,"Migration not idempotent");Require(legacy.Validate(out error),error);
        }
        public static void InvalidWeatherCommandsAreAtomicAndBoundsAreClamped()
        {
            var e=Flat();string before=Snapshot(e.World);
            Require(!e.SetWeather((WeatherKind)7,24,24)&&!e.SetWeather(WeatherKind.Rain,-1,24)&&!e.SetWeather(WeatherKind.Rain,24,24,0)
                &&!e.SetWeather(WeatherKind.Rain,24,24,12,float.NaN)&&!e.SetWeather(WeatherKind.Rain,24,24,12,1,float.PositiveInfinity)
                &&!e.SetWeather(WeatherKind.Rain,24,24,12,-1)&&!e.SetWeather(WeatherKind.Rain,24,24,12,1,-2),"Invalid weather command accepted");
            Require(Snapshot(e.World)==before,"Rejected command mutated state");Require(e.SetWeather(WeatherKind.Rain,24,24,100,2,500),"Valid bounded command rejected");
            Require(e.World.Weather.Radius==40&&e.World.Weather.Intensity==1&&e.World.Weather.RemainingSeconds==180,"Front limits not applied");Require(e.World.Validate(out string error),error);
        }
        public static void RainIsLocalExtinguishesAndFeedsRealGrowthWithoutGrantingStock()
        {
            var e=Flat();var w=e.World;int center=w.Index(24,24),outside=w.Index(2,2);w.Cells[center].Fire=w.Cells[outside].Fire=.8f;
            float growth=w.GetWeatherGrowthMultiplier(center);e.SetWeather(WeatherKind.Rain,24,24,8,1,10);Step(e,20);
            Require(w.Cells[center].Fire==0&&w.Cells[outside].Fire>.4f,"Rain did not extinguish locally");
            Require(w.Cells[center].Moisture>.3f&&w.Cells[outside].Moisture==.3f&&w.GetWeatherGrowthMultiplier(center)>growth,"Rain moisture/growth response missing or global");
            Require(w.Settlements.Count==0&&w.GroundStocks.Count==0&&w.LostResources.Sum()==0,"Weather fabricated goods");Require(w.Validate(out string error),error);
        }
        public static void SnowAccumulationRespectsTemperatureSlowsTraversalAndMelts()
        {
            var cold=Flat(201,.2f);var warm=Flat(201,.65f);int cell=cold.World.Index(24,24);float speed=cold.World.GetTerrainSpeed(cell);
            cold.SetWeather(WeatherKind.Snow,24,24,8,1,15);warm.SetWeather(WeatherKind.Snow,24,24,8,1,15);Step(cold,60);Step(warm,60);
            Require(cold.World.Cells[cell].SnowCover>warm.World.Cells[cell].SnowCover+.1f,"Snow ignores baseline climate");
            Require(cold.World.GetTerrainSpeed(cell)<speed,"Snow is a cosmetic overlay only");float cover=cold.World.Cells[cell].SnowCover;
            cold.SetWeather(WeatherKind.Clear,24,24,8,1,40);Step(cold,400);Require(cold.World.Cells[cell].SnowCover<cover*.1f,"Snow never melts outside its front");Require(cold.World.Validate(out string error),error);
        }
        public static void DroughtDriesActualSoilAndStartsBoundedWildfire()
        {
            var e=Flat(303,.9f);var w=e.World;w.Laws.FireSpread=true;for(int i=0;i<w.Cells.Length;i++)w.Cells[i].Moisture=.08f;
            e.SetWeather(WeatherKind.Drought,24,24,8,1,30);Step(e,50);
            Require(w.Cells[w.Index(24,24)].Moisture<.08f,"Drought soil did not dry");
            Require(w.Events.Any(item=>item.Kind==EventKind.Fire&&item.Text.Contains("Засуха"))&&w.Cells.Any(tile=>tile.Fire>0),"Dry fuel never ignited");
            Require(w.Cells[w.Index(2,2)].Moisture==.08f&&w.Cells[w.Index(2,2)].Fire==0,"Drought acted outside the front");Require(w.Validate(out string error),error);
        }
        public static void FloodRequiresRealWaterIsBoundedAndRecedesWithoutResurrectingStock()
        {
            var dry=Flat();dry.SetWeather(WeatherKind.Flood,24,24,12,1,10);Step(dry,50);Require(dry.World.FloodCells.Count==0,"Flood manufactured a lake without a water source");
            var e=Flat();var w=e.World;River(w);var s=Town(e);
            var farm=(BuildingData)Call(e,"AddBuilding",s,24,24,BuildingType.Farm);farm.AgriculturalStock[(int)AgriculturalProduct.Fiber]=2;
            int tree=w.Index(24,23);w.Cells[tree].Terrain=TerrainType.Forest;w.Cells[tree].Vegetation=1;w.ResourceNodes[tree]=new ResourceNodeData{Type=ResourceType.Tree,Capacity=4,Amount=4};
            int revision=w.NavigationRevision;e.SetWeather(WeatherKind.Flood,24,24,12,1,8);Step(e,60);
            Require(w.FloodCells.Count>0&&w.FloodCells.Count<=WeatherCatalog.MaximumFloodCells&&w.NavigationRevision>revision,"No bounded physical inundation");
            Require(w.IsTemporaryFlood(w.Index(24,24))&&!w.IsWalkable(24,24)&&w.GetWeatherGrowthMultiplier(w.Index(24,24))==0,"Flood is cosmetic or crops grow underwater");
            Require(w.GetBuilding(farm.Id)!=null&&farm.Health<100&&farm.Health>0,"Flood instantly deleted a healthy building instead of damaging it");
            Require(w.ResourceNodes[tree].Amount==0&&w.LostResources[(int)ItemType.Log]>=4,"Drowned tree stock not accounted");
            w.Cells[tree].SettlementId=s.Id;float loss=w.LostResources[(int)ItemType.Log];
            e.SetWeather(WeatherKind.Clear,24,24,12,0,40);Step(e,280);
            Require(w.FloodCells.Count==0&&w.Cells[tree].Terrain==TerrainType.Forest&&w.Cells[tree].WaterDepth==0&&w.Cells[tree].Height==.55f,"Flood did not restore only underlying terrain");
            Require(w.Cells[tree].SettlementId==s.Id&&w.ResourceNodes[tree].Amount==0&&w.LostResources[(int)ItemType.Log]==loss,"Flood restored deleted stock or old ownership");
            Require(w.GetBuilding(farm.Id)!=null&&farm.AgriculturalStock[(int)AgriculturalProduct.Fiber]==2,"Recession lost building/contents");Require(w.Validate(out string error),error);
        }
        public static void PaintingFloodedLandWinsOverLaterRecession()
        {
            var e=Flat();var w=e.World;River(w);e.SetWeather(WeatherKind.Flood,24,24,10,1,5);Step(e,30);
            Require(w.FloodCells.Count>0,"Fixture did not flood");int cell=w.FloodCells[0].Cell;
            e.Paint(TerrainType.Mountain,cell%w.Width,cell/w.Width,0);Require(w.GetFloodAt(cell%w.Width,cell/w.Width)==null,"Paint kept a stale flood owner");
            e.SetWeather(WeatherKind.Clear,24,24,10,0,40);Step(e,300);
            Require(w.Cells[cell].Terrain==TerrainType.Mountain&&w.Cells[cell].WaterDepth==0,"Weather undid the player's terrain edit");Require(w.Validate(out string error),error);
        }
        public static void HurricaneMovesDamagesLivingActorsAndContinuesIdenticallyAfterSave()
        {
            var e=Flat();var w=e.World;var s=Town(e,24,24);var unit=w.Units[0];unit.X=24.5f;unit.Y=24.5f;var hall=w.Buildings.First(b=>b.Type==BuildingType.Hall);
            e.SetWeather(WeatherKind.Hurricane,24,24,12,1,40);w.Weather.DirectionX=1;w.Weather.DirectionY=0;float start=w.Weather.CenterX,health=unit.Health,buildingHealth=hall.Health;
            Step(e,30);Require(w.Weather.CenterX>start+1&&unit.Health<health&&hall.Health<buildingHealth,"Hurricane failed to move or inflict real damage");
            var copy=new SimulationEngine(Clone(w));for(int i=0;i<100;i++){e.Step(.1f);copy.Step(.1f);}Require(Snapshot(w)==Snapshot(copy.World),"Saved moving front diverges");Require(w.Validate(out string error),error);
        }
        public static void FloodSavePersistsUnderlyingTerrainExpiryAndNoDuplicateRecords()
        {
            var e=Flat();var w=e.World;River(w);e.SetWeather(WeatherKind.Flood,24,24,12,1,6);Step(e,30);Require(w.FloodCells.Count>0,"Fixture did not flood");
            var copy=new SimulationEngine(Clone(w));for(int i=0;i<330;i++){e.Step(.1f);copy.Step(.1f);}Require(Snapshot(w)==Snapshot(copy.World)&&w.FloodCells.Count==0,"Saved flood expiry/recovery diverged");
            var bad=Clone(w);bad.Weather.RemainingSeconds=float.NaN;Require(!bad.Validate(out _),"Invalid weather float admitted");
            e.SetWeather(WeatherKind.Flood,24,24,12,1,6);Step(e,10);bad=Clone(w);bad.FloodCells.Add(bad.FloodCells[0]);Require(!bad.Validate(out _),"Duplicate overlay ownership admitted");
        }
        public static void ThreeSeedAutomaticCyclesAreDeterministicAndClimateCanSelectSnowAndDrought()
        {
            foreach(int seed in new[]{17,71,713})
            {
                var first=Flat(seed);first.World.Weather.Automatic=true;first.World.Weather.NextTransitionTick=0;var second=new SimulationEngine(Clone(first.World));
                for(int i=0;i<1200;i++){first.Step(.1f);second.Step(.1f);}Require(first.World.Weather.Sequence>=2,"Automatic weather never changes");
                Require(Snapshot(first.World)==Snapshot(second.World),"Same-seed weather diverged");Require(first.World.Validate(out string error),error);
            }
            bool snow=false,drought=false;
            for(int seed=1;seed<=20;seed++)
            {
                var cold=Flat(seed,.15f);var hot=Flat(seed,.9f);Call(cold,"ChooseNaturalWeather");Call(hot,"ChooseNaturalWeather");
                snow|=cold.World.Weather.Kind==WeatherKind.Snow;drought|=hot.World.Weather.Kind==WeatherKind.Drought;
            }
            Require(snow&&drought,"Natural selection ignores the world's climate");
        }
        public static void StormFloodNeverExceeds192AtAnyTick()
        {
            var e=Flat();var w=e.World;River(w);e.SetWeather(WeatherKind.Storm,24,24,30,1,180);int peak=0;
            for(int i=0;i<900;i++)
            {
                e.Step(.1f);peak=Math.Max(peak,w.FloodCells.Count);Require(w.FloodCells.Count<=192,"Natural expansion crossed its hard flood budget at "+w.Tick);
                if(i%30==0)Require(w.Validate(out string error),error);
            }
            Require(peak==192,"Fixture did not exercise the exact flood boundary");
        }
        public static void StormStartedBetweenSecondBoundariesStillStrikesWithoutCorruptingUnitIndex()
        {
            var e=Flat();var w=e.World;Town(e,24,24);Step(e,3);e.SetWeather(WeatherKind.Storm,24,24,10,1,20);Step(e,160);
            Require(w.Events.Count(item=>item.Kind==EventKind.Lightning)>=2,"Storm started on tick3 never produces scheduled lightning");Require(w.Validate(out string error),error);
        }
        public static void BrokenDurationAndForgedWetUnderlyingSnapshotsAreRejected()
        {
            var e=Flat();var w=e.World;River(w);e.SetWeather(WeatherKind.Flood,24,24,10,1,5);Step(e,20);Require(w.FloodCells.Count>0,"Fixture did not flood");
            var bad=Clone(w);bad.Weather.Kind=WeatherKind.Drought;bad.Weather.RemainingSeconds=0;bad.Weather.NextTransitionTick=0;Require(!bad.Validate(out _),"Zero-duration nonclear front accepted and cannot transition");
            bad=Clone(w);bad.FloodCells[0].OriginalTerrain=TerrainType.DeepWater;Require(!bad.Validate(out _),"Dry DeepWater underlying snapshot admitted");
            bad=Clone(w);bad.FloodCells[0].OriginalTerrain=TerrainType.Grass;bad.FloodCells[0].OriginalWaterKind=WaterKind.River;bad.FloodCells[0].OriginalDepth=12;Require(!bad.Validate(out _),"Forged permanent deep-water recovery admitted");
            bad=Clone(w);bad.FloodCells[0].AppliedDepth+=.1f;Require(!bad.Validate(out _),"Overlay no longer matching the saved tile was admitted");
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class WeatherSimulationTests
    {
        [SetUp] public void Serializers(){WeatherScenarios.Clone=w=>JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w));WeatherScenarios.Snapshot=w=>JsonUtility.ToJson(w);}
        [TestCase("LegacyWeatherMigrationPreservesCellsRngIdsAndTick")]
        [TestCase("InvalidWeatherCommandsAreAtomicAndBoundsAreClamped")]
        [TestCase("RainIsLocalExtinguishesAndFeedsRealGrowthWithoutGrantingStock")]
        [TestCase("SnowAccumulationRespectsTemperatureSlowsTraversalAndMelts")]
        [TestCase("DroughtDriesActualSoilAndStartsBoundedWildfire")]
        [TestCase("FloodRequiresRealWaterIsBoundedAndRecedesWithoutResurrectingStock")]
        [TestCase("PaintingFloodedLandWinsOverLaterRecession")]
        [TestCase("HurricaneMovesDamagesLivingActorsAndContinuesIdenticallyAfterSave")]
        [TestCase("FloodSavePersistsUnderlyingTerrainExpiryAndNoDuplicateRecords")]
        [TestCase("ThreeSeedAutomaticCyclesAreDeterministicAndClimateCanSelectSnowAndDrought")]
        [TestCase("StormFloodNeverExceeds192AtAnyTick")]
        [TestCase("StormStartedBetweenSecondBoundariesStillStrikesWithoutCorruptingUnitIndex")]
        [TestCase("BrokenDurationAndForgedWetUnderlyingSnapshotsAreRejected")]
        public void Scenario(string name){typeof(WeatherScenarios).GetMethod(name).Invoke(null,null);}
    }
#endif
}
