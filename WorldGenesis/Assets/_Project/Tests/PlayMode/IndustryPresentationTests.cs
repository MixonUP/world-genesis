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
    public sealed class IndustryPresentationTests
    {
        [Test]
        public void AtlasContainsDistinctWorkshopGradesAndEveryManufacturedGood()
        {
            Texture2D atlas = IndustryArt.CreateAtlas(true);
            try
            {
                Assert.AreEqual(FilterMode.Point, atlas.filterMode);
                HashSet<ulong> buildings = new HashSet<ulong>(), goods = new HashSet<ulong>();
                for (BuildingType type = BuildingType.Smelter; type <= BuildingType.Mint; type++)
                    for (int level = 1; level <= 5; level++)
                    {
                        Rect uv = IndustryArt.UvBuilding(type, level);
                        Assert.Greater(uv.width, 0); Assert.Less(uv.yMax, 320f / IndustryArt.Size);
                        Assert.IsTrue(buildings.Add(FrameHash(atlas, uv, IndustryArt.BuildingFrame, 350)), type + " G" + level + " must have its own painted appearance.");
                    }
                for (ItemType item = ItemType.Charcoal; item <= ItemType.StoneShot; item++)
                {
                    Rect uv = IndustryArt.UvItem(item);
                    Assert.GreaterOrEqual(uv.yMin, 320f / IndustryArt.Size); Assert.Less(uv.xMax, 1); Assert.Less(uv.yMax, 1);
                    Assert.IsTrue(goods.Add(FrameHash(atlas, uv, IndustryArt.ItemFrame, 70)), item + " must not reuse another item's pixels.");
                }
                Assert.AreEqual(35, buildings.Count); Assert.AreEqual(27, goods.Count);
                Assert.AreEqual(IndustryArt.UvBuilding(BuildingType.Mint, 5), IndustryArt.UvBuilding(BuildingType.Mint, 99));
                Assert.IsFalse(IndustryArt.SupportsItem(ItemType.Log)); Assert.IsFalse(IndustryArt.SupportsBuilding(BuildingType.House));
            }
            finally { Object.Destroy(atlas); }
        }

        [UnityTest]
        public IEnumerator WorkshopFireSmokeAndCraftGestureRequireActualProductionAndPauseTogether()
        {
            WorldState world = Fixture(out UnitData unit, out BuildingData building);
            GameObject parent = new GameObject("Industry effect proof");
            Material material = new Material(Shader.Find("Sprites/Default"));
            QuadBatch batch = new QuadBatch("Industry effects", parent.transform, material, 31);
            try
            {
                Mesh mesh = parent.GetComponentInChildren<MeshFilter>().sharedMesh;
                string before = JsonUtility.ToJson(world);
                Assert.IsTrue(IndustryCatalog.IsWorking(world, building));
                batch.Clear(); IndustryArt.DrawActivity(batch, world, building); batch.Submit();
                Assert.Greater(mesh.vertexCount, 16);
                Assert.AreEqual(CitizenSprites.CitizenAction.Craft, CitizenSprites.ResolvePose(world, unit, Vector2.up).Action);
                Assert.IsTrue(CitizenSprites.ResolvePose(world, unit, Vector2.up).SuppressTool, "A smelter does not wave a carpenter's hammer.");
                Vector3[] pausedVertices = mesh.vertices; Color32[] pausedColors = mesh.colors32;
                yield return null;
                batch.Clear(); IndustryArt.DrawActivity(batch, world, building); batch.Submit();
                CollectionAssert.AreEqual(pausedVertices, mesh.vertices); CollectionAssert.AreEqual(pausedColors, mesh.colors32);
                Assert.AreEqual(before, JsonUtility.ToJson(world), "Rendering must not consume inventory, RNG or advance production.");
                world.Tick += 5;
                batch.Clear(); IndustryArt.DrawActivity(batch, world, building); batch.Submit();
                Assert.IsFalse(pausedVertices.SequenceEqual(mesh.vertices));

                unit.Path.Add(world.Index(8, 7));
                AssertNoActivity(batch, mesh, world, building);
                Assert.AreEqual(CitizenSprites.CitizenAction.Walk, CitizenSprites.ResolvePose(world, unit, Vector2.up).Action);
                unit.Path.Clear(); unit.CargoKind = ItemType.IronOre; unit.CargoAmount = 1;
                AssertNoActivity(batch, mesh, world, building);
                unit.CargoKind = ItemType.None; unit.CargoAmount = 0;
                building.Inventory[(int)ItemType.Charcoal] = 0; AssertNoActivity(batch, mesh, world, building);
                Assert.AreEqual(CitizenSprites.CitizenAction.Idle, CitizenSprites.ResolvePose(world, unit, Vector2.up).Action);
                building.Inventory[(int)ItemType.Charcoal] = 1;
                unit.Task = UnitTask.Flee; AssertNoActivity(batch, mesh, world, building);
                unit.Task = UnitTask.Produce; unit.Health = 0; AssertNoActivity(batch, mesh, world, building);
                unit.Health = 100; world.Cells[world.Index(building.X, building.Y)].Fire = .5f; AssertNoActivity(batch, mesh, world, building);
            }
            finally { batch.Dispose(); Object.Destroy(material); Object.Destroy(parent); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ProductionMeshesUseTheirOwnRegionsAndOnlyShowRealCivilianEquipment()
        {
            WorldState world = Fixture(out UnitData unit, out BuildingData building);
            GameObject parent = new GameObject("Industry geometry proof");
            Material material = new Material(Shader.Find("Sprites/Default"));
            QuadBatch batch = new QuadBatch("Industry geometry", parent.transform, material, 60);
            try
            {
                Mesh mesh = parent.GetComponentInChildren<MeshFilter>().sharedMesh;
                batch.Clear(); IndustryArt.DrawBuilding(batch, building); batch.Submit();
                Vector3[] gradeOne = mesh.vertices; Vector2[] uvOne = mesh.uv;
                building.Level = 5;
                batch.Clear(); IndustryArt.DrawBuilding(batch, building); batch.Submit();
                Assert.Greater(mesh.bounds.size.x, gradeOne.Max(v => v.x) - gradeOne.Min(v => v.x));
                Assert.IsFalse(uvOne.SequenceEqual(mesh.uv));
                building.Health = 0;
                batch.Clear(); IndustryArt.DrawBuilding(batch, building); batch.Submit(); Assert.AreEqual(0, mesh.vertexCount);
                batch.Clear(); Assert.IsTrue(IndustryArt.DrawItem(batch, ItemType.Ballista, Vector2.zero, 1, Color.white)); batch.Submit();
                Rect ballistaUv = IndustryArt.UvItem(ItemType.Ballista);
                Assert.AreEqual(ballistaUv.xMin, mesh.uv.Min(v => v.x)); Assert.AreEqual(ballistaUv.yMax, mesh.uv.Max(v => v.y));
                batch.Clear(); Assert.IsFalse(IndustryArt.DrawItem(batch, ItemType.Log, Vector2.zero, 1, Color.white)); batch.Submit(); Assert.AreEqual(0, mesh.vertexCount);

                Vector2 position = new Vector2(unit.X, unit.Y);
                batch.Clear(); IndustryArt.DrawEquipment(batch, world, unit, position, Vector2.down); batch.Submit(); Assert.AreEqual(0, mesh.vertexCount);
                unit.WorkTool = ItemType.IronTools; unit.Clothes = ItemType.LinenClothes;
                string before = JsonUtility.ToJson(world);
                batch.Clear(); IndustryArt.DrawEquipment(batch, world, unit, position, Vector2.down); batch.Submit(); Assert.Greater(mesh.vertexCount, 0);
                Assert.AreEqual(before, JsonUtility.ToJson(world));
                unit.Clothes = ItemType.Armor; unit.WorkTool = ItemType.Sword;
                batch.Clear(); IndustryArt.DrawEquipment(batch, world, unit, position, Vector2.down); batch.Submit(); Assert.AreEqual(0, mesh.vertexCount, "A malformed civilian field must not add free battle equipment.");
                unit.WorkTool = ItemType.None; unit.Clothes = ItemType.LeatherClothes;
                batch.Clear(); IndustryArt.DrawEquipment(batch, world, unit, position, Vector2.down); batch.Submit();
                Bounds adultClothes = mesh.bounds;
                unit.Age = 10;
                batch.Clear(); IndustryArt.DrawEquipment(batch, world, unit, position, Vector2.down); batch.Submit();
                Assert.Greater(mesh.vertexCount, 0, "A child can wear a garment actually issued by the simulation.");
                Assert.Less(mesh.bounds.max.y, adultClothes.max.y); Assert.Less(mesh.bounds.size.x, adultClothes.size.x);
                Vector3[] childClothes = mesh.vertices;
                unit.WorkTool = ItemType.BronzeTools;
                batch.Clear(); IndustryArt.DrawEquipment(batch, world, unit, position, Vector2.down); batch.Submit();
                CollectionAssert.AreEqual(childClothes, mesh.vertices, "Even a malformed child WorkTool field must not draw an adult's tool.");
                unit.Clothes = ItemType.None;
                batch.Clear(); IndustryArt.DrawEquipment(batch, world, unit, position, Vector2.down); batch.Submit(); Assert.AreEqual(0, mesh.vertexCount);
                unit.Clothes = ItemType.LeatherClothes;
                unit.Age = 25;
                batch.Clear(); IndustryArt.DrawEquipment(batch, world, unit, position, Vector2.down, true); batch.Submit(); Assert.AreEqual(0, mesh.vertexCount);

                int cell = world.Index(7, 7); world.Cells[cell].Terrain = TerrainType.ShallowWater;
                world.Cells[cell].WaterKind = WaterKind.River; world.Cells[cell].WaterDepth = .4f;
                CitizenWaterPresentation.State water = CitizenWaterPresentation.Resolve(world, unit);
                Assert.IsTrue(water.Wet);
                batch.Clear(); IndustryArt.DrawEquipment(batch, world, unit, position + water.Offset, Vector2.down); batch.Submit();
                Assert.IsTrue(mesh.vertices.All(v => v.y >= water.SurfacePosition.y - .0001f), "Equipped hems and tool handles must not reintroduce pixels below the waist-deep waterline.");
                unit.Age = 10; water = CitizenWaterPresentation.Resolve(world, unit);
                batch.Clear(); IndustryArt.DrawEquipment(batch, world, unit, position + water.Offset, Vector2.down); batch.Submit();
                Assert.IsTrue(mesh.vertices.All(v => v.y >= water.SurfacePosition.y - .0001f), "A child's garment obeys the child's shallower water cut.");
            }
            finally { batch.Dispose(); Object.Destroy(material); Object.Destroy(parent); }
            yield return null;
        }

        private static ulong FrameHash(Texture2D atlas, Rect uv, int size, int minimumOpaque)
        {
            int x = Mathf.FloorToInt(uv.x * atlas.width), y = Mathf.FloorToInt(uv.y * atlas.height), opaque = 0;
            Color[] pixels = atlas.GetPixels(x, y, size, size); ulong hash = 1469598103934665603UL;
            unchecked
            {
                foreach (Color color in pixels)
                {
                    Color32 c = color; if (c.a > 0) opaque++;
                    hash = (hash ^ c.r) * 1099511628211UL; hash = (hash ^ c.g) * 1099511628211UL;
                    hash = (hash ^ c.b) * 1099511628211UL; hash = (hash ^ c.a) * 1099511628211UL;
                }
            }
            Assert.Greater(opaque, minimumOpaque, "Atlas region must contain an actual painted silhouette.");
            Assert.Less(opaque, size * size * .9f, "The transparent frame must not become an opaque tile.");
            return hash;
        }
        private static void AssertNoActivity(QuadBatch batch, Mesh mesh, WorldState world, BuildingData building)
        { batch.Clear(); IndustryArt.DrawActivity(batch, world, building); batch.Submit(); Assert.AreEqual(0, mesh.vertexCount); }
        private static WorldState Fixture(out UnitData unit, out BuildingData building)
        {
            WorldState world = new WorldState { Width = 16, Height = 16, Tick = 100, Seed = 3, Cells = new TileData[256], Infrastructure = new InfrastructureCell[256], ResourceNodes = new ResourceNodeData[256] };
            for (int i = 0; i < world.Cells.Length; i++) world.Cells[i].Terrain = TerrainType.Grass;
            SettlementData settlement = new SettlementData { Id = 1, Population = 2, Species = SpeciesType.Human };
            settlement.UnlockedTechnologies.Add("z02_ironworking"); world.Settlements.Add(settlement);
            unit = new UnitData { Id = 2, Species = SpeciesType.Human, Age = 25, Health = 100, MaxHealth = 100, X = 7.5f, Y = 7.5f, SettlementId = 1, Task = UnitTask.Produce, TargetBuildingId = 3 };
            building = new BuildingData { Id = 3, Type = BuildingType.Smelter, X = 7, Y = 7, SettlementId = 1, IndustryWorkerId = unit.Id, ActiveIndustryRecipe = IndustryRecipeType.IronIngot, Level = 1 };
            building.Inventory[(int)ItemType.IronOre] = 2; building.Inventory[(int)ItemType.Charcoal] = 1;
            world.Units.Add(unit); world.Buildings.Add(building); return world;
        }
    }
}
