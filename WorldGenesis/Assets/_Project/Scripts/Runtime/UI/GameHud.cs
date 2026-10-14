using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    public enum ToolKind { Inspect, Terrain, Creature, Power, Law }
    public sealed class WorldTool
    {
        public string Label, Description;
        public ToolKind Kind;
        public int Value;
        public Color Color;
        public WorldTool(string label, string description, ToolKind kind, int value, Color color) { Label = label; Description = description; Kind = kind; Value = value; Color = color; }
    }

    public sealed partial class GameHud : IDisposable
    {
        private readonly GameBootstrap game;
        private readonly GameObject root;
        private readonly Font font;
        private readonly Color panel = PixelArt.Hex("152632"), surface = PixelArt.Hex("223745"), muted = PixelArt.Hex("92abb5"), white = PixelArt.Hex("edf0df");
        private Text stats, inspectorTitle, inspectorBody, chronicle, status, toolHeading, radiusLabel, pauseLabel, clockLabel, weatherLabel;
        private InputField seed;
        private RawImage minimap;
        private RectTransform toolRow;
        private readonly List<Button> toolButtons = new List<Button>();
        private readonly List<WorldTool> currentTools = new List<WorldTool>();
        private readonly List<Button> categoryButtons = new List<Button>();
        private readonly List<Button> speedButtons = new List<Button>();
        private readonly Dictionary<int, Sprite> iconSprites = new Dictionary<int, Sprite>();
        private readonly StringBuilder builder = new StringBuilder(512);
        private float toastUntil, nextRefresh;
        private string toast = "";
        private int selectedCategory;
        private int creaturePage;
        private int terrainPage;
        private Button creaturePageButton;
        private Text presetLabel;
        private ScrollRect inspectorScroll;
        private int previousInspectedUnit = -1, previousInspectedCell = -2;
        public bool IsTextFocused => seed != null && seed.isFocused || maritimeQuantity != null && maritimeQuantity.isFocused;
        public string InspectorText => inspectorBody != null ? inspectorBody.text : "";

        public GameHud(GameBootstrap owner)
        {
            game = owner; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            root = new GameObject("HUD • World Genesis", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(game.transform, false);
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
            RectTransform header = Panel(root.transform, "Header", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -76), Vector2.zero, panel);
            Text title = Label(header, "WORLD GENESIS", 27, PixelArt.Gold, new Vector2(24, -13), new Vector2(330, 36)); title.fontStyle = FontStyle.Bold;
            Label(header, "СОЗДАВАЙ МИРЫ. НАБЛЮДАЙ ИСТОРИИ.", 10, muted, new Vector2(26, -48), new Vector2(350, 17));
            Label(header, "SEED", 10, muted, new Vector2(389, -14), new Vector2(60, 17));
            seed = Input(header, new Vector2(389, -34), new Vector2(112, 29), game.World.Seed.ToString());
            ButtonAt(header, "Новый мир", new Vector2(513, -24), new Vector2(120, 39), () => { if (int.TryParse(seed.text, out int value)) game.NewWorld(value); else ShowToast("Seed — целое число от −2147483648 до 2147483647"); });
            Button pause = ButtonAt(header, "II", new Vector2(660, -24), new Vector2(46, 39), () => game.Paused = !game.Paused); pauseLabel = pause.GetComponentInChildren<Text>();
            int[] speeds = SimulationPacer.Speeds;
            for (int i = 0; i < speeds.Length; i++) { float speed = speeds[i]; speedButtons.Add(ButtonAt(header, speed + "×", new Vector2(713 + i * 49, -24), new Vector2(45, 39), () => { game.Speed = speed; game.Paused = false; })); }
            clockLabel = Label(header, "", 12, PixelArt.Teal, new Vector2(1064, -19), new Vector2(275, 48));
            ButtonAt(header, "Сохранить", new Vector2(-248, -24), new Vector2(104, 39), game.Save, true);
            ButtonAt(header, "Загрузить", new Vector2(-134, -24), new Vector2(108, 39), game.Load, true);

            RectTransform left = PanelFixed(root.transform, "World information", new Vector2(18, -95), new Vector2(232, 470), panel);
            Label(left, "АТЛАС МИРА", 12, PixelArt.Gold, new Vector2(17, -16), new Vector2(196, 24)).fontStyle = FontStyle.Bold;
            stats = Label(left, "", 13, white, new Vector2(17, -47), new Vector2(203, 88));
            minimap = new GameObject("Minimap • actual terrain", typeof(RectTransform), typeof(RawImage), typeof(MinimapInput)).GetComponent<RawImage>(); minimap.transform.SetParent(left, false);
            Position(minimap.rectTransform, new Vector2(17, -142), new Vector2(198, 198)); minimap.texture = game.View.Minimap;
            MinimapInput minimapInput = minimap.GetComponent<MinimapInput>(); minimapInput.Game = game;
            Label(left, "Нажмите на карту, чтобы переместиться", 10, muted, new Vector2(17, -347), new Vector2(205, 26));
            ButtonAt(left, "Границы поселений", new Vector2(17, -383), new Vector2(198, 29), () => { game.View.ShowBorders = !game.View.ShowBorders; game.View.RefreshTerritories(); ShowToast(game.View.ShowBorders ? "Границы видны" : "Границы скрыты"); });
            presetLabel = ButtonAt(left, "", new Vector2(17, -427), new Vector2(198, 29), () => { game.GenerationPreset = (WorldPreset)(((int)game.GenerationPreset + 1) % 3); RefreshPreset(); ShowToast("Выбрана форма следующего мира. Нажмите «Новый мир»."); }).GetComponentInChildren<Text>();
            RefreshPreset();
            RectTransform weatherPanel = PanelFixed(root.transform, "Weather information", new Vector2(18, -580), new Vector2(232, 104), panel);
            weatherLabel = Label(weatherPanel, "", 12, white, new Vector2(17, -10), new Vector2(203, 86));

            RectTransform right = PanelFixed(root.transform, "Inspector", new Vector2(-18, -95), new Vector2(236, 366), panel, true);
            Label(right, "НАБЛЮДЕНИЕ", 12, PixelArt.Gold, new Vector2(17, -16), new Vector2(202, 24)).fontStyle = FontStyle.Bold;
            inspectorTitle = Label(right, "Живой мир", 21, white, new Vector2(17, -49), new Vector2(202, 48));
            RectTransform inspectorViewport = PanelFixed(right, "Inspector scroll viewport", new Vector2(17, -101), new Vector2(202, 239), Color.clear);
            inspectorViewport.gameObject.AddComponent<RectMask2D>();
            inspectorBody = Label(inspectorViewport, "", 13, muted, Vector2.zero, new Vector2(195, 239));
            inspectorBody.rectTransform.anchorMin = new Vector2(0, 1); inspectorBody.rectTransform.anchorMax = new Vector2(1, 1);
            inspectorBody.rectTransform.offsetMin = new Vector2(0, -239); inspectorBody.rectTransform.offsetMax = new Vector2(-5, 0);
            inspectorBody.verticalOverflow = VerticalWrapMode.Overflow;
            inspectorBody.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            inspectorScroll = inspectorViewport.gameObject.AddComponent<ScrollRect>(); inspectorScroll.viewport = inspectorViewport; inspectorScroll.content = inspectorBody.rectTransform;
            inspectorScroll.horizontal = false; inspectorScroll.vertical = true; inspectorScroll.movementType = ScrollRect.MovementType.Clamped; inspectorScroll.inertia = false; inspectorScroll.scrollSensitivity = 24;
            inspectorHint = Label(right, "Колесо над панелью — подробности", 10, muted, new Vector2(17, -348), new Vector2(204, 16));
            CreateBuildingAccessControls(right);
            CreateMaritimeControls(); CreateExpansionControls();
            RectTransform history = PanelFixed(root.transform, "Chronicle", new Vector2(-18, -478), new Vector2(236, 192), panel, true);
            Label(history, "ЛЕТОПИСЬ", 12, PixelArt.Gold, new Vector2(17, -16), new Vector2(202, 24)).fontStyle = FontStyle.Bold;
            chronicle = Label(history, "", 12, muted, new Vector2(17, -51), new Vector2(202, 127));

            RectTransform footer = Panel(root.transform, "Creation tools", Vector2.zero, new Vector2(1, 0), new Vector2(18, 18), new Vector2(-18, 174), panel);
            string[] categories = { "ЛАНДШАФТ", "ЖИЗНЬ", "СИЛЫ БОГА", "ЗАКОНЫ", "ПОГОДА" };
            for (int i = 0; i < categories.Length; i++) { int category = i; categoryButtons.Add(ButtonAt(footer, categories[i], new Vector2(14 + i * 112, -12), new Vector2(104, 33), () => SelectCategory(category))); }
            ButtonAt(footer, "Наблюдать  [I]", new Vector2(578, -12), new Vector2(142, 33), () => SelectInspect());
            toolHeading = Label(footer, "", 12, muted, new Vector2(737, -14), new Vector2(460, 27));
            creaturePageButton = ButtonAt(footer, "Страница  ›", new Vector2(1095, -12), new Vector2(156, 33), () => { if (selectedCategory == 0) terrainPage = (terrainPage + 1) % 2; else creaturePage = (creaturePage + 1) % 2; SelectCategory(selectedCategory); });
            radiusLabel = Label(footer, "", 12, white, new Vector2(-253, -17), new Vector2(161, 25), true);
            ButtonAt(footer, "−", new Vector2(-82, -12), new Vector2(28, 31), () => game.BrushRadius = Mathf.Max(0, game.BrushRadius - 1), true);
            ButtonAt(footer, "+", new Vector2(-45, -12), new Vector2(28, 31), () => game.BrushRadius = Mathf.Min(12, game.BrushRadius + 1), true);
            toolRow = new GameObject("Tool palette", typeof(RectTransform)).GetComponent<RectTransform>(); toolRow.SetParent(footer, false);
            toolRow.anchorMin = new Vector2(0, 0); toolRow.anchorMax = new Vector2(1, 1); toolRow.offsetMin = new Vector2(14, 12); toolRow.offsetMax = new Vector2(-14, -55);
            creaturePageButton.transform.SetParent(toolRow, false); Position(creaturePageButton.GetComponent<RectTransform>(), new Vector2(1260, 0), new Vector2(120, 79));
            Panel(root.transform, "Control hint backplate", Vector2.zero, Vector2.zero, new Vector2(260, 183), new Vector2(1330, 219), new Color(panel.r, panel.g, panel.b, .94f));
            status = Label(root.transform, "", 12, white, new Vector2(270, 188), new Vector2(1050, 28));
            RectTransform statusRect = status.rectTransform; statusRect.anchorMin = Vector2.zero; statusRect.anchorMax = Vector2.zero; statusRect.pivot = Vector2.zero;
            SelectCategory(0); SelectInspect(); Refresh(true);
        }

        public void SelectInspect()
        {
            game.Tool = new WorldTool("Наблюдение", "Выберите жителя, поселение или участок мира. Колесо — масштаб, WASD / средняя кнопка — камера.", ToolKind.Inspect, 0, PixelArt.Teal);
            RefreshSelection();
        }
        public void SelectWeatherTab() { SelectCategory(4); }

        private void SelectCategory(int category)
        {
            selectedCategory = category;
            foreach (Button button in toolButtons) UnityEngine.Object.Destroy(button.gameObject);
            toolButtons.Clear(); currentTools.Clear();
            if (category == 0)
            {
                string[] names = { "Глубокая вода", "Мелководье", "Песок", "Луга", "Лес", "Пустыня", "Снег", "Горы", "Болото", "Холмы", "Овраг", "Возвышенность" };
                foreach (TerrainType terrain in Enum.GetValues(typeof(TerrainType)))
                {
                    if ((int)terrain / 8 != terrainPage) continue;
                    currentTools.Add(new WorldTool(names[(int)terrain], "ЛКМ и движение мыши — рисовать. Кисть меняет реальные клетки мира.", ToolKind.Terrain, (int)terrain, PixelArt.Terrain(terrain)));
                }
            }
            else if (category == 1)
            {
                string[] labels = { "Люди", "Эльфы", "Гномы", "Орки", "Овцы", "Волки", "Олени", "Кабаны", "Лисы", "Медведи", "Сумрачные волки", "Лунные олени", "Козы", "Коровы", "Куры", "Ослы", "Лошади" };
                foreach (SpeciesType species in Enum.GetValues(typeof(SpeciesType)))
                {
                    if ((int)species / 9 != creaturePage) continue;
                    currentTools.Add(new WorldTool(labels[(int)species], (int)species < 4 ? "Разместите жителей на суше. Они основывают поселения, собирают ресурсы и строят дома." : "Разместите животных на суше. Их жизнь подчиняется правилам симуляции.", ToolKind.Creature, (int)species, PixelArt.Factions[(int)species % PixelArt.Factions.Length]));
                }
            }
            else if (category == 2)
            {
                string[] names = { "Огонь", "Дождь", "Молния", "Метеор", "Исцеление", "Благословение", "Чума" };
                string[] descriptions = { "Зажечь растительность. Огонь распространяется и наносит урон.", "Погасить огонь и напоить землю.", "Удар молнии наносит урон в выбранной области.", "Разрушительный удар меняет землю и жизнь вокруг.", "Восстановить здоровье существ в радиусе кисти.", "Благословить жителей в выбранной области.", "Заразить существ. Последствия развиваются в симуляции." };
                Color[] colors = { PixelArt.Hex("ed8d54"), PixelArt.Hex("85cbdc"), PixelArt.Hex("d2dfef"), PixelArt.Hex("c17752"), PixelArt.Teal, PixelArt.Gold, PixelArt.Hex("9fbb73") };
                foreach (PowerType power in Enum.GetValues(typeof(PowerType))) currentTools.Add(new WorldTool(names[(int)power], descriptions[(int)power], ToolKind.Power, (int)power, colors[(int)power]));
            }
            else if (category == 3)
            {
                string[] names = { "Рождение", "Войны", "Старение", "Рост растений", "Распростр. огня" };
                foreach (WorldLaw law in Enum.GetValues(typeof(WorldLaw))) currentTools.Add(new WorldTool(names[(int)law], "Законы управляют настоящими системами мира и сохраняются вместе с ним.", ToolKind.Law, (int)law, PixelArt.Teal));
            }
            else
            {
                SelectInspect();
                foreach (WeatherKind kind in Enum.GetValues(typeof(WeatherKind))) currentTools.Add(new WorldTool(WeatherCatalog.Name(kind), "Погодный фронт вокруг выбранного участка, иначе вокруг центра камеры.", ToolKind.Inspect, (int)kind, PixelArt.Teal));
            }
            creaturePageButton.gameObject.SetActive(category == 0 || category == 1);
            creaturePageButton.GetComponentInChildren<Text>().text = category == 0 ? "Ландшафт " + (terrainPage + 1) + "/2  ›" : "Жизнь " + (creaturePage + 1) + "/2  ›";
            for (int i = 0; i < currentTools.Count; i++)
            {
                WorldTool tool = currentTools[i];
                Button button = ButtonAt(toolRow, tool.Label, new Vector2(i * 139, 0), new Vector2(130, 79), () => { if (category == 4) ApplyWeather((WeatherKind)tool.Value); else if (tool.Kind == ToolKind.Law) { WorldLaw law = (WorldLaw)tool.Value; game.Engine.SetLaw(law, !ReadLaw(law)); ShowToast(tool.Label + (ReadLaw(law) ? ": включено" : ": выключено")); } else game.Tool = tool; RefreshSelection(); });
                Text text = button.GetComponentInChildren<Text>(); text.fontSize = 13; text.alignment = TextAnchor.LowerCenter; text.rectTransform.offsetMin = new Vector2(3, 11); text.rectTransform.offsetMax = new Vector2(-3, -7);
                Image swatch = new GameObject("Symbol", typeof(RectTransform), typeof(Image)).GetComponent<Image>(); swatch.transform.SetParent(button.transform, false); swatch.raycastTarget = false; swatch.color = tool.Color;
                Position(swatch.rectTransform, new Vector2(54, -14), new Vector2(22, 22));
                int icon = category == 4 ? 2000 + tool.Value : ToolIcon(tool);
                if (icon >= 0) { swatch.sprite = IconSprite(icon); swatch.color = Color.white; swatch.rectTransform.sizeDelta = new Vector2(39, 39); swatch.rectTransform.anchoredPosition = new Vector2(46, -5); }
                toolButtons.Add(button);
            }
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            for (int i = 0; i < toolButtons.Count; i++)
            {
                bool active = selectedCategory == 4 ? game.World.Weather != null && (int)game.World.Weather.Kind == currentTools[i].Value : currentTools[i].Kind == ToolKind.Law ? ReadLaw((WorldLaw)currentTools[i].Value) : ReferenceEquals(game.Tool, currentTools[i]);
                toolButtons[i].GetComponent<Image>().color = active ? PixelArt.Hex("3b635f") : surface;
                if (currentTools[i].Kind == ToolKind.Law) toolButtons[i].GetComponentInChildren<Text>().text = currentTools[i].Label + (active ? "  ●" : "  ○");
            }
            for (int i = 0; i < categoryButtons.Count; i++) categoryButtons[i].GetComponent<Image>().color = i == selectedCategory ? PixelArt.Hex("3a514e") : surface;
            if (selectedCategory == 4) toolHeading.text = "Погода · кнопка применяет к " + WeatherTargetLabel();
            else if (game.Tool != null) toolHeading.text = game.Tool.Label + "  ·  " + (game.Tool.Kind == ToolKind.Inspect ? "исследуйте свой мир" : "ЛКМ — применить");
        }

        private Vector2Int WeatherTarget()
        {
            if (game.World.InBounds(game.InspectX, game.InspectY)) return new Vector2Int(game.InspectX, game.InspectY);
            Vector3 center = game.MainCamera.transform.position;
            return new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(center.x), 0, game.World.Width - 1), Mathf.Clamp(Mathf.FloorToInt(center.y), 0, game.World.Height - 1));
        }
        private string WeatherTargetLabel() { Vector2Int site = WeatherTarget(); return "участку " + site.x + ", " + site.y; }
        private void ApplyWeather(WeatherKind kind)
        {
            Vector2Int site = WeatherTarget(); int radius = Mathf.Max(3, game.BrushRadius);
            bool changed = game.Engine.SetWeather(kind, site.x, site.y, radius, 1, 40);
            ShowToast(changed ? WeatherCatalog.Name(kind) + ": центр " + site.x + ", " + site.y + (kind == WeatherKind.Clear ? ". Вода после наводнения сходит постепенно." : ", радиус " + radius + ", 40 с симуляции.") : "Не удалось изменить погоду на этом участке.");
            Refresh(true);
        }

        public void ShowToast(string message) { toast = message; toastUntil = Time.unscaledTime + 6; }
        public void WorldChanged() { seed.text = game.World.Seed.ToString(); minimap.texture = game.View.Minimap; RefreshPreset(); RefreshSelection(); Refresh(true); }
        public void Refresh(bool force = false)
        {
            if (!force && Time.unscaledTime < nextRefresh) return; nextRefresh = Time.unscaledTime + .2f;
            WorldState world = game.World;
            weatherLabel.text = WeatherSummary(world);
            if (selectedCategory == 4) RefreshSelection();
            PopulationCounts population = CountPopulation(world);
            stats.text = "Поселений " + world.Settlements.Count + " · построек " + world.Buildings.Count + "\nВзрослые  М " + population.Men + " · Ж " + population.Women + "\nДети " + (population.Boys + population.Girls) + " (м " + population.Boys + " · д " + population.Girls + ")\nЖивотные " + population.Animals;
            clockLabel.text = (game.Paused ? "МИР НА ПАУЗЕ" : "МИР ЖИВЁТ") + "\nТик " + world.Tick.ToString("N0");
            if (!game.Paused && game.Speed >= 20) clockLabel.text = "Скорость " + game.Speed.ToString("0") + "× · факт " + game.ActualSimulationSpeed.ToString("0.0") + "×\nТик " + world.Tick.ToString("N0") + (game.SimulationBudgetLimited ? " · предел расчёта" : "");
            pauseLabel.text = game.Paused ? "▶" : "II";
            int[] speeds = SimulationPacer.Speeds; for (int i = 0; i < speeds.Length; i++) speedButtons[i].GetComponent<Image>().color = game.Speed == speeds[i] && !game.Paused ? PixelArt.Hex("3b635f") : surface;
            radiusLabel.text = "Радиус: " + game.BrushRadius + "  [ / ]";
            if (game.Tool != null && game.Tool.Kind == ToolKind.Power)
            {
                radiusLabel.fontSize = 10;
                radiusLabel.text = (game.ToolAffectsTerrain ? "Земля: " + game.EffectiveBrushRadius : "") + (game.UnitImpactRadius >= 0 ? (game.ToolAffectsTerrain ? "  /  " : "") + "Жители: " + game.UnitImpactRadius : "") + "  [ / ]";
            }
            else radiusLabel.fontSize = 12;
            if (selectedCategory == 4) { radiusLabel.fontSize = 10; radiusLabel.text = "Радиус команды: " + Mathf.Max(3, game.BrushRadius) + "  [ / ]"; }
            UnitData selected = null;
            for (int i = 0; i < world.Units.Count; i++) if (world.Units[i].Id == game.SelectedUnitId) { selected = world.Units[i]; break; }
            int inspectedCell = world.InBounds(game.InspectX, game.InspectY) ? world.Index(game.InspectX, game.InspectY) : -1;
            if (previousInspectedUnit != game.SelectedUnitId || previousInspectedCell != inspectedCell)
            { inspectorScroll.verticalNormalizedPosition = 1; previousInspectedUnit = game.SelectedUnitId; previousInspectedCell = inspectedCell; }
            if (TryDescribeSelectedShip(world)) { }
            else if (selected != null)
            {
                inspectorTitle.text = selected.Name;
                SettlementData city = world.GetSettlement(selected.SettlementId);
                bool civilized = (int)selected.Species < 4;
                inspectorBody.text = WildlifeArt.SpeciesName(selected.Species) + "  ·  " + ProfessionName(selected) + "\n" + DemographicDescription(selected) + BuildingPresenceDescription(world, selected) + "\nВозраст     " + selected.Age.ToString("0.0") + "\nЗдоровье   " + selected.Health.ToString("0") + " / " + selected.MaxHealth.ToString("0") + "\n\n<b>" + TaskName(world, selected) + "</b>" + (civilized ? "\nНастроение " + Percent(selected.Mood) + " · доверие " + Percent(selected.Trust) : "") + "\nГолод           " + Percent(selected.Hunger) + (civilized ? "\nЖажда          " + Percent(selected.Thirst) : "") + "\nУсталость    " + Percent(selected.Fatigue) + (civilized ? "\nОбразование  " + Percent(selected.Education) : "") + (civilized && selected.IsChild ? "\n\n" + ChildActivityDescription(world, selected) : "") + "\n\n" + (civilized ? CargoDescription(selected) + "\n\n" + EquipmentDescription(world, selected) : WildlifeDescription(world, selected)) + "\n\n" + (city == null ? "Вне поселения" : city.Name) + (city != null && city.RulerId == selected.Id ? "\n<b>" + RulerTitle(city.Government, selected.Sex) + "</b>" : "") + (selected.Blessed ? "\nБлагословлён" : "") + (selected.Infected ? "\nЗаражён" : "") + (civilized ? "\n\n" + CitizenSocietyDescription(world, selected) + "\n\n" + CitizenHoldingsDescription(world, selected) : "") + (city != null ? "\n\n" + PolicyDescription(world, city) : "");
            }
            else if (world.InBounds(game.InspectX, game.InspectY))
            {
                TileData tile = world.Cells[world.Index(game.InspectX, game.InspectY)]; SettlementData city = world.GetSettlement(tile.SettlementId);
                ResourceNodeData node = world.GetResourceAt(game.InspectX, game.InspectY);
                BuildingData inspectedBuilding = null;
                if (tile.BuildingId != 0) for (int i = 0; i < world.Buildings.Count; i++) if (world.Buildings[i].Id == tile.BuildingId) { inspectedBuilding = world.Buildings[i]; break; }
                bool showNode = node.Type != ResourceType.None && tile.BuildingId == 0 && ResourceCatalog.IsCompatible(node.Type, tile);
                if (city != null)
                {
                    inspectorTitle.text = inspectedBuilding != null ? ResourceArt.BuildingName(inspectedBuilding.Type) : showNode ? ResourceArt.ResourceName(node.Type) : city.Name;
                    UnitData ruler = world.GetUnit(city.RulerId);
                    builder.Clear();
                    bool buildingFirst = inspectedBuilding != null && (inspectedBuilding.Type == BuildingType.Hall || IndustryCatalog.IsWorkshop(inspectedBuilding.Type) || MaritimeArt.SupportsBuilding(inspectedBuilding.Type));
                    if (buildingFirst) AppendBuildingDescription(world, inspectedBuilding);
                    builder.Append(SubsistenceDescription(world, city)).Append("\n\n");
                    if (inspectedBuilding != null && !buildingFirst) AppendBuildingDescription(world, inspectedBuilding);
                    builder.Append(WeatherSiteDescription(world, game.InspectX, game.InspectY)).Append("\n\n");
                    if (showNode) builder.Append(NodeDescription(world, game.InspectX, game.InspectY)).Append("\n\n");
                    string infrastructureDescription = InfrastructureDescription(world, game.InspectX, game.InspectY);
                    if (infrastructureDescription.Length > 0) builder.Append(infrastructureDescription).Append("\n\n");
                    builder.Append("<b>").Append(city.Name).Append("</b>\n");
                    builder.Append("<b>" + GovernmentName(city.Government) + "</b>\n" +
                        (ruler == null ? "Нет взрослого правителя" : RulerTitle(city.Government, ruler.Sex) + ": " + ruler.Name) +
                        (city.Government == GovernmentType.Republic ? "\nВыборы через " + Mathf.Max(0, (city.NextElectionTick - world.Tick) * SimulationEngine.TickSeconds).ToString("0") + " с" : "") +
                        "\n\nНаселение   " + city.Population + "\n" + PopulationDescription(world, city.Id) + "\nЖильё          " + city.Housing + "\nПостройки    " + city.Buildings +
                        "\n\n" + ExpansionDescription(world, city) + "\n\n" + SettlementSocietyDescription(world, city) + "\n\n" + ResearchDescription(city) + "\n\n" + PolicyDescription(world, city) + "\n\n" + ParliamentDescription(world, city) + "\n\n<b>ОБЩИЕ ЗАПАСЫ</b>\nОбщинные порции  " + city.Food.ToString("0.#"));
                    AppendSettlementStocks(world, city.Id);
                    builder.Append("\n\n").Append(TreasuryDescription(world, city.Id));
                    builder.Append("\n\n").Append(MarketDescription(world, city.Id));
                    builder.Append("\n\n").Append(RecentTradesDescription(world, city.Id, 0));
                    builder.Append("\n\n").Append(CaravansDescription(world, city.Id));
                    inspectorBody.text = builder.ToString();
                }
                else { inspectorTitle.text = showNode ? ResourceArt.ResourceName(node.Type) : tile.WaterKind != WaterKind.None ? TransportArt.WaterName(tile.WaterKind) : TerrainName(tile.Terrain); inspectorBody.text = (showNode ? NodeDescription(world, game.InspectX, game.InspectY) + "\n\n" : "") + "Участок " + game.InspectX + ", " + game.InspectY + "\n\n" + InfrastructureDescription(world, game.InspectX, game.InspectY) + "\n\nВысота         " + tile.Height.ToString("0.00") + "\nВлажность   " + tile.Moisture.ToString("0.00") + "\nТемпература " + tile.Temperature.ToString("0.00") + "\nРастительность " + tile.Vegetation.ToString("0.00") + (tile.Fire > 0 ? "\n\nНа этом участке пожар" : ""); }
            }
            else { inspectorTitle.text = game.Tool?.Label ?? "Живой мир"; inspectorBody.text = (game.Tool?.Description ?? "") + "\n\nI — наблюдение\nПробел — пауза\nHome — показать весь мир\nF12 — снимок игры"; }
            if (selected != null) inspectorBody.text += "\n\n" + WeatherSiteDescription(world, (int)selected.X, (int)selected.Y);
            else if (game.SelectedShipId == 0 && world.InBounds(game.InspectX, game.InspectY) && world.GetSettlement(world.Cells[world.Index(game.InspectX, game.InspectY)].SettlementId) == null) inspectorBody.text = WeatherSiteDescription(world, game.InspectX, game.InspectY) + "\n\n" + inspectorBody.text;
            RefreshBuildingAccessControls(world);
            RefreshMaritimeControls(world); RefreshExpansionControls(world);
            builder.Clear(); int count = 0, eventIndex = world.Events.Count - 1, socialIndex = world.SocialEvents == null ? -1 : world.SocialEvents.Count - 1;
            while ((eventIndex >= 0 || socialIndex >= 0) && count < 5)
            {
                bool social = socialIndex >= 0 && (eventIndex < 0 || world.SocialEvents[socialIndex].Tick >= world.Events[eventIndex].Tick);
                string eventText = social ? world.SocialEvents[socialIndex--].Text : world.Events[eventIndex--].Text;
                if (string.IsNullOrEmpty(eventText)) continue;
                if (builder.Length > 0) builder.Append("\n\n"); builder.Append("<color=#d5b36d>•</color> ").Append(eventText); count++;
            }
            chronicle.text = builder.Length == 0 ? "Каждый мир начинается с тишины.\n\nПоселите жителей, и здесь появятся первые события." : builder.ToString();
            status.text = Time.unscaledTime < toastUntil ? toast : "WASD / средняя кнопка — камера     Колесо — масштаб     Пробел — пауза     Ctrl+S / Ctrl+L — сохранение";
        }
        public static string WeatherSummary(WorldState world)
        {
            WeatherData weather = world.Weather;
            if (weather == null) return "<b>ПОГОДА</b>\nНет данных";
            bool active = weather.Kind != WeatherKind.Clear && weather.RemainingSeconds > 0;
            string details = active ? "Фронт ещё " + weather.RemainingSeconds.ToString("0.0") + " с\nЦентр " + weather.CenterX.ToString("0") + ", " + weather.CenterY.ToString("0") + " · радиус " + weather.Radius.ToString("0") :
                weather.Automatic ? "Смена через " + Mathf.Max(0, (weather.NextTransitionTick - world.Tick) * SimulationEngine.TickSeconds).ToString("0") + " с" : "Автосмена выключена";
            return "<b>ПОГОДА · " + WeatherCatalog.Name(weather.Kind) + "</b>\n" + details + "\nЗатоплено клеток: " + (world.FloodCells == null ? 0 : world.FloodCells.Count);
        }
        public static string WeatherSiteDescription(WorldState world, int x, int y)
        {
            if (!world.InBounds(x, y)) return "";
            int index = world.Index(x, y); TileData tile = world.Cells[index];
            float influence = world.GetWeatherIntensityAt(x, y);
            string result = "<b>БИОМ · " + BiomeCatalog.Name(world.GetBiome(index)) + "</b>\n" +
                (influence > 0 && world.Weather != null ? WeatherCatalog.Name(world.Weather.Kind) + " · сила здесь " + Percent(influence) : "Вне активного погодного фронта") +
                "\nСнежный покров " + Percent(tile.SnowCover);
            if (world.FloodCells != null) foreach (FloodCellData flood in world.FloodCells)
                if (flood.Cell == index && flood.Matches(tile))
                {
                    result += "\n<b>Временное затопление</b>\nСпад воды через " + Mathf.Max(0, (flood.ExpiresTick - world.Tick) * SimulationEngine.TickSeconds).ToString("0.0") + " с";
                    break;
                }
            return result;
        }
        private static string TerrainName(TerrainType value) { string[] names = { "Океан", "Мелководье", "Песчаный берег", "Луг", "Лес", "Пустыня", "Снежные земли", "Горы", "Болото", "Холмы", "Овраг", "Возвышенность" }; return names[(int)value]; }
        private void RefreshPreset() { if (presetLabel != null) { string[] names = { "Остров", "Архипелаг", "Континент" }; presetLabel.text = "Форма: " + names[(int)game.GenerationPreset] + "  ›"; } }
        private bool ReadLaw(WorldLaw law)
        {
            switch (law) { case WorldLaw.Births: return game.World.Laws.Births; case WorldLaw.Warfare: return game.World.Laws.Warfare; case WorldLaw.Aging: return game.World.Laws.Aging; case WorldLaw.VegetationGrowth: return game.World.Laws.VegetationGrowth; case WorldLaw.FireSpread: return game.World.Laws.FireSpread; default: return false; }
        }
        private static string GovernmentName(GovernmentType government)
        {
            switch (government) { case GovernmentType.Feudal: return "Феодальное правление"; case GovernmentType.Republic: return "Республика"; case GovernmentType.Theocracy: return "Теократия"; default: return "Поселение формируется"; }
        }
        private static string RulerTitle(GovernmentType government, Sex sex = Sex.Male)
        {
            switch (government) { case GovernmentType.Feudal: return sex == Sex.Female ? "Княгиня" : "Князь"; case GovernmentType.Republic: return "Консул"; case GovernmentType.Theocracy: return sex == Sex.Female ? "Верховная жрица" : "Верховный жрец"; default: return "Правитель"; }
        }
        private static string ProfessionName(UnitData unit)
        {
            if ((int)unit.Species >= 4) return "животное";
            if (unit.Age < 16) return "ребёнок";
            string marine = MaritimeProfessionName(unit.Profession); if (marine.Length > 0) return marine;
            string industrial = IndustryProfessionName(unit.Profession); if (industrial.Length > 0) return industrial;
            switch (unit.Profession) { case UnitProfession.Farmer: return "земледелец"; case UnitProfession.Forester: return "добытчик / мастер"; case UnitProfession.Guard: return "страж"; case UnitProfession.Hunter: return "охотник"; case UnitProfession.Herder: return "животновод"; case UnitProfession.Merchant: return "торговец"; case UnitProfession.Carpenter: return "плотник"; default: return "житель"; }
        }
        public struct PopulationCounts { public int Men, Women, Boys, Girls, Animals; }
        public static PopulationCounts CountPopulation(WorldState world, int settlementId = 0)
        {
            PopulationCounts result = default(PopulationCounts);
            for (int i = 0; i < world.Units.Count; i++)
            {
                UnitData unit = world.Units[i];
                if (unit.Health <= 0 || (settlementId != 0 && unit.SettlementId != settlementId)) continue;
                if ((int)unit.Species >= 4) { result.Animals++; continue; }
                if (unit.IsChild) { if (unit.Sex == Sex.Female) result.Girls++; else result.Boys++; }
                else { if (unit.Sex == Sex.Female) result.Women++; else result.Men++; }
            }
            return result;
        }
        public static string PopulationDescription(WorldState world, int settlementId = 0)
        {
            PopulationCounts p = CountPopulation(world, settlementId);
            return "Взрослые: М " + p.Men + " · Ж " + p.Women + "\nДети: мальчики " + p.Boys + " · девочки " + p.Girls;
        }
        public static string DemographicDescription(UnitData unit)
        {
            if ((int)unit.Species >= 4) return unit.Sex == Sex.Female ? "Самка" : "Самец";
            if (!unit.IsChild) return unit.Sex == Sex.Female ? "Женщина" : "Мужчина";
            return (unit.Sex == Sex.Female ? "Девочка" : "Мальчик") + (unit.Age < 12 ? " · ребёнок" : " · подросток 12–15");
        }
        public static string ChildActivityDescription(WorldState world, UnitData unit)
        {
            if (!unit.IsChild) return "Взрослый житель";
            if (unit.Age < 12) return "Младше 12: работа запрещена\nИгра, учёба и отдых";
            SettlementData city = world.GetSettlement(unit.SettlementId);
            return city != null && city.ChildLabor == ChildLaborPolicy.LightWork
                ? "Лёгкая работа 12–15: разрешена\nПоле и лёгкая доставка; учёба"
                : "Детский труд запрещён\nИгра, учёба и отдых";
        }
        private static string Percent(float value) { return (value * 100).ToString("0.#") + "%"; }
        public static string CitizenSocietyDescription(WorldState world, UnitData unit)
        {
            MoodFactors factors = unit.MoodInputs;
            BuildingData home = world.GetBuilding(unit.HomeBuildingId);
            ReligionData faith = world.GetReligion(unit.FaithId);
            string homeName = home == null ? "Не закреплено" : ResourceArt.BuildingName(home.Type) + " · уровень " + home.Level;
            string faithName = faith == null || faith.FounderId == 0 ? "Не присоединился к вере" : faith.Name;
            StringBuilder text = new StringBuilder("<b>ЖИЗНЬ И СПОСОБНОСТИ</b>\n");
            text.Append("Вода: утоление жажды ").Append(Percent(world.GetWaterAccess(unit)));
            text.Append("\nКомфорт жилья ").Append(Percent(world.GetHousingComfort(unit))).Append("\nДом: ").Append(homeName);
            text.Append("\nПотребность в общении ").Append(Percent(unit.SocialNeed));
            text.Append("\nОбразование ").Append(Percent(unit.Education));
            text.Append("\nТалант к учёбе ").Append(Percent(unit.LearningTalent));
            text.Append("\nРемесленный навык ").Append(Percent(unit.CraftSkill));
            if (unit.IsMaster) text.Append("\n<b>Мастер своего дела</b>");
            text.Append("\n\n<b>ПОЧЕМУ ТАКОЕ НАСТРОЕНИЕ</b>\nТекущее ").Append(Percent(unit.Mood)).Append(" → цель ").Append(Percent(factors.Target));
            text.Append("\nУдовлетворённость:");
            text.Append("\nЕда ").Append(Percent(factors.Food)).Append(" · вода ").Append(Percent(factors.Water));
            text.Append("\nЖильё ").Append(Percent(factors.Housing)).Append(" · отдых ").Append(Percent(factors.Rest));
            text.Append("\nОбщение ").Append(Percent(factors.Social));
            text.Append("\nЗаконы ").Append(Percent(factors.Laws)).Append(" · безопасность ").Append(Percent(factors.Safety));
            text.Append("\nСтресс (снижает) ").Append(Percent(factors.Stress));
            text.Append("\nДоверие правителю ").Append(Percent(unit.Trust));
            text.Append("\n\n<b>ВЕРА</b>\n").Append(faithName);
            if (faith != null && faith.FounderId != 0)
            {
                text.Append("\nБожество: ").Append(faith.Deity).Append("\nУчение: ").Append(faith.Doctrine);
                text.Append("\nПриверженность ").Append(Percent(unit.FaithCommitment));
                UnitData founder = world.GetUnit(faith.FounderId);
                text.Append("\nОснователь: ").Append(founder == null ? "житель #" + faith.FounderId : founder.Name);
            }
            SettlementData city = world.GetSettlement(unit.SettlementId);
            ParliamentDecisionData decision = city == null ? null : city.LastParliament;
            if (decision != null && decision.Votes != null) for (int i = 0; i < decision.Votes.Count; i++)
                if (decision.Votes[i].UnitId == unit.Id) { text.Append("\n\nПоследний голос: ").Append(decision.Votes[i].Supports ? "за" : "против").Append("\nТик голосования ").Append(decision.Tick); break; }
            return text.ToString();
        }
        public static string SettlementSocietyDescription(WorldState world, SettlementData city)
        {
            string state = city.SocialState == SocialState.Protest ? "Протест" : city.SocialState == SocialState.Discontent ? "Недовольство" : "Спокойствие";
            ReligionData majority = world.GetReligion(city.MajorityFaithId);
            return "<b>ОБЩЕСТВО · " + state + "</b>\nНастроение " + Percent(city.AverageMood) + "\nДоверие " + Percent(city.AverageTrust) +
                "\nНедовольство " + Percent(city.Discontent) + "\nКризис длится " + city.CrisisSeconds.ToString("0.#") + " с" +
                "\nСторонники бунта " + city.RevoltSupporters + "\nБунтов в истории " + city.RevoltCount + "\nПотери в войнах " + city.WarDeaths +
                "\nКомфорт жилья " + Percent(world.GetHousingComfort(city.Id)) + "\nПоддержка обучения " + Percent(world.GetLearningSupport(city.Id)) +
                (majority != null && majority.FounderId != 0 ? "\nБольшинство верит: " + majority.Name : "\nОбщая вера не сложилась");
        }
        public static string ParliamentDescription(WorldState world, SettlementData city)
        {
            ParliamentDecisionData p = city.LastParliament;
            if (p == null || p.AdultCount == 0) return city.Government == GovernmentType.Republic ? "<b>ПАРЛАМЕНТ</b>\nГолосований пока не было" : "<b>ПРАВЛЕНИЕ</b>\nПарламент не предусмотрен этим правлением";
            StringBuilder text = new StringBuilder("<b>ПОСЛЕДНЕЕ ГОЛОСОВАНИЕ</b>\n");
            text.Append(p.Passed ? "Принято" : "Отклонено").Append(" · тик ").Append(p.Tick);
            text.Append("\nЗа ").Append(p.Yes).Append(" · против ").Append(p.No).Append("\nГолосовали взрослые: ").Append(p.AdultCount);
            text.Append("\nПредложение: ").Append(p.EconomyPolicy == EconomicPolicy.Market ? "платный обмен (бартер)" : "бесплатная передача");
            text.Append("\nНалог ").Append(Percent(p.TaxRate)).Append(" · терпимость ").Append(Percent(p.ReligiousTolerance));
            text.Append("\n").Append(p.ChildLabor == ChildLaborPolicy.LightWork ? "Лёгкая работа 12–15" : "Детский труд запрещён");
            text.Append("\nФокус: ").Append(FocusName(p.ResourceFocus));
            if (p.Votes != null)
            {
                int shown = Math.Min(6, p.Votes.Count);
                for (int i = 0; i < shown; i++)
                {
                    ParliamentVoteData vote = p.Votes[i]; UnitData voter = world.GetUnit(vote.UnitId);
                    text.Append('\n').Append(voter == null ? "Житель #" + vote.UnitId : voter.Name).Append(vote.Supports ? " — за" : " — против");
                }
                if (p.Votes.Count > shown) text.Append("\nИ ещё ").Append(p.Votes.Count - shown).Append(" голосов");
            }
            text.Append("\nДействующие правила — в распоряжениях выше.");
            return text.ToString();
        }
        public static string ResearchDescription(SettlementData city)
        {
            StringBuilder text = new StringBuilder("<b>ИССЛЕДОВАНИЕ</b>\n");
            TechnologyDefinition active = TechnologyCatalog.Get(city.ActiveTechnology);
            if (active == null) text.Append("Активного проекта нет");
            else
            {
                text.Append(active.Name).Append('\n').Append(city.ResearchProgress.ToString("0.##")).Append(" / ").Append(active.Cost.ToString("0.##")).Append(" очков");
                text.Append(" · ").Append(Percent(active.Cost > 0 ? city.ResearchProgress / active.Cost : 0));
                text.Append("\nСкорость ").Append(city.ResearchRate.ToString("0.####")).Append(" очков/с мира");
            }
            int count = city.UnlockedTechnologies == null ? 0 : city.UnlockedTechnologies.Count;
            text.Append("\nИзучено: ").Append(count);
            for (int i = 0; i < count; i++)
            {
                TechnologyDefinition known = TechnologyCatalog.Get(city.UnlockedTechnologies[i]);
                if (known != null) text.Append("\n✓ ").Append(known.Name);
            }
            return text.ToString();
        }
        public static string BuildingStatusDescription(WorldState world, BuildingData building)
        {
            UnitData owner = world.GetUnit(building.OwnerId);
            string grade = StorageCapacity.IsDepot(building) ? StorageCapacity.EffectiveLevel(building) + " / " + StorageCapacity.MaximumLevel(building.Type) : building.Level.ToString();
            string result = "<b>" + ResourceArt.BuildingName(building.Type) + "</b>\nУровень " + grade + "\nПрочность " + building.Health.ToString("0.#");
            result += building.Ownership == BuildingOwnership.Public ? "\nСобственность: общинная" : "\nСобственность: частная\nВладелец: " + (owner == null ? "житель #" + building.OwnerId : owner.Name);
            if (building.Type == BuildingType.Hall) result += "\n\n" + CampfireDescription(world, building);
            if (building.Type == BuildingType.Well) result += "\nЗапас воды " + building.WaterStored.ToString("0.##") + " / " + SubsistenceRules.WaterCapacity(building).ToString("0.##");
            if (building.Type == BuildingType.Gate) result += building.GateOpen ? "\nВорота открыты" : "\nВорота закрыты";
            if (StorageCapacity.IsDepot(building)) result += "\n\n" + StorageDescription(building);
            if (IndustryCatalog.FurnitureCapacity(building) > 0) result += "\nМебель " + building.FurnitureCount + " / " + IndustryCatalog.FurnitureCapacity(building);
            return result + "\n";
        }
        public static string CampfireDescription(WorldState world, BuildingData building)
        {
            if (building == null || building.Type != BuildingType.Hall) return "";
            bool selected = world.GetCampfire(building.SettlementId) == building;
            bool working = SubsistenceRules.IsWorkingCampfire(world, building);
            float daily = working ? SubsistenceRules.ProductionPerSecond * SubsistenceRules.DaySeconds : 0;
            SettlementData city = world.GetSettlement(building.SettlementId);
            string result = "<b>СНАБЖЕНИЕ ОЧАГА</b>\n" + (working ? "Работает · на " + SubsistenceRules.CampfirePeople + " жителей" : building.Health <= 0 ? "Очаг не действует" : selected ? "Снабжение приостановлено" : "Дополнительный очаг · без выработки");
            result += "\nМощность в день · еда / вода\n" + (daily * SubsistenceRules.FoodPerPersonPerDay).ToString("0.#") + " / " + (daily * SubsistenceRules.WaterPerPersonPerDay).ToString("0.#");
            result += "\nВода " + building.WaterStored.ToString("0.##") + " / " + SubsistenceRules.WaterCapacity(building).ToString("0.##");
            if (city != null) result += "\nЕда общины " + city.Food.ToString("0.##");
            if (selected) result += "\nОчаг пополняет еду до " + SubsistenceRules.FoodReserve.ToString("0.#");
            return result + "\nДень: " + (SubsistenceRules.DaySeconds / 60f).ToString("0.#") + " мин при 1×";
        }
        public static string SubsistenceDescription(WorldState world, SettlementData city)
        {
            if (city == null) return "";
            BuildingData campfire = world.GetCampfire(city.Id);
            float people = SubsistenceRules.IsWorkingCampfire(world, campfire) ? SubsistenceRules.ProductionPerSecond * SubsistenceRules.DaySeconds : 0;
            float foodNeed = city.Population * SubsistenceRules.FoodPerPersonPerDay;
            float waterNeed = city.Population * SubsistenceRules.WaterPerPersonPerDay;
            float foodSupply = people * SubsistenceRules.FoodPerPersonPerDay;
            float waterSupply = people * SubsistenceRules.WaterPerPersonPerDay;
            return "<b>БАЗОВЫЙ ПЛАН НА ДЕНЬ</b>\nДень: " + (SubsistenceRules.DaySeconds / 60f).ToString("0.#") + " мин при 1×" +
                "\nЕда / вода · порции\nНа жителя: " + SubsistenceRules.FoodPerPersonPerDay.ToString("0.#") + " / " + SubsistenceRules.WaterPerPersonPerDay.ToString("0.#") +
                "\nНужно для " + city.Population + ": " + foodNeed.ToString("0.#") + " / " + waterNeed.ToString("0.#") +
                "\nОдин очаг: " + foodSupply.ToString("0.#") + " / " + waterSupply.ToString("0.#") +
                "\nДефицит базы: " + Mathf.Max(0, foodNeed - foodSupply).ToString("0.#") + " / " + Mathf.Max(0, waterNeed - waterSupply).ToString("0.#") +
                "\nОстальное — добыча и хозяйство.";
        }
        public static string StorageDescription(BuildingData building)
        {
            if (!StorageCapacity.IsDepot(building)) return "";
            float used = StorageCapacity.TotalUsed(building), capacity = StorageCapacity.Capacity(building), overflow = StorageCapacity.Overflow(building);
            string result = "<b>ХРАНЕНИЕ</b>\nЗанято " + used.ToString("0.###") + " / " + capacity.ToString("0") + "\nСвободно " + Mathf.Max(0, capacity - used).ToString("0.###");
            result += building.Type == BuildingType.Granary ? "\nПринимает: пищу, напитки и корм" : "\nПринимает: материалы и несъедобное сырьё";
            if (overflow > .001f) result += "\nПереполнение " + overflow.ToString("0.###") + " · приём приостановлен";
            float misplaced = building.Type == BuildingType.Granary ? StorageCapacity.MaterialUsed(building) : 0;
            if (building.AgriculturalStock != null) for (int i = 1; i < building.AgriculturalStock.Length; i++)
                if (!StorageCapacity.Accepts(building, (AgriculturalProduct)i)) misplaced += building.AgriculturalStock[i];
            if (misplaced > .001f) result += "\nПрежние товары другого назначения: " + misplaced.ToString("0.###") + ". Сохранены до вывоза.";
            return result;
        }
        public static string InfrastructureDescription(WorldState world, int x, int y)
        {
            if (world.Infrastructure == null || !world.InBounds(x, y)) return "";
            int index = world.Index(x, y); InfrastructureCell cell = world.Infrastructure[index];
            TileData tile = world.Cells[index];
            StringBuilder text = new StringBuilder();
            if (tile.WaterKind != WaterKind.None)
            {
                text.Append(TransportArt.WaterName(tile.WaterKind)).Append("\nГлубина ").Append(tile.WaterDepth.ToString("0.00"));
                text.Append("\nСила течения ").Append(Percent(tile.CurrentStrength));
                if (tile.ChannelWidth > 0) text.Append("\nШирина русла ").Append(tile.ChannelWidth).Append(" кл.");
            }
            if (text.Length > 0) text.Append('\n'); text.Append(TransportArt.RoadName(cell.RoadLevel));
            if (cell.FootTraffic > 0) text.Append("\nСледы движения ").Append(cell.FootTraffic.ToString("0.#"));
            if (cell.BridgeLevel > 0)
            {
                text.Append('\n').Append(cell.BridgeLevel == 1 ? "Деревянный мост" : "Каменный мост").Append(cell.BridgeAxis == 0 ? " · запад–восток" : " · север–юг");
                SettlementData owner = world.GetSettlement(cell.BridgeOwnerId);
                if (owner != null) text.Append("\nВладелец моста: ").Append(owner.Name);
            }
            string blocked = world.GetTraversalBlockReason(index);
            if (string.IsNullOrEmpty(blocked))
            {
                text.Append(tile.WaterDepth > 0 && cell.BridgeLevel == 0 ? "\nДоступен брод" : "\nУчасток проходим пешком");
                text.Append("\nСкорость местности ×").Append(world.GetTerrainSpeed(index).ToString("0.00"));
                if (tile.Terrain == TerrainType.Ravine)
                {
                    int exits = 0;
                    if (world.InBounds(x - 1, y) && world.CanStep(index, world.Index(x - 1, y))) exits++;
                    if (world.InBounds(x + 1, y) && world.CanStep(index, world.Index(x + 1, y))) exits++;
                    if (world.InBounds(x, y - 1) && world.CanStep(index, world.Index(x, y - 1))) exits++;
                    if (world.InBounds(x, y + 1) && world.CanStep(index, world.Index(x, y + 1))) exits++;
                    text.Append("\nПроходимых выходов: ").Append(exits).Append(" / 4");
                }
            }
            else text.Append("\nПроход закрыт: ").Append(blocked);
            if (tile.Height > 0) text.Append("\nС высоты: до +20% урона по цели ниже. Подъём замедляет движение.");
            if (world.IsFreshWater(index)) { if (text.Length > 0) text.Append('\n'); text.Append("Источник пресной воды"); }
            if (cell.FishCapacity > 0)
            {
                if (text.Length > 0) text.Append('\n'); text.Append("Рыба ").Append(cell.Fish.ToString("0.##")).Append(" / ").Append(cell.FishCapacity.ToString("0.##"));
                if (cell.Fish <= .001f) text.Append(" · выловлена");
                text.Append("\nОбщий запас водоёма; возможный улов:");
                foreach (FishDefinition fish in FishingCatalog.All)
                    if (FishingCatalog.Eligible(world, index, 5, fish.Id))
                        text.Append('\n').Append(fish.Name).Append(" · снасть ").Append(fish.MinimumRodLevel).Append(" · ").Append(FishRarityName(fish.Rarity));
            }
            if (world.TransportProjects != null) foreach (TransportProjectData project in world.TransportProjects)
            {
                bool here = project.BankCell == index;
                if (!here && project.Cells != null) for (int i = 0; i < project.Cells.Length; i++) if (project.Cells[i] == index) { here = true; break; }
                if (!here) continue;
                text.Append("\n\nСТРОЙКА: ").Append(project.Bridge ? "мост" : TransportArt.RoadName(project.Level));
                text.Append("\nДоставлено: ").Append(ResourceArt.ItemName(project.Material)).Append(' ').Append(project.Delivered.ToString("0.##")).Append(" / ").Append(project.Required.ToString("0.##"));
                text.Append("\nВыполнено работы: ").Append(project.WorkSeconds.ToString("0.0")).Append(" с");
                UnitData worker = world.GetUnit(project.WorkerId); text.Append(worker == null ? "\nОжидает рабочего" : "\nРабочий: " + worker.Name);
            }
            return text.ToString();
        }
        private static string FocusName(ResourceFocus focus)
        { return focus == ResourceFocus.Timber ? "древесина" : focus == ResourceFocus.Masonry ? "каменное дело" : focus == ResourceFocus.Metals ? "руды" : "сбалансированное снабжение"; }
        public static string PolicyDescription(WorldState world, SettlementData city)
        {
            UnitData ruler = world.GetUnit(city.PolicyRulerId);
            string focus = city.ResourceFocus == ResourceFocus.Timber ? "древесина" : city.ResourceFocus == ResourceFocus.Masonry ? "каменное дело" : city.ResourceFocus == ResourceFocus.Metals ? "руды" : "сбалансированное снабжение";
            return "<b>РАСПОРЯЖЕНИЯ</b>\n" + (city.EconomyPolicy == EconomicPolicy.Market ? "Платный обмен (бартер)" : "Бесплатная передача") +
                "\nНатуральный налог  " + (city.TaxRate * 100).ToString("0.#") + "% с оплаты" +
                "\nТерпимость к вере  " + Percent(city.ReligiousTolerance) +
                "\n" + (city.ChildLabor == ChildLaborPolicy.LightWork ? "Лёгкая работа 12–15: разрешена" : "Детский труд запрещён") +
                "\nПриоритет: " + focus + (ruler != null ? "\nРешение: " + ruler.Name : "\nВременный порядок без правителя") +
                (!string.IsNullOrEmpty(city.LastDecreeReason) ? "\nПричина: " + city.LastDecreeReason : "") +
                "\nПересмотр через " + Mathf.Max(0, (city.NextPolicyReviewTick - world.Tick) * SimulationEngine.TickSeconds).ToString("0") + " с\n\n" + PlanningDescription(city);
        }
        public static string PlanningDescription(SettlementData city)
        {
            return "<b>ПЛАНИРОВКА ПОСЕЛЕНИЯ</b>\n" +
                (string.IsNullOrEmpty(city.LastPlanningReason) ? "Решений о перестройке пока нет" : "Решение · тик " + city.LastPlanningTick + "\n" + city.LastPlanningReason);
        }
        public static string CitizenHoldingsDescription(WorldState world, UnitData unit)
        {
            StringBuilder text = new StringBuilder("<b>ЛИЧНЫЕ ТОВАРЫ НА СКЛАДАХ</b>"); bool any = false;
            for (int i = 1; i < ResourceCatalog.ItemCount; i++)
            {
                float stock = world.GetCitizenStock(unit.Id, (ItemType)i); if (stock <= 0) continue;
                text.Append('\n').Append(ResourceArt.ItemName((ItemType)i)).Append("  ").Append(stock.ToString("0.###")); any = true;
            }
            if (!any) text.Append("\nЛичных партий нет");
            if (!unit.IsChild) text.Append("\n\n").Append(RecentTradesDescription(world, unit.SettlementId, unit.Id)).Append("\n\n").Append(CaravansDescription(world, unit.SettlementId, unit.Id));
            return text.ToString();
        }
        public static string CaravanPhaseName(CaravanPhase phase)
        {
            switch (phase) { case CaravanPhase.Loading: return "Сбор груза"; case CaravanPhase.Outbound: return "В пути к рынку"; case CaravanPhase.Returning: return "Возвращается"; case CaravanPhase.Completed: return "Рейс завершён"; case CaravanPhase.Cancelled: return "Рейс отменён"; default: return "Рейс"; }
        }
        private static string MemberPhaseName(CaravanMemberPhase phase)
        {
            switch (phase) { case CaravanMemberPhase.Loading: return "загружается"; case CaravanMemberPhase.Ready: return "готов к выходу"; case CaravanMemberPhase.Outbound: return "идёт на рынок"; case CaravanMemberPhase.AtMarket: return "на рынке"; case CaravanMemberPhase.Returning: return "возвращается"; case CaravanMemberPhase.Done: return "участие окончено"; default: return "участник"; }
        }
        public static string CaravanDescription(WorldState world, CaravanData caravan)
        {
            if (caravan == null) return "Рейса нет";
            SettlementData origin = world.GetSettlement(caravan.OriginId), destination = world.GetSettlement(caravan.DestinationId);
            StringBuilder text = new StringBuilder("<b>КАРАВАН #").Append(caravan.Id).Append("</b>\n")
                .Append(origin == null ? "Поселение #" + caravan.OriginId : origin.Name).Append(" → ").Append(destination == null ? "Поселение #" + caravan.DestinationId : destination.Name)
                .Append('\n').Append(CaravanPhaseName(caravan.Phase));
            if (caravan.Aborted && caravan.Phase == CaravanPhase.Returning) text.Append(" · отменён, груз возвращается");
            text.Append("\nПлан обмена: ").Append(ResourceArt.ItemName(caravan.ExportItem)).Append(" → ").Append(ResourceArt.ItemName(caravan.ImportItem));
            if (!string.IsNullOrEmpty(caravan.LastReason)) text.Append('\n').Append(caravan.LastReason);
            for (int i = 0; i < caravan.Members.Count; i++)
            {
                CaravanMemberData member = caravan.Members[i]; UnitData unit = world.GetUnit(member.UnitId);
                text.Append("\n\n").Append(unit == null ? "Житель #" + member.UnitId : unit.Name).Append(member.UnitId == caravan.LeaderId ? " · ведущий" : member.HireFoodPaid > 0 ? " · нанятый помощник" : " · добровольный помощник");
                text.Append("\n").Append(MemberPhaseName(member.Phase));
                if (member.HireFoodPaid > 0) text.Append("\nВыдано питания: ").Append(member.HireFoodPaid.ToString("0.##"));
                if (unit != null && unit.Health > 0 && unit.CaravanId == caravan.Id && member.Phase != CaravanMemberPhase.Done)
                    text.Append("\nФактически несёт: ").Append(unit.CargoAmount > .001f ? ResourceArt.ItemName(unit.CargoKind) + " " + unit.CargoAmount.ToString("0.###") : "пусто");
                if (member.BlockedSinceTick > 0 && member.Phase != CaravanMemberPhase.Done)
                    text.Append("\nЗадержка маршрута: ").Append(Mathf.Max(0, (world.Tick - member.BlockedSinceTick) * SimulationEngine.TickSeconds).ToString("0.#")).Append(" с");
            }
            return text.ToString();
        }
        public static string CaravansDescription(WorldState world, int settlementId, int unitId = 0)
        {
            StringBuilder text = new StringBuilder("<b>КАРАВАНЫ</b>"); int count = 0;
            if (world.Caravans != null) for (int i = world.Caravans.Count - 1; i >= 0 && count < 2; i--)
            {
                CaravanData caravan = world.Caravans[i]; bool include = unitId == 0 ? caravan.OriginId == settlementId || caravan.DestinationId == settlementId : caravan.Members.Exists(member => member.UnitId == unitId);
                if (!include) continue;
                text.Append("\n\n").Append(CaravanDescription(world, caravan)); count++;
            }
            if (count == 0) text.Append("\nРейсов пока нет");
            text.Append("\n\nСостоявшийся обмен:"); int trades = 0;
            if (world.CaravanExchanges != null) for (int i = world.CaravanExchanges.Count - 1; i >= 0 && trades < 3; i--)
            {
                CaravanExchangeData receipt = world.CaravanExchanges[i]; CaravanData caravan = world.GetCaravan(receipt.CaravanId);
                if (unitId != 0 ? receipt.UnitId != unitId : caravan == null || (caravan.OriginId != settlementId && caravan.DestinationId != settlementId)) continue;
                text.Append("\nТик ").Append(receipt.Tick).Append(" · ").Append(ResourceArt.ItemName(receipt.ExportItem)).Append(' ').Append(receipt.Amount.ToString("0.###")).Append(" → ")
                    .Append(ResourceArt.ItemName(receipt.ImportItem)).Append(' ').Append(receipt.Amount.ToString("0.###"));
                if (receipt.TaxAmount > 0) text.Append("\nНалог: ").Append(ResourceArt.ItemName(receipt.ExportItem)).Append(' ').Append(receipt.TaxAmount.ToString("0.###"));
                trades++;
            }
            if (trades == 0) text.Append(" ещё не было");
            return text.ToString();
        }
        public static string TreasuryDescription(WorldState world, int settlementId)
        {
            StringBuilder text = new StringBuilder("<b>КАЗНА • ТОВАРЫ НАЛОГА</b>"); bool any = false;
            for (int i = 1; i < ResourceCatalog.ItemCount; i++)
            {
                float stock = world.GetTreasuryStock(settlementId, (ItemType)i); if (stock <= 0) continue;
                text.Append('\n').Append(ResourceArt.ItemName((ItemType)i)).Append("  ").Append(stock.ToString("0.###")); any = true;
            }
            if (!any) text.Append("\nНалоговых товаров нет");
            return text.ToString();
        }
        public static string RecentTradesDescription(WorldState world, int settlementId, int unitId)
        {
            StringBuilder text = new StringBuilder("<b>ПОСЛЕДНИЕ ПЕРЕДАЧИ</b>"); int count = 0;
            if (world.Trades != null) for (int i = world.Trades.Count - 1; i >= 0 && count < 3; i--)
            {
                TradeRecord trade = world.Trades[i];
                if (unitId != 0) { if (trade.SellerId != unitId && trade.BuyerId != unitId) continue; }
                else { BuildingData building = world.GetBuilding(trade.BuildingId); if (building == null || building.SettlementId != settlementId) continue; }
                text.Append("\n\n").Append(TradeDescription(world, trade)); count++;
            }
            if (count == 0) text.Append("\nСостоявшихся передач нет");
            return text.ToString();
        }
        public static string TradeDescription(WorldState world, TradeRecord trade)
        {
            UnitData seller = world.GetUnit(trade.SellerId), buyer = world.GetUnit(trade.BuyerId);
            string parties = (seller != null ? seller.Name : "Житель #" + trade.SellerId) + (trade.Kind == TradeKind.Contribution ? " → производство" : " → " + (buyer != null ? buyer.Name : "Житель #" + trade.BuyerId));
            string result = (trade.Kind == TradeKind.Barter ? "Бартер" : trade.Kind == TradeKind.Gift ? "Дар" : "Вклад") + " · тик " + trade.Tick + "\n" + parties + "\n" + ResourceArt.ItemName(trade.Item) + "  " + trade.Amount.ToString("0.###");
            if (trade.Kind == TradeKind.Barter) result += "\nОплата: " + ResourceArt.ItemName(trade.PaymentItem) + "  " + trade.PaymentAmount.ToString("0.###") + "\nНалог: " + trade.TaxAmount.ToString("0.###") + " ед. оплаты";
            return result;
        }
        public static string NodeDescription(WorldState world, int x, int y)
        {
            ResourceNodeData node = world.GetResourceAt(x, y);
            if (node.Type == ResourceType.None) return "Нет добываемого ресурса";
            string state = "Запас   " + node.Amount.ToString("0.#") + " / " + node.Capacity.ToString("0.#");
            if (node.Amount <= 0) state += node.Type == ResourceType.Tree ? "\nСрублено • остался пень" : "\nМесторождение истощено";
            if (node.Amount <= 0 && node.RegrowTicksRemaining > 0)
            {
                TileData tile = world.Cells[world.Index(x, y)];
                string blocked = tile.BuildingId != 0 ? "Место занято постройкой" : !ResourceCatalog.IsCompatible(node.Type, tile) ? "Неподходящая местность" : tile.Fire > .01f ? "На участке пожар" : node.Type == ResourceType.Tree && !world.Laws.VegetationGrowth ? "Рост растений выключен" : null;
                string remaining = (node.RegrowTicksRemaining * SimulationEngine.TickSeconds).ToString("0.#") + " с";
                state += blocked == null ? "\nПополнение через " + remaining : "\nВосстановление остановлено\n" + blocked + "\nОсталось " + remaining;
            }
            return state;
        }
        public static string CargoDescription(UnitData unit)
        {
            string cargo = unit.CargoAmount > 0 && unit.CargoKind != ItemType.None ? ResourceArt.ItemName(unit.CargoKind) + "  " + unit.CargoAmount.ToString("0.###") : unit.CarriedFood > 0 || unit.CarriedWood > 0 || unit.HarvestAmount > 0 || unit.CarriedWater > 0 ? "" : "Нет груза";
            float total = SimulationEngine.CarriedLoad(unit);
            return "<b>НОША  " + total.ToString("0.###") + " / " + SimulationEngine.CarryCapacityFor(unit).ToString("0.#") + "</b>\n" + cargo + (unit.CarriedFood > 0 ? "\nЕда  " + unit.CarriedFood.ToString("0.###") : "") + (unit.CarriedWood > 0 ? "\nДоски  " + unit.CarriedWood.ToString("0.###") : "") + (unit.HarvestAmount > 0 ? "\n" + AgricultureArt.ProductName(unit.HarvestProduct) + "  " + unit.HarvestAmount.ToString("0.###") : "") + (unit.CarriedWater > 0 ? "\nВода  " + unit.CarriedWater.ToString("0.###") : "");
        }
        public static string EquipmentDescription(WorldState world, UnitData unit)
        {
            string text = "<b>СНАРЯЖЕНИЕ</b>\n" + (unit.BasketLevel == 0 ? "Без корзины" : "Корзина · уровень " + unit.BasketLevel);
            if (unit.WarWeapon != ItemType.None) text += "\nПоходное оружие: " + ResourceArt.ItemName(unit.WarWeapon);
            text += "\n" + ManufacturedEquipmentDescription(unit);
            int rod = FishingCatalog.RodLevel(unit);
            if (rod > 0) text += "\n" + FishingCatalog.RodName(rod) + " · грейд " + rod + "/5";
            FishDefinition lastCatch = FishingCatalog.Get(unit.FishSpeciesId);
            if (lastCatch != null) text += "\nПоследний улов: " + lastCatch.Name + " (" + FishRarityName(lastCatch.Rarity) + ")\nВсего поймано: " + unit.FishCaughtCount;
            if (unit.HuntingToolLevel > 0) text += "\nОхотничье копьё · уровень " + unit.HuntingToolLevel;
            UnitData mount = world.GetUnit(unit.MountId);
            if (mount != null && mount.Health > 0)
                text += "\n" + WildlifeArt.SpeciesName(mount.Species) + ": " + mount.Name + (WorldView.IsRiding(world, unit) ? " · верхом" : " · вьючное животное") + (unit.MountBags ? "\nВьючные сумки" : "") + (unit.HasWagon ? "\nЗапряжена телега" : "");
            else if (unit.OwnsWagon) text += "\nЕсть телега; не запряжена";
            return text + "\nОпьянение " + Percent(unit.Intoxication) + "\nПривычка к алкоголю " + Percent(unit.AlcoholHabit);
        }
        public static string FishRarityName(FishRarity rarity)
        {
            switch (rarity)
            {
                case FishRarity.Uncommon: return "необычная";
                case FishRarity.Rare: return "редкая";
                case FishRarity.Epic: return "эпическая";
                case FishRarity.Legendary: return "легендарная";
                default: return "обычная";
            }
        }
        public static string WildlifeDescription(WorldState world, UnitData unit)
        {
            SettlementData owner = world.GetSettlement(unit.AnimalOwnerId); UnitData handler = world.GetUnit(unit.HandlerId);
            string text = unit.IsDomestic ? "Домашнее животное" : "Дикое животное";
            if (owner != null) text += "\nПринадлежит: " + owner.Name;
            if (handler != null) text += "\nПогонщик: " + handler.Name;
            if (unit.PastureId != 0) text += "\nЗакреплено за пастбищем #" + unit.PastureId;
            if (unit.HasSaddleBags) text += "\nНадеты вьючные сумки";
            if (unit.PackId != 0) text += "\nСтая #" + unit.PackId + (unit.PackLeaderId == unit.Id ? " · вожак" : "");
            return text;
        }
        public static string AgricultureDescription(BuildingData building)
        {
            string result = "";
            if (building.Type == BuildingType.Farm) result = AgricultureArt.CropName(building.FarmCrop) + "\nРост " + Percent(building.CropGrowth) + "\nГотово к сбору " + building.HarvestReady.ToString("0.##");
            else if (building.Type == BuildingType.Pasture) result = "Пастбище" + (building.HerdSpecies >= 4 ? " · " + WildlifeArt.SpeciesName((SpeciesType)building.HerdSpecies) : "");
            else if (building.Type == BuildingType.Brewery) result = "Варка эля · " + building.BrewingProgress.ToString("0.#") + " с обработки\nВода для варки " + building.BrewWaterStored.ToString("0.###");
            if (building.AgriculturalStock != null) for (int i = 1; i < building.AgriculturalStock.Length; i++)
                if (building.AgriculturalStock[i] > .001f) result += "\n" + AgricultureArt.ProductName((AgriculturalProduct)i) + "  " + building.AgriculturalStock[i].ToString("0.###");
            return result;
        }
        private void AppendBuildingDescription(WorldState world, BuildingData building)
        {
            builder.Append(BuildingStatusDescription(world, building)).Append('\n');
            string maritime = MaritimeBuildingDescription(world, building); if (maritime.Length > 0) builder.Append(maritime).Append("\n\n");
            if (IndustryCatalog.IsWorkshop(building.Type)) builder.Append(ProductionDescription(world, building)).Append("\n\n");
            string access = BuildingAccessDescription(world, building); if (access.Length > 0) builder.Append(access).Append("\n\n");
            string agricultural = AgricultureDescription(building); if (agricultural.Length > 0) builder.Append(agricultural).Append('\n');
            UnitData worker = building.WorkerId != 0 ? world.GetUnit(building.WorkerId) : null;
            if (building.ActiveRecipe != RecipeType.None && worker != null && worker.Task == UnitTask.Process)
            {
                builder.Append(ResourceArt.RecipeName(building.ActiveRecipe));
                if (worker.PathCursor < worker.Path.Count) builder.Append(" • работник в пути");
                else builder.Append("\nОсталось ").Append(worker.WorkTimer.ToString("0.#")).Append(" с");
                builder.Append('\n');
            }
            else
            {
                bool job = false;
                for (int i = 0; i < world.Units.Count; i++)
                {
                    UnitData active = world.Units[i];
                    if (active.Health <= 0 || active.TargetBuildingId != building.Id || (active.Task != UnitTask.Brew && active.Task != UnitTask.CraftEquipment && active.Task != UnitTask.Tend)) continue;
                    builder.Append(active.Name).Append(": ").Append(TaskName(world, active)).Append('\n'); job = true; break;
                }
                if (!job && (building.Type == BuildingType.Hall || building.Type == BuildingType.Sawmill || building.Type == BuildingType.Stoneworks || building.Type == BuildingType.Kiln)) builder.Append("Нет активного рецепта материалов\n");
            }
            builder.Append("Общий склад:\n");
            if (building.Inventory != null) for (int i = 1; i < building.Inventory.Length; i++)
                if (building.Inventory[i] > 0) builder.Append(ResourceArt.ItemName((ItemType)i)).Append("  ").Append(building.Inventory[i].ToString("0.#")).Append('\n');
            if (building.PersonalStocks != null && building.PersonalStocks.Count > 0)
            {
                builder.Append("Личные партии:\n");
                for (int i = 0; i < building.PersonalStocks.Count; i++)
                {
                    OwnedGoodsData lot = building.PersonalStocks[i]; if (lot.Amount <= 0) continue;
                    UnitData owner = world.GetUnit(lot.OwnerId);
                    builder.Append(owner == null ? "Житель #" + lot.OwnerId : owner.Name).Append(": ").Append(ResourceArt.ItemName(lot.Item)).Append(' ').Append(lot.Amount.ToString("0.###")).Append('\n');
                }
            }
            bool taxHeader = false;
            if (building.TaxStocks != null) for (int i = 1; i < building.TaxStocks.Length; i++)
            {
                if (building.TaxStocks[i] <= 0) continue;
                if (!taxHeader) { builder.Append("Налоговый запас:\n"); taxHeader = true; }
                builder.Append(ResourceArt.ItemName((ItemType)i)).Append(' ').Append(building.TaxStocks[i].ToString("0.###")).Append('\n');
            }
            builder.Append('\n');
        }
        private void AppendSettlementStocks(WorldState world, int settlementId)
        {
            for (int i = 1; i < ResourceCatalog.ItemCount; i++)
            {
                ItemType item = (ItemType)i; float stock = world.GetSettlementStock(settlementId, item);
                if (stock > 0 || i <= (int)ItemType.Brick) builder.Append('\n').Append(ResourceArt.ItemName(item)).Append("  ").Append(stock.ToString("0.#"));
            }
            for (int i = 1; i < FishingCatalog.AgriculturalProductCount; i++)
            {
                float amount = world.GetAgriculturalStock(settlementId, (AgriculturalProduct)i);
                if (amount > .001f) builder.Append('\n').Append(AgricultureArt.ProductName((AgriculturalProduct)i)).Append("  ").Append(amount.ToString("0.##"));
            }
            builder.Append("\n\n<b>ПО ЦЕЛИ ЗАПАСОВ</b>");
            bool needs = false, surplus = false;
            for (int i = 1; i < ResourceCatalog.ItemCount; i++)
            {
                ItemType item = (ItemType)i; ResourceBalance balance = world.GetResourceBalance(settlementId, item);
                if (balance.Deficit <= .01f) continue;
                if (!needs) { builder.Append("\nНуждается:"); needs = true; }
                builder.Append('\n').Append(ResourceArt.ItemName(item)).Append("  ").Append(balance.Deficit.ToString("0.#"));
            }
            if (!needs) builder.Append("\nДефицита по цели нет");
            for (int i = 1; i < ResourceCatalog.ItemCount; i++)
            {
                ItemType item = (ItemType)i; ResourceBalance balance = world.GetResourceBalance(settlementId, item);
                if (balance.Surplus <= .01f) continue;
                if (!surplus) { builder.Append("\n\nИзбыток:"); surplus = true; }
                builder.Append('\n').Append(ResourceArt.ItemName(item)).Append("  ").Append(balance.Surplus.ToString("0.#"));
            }
        }
        public static string TaskName(WorldState world, UnitData unit)
        {
            bool moving = unit.PathCursor < unit.Path.Count;
            switch (unit.Task)
            {
                case UnitTask.Idle: return "Осматривается";
                case UnitTask.Settle: return unit.ExpeditionResting ? "Переселенец: отдыхает в пути" : moving ? "Переселенец: идёт к новому поселению" : "Переселенец: собирает припасы / строит костёр";
                case UnitTask.WarMarch: return unit.WarReturning ? "Ополченец: возвращается и сдаёт оружие" : unit.WarWeapon == ItemType.None ? "Ополченец: получает оружие" : moving ? "Вооружённый поход" : "Удерживает центр противника";
                case UnitTask.Wander: return "Исследует окрестности";
                case UnitTask.Gather:
                    ResourceType resource = unit.ResourceCell >= 0 && unit.ResourceCell < world.Cells.Length ? world.GetResourceAt(unit.ResourceCell % world.Width, unit.ResourceCell / world.Width).Type : ResourceType.None;
                    return moving ? "Идёт к ресурсу" : resource == ResourceType.Tree ? "Рубит дерево" : resource != ResourceType.None ? "Добывает: " + ResourceArt.ResourceName(resource) : "Ищет ресурс";
                case UnitTask.Farm: return moving ? "Идёт к полю" : "Работает в поле";
                case UnitTask.Return: return "Доставляет ресурсы";
                case UnitTask.Haul: return unit.CargoAmount > 0 ? "Переносит: " + ResourceArt.ItemName(unit.CargoKind) : "Идёт за грузом";
                case UnitTask.Process: return moving ? "Идёт в мастерскую" : "Обрабатывает материал";
                case UnitTask.Produce:
                    BuildingData workplace = world.GetBuilding(unit.TargetBuildingId);
                    return workplace == null ? "Ожидает мастерскую" : IndustryCatalog.IsWorking(world, workplace) ? "Изготавливает: " + IndustryCatalog.Name(workplace.ActiveIndustryRecipe) : "Снабжает: " + ResourceArt.BuildingName(workplace.Type);
                case UnitTask.InstallFurniture: return "Доставляет и устанавливает мебель";
                case UnitTask.Shipbuild: return MaritimeArt.IsShipwrightWorking(world, unit) ? "Строит корабль" : "Снабжает верфь";
                case UnitTask.Dockwork: return moving ? "Идёт к причалу" : "Исполняет портовый заказ";
                case UnitTask.Sail: return "На борту корабля #" + unit.ShipId;
                case UnitTask.EquipGoods: return "Получает изготовленное снаряжение";
                case UnitTask.VisitBuilding: return unit.InsideBuildingId != 0 ? "Находится в здании" : unit.EnteringBuildingId != 0 ? "Входит через дверь" : "Идёт ко входу";
                case UnitTask.Fight: return (int)unit.Species < 4 ? "Ополчение: защита" : moving ? "Преследует противника" : "В бою";
                case UnitTask.Flee: return "Спасается от опасности";
                case UnitTask.Eat: return moving ? "Идёт за едой" : "Ест";
                case UnitTask.Rest: return moving ? "Ищет место для отдыха" : "Восстанавливает силы";
                case UnitTask.Socialize: return moving ? "Идёт к собеседнику" : "Общается";
                case UnitTask.Patrol: return "Охраняет поселение";
                case UnitTask.Play: return moving ? "Идёт играть" : "Играет";
                case UnitTask.Learn: return moving ? "Идёт на занятие" : "Учится у взрослых";
                case UnitTask.Drink: return moving ? "Идёт к питьевой воде" : "Пьёт воду";
                case UnitTask.Fish: return moving ? "Идёт к месту ловли" : "Ловит рыбу";
                case UnitTask.Hunt: return moving ? "Выслеживает добычу" : "Охотится";
                case UnitTask.Tend: return moving ? "Идёт к стаду" : "Ухаживает за животными";
                case UnitTask.BuildTransport:
                    if (world.TransportProjects != null) foreach (TransportProjectData project in world.TransportProjects)
                    {
                        if (project.Id != unit.TransportProjectId) continue;
                        string site = project.Bridge ? "моста" : "дороги";
                        if (unit.CargoAmount > 0) return "Доставляет материал для " + site;
                        return moving || project.Delivered + .00001f < project.Required ? "Снабжает стройку " + site : "Строит " + (project.Bridge ? "мост" : "дорогу");
                    }
                    return "Ожидает строительное задание";
                case UnitTask.Brew: return unit.CarriedWater > .001f ? "Доставляет воду в пивоварню" : moving || unit.WorkTimer <= 0 || SimulationEngine.CarriedLoad(unit) > .001f ? "Снабжает пивоварню" : "Варит эль";
                case UnitTask.CraftEquipment: return moving || unit.EquipmentProgress <= 0 || SimulationEngine.CarriedLoad(unit) > .001f ? "Снабжает мастерскую" : "Изготавливает снаряжение";
                case UnitTask.Caravan:
                    CaravanData caravan = world.GetCaravan(unit.CaravanId);
                    if (caravan != null) for (int i = 0; i < caravan.Members.Count; i++) if (caravan.Members[i].UnitId == unit.Id) return "Караван: " + MemberPhaseName(caravan.Members[i].Phase);
                    return moving ? "Идёт с караваном" : "Ожидает караван";
                default: return "Наблюдает";
            }
        }
        private Sprite IconSprite(int index)
        {
            if (iconSprites.TryGetValue(index, out Sprite sprite)) return sprite;
            if (index >= 2000) { sprite = game.View.CreateWeatherIcon((WeatherKind)(index - 2000)); iconSprites.Add(index, sprite); return sprite; }
            if (index >= 1000) { sprite = game.View.CreateAnimalIcon((SpeciesType)(index - 1000)); iconSprites.Add(index, sprite); return sprite; }
            if (index >= 6 && index <= 9) { sprite = game.View.CreateCitizenIcon((SpeciesType)(index - 6)); iconSprites.Add(index, sprite); return sprite; }
            Texture2D atlas = game.View.SpriteMaterial.mainTexture as Texture2D; Rect uv = PixelArt.Uv(index);
            sprite = Sprite.Create(atlas, new Rect(uv.x * atlas.width, uv.y * atlas.height, uv.width * atlas.width, uv.height * atlas.height), new Vector2(.5f, .5f), 32);
            iconSprites.Add(index, sprite); return sprite;
        }
        private static int ToolIcon(WorldTool tool)
        {
            if (tool.Kind == ToolKind.Creature) return tool.Value >= 6 ? 1000 + tool.Value : 6 + tool.Value;
            if (tool.Kind == ToolKind.Power) return 21 + tool.Value;
            if (tool.Kind == ToolKind.Law) return 28;
            if (tool.Kind == ToolKind.Terrain) { int[] glyphs = { 15, 16, 17, 18, 0, 19, 20, 13, 122, 123, 124, 125 }; return glyphs[tool.Value]; }
            return -1;
        }
        private RectTransform PanelFixed(Transform parent, string name, Vector2 position, Vector2 size, Color color, bool right = false)
        { RectTransform rect = Panel(parent, name, right ? Vector2.one : new Vector2(0, 1), right ? Vector2.one : new Vector2(0, 1), Vector2.zero, Vector2.zero, color); Position(rect, position, size, right); return rect; }
        private RectTransform Panel(Transform parent, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax, Color color)
        { GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image)); obj.transform.SetParent(parent, false); RectTransform rect = obj.GetComponent<RectTransform>(); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; obj.GetComponent<Image>().color = color; return rect; }
        private static void Position(RectTransform rect, Vector2 position, Vector2 size, bool right = false)
        { rect.anchorMin = right ? Vector2.one : new Vector2(0, 1); rect.anchorMax = rect.anchorMin; rect.pivot = right ? Vector2.one : new Vector2(0, 1); rect.anchoredPosition = position; rect.sizeDelta = size; }
        private Text Label(Transform parent, string value, int size, Color color, Vector2 position, Vector2 dimensions, bool right = false)
        {
            GameObject obj = new GameObject("Text", typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false); Text text = obj.GetComponent<Text>(); text.font = font; text.fontSize = size; text.color = color; text.text = value; text.raycastTarget = false; text.supportRichText = true; text.alignment = TextAnchor.UpperLeft; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; Position(text.rectTransform, position, dimensions, right); return text;
        }
        private Button ButtonAt(Transform parent, string title, Vector2 position, Vector2 size, Action action, bool right = false)
        {
            RectTransform rect = PanelFixed(parent, title, position, size, surface, right); Button button = rect.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.22f, 1.22f, 1.16f); colors.pressedColor = PixelArt.Teal; colors.selectedColor = Color.white; colors.fadeDuration = .08f; button.colors = colors; button.onClick.AddListener(() => action());
            Text label = Label(rect, title, 13, white, Vector2.zero, size); label.alignment = TextAnchor.MiddleCenter; label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one; label.rectTransform.offsetMin = new Vector2(4, 2); label.rectTransform.offsetMax = new Vector2(-4, -2); return button;
        }
        private InputField Input(Transform parent, Vector2 position, Vector2 size, string value)
        {
            RectTransform rect = PanelFixed(parent, "Seed", position, size, PixelArt.Hex("0f1d27")); InputField field = rect.gameObject.AddComponent<InputField>();
            Text text = Label(rect, value, 14, white, new Vector2(8, -5), size - new Vector2(16, 7)); field.textComponent = text; field.contentType = InputField.ContentType.IntegerNumber; field.characterLimit = 11; field.text = value; return field;
        }
        public void Dispose() { foreach (Sprite sprite in iconSprites.Values) UnityEngine.Object.Destroy(sprite); iconSprites.Clear(); UnityEngine.Object.Destroy(root); }
    }

    public sealed class MinimapInput : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public GameBootstrap Game;
        public void OnPointerDown(PointerEventData data) { Jump(data); }
        public void OnDrag(PointerEventData data) { Jump(data); }
        private void Jump(PointerEventData data)
        {
            RectTransform rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.pressEventCamera, out Vector2 local)) return;
            Vector2 uv = new Vector2((local.x - rect.rect.xMin) / rect.rect.width, (local.y - rect.rect.yMin) / rect.rect.height);
            Game.MoveCamera(new Vector2(Mathf.Clamp01(uv.x) * Game.World.Width, Mathf.Clamp01(uv.y) * Game.World.Height));
        }
    }
}
