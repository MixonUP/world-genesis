using System;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    // Stateless presentation: simulation owns the front, accumulated snow, water and all consequences.
    public sealed class WeatherView : IDisposable
    {
        private readonly QuadBatch batch;
        private readonly Texture2D icons;
        public int VisibleQuads { get; private set; }
        public const int MaximumQuads = 1024;
        private static readonly Color Rain = PixelArt.Hex("b5dce8"), Snow = PixelArt.Hex("eff6ec"), Dust = PixelArt.Hex("d8ba7d");
        private static readonly Color DryGround = PixelArt.Hex("b49d65"), WetGround = PixelArt.Hex("416e68");
        private static readonly Color[] BiomeTints = {
            Color.white, Color.white, PixelArt.Hex("9aad6d"), PixelArt.Hex("659175"), PixelArt.Hex("618776"),
            PixelArt.Hex("a8bcb0"), PixelArt.Hex("b7ad66"), PixelArt.Hex("cfb67b"), PixelArt.Hex("517e6a"), PixelArt.Hex("97a09d") };
        public WeatherView(Transform parent, Material sharedMaterial)
        { batch = new QuadBatch("Weather • actual front and flood", parent, sharedMaterial, 75); icons = CreateIcons(); }

        public Sprite CreateIcon(WeatherKind kind)
        { return Sprite.Create(icons, new Rect((int)kind * 32, 0, 32, 32), new Vector2(.5f, .5f), 32); }

        public void Update(WorldState world, Camera camera)
        {
            batch.Clear(); VisibleQuads = 0;
            if (world == null) { batch.Submit(); return; }
            float time = world.Tick * SimulationEngine.TickSeconds;
            float pixel = Mathf.Clamp(camera == null ? .08f : camera.orthographicSize / 360f, .055f, .24f);
            WeatherData weather = world.Weather;
            if (weather != null && weather.Kind != WeatherKind.Clear && weather.RemainingSeconds > 0 && weather.Intensity > 0)
            {
                if (weather.Kind == WeatherKind.Hurricane) DrawCyclone(world, weather, camera, time, pixel);
                DrawFront(world, weather, camera, time, pixel);
            }
            // Flood water can remain after a front clears. Use saved flood cells, never cosmetic flooding.
            if (world.FloodCells != null) for (int i = 0; i < world.FloodCells.Count && VisibleQuads < MaximumQuads; i++)
            {
                FloodCellData flood = world.FloodCells[i];
                if (flood.Cell < 0 || flood.Cell >= world.Cells.Length || !flood.Matches(world.Cells[flood.Cell])) continue;
                int x = flood.Cell % world.Width, y = flood.Cell / world.Width;
                uint hash = PixelArt.Hash(x, y, world.Seed);
                if ((hash & 3) != 0 || !Visible(camera, x + .5f, y + .5f)) continue;
                float slide = Mathf.Sin(time * 2 + hash % 13) * .10f;
                Stroke(new Vector2(x + .5f + slide, y + .36f), .35f, pixel * .75f, 0, new Color(.72f, .91f, .89f, .38f));
            }
            batch.Submit();
        }

        private void DrawFront(WorldState world, WeatherData weather, Camera camera, float time, float pixel)
        {
            if (weather.Kind == WeatherKind.Flood) return; // Flood is visible only where simulation actually raised water.
            bool snow = weather.Kind == WeatherKind.Snow, drought = weather.Kind == WeatherKind.Drought;
            bool strong = weather.Kind == WeatherKind.Storm || weather.Kind == WeatherKind.Hurricane;
            int samples = snow ? 230 : drought ? 100 : strong ? 360 : 270;
            float radius = Mathf.Max(1, weather.Radius), diameter = radius * 2;
            Vector2 wind = new Vector2(weather.DirectionX, weather.DirectionY);
            Vector2 velocity = drought ? wind * 2.2f : new Vector2(wind.x * (strong ? 4 : 1.4f), snow ? -.55f : -8f) + (snow ? wind * .45f : Vector2.zero);
            float angle = Mathf.Atan2(velocity.y, velocity.x);
            for (int i = 0; i < samples && VisibleQuads < MaximumQuads; i++)
            {
                uint hash = PixelArt.Hash(i, weather.Sequence, world.Seed);
                float px = (hash & 65535) / 65535f * diameter, py = (hash >> 16) / 65535f * diameter;
                float x = weather.CenterX - radius + Mathf.Repeat(px + time * velocity.x, diameter);
                float y = weather.CenterY - radius + Mathf.Repeat(py + time * velocity.y, diameter);
                if (!world.InBounds((int)x, (int)y) || x < 0 || y < 0 || !Visible(camera, x, y)) continue;
                float influence = WeatherCatalog.Influence(weather, x, y);
                if (influence < .045f) continue;
                if (snow)
                {
                    x += Mathf.Sin(time * .9f + i) * .18f;
                    // Recheck displaced flakes at the authoritative edge.
                    influence = WeatherCatalog.Influence(weather, x, y); if (influence <= .045f || x < 0 || x >= world.Width) continue;
                    Color color = Snow; color.a = Mathf.Min(.8f, influence * 1.4f);
                    float size = pixel * (1.8f + hash % 3 * .4f);
                    Stroke(new Vector2(x, y), size, pixel, 0, color);
                    Stroke(new Vector2(x, y), size, pixel, Mathf.PI * .5f, color);
                }
                else if (drought)
                {
                    TileData tile = world.Cells[world.Index((int)x, (int)y)];
                    if (tile.WaterDepth > 0 || tile.Moisture > .42f) continue;
                    Color color = Dust; color.a = influence * .42f;
                    Stroke(new Vector2(x, y), pixel * (4 + hash % 4), pixel * .85f, angle, color);
                }
                else
                {
                    Color color = Rain; color.a = Mathf.Min(.7f, influence * (strong ? 1.05f : .85f));
                    Stroke(new Vector2(x, y), pixel * (strong ? 7 : 5), pixel * .7f, angle, color);
                }
            }
        }

        private void DrawCyclone(WorldState world, WeatherData weather, Camera camera, float time, float pixel)
        {
            float radius = Mathf.Max(1, weather.Radius);
            for (int arm = 0; arm < 3; arm++) for (int segment = 0; segment < 44; segment++)
            {
                float progress = (segment + 1) / 45f, r = radius * (.06f + progress * .83f);
                float angle = arm * Mathf.PI * 2 / 3 + time * 1.25f + progress * 5.2f;
                Vector2 point = new Vector2(weather.CenterX + Mathf.Cos(angle) * r, weather.CenterY + Mathf.Sin(angle) * r);
                if (!world.InBounds((int)point.x, (int)point.y) || point.x < 0 || point.y < 0 || !Visible(camera, point.x, point.y)) continue;
                float local = WeatherCatalog.Influence(weather, point.x, point.y);
                Color color = Color.Lerp(Dust, Rain, progress); color.a = local * .47f;
                Stroke(point, Mathf.Max(pixel * 4, r * .13f), pixel * (2.1f - progress), angle + Mathf.PI * .5f, color);
            }
        }
        private void Stroke(Vector2 position, float length, float thickness, float radians, Color color)
        {
            if (VisibleQuads >= MaximumQuads) return;
            batch.OrientedSprite(position, length, thickness, radians, 12, color); VisibleQuads++;
        }
        private static bool Visible(Camera camera, float x, float y)
        {
            if (camera == null) return true;
            Vector3 center = camera.transform.position;
            return Mathf.Abs(y - center.y) < camera.orthographicSize + 1 && Mathf.Abs(x - center.x) < camera.orthographicSize * camera.aspect + 1;
        }
        public static Color TerrainColor(Color original, TileData tile, BiomeType biome)
        {
            if (tile.Terrain == TerrainType.DeepWater || tile.Terrain == TerrainType.ShallowWater) return original;
            Color color = original;
            if (tile.Terrain == TerrainType.Grass || tile.Terrain == TerrainType.Forest || tile.Terrain == TerrainType.Swamp)
                color = Color.Lerp(color, BiomeTints[Mathf.Clamp((int)biome, 0, BiomeTints.Length - 1)], .28f);
            if (tile.Moisture < .26f && tile.Terrain != TerrainType.Snow && tile.Terrain != TerrainType.Mountain)
                color = Color.Lerp(color, DryGround, (.26f - tile.Moisture) * 1.5f);
            else if (tile.Moisture > .68f) color = Color.Lerp(color, WetGround, (tile.Moisture - .68f) * .48f);
            color = Color.Lerp(color, Snow, Mathf.Clamp01(tile.SnowCover) * .88f); color.a = 1; return color;
        }
        public static Color TreeColor(TileData tile)
        {
            Color color = tile.Moisture < .22f ? Color.Lerp(Color.white, Dust, (.22f - tile.Moisture) * 1.8f) : Color.white;
            return Color.Lerp(color, new Color(.81f, .93f, .97f), Mathf.Clamp01(tile.SnowCover) * .8f);
        }

        private static Texture2D CreateIcons()
        {
            var pixels = new Color32[256 * 32];
            Color ink = PixelArt.Hex("243e49"), cloud = PixelArt.Hex("a9c9cb"), gold = PixelArt.Hex("e8c77f");
            void Dot(int s, int x, int y, Color c) { if (x >= 0 && x < 32 && y >= 0 && y < 32) pixels[y * 256 + s * 32 + x] = c; }
            void Rect(int s, int x, int y, int width, int height, Color c) { for (int yy = y; yy < y + height; yy++) for (int xx = x; xx < x + width; xx++) Dot(s, xx, yy, c); }
            void Line(int s, int x0, int y0, int x1, int y1, Color c)
            { int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, error = dx + dy; while (true) { Dot(s, x0, y0, c); if (x0 == x1 && y0 == y1) break; int twice = error * 2; if (twice >= dy) { error += dy; x0 += sx; } if (twice <= dx) { error += dx; y0 += sy; } } }
            void Sun(int s, int cx, int cy, int radius) { for (int y = -radius; y <= radius; y++) for (int x = -radius; x <= radius; x++) if (x*x+y*y <= radius*radius) Dot(s,cx+x,cy+y,gold); for (int a=0;a<8;a++) { float r=a*Mathf.PI/4; Line(s,cx+(int)(Mathf.Cos(r)*(radius+3)),cy+(int)(Mathf.Sin(r)*(radius+3)),cx+(int)(Mathf.Cos(r)*(radius+5)),cy+(int)(Mathf.Sin(r)*(radius+5)),gold); } }
            Sun(0,16,16,6);
            for (int s=1;s<=3;s++) { Rect(s,5,17,22,6,ink); Rect(s,7,19,19,5,cloud); Rect(s,10,23,11,4,cloud); }
            for(int x=8;x<27;x+=7) { Line(1,x,14,x-3,7,Rain); Line(1,x+1,14,x-2,7,Rain); }
            for(int x=8;x<27;x+=8) { Line(2,x,7,x,14,Snow); Line(2,x-3,10,x+3,10,Snow); Dot(2,x-2,8,Snow); Dot(2,x+2,12,Snow); }
            for(int y=5;y<=17;y++) { int x= y>11 ? 16+(y-12)/2 : 14+(y-5)/2; Rect(3,x,y,3,1,gold); }
            Sun(4,20,22,4); Rect(4,4,4,24,5,Dust); Line(4,14,4,17,12,ink); Line(4,17,12,13,17,ink); Line(4,15,8,23,11,ink); Line(4,16,11,8,14,ink);
            for(int arm=0;arm<3;arm++) for(int i=0;i<70;i++) { float r=2+i*.135f,a=arm*Mathf.PI*2/3+i*.11f; int x=16+(int)(Mathf.Cos(a)*r),y=16+(int)(Mathf.Sin(a)*r); Dot(5,x,y,cloud); Dot(5,x+1,y,Rain); }
            Rect(6,4,6,24,12,PixelArt.Hex("487e9c")); for(int y=8;y<19;y+=5) for(int x=5;x<28;x++) Dot(6,x,y+(x/4%2),Rain); Line(6,7,23,24,23,cloud); Line(6,7,23,11,27,cloud); Line(6,24,23,20,19,cloud);
            var texture = new Texture2D(256,32,TextureFormat.RGBA32,false) { name="Original weather icons",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp };
            texture.SetPixels32(pixels); texture.Apply(false,false); return texture;
        }
        public void Dispose() { batch.Dispose(); UnityEngine.Object.Destroy(icons); }
    }
}
