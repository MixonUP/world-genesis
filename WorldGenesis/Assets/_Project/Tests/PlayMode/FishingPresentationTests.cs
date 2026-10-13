using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WorldGenesis.Runtime;
using WorldGenesis.Simulation;

namespace WorldGenesis.Tests
{
    public sealed class FishingPresentationTests
    {
        [Test]
        public void WaterEntryIsContinuousAndBridgeDeckAndMountStayDry()
        {
            WorldState world = Fixture(out UnitData unit);
            SetWater(world, 7, 7);
            unit.X = 7;
            Assert.IsFalse(CitizenWaterPresentation.Resolve(world, unit).Wet);
            unit.X = 7.001f; float first = CitizenWaterPresentation.Resolve(world, unit).Fraction;
            unit.X = 7.10f; float entering = CitizenWaterPresentation.Resolve(world, unit).Fraction;
            unit.X = 7.5f; var waist = CitizenWaterPresentation.Resolve(world, unit);
            Assert.Less(first, .001f); Assert.Greater(entering, first); Assert.Less(entering, waist.Fraction);
            Assert.AreEqual(11, waist.SubmergedCutPixels, .001f); Assert.Less(waist.VerticalOffset, -.4f);
            Assert.IsFalse(CitizenWaterPresentation.Resolve(world, unit, true).Wet);
            world.Infrastructure[world.Index(7, 7)].BridgeLevel = 1;
            Assert.IsFalse(CitizenWaterPresentation.Resolve(world, unit).Wet, "A bridge is dry even over deep river water.");
            world.Infrastructure[world.Index(7, 7)].BridgeLevel = 0;
            unit.Age = 10; unit.Species = SpeciesType.Dwarf;
            var child = CitizenWaterPresentation.Resolve(world, unit);
            Assert.AreEqual(6, child.SubmergedCutPixels); Assert.Less(Mathf.Abs(child.VerticalOffset), Mathf.Abs(waist.VerticalOffset));
            unit.X = 6.99f;
            Assert.IsFalse(CitizenWaterPresentation.Resolve(world, unit).Wet, "Dry bank must not sink because a neighbouring tile is water.");
            unit.X = -1; Assert.IsFalse(CitizenWaterPresentation.Resolve(world, unit).Wet);
            unit.X = float.NaN; Assert.IsFalse(CitizenWaterPresentation.Resolve(world, unit).Wet);
        }

        [UnityTest]
        public IEnumerator ProductionCitizenMeshCutsLegsAndLowersHeadThenRestoresOnTheBridge()
        {
            WorldState world = Fixture(out UnitData unit);
            GameObject parent = new GameObject("Citizen water production proof");
            CitizenSprites citizens = new CitizenSprites(parent.transform);
            try
            {
                citizens.Begin(world); citizens.Add(world, unit, 0, Color.white); citizens.Submit();
                Mesh mesh = parent.GetComponentInChildren<MeshFilter>().sharedMesh;
                Vector3[] dry = mesh.vertices; Vector2[] dryUv = mesh.uv;
                SetWater(world, 7, 7);
                citizens.Begin(world); citizens.Add(world, unit, 0, Color.white); citizens.Submit();
                var wet = CitizenWaterPresentation.Resolve(world, unit);
                Assert.AreEqual(dry.Max(v => v.y) + wet.VerticalOffset, mesh.vertices.Max(v => v.y), .0001f);
                Assert.AreEqual(wet.SurfacePosition.y, mesh.vertices.Min(v => v.y), .0001f);
                Assert.Greater(mesh.uv.Min(v => v.y), dryUv.Min(v => v.y), "Lower leg texels must be clipped, not squeezed into the visible torso.");
                Vector3[] held = mesh.vertices; long tick = world.Tick;
                yield return null;
                citizens.Begin(world); citizens.Add(world, unit, 0, Color.white); citizens.Submit();
                CollectionAssert.AreEqual(held, mesh.vertices); Assert.AreEqual(tick, world.Tick);
                world.Infrastructure[world.Index(7, 7)].BridgeLevel = 1;
                citizens.Begin(world); citizens.Add(world, unit, 0, Color.white); citizens.Submit();
                CollectionAssert.AreEqual(dry, mesh.vertices); CollectionAssert.AreEqual(dryUv, mesh.uv);
                Assert.AreEqual(7.5f, unit.X); Assert.AreEqual(7.5f, unit.Y);
            }
            finally { citizens.Dispose(); Object.Destroy(parent); }
            yield return null;
        }

        [Test]
        public void RodOnlyAppearsForAnActualFishingActionAndFacesTheResource()
        {
            WorldState world = Fixture(out UnitData unit); PrepareFishing(world, unit);
            Assert.IsTrue(FishingPresentation.IsFishing(world, unit));
            Assert.AreEqual(CitizenSprites.CitizenAction.Fish, CitizenSprites.ResolvePose(world, unit, Vector2.right).Action);
            unit.Path.Add(world.Index(7, 8));
            Assert.IsFalse(FishingPresentation.IsFishing(world, unit));
            Assert.AreEqual(CitizenSprites.CitizenAction.Walk, CitizenSprites.ResolvePose(world, unit, Vector2.right).Action);
            unit.Path.Clear(); unit.WorkTimer = 0; Assert.IsFalse(FishingPresentation.IsFishing(world, unit));
            unit.WorkTimer = 4; unit.Health = 0; Assert.IsFalse(FishingPresentation.IsFishing(world, unit));
            unit.Health = 100; unit.Age = 12; Assert.IsFalse(FishingPresentation.IsFishing(world, unit));
            unit.Age = 25; unit.CarriedFood = 1; Assert.IsFalse(FishingPresentation.IsFishing(world, unit));
            unit.CarriedFood = 0; world.Infrastructure[unit.ResourceCell].Fish = 0; Assert.IsFalse(FishingPresentation.IsFishing(world, unit));
            world.Infrastructure[unit.ResourceCell].Fish = 4; unit.X = 3.5f; Assert.IsFalse(FishingPresentation.IsFishing(world, unit));
            unit.X = 7.5f; unit.Task = UnitTask.Flee;
            Assert.IsFalse(FishingPresentation.IsFishing(world, unit));
            Assert.AreEqual(CitizenSprites.CitizenAction.Flee, CitizenSprites.ResolvePose(world, unit, Vector2.right).Action);
        }

        [UnityTest]
        public IEnumerator FiveRodGradesChangeProductionGeometryAndPauseHasNoWallClockDrift()
        {
            WorldState world = Fixture(out UnitData unit); PrepareFishing(world, unit);
            GameObject parent = new GameObject("Fishing rig production proof");
            Material material = new Material(Shader.Find("Sprites/Default"));
            QuadBatch batch = new QuadBatch("Fishing proof", parent.transform, material, 60);
            CitizenSprites citizens = new CitizenSprites(parent.transform);
            try
            {
                Mesh mesh = parent.GetComponentsInChildren<MeshFilter>().First(m => m.name == "Fishing proof").sharedMesh;
                Vector3[] previous = null; Color32[] previousColors = null;
                for (int grade = 1; grade <= 5; grade++)
                {
                    unit.FishingRodLevel = grade;
                    batch.Clear(); FishingPresentation.Draw(batch, world, unit, new Vector2(unit.X, unit.Y), Vector2.right); batch.Submit();
                    Assert.Greater(mesh.vertexCount, 20);
                    if (previous != null) { Assert.IsFalse(previous.SequenceEqual(mesh.vertices)); Assert.IsFalse(previousColors.SequenceEqual(mesh.colors32)); }
                    previous = mesh.vertices; previousColors = mesh.colors32;
                }
                var rig = FishingPresentation.BuildRig(world, unit, new Vector2(unit.X, unit.Y), Vector2.right);
                yield return null;
                var paused = FishingPresentation.BuildRig(world, unit, new Vector2(unit.X, unit.Y), Vector2.right);
                Assert.AreEqual(rig.Tip, paused.Tip); Assert.AreEqual(rig.Bobber, paused.Bobber);
                world.Tick += 10;
                var moving = FishingPresentation.BuildRig(world, unit, new Vector2(unit.X, unit.Y), Vector2.right);
                Assert.AreNotEqual(rig.Tip, moving.Tip); Assert.AreNotEqual(rig.Bobber, moving.Bobber);
                int[] cells = { world.Index(8, 7), world.Index(6, 7), world.Index(7, 8), world.Index(7, 6) };
                Vector2[] directions = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };
                for (int i = 0; i < cells.Length; i++)
                {
                    unit.ResourceCell = cells[i]; SetWater(world, cells[i] % world.Width, cells[i] / world.Width);
                    world.Infrastructure[cells[i]].Fish = 4;
                    citizens.Begin(world); citizens.Add(world, unit, 0, Color.white); citizens.Submit();
                    Assert.AreEqual(directions[i], citizens.Facing(unit.Id), "Stationary fishing must face its actual water patch in all four directions.");
                    var cast = FishingPresentation.BuildRig(world, unit, new Vector2(unit.X, unit.Y), directions[i]);
                    Assert.AreEqual(cells[i] % world.Width, Mathf.FloorToInt(cast.Bobber.x));
                    Assert.AreEqual(cells[i] / world.Width, Mathf.FloorToInt(cast.Bobber.y));
                    Vector2 center = new Vector2(cells[i] % world.Width + .5f, cells[i] / world.Width + .5f);
                    Assert.Greater(Vector2.Dot(cast.Bobber - center, directions[i]), .20f, "Float must land further into the same water tile rather than over the fisher's head.");
                }
                unit.Task = UnitTask.Return;
                batch.Clear(); FishingPresentation.Draw(batch, world, unit, new Vector2(unit.X, unit.Y), Vector2.right); batch.Submit();
                Assert.AreEqual(0, mesh.vertexCount, "Cancelling fishing must remove rod, line and float in the same frame.");
                Assert.AreEqual(4, world.Infrastructure[unit.ResourceCell].Fish);
                Assert.AreEqual(0, unit.CarriedFood);
            }
            finally { batch.Dispose(); citizens.Dispose(); Object.Destroy(material); Object.Destroy(parent); }
            yield return null;
        }

        private static WorldState Fixture(out UnitData unit)
        {
            WorldState world = new WorldState { Width = 16, Height = 16, Tick = 100, Seed = 3, Cells = new TileData[256], Infrastructure = new InfrastructureCell[256], ResourceNodes = new ResourceNodeData[256] };
            for (int i = 0; i < world.Cells.Length; i++) world.Cells[i].Terrain = TerrainType.Grass;
            unit = new UnitData { Id = 2, Species = SpeciesType.Human, Age = 25, Health = 100, MaxHealth = 100, X = 7.5f, Y = 7.5f, Task = UnitTask.Idle, FishingRodLevel = 1 };
            world.Units.Add(unit); return world;
        }
        private static void PrepareFishing(WorldState world, UnitData unit)
        {
            SetWater(world, 8, 7); unit.ResourceCell = world.Index(8, 7); unit.Task = UnitTask.Fish; unit.WorkTimer = 4;
            world.Infrastructure[unit.ResourceCell].Fish = world.Infrastructure[unit.ResourceCell].FishCapacity = 4;
        }
        private static void SetWater(WorldState world, int x, int y)
        {
            int cell = world.Index(x, y); world.Cells[cell].Terrain = TerrainType.ShallowWater;
            world.Cells[cell].WaterKind = WaterKind.River; world.Cells[cell].WaterDepth = .4f;
        }
    }
}
