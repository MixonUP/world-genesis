using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using WorldGenesis.Runtime;
using WorldGenesis.Simulation;

namespace WorldGenesis.Tests
{
    // Presentation fixtures change actual model fields while paused. They are not natural growth receipts.
    public sealed class TransportRenderingTests
    {
        private GameBootstrap game;
        [UnitySetUp] public IEnumerator SetUp()
        {
            game = GameBootstrap.Instance;
            if (game == null) game = new GameObject("Transport presentation integration").AddComponent<GameBootstrap>();
            yield return null; game.Paused = true; yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown() { if (game != null) Object.Destroy(game.gameObject); yield return null; }

        [UnityTest]
        public IEnumerator WaterDepthBlockedFordAndBridgeAreShownFromTheWorld()
        {
            int index = EmptyCell(); int x = index % game.World.Width, y = index / game.World.Width;
            TileData tile = game.World.Cells[index]; tile.Terrain = TerrainType.ShallowWater; tile.WaterKind = WaterKind.Lake; tile.WaterDepth = .4f; tile.CurrentStrength = .1f; tile.ChannelWidth = 0; tile.River = false;
            game.World.Cells[index] = tile; game.World.Infrastructure[index] = default(InfrastructureCell); game.World.ResourceNodes[index] = default(ResourceNodeData);
            game.View.SetWorld(game.World); game.InspectSite(x, y); yield return null;
            StringAssert.Contains("Озеро", VisibleText()); StringAssert.Contains("Доступен брод", VisibleText());
            Color shallow = GroundPixel(x, y);
            tile.WaterDepth = 4; game.World.Cells[index] = tile; game.World.TerrainRevision++; game.InspectSite(x, y);
            yield return RefreshTerrain();
            Assert.AreNotEqual(shallow, GroundPixel(x, y), "Changing saved depth must change the production water colour.");
            StringAssert.Contains(game.World.GetTraversalBlockReason(index), VisibleText()); StringAssert.Contains("Проход закрыт", VisibleText());
            tile.WaterDepth = .3f; tile.CurrentStrength = .9f; tile.WaterKind = WaterKind.MountainRiver; tile.ChannelWidth = 3; game.World.Cells[index] = tile; game.World.TerrainRevision++; game.InspectSite(x, y);
            yield return null;
            StringAssert.Contains("Горная река", VisibleText()); StringAssert.Contains("Ширина русла 3", VisibleText()); StringAssert.Contains("Быстрое течение", VisibleText());
            InfrastructureCell bridge = game.World.Infrastructure[index]; bridge.BridgeLevel = 1; bridge.BridgeAxis = 0; bridge.BridgeOwnerId = game.World.Settlements[0].Id; game.World.Infrastructure[index] = bridge; game.World.TerrainRevision++; game.InspectSite(x, y);
            yield return RefreshTerrain();
            StringAssert.Contains("Деревянный мост", VisibleText()); StringAssert.Contains("Участок проходим пешком", VisibleText());
            MeshFilter mesh = FindMesh("Transport deck " + x / 32 + "," + y / 32);
            Assert.Greater(mesh.sharedMesh.vertexCount, 0); Vector2[] wooden = mesh.sharedMesh.uv;
            bridge.BridgeLevel = 2; bridge.BridgeAxis = 1; game.World.Infrastructure[index] = bridge; game.World.TerrainRevision++; game.InspectSite(x, y);
            yield return RefreshTerrain();
            StringAssert.Contains("Каменный мост · север–юг", VisibleText()); Assert.IsFalse(wooden.SequenceEqual(mesh.sharedMesh.uv));
            long tick = game.World.Tick; Vector3[] vertices = mesh.sharedMesh.vertices;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.AreEqual(tick, game.World.Tick); Assert.AreEqual(.9f, game.World.Cells[index].CurrentStrength); CollectionAssert.AreEqual(vertices, mesh.sharedMesh.vertices);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator FourRoadGradesAndNewTerrainToolsRemainDistinctAndReachable()
        {
            int index = EmptyCell(); int x = index % game.World.Width, y = index / game.World.Width;
            TileData tile = game.World.Cells[index]; tile.Terrain = TerrainType.Grass; tile.WaterKind = WaterKind.None; tile.WaterDepth = tile.CurrentStrength = 0; tile.ChannelWidth = 0; tile.River = false; game.World.Cells[index] = tile;
            game.World.ResourceNodes[index] = default(ResourceNodeData); game.World.Infrastructure[index] = default(InfrastructureCell); game.View.SetWorld(game.World);
            Color previous = GroundPixel(x, y);
            for (byte level = 1; level <= 4; level++)
            {
                InfrastructureCell road = game.World.Infrastructure[index]; road.RoadLevel = level; road.RoadOwnerId = game.World.Settlements[0].Id; road.FootTraffic = 12; game.World.Infrastructure[index] = road;
                game.World.TerrainRevision++; game.InspectSite(x, y); yield return RefreshTerrain();
                StringAssert.Contains(TransportArt.RoadName(level), VisibleText()); StringAssert.Contains("Следы движения 12", VisibleText());
                Color current = GroundPixel(x, y); Assert.AreNotEqual(previous, current, "Every road surface must change the rendered centre pixel."); previous = current;
            }
            FindButton("ЛАНДШАФТ").onClick.Invoke(); yield return null;
            Assert.NotNull(FindButton("Глубокая вода")); FindButton("Ландшафт 1/2  ›").onClick.Invoke(); yield return null;
            string[] labels = { "Болото", "Холмы", "Овраг", "Возвышенность" };
            for (int i = 0; i < labels.Length; i++)
            {
                Button button = FindButton(labels[i]); Assert.NotNull(button); button.onClick.Invoke(); Assert.AreEqual(i + 8, game.Tool.Value);
                Assert.AreNotEqual(Color.magenta, PixelArt.Terrain((TerrainType)(i + 8)));
                Canvas.ForceUpdateCanvases(); RectTransform rect = button.GetComponent<RectTransform>(); Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
                Assert.IsTrue(game.IsScreenPointOverUI(point)); Assert.Less(point.x, Screen.width);
            }
            FindButton("Ландшафт 2/2  ›").onClick.Invoke(); yield return null; Assert.NotNull(FindButton("Горы"));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SaltDepletionAndActualWagonCargoChangeOnlyPresentation()
        {
            int index = EmptyCell(); int x = index % game.World.Width, y = index / game.World.Width;
            TileData tile = game.World.Cells[index]; tile.Terrain = TerrainType.Sand; tile.WaterKind = WaterKind.None; tile.WaterDepth = tile.CurrentStrength = 0; tile.ChannelWidth = 0; tile.River = false; game.World.Cells[index] = tile;
            game.World.ResourceNodes[index] = new ResourceNodeData { Type = ResourceType.Salt, Amount = 6, Capacity = 6 };
            game.View.SetWorld(game.World); game.InspectSite(x, y); yield return null;
            StringAssert.Contains("Соляное месторождение", VisibleText());
            MeshFilter nature = FindMesh("Nature " + x / 32 + "," + y / 32); Vector2[] full = nature.sharedMesh.uv;
            ResourceNodeData node = game.World.ResourceNodes[index]; node.Amount = 0; node.RegrowTicksRemaining = 300; game.World.ResourceNodes[index] = node; game.World.ResourceRevision++;
            game.InspectSite(x, y); yield return RefreshTerrain(); Assert.IsFalse(full.SequenceEqual(nature.sharedMesh.uv), "An exhausted salt node must not retain its full crystal pile.");
            UnitData carrier = game.World.Units.First(u => u.Species == SpeciesType.Human && !u.IsChild);
            game.Engine.Spawn(SpeciesType.Horse, (int)carrier.X, (int)carrier.Y);
            UnitData horse = game.World.Units.Last(u => u.Species == SpeciesType.Horse); horse.HandlerId = carrier.Id; horse.AnimalOwnerId = carrier.SettlementId; horse.IsDomestic = true;
            carrier.MountId = horse.Id; carrier.MountSpecies = SpeciesType.Horse; carrier.OwnsWagon = carrier.HasWagon = true;
            carrier.CargoKind = ItemType.Salt; carrier.CargoAmount = 1; carrier.CarriedFood = carrier.CarriedWood = carrier.CarriedWater = carrier.HarvestAmount = 0;
            carrier.Path.Clear(); carrier.PathCursor = 0; horse.Path.Clear(); horse.PathCursor = 0;
            horse.X = carrier.X - .7f; horse.Y = carrier.Y;
            game.InspectUnit(carrier.Id); yield return null;
            StringAssert.Contains("Соль  1", VisibleText()); Assert.Greater(FindMesh("Actual wagons").sharedMesh.vertexCount, 0);
            Vector3[] before = FindMesh("Actual wagons").sharedMesh.vertices;
            horse.X -= 1; game.InspectUnit(carrier.Id); yield return null;
            Assert.IsFalse(before.SequenceEqual(FindMesh("Actual wagons").sharedMesh.vertices), "The drawn wagon must follow the real animal, not a cached citizen location.");
            Assert.AreEqual(1, carrier.CargoAmount); Assert.AreEqual(0, game.World.ResourceNodes[index].Amount); Assert.AreEqual(300, game.World.ResourceNodes[index].RegrowTicksRemaining);
            Assert.Less(WildlifeArt.AnimalSize(SpeciesType.Horse), 2f, "The revised horse must fit the established citizen scale.");
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest]
        public IEnumerator SeparateDepotCapacitiesAndGranarySpriteFollowActualStock()
        {
            BuildingData building = game.World.Buildings.First(b => b.Type == BuildingType.House || b.Type == BuildingType.Farm);
            building.Type = BuildingType.Warehouse; building.Level = 1;
            System.Array.Clear(building.Inventory, 0, building.Inventory.Length); System.Array.Clear(building.TaxStocks, 0, building.TaxStocks.Length); System.Array.Clear(building.AgriculturalStock, 0, building.AgriculturalStock.Length); building.PersonalStocks.Clear();
            building.Inventory[(int)ItemType.RawStone] = 900; building.TaxStocks[(int)ItemType.Salt] = 10;
            building.PersonalStocks.Add(new OwnedGoodsData { OwnerId = game.World.Units.First(u => u.SettlementId == building.SettlementId).Id, Item = ItemType.Clay, Amount = 20 });
            building.AgriculturalStock[(int)AgriculturalProduct.Fiber] = 69;
            game.InspectSite(building.X, building.Y); yield return null;
            StringAssert.Contains("Склад материалов", VisibleText()); StringAssert.Contains("Занято 999 / 1000", VisibleText()); StringAssert.Contains("Свободно 1", VisibleText());
            Vector2[] warehouseUv = FindMesh("Buildings").sharedMesh.uv;
            building.Type = BuildingType.Granary; game.InspectSite(building.X, building.Y); yield return null;
            StringAssert.Contains("Амбар", VisibleText()); StringAssert.Contains("Принимает: пищу, напитки и корм", VisibleText());
            StringAssert.Contains("Прежние товары другого назначения: 999", VisibleText());
            Assert.IsFalse(warehouseUv.SequenceEqual(FindMesh("Buildings").sharedMesh.uv), "Granary must use its own production silhouette.");
            float[] capacities = { 1000, 2500, 5000, 10000, 10000 };
            for (int level = 1; level <= 5; level++)
            {
                building.Level = level; game.InspectSite(building.X, building.Y); yield return null;
                StringAssert.Contains("Занято 999 / " + capacities[level - 1].ToString("0"), VisibleText());
            }
            StringAssert.Contains("Уровень 4 / 4", VisibleText(), "Legacy grade five is presented as the final supported depot grade.");
            building.Level = 1; building.AgriculturalStock[(int)AgriculturalProduct.Grain] = 101; game.InspectSite(building.X, building.Y); yield return null;
            StringAssert.Contains("Занято 1100 / 1000", VisibleText()); StringAssert.Contains("Переполнение 100", VisibleText()); StringAssert.Contains("Свободно 0", VisibleText());
            long tick = game.World.Tick; yield return new WaitForSecondsRealtime(.2f);
            Assert.AreEqual(tick, game.World.Tick); Assert.AreEqual(1100, StorageCapacity.TotalUsed(building)); Assert.AreEqual(101, building.AgriculturalStock[(int)AgriculturalProduct.Grain]);
            LogAssert.NoUnexpectedReceived();
        }
        private IEnumerator RefreshTerrain() { yield return new WaitForSecondsRealtime(.32f); for (int i = 0; i < 10; i++) yield return null; }
        private int EmptyCell() { for (int i = 0; i < game.World.Cells.Length; i++) if (game.World.Cells[i].BuildingId == 0 && game.World.Cells[i].SettlementId == 0 && game.World.Cells[i].Terrain == TerrainType.Grass) return i; Assert.Fail("Expected one open grass tile."); return -1; }
        private Color GroundPixel(int x, int y) { Texture2D texture = (Texture2D)FindMesh("Ground " + x / 32 + "," + y / 32).GetComponent<MeshRenderer>().sharedMaterial.mainTexture; return texture.GetPixel(x % 32 * 8 + 4, y % 32 * 8 + 4); }
        private MeshFilter FindMesh(string name) { return game.GetComponentsInChildren<MeshFilter>().First(m => m.name == name); }
        private Button FindButton(string label) { return game.GetComponentsInChildren<Button>().FirstOrDefault(b => b.GetComponentInChildren<Text>()?.text == label); }
        private string VisibleText() { return string.Join("\n", game.GetComponentsInChildren<Text>().Select(t => t.text)); }
    }
}
