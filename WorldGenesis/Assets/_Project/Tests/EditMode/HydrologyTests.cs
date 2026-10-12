using System;
using System.Collections.Generic;
using System.Linq;
using WorldGenesis.Simulation;
#if UNITY_5_3_OR_NEWER
using NUnit.Framework;
using UnityEngine;
#endif

namespace WorldGenesis.Tests
{
    public static class HydrologyScenarios
    {
        public static Func<WorldState,WorldState> Clone;
        public static Func<WorldState,string> Snapshot;
        public static readonly List<object> Metrics = new List<object>();
        private static void Require(bool ok,string message) { if(!ok)throw new InvalidOperationException(message); }
        public static void FiveSeedsAcrossPresetsAreDeterministicAndDiverse()
        {
            var waterKinds=new HashSet<WaterKind>();var landKinds=new HashSet<TerrainType>();Metrics.Clear();
            var perPreset=new Dictionary<WorldPreset,HashSet<TerrainType>>();
            foreach(int seed in new[]{0,1,713,1618033,-2147483648})
            foreach(WorldPreset preset in new[]{WorldPreset.Island,WorldPreset.Archipelago,WorldPreset.Continent})
            {
                WorldState a=WorldGenerator.Generate(seed,128,128,preset), b=WorldGenerator.Generate(seed,128,128,preset);
                if(!perPreset.ContainsKey(preset))perPreset[preset]=new HashSet<TerrainType>();
                Require(a.ValidateHydrology(out string error),error);
                int freshwater=0,land=0,widened=0;
                float threshold=Math.Max(45,a.Cells.Length/170f);
                for(int i=0;i<a.Cells.Length;i++)
                {
                    TileData x=a.Cells[i],y=b.Cells[i];waterKinds.Add(x.WaterKind);landKinds.Add(x.Terrain);perPreset[preset].Add(x.Terrain);
                    Require(x.WaterKind==y.WaterKind&&x.WaterDepth==y.WaterDepth&&x.CurrentStrength==y.CurrentStrength&&x.ChannelWidth==y.ChannelWidth&&x.Terrain==y.Terrain,"Nondeterministic water or landforms");
                    if(x.WaterKind==WaterKind.None)land++;else if(x.WaterKind!=WaterKind.Ocean)freshwater++;
                    if(x.River&&x.Flow<=threshold)widened++;
                    Require((x.WaterKind==WaterKind.None)==(x.Terrain!=TerrainType.ShallowWater&&x.Terrain!=TerrainType.DeepWater),"New generator water descriptors disagree with geometry");
                    Require(x.RouteHeight+.0001f>=x.Height,"Water generation corrupted routing surface");
                    if(x.DrainageParent>=0)Require(a.Cells[x.DrainageParent].RouteHeight<=x.RouteHeight+.0001f,"Water generation reversed drainage");
                }
                Require(land>a.Cells.Length*.08f,"Hydrology consumed playable land");
                Require(freshwater<land*.55f,"Excessive freshwater flooding");
                Metrics.Add(new{seed,preset=preset.ToString(),land,freshwater,widened,
                    water=a.Cells.GroupBy(c=>c.WaterKind).ToDictionary(g=>g.Key.ToString(),g=>g.Count()),
                    terrain=a.Cells.GroupBy(c=>c.Terrain).ToDictionary(g=>g.Key.ToString(),g=>g.Count())});
            }
            foreach(WaterKind type in Enum.GetValues(typeof(WaterKind)))Require(waterKinds.Contains(type),"Missing water family across seed matrix: "+type);
            foreach(TerrainType type in new[]{TerrainType.Swamp,TerrainType.Hills,TerrainType.Ravine,TerrainType.Highlands})Require(landKinds.Contains(type),"Missing landform across seed matrix: "+type);
            foreach(var preset in perPreset)foreach(TerrainType type in new[]{TerrainType.Swamp,TerrainType.Hills,TerrainType.Ravine,TerrainType.Highlands})Require(preset.Value.Contains(type),"Missing landform in preset matrix: "+preset.Key+" / "+type);
        }
        private static WorldState Grid(int size,float height)
        {
            var w=new WorldState{Width=size,Height=size,Cells=new TileData[size*size],Seed=77};
            for(int i=0;i<w.Cells.Length;i++)w.Cells[i]=new TileData{Terrain=TerrainType.Grass,Height=height,RouteHeight=height,Flow=1,DrainageParent=-1,Vegetation=.4f};
            return w;
        }
        public static void DepressionIsOneConnectedLakeWithoutChangingElevations()
        {
            var w=Grid(32,.6f);
            for(int y=13;y<=17;y++)for(int x=13;x<=17;x++)w.Cells[w.Index(x,y)].Height=.5f;
            Hydrology.Generate(w,.445f);
            var lake=w.Cells.Select((c,i)=>new{c,i}).Where(t=>t.c.WaterKind==WaterKind.Lake).ToArray();
            Require(lake.Length==25,"A single closed bowl did not become a complete lake");
            Require(lake.All(t=>t.c.Height==.5f&&t.c.RouteHeight==.6f&&t.c.WaterDepth>3&&t.c.CurrentStrength<.1f),"Lake carving changed drainage or its calm depth is missing");
            var seen=new HashSet<int>();var queue=new Queue<int>();queue.Enqueue(lake[0].i);seen.Add(lake[0].i);
            while(queue.Count>0){int i=queue.Dequeue();foreach(int n in new[]{i-1,i+1,i-w.Width,i+w.Width})if(n>=0&&n<w.Cells.Length&&w.Cells[n].WaterKind==WaterKind.Lake&&seen.Add(n))queue.Enqueue(n);}
            Require(seen.Count==25,"Lake component contains disconnected puddles");Require(w.ValidateHydrology(out string error),error);
        }
        public static void ChannelHasFastHighlandsAndWideningMouth()
        {
            var w=Grid(32,.9f);
            for(int y=1;y<32;y++)for(int x=10;x<=22;x++)
            {
                int i=w.Index(x,y);float h=y>=29?.35f:.85f-y*.015f;
                w.Cells[i].Height=w.Cells[i].RouteHeight=h;w.Cells[i].Terrain=y>=29?TerrainType.ShallowWater:TerrainType.Grass;
                if(x==16&&y<29){w.Cells[i].Flow=50+y*25;w.Cells[i].DrainageParent=w.Index(x,y+1);}
            }
            Hydrology.Generate(w,.445f);
            TileData upper=w.Cells[w.Index(16,5)],mouth=w.Cells[w.Index(16,28)];
            Require(upper.WaterKind==WaterKind.MountainRiver&&mouth.WaterKind==WaterKind.Estuary,"Channel source/mouth classification absent");
            Require(upper.CurrentStrength>mouth.CurrentStrength&&mouth.ChannelWidth>upper.ChannelWidth,"Mountain speed or mouth widening absent");
            Require(w.Cells[w.Index(15,28)].River&&w.Cells[w.Index(17,28)].River,"Channel width is metadata only; no bank actually flooded");
            Require(w.Cells[w.Index(3,12)].WaterKind==WaterKind.None,"Remote high ground was flooded");Require(w.ValidateHydrology(out string error),error);
        }
        public static void LegacyMigrationPreservesGeographyRandomIdsAndIsIdempotent()
        {
            WorldState w=WorldGenerator.Generate(731,64,64);w.HydrologyVersion=0;
            TileData[] before=(TileData[])w.Cells.Clone();uint rng=w.RngState;int next=w.NextId;long tick=w.Tick;
            foreach(int i in Enumerable.Range(0,w.Cells.Length)){w.Cells[i].WaterKind=WaterKind.None;w.Cells[i].WaterDepth=w.Cells[i].CurrentStrength=0;w.Cells[i].ChannelWidth=0;}
            Hydrology.Initialize(w);
            for(int i=0;i<w.Cells.Length;i++)
            {
                TileData a=before[i],b=w.Cells[i];
                Require(a.Terrain==b.Terrain&&a.River==b.River&&a.Height==b.Height&&a.RouteHeight==b.RouteHeight&&a.Flow==b.Flow&&a.DrainageParent==b.DrainageParent&&a.Vegetation==b.Vegetation&&a.Moisture==b.Moisture,"Legacy coast, resources or routing changed");
            }
            Require(w.RngState==rng&&w.NextId==next&&w.Tick==tick,"Migration consumed gameplay state");string first=Snapshot(w);Hydrology.Initialize(w);Require(first==Snapshot(w),"Repeated migration changed water");
            Require(w.ValidateHydrology(out string error),error);
        }
        public static void EditsRefreshLocallyAndMalformedWaterIsRejected()
        {
            WorldState w=WorldGenerator.Generate(713,32,32);int i=Array.FindIndex(w.Cells,c=>c.WaterKind==WaterKind.None);Require(i>=0,"No dry test cell");
            TileData neighbour=w.Cells[i==0?1:i-1];w.Cells[i].Terrain=TerrainType.ShallowWater;w.Cells[i].River=false;Hydrology.RefreshCell(w,i);Require(w.Cells[i].WaterDepth>0,"Painted water has no depth");
            w.Cells[i].Terrain=TerrainType.Grass;Hydrology.RefreshCell(w,i);Require(w.Cells[i].WaterKind==WaterKind.None&&w.Cells[i].WaterDepth==0&&w.Cells[i].ChannelWidth==0,"Dry edit retained submerged properties");
            Require(w.Cells[i==0?1:i-1].WaterKind==neighbour.WaterKind,"Single-cell edit changed neighbour geography");
            w.Cells[i].WaterDepth=float.NaN;Require(!w.ValidateHydrology(out _),"NaN depth accepted");w.Cells[i].WaterDepth=0;w.Cells[i].CurrentStrength=2;Require(!w.ValidateHydrology(out _),"Invalid current accepted");
            w.Cells[i].CurrentStrength=0;w.Cells[i].ChannelWidth=10;Require(!w.ValidateHydrology(out _),"Oversized channel accepted");
            w.Cells[i].ChannelWidth=0;w.Cells[i].WaterKind=(WaterKind)7;Require(!w.ValidateHydrology(out _),"Unknown water kind accepted");
        }
        public static void HydrologySaveRoundTripContinuesDeterministically()
        {
            var a=new SimulationEngine(WorldGenerator.Generate(991,48,48));for(int i=0;i<75;i++)a.Step(.1f);
            var b=new SimulationEngine(Clone(a.World));Require(Snapshot(a.World)==Snapshot(b.World),"Hydrology lost fields on reload");
            for(int i=0;i<125;i++){a.Step(.1f);b.Step(.1f);}Require(Snapshot(a.World)==Snapshot(b.World),"Water save continuation diverged");
        }
    }
#if UNITY_5_3_OR_NEWER
    public sealed class HydrologyTests
    {
        [SetUp]public void Serializer(){HydrologyScenarios.Clone=w=>JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(w));HydrologyScenarios.Snapshot=w=>JsonUtility.ToJson(w);}
        [Test]public void SeedMatrix(){HydrologyScenarios.FiveSeedsAcrossPresetsAreDeterministicAndDiverse();}
        [Test]public void ConnectedLake(){HydrologyScenarios.DepressionIsOneConnectedLakeWithoutChangingElevations();}
        [Test]public void RiverFamiliesAndGeometry(){HydrologyScenarios.ChannelHasFastHighlandsAndWideningMouth();}
        [Test]public void LegacyMigration(){HydrologyScenarios.LegacyMigrationPreservesGeographyRandomIdsAndIsIdempotent();}
        [Test]public void LocalEditsAndValidation(){HydrologyScenarios.EditsRefreshLocallyAndMalformedWaterIsRejected();}
        [Test]public void JsonUtilityContinuation(){HydrologyScenarios.HydrologySaveRoundTripContinuesDeterministically();}
    }
#endif
}
