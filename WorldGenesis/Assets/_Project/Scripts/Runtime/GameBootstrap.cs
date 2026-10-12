using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    public sealed class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }
        public SimulationEngine Engine { get; private set; }
        public WorldState World => Engine.World;
        public WorldView View { get; private set; }
        public Camera MainCamera { get; private set; }
        public WorldTool Tool { get; set; }
        public WorldPreset GenerationPreset { get; set; } = WorldPreset.Island;
        public float Speed { get; set; } = 1;
        public int BrushRadius { get; set; } = 2;
        // Presentation of the command's documented radius contract; commands still use the engine's validation.
        public int EffectiveBrushRadius => Tool != null && Tool.Kind == ToolKind.Power ? Mathf.Max((PowerType)Tool.Value == PowerType.Meteor ? 3 : 1, BrushRadius) : BrushRadius;
        public bool ToolAffectsTerrain => Tool == null || Tool.Kind != ToolKind.Power || (PowerType)Tool.Value == PowerType.Fire || (PowerType)Tool.Value == PowerType.Rain || (PowerType)Tool.Value == PowerType.Lightning || (PowerType)Tool.Value == PowerType.Meteor;
        public int UnitImpactRadius => Tool != null && Tool.Kind == ToolKind.Power && (PowerType)Tool.Value != PowerType.Fire && (PowerType)Tool.Value != PowerType.Rain ? EffectiveBrushRadius + 1 : -1;
        public int SelectedUnitId { get; private set; }
        public int InspectX { get; private set; } = -1;
        public int InspectY { get; private set; } = -1;
        public float SimulationBacklogSeconds => tickAccumulator;
        public bool Paused { get => paused; set { paused = value; tickAccumulator = 0; } }
        private bool paused;
        private float tickAccumulator, targetZoom, nextBrushTime;
        private Vector3 targetCamera, lastMouse;
        private bool dragCamera, gestureStartedInWorld;
        private int previousBrushX = -1000, previousBrushY = -1000;
        private GameHud hud;
        private WorldEffects effects;
        private bool ownsEventSystem;
        private GameObject eventSystem;
        private bool capturing;
        private bool assistedFraming;
        private EventSystem raycastEventSystem;
        private PointerEventData raycastPointer;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>(16);
        private int pointerSampleFrame = -1;
        private bool pointerOverUi;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureGame()
        {
            if (UnityEngine.Object.FindFirstObjectByType<GameBootstrap>() == null) new GameObject("World Genesis").AddComponent<GameBootstrap>();
        }
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this; Application.targetFrameRate = 60; QualitySettings.vSyncCount = 1;
            MainCamera = Camera.main;
            if (MainCamera == null) { GameObject cameraObject = new GameObject("World camera", typeof(Camera), typeof(AudioListener)); cameraObject.tag = "MainCamera"; cameraObject.transform.SetParent(transform); MainCamera = cameraObject.GetComponent<Camera>(); }
            MainCamera.orthographic = true; MainCamera.clearFlags = CameraClearFlags.SolidColor; MainCamera.backgroundColor = PixelArt.Hex("122e43"); MainCamera.nearClipPlane = .1f; MainCamera.farClipPlane = 100;
            MainCamera.transform.rotation = Quaternion.identity;
            if (EventSystem.current == null) { eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)); ownsEventSystem = true; }
            View = new WorldView(transform); effects = new WorldEffects(transform, View.SpriteMaterial);
            NewWorld(1618033); hud = new GameHud(this);
            gameObject.AddComponent<ProceduralAudio>();
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] == "--seed" && i + 1 < arguments.Length && int.TryParse(arguments[i + 1], out int seed)) NewWorld(seed);
                if (arguments[i] == "--capture" && i + 1 < arguments.Length)
                {
                    float delay = 8; bool quit = Array.IndexOf(arguments, "--quit-after-capture") >= 0;
                    int delayIndex = Array.IndexOf(arguments, "--capture-delay");
                    if (delayIndex >= 0 && delayIndex + 1 < arguments.Length && float.TryParse(arguments[delayIndex + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float specified)) delay = Mathf.Clamp(specified, 0, 600);
                    StartCoroutine(DelayedCapture(arguments[i + 1], delay, quit));
                }
            }
            int zoomIndex = Array.IndexOf(arguments, "--zoom"); float requestedZoom = 20;
            if (zoomIndex >= 0 && zoomIndex + 1 < arguments.Length && float.TryParse(arguments[zoomIndex + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedZoom)) requestedZoom = Mathf.Clamp(parsedZoom, 5, 120);
            if (Array.IndexOf(arguments, "--focus-settlement") >= 0 && World.Settlements.Count > 0)
            {
                SettlementData settlement = World.Settlements[0]; FocusWorldPoint(new Vector2(settlement.X + .5f, settlement.Y + .5f), requestedZoom); InspectX = settlement.X; InspectY = settlement.Y; assistedFraming = true;
            }
            else if (zoomIndex >= 0) { FocusWorldPoint(new Vector2(World.Width * .5f, World.Height * .5f), requestedZoom); assistedFraming = true; }
            if (Array.IndexOf(arguments, "--inspect-citizen") >= 0)
            {
                foreach (UnitData unit in World.Units)
                {
                    if (unit.Species != SpeciesType.Human || unit.Age < 16) continue;
                    SelectedUnitId = unit.Id; InspectX = (int)unit.X; InspectY = (int)unit.Y;
                    hud.Refresh(true); break;
                }
            }
            if (Array.IndexOf(arguments, "--capture-motion") >= 0) gameObject.AddComponent<MotionCapture>();
            Debug.Log("World Genesis ready. Seed=" + World.Seed + "; size=" + World.Width + "x" + World.Height + "; save path=" + SaveStore.DefaultPath);
        }

        public void NewWorld(int seed)
        {
            WorldState candidate = WorldGenerator.Generate(seed, 128, 128, GenerationPreset);
            SimulationEngine engine = new SimulationEngine(candidate);
            // The authored opening is real simulation input, reproducible for this seed.
            SeedLife(engine);
            ReplaceWorld(engine); FrameWorld(); hud?.ShowToast("Создан мир " + seed + ". Его история начинается сейчас.");
        }
        private static void SeedLife(SimulationEngine engine)
        {
            WorldState world = engine.World;
            int[] anchorsX = { world.Width / 3, world.Width * 2 / 3, world.Width / 3, world.Width * 2 / 3 };
            int[] anchorsY = { world.Height / 3, world.Height * 2 / 3, world.Height * 2 / 3, world.Height / 3 };
            for (int s = 0; s < 4; s++)
            {
                int foundX = -1, foundY = -1; float best = float.MaxValue;
                for (int y = 4; y < world.Height - 4; y++) for (int x = 4; x < world.Width - 4; x++)
                {
                    if (!world.IsWalkable(x, y) || world.Cells[world.Index(x, y)].Terrain == TerrainType.Snow) continue;
                    float score = (x - anchorsX[s]) * (x - anchorsX[s]) + (y - anchorsY[s]) * (y - anchorsY[s]);
                    if (score < best) { best = score; foundX = x; foundY = y; }
                }
                if (foundX >= 0) { engine.Spawn((SpeciesType)s, foundX, foundY, 9); engine.Spawn(SpeciesType.Sheep, foundX + 3, foundY + 2, 3); }
            }
        }
        private void ReplaceWorld(SimulationEngine engine)
        {
            Engine = engine; GenerationPreset = World.Preset; tickAccumulator = 0; SelectedUnitId = 0; InspectX = -1; InspectY = -1;
            gestureStartedInWorld = false; dragCamera = false; previousBrushX = -1000;
            View.SetWorld(World); effects.Reset(World); hud?.WorldChanged();
        }
        public void Save()
        {
            if (SaveStore.TrySave(SaveStore.DefaultPath, World, out string error)) hud?.ShowToast("Мир сохранён. Предыдущее сохранение доступно в world.json.bak");
            else hud?.ShowToast(error);
        }
        public void Load()
        {
            if (!SaveStore.TryLoad(SaveStore.DefaultPath, out WorldState candidate, out string error)) { hud?.ShowToast(error + ". Текущий мир сохранён в памяти."); return; }
            try { SimulationEngine engine = new SimulationEngine(candidate); ReplaceWorld(engine); FrameWorld(); hud?.ShowToast("Мир загружен. Симуляция продолжается с тика " + World.Tick); }
            catch (ArgumentException exception) { hud?.ShowToast("Сохранение отклонено: " + exception.Message); }
        }
        public void MoveCamera(Vector2 point) { targetCamera = new Vector3(point.x, point.y, -10); }
        public void FocusWorldPoint(Vector2 center, float zoom)
        {
            targetZoom = Mathf.Clamp(zoom, 5, Mathf.Max(World.Width, World.Height));
            float uiScale = Mathf.Sqrt(Screen.width / 1600f * Screen.height / 900f);
            float centerOffset = (210 - 92) * .5f * uiScale * (targetZoom * 2 / Mathf.Max(1, Screen.height));
            targetCamera = new Vector3(center.x, center.y - centerOffset, -10);
            MainCamera.transform.position = targetCamera; MainCamera.orthographicSize = targetZoom;
        }
        public void FrameWorld()
        {
            float uiScale = Mathf.Sqrt(Screen.width / 1600f * Screen.height / 900f);
            float bottom = 210 * uiScale, top = 92 * uiScale;
            float freeHeight = Mathf.Max(100, Screen.height - top - bottom), freeWidth = Mathf.Max(160, Screen.width - 522 * uiScale);
            targetZoom = Mathf.Max(World.Height * Screen.height / freeHeight, World.Width * Screen.height / freeWidth) * .52f;
            float centerOffset = (bottom + freeHeight * .5f - Screen.height * .5f) * (targetZoom * 2 / Mathf.Max(1, Screen.height));
            targetCamera = new Vector3(World.Width * .5f, World.Height * .5f - centerOffset, -10);
            MainCamera.transform.position = targetCamera; MainCamera.orthographicSize = targetZoom;
        }

        private void Update()
        {
            if (Engine == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .25f);
            bool focus = hud != null && hud.IsTextFocused;
            if (!focus) HandleKeyboard();
            HandleCamera(dt, focus); HandleWorldInput();
            if (!Paused)
            {
                tickAccumulator += Time.unscaledDeltaTime * Speed;
                int work = 0;
                while (tickAccumulator >= .1f && work < 20) { Engine.Step(.1f); tickAccumulator -= .1f; work++; }
                // A slow frame retains its backlog. It never silently skips simulation ticks.
            }
            View.SelectedUnitId = SelectedUnitId; View.Update(Time.unscaledTime); effects.Update(World, dt, Paused); hud?.Refresh();
        }
        private void HandleKeyboard()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.M))
            {
                ProceduralAudio sound = GetComponent<ProceduralAudio>();
                if (sound != null) { sound.ToggleMuted(); hud?.ShowToast(sound.Muted ? "Звук выключен  [M]" : "Звук включён  [M]"); }
            }
            if (UnityEngine.Input.GetKeyDown(KeyCode.Space)) Paused = !Paused;
            if (UnityEngine.Input.GetKeyDown(KeyCode.I) || UnityEngine.Input.GetKeyDown(KeyCode.Escape)) { hud?.SelectInspect(); gestureStartedInWorld = false; }
            if (UnityEngine.Input.GetKeyDown(KeyCode.Home)) FrameWorld();
            if (UnityEngine.Input.GetKeyDown(KeyCode.LeftBracket)) BrushRadius = Mathf.Max(0, BrushRadius - 1);
            if (UnityEngine.Input.GetKeyDown(KeyCode.RightBracket)) BrushRadius = Mathf.Min(12, BrushRadius + 1);
            bool control = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
            if (control && UnityEngine.Input.GetKeyDown(KeyCode.S)) Save();
            if (control && UnityEngine.Input.GetKeyDown(KeyCode.L)) Load();
            if (UnityEngine.Input.GetKeyDown(KeyCode.F12)) Capture(Path.Combine(Application.persistentDataPath, "Captures", "WorldGenesis-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png"));
        }
        private void HandleCamera(float dt, bool focused)
        {
            bool overUi = CurrentPointerOverUI();
            if (!focused)
            {
                float horizontal = (UnityEngine.Input.GetKey(KeyCode.D) || UnityEngine.Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (UnityEngine.Input.GetKey(KeyCode.A) || UnityEngine.Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
                float vertical = (UnityEngine.Input.GetKey(KeyCode.W) || UnityEngine.Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (UnityEngine.Input.GetKey(KeyCode.S) || UnityEngine.Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
                if (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl)) { horizontal = 0; vertical = 0; }
                targetCamera += new Vector3(horizontal, vertical, 0) * targetZoom * dt * 1.1f;
            }
            if (!overUi)
            {
                float scroll = UnityEngine.Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > .001f) targetZoom = Mathf.Clamp(targetZoom * Mathf.Pow(.86f, scroll), 5, Mathf.Max(World.Width, World.Height) * .9f);
                if (UnityEngine.Input.GetMouseButtonDown(2)) { dragCamera = true; lastMouse = UnityEngine.Input.mousePosition; }
            }
            if (!UnityEngine.Input.GetMouseButton(2)) dragCamera = false;
            if (dragCamera)
            {
                Vector3 delta = UnityEngine.Input.mousePosition - lastMouse; targetCamera -= delta * (MainCamera.orthographicSize * 2 / Mathf.Max(1, Screen.height)); lastMouse = UnityEngine.Input.mousePosition; targetCamera.z = -10;
            }
            targetCamera.x = Mathf.Clamp(targetCamera.x, -World.Width * .1f, World.Width * 1.1f); targetCamera.y = Mathf.Clamp(targetCamera.y, -World.Height * .1f, World.Height * 1.1f);
            float follow = 1 - Mathf.Exp(-12 * dt); MainCamera.transform.position = Vector3.Lerp(MainCamera.transform.position, targetCamera, follow); MainCamera.orthographicSize = Mathf.Lerp(MainCamera.orthographicSize, targetZoom, follow);
        }
        private void HandleWorldInput()
        {
            Vector3 mouse = UnityEngine.Input.mousePosition;
            bool inWindow = mouse.x >= 0 && mouse.y >= 0 && mouse.x < Screen.width && mouse.y < Screen.height;
            bool overUi = CurrentPointerOverUI();
            Vector3 position = MainCamera.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, 10)); int x = Mathf.FloorToInt(position.x), y = Mathf.FloorToInt(position.y);
            bool canApply = inWindow && !overUi && World.InBounds(x, y) && Tool != null;
            View.SetBrush(x, y, Tool != null && Tool.Kind == ToolKind.Creature ? 0 : EffectiveBrushRadius, canApply && Tool.Kind != ToolKind.Inspect, Tool?.Color ?? PixelArt.Teal, UnitImpactRadius, ToolAffectsTerrain);
            if (UnityEngine.Input.GetMouseButtonDown(0)) { gestureStartedInWorld = canApply; previousBrushX = -1000; }
            if (!UnityEngine.Input.GetMouseButton(0) || !inWindow) { gestureStartedInWorld = false; return; }
            if (!canApply) { gestureStartedInWorld = false; previousBrushX = -1000; return; }
            if (!gestureStartedInWorld) return;
            if (Tool.Kind == ToolKind.Inspect) { if (UnityEngine.Input.GetMouseButtonDown(0)) Inspect(x, y); return; }
            bool changedCell = x != previousBrushX || y != previousBrushY;
            if (Tool.Kind == ToolKind.Terrain)
            {
                if (!changedCell) return;
                if (previousBrushX > -1000) { int steps = Mathf.Max(Mathf.Abs(x - previousBrushX), Mathf.Abs(y - previousBrushY)); for (int s = 1; s < steps; s++) Engine.Paint((TerrainType)Tool.Value, Mathf.RoundToInt(Mathf.Lerp(previousBrushX, x, s / (float)steps)), Mathf.RoundToInt(Mathf.Lerp(previousBrushY, y, s / (float)steps)), BrushRadius); }
                Engine.Paint((TerrainType)Tool.Value, x, y, BrushRadius);
            }
            else
            {
                if (!UnityEngine.Input.GetMouseButtonDown(0) && Time.unscaledTime < nextBrushTime) return;
                if (Tool.Kind == ToolKind.Creature) Engine.Spawn((SpeciesType)Tool.Value, x, y, 1); else Engine.ApplyPower((PowerType)Tool.Value, x, y, BrushRadius);
                nextBrushTime = Time.unscaledTime + (Tool.Kind == ToolKind.Creature ? .23f : .35f);
            }
            previousBrushX = x; previousBrushY = y;
        }
        private void Inspect(int x, int y)
        {
            InspectX = x; InspectY = y; SelectedUnitId = 0; float distance = 2.25f;
            foreach (UnitData unit in World.Units) { float dx = unit.X - (x + .5f), dy = unit.Y - (y + .5f), squared = dx * dx + dy * dy; if (squared < distance) { distance = squared; SelectedUnitId = unit.Id; } }
            hud?.Refresh(true);
        }
        public bool IsScreenPointOverUI(Vector2 position)
        {
            EventSystem current = EventSystem.current;
            if (current == null) return false;
            if (raycastPointer == null || raycastEventSystem != current) { raycastEventSystem = current; raycastPointer = new PointerEventData(current); }
            raycastPointer.Reset(); raycastPointer.position = position;
            uiHits.Clear(); current.RaycastAll(raycastPointer, uiHits);
            for (int i = 0; i < uiHits.Count; i++) if (uiHits[i].module is UnityEngine.UI.GraphicRaycaster) return true;
            return false;
        }
        private bool CurrentPointerOverUI()
        {
            if (pointerSampleFrame != Time.frameCount) { pointerSampleFrame = Time.frameCount; pointerOverUi = IsScreenPointOverUI(UnityEngine.Input.mousePosition); }
            return pointerOverUi;
        }
        private void OnApplicationFocus(bool focused) { if (!focused) { gestureStartedInWorld = false; dragCamera = false; } }
        public void Capture(string path) { if (!capturing) StartCoroutine(CaptureRoutine(path, false, false)); }
        private IEnumerator DelayedCapture(string path, float delay, bool quit)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (CaptureScenario.ApplyRequested(this, path))
            {
                hud?.Refresh(true);
                // Let normal event presentation and dirty terrain refresh reach the frame.
                yield return new WaitForSecondsRealtime(.35f);
            }
            yield return CaptureRoutine(path, quit, true);
        }
        [Serializable]
        private sealed class CaptureReceipt { public string Type, Utc, UnityVersion, Image, Preset; public int Seed, Width, Height, Population, ScreenWidth, ScreenHeight; public long Tick; public float Speed, CameraX, CameraY, OrthographicSize; public bool Paused, AutomaticCapture, AssistedFraming; }
        private IEnumerator CaptureRoutine(string path, bool quit, bool automatic)
        {
            capturing = true; string fullPath;
            try { fullPath = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(fullPath)); }
            catch (Exception exception) { Debug.LogError("Capture path rejected: " + exception.Message); capturing = false; yield break; }
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(fullPath);
            CaptureReceipt receipt = new CaptureReceipt { Type = "native Unity gameplay capture", Utc = DateTime.UtcNow.ToString("O"), UnityVersion = Application.unityVersion, Image = Path.GetFileName(fullPath), Seed = World.Seed, Width = World.Width, Height = World.Height, Population = World.Units.Count, Tick = World.Tick, Speed = Speed, Paused = Paused, AutomaticCapture = automatic, AssistedFraming = assistedFraming, CameraX = MainCamera.transform.position.x, CameraY = MainCamera.transform.position.y, OrthographicSize = MainCamera.orthographicSize, ScreenWidth = Screen.width, ScreenHeight = Screen.height, Preset = World.Preset.ToString() };
            float deadline = Time.realtimeSinceStartup + 8;
            yield return null;
            while (!File.Exists(fullPath) && Time.realtimeSinceStartup < deadline) yield return null;
            if (!File.Exists(fullPath)) { Debug.LogError("Capture failed: image was not written."); capturing = false; if (quit) Application.Quit(2); yield break; }
            try { File.WriteAllText(fullPath + ".json", JsonUtility.ToJson(receipt, true)); }
            catch (Exception exception) { Debug.LogError("Capture receipt failed: " + exception.Message); capturing = false; if (quit) Application.Quit(2); yield break; }
            yield return new WaitForSecondsRealtime(.3f);
            Debug.Log("Native capture written: " + fullPath); hud?.ShowToast("Снимок сохранён: " + fullPath); capturing = false;
            if (quit) Application.Quit(0);
        }
        private void OnDestroy()
        {
            if (Instance != this) return;
            hud?.Dispose(); effects?.Dispose(); View?.Dispose(); if (ownsEventSystem && eventSystem != null) Destroy(eventSystem); Instance = null;
        }
    }
}
