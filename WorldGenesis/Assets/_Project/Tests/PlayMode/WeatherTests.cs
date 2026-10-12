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
    // Explicit presentation fixtures; natural weather probabilities and damage are tested in Simulation.
    public sealed class WeatherTests
    {
        private GameBootstrap game;
        private int originalWidth, originalHeight;
        private bool originalFullscreen;
        [UnitySetUp] public IEnumerator SetUp()
        {
            originalWidth = Screen.width; originalHeight = Screen.height; originalFullscreen = Screen.fullScreen;
            game = GameBootstrap.Instance;
            if (game == null) game = new GameObject("Weather presentation integration").AddComponent<GameBootstrap>();
            yield return null; game.Paused = true; game.World.Weather.Automatic = false; yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if (game != null) Object.Destroy(game.gameObject);
            if (Screen.width != originalWidth || Screen.height != originalHeight) Screen.SetResolution(originalWidth, originalHeight, originalFullscreen);
            yield return null;
        }
        [UnityTest] public IEnumerator WeatherButtonsUseSelectedCellBlockInputAndFreezeWithSimulation()
        {
            Screen.SetResolution(1280, 720, false); yield return null; yield return null;
            int cell = OpenCell(); int x = cell % game.World.Width, y = cell / game.World.Width;
            game.FocusWorldPoint(new Vector2(x + .5f, y + .5f), 14); game.InspectSite(x, y); game.BrushRadius = 8;
            FindButton("ПОГОДА").onClick.Invoke(); yield return null;
            FindButton(WeatherCatalog.Name(WeatherKind.Rain)).onClick.Invoke(); yield return null;
            Assert.AreEqual(WeatherKind.Rain, game.World.Weather.Kind);
            Assert.AreEqual(x + .5f, game.World.Weather.CenterX); Assert.AreEqual(y + .5f, game.World.Weather.CenterY);
            Assert.AreEqual(8, game.World.Weather.Radius); Assert.AreEqual(ToolKind.Inspect, game.Tool.Kind);
            StringAssert.Contains(40f.ToString("0.0") + " с", VisibleText()); StringAssert.Contains("БИОМ", VisibleText());
            Canvas.ForceUpdateCanvases();
            foreach (WeatherKind kind in System.Enum.GetValues(typeof(WeatherKind)))
            {
                Button button = FindButton(WeatherCatalog.Name(kind)); Assert.NotNull(button);
                RectTransform rect = button.GetComponent<RectTransform>(); var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                Assert.GreaterOrEqual(corners[0].x, -1); Assert.LessOrEqual(corners[2].x, Screen.width + 1);
                Assert.GreaterOrEqual(corners[0].y, -1); Assert.LessOrEqual(corners[2].y, Screen.height + 1);
                Assert.IsTrue(game.IsScreenPointOverUI(RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center))));
                Assert.NotNull(button.GetComponentsInChildren<Image>().FirstOrDefault(image => image.sprite != null), "Weather command needs an authored icon.");
            }
            // Editor may retain its configured Game View size; the native 1280 capture is a separate acceptance.
            TestContext.WriteLine("Actual weather UI test viewport: " + Screen.width + "x" + Screen.height);
            Mesh mesh = WeatherMesh(); Assert.Greater(mesh.vertexCount, 0); Assert.LessOrEqual(mesh.vertexCount, WeatherView.MaximumQuads * 4);
            Vector3[] before = mesh.vertices; long tick = game.World.Tick; uint rng = game.World.RngState;
            float moisture = game.World.Cells[cell].Moisture;
            yield return new WaitForSecondsRealtime(.3f);
            Assert.AreEqual(tick, game.World.Tick); Assert.AreEqual(rng, game.World.RngState);
            Assert.AreEqual(moisture, game.World.Cells[cell].Moisture); Assert.AreEqual(40, game.World.Weather.RemainingSeconds);
            CollectionAssert.AreEqual(before, mesh.vertices, "Weather must not use unscaled time while the world is paused.");
            FindButton(WeatherCatalog.Name(WeatherKind.Clear)).onClick.Invoke(); yield return null;
            Assert.AreEqual(0, WeatherMesh().vertexCount, "Clear removes the front, with no surviving cosmetic particles.");
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator SavedMoistureAndSnowChangeActualTerrainWithoutChangingItsBiome()
        {
            int cell = OpenCell(); int x = cell % game.World.Width, y = cell / game.World.Width;
            TileData tile = game.World.Cells[cell]; tile.Terrain = TerrainType.Grass; tile.WaterDepth = 0; tile.CurrentStrength = 0; tile.ChannelWidth = 0; tile.WaterKind = WaterKind.None;
            tile.River = false; tile.SnowCover = 0; tile.Moisture = .8f; tile.Biome = (byte)BiomeType.TropicalWetland;
            game.World.Cells[cell] = tile; game.World.Infrastructure[cell] = default(InfrastructureCell);
            game.World.ResourceNodes[cell] = default(ResourceNodeData); game.View.SetWorld(game.World); game.InspectSite(x, y);
            yield return null; Color wet = GroundPixel(x, y);
            tile.Moisture = .02f; game.World.Cells[cell] = tile; game.World.TerrainRevision++;
            yield return RefreshTerrain(); game.InspectSite(x, y); yield return null;
            Color dry = GroundPixel(x, y); Assert.AreNotEqual(wet, dry);
            StringAssert.Contains(BiomeCatalog.Name(BiomeType.TropicalWetland), VisibleText());
            tile.SnowCover = 1; game.World.Cells[cell] = tile; game.World.TerrainRevision++;
            yield return RefreshTerrain(); game.InspectSite(x, y); yield return null;
            Color snow = GroundPixel(x, y); Assert.Greater(snow.grayscale, dry.grayscale);
            StringAssert.Contains("Снежный покров 100%", VisibleText());
            Assert.AreEqual(BiomeType.TropicalWetland, game.World.GetBiome(cell)); Assert.AreEqual(.02f, game.World.Cells[cell].Moisture);
            Assert.AreEqual(1, game.World.Cells[cell].SnowCover); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator HurricaneFollowsRealCenterAndFloodFoamSurvivesOnlyActualFloodState()
        {
            int cell = OpenCell(true); int x = cell % game.World.Width, y = cell / game.World.Width;
            game.FocusWorldPoint(new Vector2(x + .5f, y + .5f), 14);
            Assert.IsTrue(game.Engine.SetWeather(WeatherKind.Hurricane, x, y, 9, .7f, 40)); yield return null;
            Vector3[] first = WeatherMesh().vertices; Assert.Greater(first.Length, 0);
            float initialX = game.World.Weather.CenterX, initialY = game.World.Weather.CenterY;
            for (int i = 0; i < 10; i++) game.Engine.Step(.1f);
            yield return null;
            Assert.IsTrue(initialX != game.World.Weather.CenterX || initialY != game.World.Weather.CenterY);
            Assert.IsFalse(first.SequenceEqual(WeatherMesh().vertices)); Assert.LessOrEqual(WeatherMesh().vertexCount, WeatherView.MaximumQuads * 4);
            TileData prior = game.World.Cells[cell], tile = prior;
            tile.Terrain = TerrainType.ShallowWater; tile.WaterKind = WaterKind.River; tile.WaterDepth = .8f; tile.CurrentStrength = .35f; tile.ChannelWidth = 1; tile.River = false;
            game.World.Cells[cell] = tile;
            game.World.FloodCells.Add(new FloodCellData { Cell = cell, ExpiresTick = game.World.Tick + 250, OriginalTerrain = prior.Terrain, OriginalWaterKind = prior.WaterKind, OriginalDepth = prior.WaterDepth, OriginalCurrent = prior.CurrentStrength, OriginalChannelWidth = prior.ChannelWidth, OriginalRiver = prior.River, OriginalHeight = prior.Height, AppliedDepth = .8f, AppliedCurrent = .35f, WaterSurface = prior.Height + .1f });
            Assert.IsTrue(game.Engine.SetWeather(WeatherKind.Clear, x, y)); game.InspectSite(x, y); yield return null;
            StringAssert.Contains("Временное затопление", VisibleText()); StringAssert.Contains("Спад воды через " + 25f.ToString("0.0") + " с", VisibleText());
            Assert.Greater(WeatherMesh().vertexCount, 0, "A real saved flood remains visible after clearing the front.");
            game.Engine.Paint(TerrainType.Grass, x, y, 0); yield return null;
            Assert.IsNull(game.World.GetFloodAt(x, y)); Assert.AreEqual(0, WeatherMesh().vertexCount);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator SparseFishAndPersistentFlamesRespectResourcesAndSimulationClock()
        {
            int cell = OpenCell(); int x = cell % game.World.Width, y = cell / game.World.Width;
            game.FocusWorldPoint(new Vector2(x + .5f, y + .5f), 12);
            for (int i = 0; i < game.World.Infrastructure.Length; i++)
            {
                InfrastructureCell stock = game.World.Infrastructure[i]; stock.Fish = 0; game.World.Infrastructure[i] = stock;
            }
            game.Engine.Paint(TerrainType.ShallowWater, x, y, 4);
            for (int yy = y - 3; yy <= y + 3; yy++) for (int xx = x - 3; xx <= x + 3; xx++)
            {
                int i = game.World.Index(xx, yy); InfrastructureCell stock = game.World.Infrastructure[i];
                stock.Fish = .003f; stock.FishCapacity = 4; game.World.Infrastructure[i] = stock;
            }
            yield return null;
            Mesh fish = game.GetComponentsInChildren<MeshFilter>().First(m => m.name == "Actual fish stocks").sharedMesh;
            Assert.AreEqual(0, fish.vertexCount, "A minute regenerated fraction must not look like an established school.");
            Assert.AreEqual(.003f, game.World.Infrastructure[cell].Fish, "Presentation must not remove the hidden fractional resource.");
            for (int yy = y - 3; yy <= y + 3; yy++) for (int xx = x - 3; xx <= x + 3; xx++)
            {
                int i = game.World.Index(xx, yy); InfrastructureCell stock = game.World.Infrastructure[i];
                stock.Fish = 4; game.World.Infrastructure[i] = stock;
            }
            yield return null; Assert.Greater(fish.vertexCount, 0); Assert.AreEqual(4, game.World.Infrastructure[cell].Fish);

            // Isolate the real persistent-fire renderer after acknowledging its event burst.
            // This removes unrelated particle lifetimes from the pause assertion.
            WorldState fireWorld = WorldGenerator.Generate(8721, 32, 32);
            var engine = new SimulationEngine(fireWorld); fireWorld.NaturalWildlife = false; fireWorld.Laws.FireSpread = false;
            engine.Paint(TerrainType.Grass, 16, 16, 2);
            for (int yy = 14; yy <= 18; yy++) for (int xx = 14; xx <= 18; xx++)
            { TileData tile = fireWorld.Cells[fireWorld.Index(xx, yy)]; tile.Vegetation = 1; fireWorld.Cells[fireWorld.Index(xx, yy)] = tile; }
            engine.ApplyPower(PowerType.Fire, 16, 16, 1);
            GameObject owner = new GameObject("Persistent flame pause fixture"); owner.transform.SetParent(game.transform);
            var effects = new WorldEffects(owner.transform, game.View.SpriteMaterial);
            try
            {
                effects.Reset(fireWorld); effects.Update(fireWorld, .05f, true);
                Mesh flames = owner.GetComponentInChildren<MeshFilter>().sharedMesh; Assert.Greater(flames.vertexCount, 0);
                Vector3[] held = flames.vertices; uint rng = fireWorld.RngState; long tick = fireWorld.Tick;
                float fire = fireWorld.Cells[fireWorld.Index(16, 16)].Fire;
                effects.Update(fireWorld, .05f, true); yield return null; effects.Update(fireWorld, .05f, true);
                CollectionAssert.AreEqual(held, flames.vertices); Assert.AreEqual(tick, fireWorld.Tick);
                Assert.AreEqual(rng, fireWorld.RngState); Assert.AreEqual(fire, fireWorld.Cells[fireWorld.Index(16, 16)].Fire);
                engine.Step(.2f); effects.Update(fireWorld, .05f, false);
                Assert.IsFalse(held.SequenceEqual(flames.vertices), "Advancing real simulation changes the flame pose.");
                engine.Paint(TerrainType.Grass, 16, 16, 2); effects.Reset(fireWorld); effects.Update(fireWorld, .05f, true);
                Assert.AreEqual(0, flames.vertexCount, "Extinguished terrain cannot retain an ongoing flame.");
            }
            finally { effects.Dispose(); Object.Destroy(owner); }
            LogAssert.NoUnexpectedReceived();
        }
        private IEnumerator RefreshTerrain() { yield return new WaitForSecondsRealtime(.35f); for (int i = 0; i < 12; i++) yield return null; }
        private int OpenCell(bool foam = false)
        {
            for (int y = 10; y < game.World.Height - 10; y++) for (int x = 10; x < game.World.Width - 10; x++)
            {
                int cell = game.World.Index(x, y); TileData tile = game.World.Cells[cell];
                if (tile.Terrain == TerrainType.Grass && tile.BuildingId == 0 && tile.SettlementId == 0 && (!foam || (PixelArt.Hash(x, y, game.World.Seed) & 3) == 0)) return cell;
            }
            Assert.Fail("No unbuilt grass site for weather presentation."); return -1;
        }
        private Mesh WeatherMesh() { return game.GetComponentsInChildren<MeshFilter>().First(m => m.name == "Weather • actual front and flood").sharedMesh; }
        private Color GroundPixel(int x, int y) { MeshFilter ground = game.GetComponentsInChildren<MeshFilter>().First(m => m.name == "Ground " + x / 32 + "," + y / 32); return ((Texture2D)ground.GetComponent<MeshRenderer>().sharedMaterial.mainTexture).GetPixel(x % 32 * 8 + 4, y % 32 * 8 + 4); }
        private Button FindButton(string label) { return game.GetComponentsInChildren<Button>().FirstOrDefault(b => b.GetComponentInChildren<Text>()?.text == label); }
        private string VisibleText() { return string.Join("\n", game.GetComponentsInChildren<Text>().Select(t => t.text)); }
    }
}
