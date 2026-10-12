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

    public sealed class GameHud : IDisposable
    {
        private readonly GameBootstrap game;
        private readonly GameObject root;
        private readonly Font font;
        private readonly Color panel = PixelArt.Hex("152632"), surface = PixelArt.Hex("223745"), muted = PixelArt.Hex("92abb5"), white = PixelArt.Hex("edf0df");
        private Text stats, inspectorTitle, inspectorBody, chronicle, status, toolHeading, radiusLabel, pauseLabel, clockLabel;
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
        private Text presetLabel;
        public bool IsTextFocused => seed != null && seed.isFocused;

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
            float[] speeds = { 1, 2, 5, 10 };
            for (int i = 0; i < speeds.Length; i++) { float speed = speeds[i]; speedButtons.Add(ButtonAt(header, speed + "×", new Vector2(713 + i * 45, -24), new Vector2(39, 39), () => { game.Speed = speed; game.Paused = false; })); }
            clockLabel = Label(header, "", 13, PixelArt.Teal, new Vector2(905, -25), new Vector2(190, 34));
            ButtonAt(header, "Сохранить", new Vector2(-248, -24), new Vector2(104, 39), game.Save, true);
            ButtonAt(header, "Загрузить", new Vector2(-134, -24), new Vector2(108, 39), game.Load, true);

            RectTransform left = PanelFixed(root.transform, "World information", new Vector2(18, -95), new Vector2(232, 470), panel);
            Label(left, "АТЛАС МИРА", 12, PixelArt.Gold, new Vector2(17, -16), new Vector2(196, 24)).fontStyle = FontStyle.Bold;
            stats = Label(left, "", 14, white, new Vector2(17, -47), new Vector2(203, 82));
            minimap = new GameObject("Minimap • actual terrain", typeof(RectTransform), typeof(RawImage), typeof(MinimapInput)).GetComponent<RawImage>(); minimap.transform.SetParent(left, false);
            Position(minimap.rectTransform, new Vector2(17, -142), new Vector2(198, 198)); minimap.texture = game.View.Minimap;
            MinimapInput minimapInput = minimap.GetComponent<MinimapInput>(); minimapInput.Game = game;
            Label(left, "Нажмите на карту, чтобы переместиться", 10, muted, new Vector2(17, -347), new Vector2(205, 26));
            ButtonAt(left, "Границы поселений", new Vector2(17, -383), new Vector2(198, 29), () => { game.View.ShowBorders = !game.View.ShowBorders; game.View.RefreshTerritories(); ShowToast(game.View.ShowBorders ? "Границы видны" : "Границы скрыты"); });
            presetLabel = ButtonAt(left, "", new Vector2(17, -427), new Vector2(198, 29), () => { game.GenerationPreset = (WorldPreset)(((int)game.GenerationPreset + 1) % 3); RefreshPreset(); ShowToast("Выбрана форма следующего мира. Нажмите «Новый мир»."); }).GetComponentInChildren<Text>();
            RefreshPreset();

            RectTransform right = PanelFixed(root.transform, "Inspector", new Vector2(-18, -95), new Vector2(236, 366), panel, true);
            Label(right, "НАБЛЮДЕНИЕ", 12, PixelArt.Gold, new Vector2(17, -16), new Vector2(202, 24)).fontStyle = FontStyle.Bold;
            inspectorTitle = Label(right, "Живой мир", 23, white, new Vector2(17, -49), new Vector2(202, 48));
            inspectorBody = Label(right, "", 13, muted, new Vector2(17, -101), new Vector2(202, 251));
            RectTransform history = PanelFixed(root.transform, "Chronicle", new Vector2(-18, -478), new Vector2(236, 192), panel, true);
            Label(history, "ЛЕТОПИСЬ", 12, PixelArt.Gold, new Vector2(17, -16), new Vector2(202, 24)).fontStyle = FontStyle.Bold;
            chronicle = Label(history, "", 12, muted, new Vector2(17, -51), new Vector2(202, 127));

            RectTransform footer = Panel(root.transform, "Creation tools", Vector2.zero, new Vector2(1, 0), new Vector2(18, 18), new Vector2(-18, 174), panel);
            string[] categories = { "ЛАНДШАФТ", "ЖИЗНЬ", "СИЛЫ БОГА", "ЗАКОНЫ" };
            for (int i = 0; i < categories.Length; i++) { int category = i; categoryButtons.Add(ButtonAt(footer, categories[i], new Vector2(14 + i * 132, -12), new Vector2(124, 33), () => SelectCategory(category))); }
            ButtonAt(footer, "Наблюдать  [I]", new Vector2(557, -12), new Vector2(142, 33), () => SelectInspect());
            toolHeading = Label(footer, "", 12, muted, new Vector2(717, -14), new Vector2(370, 27));
            radiusLabel = Label(footer, "", 12, white, new Vector2(-253, -17), new Vector2(161, 25), true);
            ButtonAt(footer, "−", new Vector2(-82, -12), new Vector2(28, 31), () => game.BrushRadius = Mathf.Max(0, game.BrushRadius - 1), true);
            ButtonAt(footer, "+", new Vector2(-45, -12), new Vector2(28, 31), () => game.BrushRadius = Mathf.Min(12, game.BrushRadius + 1), true);
            toolRow = new GameObject("Tool palette", typeof(RectTransform)).GetComponent<RectTransform>(); toolRow.SetParent(footer, false);
            toolRow.anchorMin = new Vector2(0, 0); toolRow.anchorMax = new Vector2(1, 1); toolRow.offsetMin = new Vector2(14, 12); toolRow.offsetMax = new Vector2(-14, -55);
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

        private void SelectCategory(int category)
        {
            selectedCategory = category;
            foreach (Button button in toolButtons) UnityEngine.Object.Destroy(button.gameObject);
            toolButtons.Clear(); currentTools.Clear();
            if (category == 0)
            {
                string[] names = { "Глубокая вода", "Мелководье", "Песок", "Луга", "Лес", "Пустыня", "Снег", "Горы" };
                foreach (TerrainType terrain in Enum.GetValues(typeof(TerrainType))) currentTools.Add(new WorldTool(names[(int)terrain], "ЛКМ и движение мыши — рисовать. Кисть меняет реальные клетки мира.", ToolKind.Terrain, (int)terrain, PixelArt.Terrain(terrain)));
            }
            else if (category == 1)
            {
                string[] names = { "Люди", "Эльфы", "Гномы", "Орки", "Овцы", "Волки" };
                foreach (SpeciesType species in Enum.GetValues(typeof(SpeciesType))) currentTools.Add(new WorldTool(names[(int)species], (int)species < 4 ? "Разместите жителей на суше. Они основывают поселения, собирают ресурсы и строят дома." : "Разместите животных на суше. Их жизнь подчиняется правилам симуляции.", ToolKind.Creature, (int)species, PixelArt.Factions[(int)species % PixelArt.Factions.Length]));
            }
            else if (category == 2)
            {
                string[] names = { "Огонь", "Дождь", "Молния", "Метеор", "Исцеление", "Благословение", "Чума" };
                string[] descriptions = { "Зажечь растительность. Огонь распространяется и наносит урон.", "Погасить огонь и напоить землю.", "Удар молнии наносит урон в выбранной области.", "Разрушительный удар меняет землю и жизнь вокруг.", "Восстановить здоровье существ в радиусе кисти.", "Благословить жителей в выбранной области.", "Заразить существ. Последствия развиваются в симуляции." };
                Color[] colors = { PixelArt.Hex("ed8d54"), PixelArt.Hex("85cbdc"), PixelArt.Hex("d2dfef"), PixelArt.Hex("c17752"), PixelArt.Teal, PixelArt.Gold, PixelArt.Hex("9fbb73") };
                foreach (PowerType power in Enum.GetValues(typeof(PowerType))) currentTools.Add(new WorldTool(names[(int)power], descriptions[(int)power], ToolKind.Power, (int)power, colors[(int)power]));
            }
            else
            {
                string[] names = { "Рождение", "Войны", "Старение", "Рост растений", "Распростр. огня" };
                foreach (WorldLaw law in Enum.GetValues(typeof(WorldLaw))) currentTools.Add(new WorldTool(names[(int)law], "Законы управляют настоящими системами мира и сохраняются вместе с ним.", ToolKind.Law, (int)law, PixelArt.Teal));
            }
            for (int i = 0; i < currentTools.Count; i++)
            {
                WorldTool tool = currentTools[i];
                Button button = ButtonAt(toolRow, tool.Label, new Vector2(i * 139, 0), new Vector2(130, 79), () => { if (tool.Kind == ToolKind.Law) { WorldLaw law = (WorldLaw)tool.Value; game.Engine.SetLaw(law, !ReadLaw(law)); ShowToast(tool.Label + (ReadLaw(law) ? ": включено" : ": выключено")); } else game.Tool = tool; RefreshSelection(); });
                Text text = button.GetComponentInChildren<Text>(); text.fontSize = 13; text.alignment = TextAnchor.LowerCenter; text.rectTransform.offsetMin = new Vector2(3, 11); text.rectTransform.offsetMax = new Vector2(-3, -7);
                Image swatch = new GameObject("Symbol", typeof(RectTransform), typeof(Image)).GetComponent<Image>(); swatch.transform.SetParent(button.transform, false); swatch.raycastTarget = false; swatch.color = tool.Color;
                Position(swatch.rectTransform, new Vector2(54, -14), new Vector2(22, 22));
                int icon = ToolIcon(tool);
                if (icon >= 0) { swatch.sprite = IconSprite(icon); swatch.color = Color.white; swatch.rectTransform.sizeDelta = new Vector2(39, 39); swatch.rectTransform.anchoredPosition = new Vector2(46, -5); }
                toolButtons.Add(button);
            }
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            for (int i = 0; i < toolButtons.Count; i++)
            {
                bool active = currentTools[i].Kind == ToolKind.Law ? ReadLaw((WorldLaw)currentTools[i].Value) : ReferenceEquals(game.Tool, currentTools[i]);
                toolButtons[i].GetComponent<Image>().color = active ? PixelArt.Hex("3b635f") : surface;
                if (currentTools[i].Kind == ToolKind.Law) toolButtons[i].GetComponentInChildren<Text>().text = currentTools[i].Label + (active ? "  ●" : "  ○");
            }
            for (int i = 0; i < categoryButtons.Count; i++) categoryButtons[i].GetComponent<Image>().color = i == selectedCategory ? PixelArt.Hex("3a514e") : surface;
            if (game.Tool != null) toolHeading.text = game.Tool.Label + "  ·  " + (game.Tool.Kind == ToolKind.Inspect ? "исследуйте свой мир" : "ЛКМ — применить");
        }

        public void ShowToast(string message) { toast = message; toastUntil = Time.unscaledTime + 6; }
        public void WorldChanged() { seed.text = game.World.Seed.ToString(); minimap.texture = game.View.Minimap; RefreshPreset(); RefreshSelection(); Refresh(true); }
        public void Refresh(bool force = false)
        {
            if (!force && Time.unscaledTime < nextRefresh) return; nextRefresh = Time.unscaledTime + .2f;
            WorldState world = game.World;
            stats.text = "Жителей и животных   <b>" + world.Units.Count + "</b>\nПоселений                    <b>" + world.Settlements.Count + "</b>\nПостроек                      <b>" + world.Buildings.Count + "</b>";
            clockLabel.text = (game.Paused ? "МИР НА ПАУЗЕ" : "МИР ЖИВЁТ") + "\nТик " + world.Tick.ToString("N0");
            if (game.SimulationBacklogSeconds > 1) clockLabel.text = "ОБРАБОТКА ТИКОВ\nОчередь " + game.SimulationBacklogSeconds.ToString("0.0") + " с";
            pauseLabel.text = game.Paused ? "▶" : "II";
            float[] speeds = { 1, 2, 5, 10 }; for (int i = 0; i < speeds.Length; i++) speedButtons[i].GetComponent<Image>().color = game.Speed == speeds[i] && !game.Paused ? PixelArt.Hex("3b635f") : surface;
            radiusLabel.text = "Радиус: " + game.BrushRadius + "  [ / ]";
            if (game.Tool != null && game.Tool.Kind == ToolKind.Power)
            {
                radiusLabel.fontSize = 10;
                radiusLabel.text = (game.ToolAffectsTerrain ? "Земля: " + game.EffectiveBrushRadius : "") + (game.UnitImpactRadius >= 0 ? (game.ToolAffectsTerrain ? "  /  " : "") + "Жители: " + game.UnitImpactRadius : "") + "  [ / ]";
            }
            else radiusLabel.fontSize = 12;
            UnitData selected = null;
            for (int i = 0; i < world.Units.Count; i++) if (world.Units[i].Id == game.SelectedUnitId) { selected = world.Units[i]; break; }
            if (selected != null)
            {
                inspectorTitle.text = selected.Name;
                string[] species = { "Человек", "Эльф", "Гном", "Орк", "Овца", "Волк" };
                SettlementData city = world.GetSettlement(selected.SettlementId);
                inspectorBody.text = species[(int)selected.Species] + "  ·  " + ProfessionName(selected) + "\nЗдоровье   " + selected.Health.ToString("0") + " / " + selected.MaxHealth.ToString("0") + "\nВозраст     " + selected.Age.ToString("0.0") + "\n\n<b>" + TaskName(selected) + "</b>\nГолод           " + (selected.Hunger * 100).ToString("0") + "%\nУсталость    " + (selected.Fatigue * 100).ToString("0") + "%" + ((int)selected.Species < 4 ? "\nОбщение     " + (selected.SocialNeed * 100).ToString("0") + "% потребн." : "") + "\nНоша: еда " + selected.CarriedFood.ToString("0.0") + ", дерево " + selected.CarriedWood.ToString("0.0") + "\n\n" + (city == null ? "Вне поселения" : city.Name) + (city != null && city.RulerId == selected.Id ? "\n<b>" + RulerTitle(city.Government) + "</b>" : "") + (selected.Blessed ? "\nБлагословлён" : "") + (selected.Infected ? "\nЗаражён" : "");
            }
            else if (world.InBounds(game.InspectX, game.InspectY))
            {
                TileData tile = world.Cells[world.Index(game.InspectX, game.InspectY)]; SettlementData city = world.GetSettlement(tile.SettlementId);
                if (city != null)
                {
                    inspectorTitle.text = city.Name;
                    UnitData ruler = world.GetUnit(city.RulerId);
                    inspectorBody.text = "<b>" + GovernmentName(city.Government) + "</b>\n" +
                        (ruler == null ? "Нет взрослого правителя" : RulerTitle(city.Government) + ": " + ruler.Name) +
                        (city.Government == GovernmentType.Republic ? "\nВыборы через " + Mathf.Max(0, (city.NextElectionTick - world.Tick) * SimulationEngine.TickSeconds).ToString("0") + " с" : "") +
                        "\n\nНаселение   " + city.Population + "\nЖильё          " + city.Housing + "\nПостройки    " + city.Buildings +
                        "\n\nЕда       " + city.Food.ToString("0") + "\nДерево  " + city.Wood.ToString("0") + "\nКамень  " + city.Stone.ToString("0");
                }
                else { inspectorTitle.text = TerrainName(tile.Terrain); inspectorBody.text = "Участок " + game.InspectX + ", " + game.InspectY + "\n\nВысота         " + tile.Height.ToString("0.00") + "\nВлажность   " + tile.Moisture.ToString("0.00") + "\nТемпература " + tile.Temperature.ToString("0.00") + "\nРастительность " + tile.Vegetation.ToString("0.00") + (tile.Fire > 0 ? "\n\nНа этом участке пожар" : "") + (tile.River ? "\nЗдесь проходит река" : ""); }
            }
            else { inspectorTitle.text = game.Tool?.Label ?? "Живой мир"; inspectorBody.text = (game.Tool?.Description ?? "") + "\n\nI — наблюдение\nПробел — пауза\nHome — показать весь мир\nF12 — снимок игры"; }
            builder.Clear(); int count = 0;
            for (int i = world.Events.Count - 1; i >= 0 && count < 5; i--)
            {
                WorldEvent item = world.Events[i]; if (string.IsNullOrEmpty(item.Text)) continue;
                if (builder.Length > 0) builder.Append("\n\n"); builder.Append("<color=#d5b36d>•</color> ").Append(item.Text); count++;
            }
            chronicle.text = builder.Length == 0 ? "Каждый мир начинается с тишины.\n\nПоселите жителей, и здесь появятся первые события." : builder.ToString();
            status.text = Time.unscaledTime < toastUntil ? toast : "WASD / средняя кнопка — камера     Колесо — масштаб     Пробел — пауза     Ctrl+S / Ctrl+L — сохранение";
        }
        private static string TerrainName(TerrainType value) { string[] names = { "Океан", "Мелководье", "Песчаный берег", "Луг", "Лес", "Пустыня", "Снежные земли", "Горы" }; return names[(int)value]; }
        private void RefreshPreset() { if (presetLabel != null) { string[] names = { "Остров", "Архипелаг", "Континент" }; presetLabel.text = "Форма: " + names[(int)game.GenerationPreset] + "  ›"; } }
        private bool ReadLaw(WorldLaw law)
        {
            switch (law) { case WorldLaw.Births: return game.World.Laws.Births; case WorldLaw.Warfare: return game.World.Laws.Warfare; case WorldLaw.Aging: return game.World.Laws.Aging; case WorldLaw.VegetationGrowth: return game.World.Laws.VegetationGrowth; case WorldLaw.FireSpread: return game.World.Laws.FireSpread; default: return false; }
        }
        private static string GovernmentName(GovernmentType government)
        {
            switch (government) { case GovernmentType.Feudal: return "Феодальное правление"; case GovernmentType.Republic: return "Республика"; case GovernmentType.Theocracy: return "Теократия"; default: return "Поселение формируется"; }
        }
        private static string RulerTitle(GovernmentType government)
        {
            switch (government) { case GovernmentType.Feudal: return "Князь"; case GovernmentType.Republic: return "Консул"; case GovernmentType.Theocracy: return "Верховный жрец"; default: return "Правитель"; }
        }
        private static string ProfessionName(UnitData unit)
        {
            if ((int)unit.Species >= 4) return "животное";
            if (unit.Age < 16) return "ребёнок";
            switch (unit.Profession) { case UnitProfession.Farmer: return "земледелец"; case UnitProfession.Forester: return "лесоруб"; case UnitProfession.Guard: return "страж"; default: return "житель"; }
        }
        private static string TaskName(UnitData unit)
        {
            bool moving = unit.PathCursor < unit.Path.Count;
            switch (unit.Task)
            {
                case UnitTask.Idle: return "Осматривается";
                case UnitTask.Wander: return "Исследует окрестности";
                case UnitTask.Gather: return moving ? "Идёт за древесиной" : "Заготавливает древесину";
                case UnitTask.Farm: return moving ? "Идёт к полю" : "Работает в поле";
                case UnitTask.Return: return "Доставляет ресурсы";
                case UnitTask.Fight: return (int)unit.Species < 4 ? "Ополчение: защита" : moving ? "Преследует противника" : "В бою";
                case UnitTask.Flee: return "Спасается от опасности";
                case UnitTask.Eat: return moving ? "Идёт за едой" : "Ест";
                case UnitTask.Rest: return moving ? "Ищет место для отдыха" : "Восстанавливает силы";
                case UnitTask.Socialize: return moving ? "Идёт к собеседнику" : "Общается";
                case UnitTask.Patrol: return "Охраняет поселение";
                default: return "Наблюдает";
            }
        }
        private Sprite IconSprite(int index)
        {
            if (iconSprites.TryGetValue(index, out Sprite sprite)) return sprite;
            if (index == 6) { sprite = game.View.CreateHumanIcon(); iconSprites.Add(index, sprite); return sprite; }
            Texture2D atlas = game.View.SpriteMaterial.mainTexture as Texture2D; Rect uv = PixelArt.Uv(index);
            sprite = Sprite.Create(atlas, new Rect(uv.x * atlas.width, uv.y * atlas.height, uv.width * atlas.width, uv.height * atlas.height), new Vector2(.5f, .5f), 32);
            iconSprites.Add(index, sprite); return sprite;
        }
        private static int ToolIcon(WorldTool tool)
        {
            if (tool.Kind == ToolKind.Creature) return 6 + tool.Value;
            if (tool.Kind == ToolKind.Power) return 21 + tool.Value;
            if (tool.Kind == ToolKind.Law) return 28;
            if (tool.Kind == ToolKind.Terrain) { int[] glyphs = { 15, 16, 17, 18, 0, 19, 20, 13 }; return glyphs[tool.Value]; }
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
