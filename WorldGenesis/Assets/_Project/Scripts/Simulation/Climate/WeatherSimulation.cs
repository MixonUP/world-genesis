using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private void MigrateWeather()
        {
            if(World.WeatherVersion==1)return;
            World.Weather=new WeatherData{CenterX=World.Width*.5f,CenterY=World.Height*.5f,Radius=Math.Min(12,World.Width*.5f),NextTransitionTick=World.Tick+1200};
            World.FloodCells=new List<FloodCellData>();World.WeatherVersion=1;
        }
        public bool SetWeather(WeatherKind kind,int x,int y,int radius=12,float intensity=1,float seconds=40)
        {
            if((int)kind<0||(int)kind>6||!World.InBounds(x,y)||radius<1||!WeatherFinite(intensity)||intensity<0||!WeatherFinite(seconds)||seconds<0||(kind!=WeatherKind.Clear&&seconds==0))return false;
            if(World.WeatherVersion==0)MigrateWeather();WeatherData w=World.Weather;
            w.Sequence=w.Sequence==int.MaxValue?0:w.Sequence+1;w.Kind=kind;w.CenterX=x+.5f;w.CenterY=y+.5f;w.Radius=Math.Min(40,radius);
            w.Intensity=kind==WeatherKind.Clear?0:Math.Min(1,intensity);w.RemainingSeconds=kind==WeatherKind.Clear?0:Math.Min(180,seconds);w.StartedTick=World.Tick;
            w.NextTransitionTick=World.Tick+(long)Math.Max(200,Math.Min(180,seconds)*10);
            uint direction=StableRandom.Hash(World.Seed,w.Sequence,x+y*World.Width,8101)%8;
            float[] dx={1,.70710677f,0,-.70710677f,-1,-.70710677f,0,.70710677f};
            float[] dy={0,.70710677f,1,.70710677f,0,-.70710677f,-1,-.70710677f};
            w.DirectionX=dx[direction];w.DirectionY=dy[direction];
            AddEvent(EventKind.Built,w.CenterX,w.CenterY,"Погода: "+WeatherCatalog.Name(kind));World.Revision++;return true;
        }
        private static bool WeatherFinite(float v){return !float.IsNaN(v)&&!float.IsInfinity(v);}
        // A user terrain edit owns the new terrain. Discard the overlay BEFORE Paint/Meteor writes it.
        private void ClearFloodAt(int cell)
        {
            if(World.FloodCells==null)return;
            for(int i=World.FloodCells.Count-1;i>=0;i--)if(World.FloodCells[i].Cell==cell)World.FloodCells.RemoveAt(i);
        }
        private void UpdateWeather(float dt)
        {
            WeatherData w=World.Weather;if(w==null)return;
            RecedeFloods();
            if(w.Kind!=WeatherKind.Clear&&w.RemainingSeconds>0)
            {
                w.RemainingSeconds=Math.Max(0,w.RemainingSeconds-dt);
                if(w.Kind==WeatherKind.Hurricane)
                {
                    float x=w.CenterX+w.DirectionX*.6f*dt,y=w.CenterY+w.DirectionY*.6f*dt;
                    if(x<.5f||x>World.Width-.5f){w.DirectionX=-w.DirectionX;x=Math.Max(.5f,Math.Min(World.Width-.5f,x));}
                    if(y<.5f||y>World.Height-.5f){w.DirectionY=-w.DirectionY;y=Math.Max(.5f,Math.Min(World.Height-.5f,y));}
                    w.CenterX=x;w.CenterY=y;
                }
                if(w.RemainingSeconds==0){w.Kind=WeatherKind.Clear;w.Intensity=0;w.NextTransitionTick=World.Tick+200+StableRandom.Hash(World.Seed,w.Sequence,0,8102)%301;}
            }
            if(w.Automatic&&w.Kind==WeatherKind.Clear&&World.Tick>=w.NextTransitionTick)ChooseNaturalWeather();
            if(World.Tick%10!=0)return;
            WeatherPulse();
            if(w.Kind==WeatherKind.Storm&&w.Intensity>=.65f&&World.Tick-w.StartedTick>=50&&((World.Tick-w.StartedTick)/10)%5==0)WeatherLightning();
            if((w.Kind==WeatherKind.Flood||w.Kind==WeatherKind.Hurricane||w.Kind==WeatherKind.Storm||w.Kind==WeatherKind.Rain)&&w.Intensity>=.8f
                &&World.Tick-w.StartedTick>=(w.Kind==WeatherKind.Flood?0:w.Kind==WeatherKind.Rain?120:30))ExpandFlood();
            DamageWeatherExposedBuildings();
            DestroyInvalidBuildings();
        }
        private void ChooseNaturalWeather()
        {
            WeatherData w=World.Weather;int sequence=w.Sequence+1;
            uint hash=StableRandom.Hash(World.Seed,sequence,(int)(World.Tick/10),8103);
            int cell=(int)(hash%(uint)World.Cells.Length);
            for(int i=0;i<12&&(World.Cells[cell].WaterKind==WaterKind.Ocean||World.Cells[cell].Terrain==TerrainType.Mountain);i++)cell=(int)(StableRandom.Hash(World.Seed,sequence,i,8104)%(uint)World.Cells.Length);
            TileData climate=World.Cells[cell];uint roll=StableRandom.Hash(World.Seed,sequence,cell,8105)%100;
            WeatherKind kind=roll<18?WeatherKind.Clear:climate.Temperature<.33f&&roll<75?WeatherKind.Snow:
                (climate.Moisture<.4f||climate.Temperature>.68f)&&roll<65?WeatherKind.Drought:roll<28?WeatherKind.Drought:roll<78?WeatherKind.Rain:roll<98?WeatherKind.Storm:WeatherKind.Hurricane;
            int radius=8+(int)(StableRandom.Hash(World.Seed,sequence,cell,8106)%17);
            float intensity=.35f+.6f*StableRandom.Coordinate(World.Seed,sequence,cell,8107);
            float duration=kind==WeatherKind.Drought?60+hash%41:kind==WeatherKind.Hurricane?25+hash%16:35+hash%26;
            SetWeather(kind,cell%World.Width,cell/World.Width,radius,intensity,duration);
        }
        private void WeatherPulse()
        {
            WeatherData w=World.Weather;bool changed=false,navigationChanged=false;
            // One bounded front plus a full-grid melt pass once per second. No per-cell objects or RNG draws.
            for(int cell=0;cell<World.Cells.Length;cell++)
            {
                ref TileData t=ref World.Cells[cell];float force=WeatherCatalog.Influence(w,cell%World.Width+.5f,cell/World.Width+.5f);
                float beforeSnow=t.SnowCover;
                if(t.SnowCover>0&&!(w.Kind==WeatherKind.Snow&&force>0))
                {float melt=Math.Min(t.SnowCover,.006f+Math.Max(0,t.Temperature-.25f)*.04f);t.SnowCover-=melt;t.Moisture=Math.Min(1,t.Moisture+melt*.35f);changed=true;}
                if(Math.Abs(beforeSnow-t.SnowCover)>.005f)navigationChanged=true;
                if(force<=0)continue;
                bool rain=w.Kind==WeatherKind.Rain||w.Kind==WeatherKind.Storm||w.Kind==WeatherKind.Hurricane||w.Kind==WeatherKind.Flood;
                if(rain)
                {t.Moisture=Math.Min(1,t.Moisture+force*(.025f+(1-t.Temperature)*.015f));t.Fire=Math.Max(0,t.Fire-force*.75f);changed=true;}
                else if(w.Kind==WeatherKind.Snow)
                {float frozen=Math.Max(0,1-t.Temperature/.7f);t.SnowCover=Math.Min(1,t.SnowCover+force*.055f*frozen);t.Moisture=Math.Min(1,t.Moisture+force*.008f);t.Fire=Math.Max(0,t.Fire-force*.4f);changed=true;}
                else if(w.Kind==WeatherKind.Drought)
                {t.Moisture=Math.Max(0,t.Moisture-force*(.015f+.025f*t.Temperature));if(t.Moisture<.22f)t.Vegetation=Math.Max(0,t.Vegetation-force*.008f);changed=true;}
                if(w.Kind==WeatherKind.Hurricane&&force>.65f&&World.ResourceNodes[cell].Type==ResourceType.Tree&&World.ResourceNodes[cell].Amount>0
                    &&StableRandom.Hash(World.Seed,cell,(int)(World.Tick/10),8108)%100<3)
                {ExhaustNode(cell,true);World.TerrainRevision++;}
                if(Math.Abs(beforeSnow-t.SnowCover)>.005f)navigationChanged=true;
            }
            if(w.Kind==WeatherKind.Drought&&World.Laws.FireSpread&&World.Tick%50==0)
            {
                // Pick at most one truly dry combustible cell; no forced fire in wet terrain or outside the front.
                int start=(int)(StableRandom.Hash(World.Seed,w.Sequence,(int)(World.Tick/10),8109)%(uint)World.Cells.Length);
                for(int n=0;n<World.Cells.Length;n++)
                {
                    int cell=(start+n)%World.Cells.Length;TileData t=World.Cells[cell];
                    if(WeatherCatalog.Influence(w,cell%World.Width+.5f,cell/World.Width+.5f)<.35f||t.Moisture>=.15f||t.Vegetation<.15f||t.Fire>0||t.WaterDepth>0)continue;
                    World.Cells[cell].Fire=.65f;AddEvent(EventKind.Fire,cell%World.Width+.5f,cell/World.Width+.5f,"Засуха: загорелась сухая растительность");changed=true;break;
                }
            }
            if(changed)World.TerrainRevision++;if(navigationChanged)World.NavigationRevision++;
        }
        private void WeatherLightning()
        {
            WeatherData w=World.Weather;int span=Math.Max(1,(int)(w.Radius*.6f));
            uint roll=StableRandom.Hash(World.Seed,w.Sequence,(int)World.Tick,8110);
            int x=(int)w.CenterX+(int)(roll%(uint)(span*2+1))-span;
            int y=(int)w.CenterY+(int)((roll>>12)%(uint)(span*2+1))-span;
            if(!World.InBounds(x,y)||World.GetWeatherIntensityAt(x,y)<=.15f)return;
            // Do not execute the public command's mid-tick RemoveDead: the spatial index still refers to this tick's unit list.
            for(int cy=Math.Max(0,y-1);cy<=Math.Min(World.Height-1,y+1);cy++)for(int cx=Math.Max(0,x-1);cx<=Math.Min(World.Width-1,x+1);cx++)
                if(DistanceSquared(cx,cy,x,y)<=1&&World.IsTerrainWalkable(cx,cy))World.Cells[World.Index(cx,cy)].Fire=.8f;
            for(int i=0;i<World.Units.Count;i++)
                if(DistanceSquared(World.Units[i].X,World.Units[i].Y,x+.5f,y+.5f)<=4)World.Units[i].Health=Math.Max(0,World.Units[i].Health-85);
            for(int i=0;i<World.Buildings.Count;i++)
                if(DistanceSquared(World.Buildings[i].X,World.Buildings[i].Y,x,y)<=1)World.Buildings[i].Health=Math.Max(0,World.Buildings[i].Health-85);
            AddEvent(EventKind.Lightning,x+.5f,y+.5f,"Гроза: удар молнии");World.TerrainRevision++;
        }
        private void DamageWeatherExposedBuildings()
        {
            WeatherData w=World.Weather;
            for(int i=0;i<World.Buildings.Count;i++)
            {
                BuildingData b=World.Buildings[i];int cell=World.Index(b.X,b.Y);
                float hurricane=w.Kind==WeatherKind.Hurricane?WeatherCatalog.Influence(w,b.X+.5f,b.Y+.5f):0;
                float flood=World.IsTemporaryFlood(cell)?World.Cells[cell].WaterDepth:0;
                b.Health=Math.Max(0,b.Health-hurricane*3.5f-flood*.8f);
                if(w.Kind==WeatherKind.Drought&&b.Type==BuildingType.Farm&&World.Cells[cell].Moisture<.15f)
                    b.CropGrowth=Math.Max(0,b.CropGrowth-.006f*World.GetWeatherIntensityAt(b.X,b.Y));
            }
            if(w.Kind!=WeatherKind.Hurricane)return;
            for(int i=0;i<World.Units.Count;i++)
            {
                UnitData u=World.Units[i];float force=WeatherCatalog.Influence(w,u.X,u.Y);
                u.Health=Math.Max(0,u.Health-force*1.6f);if(IsCivilized(u.Species)&&force>.25f)u.EventStress=Math.Min(1,u.EventStress+force*.015f);
            }
        }
        private void ExpandFlood()
        {
            WeatherData w=World.Weather;int added=0,limit=w.Kind==WeatherKind.Rain?2:w.Kind==WeatherKind.Storm?5:w.Kind==WeatherKind.Hurricane?8:12;
            for(int i=0;i<World.FloodCells.Count;i++)
            {
                FloodCellData existing=World.FloodCells[i];
                if(World.GetWeatherIntensityAt(existing.Cell%World.Width,existing.Cell/World.Width)>.3f)existing.ExpiresTick=World.Tick+250;
            }
            if(World.FloodCells.Count>=WeatherCatalog.MaximumFloodCells)return;
            // Stable centre-out scan: a command inundates its nearby bank before a remote edge of the front.
            int extent=(int)Math.Ceiling(w.Radius)*2;
            for(int yi=0;yi<=extent&&added<limit&&World.FloodCells.Count<WeatherCatalog.MaximumFloodCells;yi++)
            for(int xi=0;xi<=extent&&added<limit&&World.FloodCells.Count<WeatherCatalog.MaximumFloodCells;xi++)
            {
                int x=(int)w.CenterX+(xi%2==0?xi/2:-(xi+1)/2),y=(int)w.CenterY+(yi%2==0?yi/2:-(yi+1)/2);
                if(x<1||y<1||x>=World.Width-1||y>=World.Height-1)continue;
                int cell=World.Index(x,y);TileData t=World.Cells[cell];float force=World.GetWeatherIntensityAt(x,y);
                if(force<.2f||t.WaterDepth>0||t.Terrain==TerrainType.DeepWater||t.Terrain==TerrainType.ShallowWater||t.Terrain==TerrainType.Mountain||t.Terrain==TerrainType.Highlands||t.Terrain==TerrainType.Hills||World.GetFloodAt(x,y)!=null)continue;
                float surface=float.NegativeInfinity;
                for(int d=0;d<4;d++)
                {
                    int nx=x+Dx[d],ny=y+Dy[d],near=World.Index(nx,ny);TileData source=World.Cells[near];if(source.WaterDepth<=0)continue;
                    FloodCellData existing=World.GetFloodAt(nx,ny);
                    float level=existing!=null?existing.WaterSurface:source.Height+.09f*w.Intensity;
                    if(level>surface)surface=level;
                }
                if(surface==float.NegativeInfinity||t.Height>surface)continue;
                var record=new FloodCellData{Cell=cell,ExpiresTick=World.Tick+250,OriginalTerrain=t.Terrain,OriginalWaterKind=t.WaterKind,OriginalDepth=t.WaterDepth,OriginalCurrent=t.CurrentStrength,
                    OriginalHeight=t.Height,OriginalChannelWidth=t.ChannelWidth,OriginalRiver=t.River,AppliedDepth=Math.Min(1.8f,.2f+(surface-t.Height)*8+force*.5f),AppliedCurrent=Math.Min(.7f,.15f+force*.35f),WaterSurface=surface};
                World.FloodCells.Add(record);t.Terrain=TerrainType.ShallowWater;t.WaterKind=WaterKind.River;t.WaterDepth=record.AppliedDepth;t.CurrentStrength=record.AppliedCurrent;t.ChannelWidth=1;t.River=false;t.Fire=0;t.Moisture=1;
                World.Cells[cell]=t;added++;if(World.FloodCells.Count>=WeatherCatalog.MaximumFloodCells)break;
            }
            if(added>0){World.NavigationRevision++;World.TerrainRevision++;}
        }
        private void RecedeFloods()
        {
            for(int i=World.FloodCells.Count-1;i>=0;i--)
            {
                FloodCellData flood=World.FloodCells[i];TileData t=World.Cells[flood.Cell];
                if(!flood.Matches(t)){World.FloodCells.RemoveAt(i);continue;}
                if(World.Tick<flood.ExpiresTick)continue;
                // Restore only the owned overlay. Life, resources, fire, ownership and buildings keep their new state.
                t.Terrain=flood.OriginalTerrain;t.WaterKind=flood.OriginalWaterKind;t.WaterDepth=flood.OriginalDepth;t.CurrentStrength=flood.OriginalCurrent;
                t.ChannelWidth=flood.OriginalChannelWidth;t.River=flood.OriginalRiver;World.Cells[flood.Cell]=t;World.FloodCells.RemoveAt(i);
                World.NavigationRevision++;World.TerrainRevision++;
            }
        }
    }
}
