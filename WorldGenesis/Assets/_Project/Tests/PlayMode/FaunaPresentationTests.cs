using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WorldGenesis.Runtime;
using WorldGenesis.Simulation;

namespace WorldGenesis.Tests
{
    public sealed class FaunaPresentationTests
    {
        [Test]
        public void AllThirteenAnimalsHaveFourDistinctViewsAndFourActualGaitFrames()
        {
            Texture2D atlas = WildlifeArt.CreateAtlas(true);
            try
            {
                Color32[] pixels = atlas.GetPixels32();
                for (int species = 4; species <= 16; species++)
                {
                    var views = new HashSet<ulong>();
                    for (int direction = 0; direction < 4; direction++)
                    {
                        var steps = new HashSet<ulong>();
                        for (int phase = 0; phase < 4; phase++)
                        {
                            int sprite = WildlifeArt.SpriteFor((SpeciesType)species, Sex.Male, (WildlifeArt.AnimalDirection)direction, phase);
                            ulong hash = Hash(pixels, sprite);
                            Assert.IsTrue(steps.Add(hash), "Gait frames must change actual pixels: " + (SpeciesType)species + "/" + direction + "/" + phase);
                            if (phase == 0) Assert.IsTrue(views.Add(hash), "Front/back cannot be aliases of the side view: " + (SpeciesType)species);
                        }
                    }
                }
                foreach (WildlifeArt.AnimalDirection direction in System.Enum.GetValues(typeof(WildlifeArt.AnimalDirection)))
                {
                    int male = WildlifeArt.SpriteFor(SpeciesType.Deer, Sex.Male, direction, 0), female = WildlifeArt.SpriteFor(SpeciesType.Deer, Sex.Female, direction, 0);
                    Assert.AreNotEqual(Hash(pixels, male), Hash(pixels, female), "A doe keeps her own antler-free view.");
                }
                foreach (int equipment in new[] { WildlifeArt.BasketSmall, WildlifeArt.BasketMedium, WildlifeArt.BasketLarge, WildlifeArt.Saddle, WildlifeArt.SaddleBags, WildlifeArt.FoodBundle, WildlifeArt.HuntingSpear }) Hash(pixels, equipment);
            }
            finally { Object.Destroy(atlas); }
        }

        [UnityTest]
        public IEnumerator AnimalPathTurnsNativeSilhouetteAndArrivalStopsItsGait()
        {
            WorldState world = Fixture(out UnitData unit); unit.Species = SpeciesType.Wolf; unit.SettlementId = 0;
            GameObject root = new GameObject("Four direction animal proof"), cameraObject = new GameObject("Animal proof camera");
            WorldView view = new WorldView(root.transform); Camera camera = Configure(cameraObject, new Vector2(7.5f, 8), out RenderTexture target);
            try
            {
                view.SetWorld(world); Layer(root);
                MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>();
                Mesh mesh = root.GetComponentsInChildren<MeshFilter>().First(m => m.name == "Actual wildlife").sharedMesh;
                Color32[] previous = null;
                foreach (Vector2Int direction in new[] { Vector2Int.down, Vector2Int.left, Vector2Int.up, Vector2Int.right })
                {
                    unit.Path.Clear(); unit.Path.Add(world.Index(7 + direction.x, 7 + direction.y)); unit.PathCursor = 0; view.Update(1);
                    Color32[] image = Capture(camera, target, renderers, "Actual wildlife");
                    Assert.Greater(image.Count(p => p.a > 250), 80);
                    if (previous != null) Assert.Greater(image.Where((p, i) => Difference(p, previous[i]) > 15).Count(), 80, "A path turn must change the native silhouette.");
                    previous = image;
                }
                Vector2[] moving = mesh.uv; string before = JsonUtility.ToJson(world); yield return null; view.Update(50);
                CollectionAssert.AreEqual(moving, mesh.uv, "Wall-clock time cannot advance a paused gait."); Assert.AreEqual(before, JsonUtility.ToJson(world));
                world.Tick += 3; view.Update(51); Assert.IsFalse(moving.SequenceEqual(mesh.uv));
                unit.PathCursor = unit.Path.Count; view.Update(52); Vector2[] stopped = mesh.uv;
                world.Tick += 40; view.Update(53); CollectionAssert.AreEqual(stopped, mesh.uv, "Arrival must stop walking while retaining the last facing.");
                unit.Species = SpeciesType.SpiritStag;
                previous = null;
                foreach (Vector2Int direction in new[] { Vector2Int.down, Vector2Int.left, Vector2Int.up, Vector2Int.right })
                {
                    unit.X = unit.Y = 7.5f; unit.Path.Clear(); unit.Path.Add(world.Index(7 + direction.x, 7 + direction.y)); unit.PathCursor = 0;
                    view.Update(53); Vector2[] expectedMoving = mesh.uv;
                    unit.X = 7.5f + direction.x * .995f; unit.Y = 7.5f + direction.y * .995f;
                    view.Update(53);
                    CollectionAssert.AreEqual(expectedMoving, mesh.uv, "A .005-cell remainder must not turn the moving spirit stag east: " + direction);
                    view.SetWorld(world); Layer(root);
                    CollectionAssert.AreEqual(expectedMoving, mesh.uv, "First render near arrival must resolve the real segment without a cached facing: " + direction);
                    unit.X = 7.5f + direction.x; unit.Y = 7.5f + direction.y; view.Update(53);
                    CollectionAssert.AreEqual(expectedMoving, mesh.uv, "An exactly reached waypoint must retain the cached direction until its cursor advances: " + direction);
                    unit.PathCursor = unit.Path.Count; view.Update(53); stopped = mesh.uv;
                    Rect standing = WildlifeArt.Uv(WildlifeArt.SpriteFor(unit.Species, unit.Sex, WildlifeArt.DirectionFor(direction), 0));
                    Assert.AreEqual(standing.xMin, stopped.Min(uv => uv.x), .000001f);
                    Assert.AreEqual(standing.yMin, stopped.Min(uv => uv.y), .000001f);
                    world.Tick += 40; view.Update(53); CollectionAssert.AreEqual(stopped, mesh.uv, "A stopped spirit stag must keep its four-way facing: " + direction);
                    camera.transform.position = new Vector3(unit.X, unit.Y + .5f, -10);
                    Color32[] image = Capture(camera, target, renderers, "Actual wildlife");
                    Assert.Greater(image.Count(p => p.a > 250), 80);
                    if (previous != null) Assert.Greater(image.Where((p, i) => Difference(p, previous[i]) > 15).Count(), 80, "Native standing spirit stags must retain different silhouettes after arrival.");
                    previous = image;
                }
                unit.Health = 0; view.Update(54); Assert.AreEqual(0, mesh.vertexCount);
            }
            finally { view.Dispose(); camera.targetTexture = null; target.Release(); Object.Destroy(target); Object.Destroy(cameraObject); Object.Destroy(root); }
        }

        [UnityTest]
        public IEnumerator FlatCropsAndPastureCannotHideResidentButBreweryRetainsBuildingDepth()
        {
            WorldState world = Fixture(out UnitData unit); var building = new BuildingData { Id = 3, Type = BuildingType.Farm, X = 7, Y = 7, CropGrowth = 1, FarmCrop = CropType.Wheat };
            world.Buildings.Add(building); world.Cells[world.Index(7, 7)].BuildingId = building.Id;
            GameObject root = new GameObject("Flat cultivated ground proof"), cameraObject = new GameObject("Cultivated ground camera");
            WorldView view = new WorldView(root.transform); Camera camera = Configure(cameraObject, new Vector2(7.5f, 8), out RenderTexture target);
            try
            {
                view.SetWorld(world); Layer(root); MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>();
                foreach (BuildingType type in new[] { BuildingType.Farm, BuildingType.Pasture, BuildingType.Brewery })
                {
                    building.Type = type; unit.Y = 8; view.Update(1);
                    string buildingBatch = type == BuildingType.Brewery ? "Rural buildings and loose produce" : "Fields and rural buildings";
                    Color32[] site = Capture(camera, target, renderers, buildingBatch), body = Capture(camera, target, renderers, "Animated human citizens");
                    Color32[] combined = Capture(camera, target, renderers, buildingBatch, "Animated human citizens");
                    int overlap = 0;
                    for (int i = 0; i < body.Length; i++)
                    {
                        if (body[i].a < 250 || site[i].a < 250 || Difference(body[i], site[i]) < 40) continue;
                        Color32 expected = type == BuildingType.Brewery ? site[i] : body[i];
                        Assert.LessOrEqual(Difference(expected, combined[i]), 6, type + " must obey ground/building depth at overlapping native pixel " + i); overlap++;
                    }
                    Assert.Greater(overlap, 30, "Fixture must overlap real opaque art, not empty sprite rectangles.");
                }
                yield return null;
            }
            finally { view.Dispose(); camera.targetTexture = null; target.Release(); Object.Destroy(target); Object.Destroy(cameraObject); Object.Destroy(root); }
        }

        [UnityTest]
        public IEnumerator CrownFitsActualHeadForEveryPeopleAndSexAndTransfersWithGovernment()
        {
            WorldState world = Fixture(out UnitData unit);
            var city = new SettlementData { Id = 1, Government = GovernmentType.Feudal, RulerId = unit.Id }; world.Settlements.Add(city); unit.SettlementId = city.Id;
            GameObject root = new GameObject("Ruler crown proof"), cameraObject = new GameObject("Crown attachment camera");
            WorldView view = new WorldView(root.transform); Camera camera = Configure(cameraObject, new Vector2(7.5f, 8), out RenderTexture target);
            try
            {
                view.SetWorld(world); Layer(root); MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>();
                Mesh crown = root.GetComponentsInChildren<MeshFilter>().First(m => m.name == "Living ruler crowns").sharedMesh;
                for (int race = 0; race < 4; race++) foreach (Sex sex in new[] { Sex.Female, Sex.Male })
                {
                    unit.Species = (SpeciesType)race; unit.Sex = sex; view.Update(1);
                    Color32[] body = Capture(camera, target, renderers, "Animated human citizens"), crownPixels = Capture(camera, target, renderers, "Living ruler crowns");
                    int bodyTop = -1, crownBottom = target.height, crownTop = -1;
                    for (int i = 0; i < body.Length; i++) { if (body[i].a > 250) bodyTop = Mathf.Max(bodyTop, i / target.width); if (crownPixels[i].a > 250) { crownBottom = Mathf.Min(crownBottom, i / target.width); crownTop = Mathf.Max(crownTop, i / target.width); } }
                    Assert.Greater(crownTop, bodyTop, "Crown peaks must emerge above " + unit.Species + "/" + sex);
                    Assert.LessOrEqual(crownBottom, bodyTop + 1, "The gold band must touch the actual hair, not float over it.");
                    Assert.Greater(crownBottom, bodyTop - 8, "The band must not cover the face.");
                    Assert.Greater(crownPixels.Count(p => p.a > 250), 12);
                }
                float dryHead = crown.bounds.center.y; int waterCell = world.Index((int)unit.X, (int)unit.Y);
                world.Cells[waterCell].Terrain = TerrainType.ShallowWater; world.Cells[waterCell].WaterKind = WaterKind.River; world.Cells[waterCell].WaterDepth = .4f;
                CitizenWaterPresentation.State water = CitizenWaterPresentation.Resolve(world, unit); Assert.IsTrue(water.Wet);
                view.Update(1.5f); Assert.AreEqual(water.Offset.y, crown.bounds.center.y - dryHead, .002f, "The crown must sink with its actual wearer when wading.");
                world.Cells[waterCell].Terrain = TerrainType.Grass; world.Cells[waterCell].WaterKind = WaterKind.None; world.Cells[waterCell].WaterDepth = 0; view.Update(1.6f);
                float priorX = crown.bounds.center.x;
                var successor = new UnitData { Id = 4, Species = SpeciesType.Elf, Sex = Sex.Female, Age = 28, X = 11.5f, Y = unit.Y, Health = 100, MaxHealth = 100, SettlementId = city.Id }; world.Units.Add(successor);
                city.Government = GovernmentType.Republic; city.RulerId = successor.Id; view.Update(2);
                Assert.AreEqual(4, crown.bounds.center.x - priorX, .15f, "Succession updates on the current state even while paused.");
                Assert.IsFalse(RulerPresentation.IsRuler(world, unit)); Assert.IsTrue(RulerPresentation.IsRuler(world, successor));
                successor.InsideBuildingId = 99; view.Update(3); Assert.AreEqual(0, crown.vertexCount, "No crown floats over the ruler's building.");
                successor.InsideBuildingId = 0; successor.ShipId = 99; view.Update(4); Assert.AreEqual(0, crown.vertexCount);
                successor.ShipId = 0; successor.Health = 0; view.Update(5); Assert.AreEqual(0, crown.vertexCount);
                successor.Health = 100; city.RulerId = 0; view.Update(6); Assert.AreEqual(0, crown.vertexCount);
                yield return null;
            }
            finally { view.Dispose(); camera.targetTexture = null; target.Release(); Object.Destroy(target); Object.Destroy(cameraObject); Object.Destroy(root); }
        }

        private static WorldState Fixture(out UnitData unit)
        {
            var world = new WorldState { Width = 16, Height = 16, Seed = 130900, Tick = 120, Cells = new TileData[256], Infrastructure = new InfrastructureCell[256], ResourceNodes = new ResourceNodeData[256] };
            for (int i = 0; i < world.Cells.Length; i++) world.Cells[i].Terrain = TerrainType.Grass;
            unit = new UnitData { Id = 2, Age = 25, X = 7.5f, Y = 7.5f, Species = SpeciesType.Human, Sex = Sex.Male, Task = UnitTask.Idle, Health = 100, MaxHealth = 100 };
            world.Units.Add(unit); return world;
        }
        private static void Layer(GameObject root) { foreach (Transform child in root.GetComponentsInChildren<Transform>()) child.gameObject.layer = 30; }
        private static Camera Configure(GameObject cameraObject, Vector2 point, out RenderTexture target)
        {
            Camera camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 2.1f;
            camera.transform.position = new Vector3(point.x, point.y, -10); camera.nearClipPlane = .1f; camera.farClipPlane = 100; camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target; return camera;
        }
        private static Color32[] Capture(Camera camera, RenderTexture target, MeshRenderer[] renderers, params string[] visible)
        {
            foreach (MeshRenderer renderer in renderers) renderer.enabled = visible.Contains(renderer.name);
            camera.Render(); RenderTexture prior = RenderTexture.active; RenderTexture.active = target;
            Texture2D image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try { image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); return image.GetPixels32(); }
            finally { RenderTexture.active = prior; Object.Destroy(image); }
        }
        private static int Difference(Color32 a, Color32 b) { return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b); }
        private static ulong Hash(Color32[] pixels, int sprite)
        {
            ulong hash = 1469598103934665603UL; int opaque = 0;
            for (int y = 0; y < WildlifeArt.Frame; y++) for (int x = 0; x < WildlifeArt.Frame; x++)
            {
                Color32 p = pixels[(sprite / WildlifeArt.Columns * WildlifeArt.Frame + y) * WildlifeArt.Width + sprite % WildlifeArt.Columns * WildlifeArt.Frame + x];
                if (p.a > 200) opaque++; hash = unchecked((hash ^ p.r) * 1099511628211UL); hash = unchecked((hash ^ p.g) * 1099511628211UL); hash = unchecked((hash ^ p.b) * 1099511628211UL); hash = unchecked((hash ^ p.a) * 1099511628211UL);
            }
            Assert.Greater(opaque, 30, "A direction/equipment slot must contain actual art."); return hash;
        }
    }
}
