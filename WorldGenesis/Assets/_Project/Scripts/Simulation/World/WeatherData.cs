using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public enum WeatherKind { Clear=0, Rain=1, Snow=2, Storm=3, Drought=4, Hurricane=5, Flood=6 }

    [Serializable]
    public sealed class WeatherData
    {
        public WeatherKind Kind;
        public float CenterX, CenterY, Radius=12, Intensity, DirectionX=1, DirectionY;
        public float RemainingSeconds;
        public long StartedTick, NextTransitionTick;
        public int Sequence;
        public bool Automatic=true;
    }

    [Serializable]
    public sealed class FloodCellData
    {
        public int Cell;
        public long ExpiresTick;
        public TerrainType OriginalTerrain;
        public WaterKind OriginalWaterKind;
        public float OriginalDepth, OriginalCurrent, OriginalHeight;
        public byte OriginalChannelWidth;
        public bool OriginalRiver;
        public float AppliedDepth, AppliedCurrent, WaterSurface;

        public bool Matches(TileData tile)
        {
            return tile.Terrain==TerrainType.ShallowWater && tile.WaterKind==WaterKind.River && !tile.River
                && tile.WaterDepth==AppliedDepth && tile.CurrentStrength==AppliedCurrent && tile.ChannelWidth==1 && tile.Height==OriginalHeight;
        }
    }

#pragma warning disable CS0282 // Save format serializes named fields, never raw struct offsets.
    public partial struct TileData { public float SnowCover; }
#pragma warning restore CS0282

    public static class WeatherCatalog
    {
        public const int MaximumFloodCells=192;
        public static string Name(WeatherKind kind)
        {
            switch(kind)
            {
                case WeatherKind.Rain:return "Дождь";case WeatherKind.Snow:return "Снег";
                case WeatherKind.Storm:return "Гроза";case WeatherKind.Drought:return "Засуха";
                case WeatherKind.Hurricane:return "Ураган";case WeatherKind.Flood:return "Наводнение";
                default:return "Ясно";
            }
        }
        public static float Influence(WeatherData weather,float x,float y)
        {
            if(weather==null||weather.Kind==WeatherKind.Clear||weather.RemainingSeconds<=0||weather.Radius<=0)return 0;
            float dx=x-weather.CenterX,dy=y-weather.CenterY;
            return weather.Intensity*Math.Max(0,1-(float)Math.Sqrt(dx*dx+dy*dy)/weather.Radius);
        }
    }

    public sealed partial class WorldState
    {
        public int WeatherVersion;
        public WeatherData Weather;
        public List<FloodCellData> FloodCells=new List<FloodCellData>();

        public float GetWeatherIntensityAt(int x,int y) { return InBounds(x,y)?WeatherCatalog.Influence(Weather,x+.5f,y+.5f):0; }
        public FloodCellData GetFloodAt(int x,int y)
        {
            if(!InBounds(x,y)||FloodCells==null)return null;int cell=Index(x,y);
            for(int i=0;i<FloodCells.Count;i++)if(FloodCells[i].Cell==cell)return FloodCells[i];return null;
        }
        public bool IsTemporaryFlood(int cell)
        {
            if(cell<0||cell>=Cells.Length||FloodCells==null)return false;
            for(int i=0;i<FloodCells.Count;i++)if(FloodCells[i].Cell==cell)return FloodCells[i].Matches(Cells[cell]);return false;
        }
        public float GetWeatherGrowthMultiplier(int cell)
        {
            if(cell<0||cell>=Cells.Length)return 0;
            TileData t=Cells[cell];if(t.WaterDepth>.6f)return 0;
            float warmth=Math.Max(.12f,1-Math.Abs(t.Temperature-.58f)*1.5f);
            return Math.Max(.05f,Math.Min(1.3f,(.12f+t.Moisture*1.35f)*warmth*(1-.85f*t.SnowCover)));
        }
        public bool ValidateWeather(out string error)
        {
            error=null;
            if(WeatherVersion<0||WeatherVersion>1){error="Unsupported weather version.";return false;}
            for(int i=0;i<Cells.Length;i++)if(!UnitRange(Cells[i].SnowCover)){error="Invalid snow cover.";return false;}
            if(WeatherVersion==0)return true;
            WeatherData w=Weather;
            if(w==null||(int)w.Kind<0||(int)w.Kind>6||!Finite(w.CenterX)||!Finite(w.CenterY)||w.CenterX<0||w.CenterY<0||w.CenterX>=Width||w.CenterY>=Height
                ||!Finite(w.Radius)||w.Radius<1||w.Radius>40||!UnitRange(w.Intensity)||!Finite(w.DirectionX)||!Finite(w.DirectionY)||Math.Abs(w.DirectionX)>1.001f||Math.Abs(w.DirectionY)>1.001f
                ||!Finite(w.RemainingSeconds)||w.RemainingSeconds<0||w.RemainingSeconds>180||w.StartedTick<0||w.StartedTick>Tick||w.NextTransitionTick<0||w.Sequence<0
                ||Math.Abs(w.DirectionX*w.DirectionX+w.DirectionY*w.DirectionY-1)>.002f
                ||(w.Kind==WeatherKind.Clear&&(w.Intensity!=0||w.RemainingSeconds!=0))||(w.Kind!=WeatherKind.Clear&&w.RemainingSeconds<=0))
            {error="Invalid weather front.";return false;}
            if(FloodCells==null||FloodCells.Count>WeatherCatalog.MaximumFloodCells){error="Invalid temporary flood list.";return false;}
            var unique=new HashSet<int>();
            for(int i=0;i<FloodCells.Count;i++)
            {
                FloodCellData f=FloodCells[i];
                if(f==null||f.Cell<0||f.Cell>=Cells.Length||!unique.Add(f.Cell)||f.ExpiresTick<0||(int)f.OriginalTerrain<0||(int)f.OriginalTerrain>11
                    ||(int)f.OriginalWaterKind<0||(int)f.OriginalWaterKind>6||!Finite(f.OriginalDepth)||f.OriginalDepth<0||f.OriginalDepth>12||!UnitRange(f.OriginalCurrent)||f.OriginalChannelWidth>9
                    ||!Finite(f.OriginalHeight)||!Finite(f.WaterSurface)||f.WaterSurface<f.OriginalHeight||!Finite(f.AppliedDepth)||f.AppliedDepth<=0||f.AppliedDepth>2||!UnitRange(f.AppliedCurrent)
                    ||f.OriginalWaterKind!=WaterKind.None||f.OriginalDepth!=0||f.OriginalCurrent!=0||f.OriginalChannelWidth!=0
                    ||f.OriginalTerrain==TerrainType.DeepWater||f.OriginalTerrain==TerrainType.ShallowWater||f.OriginalTerrain==TerrainType.Mountain||f.OriginalTerrain==TerrainType.Hills||f.OriginalTerrain==TerrainType.Highlands||!f.Matches(Cells[f.Cell]))
                {error="Invalid temporary flood record.";return false;}
            }
            return true;
        }
    }
}
