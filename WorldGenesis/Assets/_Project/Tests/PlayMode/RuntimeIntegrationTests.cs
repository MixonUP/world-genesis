using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using WorldGenesis.Runtime;
using WorldGenesis.Simulation;

namespace WorldGenesis.Tests
{
    public sealed class RuntimeIntegrationTests
    {
        private GameBootstrap game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            game = GameBootstrap.Instance;
            if (game == null) game = new GameObject("Integration test bootstrap").AddComponent<GameBootstrap>();
            yield return null;
            game.Paused = true;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (game != null) Object.Destroy(game.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BootstrapRendersLiveWorldAndPauseDoesNotAccumulateTicks()
        {
            Assert.NotNull(game.World);
            Assert.NotNull(game.MainCamera);
            Assert.Greater(game.World.Units.Count, 0, "Starting scene must contain living residents.");
            Assert.Greater(Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Length, 0);
            long before = game.World.Tick;
            yield return new WaitForSecondsRealtime(.35f);
            Assert.AreEqual(before, game.World.Tick, "Paused world must not simulate.");
            game.Paused = false;
            yield return new WaitForSecondsRealtime(.35f);
            Assert.Greater(game.World.Tick, before);
            Assert.Less(game.World.Tick - before, 12, "Pause time must not be replayed after resuming.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator HudButtonsSelectProductionToolsAndCapturePointerHits()
        {
            Button life = FindButton("ЖИЗНЬ");
            Assert.NotNull(life);
            ExecuteEvents.Execute(life.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
            yield return null;
            Button humans = FindButton("Люди"); Assert.NotNull(humans);
            humans.onClick.Invoke();
            Assert.AreEqual(ToolKind.Creature, game.Tool.Kind);
            Assert.AreEqual((int)SpeciesType.Human, game.Tool.Value);
            Button powers = FindButton("СИЛЫ БОГА"); powers.onClick.Invoke();
            yield return null;
            FindButton("Метеор").onClick.Invoke();
            Assert.AreEqual(ToolKind.Power, game.Tool.Kind);
            Assert.AreEqual((int)PowerType.Meteor, game.Tool.Value);
            Canvas.ForceUpdateCanvases();
            RectTransform rect = powers.GetComponent<RectTransform>();
            Vector2 position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
            Assert.IsTrue(hits.Exists(hit => hit.gameObject.transform.IsChildOf(powers.transform) || hit.gameObject == powers.gameObject), "HUD must intercept pointer hits.");
            Assert.IsTrue(game.IsScreenPointOverUI(position), "The production input gate must block this UI position immediately.");
            Assert.IsFalse(game.IsScreenPointOverUI(new Vector2(Screen.width * .5f, Screen.height * .5f)), "The free world viewport must remain interactive.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ReplacingWorldRebindsRenderingWithoutDuplicateBootstrap()
        {
            WorldState previous = game.World;
            game.NewWorld(9381);
            game.Paused = true;
            yield return null;
            Assert.AreNotSame(previous, game.World);
            Assert.AreEqual(9381, game.World.Seed);
            Assert.IsTrue(game.World.Validate(out string error), error);
            Assert.AreEqual(1, Object.FindObjectsByType<GameBootstrap>(FindObjectsSortMode.None).Length);
            Assert.NotNull(game.View.Minimap);
            Assert.AreEqual(game.World.Width, game.View.Minimap.width);
            LogAssert.NoUnexpectedReceived();
        }

        private Button FindButton(string label)
        {
            foreach (Button button in game.GetComponentsInChildren<Button>())
            {
                Text text = button.GetComponentInChildren<Text>();
                if (text != null && text.text == label) return button;
            }
            return null;
        }
    }
}
