using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using WorldGenesis.Runtime;
using WorldGenesis.Simulation;

namespace WorldGenesis.Tests
{
    public sealed class MaritimePresentationTests
    {
        [Test]
        public void OriginalAtlasHasNineDistinctHullsEightViewsAndThreeWarehouseLevels()
        {
            Texture2D atlas = MaritimeArt.CreateAtlas(true);
            try
            {
                var frames = new HashSet<ulong>();
                for (int kind = 0; kind < 9; kind++) for (int direction = 0; direction < 8; direction++)
                    Assert.IsTrue(frames.Add(Hash(atlas, MaritimeArt.UvShip((ShipKind)kind, direction))), "Distinct hull/view " + kind + "/" + direction);
                Assert.AreEqual(72, frames.Count);
                frames.Clear();
                for (BuildingType type = BuildingType.Port; type <= BuildingType.HarborWarehouse; type++)
                    for (int grade = 1; grade <= (type == BuildingType.HarborWarehouse ? 3 : 5); grade++)
                        Assert.IsTrue(frames.Add(Hash(atlas, MaritimeArt.UvBuilding(type, grade))), type + " " + grade);
                Assert.AreEqual(18, frames.Count);
                Assert.AreEqual(MaritimeArt.UvBuilding(BuildingType.HarborWarehouse, 3), MaritimeArt.UvBuilding(BuildingType.HarborWarehouse, 5));
                Assert.AreEqual(FilterMode.Point, atlas.filterMode);
            }
            finally { Object.Destroy(atlas); }
        }
        [UnityTest]
        public IEnumerator MotionUsesSimulationClockAndOnlyActualManifestProducesDeckCargo()
        {
            SimulationEngine engine = Fixture(out ShipData ship, out BuildingData store, out UnitData resident);
            GameObject go = new GameObject("Marine presentation proof"); Texture2D atlas = PixelArt.CreateAtlas(); Material material = WorldDepth.Material(atlas);
            QuadBatch batch = new QuadBatch("Proof mesh", go.transform, material, 40); Mesh mesh = go.GetComponentInChildren<MeshFilter>().sharedMesh;
            try
            {
                WorldState world = engine.World; string before = JsonUtility.ToJson(world);
                MaritimeArt.DrawWake(batch, world, ship); batch.Submit(); Assert.AreEqual(0, mesh.vertexCount, "Docked vessel must not travel in its wake.");
                batch.Clear(); MaritimeArt.DrawManifest(batch, world, ship); batch.Submit(); Assert.AreEqual(0, mesh.vertexCount, "An empty boat has no painted supplies or invented sailors.");
                ship.Phase = ShipPhase.Sailing; ship.Path.Add(world.Index(9, 5));
                batch.Clear(); MaritimeArt.DrawWake(batch, world, ship); batch.Submit(); Assert.Greater(mesh.vertexCount, 0);
                Vector3[] vertices = mesh.vertices; Color32[] colors = mesh.colors32; yield return null;
                batch.Clear(); MaritimeArt.DrawWake(batch, world, ship); batch.Submit(); CollectionAssert.AreEqual(vertices, mesh.vertices); CollectionAssert.AreEqual(colors, mesh.colors32);
                world.Tick += 3; batch.Clear(); MaritimeArt.DrawWake(batch, world, ship); batch.Submit(); Assert.IsFalse(vertices.SequenceEqual(mesh.vertices));
                ship.PathCursor = ship.Path.Count; batch.Clear(); MaritimeArt.DrawWake(batch, world, ship); batch.Submit(); Assert.AreEqual(0, mesh.vertexCount);
                ship.Cargo[(int)ItemType.IronIngot] = 2; ship.Food = 3; ship.Water = 4;
                ship.CrewIds.Add(resident.Id); Assert.AreEqual(0, MaritimeArt.VisibleCrew(world, ship), "A reserved resident has not boarded yet.");
                resident.ShipId = ship.Id; ship.CaptainUnitId = resident.Id;
                Assert.AreEqual(1, MaritimeArt.VisibleCrew(world, ship));
                string manifest = JsonUtility.ToJson(world);
                batch.Clear(); MaritimeArt.DrawManifest(batch, world, ship); batch.Submit(); Assert.Greater(mesh.vertexCount, 24);
                Assert.AreEqual(manifest, JsonUtility.ToJson(world));
                ship.Phase = ShipPhase.Abroad; batch.Clear(); MaritimeArt.DrawManifest(batch, world, ship); batch.Submit(); Assert.AreEqual(0, mesh.vertexCount);
                Assert.IsNotEmpty(before);
            }
            finally { batch.Dispose(); Object.Destroy(material); Object.Destroy(atlas); Object.Destroy(go); }
        }
        [UnityTest]
        public IEnumerator EmbarkedBodyAndCargoDisappearButHullRendersThroughActualMaterialAndCamera()
        {
            SimulationEngine engine = Fixture(out ShipData ship, out BuildingData store, out UnitData unit);
            GameObject go = new GameObject("Embarked visual fixture"); WorldView view = new WorldView(go.transform);
            GameObject cameraGo = new GameObject("Maritime render camera"); Camera camera = cameraGo.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
            camera.orthographicSize = 2.8f; camera.transform.position = new Vector3(ship.X, ship.Y + .45f, -10); camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            RenderTexture target = new RenderTexture(192, 192, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
            try
            {
                unit.ShipId = ship.Id; unit.CargoKind = ItemType.IronIngot; unit.CargoAmount = 1;
                ship.CrewIds.Add(unit.Id); view.SetWorld(engine.World);
                Mesh body = go.GetComponentsInChildren<MeshFilter>().First(m => m.name == "Animated human citizens").sharedMesh;
                Mesh carried = go.GetComponentsInChildren<MeshFilter>().First(m => m.name == "Manufactured goods").sharedMesh;
                Assert.AreEqual(0, body.vertexCount); Assert.AreEqual(0, carried.vertexCount);
                Assert.AreEqual(new Vector2(ship.X, ship.Y), view.GetPresentationPosition(unit));
                foreach (Transform child in go.GetComponentsInChildren<Transform>()) child.gameObject.layer = 30;
                foreach (MeshRenderer renderer in go.GetComponentsInChildren<MeshRenderer>()) renderer.enabled = renderer.name == "Directional maritime hulls";
                Color32[] north = Capture(camera, target); Assert.Greater(north.Count(c => c.a > 250), 80);
                ship.HeadingX = 1; ship.HeadingY = 0; view.Update(1);
                Color32[] east = Capture(camera, target); Assert.Greater(north.Zip(east, (a, b) => !a.Equals(b)).Count(d => d), 150, "Rotating actual heading must change native hull silhouette, not only metadata.");
                ship.Phase = ShipPhase.Departed; view.Update(2);
                Assert.AreEqual(0, Capture(camera, target).Count(c => c.a > 0));
                yield return null;
            }
            finally { view.Dispose(); camera.targetTexture = null; target.Release(); Object.Destroy(target); Object.Destroy(cameraGo); Object.Destroy(go); }
        }
        [UnityTest]
        public IEnumerator ShipInspectorButtonsTransferFiniteProvisionsThroughRealConnectedWarehouse()
        {
            GameBootstrap game = GameBootstrap.Instance;
            if (game == null)
            {
                LogAssert.Expect(LogType.Log, "World Genesis ready. Seed=1618033; size=128x128; save path=" + SaveStore.DefaultPath);
                game = new GameObject("Marine HUD command integration").AddComponent<GameBootstrap>();
            }
            yield return null; game.Paused = true;
            SimulationEngine engine = Fixture(out ShipData ship, out BuildingData store, out UnitData resident);
            store.PortFoodStored = 6; store.WaterStored = 9; store.Inventory[(int)ItemType.IronIngot] = 20;
            var extension = new BuildingData { Id = engine.World.NextId++, Type = BuildingType.HarborWarehouse, SettlementId = store.SettlementId, X = 5, Y = 7, Level = 1, EntranceCell = engine.World.Index(5, 6), PortFoodStored = 6 };
            engine.World.Buildings.Add(extension); engine.World.Cells[engine.World.Index(5, 7)].BuildingId = extension.Id; engine.World.Infrastructure[extension.EntranceCell].RoadLevel = 1;
            engine.World.NavigationRevision++; engine.World.TerrainRevision++;
            Assert.AreEqual(2, engine.GetHarborWarehouses(ship.DockedPortId).Count);
            typeof(GameBootstrap).GetMethod("ReplaceWorld", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(game, new object[] { engine });
            try
            {
                game.InspectShip(ship.Id); yield return null;
                Assert.AreEqual(ship.Id, game.SelectedShipId);
                string description = GameHud.ShipDescription(engine, ship);
                StringAssert.Contains("Капитан: не назначен", description); StringAssert.Contains("ТРЮМ 0/40", description);
                Button(game, "Трюм").onClick.Invoke(); yield return null;
                Button(game, "Склад → трюм").onClick.Invoke(); yield return null;
                Assert.AreEqual(10, ship.Food); Assert.AreEqual(2, store.PortFoodStored + extension.PortFoodStored, "One click uses both physical warehouse sections.");
                Button(game, "Склад → трюм").onClick.Invoke(); yield return null;
                Assert.AreEqual(10, ship.Food, "A rejected 10-unit request cannot invent the missing 8 portions.");
                Button(game, "Трюм → склад").onClick.Invoke(); yield return null;
                Assert.AreEqual(0, ship.Food); Assert.AreEqual(12, store.PortFoodStored + extension.PortFoodStored);
                Button(game, "Команда").onClick.Invoke(); yield return null;
                Button(game, "Назначить капитаном").onClick.Invoke(); yield return null;
                Assert.AreEqual(UnitTask.Dockwork, resident.Task, "The button queues actual physical boarding, without inserting a fictional captain into the ship.");
                Assert.AreEqual(0, ship.CaptainUnitId); Assert.AreEqual(0, resident.ShipId);
                Assert.IsTrue(engine.World.DockOrders.Any(o => o.Captain && o.WorkerId == resident.Id && o.ShipId == ship.Id));
                LogAssert.NoUnexpectedReceived();
            }
            finally { Object.Destroy(game.gameObject); }
            yield return null;
        }
        [Test]
        public void JettyFollowsPhysicalBerthAndNeverCrossesDryCells()
        {
            SimulationEngine engine = Fixture(out ShipData ship, out BuildingData store, out UnitData resident);
            WorldState world = engine.World; BuildingData port = world.GetBuilding(ship.HomePortId);
            GameObject go = new GameObject("Physical jetty proof"); Texture2D atlas = PixelArt.CreateAtlas(); Material material = WorldDepth.Material(atlas);
            QuadBatch batch = new QuadBatch("Jetty proof mesh", go.transform, material, 13); Mesh mesh = go.GetComponentInChildren<MeshFilter>().sharedMesh;
            try
            {
                port.BerthCell = engine.FindBerth(port.X, port.Y);
                Assert.AreEqual(world.Index(9, 5), port.BerthCell, "A clear two-cell water approach provides room for the actual docked hull.");
                string before = JsonUtility.ToJson(world);
                MaritimeArt.DrawJetty(batch, world, port); batch.Submit(); Assert.Greater(mesh.vertexCount, 32);
                Assert.Less(mesh.bounds.max.x, 9.5f); Assert.Greater(mesh.bounds.max.x, 9);
                Assert.GreaterOrEqual(mesh.bounds.min.x, 7.5f); Assert.Less(mesh.bounds.size.y, .7f);
                Assert.AreEqual(before, JsonUtility.ToJson(world), "Drawing a gangway cannot relocate a ship or change its berth.");
                world.Cells[world.Index(8, 5)].Terrain = TerrainType.Grass; world.Cells[world.Index(8, 5)].WaterDepth = 0;
                batch.Clear(); MaritimeArt.DrawJetty(batch, world, port); batch.Submit(); Assert.AreEqual(0, mesh.vertexCount, "No decorative bridge across a broken water approach.");
                port.BerthCell = -1; batch.Clear(); MaritimeArt.DrawJetty(batch, world, port); batch.Submit(); Assert.AreEqual(0, mesh.vertexCount);
            }
            finally { batch.Dispose(); Object.Destroy(material); Object.Destroy(atlas); Object.Destroy(go); }
        }
        private static Button Button(GameBootstrap game, string label) { return game.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>().text == label); }
        private static Color32[] Capture(Camera camera, RenderTexture target)
        {
            camera.Render(); RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            Texture2D image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false); image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            Color32[] result = image.GetPixels32(); Object.Destroy(image); RenderTexture.active = previous; return result;
        }
        private static ulong Hash(Texture2D texture, Rect uv)
        {
            int x = Mathf.FloorToInt(uv.x * texture.width), y = Mathf.FloorToInt(uv.y * texture.height), count = 0; ulong hash = 1469598103934665603UL;
            Color32[] pixels = texture.GetPixels32();
            for (int row = 0; row < 64; row++) for (int col = 0; col < 64; col++) { Color32 c = pixels[(y + row) * texture.width + x + col]; if (c.a > 0) count++; hash = unchecked((hash ^ c.r) * 1099511628211UL); hash = unchecked((hash ^ c.g) * 1099511628211UL); hash = unchecked((hash ^ c.b) * 1099511628211UL); hash = unchecked((hash ^ c.a) * 1099511628211UL); }
            Assert.Greater(count, 150); return hash;
        }
        private static SimulationEngine Fixture(out ShipData ship, out BuildingData warehouse, out UnitData unit)
        {
            WorldState world = WorldGenerator.Generate(130801, 16, 16); SimulationEngine engine = new SimulationEngine(world);
            for (int i = 0; i < world.Cells.Length; i++) { world.Cells[i].Terrain = i % world.Width >= 8 ? TerrainType.DeepWater : TerrainType.Grass; world.Cells[i].WaterDepth = i % world.Width >= 8 ? 1 : 0; world.Cells[i].WaterKind = i % world.Width >= 8 ? WaterKind.Ocean : WaterKind.None; world.Cells[i].BuildingId = 0; world.Cells[i].Height = .5f; }
            world.Settlements.Add(new SettlementData { Id = 100, X = 6, Y = 5, Name = "Проверочная гавань" });
            var port = new BuildingData { Id = 101, SettlementId = 100, Type = BuildingType.Port, X = 7, Y = 5, Level = 1, EntranceCell = world.Index(7, 4), BerthCell = world.Index(8, 5) };
            warehouse = new BuildingData { Id = 102, SettlementId = 100, Type = BuildingType.HarborWarehouse, X = 5, Y = 5, Level = 1, EntranceCell = world.Index(5, 4) };
            foreach (BuildingData b in new[] { port, warehouse }) { world.Buildings.Add(b); world.Cells[world.Index(b.X, b.Y)].BuildingId = b.Id; world.Cells[world.Index(b.X, b.Y)].SettlementId = 100; world.Infrastructure[b.EntranceCell].RoadLevel = 1; }
            unit = new UnitData { Id = 103, Age = 24, X = 6.5f, Y = 3.5f, Health = 100, MaxHealth = 100, Name = "Ирина", Species = SpeciesType.Human, SettlementId = 100, Sex = Sex.Female, Task = UnitTask.Idle };
            world.Units.Add(unit); world.NextId = 104;
            ship = new ShipData { Id = 1, Kind = ShipKind.FishingBoat, OwnerSettlementId = 100, HomePortId = port.Id, DockedPortId = port.Id, X = 8.5f, Y = 5.5f, HeadingY = 1, Health = 70, MaxHealth = 70, Phase = ShipPhase.Docked };
            world.Ships.Add(ship); world.NextShipId = 2; world.Revision++; world.NavigationRevision++; world.TerrainRevision++;
            Assert.IsTrue(BuildingAccess.IsUsable(world, port)); Assert.IsTrue(engine.GetHarborWarehouses(port.Id).Contains(warehouse), "The presentation fixture has a real connected warehouse.");
            return engine;
        }
    }
}
