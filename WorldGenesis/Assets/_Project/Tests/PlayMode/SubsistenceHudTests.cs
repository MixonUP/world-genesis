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
    // Prepared presentation states. Core tests separately verify production and consumption.
    public sealed class SubsistenceHudTests
    {
        private GameBootstrap game;
        private BuildingData campfire;
        private SettlementData town;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            game = GameBootstrap.Instance;
            if (game == null) game = new GameObject("Subsistence HUD integration").AddComponent<GameBootstrap>();
            yield return null; game.Paused = true; yield return null;
            town = game.World.Settlements[0]; campfire = game.World.GetCampfire(town.Id);
            Assert.NotNull(campfire);
            Assert.IsTrue(SubsistenceRules.IsWorkingCampfire(game.World, campfire), "The starting civic campfire must be available.");
        }

        [UnityTearDown]
        public IEnumerator TearDown() { if (game != null) Object.Destroy(game.gameObject); yield return null; }

        [UnityTest]
        public IEnumerator ActualReservesRemainAboveSoftFoodCapAndUnchangedByInspection()
        {
            town.Food = 73; campfire.WaterStored = 12;
            game.InspectSite(campfire.X, campfire.Y); yield return null;
            Text body = InspectorBody();
            StringAssert.Contains("Работает · на 25 жителей", body.text);
            StringAssert.Contains("Мощность в день · еда / вода\n25 / 25", body.text);
            StringAssert.Contains("Вода 12 / 50", body.text);
            StringAssert.Contains("Еда общины 73", body.text);
            StringAssert.Contains("Очаг пополняет еду до 50", body.text);
            StringAssert.Contains("День: 10 мин при 1×", body.text);
            campfire.Level = 5; game.InspectSite(campfire.X, campfire.Y); yield return null;
            StringAssert.Contains("Мощность в день · еда / вода\n25 / 25", body.text);
            StringAssert.Contains("Вода 12 / 50", body.text);
            ScrollRect scroll = body.GetComponentInParent<ScrollRect>();
            Assert.NotNull(scroll); Assert.IsTrue(scroll.vertical); Assert.IsFalse(scroll.horizontal);
            Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 0; yield return null;
            Assert.AreEqual(0, scroll.verticalNormalizedPosition, .001f, "The long factual inspector must remain scrollable to its bottom.");
            long tick = game.World.Tick; uint rng = game.World.RngState;
            yield return new WaitForSecondsRealtime(.3f);
            Assert.AreEqual(73, town.Food, "Showing a passive reserve limit must not clamp collected food.");
            Assert.AreEqual(12, campfire.WaterStored); Assert.AreEqual(tick, game.World.Tick); Assert.AreEqual(rng, game.World.RngState);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DuplicateAndBlockedCampfiresDoNotOverstateDailyBaseSupply()
        {
            // Population is a deliberate display fixture, not a claim about natural births.
            town.Population = 30;
            BuildingData duplicate = game.World.Buildings.First(b => b.SettlementId == town.Id && b.Id > campfire.Id && b.Type != BuildingType.Hall);
            duplicate.Type = BuildingType.Hall; duplicate.Level = 5; duplicate.WaterStored = 7;
            Assert.AreSame(campfire, game.World.GetCampfire(town.Id));
            game.InspectSite(duplicate.X, duplicate.Y); yield return null;
            Text body = InspectorBody();
            StringAssert.Contains("Дополнительный очаг · без выработки", body.text);
            StringAssert.Contains("Мощность в день · еда / вода\n0 / 0", body.text);
            StringAssert.Contains("Вода 7 / 50", body.text);
            StringAssert.Contains("Нужно для 30: 30 / 30", body.text);
            StringAssert.Contains("Один очаг: 25 / 25", body.text);
            StringAssert.Contains("Дефицит базы: 5 / 5", body.text);
            int cell = game.World.Index(campfire.X, campfire.Y);
            TileData tile = game.World.Cells[cell]; tile.Fire = .5f; game.World.Cells[cell] = tile;
            game.InspectSite(campfire.X, campfire.Y); yield return null;
            StringAssert.Contains("Снабжение приостановлено", body.text);
            StringAssert.Contains("Один очаг: 0 / 0", body.text);
            StringAssert.Contains("Дефицит базы: 30 / 30", body.text);
            tile.Fire = 0; game.World.Cells[cell] = tile;
            game.InspectSite(campfire.X, campfire.Y); yield return null;
            StringAssert.Contains("Работает · на 25 жителей", body.text);
            StringAssert.Contains("Дефицит базы: 5 / 5", body.text);
            Assert.AreEqual(7, duplicate.WaterStored, "Inspector updates must not move the duplicate's existing water.");
            int townCell = Enumerable.Range(0, game.World.Cells.Length).First(i => game.World.Cells[i].SettlementId == town.Id && game.World.Cells[i].BuildingId == 0);
            game.InspectSite(townCell % game.World.Width, townCell / game.World.Width); yield return null;
            StringAssert.StartsWith("<b>БАЗОВЫЙ ПЛАН НА ДЕНЬ</b>", body.text, "The settlement's daily needs must precede weather, terrain and politics.");
            Assert.AreEqual(body.text.IndexOf("<b>БАЗОВЫЙ ПЛАН НА ДЕНЬ</b>"), body.text.LastIndexOf("<b>БАЗОВЫЙ ПЛАН НА ДЕНЬ</b>"), "The reordered plan must appear only once.");
            string firstSection = body.text.Substring(0, body.text.IndexOf("\n\n"));
            StringAssert.Contains("День: 10 мин при 1×", firstSection);
            StringAssert.Contains("На жителя: 1 / 1", firstSection);
            StringAssert.Contains("Дефицит базы: 5 / 5", firstSection);
            Canvas.ForceUpdateCanvases();
            float planHeight = body.cachedTextGeneratorForLayout.GetPreferredHeight(firstSection, body.GetGenerationSettings(new Vector2(body.rectTransform.rect.width, 0))) / body.pixelsPerUnit;
            Assert.LessOrEqual(planHeight, body.GetComponentInParent<ScrollRect>().viewport.rect.height, "The complete daily plan must fit the first inspector screen at its actual font and width.");
            LogAssert.NoUnexpectedReceived();
        }

        private Text InspectorBody() => game.GetComponentsInChildren<Text>().First(t => t.text.Contains("<b>СНАБЖЕНИЕ ОЧАГА</b>"));
    }
}
