using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    public sealed class WorldView : IDisposable
    {
        private const int ChunkSize = 32;
        private const int PixelsPerCell = 8;
        private const int MaxChunkRebuildsPerFrame = 2;
        private static readonly Color ScorchedColor = PixelArt.Hex("7e553c");
        private sealed class Chunk
        {
            public int X, Y, Width, Height;
            public uint Hash;
            public bool Queued;
            public Texture2D Texture;
            public Color32[] Pixels;
            public Material Material;
            public QuadBatch Ground, Nature, Bridges;
        }
        private readonly Transform root;
        private readonly Texture2D atlas;
        private readonly Material spriteMaterial;
        private readonly Material depthSpriteMaterial, depthEffectMaterial, wagonMaterial;
        private readonly Texture2D wildlifeAtlas, agricultureAtlas;
        private readonly Material wildlifeMaterial, agricultureMaterial, agriculturalGroundMaterial;
        private readonly Texture2D transportAtlas;
        private readonly Material transportMaterial;
        private readonly Texture2D industryAtlas;
        private readonly Material industryMaterial;
        private readonly QuadBatch industryBuildings, industryCargo, industryEffects;
        private readonly QuadBatch waterFlow, wagons;
        private readonly QuadBatch wildlife, equipmentBack, equipmentFront, agriculture, agriculturalCargo, ruralStructures, crowns;
        private readonly CitizenSprites citizenSprites;
        private readonly WeatherView weatherView;
        private readonly MaritimeView maritimeView;
        private readonly RemainsView remainsView;
        private readonly QuadBatch buildings, people, borders, cursor, waterLife;
        private readonly CrowdPresentation crowd = new CrowdPresentation();
        private readonly Dictionary<int, BuildingData> buildingLookup = new Dictionary<int, BuildingData>();
        private readonly Dictionary<int, UnitData> unitLookup = new Dictionary<int, UnitData>(1024);
        private readonly Dictionary<int, Vector2> animalFacing = new Dictionary<int, Vector2>();
        private Chunk[] chunks;
        private Queue<Chunk> dirtyChunks;
        private Color32[] minimapPixels;
        private int revision = -1;
        private int resourceRevision = -1;
        private int lastBuildingCount = -1;
        private float nextRefresh;
        private WorldState world;
        public Texture2D Minimap { get; private set; }
        public bool ShowBorders = true;
        public int SelectedUnitId, SelectedShipId;
        public void SetMaritimeQueries(Func<int, List<BuildingData>> query) { maritimeView.SetQueries(query); }
        public Material SpriteMaterial => spriteMaterial;

        public WorldView(Transform parent)
        {
            root = new GameObject("World • batched pixel rendering").transform; root.SetParent(parent, false);
            atlas = PixelArt.CreateAtlas(); spriteMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = atlas, name = "Shared pixel atlas" };
            depthSpriteMaterial = WorldDepth.Material(atlas); depthEffectMaterial = WorldDepth.Material(atlas, false);
            buildings = new QuadBatch("Buildings", root, depthSpriteMaterial, 30);
            people = new QuadBatch("Life and citizen markers", root, depthSpriteMaterial, 60);
            waterLife = new QuadBatch("Actual fish stocks", root, spriteMaterial, 10);
            wildlifeAtlas = WildlifeArt.CreateAtlas(); wildlifeMaterial = WorldDepth.Material(wildlifeAtlas);
            agricultureAtlas = AgricultureArt.CreateAtlas(); agricultureMaterial = WorldDepth.Material(agricultureAtlas);
            agriculturalGroundMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = agricultureAtlas, name = "Flat cultivated ground" };
            wildlife = new QuadBatch("Actual wildlife", root, wildlifeMaterial, 40);
            equipmentBack = new QuadBatch("Owned carried equipment", root, wildlifeMaterial, 45);
            equipmentFront = new QuadBatch("Actual parcels and pack bags", root, wildlifeMaterial, 60);
            agriculture = new QuadBatch("Fields and rural buildings", root, agriculturalGroundMaterial, 15);
            ruralStructures = new QuadBatch("Rural buildings and loose produce", root, agricultureMaterial, 30);
            crowns = new QuadBatch("Living ruler crowns", root, depthSpriteMaterial, 61);
            agriculturalCargo = new QuadBatch("Actual agricultural goods", root, agricultureMaterial, 60);
            transportAtlas = TransportArt.CreateAtlas(); transportMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = transportAtlas };
            wagonMaterial = WorldDepth.Material(transportAtlas);
            waterFlow = new QuadBatch("Actual water current", root, spriteMaterial, 11);
            wagons = new QuadBatch("Actual wagons", root, wagonMaterial, 45);
            industryAtlas = IndustryArt.CreateAtlas(); industryMaterial = WorldDepth.Material(industryAtlas);
            industryBuildings = new QuadBatch("Production workshops", root, industryMaterial, 30);
            industryCargo = new QuadBatch("Manufactured goods", root, industryMaterial, 60);
            industryEffects = new QuadBatch("Active workshop effects", root, depthEffectMaterial, 31);
            maritimeView = new MaritimeView(root, depthSpriteMaterial, depthEffectMaterial, spriteMaterial);
            remainsView = new RemainsView(root);
            citizenSprites = new CitizenSprites(root);
            weatherView = new WeatherView(root, spriteMaterial);
            borders = new QuadBatch("Territories", root, spriteMaterial, 10);
            cursor = new QuadBatch("Brush", root, spriteMaterial, 80);
        }

        public void SetWorld(WorldState value)
        {
            if (chunks != null) foreach (Chunk chunk in chunks) { chunk.Ground.Dispose(); chunk.Nature.Dispose(); chunk.Bridges.Dispose(); UnityEngine.Object.Destroy(chunk.Texture); UnityEngine.Object.Destroy(chunk.Material); }
            if (Minimap != null) UnityEngine.Object.Destroy(Minimap);
            world = value; crowd.Reset(); animalFacing.Clear(); revision = -1; resourceRevision = -1; nextRefresh = 0; lastBuildingCount = -1;
            citizenSprites.Reset();
            int across = (world.Width + ChunkSize - 1) / ChunkSize, down = (world.Height + ChunkSize - 1) / ChunkSize;
            chunks = new Chunk[across * down];
            // Replacing a world drops only the old world's work, never carries chunks across worlds.
            dirtyChunks = new Queue<Chunk>(chunks.Length);
            for (int cy = 0; cy < down; cy++) for (int cx = 0; cx < across; cx++)
            {
                Chunk chunk = new Chunk { X = cx * ChunkSize, Y = cy * ChunkSize, Width = Math.Min(ChunkSize, world.Width - cx * ChunkSize), Height = Math.Min(ChunkSize, world.Height - cy * ChunkSize) };
                chunk.Texture = new Texture2D(chunk.Width * PixelsPerCell, chunk.Height * PixelsPerCell, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Terrain chunk" };
                chunk.Pixels = new Color32[chunk.Width * chunk.Height * PixelsPerCell * PixelsPerCell];
                chunk.Material = new Material(Shader.Find("Sprites/Default")) { mainTexture = chunk.Texture };
                chunk.Ground = new QuadBatch("Ground " + cx + "," + cy, root, chunk.Material, 0);
                chunk.Ground.Add(chunk.X, chunk.Y, chunk.Width, chunk.Height, new Rect(0, 0, 1, 1), Color.white); chunk.Ground.Submit();
                chunk.Nature = new QuadBatch("Nature " + cx + "," + cy, root, depthSpriteMaterial, 20);
                chunk.Bridges = new QuadBatch("Transport deck " + cx + "," + cy, root, transportMaterial, 14);
                chunks[cy * across + cx] = chunk; Rebuild(chunk); chunk.Hash = ChunkHash(chunk);
            }
            Minimap = new Texture2D(world.Width, world.Height, TextureFormat.RGB24, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            minimapPixels = new Color32[world.Cells.Length];
            // Initial presentation is complete immediately; later edits use the bounded queue.
            revision = world.TerrainRevision; resourceRevision = world.ResourceRevision; lastBuildingCount = world.Buildings.Count;
            RefreshMinimap(); RefreshTerritories(); Update(0);
        }

        public void Update(float now)
        {
            if (world == null) return;
            if ((revision != world.TerrainRevision || resourceRevision != world.ResourceRevision) && now >= nextRefresh)
            {
                bool buildingsChanged = lastBuildingCount != world.Buildings.Count;
                foreach (Chunk chunk in chunks)
                {
                    if (!chunk.Queued && (ChunkHash(chunk) != chunk.Hash || buildingsChanged))
                    {
                        chunk.Queued = true; dirtyChunks.Enqueue(chunk);
                    }
                }
                // This is the scanned revision, not a claim that all queued textures are rendered.
                bool terrainChanged = revision != world.TerrainRevision;
                revision = world.TerrainRevision; resourceRevision = world.ResourceRevision; lastBuildingCount = world.Buildings.Count; nextRefresh = now + .28f;
                if (terrainChanged) { RefreshMinimap(); RefreshTerritories(); }
            }
            for (int rebuilt = 0; rebuilt < MaxChunkRebuildsPerFrame && dirtyChunks.Count > 0; rebuilt++)
            {
                Chunk chunk = dirtyChunks.Dequeue(); chunk.Queued = false;
                // Read the newest state when dequeued. A later scan compares against this actual
                // rendered hash, so changes made while a chunk was waiting cannot be lost.
                uint renderedHash = ChunkHash(chunk); Rebuild(chunk); chunk.Hash = renderedHash;
            }
            buildings.Clear(); wildlife.Clear(); equipmentBack.Clear(); equipmentFront.Clear(); agriculture.Clear(); ruralStructures.Clear(); crowns.Clear(); agriculturalCargo.Clear(); wagons.Clear(); buildingLookup.Clear(); unitLookup.Clear();
            industryBuildings.Clear(); industryCargo.Clear(); industryEffects.Clear(); maritimeView.Begin();
            remainsView.Update(world);
            for (int i = 0; i < world.Units.Count; i++) unitLookup[world.Units[i].Id] = world.Units[i];
            if (animalFacing.Count > world.Units.Count * 2 + 64) animalFacing.Clear();
            for (int i = 0; i < world.Buildings.Count; i++) buildingLookup[world.Buildings[i].Id] = world.Buildings[i];
            if (world.GroundStocks != null) for (int i = 0; i < world.GroundStocks.Count; i++)
            {
                GroundStockData stock = world.GroundStocks[i]; if (stock.Amount <= 0) continue;
                buildings.SetFootDepth(stock.Y); industryBuildings.SetFootDepth(stock.Y); equipmentBack.SetFootDepth(stock.Y);
                int glyph = stock.IsFood ? ResourceArt.FoodBasket : ResourceArt.CargoSprite(stock.Item);
                if (stock.IsFood) Equipment(equipmentBack, stock.X, stock.Y - .15f, .82f, WildlifeArt.FoodBundle);
                else if (IndustryArt.SupportsItem(stock.Item)) IndustryArt.DrawItem(industryBuildings, stock.Item, new Vector2(stock.X, stock.Y - .15f), .82f, Color.white);
                else if (glyph >= 0) buildings.Sprite(stock.X, stock.Y - .15f, .82f, .82f, glyph, ResourceArt.CargoColor(stock.Item));
            }
            if (world.AgriculturalDrops != null) for (int i = 0; i < world.AgriculturalDrops.Count; i++)
            {
                AgriculturalDrop drop = world.AgriculturalDrops[i]; if (drop.Amount <= .001f) continue;
                ruralStructures.SetFootDepth(drop.Y);
                Vector2 dropPosition = remainsView.AgriculturalDropPosition(drop);
                AgroSprite(ruralStructures, dropPosition.x, dropPosition.y - .14f, .85f, AgricultureArt.ProductSprite(drop.Product));
            }
            for (int i = 0; i < world.Buildings.Count; i++)
            {
                BuildingData b = world.Buildings[i]; if (b.Health <= 0) continue;
                float buildingFoot = b.Y + .25f;
                buildings.SetFootDepth(buildingFoot); industryBuildings.SetFootDepth(buildingFoot); ruralStructures.SetFootDepth(buildingFoot);
                industryEffects.SetFootDepth(buildingFoot, WorldDepth.Front);
                int sprite = ResolveBuildingSprite(b);
                bool barrier = b.Type == BuildingType.WoodWall || b.Type == BuildingType.Gate;
                float size = barrier ? 1.1f : b.Type == BuildingType.Hall ? 2.5f : b.Type == BuildingType.Well ? 1.65f : b.Type == BuildingType.Sawmill || b.Type == BuildingType.Stoneworks || b.Type == BuildingType.Warehouse || b.Type == BuildingType.Granary ? 2.8f : b.Type == BuildingType.House ? 2.15f + Mathf.Clamp(b.Level, 1, 5) * .09f : 2.3f;
                if (MaritimeArt.SupportsBuilding(b.Type)) maritimeView.DrawBuilding(world, b);
                else if (IndustryArt.SupportsBuilding(b.Type))
                {
                    IndustryArt.DrawBuilding(industryBuildings, b);
                    IndustryArt.DrawActivity(industryEffects, world, b);
                }
                else if (b.Type == BuildingType.Farm) AgroSprite(agriculture, b.X + .5f, b.Y - .2f, 2.55f, AgricultureArt.FieldSprite(b.FarmCrop, b.CropGrowth));
                else if (b.Type == BuildingType.Pasture) AgroSprite(agriculture, b.X + .5f, b.Y - .2f, 3.2f, AgricultureArt.Pasture);
                else if (b.Type == BuildingType.Brewery) AgroSprite(ruralStructures, b.X + .5f, b.Y - .2f, 2.75f, AgricultureArt.Brewery);
                else buildings.Sprite(b.X + .5f, b.Y - (barrier ? 0 : .15f), size, size, sprite, Color.white);
                SettlementData settlement = world.GetSettlement(b.SettlementId);
                if (settlement != null && b.Type == BuildingType.Hall)
                {
                    Color flag = PixelArt.Factions[(uint)settlement.ColorIndex % (uint)PixelArt.Factions.Length];
                    buildings.Sprite(b.X - .48f, b.Y + .9f, .06f, 1.2f, 12, PixelArt.Hex("765a40"));
                    buildings.Sprite(b.X - .20f, b.Y + 1.76f, .57f, .3f, 12, flag);
                }
                DrawBuildingSupplies(b);
            }
            people.Clear(); citizenSprites.Begin(world); crowd.Prepare(world);
            Camera camera = Camera.main;
            bool showDetails = camera != null && camera.orthographicSize <= 25;
            float simulationTime = world.Tick * SimulationEngine.TickSeconds;
            DrawFishStocks(camera, showDetails);
            DrawWaterCurrent(camera, showDetails);
            for (int i = 0; i < world.Units.Count; i++)
            {
                UnitData unit = world.Units[i]; if (unit.Health <= 0 || unit.InsideBuildingId != 0 || unit.ShipId != 0) continue;
                Vector2 offset = crowd.Offset(unit.Id), position = new Vector2(unit.X, unit.Y) + offset;
                bool riding = IsRiding(world, unit);
                if (riding && unitLookup.TryGetValue(unit.MountId, out UnitData ridden)) { position = new Vector2(ridden.X, ridden.Y + WildlifeArt.AnimalSize(ridden.Species) * .2f); offset = position - new Vector2(unit.X, unit.Y); }
                float unitFoot = riding ? unitLookup[unit.MountId].Y : position.y;
                SetUnitDepth(unitFoot);
                Color tint = Color.white;
                SettlementData settlement = world.GetSettlement(unit.SettlementId);
                if (settlement != null) tint = Color.Lerp(Color.white, PixelArt.Factions[(uint)settlement.ColorIndex % (uint)PixelArt.Factions.Length], .37f);
                if (unit.Infected) tint = Color.Lerp(tint, new Color(.65f, .9f, .25f), .55f);
                bool human = CitizenSprites.IsCivilized(unit.Species);
                float bob = unit.PathCursor < unit.Path.Count ? Mathf.Sin(simulationTime * 10 + unit.Id) * .06f : 0;
                if (human)
                {
                    Color humanTint = unit.Infected ? new Color(.74f, .93f, .65f) : Color.white;
                    citizenSprites.Add(world, unit, settlement == null ? 0 : settlement.ColorIndex, humanTint, offset, riding);
                }
                else if (WildlifeCatalog.IsAnimal(unit.Species)) DrawWildlife(unit, tint);
                else
                {
                    float size = (int)unit.Species < 4 && unit.IsChild ? .81f : 1.2f;
                    people.Sprite(position.x, position.y - .15f + bob, size, size, ResourceArt.CitizenSprite(unit), tint);
                }
                CitizenWaterPresentation.State immersion = CitizenWaterPresentation.Resolve(world, unit, riding);
                if (immersion.Wet)
                {
                    CitizenWaterPresentation.DrawWake(people, world, unit, immersion.SurfacePosition + offset, immersion);
                    position += immersion.Offset; offset += immersion.Offset;
                }
                float head = human ? CitizenSprites.HeadHeight(unit) : (int)unit.Species >= 6 ? WildlifeArt.AnimalSize(unit.Species) * .9f : .96f;
                bool ruler = RulerPresentation.IsRuler(world, unit);
                if (ruler && citizenSprites.TryHeadPosition(unit.Id, out Vector2 crownHead)) RulerPresentation.Draw(crowns, unit, crownHead, unitFoot);
                if (unit.Blessed) people.Sprite(position.x, position.y + head + (ruler ? .28f : 0), .2f, .08f, 12, PixelArt.Gold);
                if (showDetails && (int)unit.Species < 4 && !ruler)
                {
                    Color role = unit.IsChild ? PixelArt.Hex("dbb0d5") : unit.Profession == UnitProfession.Forester ? PixelArt.Teal : unit.Profession == UnitProfession.Guard ? PixelArt.Hex("a6b8ed") : PixelArt.Gold;
                    people.Sprite(position.x - .17f, position.y + head + .13f, .16f, .12f, 12, role);
                    if (unit.Task == UnitTask.Flee || unit.Task == UnitTask.Eat || unit.Task == UnitTask.Rest || unit.Task == UnitTask.Socialize || unit.Task == UnitTask.Learn || unit.Task == UnitTask.Play)
                    {
                        Color activity = unit.Task == UnitTask.Flee ? PixelArt.Hex("ed8677") : unit.Task == UnitTask.Eat ? PixelArt.Teal : unit.Task == UnitTask.Rest ? PixelArt.Hex("92abb5") : PixelArt.Hex("d5a9d9");
                        people.Sprite(position.x + .08f, position.y + head + .13f, .14f, .12f, 12, activity);
                    }
                }
                Vector2 facing = human ? citizenSprites.Facing(unit.Id) : TravelDirection(unit);
                DrawEquipment(unit, facing);
                SetUnitDepth(unitFoot);
                if (showDetails && human) IndustryArt.DrawEquipment(people, world, unit, position, facing, riding);
                DrawCargo(unit, facing, simulationTime, offset);
                SetUnitDepth(unitFoot);
                if (showDetails) DrawWaterActivity(unit, position, facing);
                if (unit.Id == SelectedUnitId)
                {
                    people.Sprite(position.x, position.y + head + .38f, .85f, .1f, 12, PixelArt.Ink);
                    people.Sprite(position.x - .425f + .425f * Mathf.Clamp01(unit.Health / unit.MaxHealth), position.y + head + .38f, .85f * Mathf.Clamp01(unit.Health / unit.MaxHealth), .1f, 12, PixelArt.Teal);
                }
            }
            buildings.Submit(); people.Submit(); wildlife.Submit(); equipmentBack.Submit(); equipmentFront.Submit(); agriculture.Submit(); ruralStructures.Submit(); crowns.Submit(); agriculturalCargo.Submit(); wagons.Submit(); citizenSprites.Submit();
            industryBuildings.Submit(); industryCargo.Submit(); industryEffects.Submit();
            maritimeView.Finish(world, SelectedShipId);
            weatherView.Update(world, camera);
        }

        public Sprite CreateWeatherIcon(WeatherKind kind) { return weatherView.CreateIcon(kind); }
        public Sprite CreateHumanIcon() { return citizenSprites.CreatePortrait(); }
        public Sprite CreateCitizenIcon(SpeciesType species) { return citizenSprites.CreateCitizenSprite(species, CitizenSprites.CitizenAppearance.AdultMale, CitizenSprites.CitizenDirection.South); }
        public Vector2 GetPresentationPosition(UnitData unit)
        {
            if (unit.ShipId != 0) { ShipData ship = world.GetShip(unit.ShipId); if (ship != null) return new Vector2(ship.X, ship.Y); }
            if (unit.InsideBuildingId != 0) { BuildingData inside = world.GetBuilding(unit.InsideBuildingId); if (inside != null) return new Vector2(inside.X + .5f, inside.Y + .5f); }
            if (IsRiding(world, unit)) { UnitData mount = world.GetUnit(unit.MountId); return new Vector2(mount.X, mount.Y + WildlifeArt.AnimalSize(mount.Species) * .2f); }
            return new Vector2(unit.X, unit.Y) + crowd.Offset(unit.Id) + CitizenWaterPresentation.Resolve(world, unit).Offset;
        }
        private void SetUnitDepth(float foot)
        {
            wildlife.SetFootDepth(foot); equipmentBack.SetFootDepth(foot, WorldDepth.Back);
            equipmentFront.SetFootDepth(foot, WorldDepth.Front); people.SetFootDepth(foot, WorldDepth.Front);
            agriculturalCargo.SetFootDepth(foot, WorldDepth.Front); industryCargo.SetFootDepth(foot, WorldDepth.Front);
            buildings.SetFootDepth(foot, WorldDepth.Back); wagons.SetFootDepth(foot, WorldDepth.Back);
        }
        public Sprite CreateAnimalIcon(SpeciesType species)
        {
            int sprite = WildlifeArt.SpriteFor(species, Sex.Male, WildlifeArt.AnimalDirection.East, 0); Rect uv = WildlifeArt.Uv(sprite);
            return Sprite.Create(wildlifeAtlas, new Rect(uv.x * WildlifeArt.Width, uv.y * WildlifeArt.Height, uv.width * WildlifeArt.Width, uv.height * WildlifeArt.Height), new Vector2(.5f, .5f), 32);
        }
        public static bool IsRiding(WorldState state, UnitData unit)
        {
            if (state == null || unit.MountId == 0 || (int)unit.Species >= 4 || unit.HasWagon) return false;
            // A nearby owned mount still carries goods while its worker dismounts to work,
            // drink or rest. Ownership alone is not an instruction to sit through an action.
            bool moving = unit.Path != null && unit.PathCursor < unit.Path.Count;
            if (!moving && unit.Task != UnitTask.Idle && unit.Task != UnitTask.Wander && unit.Task != UnitTask.Return && unit.Task != UnitTask.Haul && unit.Task != UnitTask.Caravan && unit.Task != UnitTask.Flee) return false;
            UnitData mount = state.GetUnit(unit.MountId);
            return mount != null && mount.Health > 0 && mount.HandlerId == unit.Id && (mount.Species == SpeciesType.Horse || mount.Species == SpeciesType.Donkey || mount.Species == SpeciesType.SpiritStag) && (mount.X - unit.X) * (mount.X - unit.X) + (mount.Y - unit.Y) * (mount.Y - unit.Y) <= 1.4f * 1.4f;
        }
        private static void Equipment(QuadBatch batch, float x, float y, float size, int glyph, bool flip = false) { batch.Add(x - size * .5f, y, size, size, WildlifeArt.Uv(glyph), Color.white, flip); }
        private static void AgroSprite(QuadBatch batch, float x, float y, float size, int glyph, bool flip = false) { batch.Add(x - size * .5f, y, size, size, AgricultureArt.Uv(glyph), Color.white, flip); }
        private void DrawWildlife(UnitData unit, Color tint)
        {
            Vector2 direction = animalFacing.TryGetValue(unit.Id, out Vector2 previous) ? previous : Vector2.right;
            if (unit.Task == UnitTask.Fight && unitLookup.TryGetValue(unit.TargetId, out UnitData target)) { Vector2 delta = new Vector2(target.X - unit.X, target.Y - unit.Y); if (delta.sqrMagnitude > .001f) direction = delta.normalized; }
            else if (unit.Path != null && unit.PathCursor < unit.Path.Count) direction = TravelDirection(unit);
            animalFacing[unit.Id] = direction;
            float size = WildlifeArt.AnimalSize(unit.Species);
            Vector2 hit = unit.Task == UnitTask.Fight && unit.AttackTimer > .55f ? direction * Mathf.Clamp01((unit.AttackTimer - .55f) * 4) * .17f : Vector2.zero;
            wildlife.Add(unit.X - size * .5f + hit.x, unit.Y - .17f + hit.y, size, size, WildlifeArt.Uv(WildlifeArt.AnimalSprite(unit, world.Tick, direction)), tint);
            if (unit.HasSaddleBags) Equipment(equipmentFront, unit.X, unit.Y - .08f, size * .68f, WildlifeArt.SaddleBags, direction.x < 0);
        }
        private void DrawEquipment(UnitData unit, Vector2 direction)
        {
            if ((int)unit.Species >= 4) return;
            if (unit.HuntingToolLevel > 0 && !unit.IsChild)
            {
                Vector2 point = GetPresentationPosition(unit); bool flip = direction.x < 0;
                float strike = unit.Task == UnitTask.Hunt && unit.AttackTimer > .5f ? (unit.AttackTimer - .5f) * .45f : 0;
                Equipment(equipmentFront, point.x + (flip ? -.3f : .3f) + direction.normalized.x * strike, point.y + direction.normalized.y * strike, 1.25f, WildlifeArt.HuntingSpear, flip);
            }
            if (unit.MountId != 0 && unitLookup.TryGetValue(unit.MountId, out UnitData mount) && mount.Health > 0 && mount.HandlerId == unit.Id)
            {
                Vector2 facing = animalFacing.TryGetValue(mount.Id, out Vector2 animalDirection) ? animalDirection : TravelDirection(mount);
                if (unit.HasWagon)
                {
                    Vector2 wagon = WagonPosition(mount, facing);
                    wagons.SetFootDepth(wagon.y, WorldDepth.Back); people.SetFootDepth(wagon.y, WorldDepth.Front);
                    bool vertical = Mathf.Abs(facing.y) > Mathf.Abs(facing.x);
                    float size = vertical ? 1.7f : 1.9f;
                    wagons.Add(wagon.x - size * .5f, wagon.y - size * .35f, size, size, TransportArt.Uv(vertical ? TransportArt.WagonVertical : TransportArt.WagonSide), Color.white, facing.x < 0);
                    Vector2 normal = new Vector2(-facing.y, facing.x).normalized * .19f;
                    Vector2 hitch = new Vector2(mount.X, mount.Y + .43f), front = wagon + facing.normalized * .45f + Vector2.up * .16f;
                    // Two shafts and a short body strap share the same saved animal and direction as the wagon.
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector2 a = front + normal * side, b = hitch + normal * side, d = b - a;
                        people.OrientedSprite((a + b) * .5f, d.magnitude, .045f, Mathf.Atan2(d.y, d.x), 12, PixelArt.Hex("b79768"));
                    }
                    people.OrientedSprite(hitch, .42f, .055f, Mathf.Atan2(normal.y, normal.x), 12, PixelArt.Hex("5d4b3a"));
                }
                else { equipmentBack.SetFootDepth(mount.Y, WorldDepth.Front); Equipment(equipmentBack, mount.X, mount.Y + .11f, WildlifeArt.AnimalSize(mount.Species) * .70f, WildlifeArt.Saddle, facing.x < 0); }
                equipmentFront.SetFootDepth(mount.Y, WorldDepth.Front);
                if (unit.MountBags && !mount.HasSaddleBags) Equipment(equipmentFront, mount.X, mount.Y - .08f, WildlifeArt.AnimalSize(mount.Species) * .68f, WildlifeArt.SaddleBags, facing.x < 0);
            }
            else if (unit.BasketLevel > 0)
            {
                Vector2 position = GetPresentationPosition(unit);
                float size = .8f + .08f * unit.BasketLevel;
                Rect geometry = new Rect(position.x + (direction.x < 0 ? .27f : -.27f) - size * .5f, position.y + .03f, size, size);
                Rect uv = WildlifeArt.Uv(WildlifeArt.BasketSmall + Mathf.Clamp(unit.BasketLevel - 1, 0, 2));
                CitizenWaterPresentation.State water = CitizenWaterPresentation.Resolve(world, unit);
                if (!water.Wet || CitizenWaterPresentation.Clip(ref geometry, ref uv, water.SurfacePosition.y + crowd.Offset(unit.Id).y))
                    equipmentBack.Add(geometry.x, geometry.y, geometry.width, geometry.height, uv, Color.white);
            }
        }

        public static int ResolveBuildingSprite(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.Hall: return ResourceArt.Hearth;
                case BuildingType.House: return SocietyArt.HouseOne; case BuildingType.Farm: return 4; case BuildingType.Tower: return 5;
                case BuildingType.Sawmill: return ResourceArt.Sawmill;
                case BuildingType.Stoneworks: return ResourceArt.Stoneworks;
                case BuildingType.Kiln: return ResourceArt.Kiln;
                case BuildingType.Well: return SocietyArt.Well; case BuildingType.Warehouse: return SocietyArt.Warehouse; case BuildingType.Granary: return ResourceArt.Granary;
                case BuildingType.Shop: return SocietyArt.Shop; case BuildingType.School: return SocietyArt.School;
                case BuildingType.WoodWall: return SocietyArt.WallHorizontal; case BuildingType.Gate: return SocietyArt.GateOpen;
                default: return SocietyArt.HouseOne;
            }
        }
        public int ResolveBuildingSprite(BuildingData building)
        {
            switch (building.Type)
            {
                case BuildingType.House: return SocietyArt.HouseSprite(building.Level);
                case BuildingType.Well: return building.WaterStored > .001f ? SocietyArt.Well : SocietyArt.WellEmpty; case BuildingType.Warehouse: return SocietyArt.Warehouse;
                case BuildingType.Shop: return SocietyArt.Shop; case BuildingType.School: return SocietyArt.School;
                case BuildingType.WoodWall: return BarrierVertical(building) ? SocietyArt.WallVertical : SocietyArt.WallHorizontal;
                case BuildingType.Gate: return BarrierVertical(building) ? (building.GateOpen ? SocietyArt.GateVerticalOpen : SocietyArt.GateVerticalClosed) : (building.GateOpen ? SocietyArt.GateOpen : SocietyArt.GateClosed);
                default: return ResolveBuildingSprite(building.Type);
            }
        }
        private bool BarrierVertical(BuildingData building)
        {
            int horizontal = BarrierAt(building.X - 1, building.Y) + BarrierAt(building.X + 1, building.Y);
            int vertical = BarrierAt(building.X, building.Y - 1) + BarrierAt(building.X, building.Y + 1);
            return vertical > horizontal;
        }
        private int BarrierAt(int x, int y)
        {
            if (!world.InBounds(x, y)) return 0;
            buildingLookup.TryGetValue(world.Cells[world.Index(x, y)].BuildingId, out BuildingData b);
            return b != null && b.Health > 0 && (b.Type == BuildingType.WoodWall || b.Type == BuildingType.Gate) ? 1 : 0;
        }
        private Vector2 TravelDirection(UnitData unit)
        {
            bool animal = WildlifeCatalog.IsAnimal(unit.Species);
            if (unit.Path != null && unit.PathCursor < unit.Path.Count)
            {
                int next = unit.Path[unit.PathCursor]; Vector2 delta = new Vector2(next % world.Width + .5f - unit.X, next / world.Width + .5f - unit.Y);
                // A fast animal can be only .005 cells short of arrival for a full tick.
                // That small but real segment still has a direction, including on first render.
                if (delta.sqrMagnitude > (animal ? .00000001f : .0001f)) return delta.normalized;
            }
            if (animal && animalFacing.TryGetValue(unit.Id, out Vector2 savedDirection)) return savedDirection;
            return Vector2.right;
        }
        public static Vector2 CargoPosition(UnitData unit, Vector2 direction)
        {
            direction = direction.sqrMagnitude > .0001f ? direction.normalized : Vector2.right;
            float size = unit.IsChild ? .7f : 1;
            // Sprite() takes a quad's bottom Y, not its centre. Resource pixels already
            // occupy the middle of that quad, so a .5 Y offset would cover the face.
            return new Vector2(unit.X, unit.Y) + (unit.CargoKind == ItemType.Log ? -direction * .82f + Vector2.up * .03f : CitizenSprites.IsCivilized(unit.Species) ? CitizenSprites.ParcelOffset(unit, direction) : new Vector2((direction.x < 0 ? -.52f : .52f) * size, -.08f * size));
        }
        public static Vector2 WagonPosition(UnitData mount, Vector2 direction)
        {
            Vector2 facing = direction.sqrMagnitude > .0001f ? direction.normalized : Vector2.right;
            return new Vector2(mount.X, mount.Y) - facing * 1.55f;
        }
        private void DrawCargo(UnitData unit, Vector2 direction, float simulationTime, Vector2 presentationOffset)
        {
            bool resource = unit.CargoAmount > .001f && unit.CargoKind != ItemType.None;
            bool food = !resource && unit.CarriedFood > .001f;
            bool legacyPlanks = !resource && !food && unit.CarriedWood > .001f;
            bool harvest = unit.HarvestAmount > .001f && unit.HarvestProduct != AgriculturalProduct.None;
            bool water = unit.CarriedWater > .001f;
            if (!resource && !food && !legacyPlanks && !harvest && !water) return;
            Vector2 position = CargoPosition(unit, direction) + presentationOffset;
            bool pack = unit.MountId != 0 && unitLookup.TryGetValue(unit.MountId, out UnitData cargoMount) && cargoMount.Health > 0 && cargoMount.HandlerId == unit.Id;
            if (pack)
            {
                UnitData mount = unitLookup[unit.MountId]; Vector2 travel = animalFacing.TryGetValue(mount.Id, out Vector2 savedDirection) ? savedDirection : TravelDirection(mount);
                position = unit.HasWagon ? WagonPosition(mount, travel) + new Vector2(0, -.04f) : new Vector2(mount.X + .28f, mount.Y + .12f);
                float foot = unit.HasWagon ? WagonPosition(mount, travel).y : mount.Y;
                people.SetFootDepth(foot, WorldDepth.Front); equipmentFront.SetFootDepth(foot, WorldDepth.Front);
                agriculturalCargo.SetFootDepth(foot, WorldDepth.Front); industryCargo.SetFootDepth(foot, WorldDepth.Front);
            }
            if (harvest) AgroSprite(agriculturalCargo, position.x + (resource || food || legacyPlanks ? .35f : 0), position.y, unit.IsChild ? .58f : .78f, AgricultureArt.ProductSprite(unit.HarvestProduct));
            if (water)
            {
                Vector2 waterPosition = pack ? position : new Vector2(unit.X, unit.Y) + CitizenSprites.ParcelOffset(unit, direction) + presentationOffset;
                if (resource || food || legacyPlanks || harvest) waterPosition.x -= .35f;
                people.Sprite(waterPosition.x, waterPosition.y, .67f, .67f, SocietyArt.WaterPail, Color.white);
            }
            if (!resource && !food && !legacyPlanks) return;
            if (resource && unit.CargoKind == ItemType.Log && !pack)
            {
                Vector2 d = direction.sqrMagnitude > .0001f ? direction.normalized : Vector2.right;
                float angle = Mathf.Atan2(d.y, d.x);
                // Only the carried log rotates. Rope links its front end to the worker's hand.
                // A log dragged north of a southbound citizen lies behind the body.
                QuadBatch cargoBatch = d.y < -.2f ? buildings : people;
                cargoBatch.OrientedSprite(position, 1.26f, .83f, angle, ResourceArt.Log, Color.white);
                Vector2 end = position + d * .55f, hand = new Vector2(unit.X, unit.Y) + CitizenSprites.HandOffset(unit, direction) + presentationOffset, rope = hand - end;
                cargoBatch.OrientedSprite((end + hand) * .5f, rope.magnitude, .035f, Mathf.Atan2(rope.y, rope.x), 12, PixelArt.Hex("bfac7f"));
                return;
            }
            if (!pack) position = new Vector2(unit.X, unit.Y) + CitizenSprites.ParcelOffset(unit, direction) + presentationOffset;
            float bob = unit.Path != null && unit.PathCursor < unit.Path.Count ? Mathf.Sin(simulationTime * 10 + unit.Id) * .025f : 0;
            float size = unit.IsChild ? .58f : .75f;
            ItemType item = legacyPlanks ? ItemType.Plank : unit.CargoKind;
            int glyph = food ? ResourceArt.FoodBasket : ResourceArt.CargoSprite(item);
            if (food) Equipment(equipmentFront, position.x, position.y + bob, size, WildlifeArt.FoodBundle);
            else if (IndustryArt.SupportsItem(item)) IndustryArt.DrawItem(industryCargo, item, position + Vector2.up * bob, size, Color.white);
            else people.Sprite(position.x, position.y + bob, size, size, glyph, ResourceArt.CargoColor(item));
        }
        private void DrawWaterCurrent(Camera camera, bool visible)
        {
            RiverArt.DrawSurface(world, camera, waterFlow, visible);
        }
        private void DrawFishStocks(Camera camera, bool visible)
        {
            waterLife.Clear();
            if (!visible || world.Infrastructure == null) { waterLife.Submit(); return; }
            float height = camera.orthographicSize, width = height * camera.aspect;
            int minX = Mathf.Max(0, Mathf.FloorToInt(camera.transform.position.x - width - 1)), maxX = Mathf.Min(world.Width - 1, Mathf.CeilToInt(camera.transform.position.x + width + 1));
            int minY = Mathf.Max(0, Mathf.FloorToInt(camera.transform.position.y - height - 1)), maxY = Mathf.Min(world.Height - 1, Mathf.CeilToInt(camera.transform.position.y + height + 1));
            for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
            {
                int index = world.Index(x, y); InfrastructureCell cell = world.Infrastructure[index];
                if (cell.Fish < .25f || cell.FishCapacity <= 0 || !IsWater(x, y)) continue;
                uint hash = PixelArt.Hash(x, y, world.Seed);
                float fullness = Mathf.Clamp01(cell.Fish / cell.FishCapacity);
                // A glyph represents a resource patch, not one fish. Sparse stable sampling avoids
                // making tiny newly recovered stocks look like a full school on every flooded tile.
                if ((hash % 1000) / 1000f >= fullness * .45f) continue;
                int frame = SocietyArt.FishFirst + (int)((world.Tick / 3 + hash % 3) % 3);
                float size = .28f + .30f * Mathf.Sqrt(fullness);
                float offsetX = ((hash >> 10) % 100) / 100f * .3f - .15f, offsetY = ((hash >> 20) % 100) / 100f * .22f - .11f;
                waterLife.Add(x + .5f + offsetX - size * .5f, y + .18f + offsetY, size, size, PixelArt.Uv(frame), new Color(.78f, .95f, .96f, .3f + .38f * Mathf.Sqrt(fullness)), (hash & 1) != 0);
            }
            waterLife.Submit();
        }
        private void DrawWaterActivity(UnitData unit, Vector2 position, Vector2 facing)
        {
            if (unit.Path != null && unit.PathCursor < unit.Path.Count || unit.WorkTimer <= 0) return;
            if (unit.Task == UnitTask.Drink && unit.CarriedWater <= .001f)
                people.Sprite(position.x + .28f, position.y + .06f, .58f, .58f, SocietyArt.WaterPail, Color.white);
            else FishingPresentation.Draw(people, world, unit, position, facing);
        }
        private void DrawBuildingSupplies(BuildingData building)
        {
            float supplyFoot = building.Y - .12f;
            buildings.SetFootDepth(supplyFoot); agriculturalCargo.SetFootDepth(supplyFoot); industryCargo.SetFootDepth(supplyFoot);
            if (building.Type == BuildingType.Brewery && building.BrewWaterStored > .001f)
                buildings.Sprite(building.X - .45f, building.Y - .25f, .65f, .65f, SocietyArt.WaterPail, Color.white);
            if (building.AgriculturalStock != null)
            {
                int shown = 0;
                for (int item = 1; item < building.AgriculturalStock.Length && shown < 3; item++)
                {
                    if (building.AgriculturalStock[item] <= .001f) continue;
                    AgroSprite(agriculturalCargo, building.X + .1f + shown * .45f, building.Y - .22f, .70f, AgricultureArt.ProductSprite((AgriculturalProduct)item)); shown++;
                }
            }
            if (building.Inventory != null)
            {
                int drawn = 0;
                IndustryRecipe production = IndustryCatalog.Get(building.ActiveIndustryRecipe);
                ItemType visibleOutput = production != null && building.Inventory[(int)production.Output] > .001f ? production.Output : ItemType.None;
                if (visibleOutput != ItemType.None)
                {
                    IndustryArt.DrawItem(industryCargo, visibleOutput, new Vector2(building.X - .3f, building.Y - .27f), .78f, Color.white);
                    drawn++;
                }
                for (int item = 1; item < building.Inventory.Length && drawn < 3; item++)
                {
                    if (building.Inventory[item] <= .001f || (ItemType)item == visibleOutput) continue;
                    ItemType type = (ItemType)item;
                    if (IndustryArt.SupportsItem(type)) IndustryArt.DrawItem(industryCargo, type, new Vector2(building.X - .3f + drawn * .48f, building.Y - .27f), .78f, Color.white);
                    else
                    {
                        int sprite = ResourceArt.CargoSprite(type); if (sprite < 0) continue;
                        buildings.Sprite(building.X - .3f + drawn * .48f, building.Y - .27f, .78f, .78f, sprite, ResourceArt.CargoColor(type));
                    }
                    drawn++;
                }
            }
            UnitData worker = building.WorkerId != 0 ? world.GetUnit(building.WorkerId) : null;
            if (worker == null || worker.Task != UnitTask.Process || worker.WorkTimer <= 0 || worker.PathCursor < worker.Path.Count || building.ActiveRecipe == RecipeType.None) return;
            float phase = building.ProcessProgress * 5;
            buildings.SetFootDepth(building.Y + .25f, WorldDepth.Front);
            if (building.Type == BuildingType.Sawmill)
            {
                buildings.Sprite(building.X + .9f, building.Y + 1.25f + Mathf.Sin(phase) * .055f, .50f, .055f, 12, PixelArt.Hex("d3ded1"));
            }
            else if (building.Type == BuildingType.Stoneworks)
            {
                float lift = (Mathf.Sin(phase) + 1) * .13f;
                buildings.OrientedSprite(new Vector2(building.X + .8f, building.Y + 1.55f + lift), .40f, .065f, -.65f, 12, PixelArt.Hex("c6b38b"));
            }
            else if (building.Type == BuildingType.Kiln)
            {
                buildings.Sprite(building.X + .55f, building.Y + .05f, .74f, .9f + Mathf.Sin(phase) * .06f, 21, Color.white);
            }
        }

        private uint ChunkHash(Chunk chunk)
        {
            uint hash = 2166136261;
            for (int y = chunk.Y; y < chunk.Y + chunk.Height; y++) for (int x = chunk.X; x < chunk.X + chunk.Width; x++)
            {
                TileData cell = world.Cells[world.Index(x, y)];
                unchecked { hash = (hash ^ (uint)((int)cell.Terrain * 1009 + (int)(cell.Vegetation * 12) * 31 + cell.SettlementId * 13 + cell.BuildingId * 17 + cell.DrainageParent * 23 + (cell.Fire > 0 ? 1 : 0) + (cell.River ? 7 : 0))) * 16777619; }
                ResourceNodeData node = world.GetResourceAt(x, y);
                unchecked { hash = (hash ^ (uint)((int)node.Type * 137 + ResourceArt.VisualAmount(node.Amount, node.Capacity) * 19)) * 16777619; }
                InfrastructureCell infrastructure = InfrastructureAt(x, y);
                unchecked { hash = (hash ^ (uint)(infrastructure.RoadLevel * 103 + (int)(infrastructure.Stream * 16) * 43)) * 16777619; }
                hash = TransportHash(hash, cell, infrastructure);
                unchecked { hash = (hash ^ (uint)((int)(cell.Moisture * 32) * 449 + (int)(cell.SnowCover * 32) * 457 + cell.Biome * 463)) * 16777619; }
            }
            // Shore and river joins depend on the one-cell halo across chunk boundaries.
            for (int y = chunk.Y - 1; y <= chunk.Y + chunk.Height; y++) for (int x = chunk.X - 1; x <= chunk.X + chunk.Width; x++)
            {
                if (!world.InBounds(x, y) || (x >= chunk.X && x < chunk.X + chunk.Width && y >= chunk.Y && y < chunk.Y + chunk.Height)) continue;
                TileData adjacent = world.Cells[world.Index(x, y)];
                unchecked { hash = (hash ^ (uint)((int)adjacent.Terrain * 1009 + adjacent.DrainageParent * 23 + (adjacent.River ? 7 : 0))) * 16777619; }
                InfrastructureCell infrastructure = InfrastructureAt(x, y);
                unchecked { hash = (hash ^ (uint)(infrastructure.RoadLevel * 103 + (int)(infrastructure.Stream * 16) * 43)) * 16777619; }
                hash = TransportHash(hash, adjacent, infrastructure);
            }
            return hash;
        }
        private static uint TransportHash(uint hash, TileData tile, InfrastructureCell infrastructure)
        {
            unchecked { return (hash ^ (uint)((int)tile.WaterKind * 107 + (int)(tile.WaterDepth * 20) * 193 + (int)(tile.CurrentStrength * 20) * 277 + tile.ChannelWidth * 307 + infrastructure.BridgeLevel * 347 + infrastructure.BridgeAxis * 419 + (int)(tile.Height * 64) * 431)) * 16777619; }
        }

        public void SetBrush(int x, int y, int radius, bool visible, Color color, int unitRadius = -1, bool showTerrain = true)
        {
            cursor.Clear();
            if (visible && world != null)
            {
                if (showTerrain) for (int yy = -radius; yy <= radius; yy++) for (int xx = -radius; xx <= radius; xx++)
                {
                    int d = xx * xx + yy * yy; if (d > radius * radius || !world.InBounds(x + xx, y + yy)) continue;
                    bool edge = (xx - 1) * (xx - 1) + yy * yy > radius * radius || (xx + 1) * (xx + 1) + yy * yy > radius * radius || xx * xx + (yy - 1) * (yy - 1) > radius * radius || xx * xx + (yy + 1) * (yy + 1) > radius * radius;
                    Color fill = color; fill.a = edge ? .55f : .12f; cursor.Sprite(x + xx + .5f, y + yy, 1, 1, 12, fill);
                }
                if (unitRadius >= 0)
                {
                    Color unitColor = Color.Lerp(color, Color.white, .65f);
                    for (int yy = -unitRadius; yy <= unitRadius; yy++) for (int xx = -unitRadius; xx <= unitRadius; xx++)
                    {
                        if (xx * xx + yy * yy > unitRadius * unitRadius || !world.InBounds(x + xx, y + yy)) continue;
                        bool edge = (xx - 1) * (xx - 1) + yy * yy > unitRadius * unitRadius || (xx + 1) * (xx + 1) + yy * yy > unitRadius * unitRadius || xx * xx + (yy - 1) * (yy - 1) > unitRadius * unitRadius || xx * xx + (yy + 1) * (yy + 1) > unitRadius * unitRadius;
                        if (edge) { unitColor.a = .8f; cursor.Sprite(x + xx + .5f, y + yy + .35f, .3f, .3f, 12, unitColor); }
                        else if (!showTerrain) { unitColor.a = .1f; cursor.Sprite(x + xx + .5f, y + yy, 1, 1, 12, unitColor); }
                    }
                }
            }
            cursor.Submit();
        }

        public void RefreshTerritories()
        {
            borders.Clear(); if (world == null || !ShowBorders) { borders.Submit(); return; }
            for (int y = 0; y < world.Height; y++) for (int x = 0; x < world.Width; x++)
            {
                int owner = world.Cells[world.Index(x, y)].SettlementId; if (owner == 0) continue;
                SettlementData settlement = world.GetSettlement(owner); if (settlement == null) continue;
                Color color = PixelArt.Factions[(uint)settlement.ColorIndex % (uint)PixelArt.Factions.Length]; color.a = .64f;
                Rect uv = PixelArt.Uv(12);
                if (x == 0 || world.Cells[world.Index(x - 1, y)].SettlementId != owner) borders.Add(x, y, .10f, 1, uv, color);
                if (y == 0 || world.Cells[world.Index(x, y - 1)].SettlementId != owner) borders.Add(x, y, 1, .10f, uv, color);
                if (x == world.Width - 1 || world.Cells[world.Index(x + 1, y)].SettlementId != owner) borders.Add(x + .9f, y, .1f, 1, uv, color);
                if (y == world.Height - 1 || world.Cells[world.Index(x, y + 1)].SettlementId != owner) borders.Add(x, y + .9f, 1, .1f, uv, color);
            }
            borders.Submit();
        }

        private void RefreshMinimap()
        {
            for (int i = 0; i < minimapPixels.Length; i++) { TileData tile = world.Cells[i]; minimapPixels[i] = tile.WaterDepth > 0 && (tile.Terrain == TerrainType.DeepWater || tile.Terrain == TerrainType.ShallowWater) ? TransportArt.WaterColor(tile) : WeatherView.TerrainColor(PixelArt.Terrain(tile.Terrain), tile, world.GetBiome(i)); }
            Minimap.SetPixels32(minimapPixels); Minimap.Apply(false, false);
        }

        private void Rebuild(Chunk chunk)
        {
            int textureWidth = chunk.Width * PixelsPerCell;
            Color32[] pixels = chunk.Pixels; chunk.Nature.Clear(); chunk.Bridges.Clear();
            for (int localY = chunk.Height - 1; localY >= 0; localY--) for (int localX = 0; localX < chunk.Width; localX++)
            {
                int x = chunk.X + localX, y = chunk.Y + localY;
                chunk.Nature.SetFootDepth(y + .25f);
                TileData cell = world.Cells[world.Index(x, y)]; uint hash = PixelArt.Hash(x, y, world.Seed);
                Color baseColor = PixelArt.Terrain(cell.Terrain);
                bool water = cell.Terrain == TerrainType.DeepWater || cell.Terrain == TerrainType.ShallowWater;
                if (water && cell.WaterDepth > 0) baseColor = TransportArt.WaterColor(cell);
                else baseColor = WeatherView.TerrainColor(baseColor, cell, world.GetBiome(world.Index(x, y)));
                InfrastructureCell infrastructure = InfrastructureAt(x, y);
                RiverArt.Bank bank = RiverArt.SampleBank(world, x, y);
                bool watercourse = cell.River || infrastructure.Stream > .01f;
                int riverConnections = watercourse ? RiverConnections(x, y, cell) : 0;
                int roadConnections = infrastructure.RoadLevel > 0 ? RoadConnections(x, y) : 0;
                float light = .96f + (hash % 13) / 160f;
                bool relief = cell.Terrain == TerrainType.Hills || cell.Terrain == TerrainType.Highlands || cell.Terrain == TerrainType.Ravine;
                if (relief)
                {
                    float west = world.InBounds(x - 1, y) ? world.Cells[world.Index(x - 1, y)].Height : cell.Height;
                    float north = world.InBounds(x, y + 1) ? world.Cells[world.Index(x, y + 1)].Height : cell.Height;
                    light *= Mathf.Clamp(1 + (north - west) * 1.5f, .77f, 1.15f);
                }
                for (int py = 0; py < PixelsPerCell; py++) for (int px = 0; px < PixelsPerCell; px++)
                {
                    float variation = 1;
                    uint grain = PixelArt.Hash(x * PixelsPerCell + px, y * PixelsPerCell + py, world.Seed);
                    if (grain % 31 == 0) variation = 1.1f; else if (grain % 29 == 0) variation = .92f;
                    if (cell.Terrain == TerrainType.Mountain && grain % 17 == 0) variation *= .86f;
                    Color color = baseColor * light * variation; color.a = 1;
                    color = RiverArt.ShadePixel(cell, bank, x * PixelsPerCell + px, y * PixelsPerCell + py, world.Seed, color);
                    if (cell.Terrain == TerrainType.Swamp && hash % 3 == 0 && (px - 4) * (px - 4) + (py - 4) * (py - 4) * 2 < 7) color = PixelArt.Hex("527a70");
                    if (cell.Terrain == TerrainType.Ravine && py == 7 && world.InBounds(x, y + 1) && world.Cells[world.Index(x, y + 1)].Terrain != TerrainType.Ravine) color *= .64f;
                    if (cell.Terrain == TerrainType.Highlands && py == 0 && world.InBounds(x, y - 1) && world.Cells[world.Index(x, y - 1)].Height < cell.Height - .04f) color *= .76f;
                    if (watercourse && !water && RiverPixel(px, py, riverConnections)) color = TransportArt.WaterColor(cell);
                    int roadMin = infrastructure.RoadLevel == 1 ? 3 : 2, roadMax = infrastructure.RoadLevel == 1 ? 4 : 5;
                    if (infrastructure.RoadLevel > 0 && !water && PathPixel(px, py, roadConnections, roadMin, roadMax))
                    {
                        color = TransportArt.RoadColor(infrastructure.RoadLevel, px, py, grain);
                        color = Color.Lerp(color, new Color(.92f, .96f, .94f), Mathf.Clamp01(cell.SnowCover) * .64f);
                    }
                    if (cell.Fire > 0) color = Color.Lerp(color, ScorchedColor, .35f);
                    pixels[(localY * PixelsPerCell + py) * textureWidth + localX * PixelsPerCell + px] = color;
                }
                ResourceNodeData node = world.GetResourceAt(x, y);
                if (infrastructure.BridgeLevel > 0)
                    chunk.Bridges.Add(x, y, 1, 1, TransportArt.Uv(TransportArt.BridgeSprite(infrastructure)), Color.white);
                if (!water && bank.HasShore && !bank.Sandy && !bank.Rocky && infrastructure.RoadLevel == 0 && infrastructure.BridgeLevel == 0 && cell.BuildingId == 0 && node.Type == ResourceType.None && hash % 13 == 0)
                    chunk.Nature.Sprite(x + .5f + ((hash >> 9) % 5 - 2f) * .05f, y - .05f, .64f, .64f, 122, Color.white);
                if (cell.Terrain == TerrainType.Swamp && cell.BuildingId == 0 && node.Type == ResourceType.None && hash % 7 == 0)
                    chunk.Nature.Sprite(x + .5f, y - .07f, .8f, .8f, 122, Color.white);
                if (cell.Terrain == TerrainType.Mountain && node.Type == ResourceType.None)
                {
                    if (hash % 11 == 0) { float size = 2.5f + (hash % 7) * .16f; chunk.Nature.Sprite(x + .5f, y - .2f, size, size, 13, Color.white); }
                    else if (hash % 9 == 0) chunk.Nature.Sprite(x + .5f, y - .1f, 1.1f, 1.1f, 14, Color.white);
                }
                if (cell.BuildingId == 0 && !water && node.Type != ResourceType.None)
                {
                    float offset = ((hash >> 10) % 100) / 200f - .25f;
                    if (node.Type == ResourceType.Tree)
                    {
                        bool fir = cell.Temperature < .42f || hash % 5 == 0;
                        float size = node.Amount <= 0 ? 1.18f : 1.40f + (hash % 5) * .13f;
                        chunk.Nature.Sprite(x + .5f + offset, y - .10f, size, size, node.Amount <= 0 ? ResourceArt.Stump : fir ? 1 : 0, cell.Terrain == TerrainType.Snow ? Color.Lerp(WeatherView.TreeColor(cell), new Color(.85f, .97f, .92f), .7f) : WeatherView.TreeColor(cell));
                    }
                    else
                    {
                        int sprite = ResourceArt.NodeSprite(node.Type, node.Amount, node.Capacity);
                        if (sprite >= 0)
                        {
                            // All nodes remain present; vary their appearance without consuming simulation RNG.
                            float size = .86f + ((hash >> 15) % 27) * .01f;
                            float jitterX = (((hash >> 9) % 101) * .01f - .5f) * .35f;
                            float jitterY = (((hash >> 18) % 101) * .01f - .5f) * .32f;
                            chunk.Nature.Add(x + .5f + jitterX - size * .5f, y + .03f + jitterY, size, size, PixelArt.Uv(sprite), Color.white, (hash & 16) != 0);
                        }
                    }
                }
            }
            chunk.Texture.SetPixels32(pixels); chunk.Texture.Apply(false, false); chunk.Nature.Submit(); chunk.Bridges.Submit();
        }
        private bool IsLand(int x, int y) { return world.InBounds(x, y) && !IsWater(x, y); }
        private InfrastructureCell InfrastructureAt(int x, int y) { return world.Infrastructure != null && world.InBounds(x, y) ? world.Infrastructure[world.Index(x, y)] : default(InfrastructureCell); }
        private bool IsWater(int x, int y) { return world.InBounds(x, y) && (world.Cells[world.Index(x, y)].Terrain == TerrainType.DeepWater || world.Cells[world.Index(x, y)].Terrain == TerrainType.ShallowWater); }
        private int RiverConnections(int x, int y, TileData tile)
        {
            int index = world.Index(x, y), mask = 0;
            if (RiverConnects(index, tile.DrainageParent, x - 1, y)) mask |= 1;
            if (RiverConnects(index, tile.DrainageParent, x + 1, y)) mask |= 2;
            if (RiverConnects(index, tile.DrainageParent, x, y - 1)) mask |= 4;
            if (RiverConnects(index, tile.DrainageParent, x, y + 1)) mask |= 8;
            return mask;
        }
        private bool RiverConnects(int index, int parent, int x, int y)
        {
            if (!world.InBounds(x, y)) return false;
            int neighborIndex = world.Index(x, y); TileData neighbor = world.Cells[neighborIndex];
            bool neighborCourse = neighbor.River || InfrastructureAt(x, y).Stream > .01f;
            return (parent == neighborIndex && (neighborCourse || IsWater(x, y))) || (neighborCourse && neighbor.DrainageParent == index);
        }
        private static bool RiverPixel(int x, int y, int connections)
        {
            bool centerX = x == 3 || x == 4, centerY = y == 3 || y == 4;
            return (centerX && centerY) || (centerY && (((connections & 1) != 0 && x <= 4) || ((connections & 2) != 0 && x >= 3))) || (centerX && (((connections & 4) != 0 && y <= 4) || ((connections & 8) != 0 && y >= 3)));
        }
        private int RoadConnections(int x, int y)
        {
            int mask = 0;
            if (InfrastructureAt(x - 1, y).RoadLevel > 0) mask |= 1;
            if (InfrastructureAt(x + 1, y).RoadLevel > 0) mask |= 2;
            if (InfrastructureAt(x, y - 1).RoadLevel > 0) mask |= 4;
            if (InfrastructureAt(x, y + 1).RoadLevel > 0) mask |= 8;
            return mask;
        }
        private static bool PathPixel(int x, int y, int mask, int min, int max)
        {
            bool centerX = x >= min && x <= max, centerY = y >= min && y <= max;
            return (centerX && centerY) || (centerY && (((mask & 1) != 0 && x <= max) || ((mask & 2) != 0 && x >= min))) ||
                (centerX && (((mask & 4) != 0 && y <= max) || ((mask & 8) != 0 && y >= min)));
        }
        private bool NearBuilding(int x, int y)
        {
            foreach (BuildingData building in world.Buildings) { int dx = x - building.X, dy = y - building.Y; if (dx * dx + dy * dy < 8) return true; }
            return false;
        }
        private float ForestCluster(int x, int y)
        {
            int gx = x / 7, gy = y / 7; float tx = Mathf.SmoothStep(0, 1, (x % 7) / 7f), ty = Mathf.SmoothStep(0, 1, (y % 7) / 7f);
            float a = (PixelArt.Hash(gx, gy, world.Seed) % 1000) / 1000f, b = (PixelArt.Hash(gx + 1, gy, world.Seed) % 1000) / 1000f;
            float c = (PixelArt.Hash(gx, gy + 1, world.Seed) % 1000) / 1000f, d = (PixelArt.Hash(gx + 1, gy + 1, world.Seed) % 1000) / 1000f;
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }
        public void Dispose()
        {
            UnityEngine.Object.Destroy(depthSpriteMaterial); UnityEngine.Object.Destroy(depthEffectMaterial); UnityEngine.Object.Destroy(wagonMaterial);
            industryBuildings.Dispose(); industryCargo.Dispose(); industryEffects.Dispose(); UnityEngine.Object.Destroy(industryMaterial); UnityEngine.Object.Destroy(industryAtlas);
            weatherView.Dispose(); maritimeView.Dispose(); remainsView.Dispose();
            if (chunks != null) foreach (Chunk chunk in chunks) { chunk.Ground.Dispose(); chunk.Nature.Dispose(); chunk.Bridges.Dispose(); UnityEngine.Object.Destroy(chunk.Texture); UnityEngine.Object.Destroy(chunk.Material); }
            waterFlow.Dispose(); wagons.Dispose(); UnityEngine.Object.Destroy(transportMaterial); UnityEngine.Object.Destroy(transportAtlas);
            buildings.Dispose(); people.Dispose(); waterLife.Dispose(); wildlife.Dispose(); equipmentBack.Dispose(); equipmentFront.Dispose(); agriculture.Dispose(); ruralStructures.Dispose(); crowns.Dispose(); agriculturalCargo.Dispose(); citizenSprites.Dispose(); borders.Dispose(); cursor.Dispose(); UnityEngine.Object.Destroy(wildlifeAtlas); UnityEngine.Object.Destroy(agricultureAtlas); UnityEngine.Object.Destroy(wildlifeMaterial); UnityEngine.Object.Destroy(agricultureMaterial); UnityEngine.Object.Destroy(agriculturalGroundMaterial); UnityEngine.Object.Destroy(atlas); UnityEngine.Object.Destroy(spriteMaterial); UnityEngine.Object.Destroy(Minimap); UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
