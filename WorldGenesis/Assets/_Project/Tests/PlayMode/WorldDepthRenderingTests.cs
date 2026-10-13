using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WorldGenesis.Runtime;
using WorldGenesis.Simulation;

namespace WorldGenesis.Tests
{
    public sealed class WorldDepthRenderingTests
    {
        [UnityTest]
        public IEnumerator NativeOpaqueHousePixelsOccludeNorthernCitizenAndCargoButSouthernCitizenIsInFront()
        {
            GameObject parent = new GameObject("Native foot-depth proof");
            GameObject cameraObject = new GameObject("Depth proof camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 2.1f;
            camera.transform.position = new Vector3(7.5f, 8.0f, -10); camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear; camera.cullingMask = 1 << 30;
            RenderTexture target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            target.Create(); camera.targetTexture = target;
            WorldState world = Fixture(out UnitData citizen, out BuildingData house);
            WorldView view = new WorldView(parent.transform);
            try
            {
                view.SetWorld(world);
                foreach (Transform transform in parent.GetComponentsInChildren<Transform>()) transform.gameObject.layer = 30;
                MeshRenderer[] renderers = parent.GetComponentsInChildren<MeshRenderer>();
                Assert.IsTrue(renderers.Any(r => r.sharedMaterial.shader.name == "World Genesis/Depth Sprite"));
                for (int position = 0; position < 2; position++)
                {
                    citizen.Y = position == 0 ? 8.0f : 6.9f;
                    world.Tick++; world.Revision++; view.Update(position + 1);
                    Color32[] building = Capture(camera, target, renderers, true, false);
                    Color32[] resident = Capture(camera, target, renderers, false, true);
                    Color32[] parcel = Capture(camera, target, renderers, false, true, true);
                    Color32[] combined = Capture(camera, target, renderers, true, true);
                    int checkedPixels = 0, overlappingParcel = 0;
                    for (int i = 0; i < combined.Length; i++)
                    {
                        if (building[i].a < 250 || resident[i].a < 250 || Difference(building[i], resident[i]) < 35) continue;
                        Color32 expected = position == 0 ? building[i] : resident[i];
                        Assert.LessOrEqual(Difference(expected, combined[i]), 6, "Opaque overlap at pixel " + i + " must follow the foot Y, including the citizen's parcel.");
                        if (parcel[i].a >= 250) overlappingParcel++;
                        checkedPixels++;
                    }
                    Assert.Greater(checkedPixels, 25, "The native images must really overlap; a geometry-only or off-screen pass is insufficient.");
                    Assert.Greater(overlappingParcel, 5, "This proof must exercise a real cargo silhouette at the facade as well as the body.");
                }

                citizen.InsideBuildingId = house.Id; citizen.X = house.X + .5f; citizen.Y = house.Y + .5f;
                world.Tick++; world.Revision++; view.Update(3);
                Mesh body = parent.GetComponentsInChildren<MeshFilter>().First(m => m.name == "Animated human citizens").sharedMesh;
                Mesh cargo = parent.GetComponentsInChildren<MeshFilter>().First(m => m.name == "Manufactured goods").sharedMesh;
                Assert.AreEqual(0, body.vertexCount); Assert.AreEqual(0, cargo.vertexCount, "An occupant's item must not float over the roof after the body is hidden.");
                Assert.AreEqual(new Vector2(house.X + .5f, house.Y + .5f), view.GetPresentationPosition(citizen));
                Color32[] occupied = Capture(camera, target, renderers, true, true);
                Color32[] emptyFacade = Capture(camera, target, renderers, true, false);
                CollectionAssert.AreEqual(emptyFacade, occupied);
                Assert.AreEqual(ItemType.LinenClothes, citizen.CargoKind); Assert.AreEqual(1, citizen.CargoAmount);

                // An actual indoor production state remains legible through the workshop's own
                // activity, while its worker and possessions are still hidden behind the facade.
                world.BuildingAccessVersion = 1;
                house.Type = BuildingType.Smithy; house.SettlementId = 1; house.EntranceCell = world.Index(7, 6);
                house.ActiveIndustryRecipe = IndustryRecipeType.IronTools; house.IndustryWorkerId = citizen.Id;
                house.Inventory[(int)ItemType.IronIngot] = 1; house.Inventory[(int)ItemType.Plank] = 1; house.Inventory[(int)ItemType.Charcoal] = 1;
                citizen.SettlementId = 1; citizen.TargetBuildingId = house.Id; citizen.Task = UnitTask.Produce;
                citizen.CargoKind = ItemType.None; citizen.CargoAmount = 0;
                SettlementData settlement = new SettlementData { Id = 1, Population = 1 };
                settlement.UnlockedTechnologies.Add("z02_ironworking"); world.Settlements.Add(settlement);
                world.Tick++; world.Revision++; view.Update(4);
                Assert.IsTrue(IndustryCatalog.IsWorking(world, house)); Assert.AreEqual(0, body.vertexCount);
                Mesh effects = parent.GetComponentsInChildren<MeshFilter>().First(m => m.name == "Active workshop effects").sharedMesh;
                Assert.Greater(effects.vertexCount, 0);
                Color32[] activeWorkshop = Capture(camera, target, renderers, true, true);
                house.IndustryWorkerId = 0; citizen.Task = UnitTask.Idle;
                world.Tick++; world.Revision++; view.Update(5);
                Assert.AreEqual(0, effects.vertexCount);
                Color32[] coldWorkshop = Capture(camera, target, renderers, true, true);
                Assert.Greater(activeWorkshop.Where((pixel, i) => Difference(pixel, coldWorkshop[i]) > 15).Count(), 10, "The real active workshop effect must survive its own facade's depth buffer.");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                view.Dispose(); camera.targetTexture = null; target.Release();
                Object.Destroy(target); Object.Destroy(parent); Object.Destroy(cameraObject);
            }
            yield return null;
        }

        private static Color32[] Capture(Camera camera, RenderTexture target, MeshRenderer[] renderers, bool building, bool citizen, bool parcelOnly = false)
        {
            foreach (MeshRenderer renderer in renderers)
                renderer.enabled = building && (renderer.name == "Buildings" || renderer.name == "Production workshops" || renderer.name == "Active workshop effects") || citizen && ((!parcelOnly && renderer.name == "Animated human citizens") || renderer.name == "Manufactured goods");
            camera.Render();
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            Texture2D frame = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try { frame.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); frame.Apply(); return frame.GetPixels32(); }
            finally { RenderTexture.active = previous; Object.Destroy(frame); }
        }
        private static int Difference(Color32 a, Color32 b) { return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b); }
        private static WorldState Fixture(out UnitData unit, out BuildingData building)
        {
            WorldState world = new WorldState { Width = 16, Height = 16, Tick = 100, Seed = 3, Cells = new TileData[256], Infrastructure = new InfrastructureCell[256], ResourceNodes = new ResourceNodeData[256] };
            for (int i = 0; i < world.Cells.Length; i++) world.Cells[i].Terrain = TerrainType.Grass;
            building = new BuildingData { Id = 3, X = 7, Y = 7, Type = BuildingType.House, Level = 1 };
            unit = new UnitData { Id = 2, X = 7.5f, Y = 8, Age = 25, Species = SpeciesType.Human, Sex = Sex.Male, Health = 100, MaxHealth = 100, Task = UnitTask.Idle, CargoKind = ItemType.LinenClothes, CargoAmount = 1 };
            world.Cells[world.Index(7, 7)].BuildingId = building.Id;
            world.Buildings.Add(building); world.Units.Add(unit); return world;
        }
    }
}
