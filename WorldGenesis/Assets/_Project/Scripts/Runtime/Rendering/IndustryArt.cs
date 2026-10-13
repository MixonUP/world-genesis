using System;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    // Original pixel recipes. Buildings contain fixtures and signs, never painted stock counts.
    // Inventory, equipped goods and active workshop effects are drawn separately from saved state.
    public static class IndustryArt
    {
        public const int Size = 512, BuildingFrame = 64, ItemFrame = 32, BuildingCount = 7, ItemCount = 27;
        private const int ItemY = 320;
        private static readonly Color Ink = PixelArt.Hex("303b3e"), DarkWood = PixelArt.Hex("594b3c"), Wood = PixelArt.Hex("9d7951"), WoodLight = PixelArt.Hex("d1b380");
        private static readonly Color Stone = PixelArt.Hex("88948a"), StoneLight = PixelArt.Hex("c0c5ae"), StoneDark = PixelArt.Hex("586c6e");
        private static readonly Color Shadow = new Color(.055f, .10f, .12f, .34f);

        public static bool SupportsBuilding(BuildingType type) { int n = (int)type - (int)BuildingType.Smelter; return n >= 0 && n < BuildingCount; }
        public static bool SupportsItem(ItemType item) { int n = (int)item - (int)ItemType.Charcoal; return n >= 0 && n < ItemCount; }
        public static Rect UvBuilding(BuildingType type, int level)
        {
            if (!SupportsBuilding(type)) return default(Rect);
            int frame = ((int)type - (int)BuildingType.Smelter) * 5 + Mathf.Clamp(level, 1, 5) - 1;
            return new Rect((frame % 8 * 64 + .25f) / Size, (frame / 8 * 64 + .25f) / Size, 63.5f / Size, 63.5f / Size);
        }
        public static Rect UvItem(ItemType item)
        {
            if (!SupportsItem(item)) return default(Rect);
            int frame = (int)item - (int)ItemType.Charcoal;
            return new Rect((frame % 16 * 32 + .25f) / Size, (ItemY + frame / 16 * 32 + .25f) / Size, 31.5f / Size, 31.5f / Size);
        }
        public static float BuildingSize(BuildingData building) { return 3.0f + Mathf.Clamp(building.Level - 1, 0, 4) * .1f; }
        public static void DrawBuilding(QuadBatch batch, BuildingData building)
        {
            if (building == null || building.Health <= 0 || !SupportsBuilding(building.Type)) return;
            float size = BuildingSize(building);
            batch.Add(building.X + .5f - size * .5f, building.Y - .15f, size, size, UvBuilding(building.Type, building.Level), Color.white);
        }
        // Position follows the same bottom-centre convention as the existing cargo sprites.
        public static bool DrawItem(QuadBatch batch, ItemType item, Vector2 position, float size, Color tint)
        {
            if (!SupportsItem(item)) return false;
            batch.Add(position.x - size * .5f, position.y, size, size, UvItem(item), tint); return true;
        }

        public static void DrawActivity(QuadBatch effects, WorldState world, BuildingData building)
        {
            if (building == null || !SupportsBuilding(building.Type) || !IndustryCatalog.IsWorking(world, building)) return;
            float pixel = BuildingSize(building) / BuildingFrame;
            Vector2 origin = new Vector2(building.X + .5f - BuildingSize(building) * .5f, building.Y - .15f);
            float clock = world.Tick * SimulationEngine.TickSeconds;
            bool furnace = building.Type == BuildingType.Smelter || building.Type == BuildingType.Smithy;
            if (furnace)
            {
                Vector2 mouth = origin + new Vector2(building.Type == BuildingType.Smelter ? 47 : 49, building.Type == BuildingType.Smelter ? 21 : 19) * pixel;
                float glow = .75f + .25f * Mathf.Sin(clock * 6 + building.Id);
                effects.Sprite(mouth.x, mouth.y, pixel * 8, pixel * 5, 12, new Color(.86f, .36f, .14f, .8f));
                effects.Sprite(mouth.x - pixel, mouth.y + pixel, pixel * 5, pixel * (2 + glow), 12, new Color(1, .70f, .32f, .9f));
                Vector2 chimney = origin + new Vector2(building.Type == BuildingType.Smelter ? 47 : 49, 61) * pixel;
                for (int puff = 0; puff < 3; puff++)
                {
                    float phase = Mathf.Repeat(clock * .48f + puff / 3f + (building.Id % 11) * .071f, 1);
                    Vector2 position = chimney + new Vector2(phase * .22f + Mathf.Sin(phase * 5 + puff) * .055f, phase * .72f);
                    float size = .14f + phase * .24f;
                    Color smoke = new Color(.43f, .46f, .43f, .33f * (1 - phase));
                    effects.Sprite(position.x, position.y, size, size * .55f, 12, smoke);
                    effects.Sprite(position.x - size * .2f, position.y + size * .2f, size * .55f, size * .48f, 12, smoke);
                }
            }
            if (building.Type == BuildingType.Smithy || building.Type == BuildingType.Mint)
            {
                Vector2 at = origin + (building.Type == BuildingType.Mint ? new Vector2(54, 24) : new Vector2(26, 27)) * pixel;
                float phase = Mathf.Repeat(clock * 1.25f + building.Id * .17f, 1);
                if (phase < .30f)
                    for (int i = 0; i < 3; i++)
                    {
                        float flight = phase / .30f;
                        Vector2 spark = at + new Vector2((i - 1) * flight * .24f, Mathf.Sin(flight * Mathf.PI) * .20f + i * .02f);
                        effects.Sprite(spark.x, spark.y, .042f, .042f, 12, new Color(1, .77f, .32f, 1 - flight));
                    }
            }
            else if (building.Type == BuildingType.Weaver || building.Type == BuildingType.Tailor)
            {
                Vector2 at = origin + (building.Type == BuildingType.Weaver ? new Vector2(27, 26) : new Vector2(35, 19)) * pixel;
                at.x += Mathf.Sin(clock * 5 + building.Id) * .21f;
                effects.Sprite(at.x, at.y, .17f, .055f, 12, WoodLight);
                effects.Sprite(at.x, at.y + .02f, .075f, .04f, 12, StoneLight);
            }
            else if (building.Type == BuildingType.Carpentry)
            {
                Vector2 at = origin + new Vector2(31, 25) * pixel;
                float saw = Mathf.Sin(clock * 7 + building.Id) * .13f;
                effects.OrientedSprite(at + Vector2.right * saw, .36f, .055f, .30f, 12, StoneLight);
                for (int i = 0; i < 2; i++)
                {
                    float phase = Mathf.Repeat(clock + i * .5f + building.Id * .13f, 1);
                    effects.Sprite(at.x + phase * .18f * (i == 0 ? -1 : 1), at.y - phase * .23f, .035f, .035f, 12, new Color(.79f, .62f, .37f, 1 - phase));
                }
            }
            else if (building.Type == BuildingType.Tannery)
            {
                Vector2 vat = origin + new Vector2(28, 21) * pixel;
                float slide = Mathf.Sin(clock * 2 + building.Id) * .04f;
                effects.Sprite(vat.x + slide, vat.y, .25f, .025f, 12, new Color(.76f, .65f, .47f, .55f));
            }
        }

        // Small hem/belt details show items actually equipped; role clothing and identity remain
        // untouched. Weapons and armour are never issued or shown by this civilian adapter.
        public static void DrawEquipment(QuadBatch effects, WorldState world, UnitData unit, Vector2 position, Vector2 facing, bool mounted = false)
        {
            if (unit == null || unit.Health <= 0 || mounted || !CitizenSprites.IsCivilized(unit.Species)) return;
            float scale = OriginalCitizenAtlas.RaceScale(unit.Species), pixel = scale / OriginalCitizenAtlas.PixelsPerUnit;
            CitizenWaterPresentation.State water = CitizenWaterPresentation.Resolve(world, unit, mounted);
            float waterline = water.SurfacePosition.y + position.y - unit.Y - water.VerticalOffset;
            bool side = Mathf.Abs(facing.x) > .5f;
            float width = (unit.IsChild ? (side ? 3 : 5) : (side ? 4 : 7)) * pixel;
            if (IndustryCatalog.IsClothes(unit.Clothes))
            {
                // Children's waist is three source pixels lower, matching their authored body.
                float hem = (unit.IsChild ? 4 : 7) * pixel;
                Color cloth = unit.Clothes == ItemType.LinenClothes ? PixelArt.Hex("c4d0b7") : PixelArt.Hex("ab8155");
                Detail(effects, new Rect(position.x - width * .5f, position.y + hem, width, 3 * pixel), cloth, water.Wet, waterline);
                Detail(effects, new Rect(position.x - width * .5f, position.y + hem + 2 * pixel, width, pixel), DarkWood, water.Wet, waterline);
                if (!side && facing.y < .5f) Detail(effects, new Rect(position.x - pixel, position.y + hem + 2 * pixel, 2 * pixel, pixel), WoodLight, water.Wet, waterline);
            }
            if (!unit.IsChild && IndustryCatalog.IsTool(unit.WorkTool))
            {
                float x = position.x + (facing.x < 0 ? -1 : 1) * (side ? 2.5f : 4.5f) * pixel;
                Color metal = unit.WorkTool == ItemType.BronzeTools ? PixelArt.Hex("b9a465") : StoneLight;
                Detail(effects, new Rect(x - pixel * .5f, position.y + 5 * pixel, pixel, 5 * pixel), Wood, water.Wet, waterline);
                Detail(effects, new Rect(x - pixel * 1.5f, position.y + 9 * pixel, 3 * pixel, 2 * pixel), metal, water.Wet, waterline);
            }
        }
        private static void Detail(QuadBatch batch, Rect shape, Color color, bool wet, float waterline)
        {
            Rect uv = PixelArt.Uv(12);
            if (!wet || CitizenWaterPresentation.Clip(ref shape, ref uv, waterline)) batch.Add(shape.x, shape.y, shape.width, shape.height, uv, color);
        }

        public static Texture2D CreateAtlas(bool keepReadable = false)
        {
            Color32[] pixels = new Color32[Size * Size];
            for (int kind = 0; kind < BuildingCount; kind++) for (int level = 1; level <= 5; level++)
            {
                int frame = kind * 5 + level - 1;
                PaintBuilding(new Painter(pixels, frame % 8 * 64, frame / 8 * 64, 64), (BuildingType)((int)BuildingType.Smelter + kind), level);
            }
            for (int item = 0; item < ItemCount; item++)
                PaintItem(new Painter(pixels, item % 16 * 32, ItemY + item / 16 * 32, 32), (ItemType)((int)ItemType.Charcoal + item));
            Texture2D atlas = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            { name = "World Genesis • original industry workshops and goods", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            atlas.SetPixels32(pixels); atlas.Apply(false, !keepReadable); return atlas;
        }

        private static void PaintBuilding(Painter p, BuildingType type, int level)
        {
            Color wall = level < 3 ? PixelArt.Hex("b3a582") : level == 3 ? PixelArt.Hex("b3ab91") : PixelArt.Hex("d0cbb0");
            Color roof = type == BuildingType.Smithy ? PixelArt.Hex("697c82") : type == BuildingType.Tailor ? PixelArt.Hex("867395") : type == BuildingType.Weaver ? PixelArt.Hex("727e94") : type == BuildingType.Carpentry ? PixelArt.Hex("6e8975") : type == BuildingType.Mint ? PixelArt.Hex("b69a56") : PixelArt.Hex("ac775b");
            if (level == 1) roof = Color.Lerp(roof, Wood, .48f);
            if (level == 5) roof = Color.Lerp(roof, PixelArt.Hex("7da9a2"), .25f);
            p.Ellipse(32, 7, 29, 5, Shadow);
            p.Polygon(level >= 3 ? StoneDark : DarkWood, 6,8, 50,8, 60,15, 16,15);
            p.Rect(7,9,44,4,level >= 3 ? Stone : Wood);
            p.Line(8,12,49,12,level >= 3 ? StoneLight : WoodLight);
            if (level >= 2) for (int x = 8; x < 50; x += 8) p.Rect(x,9,1,3,StoneDark);
            if (type == BuildingType.Smelter)
            {
                Smelter(p, level, wall, roof);
            }
            else
            {
                int width = type == BuildingType.Carpentry ? 43 : 40;
                int height = type == BuildingType.Mint ? 28 : 23;
                House(p, 9, 12, width, height, wall, roof, level);
                switch (type)
                {
                    case BuildingType.Smithy:
                        Chimney(p, 44, 30, 11, 28, level);
                        p.Rect(12,14,29,16,Ink); p.Rect(14,14,25,13,PixelArt.Hex("504841"));
                        p.Rect(13,14,3,18,Wood); p.Rect(39,14,3,18,Wood);
                        p.Rect(17,12,13,6,DarkWood); p.Rect(17,17,13,2,WoodLight);
                        Anvil(p, 25, 17, 1);
                        p.Rect(44,16,9,8,StoneDark); p.Rect(46,17,6,5,Ink); // Cold forge mouth; fire belongs to DrawActivity.
                        p.Line(34,19,34,27,WoodLight); p.Rect(31,26,7,3,StoneLight);
                        Sign(p, 17, 36, 0); break;
                    case BuildingType.Tannery:
                        p.Rect(10,13,10,17,DarkWood); p.Rect(12,14,7,15,Wood); p.Dot(17,21,WoodLight);
                        p.Rect(37,13,3,26,DarkWood); p.Rect(57,13,3,26,DarkWood); p.Rect(37,37,23,3,WoodLight);
                        p.Line(38,14,57,36,Wood); p.Line(38,36,57,14,Wood);
                        // A recognisable stretched-hide trade sign, not a simulated hide pile.
                        p.Polygon(PixelArt.Hex("c4a071"), 44,20, 42,23, 44,27, 43,32, 48,30, 53,32, 52,27, 54,23, 51,20, 48,22);
                        p.Line(48,23,48,29,WoodLight);
                        Vat(p, 28, 13, 8, PixelArt.Hex("88694e"));
                        Sign(p, 19, 36, 1); break;
                    case BuildingType.Weaver:
                        p.Rect(11,14,29,15,Ink); p.Rect(13,14,25,13,PixelArt.Hex("676454"));
                        p.Rect(16,12,3,22,DarkWood); p.Rect(35,12,3,22,DarkWood); p.Rect(15,32,25,3,WoodLight);
                        for (int x = 20; x < 35; x += 2) p.Line(x,16,x,32,PixelArt.Hex("d4ccab"));
                        p.Rect(18,17,19,6,PixelArt.Hex("91a69c")); p.Rect(18,17,19,1,PixelArt.Hex("d8d7b6"));
                        p.Rect(17,13,23,3,Wood); p.Rect(44,15,3,13,DarkWood); p.Rect(42,28,7,2,WoodLight);
                        p.Ellipse(45,22,4,5,PixelArt.Hex("cbc6a3")); p.Line(43,21,46,25,StoneLight);
                        Sign(p, 21, 40, 2); break;
                    case BuildingType.Tailor:
                        p.Rect(12,13,10,18,DarkWood); p.Rect(14,14,7,15,PixelArt.Hex("75644e"));
                        Window(p, 28, 20, 15, 11);
                        p.Rect(25,17,21,3,WoodLight);
                        p.Polygon(PixelArt.Hex("91ad9d"), 24,30, 28,35, 49,35, 45,30);
                        for (int x = 25; x < 46; x += 5) p.Rect(x,28,3,3,PixelArt.Hex("d5d3b4"));
                        Sign(p, 19, 41, 3); break;
                    case BuildingType.Carpentry:
                        p.Rect(12,13,37,15,DarkWood); p.Rect(15,14,31,12,PixelArt.Hex("636451"));
                        p.Rect(16,11,3,10,DarkWood); p.Rect(42,11,3,10,DarkWood); p.Rect(14,20,33,4,WoodLight);
                        p.Line(16,23,46,23,PixelArt.Hex("e0c594"));
                        p.Rect(20,24,9,3,Wood); p.Line(23,27,34,32,StoneLight); p.Line(23,26,34,31,StoneDark);
                        for (int x = 23; x < 34; x += 3) p.Dot(x,25+(x-23)/2,Ink);
                        p.Rect(51,12,3,18,Wood); p.Rect(54,12,3,25,DarkWood);
                        Sign(p, 20, 40, 4); break;
                    case BuildingType.Mint:
                        p.Rect(9,12,5,26,Stone); p.Rect(45,12,5,26,Stone);
                        for (int y = 14; y < 37; y += 6) { p.Line(9,y,14,y,StoneLight); p.Line(45,y,49,y,StoneLight); }
                        p.Rect(23,13,14,20,StoneDark); p.Rect(25,14,10,18,DarkWood); p.Line(30,14,30,31,StoneLight);
                        p.Rect(26,22,8,2,PixelArt.Hex("d7b969"));
                        Window(p,15,25,5,9); Window(p,39,25,5,9);
                        p.Ellipse(30,42,7,7,DarkWood); p.Ellipse(30,43,5,5,PixelArt.Hex("d6b364")); p.Line(29,40,29,46,PixelArt.Hex("f0d998")); p.Line(27,43,32,43,Wood);
                        p.Rect(51,13,7,5,StoneDark); p.Rect(53,18,3,15,Stone); p.Rect(49,29,10,3,StoneLight);
                        break;
                }
            }
            // Later grades gain real architectural details. These are presentation variants;
            // simulation decides which levels can actually be constructed.
            if (level >= 3) { p.Rect(7,12,4,10,Stone); p.Rect(8,18,3,2,StoneLight); }
            if (level >= 4) { p.Rect(48,34,3,7,DarkWood); p.Rect(49,37,5,2,WoodLight); p.Rect(52,32,4,5,PixelArt.Hex("d8c089")); p.Rect(53,33,2,3,PixelArt.Hex("ede0b0")); }
            if (level == 5) { p.Line(15,53,28,59,StoneLight); p.Line(28,59,42,53,StoneLight); p.Rect(27,58,3,3,PixelArt.Hex("e0c47d")); }
        }

        private static void House(Painter p, int x, int y, int width, int height, Color wall, Color roof, int level)
        {
            p.Rect(x,y,width,height,DarkWood); p.Rect(x+2,y+2,width-4,height-3,wall);
            p.Polygon(Color.Lerp(wall,StoneDark,.35f), x+width,y, x+width+8,y+5, x+width+8,y+height+5, x+width,y+height);
            p.Line(x+width+1,y+height-2,x+width+6,y+height+1,StoneLight);
            for (int xx = x + 2; xx < x + width; xx += level >= 3 ? 12 : 10) p.Rect(xx,y+2,2,height-3,level >= 4 ? Stone : Wood);
            p.Rect(x,y+height-3,width,3,DarkWood);
            int top = y+height;
            p.Polygon(Color.Lerp(roof,Ink,.25f), x-4,top-1, x+9,top+14, x+width-5,top+14, x+width+4,top-1);
            p.Polygon(roof, x-3,top, x+10,top+13, x+width-6,top+13, x+width+2,top);
            for (int row = 0; row < 4; row++)
            {
                int yy=top+row*3, left=x-2+row*3, right=x+width+1-row*2;
                p.Line(left,yy,right,yy,Color.Lerp(roof,WoodLight,.38f));
                if (level >= 3) for (int xx=left+3+(row%2)*4;xx<right;xx+=8) p.Line(xx,yy+1,xx-2,yy+3,Color.Lerp(roof,Ink,.25f));
            }
            p.Line(x+9,top+14,x+width-5,top+14,WoodLight); p.Line(x-3,top-1,x+width+3,top-1,DarkWood);
            if(level>=2) { p.Rect(x+3,y,3,5,StoneLight);p.Rect(x+width-6,y,3,5,StoneLight); }
        }
        private static void Smelter(Painter p,int level,Color wall,Color roof)
        {
            House(p,8,12,27,19,wall,roof,level);
            p.Rect(11,13,20,11,DarkWood);p.Rect(14,14,14,9,PixelArt.Hex("55584d"));
            p.Rect(39,16,16,21,StoneDark);p.Ellipse(47,35,8,7,StoneDark);p.Rect(40,17,14,18,level<3?PixelArt.Hex("9d6b52"):Stone);
            p.Ellipse(47,34,7,6,level<3?PixelArt.Hex("b27d5c"):Stone);p.Rect(43,36,8,21,StoneDark);p.Rect(44,36,6,20,Stone);
            for(int y=39;y<56;y+=5) {p.Line(44,y,49,y,StoneLight);p.Dot(y%2==0?46:48,y-2,StoneDark);}
            p.Rect(42,56,10,3,StoneLight);p.Rect(44,58,6,2,Ink);
            p.Rect(40,15,14,4,StoneLight);p.Ellipse(47,23,5,7,Ink);p.Rect(42,17,11,7,Ink);
            p.Rect(42,17,11,2,StoneDark);
            for(int y=28;y<35;y+=4)p.Line(40,y,53,y,level<3?WoodLight:StoneLight);
            p.Polygon(PixelArt.Hex("977a55"), 26,18, 34,16, 38,23, 31,25);p.Line(26,18,31,25,WoodLight);p.Line(34,18,39,22,StoneDark);
            p.Rect(17,10,14,3,StoneDark);p.Rect(19,13,10,3,Stone);Sign(p,18,34,5);
        }
        private static void Chimney(Painter p,int x,int y,int w,int h,int level)
        { p.Rect(x,y,w,h,StoneDark);p.Rect(x+1,y+1,w-3,h-2,level<3?PixelArt.Hex("997761"):Stone);for(int yy=y+4;yy<y+h;yy+=5){p.Line(x+1,yy,x+w-3,yy,StoneLight);p.Rect(x+2+(yy/5%2)*3,yy-3,1,3,StoneDark);}p.Rect(x-1,y+h-1,w+2,3,StoneLight);p.Rect(x+1,y+h+2,w-2,2,Ink); }
        private static void Window(Painter p,int x,int y,int w,int h)
        {p.Rect(x,y,w,h,DarkWood);p.Rect(x+1,y+1,w-2,h-2,PixelArt.Hex("7caaa5"));p.Rect(x+1,y+1,w-2,2,PixelArt.Hex("adc9b8"));p.Line(x+w/2,y+1,x+w/2,y+h-2,WoodLight);p.Line(x+1,y+h/2,x+w-2,y+h/2,WoodLight);}
        private static void Vat(Painter p,int x,int y,int radius,Color liquid)
        {p.Rect(x-radius,y,radius*2,7,Wood);p.Ellipse(x,y+7,radius,3,DarkWood);p.Ellipse(x,y+8,radius-2,2,liquid);p.Line(x-radius,y+2,x+radius,y+2,StoneDark);p.Rect(x-radius+2,y+1,2,5,WoodLight);}
        private static void Anvil(Painter p,int x,int y,int scale)
        {p.Polygon(StoneLight, x-8*scale,y+8*scale, x+8*scale,y+8*scale, x+12*scale,y+10*scale, x+5*scale,y+11*scale, x-10*scale,y+11*scale);p.Rect(x-4*scale,y+3*scale,8*scale,5*scale,StoneDark);p.Rect(x-7*scale,y,14*scale,3*scale,Stone);p.Line(x-8*scale,y+9*scale,x+7*scale,y+9*scale,Stone);}
        private static void Sign(Painter p,int x,int y,int symbol)
        {
            p.Rect(x-7,y-3,15,10,DarkWood);p.Rect(x-6,y-2,13,8,Wood);p.Line(x-6,y+5,x+6,y+5,WoodLight);
            Color mark=PixelArt.Hex("e2d3a6");
            if(symbol==0){p.Line(x-2,y-1,x+2,y+4,mark);p.Rect(x-2,y+3,6,2,mark);}
            else if(symbol==1){p.Polygon(mark, x-3,y-1,x-4,y+3,x-1,y+4,x+2,y+3,x+4,y+4,x+3,y-1);}
            else if(symbol==2){p.Rect(x-3,y-1,7,5,mark);for(int xx=x-2;xx<x+4;xx+=2)p.Line(xx,y-1,xx,y+3,Wood);}
            else if(symbol==3){p.Line(x-3,y-1,x+3,y+4,mark);p.Line(x-3,y+4,x+3,y-1,mark);p.Ellipse(x-3,y,1,1,mark);}
            else if(symbol==4){p.Line(x-4,y,x+4,y+3,mark);p.Line(x-3,y-1,x+4,y+2,mark);}
            else {p.Polygon(mark, x-4,y,x-2,y+3,x+4,y+3,x+3,y);}
        }

        private static void PaintItem(Painter p,ItemType item)
        {
            Color pale=PixelArt.Hex("ded7b6"), silver=PixelArt.Hex("b7c8c5"), copper=PixelArt.Hex("c38e61"), gold=PixelArt.Hex("e2bf68");
            p.Ellipse(16,6,12,3,Shadow);
            switch(item)
            {
                case ItemType.Charcoal:
                    p.Polygon(Ink, 5,8,3,15,8,23,15,26,21,22,28,17,27,9,17,6);p.Polygon(PixelArt.Hex("53605c"), 7,10,5,16,10,22,16,21,15,10);p.Polygon(PixelArt.Hex("394c4c"), 17,11,18,23,25,18,25,11);p.Line(9,20,13,18,Stone);p.Line(18,9,22,11,StoneDark);break;
                case ItemType.IronIngot: case ItemType.CopperIngot: case ItemType.TinIngot: case ItemType.BronzeIngot: case ItemType.GoldIngot: case ItemType.SilverIngot:
                    Color metal=item==ItemType.GoldIngot?gold:item==ItemType.CopperIngot?copper:item==ItemType.BronzeIngot?PixelArt.Hex("b9a465"):item==ItemType.TinIngot?PixelArt.Hex("92b8bf"):item==ItemType.SilverIngot?PixelArt.Hex("d9e3db"):silver;
                    Ingot(p,7,9,19,9,metal);Ingot(p,9,18,16,7,Color.Lerp(metal,Color.white,.08f));
                    int stamp=(int)item-(int)ItemType.IronIngot;for(int i=0;i<=stamp%3;i++)p.Rect(13+i*3,20,1,2,Color.Lerp(metal,Ink,.48f));break;
                case ItemType.IronTools: case ItemType.BronzeTools:
                    Color tool=item==ItemType.BronzeTools?PixelArt.Hex("b9a465"):silver;
                    p.Line(7,7,22,25,DarkWood,3);p.Line(7,7,22,25,WoodLight,1);p.Polygon(tool, 17,25,21,29,28,22,26,19);p.Line(20,27,27,21,pale);
                    p.Line(24,7,10,24,DarkWood,3);p.Line(24,7,10,24,Wood,1);p.Polygon(tool, 5,23,10,27,17,25,18,23,10,25,6,21);break;
                case ItemType.Sword:
                    p.Line(8,7,23,24,Ink,4);p.Polygon(silver, 11,13,21,25,27,29,25,22,15,10);p.Line(13,14,25,27,pale);p.Line(7,16,17,7,copper,2);p.Line(7,6,10,10,Wood,2);p.Ellipse(6,5,2,2,gold);break;
                case ItemType.Spear:
                    p.Line(6,5,23,24,DarkWood,3);p.Line(6,5,23,24,WoodLight,1);p.Polygon(silver, 18,24,26,30,27,20,23,18);p.Line(23,22,26,28,pale);p.Line(19,18,23,22,Wood,2);break;
                case ItemType.Armor:
                    p.Polygon(Ink, 9,26,13,28,18,28,23,26,28,20,24,16,23,7,8,7,7,16,3,20);p.Polygon(silver, 9,25,13,26,16,22,20,26,23,25,26,20,22,17,21,9,10,9,9,17,6,20);p.Rect(13,10,5,12,Stone);p.Line(10,20,13,18,pale);p.Line(20,20,17,18,pale);p.Rect(10,9,12,3,copper);p.Rect(14,9,3,3,gold);break;
                case ItemType.Leather:
                    p.Polygon(DarkWood, 7,7,4,13,7,18,5,24,10,27,16,23,22,27,27,24,25,18,28,13,24,7,18,9,12,8);p.Polygon(PixelArt.Hex("b98658"), 8,9,7,15,9,18,8,23,11,24,16,21,22,24,24,22,22,18,25,14,22,9,16,11);p.Line(14,11,18,21,WoodLight);for(int y=12;y<21;y+=3)p.Dot(11,y,DarkWood);break;
                case ItemType.LinenCloth:
                    p.Rect(5,8,23,14,DarkWood);p.Rect(6,9,20,12,PixelArt.Hex("b8bdab"));p.Polygon(pale, 8,21,11,27,28,27,25,21);p.Ellipse(8,15,4,7,pale);p.Ellipse(8,15,2,4,Wood);p.Line(14,12,24,12,StoneLight);p.Line(16,16,24,16,pale);p.Line(19,22,18,26,Stone);break;
                case ItemType.LeatherClothes: case ItemType.LinenClothes:
                    Color fabric=item==ItemType.LinenClothes?PixelArt.Hex("b9c9b5"):PixelArt.Hex("ad8054");
                    p.Polygon(Ink, 9,26,12,28,20,28,23,26,29,21,25,16,23,18,23,6,8,6,8,18,6,16,2,21);
                    p.Polygon(fabric, 10,25,13,26,16,23,19,26,22,25,27,21,25,18,21,21,21,8,10,8,10,21,6,18,4,21);
                    p.Line(15,23,15,10,Color.Lerp(fabric,pale,.45f));p.Rect(9,11,14,3,DarkWood);p.Rect(14,11,4,3,copper);p.Rect(11,7,9,1,Color.Lerp(fabric,pale,.4f));break;
                case ItemType.Furniture:
                    p.Rect(6,5,3,19,DarkWood);p.Rect(20,5,3,19,DarkWood);p.Rect(7,20,15,9,Wood);p.Line(8,27,20,27,WoodLight);p.Rect(7,18,15,4,WoodLight);p.Polygon(Wood, 7,18,22,18,27,12,12,12);p.Line(12,12,26,12,WoodLight);p.Rect(12,5,3,7,DarkWood);p.Rect(24,4,3,9,DarkWood);p.Rect(11,23,3,4,DarkWood);p.Rect(16,23,3,4,DarkWood);break;
                case ItemType.Bow:
                    p.Line(8,5,22,27,pale);p.Line(8,5,16,8,DarkWood,3);p.Line(16,8,23,16,DarkWood,3);p.Line(23,16,24,22,DarkWood,3);p.Line(24,22,22,27,DarkWood,3);p.Line(9,6,17,10,WoodLight);p.Line(17,10,22,17,WoodLight);p.Line(22,17,23,23,WoodLight);p.Line(7,12,27,22,Wood);p.Polygon(silver, 24,23,30,24,28,19);break;
                case ItemType.CopperCoin: case ItemType.SilverCoin: case ItemType.GoldCoin:
                    Color coin=item==ItemType.CopperCoin?copper:item==ItemType.SilverCoin?silver:gold;
                    for(int row=0;row<3;row++){p.Ellipse(12,8+row*3,8,3,Color.Lerp(coin,DarkWood,.3f));p.Line(5,9+row*3,18,9+row*3,coin);}
                    p.Ellipse(22,19,7,8,Color.Lerp(coin,DarkWood,.25f));p.Ellipse(21,20,5,6,coin);p.Line(19,17,19,23,Color.Lerp(coin,Color.white,.4f));p.Line(21,18,24,21,DarkWood);p.Line(21,22,24,21,DarkWood);break;
                case ItemType.Rope:
                    for(int ring=0;ring<3;ring++){p.Ellipse(16,11+ring*5,11-ring,5,DarkWood);p.Ellipse(16,12+ring*5,10-ring,4,WoodLight);p.Ellipse(16,13+ring*5,6-ring,2,Color.clear);}
                    p.Line(22,8,26,5,WoodLight,2);for(int x=8;x<25;x+=4)p.Dot(x,12,Wood);break;
                case ItemType.Ballista: case ItemType.Catapult: case ItemType.Trebuchet:
                    // Bundled chassis, wheels, arm and a stamped assembly label: a cargo kit,
                    // never a deployed siege engine with invented firing behaviour.
                    p.Rect(4,7,24,13,DarkWood);p.Rect(5,8,22,11,Wood);p.Line(6,16,25,16,WoodLight);p.Line(6,11,25,11,WoodLight);p.Rect(10,7,2,13,StoneDark);p.Rect(22,7,2,13,StoneDark);
                    p.Ellipse(8,21,5,5,DarkWood);p.Ellipse(8,21,3,3,WoodLight);p.Line(8,18,8,24,Wood);p.Line(5,21,11,21,Wood);
                    p.Line(13,22,27,28,WoodLight,3);p.Line(14,23,26,28,Wood);
                    p.Rect(14,10,6,7,pale);
                    if(item==ItemType.Ballista){p.Line(15,13,18,13,DarkWood);p.Line(17,11,17,16,DarkWood);p.Line(15,12,15,15,DarkWood);p.Line(19,12,19,15,DarkWood);}
                    else if(item==ItemType.Catapult){p.Line(15,11,18,15,DarkWood);p.Ellipse(18,15,1,1,DarkWood);p.Rect(15,11,4,1,DarkWood);}
                    else{p.Line(15,11,17,15,DarkWood);p.Line(17,15,19,11,DarkWood);p.Line(15,16,19,14,DarkWood);}
                    break;
                case ItemType.BallistaBolts:
                    for(int i=0;i<3;i++){int shift=i*5;p.Line(5+shift,5,13+shift,24,DarkWood,3);p.Line(5+shift,5,13+shift,24,WoodLight);p.Polygon(silver,10+shift,22,13+shift,29,16+shift,23);p.Line(4+shift,7,8+shift,8,StoneLight);}
                    p.Line(8,13,25,14,Wood,2);break;
                case ItemType.StoneShot:
                    p.Ellipse(10,12,7,7,StoneDark);p.Ellipse(21,12,7,7,StoneDark);p.Ellipse(16,22,7,7,StoneDark);p.Ellipse(9,14,5,4,Stone);p.Ellipse(20,14,5,4,Stone);p.Ellipse(15,24,5,4,Stone);p.Line(8,17,11,17,StoneLight);p.Line(15,27,18,27,StoneLight);break;
            }
        }
        private static void Ingot(Painter p,int x,int y,int w,int h,Color color)
        {p.Polygon(Color.Lerp(color,Ink,.35f), x,y,x-2,y+h-3,x+2,y+h,x+w-3,y+h,x+w,y+3,x+w-2,y);p.Polygon(color, x,y+h-3,x+3,y+h-1,x+w-4,y+h-1,x+w-1,y+3,x+2,y+3);p.Line(x+2,y+h-2,x+w-5,y+h-2,Color.Lerp(color,Color.white,.38f));p.Line(x+2,y+2,x+w-2,y+2,Color.Lerp(color,Ink,.2f));}

        private sealed class Painter
        {
            private readonly Color32[] pixels;private readonly int ox,oy,frame;
            public Painter(Color32[] target,int x,int y,int dimension){pixels=target;ox=x;oy=y;frame=dimension;}
            public void Dot(int x,int y,Color color){if((uint)x<(uint)frame&&(uint)y<(uint)frame)pixels[(oy+y)*Size+ox+x]=color;}
            public void Rect(int x,int y,int w,int h,Color c){for(int yy=y;yy<y+h;yy++)for(int xx=x;xx<x+w;xx++)Dot(xx,yy,c);}
            public void Ellipse(int cx,int cy,int rx,int ry,Color c){for(int y=-ry;y<=ry;y++)for(int x=-rx;x<=rx;x++)if((float)x*x/(rx*rx)+(float)y*y/(ry*ry)<=1)Dot(cx+x,cy+y,c);}
            public void Line(int x0,int y0,int x1,int y1,Color color,int thickness=1)
            {int dx=Math.Abs(x1-x0),sx=x0<x1?1:-1,dy=-Math.Abs(y1-y0),sy=y0<y1?1:-1,e=dx+dy;while(true){Rect(x0-thickness/2,y0-thickness/2,thickness,thickness,color);if(x0==x1&&y0==y1)break;int ee=e*2;if(ee>=dy){e+=dy;x0+=sx;}if(ee<=dx){e+=dx;y0+=sy;}}}
            public void Polygon(Color c, params int[] points)
            {
                int min=frame,max=0;for(int n=1;n<points.Length;n+=2){min=Math.Min(min,points[n]);max=Math.Max(max,points[n]);}
                for(int y=Math.Max(0,min);y<=Math.Min(frame-1,max);y++)for(int x=0;x<frame;x++)
                {bool inside=false;for(int i=0,j=points.Length-2;i<points.Length;j=i,i+=2){int xi=points[i],yi=points[i+1],xj=points[j],yj=points[j+1];if((yi>y)!=(yj>y)&&x<(float)(xj-xi)*(y-yi)/(yj-yi)+xi)inside=!inside;}if(inside)Dot(x,y,c);}
            }
        }
    }
}
