using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    /// <summary>Opt-in, isolated native presentation fixture. Never replaces or saves a gameplay world.</summary>
    public sealed class NativeCitizenGallery : MonoBehaviour
    {
        [Serializable] private sealed class Subject
        {
            public int id;
            public string race, appearance, profession, task, direction, label;
            public bool preparedMovingPath, preparedCargo, preparedAttackTimer, preparedTransportProject;
        }
        [Serializable] private sealed class Sample
        {
            public int index;
            public double wallSeconds;
            public long tick;
            public int[] atlasFrames, toolFrames, directions;
            public string[] actions;
            public bool[] derivedGestures;
        }
        [Serializable] private sealed class Receipt
        {
            public string kind = "Isolated native Unity engine preview; prepared subjects, not gameplay or natural progression";
            public string renderPath = "Production CitizenSprites.Begin -> Add -> Submit; no gallery sprite replacement";
            public string clock = "Empty SimulationEngine.Step(0.1) at real time; its Tick is copied to a separate presentation-only WorldState";
            public string limitation = "Walk in place. Paths, cargo, work and attack timers are prepared fixtures. Derived gestures are not new authored clips. No simulation/save/performance/artistic acceptance claim.";
            public string startedUtc, completedUtc, mode, sex, unityVersion;
            public int page, width, height, atlasWidth, atlasHeight, seed;
            public bool gameplay = false, imageGeneration = false, artisticAcceptance = false;
            public List<Subject> subjects = new List<Subject>();
            public List<Sample> frames = new List<Sample>();
        }
        private sealed class Entry
        {
            public UnitData Unit;
            public Vector2 Direction;
            public bool Moving, Attack;
        }

        private static readonly SpeciesType[] Races = { SpeciesType.Human, SpeciesType.Elf, SpeciesType.Dwarf, SpeciesType.Orc };
        private static readonly string[] RaceNames = { "ЛЮДИ", "ЭЛЬФЫ", "ГНОМЫ", "ОРКИ" };
        private static readonly UnitProfession[] Professions = { UnitProfession.None, UnitProfession.Farmer, UnitProfession.Forester, UnitProfession.Hunter, UnitProfession.Herder, UnitProfession.Merchant, UnitProfession.Carpenter };
        private static readonly string[] ProfessionNames = { "Житель", "Земледелец", "Лесоруб", "Охотник", "Животновод", "Торговец", "Плотник" };
        private static readonly string[] Directions = { "Юг", "Запад", "Восток", "Север" };
        private static readonly UnitTask[] ActionTasks = {
            UnitTask.Idle, UnitTask.Wander, UnitTask.Return, UnitTask.Gather, UnitTask.Gather, UnitTask.Farm, UnitTask.Process, UnitTask.Fight,
            UnitTask.Flee, UnitTask.Eat, UnitTask.Rest, UnitTask.Socialize, UnitTask.Play, UnitTask.Learn, UnitTask.Drink, UnitTask.Fish,
            UnitTask.Tend, UnitTask.Brew, UnitTask.CraftEquipment, UnitTask.BuildTransport, UnitTask.Haul, UnitTask.Caravan, UnitTask.Hunt, UnitTask.Patrol };
        private static readonly string[] ActionNames = {
            "Покой", "Ходьба", "С грузом", "Рубка", "Добыча", "Поле", "Обработка", "Выпад*",
            "Бегство", "Еда", "Отдых", "Общение", "Игра ребёнка", "Учёба ребёнка", "Питьё", "Рыбалка",
            "Уход за стадом", "Пивоварение", "Ремесло", "Стройка", "Переноска", "Караван", "Путь охотника", "Обход" };

        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<GameObject> panels = new List<GameObject>();
        private CitizenSprites citizens;
        private SimulationEngine clock;
        private WorldState preview;
        private Camera cameraView;
        private Canvas canvas;
        private Font font;
        private Text tickLabel;
        private Sprite panelSprite;
        private Texture2D panelTexture;
        private Receipt receipt;
        private string mode, folder;
        private Sex sex;
        private int page;
        private float duration, accumulator;
        private bool ready, quit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallOnlyWhenRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--citizen-gallery") >= 0)
                new GameObject("Native citizen gallery • isolated preview").AddComponent<NativeCitizenGallery>();
        }

        private IEnumerator Start()
        {
            // Bootstrap and this opt-in initializer have unspecified callback ordering.
            yield return null;
            try { Prepare(); }
            catch (Exception error) { Debug.LogError("Citizen gallery preparation failed: " + error); Application.Quit(2); yield break; }
            yield return null;
            yield return new WaitForEndOfFrame();
            if (string.IsNullOrEmpty(folder)) yield break;
            double start = Time.realtimeSinceStartupAsDouble;
            int index = 0;
            while (Time.realtimeSinceStartupAsDouble - start < duration)
            {
                yield return new WaitForEndOfFrame();
                CaptureSample(index++, Time.realtimeSinceStartupAsDouble - start);
                double next = start + index * .1;
                while (Time.realtimeSinceStartupAsDouble < next) yield return null;
            }
            // Native screenshot writes finish asynchronously. The wrapper also verifies every PNG.
            yield return new WaitForSecondsRealtime(.75f);
            receipt.completedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            File.WriteAllText(Path.Combine(folder, "gallery.json"), JsonUtility.ToJson(receipt, true));
            Debug.Log("Native citizen gallery captured " + receipt.frames.Count + " frames; isolated preview, not gameplay.");
            if (quit) Application.Quit();
        }

        private static string Argument(string[] args, string name, string fallback)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        private void Prepare()
        {
            string[] args = Environment.GetCommandLineArgs();
            mode = Argument(args, "--citizen-gallery", "");
            if (mode != "families" && mode != "wardrobe" && mode != "actions") throw new ArgumentException("Gallery must be families, wardrobe or actions.");
            string selectedSex = Argument(args, "--gallery-sex", "male");
            if (selectedSex != "male" && selectedSex != "female") throw new ArgumentException("Gallery sex must be male or female.");
            sex = selectedSex == "female" ? Sex.Female : Sex.Male;
            if (!int.TryParse(Argument(args, "--gallery-page", "0"), out page) || page < 0 || page > 2) throw new ArgumentException("Action page must be 0, 1 or 2.");
            if (!float.TryParse(Argument(args, "--gallery-seconds", "6"), NumberStyles.Float, CultureInfo.InvariantCulture, out duration) || float.IsNaN(duration) || float.IsInfinity(duration) || duration < 1 || duration > 20) throw new ArgumentException("Gallery duration must be 1..20 seconds.");
            folder = Argument(args, "--gallery-output", "");
            if (folder.Length > 0) { folder = Path.GetFullPath(folder); Directory.CreateDirectory(folder); }
            quit = Array.IndexOf(args, "--quit-after-gallery") >= 0;
            GameBootstrap game = GameBootstrap.Instance;
            if (game == null || game.MainCamera == null) throw new InvalidOperationException("GameBootstrap has not initialized.");
            game.Paused = true;
            game.StopAllCoroutines();
            foreach (MonoBehaviour behaviour in game.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (Renderer renderer in game.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (Canvas oldCanvas in game.GetComponentsInChildren<Canvas>(true)) oldCanvas.enabled = false;
            foreach (AudioSource source in game.GetComponentsInChildren<AudioSource>(true)) source.Stop();
            cameraView = game.MainCamera;
            cameraView.transform.position = new Vector3(32, 32, -10);
            cameraView.transform.rotation = Quaternion.identity;
            cameraView.orthographic = true;
            cameraView.orthographicSize = 10;
            cameraView.backgroundColor = new Color32(17, 27, 34, 255);
            cameraView.clearFlags = CameraClearFlags.SolidColor;

            clock = new SimulationEngine(WorldGenerator.Generate(6192026, 16, 16));
            clock.World.Weather.Automatic = false;
            clock.World.Laws.Births = clock.World.Laws.Warfare = clock.World.Laws.Aging = false;
            preview = WorldGenerator.Generate(6192026, 64, 64);
            citizens = new CitizenSprites(transform);
            receipt = new Receipt { startedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), mode = mode, sex = selectedSex, page = page,
                width = Screen.width, height = Screen.height, unityVersion = Application.unityVersion, atlasWidth = citizens.AtlasWidth, atlasHeight = citizens.AtlasHeight, seed = preview.Seed };
            MakeCanvas();
            if (mode == "actions") MakeActions(); else MakeFamiliesOrWardrobe();

            // Prime persistent facing through the same path branch used in the game, then
            // remove moving paths for stationary action fixtures before any capture.
            citizens.Begin(preview);
            foreach (Entry entry in entries) citizens.Add(preview, entry.Unit, 0, Color.white);
            citizens.Submit();
            foreach (Entry entry in entries) if (!entry.Moving) entry.Unit.Path.Clear();
            ready = true;
            RenderSubjects();
        }

        private void MakeCanvas()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject obj = new GameObject("Gallery labels • prepared fixture", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            obj.transform.SetParent(transform, false);
            canvas = obj.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
            CanvasScaler scale = obj.GetComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(1920, 1080); scale.matchWidthOrHeight = .5f;
            string title = mode == "families" ? "ЧЕТЫРЕ НАРОДА · СЕМЬИ" : mode == "wardrobe" ? "ОДЕЖДА · СЕМЬ ГРАЖДАНСКИХ ПРОФЕССИЙ" : "ДЕЙСТВИЯ · СТРАНИЦА " + (page + 1) + " / 3";
            Label(title, 40, 26, 1800, 45, 30, TextAnchor.MiddleLeft, new Color32(235, 215, 165, 255));
            Label("ИЗОЛИРОВАННЫЙ UNITY PREVIEW · подготовленные состояния · не игровой процесс", 40, 76, 1840, 32, 22, TextAnchor.MiddleLeft, new Color32(226, 190, 122, 255));
            string detail = mode == "families" ? "16 профилей × 4 направления. Ходьба на месте, исходные кадры production-атласа." :
                mode == "wardrobe" ? (sex == Sex.Female ? "Женщины" : "Мужчины") + ". Одежда профессии не выдаёт оружие, броню или груз. Ходьба на месте." :
                "Production-позы; рабочие действия — производные жесты. *Таймер выпада задан, реального боя нет.";
            Label(detail, 40, 113, 1840, 32, 20, TextAnchor.MiddleLeft, new Color32(169, 190, 197, 255));
            tickLabel = Label("", 40, 1014, 1840, 42, 19, TextAnchor.MiddleLeft, new Color32(169, 190, 197, 255));
            panelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            panelTexture.SetPixel(0, 0, Color.white); panelTexture.Apply();
            panelSprite = Sprite.Create(panelTexture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1);
        }

        private Text Label(string text, float x, float y, float width, float height, int size, TextAnchor alignment, Color color)
        {
            GameObject obj = new GameObject("Label " + text, typeof(RectTransform), typeof(Text)); obj.transform.SetParent(canvas.transform, false);
            Text label = obj.GetComponent<Text>(); label.font = font; label.fontSize = size; label.color = color;
            label.text = text; label.alignment = alignment; label.raycastTarget = false; label.supportRichText = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
            RectTransform rect = label.rectTransform; rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return label;
        }
        private Vector2 Position(float x, float y)
        {
            return new Vector2(32 + (x / 1920 - .5f) * cameraView.orthographicSize * 2 * cameraView.aspect, 32 + (.5f - y / 1080) * cameraView.orthographicSize * 2);
        }
        private void Panel(float x, float y, float width, float height, int parity)
        {
            GameObject obj = new GameObject("Gallery card background", typeof(SpriteRenderer)); obj.transform.SetParent(transform, false); panels.Add(obj);
            SpriteRenderer renderer = obj.GetComponent<SpriteRenderer>(); renderer.sprite = panelSprite; renderer.sortingOrder = -100;
            renderer.color = parity % 2 == 0 ? new Color32(29, 43, 50, 255) : new Color32(34, 49, 55, 255);
            obj.transform.position = Position(x + width * .5f, y + height * .5f);
            obj.transform.localScale = new Vector3(width / 1920 * 20 * cameraView.aspect, height / 1080 * 20, 1);
        }
        private void MakeFamiliesOrWardrobe()
        {
            bool wardrobe = mode == "wardrobe";
            string[] families = { "Мужчины", "Женщины", "Мальчики", "Девочки" };
            int rows = wardrobe ? 7 : 4;
            float rowHeight = wardrobe ? 112 : 194;
            for (int race = 0; race < 4; race++) Label(RaceNames[race], 182 + race * 425, 152, 414, 35, 22, TextAnchor.MiddleCenter, Color.white);
            for (int row = 0; row < rows; row++)
            {
                float top = 192 + row * rowHeight;
                Label(wardrobe ? ProfessionNames[row] : families[row], 18, top + 28, 154, 60, wardrobe ? 18 : 22, TextAnchor.MiddleLeft, Color.white);
                for (int race = 0; race < 4; race++)
                {
                    float left = 182 + race * 425;
                    Panel(left, top, 414, rowHeight - 8, race + row);
                    for (int direction = 0; direction < 4; direction++)
                    {
                        float px = left + 54 + direction * 102, py = top + (wardrobe ? 78 : 136);
                        Sex profileSex = wardrobe ? sex : row % 2 == 0 ? Sex.Male : Sex.Female;
                        UnitData unit = NewSubject(Races[race], profileSex, !wardrobe && row >= 2, wardrobe ? Professions[row] : UnitProfession.None, px, py, direction, UnitTask.Wander, true, wardrobe ? ProfessionNames[row] : families[row]);
                        Label(Directions[direction], px - 42, py + 8, 84, 26, wardrobe ? 14 : 17, TextAnchor.MiddleCenter, new Color32(156, 179, 185, 255));
                    }
                }
            }
        }
        private void MakeActions()
        {
            for (int column = 0; column < 8; column++) Label(ActionNames[page * 8 + column], 182 + column * 213, 152, 202, 36, 19, TextAnchor.MiddleCenter, Color.white);
            for (int race = 0; race < 4; race++)
            {
                float top = 196 + race * 194;
                Label(RaceNames[race], 20, top + 60, 150, 46, 21, TextAnchor.MiddleLeft, Color.white);
                for (int column = 0; column < 8; column++)
                {
                    int action = page * 8 + column;
                    UnitTask task = ActionTasks[action];
                    float left = 182 + column * 213;
                    Panel(left, top, 202, 184, race + column);
                    bool moving = task == UnitTask.Wander || task == UnitTask.Return || task == UnitTask.Flee || task == UnitTask.Haul || task == UnitTask.Caravan || task == UnitTask.Hunt || task == UnitTask.Patrol;
                    bool child = task == UnitTask.Play || task == UnitTask.Learn;
                    UnitProfession profession = child ? UnitProfession.None : task == UnitTask.Farm ? UnitProfession.Farmer : task == UnitTask.Gather && action == 3 ? UnitProfession.Forester :
                        task == UnitTask.CraftEquipment || task == UnitTask.Process || task == UnitTask.BuildTransport ? UnitProfession.Carpenter : task == UnitTask.Tend ? UnitProfession.Herder : task == UnitTask.Caravan ? UnitProfession.Merchant : task == UnitTask.Hunt ? UnitProfession.Hunter : UnitProfession.None;
                    UnitData unit = NewSubject(Races[race], sex, child, profession, left + 101, top + 131, 2, task, moving, ActionNames[action]);
                    if (task == UnitTask.Return || task == UnitTask.Haul) { unit.CargoKind = ItemType.Plank; unit.CargoAmount = 1; receipt.subjects[receipt.subjects.Count - 1].preparedCargo = true; }
                    if (task == UnitTask.Gather)
                    {
                        unit.ResourceCell = unit.Path[0];
                        preview.Cells[unit.ResourceCell].Fire = 0;
                        preview.ResourceNodes[unit.ResourceCell] = new ResourceNodeData { Type = action == 3 ? ResourceType.Tree : ResourceType.Stone, Amount = 3, Capacity = 3 };
                    }
                    if (task == UnitTask.Process || task == UnitTask.Tend || task == UnitTask.Brew) unit.WorkTimer = 1;
                    if (task == UnitTask.CraftEquipment) unit.EquipmentProgress = 1;
                    if (task == UnitTask.BuildTransport)
                    {
                        // Production poses require a delivered project and an on-site worker.
                        // This is an explicit presentation fixture, never a saved construction.
                        unit.X = Mathf.Floor(unit.X) + .5f; unit.Y = Mathf.Floor(unit.Y) + .5f;
                        unit.TransportProjectId = preview.NextId++;
                        int bank = preview.Index((int)unit.X, (int)unit.Y);
                        preview.TransportProjects.Add(new TransportProjectData { Id = unit.TransportProjectId, WorkerId = unit.Id,
                            BankCell = bank, Cells = new[] { bank }, Material = ItemType.Plank, Required = 1, Delivered = 1, Level = 1 });
                        receipt.subjects[receipt.subjects.Count - 1].preparedTransportProject = true;
                    }
                    if (task == UnitTask.Fight) { entries[entries.Count - 1].Attack = true; receipt.subjects[receipt.subjects.Count - 1].preparedAttackTimer = true; }
                    Label(task.ToString(), left + 6, top + 148, 190, 26, 14, TextAnchor.MiddleCenter, new Color32(156, 179, 185, 255));
                }
            }
        }
        private UnitData NewSubject(SpeciesType race, Sex profileSex, bool child, UnitProfession profession, float px, float py, int direction, UnitTask task, bool moving, string label)
        {
            Vector2 point = Position(px, py), facing = CitizenSprites.DirectionVector((CitizenSprites.CitizenDirection)direction);
            UnitData unit = new UnitData { Id = preview.NextId++, Name = "Gallery fixture", Species = race, Sex = profileSex, Age = child ? 10 : 24,
                Profession = profession, Task = task, X = point.x, Y = point.y, MaxHealth = 100, Health = 100 };
            int targetX = Mathf.Clamp(Mathf.FloorToInt(point.x + facing.x * 2), 0, preview.Width - 1);
            int targetY = Mathf.Clamp(Mathf.FloorToInt(point.y + facing.y * 2), 0, preview.Height - 1);
            unit.Path.Add(preview.Index(targetX, targetY));
            preview.Units.Add(unit);
            entries.Add(new Entry { Unit = unit, Direction = facing, Moving = moving });
            receipt.subjects.Add(new Subject { id = unit.Id, race = race.ToString(), appearance = CitizenSprites.AppearanceFor(unit).ToString(), profession = profession.ToString(),
                task = task.ToString(), direction = ((CitizenSprites.CitizenDirection)direction).ToString(), label = label, preparedMovingPath = moving });
            return unit;
        }
        private void Update()
        {
            if (!ready) return;
            accumulator += Time.unscaledDeltaTime;
            int processed = 0;
            while (accumulator >= SimulationEngine.TickSeconds && processed++ < 20)
            { clock.Step(SimulationEngine.TickSeconds); accumulator -= SimulationEngine.TickSeconds; }
            preview.Tick = clock.World.Tick;
            preview.SimulatedSeconds = clock.World.SimulatedSeconds;
            RenderSubjects();
        }
        private void RenderSubjects()
        {
            foreach (Entry entry in entries)
                if (entry.Attack) { int phase = (int)(preview.Tick % 12); entry.Unit.AttackTimer = phase < 4 ? .8f - phase * .1f : 0; }
            citizens.Begin(preview);
            foreach (Entry entry in entries) citizens.Add(preview, entry.Unit, 0, Color.white);
            citizens.Submit();
            tickLabel.text = "Тик " + preview.Tick + " · 10 тиков/с · " + entries.Count + " подготовленных фигур · CitizenSprites Add → Submit · оценка внешнего вида ожидается";
        }
        private void CaptureSample(int index, double elapsed)
        {
            Sample sample = new Sample { index = index, wallSeconds = elapsed, tick = preview.Tick, atlasFrames = new int[entries.Count], toolFrames = new int[entries.Count], directions = new int[entries.Count], actions = new string[entries.Count], derivedGestures = new bool[entries.Count] };
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                CitizenSprites.Pose pose = CitizenSprites.ResolvePose(preview, entry.Unit, citizens.Facing(entry.Unit.Id));
                sample.atlasFrames[i] = pose.Frame; sample.toolFrames[i] = pose.ToolFrame; sample.directions[i] = (int)pose.Direction; sample.actions[i] = pose.Action.ToString(); sample.derivedGestures[i] = pose.DerivedGesture;
            }
            receipt.frames.Add(sample);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "frame-" + index.ToString("D4", CultureInfo.InvariantCulture) + ".png"));
        }
        private void OnDestroy()
        {
            citizens?.Dispose();
            if (panelSprite != null) Destroy(panelSprite);
            if (panelTexture != null) Destroy(panelTexture);
        }
    }
}
