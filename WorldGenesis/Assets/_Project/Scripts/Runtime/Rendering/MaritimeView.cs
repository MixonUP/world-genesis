using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    public sealed class MaritimeView : IDisposable
    {
        private readonly Texture2D atlas;
        private readonly Material material;
        private readonly QuadBatch buildings, ships, manifest, effects, apron;
        private readonly Dictionary<int, List<BuildingData>> networks = new Dictionary<int, List<BuildingData>>();
        private long networkTick = -1;
        private int networkRevision = -1;
        private WorldState previousWorld;
        private Func<int, List<BuildingData>> harborWarehouseQuery;
        public void SetQueries(Func<int, List<BuildingData>> query) { harborWarehouseQuery = query; networkTick = -1; }
        public MaritimeView(Transform parent, Material pixels, Material translucentPixels, Material groundPixels)
        {
            atlas = MaritimeArt.CreateAtlas(); material = WorldDepth.Material(atlas);
            buildings = new QuadBatch("Ports shipyards and harbor stores", parent, material, 30);
            ships = new QuadBatch("Directional maritime hulls", parent, material, 42);
            manifest = new QuadBatch("Actual ship cargo and crew", parent, pixels, 43);
            effects = new QuadBatch("Actual ship movement and combat", parent, translucentPixels, 41);
            apron = new QuadBatch("Connected harbor aprons", parent, groundPixels, 13);
        }
        public void Begin() { buildings.Clear(); ships.Clear(); manifest.Clear(); effects.Clear(); apron.Clear(); }
        public void DrawBuilding(WorldState world, BuildingData building) { MaritimeArt.DrawJetty(apron, world, building); MaritimeArt.DrawBuilding(buildings, building); MaritimeArt.DrawShipyardWork(manifest, world, building); }
        public void Finish(WorldState world, int selectedShipId)
        {
            DrawConnections(world);
            if (world.Ships != null) foreach (ShipData ship in world.Ships)
            {
                MaritimeArt.DrawWake(effects, world, ship); MaritimeArt.DrawShip(ships, ship);
                MaritimeArt.DrawManifest(manifest, world, ship); MaritimeArt.DrawAttack(effects, world, ship);
                if (ship.Id == selectedShipId && MaritimeArt.IsVisible(ship))
                {
                    float size = MaritimeArt.ShipSize(ship.Kind);
                    manifest.SetFootDepth(ship.Y, WorldDepth.Front);
                    manifest.Sprite(ship.X, ship.Y + size * .68f, 1.25f, .07f, 12, PixelArt.Ink);
                    float health = Mathf.Clamp01(ship.Health / ship.MaxHealth);
                    manifest.Sprite(ship.X - .625f + .625f * health, ship.Y + size * .68f, 1.25f * health, .07f, 12, PixelArt.Teal);
                }
            }
            buildings.Submit(); ships.Submit(); manifest.Submit(); effects.Submit(); apron.Submit();
        }
        private void DrawConnections(WorldState world)
        {
            if (harborWarehouseQuery == null) return;
            if (previousWorld != world || networkRevision != world.TerrainRevision || world.Tick - networkTick >= 10 || networkTick < 0)
            {
                previousWorld = world; networkTick = world.Tick; networkRevision = world.TerrainRevision; networks.Clear();
                foreach (BuildingData port in world.Buildings) if (port.Type == BuildingType.Port && port.Health > 0) networks[port.Id] = harborWarehouseQuery(port.Id);
            }
            foreach (KeyValuePair<int, List<BuildingData>> entry in networks)
            {
                BuildingData port = world.GetBuilding(entry.Key); if (port == null || port.Health <= 0) continue;
                foreach (BuildingData warehouse in entry.Value)
                {
                    if (warehouse.Health <= 0) continue;
                    apron.Sprite(warehouse.X + .5f, warehouse.Y + .08f, 1.1f, .17f, 12, PixelArt.Hex("b7a879"));
                    // Only adjacent footprints get a joined apron. Longer connected routes
                    // remain real roads rather than invented straight connectors through walls.
                    JoinApron(port, warehouse);
                    foreach (BuildingData other in entry.Value) if (other.Id < warehouse.Id) JoinApron(other, warehouse);
                }
            }
        }
        private void JoinApron(BuildingData a, BuildingData b)
        {
            if (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) != 1) return;
            Vector2 from = new Vector2(a.X + .5f, a.Y + .2f), to = new Vector2(b.X + .5f, b.Y + .2f), d = to - from;
            apron.OrientedSprite((from + to) * .5f, d.magnitude + .3f, .45f, Mathf.Atan2(d.y, d.x), 12, PixelArt.Hex("998b6c"));
        }
        public void Dispose() { buildings.Dispose(); ships.Dispose(); manifest.Dispose(); effects.Dispose(); apron.Dispose(); UnityEngine.Object.Destroy(material); UnityEngine.Object.Destroy(atlas); }
    }
}
