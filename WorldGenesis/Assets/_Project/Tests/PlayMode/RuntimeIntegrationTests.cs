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

        [UnityTest]
        public IEnumerator ImportedCitizensRenderAndTheirMeshFreezesOnPause()
        {
            MeshFilter humanMesh = null;
            foreach (MeshFilter candidate in game.GetComponentsInChildren<MeshFilter>())
                if (candidate.name == "Animated human citizens") humanMesh = candidate;
            Assert.NotNull(humanMesh, "Imported characters must render in the production scene.");
            Assert.Greater(humanMesh.sharedMesh.vertexCount, 0);
            foreach (UnitData resident in game.World.Units)
            {
                Assert.AreNotEqual(UnitProfession.Guard, resident.Profession, "Founding a settlement cannot recruit soldiers or grant equipment.");
                Assert.IsFalse(CitizenSprites.ResolvePose(game.World, resident, Vector2.right).Warrior, "Starting residents must not appear in armour.");
            }
            Texture texture = humanMesh.GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
            Assert.Greater(texture.width, 256, "A real multi-frame citizen atlas must be present.");
            Assert.AreEqual(FilterMode.Point, texture.filterMode);
            Vector2[] uv = humanMesh.sharedMesh.uv;
            Vector3[] vertices = humanMesh.sharedMesh.vertices;
            yield return new WaitForSecondsRealtime(.4f);
            CollectionAssert.AreEqual(uv, humanMesh.sharedMesh.uv, "Pause must freeze animation frames.");
            CollectionAssert.AreEqual(vertices, humanMesh.sharedMesh.vertices, "Pause must freeze citizen placement.");
            Sprite portrait = game.View.CreateHumanIcon();
            Assert.AreSame(texture, portrait.texture);
            Object.Destroy(portrait);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CitizenFramesDistinguishTravelWorkCargoAndActualHits()
        {
            WorldState world = WorldGenerator.Generate(31, 16, 16);
            ResourceCatalog.Generate(world);
            UnitData unit = new UnitData { Id = 4, Age = 25, Profession = UnitProfession.Forester, Task = UnitTask.Gather, TargetCell = 100, ResourceCell = 100, WorkTimer = 1 };
            world.Cells[100].Vegetation = 1; world.Cells[100].Fire = 0;
            world.ResourceNodes[100] = new ResourceNodeData { Type = ResourceType.Tree, Amount = 3, Capacity = 3 };
            unit.Path.Add(100);
            Assert.AreEqual(CitizenSprites.CitizenAction.Walk, CitizenSprites.ResolvePose(world, unit, Vector2.right).Action, "Approaching a tree is walking.");
            unit.PathCursor = 1;
            Assert.AreEqual(CitizenSprites.CitizenAction.Chop, CitizenSprites.ResolvePose(world, unit, Vector2.right).Action, "Working at the tree uses the derived chop gesture.");
            unit.Task = UnitTask.Return; unit.CarriedWood = 2; unit.PathCursor = 0;
            Assert.AreEqual(CitizenSprites.CitizenAction.Carry, CitizenSprites.ResolvePose(world, unit, Vector2.left).Action, "Loaded travel is identified by real carried goods.");
            Assert.AreEqual(CitizenSprites.CitizenDirection.West, CitizenSprites.ResolvePose(world, unit, Vector2.left).Direction);
            Assert.IsFalse(CitizenSprites.ResolvePose(world, unit, Vector2.left).FlipX, "The authored west view must not be a mirrored east view.");
            unit.Path.Clear(); unit.Task = UnitTask.Fight; unit.AttackTimer = .8f;
            CitizenSprites.Pose militiaHit = CitizenSprites.ResolvePose(world, unit, Vector2.up);
            Assert.IsFalse(militiaHit.Warrior, "Self-defence cannot equip armour.");
            Assert.AreEqual(0, militiaHit.Row, "Militia cannot acquire an authored weapon from a work/cargo clip.");
            Assert.Greater(militiaHit.StrikeOffset.y, .1f, "An actual civilian hit must show a short lunge toward its target.");
            Assert.Greater(CitizenSprites.ResolvePose(world, unit, Vector2.zero).StrikeOffset.magnitude, .1f, "Overlapping fighters still need visible hit feedback.");
            Assert.AreEqual(UnitProfession.Forester, unit.Profession);
            unit.AttackTimer = .4f;
            Assert.AreEqual(Vector2.zero, CitizenSprites.ResolvePose(world, unit, Vector2.up).StrikeOffset, "Recovery cannot repeat the hit.");
            // A legacy/synthetic profession cannot grant a sword, armour or male body.
            unit.Profession = UnitProfession.Guard; unit.Task = UnitTask.Fight; unit.AttackTimer = .8f;
            CitizenSprites.Pose hit = CitizenSprites.ResolvePose(world, unit, Vector2.up);
            Assert.AreEqual(CitizenSprites.CitizenAction.Attack, hit.Action);
            Assert.IsFalse(hit.Warrior); Assert.IsTrue(hit.DerivedGesture);
            Assert.AreEqual(CitizenSprites.AppearanceFor(unit), hit.Appearance);
            unit.Path.Clear(); unit.AttackTimer = .4f;
            Assert.AreEqual(0, CitizenSprites.ResolvePose(world, unit, Vector2.up).Row, "Cooldown cannot invent another attack.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator OriginalAtlasCoversFourPeoplesAndFourDirectionsInProductionBatch()
        {
            GameObject holder = new GameObject("Original citizens coverage fixture");
            CitizenSprites sprites = new CitizenSprites(holder.transform);
            uint randomBefore = game.World.RngState; long tickBefore = game.World.Tick;
            try
            {
                var regions = new System.Collections.Generic.HashSet<Rect>();
                for (int race = 0; race < 4; race++) for (int appearance = 0; appearance < 4; appearance++)
                for (int direction = 0; direction < 4; direction++) for (int frame = 0; frame < 4; frame++)
                {
                    Sprite sprite = sprites.CreateCitizenSprite((SpeciesType)race, (CitizenSprites.CitizenAppearance)appearance, (CitizenSprites.CitizenDirection)direction, frame);
                    Assert.AreEqual(32, sprite.rect.width); Assert.AreEqual(32, sprite.rect.height);
                    Assert.IsTrue(regions.Add(sprite.rect), "Every authored body/demographic/direction/frame must retain its own region.");
                    Object.Destroy(sprite);
                }
                UnitData[] fixture = new UnitData[4];
                sprites.Begin(game.World);
                for (int race = 0; race < 4; race++)
                {
                    UnitData unit = new UnitData { Id = 70000 + race, Species = (SpeciesType)race, Age = 25, Sex = Sex.Female, X = 8.5f, Y = 8.5f, Task = UnitTask.Wander };
                    unit.Path.Add(game.World.Index(9, 8));
                    fixture[race] = unit;
                    sprites.Add(game.World, unit, 0, Color.white);
                }
                sprites.Submit();
                MeshFilter mesh = holder.GetComponentInChildren<MeshFilter>();
                Assert.AreEqual(16, mesh.sharedMesh.vertexCount, "All four civilized peoples must use the animated production batch.");
                Vector2[] uv = mesh.sharedMesh.uv; Vector3[] vertices = mesh.sharedMesh.vertices;
                yield return new WaitForSecondsRealtime(.2f);
                sprites.Begin(game.World);
                foreach (UnitData unit in fixture) sprites.Add(game.World, unit, 0, Color.white);
                sprites.Submit();
                CollectionAssert.AreEqual(uv, mesh.sharedMesh.uv); CollectionAssert.AreEqual(vertices, mesh.sharedMesh.vertices);
                Assert.AreEqual(randomBefore, game.World.RngState); Assert.AreEqual(tickBefore, game.World.Tick);
            }
            finally { sprites.Dispose(); Object.Destroy(holder); }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ResourceInspectorReflectsCargoAndRegrowthWithoutCreatingStock()
        {
            UnitData unit = game.World.Units.Find(u => u.Species == SpeciesType.Human);
            Assert.NotNull(unit);
            float oldStock = game.World.GetSettlementStock(unit.SettlementId, ItemType.Log);
            unit.CargoKind = ItemType.Log; unit.CargoAmount = 1;
            unit.Task = UnitTask.Return;
            game.InspectUnit(unit.Id);
            yield return null;
            string displayed = "";
            foreach (Text label in game.GetComponentsInChildren<Text>()) displayed += label.text + "\n";
            StringAssert.Contains("Брёвна  1", displayed, "Production inspector must display the actual carrier's load.");
            Assert.AreEqual(oldStock, game.World.GetSettlementStock(unit.SettlementId, ItemType.Log), "Showing carried material must not credit the warehouse.");
            SettlementData settlement = game.World.GetSettlement(unit.SettlementId);
            game.InspectSite(settlement.X, settlement.Y);
            yield return null;
            Canvas.ForceUpdateCanvases();
            ScrollRect inspector = game.GetComponentInChildren<ScrollRect>();
            Assert.NotNull(inspector);
            Assert.Greater(inspector.content.rect.height, inspector.viewport.rect.height, "Long settlement stocks must be scrollable rather than clipped permanently.");
            float top = inspector.verticalNormalizedPosition;
            inspector.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -3) });
            Assert.Less(inspector.verticalNormalizedPosition, top);
            Vector2 inside = RectTransformUtility.WorldToScreenPoint(null, inspector.viewport.TransformPoint(inspector.viewport.rect.center));
            Assert.IsTrue(game.IsScreenPointOverUI(inside), "Scrolling information must not edit terrain or zoom through the panel.");
            Vector2 dragged = WorldView.CargoPosition(unit, Vector2.right);
            Assert.Less(dragged.x, unit.X, "A log trails its carrier's movement.");
            int cell = game.World.Index(1, 1);
            game.World.Cells[cell].Terrain = TerrainType.Forest;
            game.World.Cells[cell].Fire = 0; game.World.Cells[cell].BuildingId = 0;
            game.World.ResourceNodes[cell] = new ResourceNodeData { Type = ResourceType.Tree, Capacity = 3, Amount = 0, RegrowTicksRemaining = 900 };
            StringAssert.Contains("Пополнение через 90 с", GameHud.NodeDescription(game.World, 1, 1));
            game.Engine.SetLaw(WorldLaw.VegetationGrowth, false);
            StringAssert.Contains("Рост растений выключен", GameHud.NodeDescription(game.World, 1, 1));
            StringAssert.DoesNotContain("Пополнение через", GameHud.NodeDescription(game.World, 1, 1), "A paused regrowth clock must not promise an active countdown.");
            Assert.AreNotEqual(ResourceArt.NodeSprite(ResourceType.Tree, 3, 3), ResourceArt.NodeSprite(ResourceType.Tree, 0, 3), "An exhausted tree must be visibly different.");
            Assert.AreNotEqual(ResourceArt.NodeSprite(ResourceType.IronOre, 8, 8), ResourceArt.NodeSprite(ResourceType.IronOre, 0, 8), "Exhausted ore cannot keep a full-pile visual.");
            Assert.AreEqual(ResourceArt.Hearth, WorldView.ResolveBuildingSprite(BuildingType.Hall));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DemographyAndRulerPolicyAreVisibleInTheProductionInspector()
        {
            // Explicit UI fixture: it checks presentation of model values, not natural births.
            UnitData unit = game.World.Units.Find(u => u.Species == SpeciesType.Human && game.World.GetSettlement(u.SettlementId).RulerId != u.Id);
            Assert.NotNull(unit);
            unit.Age = 25; unit.Sex = Sex.Female; unit.Profession = UnitProfession.Farmer;
            game.InspectUnit(unit.Id);
            yield return null;
            StringAssert.Contains("Женщина", VisibleText());
            Assert.AreEqual(CitizenSprites.CitizenAppearance.AdultFemale, CitizenSprites.ResolvePose(game.World, unit, Vector2.right).Appearance);
            SettlementData home = game.World.GetSettlement(unit.SettlementId);
            game.Engine.SetEconomicPolicy(home.Id, EconomicPolicy.Market, .2f, ChildLaborPolicy.LightWork, ResourceFocus.Masonry);
            unit.Age = 13; unit.Task = UnitTask.Play; unit.Profession = UnitProfession.None;
            unit.Path.Clear(); unit.PathCursor = 0;
            game.InspectUnit(unit.Id);
            yield return null;
            StringAssert.Contains("Девочка · подросток 12–15", VisibleText());
            StringAssert.Contains("Лёгкая работа 12–15: разрешена", VisibleText());
            Assert.AreEqual(CitizenSprites.CitizenAppearance.Girl, CitizenSprites.ResolvePose(game.World, unit, Vector2.right).Appearance);
            game.Engine.SetEconomicPolicy(home.Id, EconomicPolicy.Market, .2f, ChildLaborPolicy.Forbidden, ResourceFocus.Masonry);
            game.InspectUnit(unit.Id);
            yield return null;
            StringAssert.Contains("Детский труд запрещён", VisibleText());
            game.InspectSite(home.X, home.Y);
            yield return null;
            StringAssert.Contains("Платный обмен (бартер)", VisibleText());
            StringAssert.Contains("Натуральный налог", VisibleText());
            Assert.AreEqual(.2f, home.TaxRate);
            Assert.IsTrue(game.Paused, "Inspector refresh must not advance the world.");
            LogAssert.NoUnexpectedReceived();
        }

        private string VisibleText()
        {
            string text = "";
            foreach (Text label in game.GetComponentsInChildren<Text>()) text += label.text + "\n";
            return text;
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
