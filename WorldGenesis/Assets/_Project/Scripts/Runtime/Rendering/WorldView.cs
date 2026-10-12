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
        private static readonly Color ShoreColor = PixelArt.Hex("80b7ad");
        private static readonly Color RiverColor = PixelArt.Hex("5a9d9c");
        private static readonly Color ScorchedColor = PixelArt.Hex("7e553c");
        private sealed class Chunk
        {
            public int X, Y, Width, Height;
            public uint Hash;
            public bool Queued;
            public Texture2D Texture;
            public Color32[] Pixels;
            public Material Material;
            public QuadBatch Ground, Nature;
        }
        private readonly Transform root;
        private readonly Texture2D atlas;
        private readonly Material spriteMaterial;
        private readonly CitizenSprites citizenSprites;
        private readonly QuadBatch buildings, people, borders, cursor;
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
        public int SelectedUnitId;
        public Material SpriteMaterial => spriteMaterial;

        public WorldView(Transform parent)
        {
            root = new GameObject("World • batched pixel rendering").transform; root.SetParent(parent, false);
            atlas = PixelArt.CreateAtlas(); spriteMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = atlas, name = "Shared pixel atlas" };
            buildings = new QuadBatch("Buildings", root, spriteMaterial, 3);
            people = new QuadBatch("Life and citizen markers", root, spriteMaterial, 5);
            citizenSprites = new CitizenSprites(root);
            borders = new QuadBatch("Territories", root, spriteMaterial, 1);
            cursor = new QuadBatch("Brush", root, spriteMaterial, 8);
        }

        public void SetWorld(WorldState value)
        {
            if (chunks != null) foreach (Chunk chunk in chunks) { chunk.Ground.Dispose(); chunk.Nature.Dispose(); UnityEngine.Object.Destroy(chunk.Texture); UnityEngine.Object.Destroy(chunk.Material); }
            if (Minimap != null) UnityEngine.Object.Destroy(Minimap);
            world = value; revision = -1; resourceRevision = -1; nextRefresh = 0; lastBuildingCount = -1;
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
                chunk.Nature = new QuadBatch("Nature " + cx + "," + cy, root, spriteMaterial, 2);
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
            buildings.Clear();
            if (world.GroundStocks != null) for (int i = 0; i < world.GroundStocks.Count; i++)
            {
                GroundStockData stock = world.GroundStocks[i]; if (stock.Amount <= 0) continue;
                int glyph = stock.IsFood ? ResourceArt.FoodBasket : ResourceArt.CargoSprite(stock.Item);
                if (glyph >= 0) buildings.Sprite(stock.X, stock.Y - .15f, .82f, .82f, glyph, stock.IsFood ? Color.white : ResourceArt.CargoColor(stock.Item));
            }
            for (int i = 0; i < world.Buildings.Count; i++)
            {
                BuildingData b = world.Buildings[i]; if (b.Health <= 0) continue;
                int sprite = ResolveBuildingSprite(b.Type);
                float size = b.Type == BuildingType.Hall ? 2.5f : b.Type == BuildingType.Sawmill || b.Type == BuildingType.Stoneworks ? 2.8f : 2.3f;
                buildings.Sprite(b.X + .5f, b.Y - .15f, size, size, sprite, Color.white);
                SettlementData settlement = world.GetSettlement(b.SettlementId);
                if (settlement != null && b.Type == BuildingType.Hall)
                {
                    Color flag = PixelArt.Factions[(uint)settlement.ColorIndex % (uint)PixelArt.Factions.Length];
                    buildings.Sprite(b.X - .48f, b.Y + .9f, .06f, 1.2f, 12, PixelArt.Hex("765a40"));
                    buildings.Sprite(b.X - .20f, b.Y + 1.76f, .57f, .3f, 12, flag);
                }
                DrawBuildingSupplies(b);
            }
            people.Clear(); citizenSprites.Begin(world);
            Camera camera = Camera.main;
            bool showDetails = camera != null && camera.orthographicSize <= 25;
            float simulationTime = world.Tick * SimulationEngine.TickSeconds;
            for (int i = 0; i < world.Units.Count; i++)
            {
                UnitData unit = world.Units[i]; if (unit.Health <= 0) continue;
                Color tint = Color.white;
                SettlementData settlement = world.GetSettlement(unit.SettlementId);
                if (settlement != null) tint = Color.Lerp(Color.white, PixelArt.Factions[(uint)settlement.ColorIndex % (uint)PixelArt.Factions.Length], .37f);
                if (unit.Infected) tint = Color.Lerp(tint, new Color(.65f, .9f, .25f), .55f);
                bool human = unit.Species == SpeciesType.Human;
                float bob = unit.PathCursor < unit.Path.Count ? Mathf.Sin(simulationTime * 10 + unit.Id) * .06f : 0;
                if (human)
                {
                    Color humanTint = unit.Infected ? new Color(.74f, .93f, .65f) : Color.white;
                    citizenSprites.Add(world, unit, settlement == null ? 0 : settlement.ColorIndex, humanTint);
                }
                else
                {
                    float size = (int)unit.Species < 4 && unit.IsChild ? .81f : 1.2f;
                    people.Sprite(unit.X, unit.Y - .15f + bob, size, size, ResourceArt.CitizenSprite(unit), tint);
                }
                float head = human ? (unit.IsChild ? .8f : unit.Profession == UnitProfession.Guard ? 1.62f : unit.Sex == Sex.Female ? 1.28f : 1.15f) : (int)unit.Species < 4 && unit.IsChild ? .64f : .96f;
                if (unit.Blessed) people.Sprite(unit.X, unit.Y + head, .2f, .08f, 12, PixelArt.Gold);
                if (showDetails && (int)unit.Species < 4)
                {
                    Color role = unit.IsChild ? PixelArt.Hex("dbb0d5") : unit.Profession == UnitProfession.Forester ? PixelArt.Teal : unit.Profession == UnitProfession.Guard ? PixelArt.Hex("a6b8ed") : PixelArt.Gold;
                    people.Sprite(unit.X - .17f, unit.Y + head + .13f, .16f, .12f, 12, role);
                    if (unit.Task == UnitTask.Flee || unit.Task == UnitTask.Eat || unit.Task == UnitTask.Rest || unit.Task == UnitTask.Socialize || unit.Task == UnitTask.Learn || unit.Task == UnitTask.Play)
                    {
                        Color activity = unit.Task == UnitTask.Flee ? PixelArt.Hex("ed8677") : unit.Task == UnitTask.Eat ? PixelArt.Teal : unit.Task == UnitTask.Rest ? PixelArt.Hex("92abb5") : PixelArt.Hex("d5a9d9");
                        people.Sprite(unit.X + .08f, unit.Y + head + .13f, .14f, .12f, 12, activity);
                    }
                }
                DrawCargo(unit, human ? citizenSprites.Facing(unit.Id) : TravelDirection(unit), simulationTime);
                if (unit.Id == SelectedUnitId)
                {
                    people.Sprite(unit.X, unit.Y + head + .38f, .85f, .1f, 12, PixelArt.Ink);
                    people.Sprite(unit.X - .425f + .425f * Mathf.Clamp01(unit.Health / unit.MaxHealth), unit.Y + head + .38f, .85f * Mathf.Clamp01(unit.Health / unit.MaxHealth), .1f, 12, PixelArt.Teal);
                }
            }
            buildings.Submit(); people.Submit(); citizenSprites.Submit();
        }

        public Sprite CreateHumanIcon() { return citizenSprites.CreatePortrait(); }

        public static int ResolveBuildingSprite(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.Hall: return ResourceArt.Hearth;
                case BuildingType.House: return 3; case BuildingType.Farm: return 4; case BuildingType.Tower: return 5;
                case BuildingType.Sawmill: return ResourceArt.Sawmill;
                case BuildingType.Stoneworks: return ResourceArt.Stoneworks;
                case BuildingType.Kiln: return ResourceArt.Kiln; default: return 3;
            }
        }
        private Vector2 TravelDirection(UnitData unit)
        {
            if (unit.Path != null && unit.PathCursor < unit.Path.Count)
            {
                int next = unit.Path[unit.PathCursor]; Vector2 delta = new Vector2(next % world.Width + .5f - unit.X, next / world.Width + .5f - unit.Y);
                if (delta.sqrMagnitude > .0001f) return delta.normalized;
            }
            return Vector2.right;
        }
        public static Vector2 CargoPosition(UnitData unit, Vector2 direction)
        {
            direction = direction.sqrMagnitude > .0001f ? direction.normalized : Vector2.right;
            float size = unit.IsChild ? .7f : 1;
            // Sprite() takes a quad's bottom Y, not its centre. Resource pixels already
            // occupy the middle of that quad, so a .5 Y offset would cover the face.
            return new Vector2(unit.X, unit.Y) + (unit.CargoKind == ItemType.Log ? -direction * .82f + Vector2.up * .03f : new Vector2((direction.x < 0 ? -.52f : .52f) * size, -.08f * size));
        }
        private void DrawCargo(UnitData unit, Vector2 direction, float simulationTime)
        {
            bool resource = unit.CargoAmount > .001f && unit.CargoKind != ItemType.None;
            bool food = !resource && unit.CarriedFood > .001f;
            bool legacyPlanks = !resource && !food && unit.CarriedWood > .001f;
            if (!resource && !food && !legacyPlanks) return;
            Vector2 position = CargoPosition(unit, direction);
            if (resource && unit.CargoKind == ItemType.Log)
            {
                Vector2 d = direction.sqrMagnitude > .0001f ? direction.normalized : Vector2.right;
                float angle = Mathf.Atan2(d.y, d.x);
                // Only the carried log rotates. Rope links its front end to the worker's hand.
                // A log dragged north of a southbound citizen lies behind the body.
                QuadBatch cargoBatch = d.y < -.2f ? buildings : people;
                cargoBatch.OrientedSprite(position, 1.26f, .83f, angle, ResourceArt.Log, Color.white);
                Vector2 end = position + d * .55f, hand = new Vector2(unit.X, unit.Y + .26f), rope = hand - end;
                cargoBatch.OrientedSprite((end + hand) * .5f, rope.magnitude, .035f, Mathf.Atan2(rope.y, rope.x), 12, PixelArt.Hex("bfac7f"));
                return;
            }
            if (unit.Species == SpeciesType.Human && !unit.IsChild)
            {
                CitizenSprites.Pose pose = CitizenSprites.ResolvePose(world, unit, direction);
                // The authored carry rows raise the hands. Keep the parcel beside that
                // hand; work rows hold it at the waist and never wear a basket as a hat.
                if (!pose.Warrior && (pose.Row == 4 || pose.Row == 5))
                    position = new Vector2(unit.X + (direction.x < 0 ? -.55f : .55f), unit.Y + .42f);
            }
            float bob = unit.Path != null && unit.PathCursor < unit.Path.Count ? Mathf.Sin(simulationTime * 10 + unit.Id) * .025f : 0;
            float size = unit.IsChild ? .58f : .75f;
            ItemType item = legacyPlanks ? ItemType.Plank : unit.CargoKind;
            int glyph = food ? ResourceArt.FoodBasket : ResourceArt.CargoSprite(item);
            people.Sprite(position.x, position.y + bob, size, size, glyph, food ? Color.white : ResourceArt.CargoColor(item));
        }
        private void DrawBuildingSupplies(BuildingData building)
        {
            if (building.Inventory != null)
            {
                int drawn = 0;
                for (int item = 1; item < building.Inventory.Length && drawn < 3; item++)
                {
                    if (building.Inventory[item] <= .001f) continue;
                    ItemType type = (ItemType)item; int sprite = ResourceArt.CargoSprite(type); if (sprite < 0) continue;
                    buildings.Sprite(building.X - .3f + drawn * .48f, building.Y - .27f, .78f, .78f, sprite, ResourceArt.CargoColor(type)); drawn++;
                }
            }
            UnitData worker = building.WorkerId != 0 ? world.GetUnit(building.WorkerId) : null;
            if (worker == null || worker.Task != UnitTask.Process || worker.WorkTimer <= 0 || worker.PathCursor < worker.Path.Count || building.ActiveRecipe == RecipeType.None) return;
            float phase = building.ProcessProgress * 5;
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
            }
            // Shore and river joins depend on the one-cell halo across chunk boundaries.
            for (int y = chunk.Y - 1; y <= chunk.Y + chunk.Height; y++) for (int x = chunk.X - 1; x <= chunk.X + chunk.Width; x++)
            {
                if (!world.InBounds(x, y) || (x >= chunk.X && x < chunk.X + chunk.Width && y >= chunk.Y && y < chunk.Y + chunk.Height)) continue;
                TileData adjacent = world.Cells[world.Index(x, y)];
                unchecked { hash = (hash ^ (uint)((int)adjacent.Terrain * 1009 + adjacent.DrainageParent * 23 + (adjacent.River ? 7 : 0))) * 16777619; }
            }
            return hash;
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
            for (int i = 0; i < minimapPixels.Length; i++) minimapPixels[i] = PixelArt.Terrain(world.Cells[i].Terrain);
            Minimap.SetPixels32(minimapPixels); Minimap.Apply(false, false);
        }

        private void Rebuild(Chunk chunk)
        {
            int textureWidth = chunk.Width * PixelsPerCell;
            Color32[] pixels = chunk.Pixels; chunk.Nature.Clear();
            for (int localY = chunk.Height - 1; localY >= 0; localY--) for (int localX = 0; localX < chunk.Width; localX++)
            {
                int x = chunk.X + localX, y = chunk.Y + localY;
                TileData cell = world.Cells[world.Index(x, y)]; uint hash = PixelArt.Hash(x, y, world.Seed);
                Color baseColor = PixelArt.Terrain(cell.Terrain);
                bool water = cell.Terrain == TerrainType.DeepWater || cell.Terrain == TerrainType.ShallowWater;
                int riverConnections = cell.River ? RiverConnections(x, y, cell) : 0;
                float light = .96f + (hash % 13) / 160f;
                for (int py = 0; py < PixelsPerCell; py++) for (int px = 0; px < PixelsPerCell; px++)
                {
                    float variation = 1;
                    uint grain = PixelArt.Hash(x * PixelsPerCell + px, y * PixelsPerCell + py, world.Seed);
                    if (grain % 31 == 0) variation = 1.1f; else if (grain % 29 == 0) variation = .92f;
                    if (water && py == (int)(hash % 5 + 1) && px > 1 && px < 6 && hash % 6 == 0) variation = 1.16f;
                    if (cell.Terrain == TerrainType.Mountain && grain % 17 == 0) variation *= .86f;
                    Color color = baseColor * light * variation; color.a = 1;
                    if (water && cell.Terrain == TerrainType.ShallowWater && ((py == 7 && IsLand(x, y + 1)) || (px == 0 && IsLand(x - 1, y)))) color = ShoreColor;
                    if (!water && ((py == 0 && IsWater(x, y - 1)) || (px == 7 && IsWater(x + 1, y)))) color *= .82f;
                    if (cell.River && RiverPixel(px, py, riverConnections)) color = RiverColor;
                    if (cell.Fire > 0) color = Color.Lerp(color, ScorchedColor, .35f);
                    pixels[(localY * PixelsPerCell + py) * textureWidth + localX * PixelsPerCell + px] = color;
                }
                ResourceNodeData node = world.GetResourceAt(x, y);
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
                        chunk.Nature.Sprite(x + .5f + offset, y - .10f, size, size, node.Amount <= 0 ? ResourceArt.Stump : fir ? 1 : 0, cell.Terrain == TerrainType.Snow ? new Color(.85f, .97f, .92f) : Color.white);
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
            chunk.Texture.SetPixels32(pixels); chunk.Texture.Apply(false, false); chunk.Nature.Submit();
        }
        private bool IsLand(int x, int y) { return world.InBounds(x, y) && !IsWater(x, y); }
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
            return (parent == neighborIndex && (neighbor.River || IsWater(x, y))) || (neighbor.River && neighbor.DrainageParent == index);
        }
        private static bool RiverPixel(int x, int y, int connections)
        {
            bool centerX = x == 3 || x == 4, centerY = y == 3 || y == 4;
            return (centerX && centerY) || (centerY && (((connections & 1) != 0 && x <= 4) || ((connections & 2) != 0 && x >= 3))) || (centerX && (((connections & 4) != 0 && y <= 4) || ((connections & 8) != 0 && y >= 3)));
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
            if (chunks != null) foreach (Chunk chunk in chunks) { chunk.Ground.Dispose(); chunk.Nature.Dispose(); UnityEngine.Object.Destroy(chunk.Texture); UnityEngine.Object.Destroy(chunk.Material); }
            buildings.Dispose(); people.Dispose(); citizenSprites.Dispose(); borders.Dispose(); cursor.Dispose(); UnityEngine.Object.Destroy(atlas); UnityEngine.Object.Destroy(spriteMaterial); UnityEngine.Object.Destroy(Minimap); UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
