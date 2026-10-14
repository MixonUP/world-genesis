using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WorldGenesis.Runtime;
using WorldGenesis.Simulation;

namespace WorldGenesis.Tests
{
    public sealed class RemainsPresentationTests
    {
        [Test]
        public void SkeletonAtlasHasDistinctHumanAnimalAndBirdBones()
        {
            Texture2D atlas = RemainsArt.CreateAtlas(true);
            try
            {
                var hashes = new HashSet<string>(); Color32[] pixels = atlas.GetPixels32();
                for (int kind = 0; kind < 6; kind++)
                {
                    int glyph = kind * 4 + 2; var frame = new List<Color32>();
                    for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) frame.Add(pixels[(glyph / 8 * 32 + y) * RemainsArt.Width + glyph % 8 * 32 + x]);
                    Assert.Greater(frame.Count(p => p.a > 250), 65, "Empty skeletal recipe " + kind);
                    Assert.IsTrue(hashes.Add(string.Join("|", frame.Select(p => p.r + "," + p.g + "," + p.b + "," + p.a))), "Different skeletal families share identical pixels");
                }
                RemainsData child = new RemainsData { Species = SpeciesType.Human, Child = true }, adult = new RemainsData { Species = SpeciesType.Human };
                Assert.Less(RemainsArt.Size(child).x, RemainsArt.Size(adult).x * .7f);
                Assert.AreEqual(RemainsArt.Shape.Bird, RemainsArt.ShapeFor(SpeciesType.Chicken));
            }
            finally { Object.DestroyImmediate(atlas); }
        }

        [UnityTest]
        public IEnumerator NativeBonesFadeOnSavedSimulationClockAndNeverCoverLivingBodiesOrHouses()
        {
            var world = new WorldState { Width = 16, Height = 16, Seed = 100014, Tick = 120, Cells = new TileData[256], Infrastructure = new InfrastructureCell[256], ResourceNodes = new ResourceNodeData[256] };
            for (int i = 0; i < world.Cells.Length; i++) world.Cells[i].Terrain = TerrainType.Grass;
            var unit = new UnitData { Id = 2, Species = SpeciesType.Human, Sex = Sex.Male, Age = 25, X = 7.5f, Y = 7.5f, Health = 100, MaxHealth = 100 };
            world.Units.Add(unit);
            var remains = new RemainsData { SourceUnitId = 1, Species = SpeciesType.Human, X = 7.5f, Y = 7.65f, Direction = 2, DeathTick = 120, RemainingSeconds = 150 };
            world.Remains.Add(remains);
            var farm = new BuildingData { Id = 3, Type = BuildingType.Farm, X = 7, Y = 7, CropGrowth = 1, FarmCrop = CropType.Wheat }; world.Buildings.Add(farm); world.Cells[119].BuildingId = farm.Id;
            GameObject root = new GameObject("Native remains proof"), cameraObject = new GameObject("Remains proof camera");
            var view = new WorldView(root.transform); Camera camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 2.1f;
            camera.transform.position = new Vector3(7.5f, 8, -10); camera.nearClipPlane = .1f; camera.farClipPlane = 100; camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
            try
            {
                view.SetWorld(world); foreach (Transform child in root.GetComponentsInChildren<Transform>()) child.gameObject.layer = 30;
                MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(); Mesh bonesMesh = root.GetComponentsInChildren<MeshFilter>().Single(m => m.name == "Temporary ground remains").sharedMesh;
                Color32[] bones = Capture(camera, target, renderers, "Temporary ground remains"); Assert.Greater(bones.Count(p => p.a > 250), 80);
                foreach (string other in new[] { "Animated human citizens", "Fields and rural buildings" })
                {
                    Color32[] alone = Capture(camera, target, renderers, other), together = Capture(camera, target, renderers, other, "Temporary ground remains"); int overlap = 0;
                    for (int i = 0; i < bones.Length; i++) if (bones[i].a > 250 && alone[i].a > 250 && Difference(bones[i], alone[i]) > 30)
                    { Assert.LessOrEqual(Difference(together[i], other == "Animated human citizens" ? alone[i] : bones[i]), 6, "Wrong actual opaque overlap: " + other); overlap++; }
                    Assert.Greater(overlap, 4, "Native proof did not overlap actual bone and " + other + " pixels");
                }
                farm.Type = BuildingType.House; view.Update(1);
                Color32[] house = Capture(camera, target, renderers, "Buildings"), composite = Capture(camera, target, renderers, "Buildings", "Temporary ground remains"); int covered = 0;
                for (int i = 0; i < bones.Length; i++) if (bones[i].a > 250 && house[i].a > 250 && Difference(bones[i], house[i]) > 30)
                { Assert.LessOrEqual(Difference(house[i], composite[i]), 6); covered++; }
                Assert.Greater(covered, 15, "House must actually occlude the skeleton");
                world.Buildings.Clear(); world.Cells[119].BuildingId = 0; world.Units.Clear(); remains.Species = SpeciesType.Bear;
                var drop = new AgriculturalDrop { X = remains.X, Y = remains.Y, Product = AgriculturalProduct.Meat, Amount = 3 }; world.AgriculturalDrops.Add(drop);
                string beforeLootRender = JsonUtility.ToJson(world); view.Update(1.5f);
                Assert.AreEqual(beforeLootRender, JsonUtility.ToJson(world), "Presentation offset changed stock coordinates or quantities");
                Mesh lootMesh = root.GetComponentsInChildren<MeshFilter>().Single(m => m.name == "Rural buildings and loose produce").sharedMesh;
                Assert.Greater(lootMesh.bounds.min.x, bonesMesh.bounds.max.x, "Actual loot icon still overlaps the skeleton footprint");
                bones = Capture(camera, target, renderers, "Temporary ground remains"); Color32[] bonesWithLoot = Capture(camera, target, renderers, "Temporary ground remains", "Rural buildings and loose produce");
                int exposed = 0;
                for (int i = 0; i < bones.Length; i++) if (bones[i].a > 250) { Assert.LessOrEqual(Difference(bones[i], bonesWithLoot[i]), 6, "Real loot pixels cover bones"); exposed++; }
                Assert.Greater(exposed, 80);
                remains.RemainingSeconds = 15; view.Update(2); Color32[] half = Capture(camera, target, renderers, "Temporary ground remains");
                Assert.That(half.Max(p => (int)p.a), Is.InRange(125, 130), "Half opacity must reach the native render target");
                string before = JsonUtility.ToJson(world); Color32[] frozen = bonesMesh.colors32; yield return null; view.Update(500);
                Assert.AreEqual(before, JsonUtility.ToJson(world)); CollectionAssert.AreEqual(frozen, bonesMesh.colors32, "Wall clock cannot advance corpse fade while paused");
                world.Remains.Clear(); view.Update(501); Assert.AreEqual(0, bonesMesh.vertexCount);
                Assert.AreEqual(drop.X, lootMesh.bounds.center.x, .0001f, "Expired remains left a permanent offset on a real resource");
                Assert.IsTrue(Capture(camera, target, renderers, "Temporary ground remains").All(p => p.a == 0), "Expired remains still render pixels");
            }
            finally { view.Dispose(); camera.targetTexture = null; target.Release(); Object.Destroy(target); Object.Destroy(cameraObject); Object.Destroy(root); }
        }
        private static int Difference(Color32 a, Color32 b) { return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b); }
        private static Color32[] Capture(Camera camera, RenderTexture target, MeshRenderer[] renderers, params string[] visible)
        {
            foreach (MeshRenderer renderer in renderers) renderer.enabled = visible.Contains(renderer.name);
            camera.Render(); RenderTexture prior = RenderTexture.active; RenderTexture.active = target; var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try { image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); return image.GetPixels32(); }
            finally { RenderTexture.active = prior; Object.Destroy(image); }
        }
    }
}
