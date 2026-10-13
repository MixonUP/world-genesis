using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using WorldGenesis.Runtime;

namespace WorldGenesis.Tests
{
    public sealed class HighSpeedHudTests
    {
        [UnityTest]
        public IEnumerator AllHighSpeedButtonsAdvancePauseAndReturnToNormalWithoutCatchUp()
        {
            GameBootstrap game = GameBootstrap.Instance;
            if (game == null) game = new GameObject("High speed UI test").AddComponent<GameBootstrap>();
            yield return null; game.Paused = true;
            try
            {
                foreach (int speed in new[] { 20, 50, 100 })
                {
                    Button button = game.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>()?.text == speed + "×");
                    button.onClick.Invoke(); Assert.AreEqual(speed, game.Speed); Assert.IsFalse(game.Paused);
                    long tick = game.World.Tick; yield return new WaitForSecondsRealtime(.12f);
                    Assert.Greater(game.World.Tick, tick);
                    game.Paused = true; tick = game.World.Tick;
                    yield return new WaitForSecondsRealtime(.12f); Assert.AreEqual(tick, game.World.Tick);
                    Assert.AreEqual(0, game.SimulationBacklogSeconds);
                }
                game.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>()?.text == "1×").onClick.Invoke();
                long start = game.World.Tick; yield return new WaitForSecondsRealtime(.3f);
                Assert.Less(game.World.Tick - start, 9, "Changing speed must not replay an old high-speed backlog.");
            }
            finally { Object.Destroy(game.gameObject); }
            yield return null;
        }
    }
}
