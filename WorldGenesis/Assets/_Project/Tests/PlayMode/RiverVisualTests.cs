using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WorldGenesis.Runtime;
using WorldGenesis.Simulation;

namespace WorldGenesis.Tests
{
    public sealed class RiverVisualTests
    {
        private static WorldState EmptyWorld()
        {
            var world = new WorldState { Width = 64, Height = 32, Seed = 8013, RngState = 7123,
                Cells = new TileData[64 * 32], Infrastructure = new InfrastructureCell[64 * 32], ResourceNodes = new ResourceNodeData[64 * 32] };
            for (int i = 0; i < world.Cells.Length; i++) world.Cells[i] = new TileData { Terrain = TerrainType.Grass, Moisture = .5f, Temperature = .6f, Height = .6f, DrainageParent = -1 };
            return world;
        }

        private static void Water(WorldState world, int x, int y, WaterKind kind = WaterKind.River, float current = .75f)
        {
            world.Cells[world.Index(x, y)] = new TileData { Terrain = TerrainType.ShallowWater, WaterKind = kind, WaterDepth = 1.2f,
                River = kind == WaterKind.River, CurrentStrength = current, Moisture = 1, Height = .5f, Temperature = .6f, DrainageParent = world.InBounds(x, y - 1) ? world.Index(x, y - 1) : -1 };
        }

        [Test]
        public void DownstreamRejectsInvalidNonLocalAndDryParentLinks()
        {
            WorldState world = EmptyWorld(); int index = world.Index(32, 16); Water(world, 32, 16); Water(world, 32, 15);
            Assert.IsTrue(RiverArt.TryDownstream(world, index, out Vector2 direction)); Assert.AreEqual(Vector2.down, direction);
            int[] invalid = { -1, world.Cells.Length, index, world.Index(33, 16), world.Index(32, 12) };
            foreach (int parent in invalid)
            {
                world.Cells[index].DrainageParent = parent;
                Assert.IsFalse(RiverArt.TryDownstream(world, index, out direction), "Current cannot invent a direction from parent " + parent);
                Assert.AreEqual(Vector2.zero, direction);
            }
            world.Cells[index].DrainageParent = world.Index(32, 15); world.Cells[index].CurrentStrength = 0;
            Assert.IsFalse(RiverArt.TryDownstream(world, index, out direction));
            world.Cells[index].CurrentStrength = .08f; world.Cells[index].WaterKind = WaterKind.Ocean;
            Assert.IsFalse(RiverArt.TryDownstream(world, index, out direction), "Ocean drainage links are not animated as river currents.");
            world.Cells[index].WaterKind = WaterKind.Lake;
            Assert.IsFalse(RiverArt.TryDownstream(world, index, out direction), "Lake drainage links are not animated as river currents.");
        }

        [Test]
        public void BanksSurroundAllFourSidesAndDiagonalCornersWithoutChangingTheWorld()
        {
            WorldState world = EmptyWorld(); Water(world, 32, 16);
            TileData water = world.Cells[world.Index(32, 16)]; RiverArt.Bank sample = RiverArt.SampleBank(world, 32, 16);
            Assert.AreEqual(.0625f, RiverArt.BankDistance(sample, .0625f, .5f), .0001f);
            Assert.AreEqual(.0625f, RiverArt.BankDistance(sample, .9375f, .5f), .0001f);
            Assert.AreEqual(.0625f, RiverArt.BankDistance(sample, .5f, .0625f), .0001f);
            Assert.AreEqual(.0625f, RiverArt.BankDistance(sample, .5f, .9375f), .0001f);
            Assert.IsTrue(RiverArt.SampleBank(world, 31, 15).HasShore, "Diagonal bank samples must cross the chunk corner too.");
            uint random = world.RngState;
            Color edge = RiverArt.ShadePixel(water, sample, 32 * 8, 16 * 8 + 4, world.Seed, Color.black);
            Color center = RiverArt.ShadePixel(water, sample, 32 * 8 + 4, 16 * 8 + 4, world.Seed, Color.black);
            Assert.AreNotEqual(edge, center, "The submerged shelf needs a depth gradient.");
            Assert.AreEqual(edge, RiverArt.ShadePixel(water, sample, 32 * 8, 16 * 8 + 4, world.Seed, Color.black));
            Assert.AreEqual(random, world.RngState); Assert.AreEqual(water, world.Cells[world.Index(32, 16)]);
            Assert.DoesNotThrow(() => RiverArt.SampleBank(world, 0, 0));
        }

        [UnityTest]
        public IEnumerator SurfaceFollowsSimulationClockStopsAtPauseAndDoesNotCoverBridgeDecks()
        {
            WorldState world = EmptyWorld();
            for (int y = 7; y < 25; y++) for (int x = 30; x <= 34; x++) Water(world, x, y);
            GameObject root = new GameObject("River surface fixture");
            Camera camera = new GameObject("River fixture camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform);
            camera.orthographic = true; camera.orthographicSize = 7; camera.aspect = 1.6f; camera.transform.position = new Vector3(32, 16, -10);
            Texture2D atlas = PixelArt.CreateAtlas(); Material material = new Material(Shader.Find("Sprites/Default")) { mainTexture = atlas };
            var batch = new QuadBatch("Current fixture", root.transform, material, 11);
            try
            {
                RiverArt.DrawSurface(world, camera, batch, true); Mesh mesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
                Assert.Greater(mesh.vertexCount, 0);
                Vector3[] paused = mesh.vertices; Color32[] pausedColors = mesh.colors32;
                uint random = world.RngState;
                yield return new WaitForSecondsRealtime(.1f);
                RiverArt.DrawSurface(world, camera, batch, true);
                CollectionAssert.AreEqual(paused, mesh.vertices); CollectionAssert.AreEqual(pausedColors, mesh.colors32);
                world.Tick += 3; RiverArt.DrawSurface(world, camera, batch, true);
                Assert.IsFalse(paused.SequenceEqual(mesh.vertices), "Advancing the simulation must move the production stream traces.");
                Assert.AreEqual(random, world.RngState);
                for (int i = 0; i < world.Infrastructure.Length; i++) if (RiverArt.IsWater(world.Cells[i])) world.Infrastructure[i].BridgeLevel = 1;
                RiverArt.DrawSurface(world, camera, batch, true); Assert.AreEqual(0, mesh.vertexCount, "Water traces must not paint over bridge decks.");
                for (int i = 0; i < world.Infrastructure.Length; i++) world.Infrastructure[i].BridgeLevel = 0;
                for (int i = 0; i < world.Cells.Length; i++) if (RiverArt.IsWater(world.Cells[i])) world.Cells[i].WaterKind = WaterKind.Lake;
                RiverArt.DrawSurface(world, camera, batch, true); Vector3[] calm = mesh.vertices; Assert.Greater(calm.Length, 0);
                world.Tick += 3; RiverArt.DrawSurface(world, camera, batch, true); Vector3[] later = mesh.vertices;
                Assert.IsFalse(calm.SequenceEqual(later), "Still-water ripples must expand with simulation time.");
                for (int i = 0; i < calm.Length; i += 8)
                {
                    Vector3 beforeCenter = (calm[i] + calm[i + 2] + calm[i + 4] + calm[i + 6]) * .25f;
                    Vector3 afterCenter = (later[i] + later[i + 2] + later[i + 4] + later[i + 6]) * .25f;
                    Assert.Less(Vector3.Distance(beforeCenter, afterCenter), .0001f, "Lake ripples must not travel in an invented downstream direction.");
                }
                RiverArt.DrawSurface(world, camera, batch, false); Assert.AreEqual(0, mesh.vertexCount, "Far-camera LOD must remove detailed surface meshes.");
                camera.transform.position = new Vector3(-100, -100, -10);
                RiverArt.DrawSurface(world, camera, batch, true); Assert.AreEqual(0, mesh.vertexCount, "Offscreen geometry must be culled.");
            }
            finally { batch.Dispose(); Object.Destroy(material); Object.Destroy(atlas); Object.Destroy(root); }
            yield return null; LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator NeighborEditRefreshesBankAcrossChunkSeamAndPreservesRoadAndBridge()
        {
            WorldState world = EmptyWorld(); Water(world, 31, 16);
            world.Infrastructure[world.Index(32, 16)].RoadLevel = 4;
            world.Infrastructure[world.Index(31, 16)].BridgeLevel = 1;
            GameObject root = new GameObject("River bank integration fixture"); var view = new WorldView(root.transform);
            try
            {
                view.SetWorld(world);
                MeshFilter groundLeft = root.GetComponentsInChildren<MeshFilter>().First(m => m.name == "Ground 0,0");
                MeshFilter groundRight = root.GetComponentsInChildren<MeshFilter>().First(m => m.name == "Ground 1,0");
                var left = (Texture2D)groundLeft.GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
                var right = (Texture2D)groundRight.GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
                Color before = left.GetPixel(31 * 8 + 7, 16 * 8 + 4);
                uint grain = PixelArt.Hash(32 * 8 + 4, 16 * 8 + 4, world.Seed);
                Assert.AreEqual((Color32)TransportArt.RoadColor(4, 4, 4, grain), (Color32)right.GetPixel(4, 16 * 8 + 4), "Road surface takes precedence over the shore recipe.");
                Mesh bridge = root.GetComponentsInChildren<MeshFilter>().First(m => m.name == "Transport deck 0,0").sharedMesh;
                Assert.AreEqual(4, bridge.vertexCount);
                Water(world, 32, 16); world.Infrastructure[world.Index(32, 16)].RoadLevel = 0;
                world.TerrainRevision++; view.Update(1);
                Assert.AreNotEqual(before, left.GetPixel(31 * 8 + 7, 16 * 8 + 4), "Editing the neighboring chunk must rebuild the old chunk's bank halo.");
                Assert.AreEqual(4, bridge.vertexCount); Assert.AreEqual(7123u, world.RngState);
                yield return null;
            }
            finally { view.Dispose(); Object.Destroy(root); }
            yield return null; LogAssert.NoUnexpectedReceived();
        }
    }
}
