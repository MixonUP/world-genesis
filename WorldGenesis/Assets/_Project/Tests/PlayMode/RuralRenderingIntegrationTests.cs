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
    // Explicit presentation fixtures: these verify production meshes/HUD read model fields.
    // They do not claim that equipment was naturally crafted or a farm naturally matured.
    public sealed class RuralRenderingIntegrationTests
    {
        private GameBootstrap game;
        [UnitySetUp] public IEnumerator SetUp()
        {
            game = GameBootstrap.Instance;
            if (game == null) game = new GameObject("Rural rendering integration").AddComponent<GameBootstrap>();
            yield return null; game.Paused = true; yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown() { if (game != null) Object.Destroy(game.gameObject); yield return null; }

        [UnityTest]
        public IEnumerator FieldsPastureAndBreweryUseActualValuesInMeshAndInspector()
        {
            BuildingData site = game.World.Buildings.Find(b => b.Type == BuildingType.Farm);
            Assert.NotNull(site, "The starting village needs its real farm.");
            site.FarmCrop = CropType.Wheat; site.CropGrowth = .1f; site.HarvestReady = 0;
            game.InspectSite(site.X, site.Y); yield return null;
            StringAssert.Contains("Пшеница", VisibleText()); StringAssert.Contains("Рост 10", VisibleText());
            MeshFilter mesh = FindMesh("Fields and rural buildings"); Assert.NotNull(mesh); Assert.Greater(mesh.sharedMesh.vertexCount, 0);
            Vector2[] wheat = mesh.sharedMesh.uv;
            site.FarmCrop = CropType.Flax; site.CropGrowth = 1; site.HarvestReady = 12;
            game.InspectSite(site.X, site.Y); yield return null;
            StringAssert.Contains("Лён", VisibleText()); StringAssert.Contains("Рост 100", VisibleText()); StringAssert.Contains("Готово к сбору 12", VisibleText());
            Assert.IsFalse(wheat.SequenceEqual(mesh.sharedMesh.uv), "The live mesh must change to the new crop and growth stage.");
            Vector2[] flax = mesh.sharedMesh.uv;
            site.Type = BuildingType.Pasture; site.HerdSpecies = (int)SpeciesType.Cow;
            game.InspectSite(site.X, site.Y); yield return null;
            StringAssert.Contains("Пастбище", VisibleText()); StringAssert.Contains("Корова", VisibleText());
            Assert.IsFalse(flax.SequenceEqual(mesh.sharedMesh.uv), "A pasture must use its enclosure image.");
            site.Type = BuildingType.Brewery; site.BrewingProgress = 3; site.BrewWaterStored = 1.5f; site.AgriculturalStock[(int)AgriculturalProduct.Ale] = 2;
            site.Inventory[(int)ItemType.Plank] = 3;
            game.InspectSite(site.X, site.Y); yield return null;
            StringAssert.Contains("Пивоварня", VisibleText()); StringAssert.Contains("Варка эля · 3", VisibleText()); StringAssert.Contains("Эль  2", VisibleText());
            StringAssert.Contains("Вода для варки " + site.BrewWaterStored.ToString("0.###"), VisibleText()); StringAssert.Contains("Доски  3", VisibleText());
            Assert.Greater(FindMesh("Actual agricultural goods").sharedMesh.vertexCount, 0);
            float stock = game.World.GetAgriculturalStock(site.SettlementId, AgriculturalProduct.Ale);
            long tick = game.World.Tick; Vector2[] pausedUv = mesh.sharedMesh.uv;
            yield return new WaitForSecondsRealtime(.25f);
            Assert.AreEqual(stock, game.World.GetAgriculturalStock(site.SettlementId, AgriculturalProduct.Ale), "Rendering must not produce or consume ale.");
            Assert.AreEqual(1.5f, site.BrewWaterStored, "The shown local water buffer must not refill or empty while paused.");
            Assert.AreEqual(tick, game.World.Tick); CollectionAssert.AreEqual(pausedUv, mesh.sharedMesh.uv);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RealCargoEquipmentAndMountRemainVisibleWithoutCreditingStock()
        {
            UnitData worker = game.World.Units.Find(u => u.Species == SpeciesType.Human && !u.IsChild);
            Assert.NotNull(worker);
            worker.Path.Clear(); worker.PathCursor = 0; worker.Task = UnitTask.Idle;
            worker.BasketLevel = 0; worker.MountId = 0; worker.CarriedFood = worker.CarriedWood = worker.CarriedWater = 0;
            worker.CargoKind = ItemType.Clay; worker.CargoAmount = .75f; worker.HarvestProduct = AgriculturalProduct.Herbs; worker.HarvestAmount = .25f;
            float stock = game.World.GetSettlementStock(worker.SettlementId, ItemType.Clay);
            game.InspectUnit(worker.Id); yield return null;
            StringAssert.Contains("НОША  1 / 1", VisibleText()); StringAssert.Contains("Травы", VisibleText()); StringAssert.Contains("Без корзины", VisibleText());
            MeshFilter carried = FindMesh("Life and citizen markers"); Rect pail = PixelArt.Uv(SocietyArt.WaterPail); Vector2 pailCorner = new Vector2(pail.xMin, pail.yMin);
            int pailsBefore = carried.sharedMesh.uv.Count(uv => Vector2.Distance(uv, pailCorner) < .00001f);
            worker.CargoAmount = .5f; worker.CarriedWater = .25f; game.InspectUnit(worker.Id); yield return null;
            StringAssert.Contains("Вода  " + worker.CarriedWater.ToString("0.###"), VisibleText()); StringAssert.Contains("НОША  1 / 1", VisibleText());
            Assert.Greater(carried.sharedMesh.uv.Count(uv => Vector2.Distance(uv, pailCorner) < .00001f), pailsBefore, "Actual carried water must add the production pail sprite.");
            worker.CargoAmount = .75f; worker.CarriedWater = 0; game.InspectUnit(worker.Id); yield return null;
            Assert.AreEqual(pailsBefore, carried.sharedMesh.uv.Count(uv => Vector2.Distance(uv, pailCorner) < .00001f), "An empty water load must not keep its bucket glyph.");
            worker.BasketLevel = 3; game.InspectUnit(worker.Id); yield return null;
            StringAssert.Contains("НОША  1 / 25", VisibleText()); StringAssert.Contains("Корзина · уровень 3", VisibleText());
            Assert.Greater(FindMesh("Owned carried equipment").sharedMesh.vertexCount, 0);
            game.Engine.Spawn(SpeciesType.Horse, (int)worker.X, (int)worker.Y);
            UnitData horse = game.World.Units.Last(u => u.Species == SpeciesType.Horse);
            horse.X = worker.X; horse.Y = worker.Y; horse.HandlerId = worker.Id; horse.AnimalOwnerId = worker.SettlementId; horse.IsDomestic = true;
            horse.Path.Clear(); horse.PathCursor = 0;
            worker.MountId = horse.Id; worker.MountSpecies = SpeciesType.Horse; worker.MountBags = false; worker.HasWagon = false;
            game.InspectUnit(worker.Id); yield return null;
            Assert.IsTrue(WorldView.IsRiding(game.World, worker));
            StringAssert.Contains("НОША  1 / 75", VisibleText()); StringAssert.Contains("верхом", VisibleText());
            Assert.Greater(FindMesh("Actual wildlife").sharedMesh.vertexCount, 0);
            Vector3[] animal = FindMesh("Actual wildlife").sharedMesh.vertices;
            worker.MountBags = true; game.InspectUnit(worker.Id); yield return null;
            StringAssert.Contains("НОША  1 / 100", VisibleText()); StringAssert.Contains("Вьючные сумки", VisibleText());
            worker.OwnsWagon = worker.HasWagon = true; game.InspectUnit(worker.Id); yield return null;
            Assert.IsFalse(WorldView.IsRiding(game.World, worker), "A wagon is drawn behind its animal; the accompanying worker is not a seated rider.");
            StringAssert.Contains("НОША  1 / 150", VisibleText()); StringAssert.Contains("Запряжена телега", VisibleText());
            Assert.AreEqual(stock, game.World.GetSettlementStock(worker.SettlementId, ItemType.Clay), "Inspecting a mounted carrier must not deliver goods.");
            CollectionAssert.AreEqual(animal, FindMesh("Actual wildlife").sharedMesh.vertices, "Equipment changes cannot move a paused animal.");
            Assert.AreEqual(1, SimulationEngine.CarriedLoad(worker), .0001f);
            SettlementData destination = game.World.Settlements.First(s => s.Id != worker.SettlementId);
            CaravanData caravan = new CaravanData { Id = game.World.NextCaravanId++, OriginId = worker.SettlementId, DestinationId = destination.Id, LeaderId = worker.Id, ExportItem = ItemType.Clay, ImportItem = ItemType.RawStone, Phase = CaravanPhase.Outbound };
            caravan.Members.Add(new CaravanMemberData { UnitId = worker.Id, Phase = CaravanMemberPhase.Outbound, ExportQuota = 25 });
            worker.CaravanId = caravan.Id; worker.Task = UnitTask.Caravan; game.World.Caravans.Add(caravan);
            game.InspectUnit(worker.Id); yield return null;
            StringAssert.Contains("Фактически несёт: Глина " + worker.CargoAmount.ToString("0.###"), VisibleText(), "A planned export quota must not become displayed physical goods.");
            worker.CargoAmount = .2f; game.InspectUnit(worker.Id); yield return null;
            StringAssert.Contains("Фактически несёт: Глина " + worker.CargoAmount.ToString("0.###"), VisibleText());
            Assert.AreEqual(25, caravan.Members[0].ExportQuota, "Reading cargo must not rewrite the saved plan.");
            Assert.AreEqual(stock, game.World.GetSettlementStock(worker.SettlementId, ItemType.Clay));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AnimalPalettePagesExposeLastSpeciesWithNativeIconAndUiBlocking()
        {
            FindButton("ЖИЗНЬ").onClick.Invoke(); yield return null;
            Assert.NotNull(FindButton("Люди")); Assert.NotNull(FindButton("Лисы"));
            FindButton("Жизнь 1/2  ›").onClick.Invoke(); yield return null;
            Button horse = FindButton("Лошади"); Assert.NotNull(horse);
            Image glyph = horse.GetComponentsInChildren<Image>().First(i => i.name == "Symbol");
            Assert.NotNull(glyph.sprite); Assert.AreEqual(WildlifeArt.Width, glyph.sprite.texture.width); Assert.AreEqual(WildlifeArt.Height, glyph.sprite.texture.height); Assert.AreEqual(FilterMode.Point, glyph.sprite.texture.filterMode);
            Rect horseRegion = WildlifeArt.Uv(WildlifeArt.SpriteFor(SpeciesType.Horse, Sex.Male, WildlifeArt.AnimalDirection.East, 0));
            Assert.AreEqual(horseRegion.x * WildlifeArt.Width, glyph.sprite.rect.x, .01f); Assert.AreEqual(horseRegion.y * WildlifeArt.Height, glyph.sprite.rect.y, .01f);
            Assert.AreEqual(horseRegion.width * WildlifeArt.Width, glyph.sprite.rect.width, .01f); Assert.AreEqual(horseRegion.height * WildlifeArt.Height, glyph.sprite.rect.height, .01f);
            horse.onClick.Invoke(); Assert.AreEqual(ToolKind.Creature, game.Tool.Kind); Assert.AreEqual((int)SpeciesType.Horse, game.Tool.Value);
            Canvas.ForceUpdateCanvases(); RectTransform rect = horse.GetComponent<RectTransform>();
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            Assert.Greater(point.x, 0); Assert.Less(point.x, Screen.width); Assert.Greater(point.y, 0); Assert.Less(point.y, Screen.height);
            Assert.IsTrue(game.IsScreenPointOverUI(point));
            FindButton("Жизнь 2/2  ›").onClick.Invoke(); yield return null;
            Assert.NotNull(FindButton("Люди")); LogAssert.NoUnexpectedReceived();
        }
        private MeshFilter FindMesh(string name) { return game.GetComponentsInChildren<MeshFilter>().FirstOrDefault(m => m.name == name); }
        private Button FindButton(string label) { return game.GetComponentsInChildren<Button>().FirstOrDefault(b => b.GetComponentInChildren<Text>()?.text == label); }
        private string VisibleText() { return string.Join("\n", game.GetComponentsInChildren<Text>().Select(t => t.text)); }
    }
}
