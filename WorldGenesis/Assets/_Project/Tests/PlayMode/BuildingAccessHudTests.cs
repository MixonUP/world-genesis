using System.Collections;
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
    public sealed class BuildingAccessHudTests
    {
        private GameBootstrap game;
        [UnitySetUp] public IEnumerator SetUp()
        {
            game = GameBootstrap.Instance;
            if (game == null) game = new GameObject("Indoor inspector integration").AddComponent<GameBootstrap>();
            yield return null; game.Paused = true; yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        { if (game != null) Object.Destroy(game.gameObject); yield return null; }

        [UnityTest]
        public IEnumerator BuildingClickAndOccupantButtonsInspectHiddenResidentWithoutMutatingWorld()
        {
            // Authored paused presentation fixture; core entry/save behavior has independent tests.
            WorldState world = game.World;
            BuildingData house = AddPresentationHouse(world);
            UnitData resident = world.Units.First(u => u.SettlementId == house.SettlementId && (int)u.Species < 4);
            resident.InsideBuildingId = house.Id; resident.EnteringBuildingId = 0; resident.BuildingInteractionTimer = 0;
            resident.X = house.X + .5f; resident.Y = house.Y + .5f; resident.Path.Clear(); resident.PathCursor = 0;
            resident.Task = UnitTask.Rest;
            string before = JsonUtility.ToJson(world);
            typeof(GameBootstrap).GetMethod("Inspect", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(game, new object[] { new Vector3(house.X + .5f, house.Y + .5f, 0) });
            yield return null;
            Assert.AreEqual(0, game.SelectedUnitId, "Clicking a wall cannot select its invisible occupant.");
            Button occupants = game.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<Text>().text.StartsWith("Внутри:"));
            occupants.onClick.Invoke(); yield return null;
            Assert.AreEqual(resident.Id, game.SelectedUnitId);
            Assert.IsTrue(game.GetComponentsInChildren<Text>().Any(t => t.text.Contains("Внутри: " + ResourceArt.BuildingName(house.Type))), "The selected hidden resident must explain where they are.");
            game.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<Text>().text == "К зданию").onClick.Invoke();
            yield return null;
            Assert.AreEqual(0, game.SelectedUnitId); Assert.AreEqual(house.X, game.InspectX); Assert.AreEqual(house.Y, game.InspectY);
            Assert.AreEqual(before, JsonUtility.ToJson(world), "Inspection and cycling do not change interior state, goods or RNG.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void OccupantSelectionExcludesDeadAndQueuedVisitorsAndWraps()
        {
            WorldState world = game.World; BuildingData house = AddPresentationHouse(world);
            var inside = new UnitData { Id = 900001, Health = 100, InsideBuildingId = house.Id };
            var waiting = new UnitData { Id = 900002, Health = 100, EnteringBuildingId = house.Id };
            var dead = new UnitData { Id = 900003, Health = 0, InsideBuildingId = house.Id };
            world.Units.Add(inside); world.Units.Add(waiting); world.Units.Add(dead);
            Assert.AreEqual(inside.Id, GameHud.NextOccupantId(world, house.Id, 0));
            Assert.AreEqual(inside.Id, GameHud.NextOccupantId(world, house.Id, inside.Id));
            Assert.AreEqual(0, GameHud.NextOccupantId(world, 999999, 0));
        }

        private static BuildingData AddPresentationHouse(WorldState world)
        {
            // A primitive settlement intentionally starts without houses. Author one paused
            // inspector fixture, with an actual door and exterior road, instead of waiting for AI.
            SettlementData settlement = world.Settlements.First();
            int site = Enumerable.Range(world.Width, world.Cells.Length - 2 * world.Width)
                .First(cell => cell % world.Width > 0 && cell % world.Width < world.Width - 1 &&
                    world.Cells[cell].Terrain == TerrainType.Grass && world.Cells[cell - world.Width].Terrain == TerrainType.Grass &&
                    world.IsWalkable(cell % world.Width, cell / world.Width) && world.Cells[cell].BuildingId == 0 &&
                    world.IsWalkable(cell % world.Width, cell / world.Width - 1) && world.Cells[cell - world.Width].BuildingId == 0 &&
                    world.Units.All(u => (u.X - cell % world.Width - .5f) * (u.X - cell % world.Width - .5f) +
                        (u.Y - cell / world.Width - .5f) * (u.Y - cell / world.Width - .5f) > 9));
            BuildingData house = new BuildingData { Id = world.NextId++, SettlementId = settlement.Id, Type = BuildingType.House,
                X = site % world.Width, Y = site / world.Width, Level = 1, EntranceCell = site - world.Width };
            world.Buildings.Add(house); world.Cells[site].BuildingId = house.Id; world.Cells[site].SettlementId = settlement.Id;
            world.Infrastructure[house.EntranceCell].RoadLevel = 1;
            world.Revision++; world.TerrainRevision++; world.NavigationRevision++;
            Assert.IsTrue(BuildingAccess.IsUsable(world, house), "The explicit inspector fixture must have a reachable exterior doorway.");
            return house;
        }
    }
}
