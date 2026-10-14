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
    public sealed class ExpansionHudTests
    {
        private GameBootstrap game;
        [UnitySetUp] public IEnumerator SetUp()
        { game = GameBootstrap.Instance; if (game == null) game = new GameObject("Settlement HUD verification").AddComponent<GameBootstrap>(); yield return null; game.Paused = true; }
        [UnityTearDown] public IEnumerator TearDown()
        { if (game != null) Object.Destroy(game.gameObject); yield return null; }

        [UnityTest] public IEnumerator HallCommandsDeclareAndEndWarWithoutMintingWeaponsOrPeople()
        {
            WorldState w = game.World;
            Assert.GreaterOrEqual(w.Settlements.Count, 2);
            SettlementData home = w.Settlements[0]; int population = w.Units.Count;
            float weapons = w.Buildings.Sum(b => b.Inventory[(int)ItemType.Sword] + b.Inventory[(int)ItemType.Spear]);
            game.InspectSite(home.X, home.Y); yield return null;
            Button war = game.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<Text>().text == "Война / мир с соседом");
            Assert.IsTrue(war.gameObject.activeInHierarchy);
            war.onClick.Invoke(); yield return null;
            Assert.IsTrue(w.Relations.Any(r => r.AtWar && (r.FirstId == home.Id || r.SecondId == home.Id)));
            Button recruit = game.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<Text>().text == "Собрать вооружённый отряд");
            recruit.onClick.Invoke(); yield return null;
            Assert.IsFalse(w.Units.Any(u => u.WarWeapon != ItemType.None), "Primitive people cannot receive free equipment.");
            war.onClick.Invoke(); yield return null;
            Assert.IsFalse(w.Relations.Any(r => r.AtWar && (r.FirstId == home.Id || r.SecondId == home.Id)));
            Assert.AreEqual(population, w.Units.Count);
            Assert.AreEqual(weapons, w.Buildings.Sum(b => b.Inventory[(int)ItemType.Sword] + b.Inventory[(int)ItemType.Spear]));
            game.InspectUnit(w.Units.First(u => SimulationEngine.IsCivilized(u.Species)).Id); yield return null;
            Assert.IsFalse(war.gameObject.activeInHierarchy, "Town controls cannot stay over the citizen inspector.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
