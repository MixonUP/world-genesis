using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    // Four authored walk frames/directions. Other actions use derived body gestures;
    // presentation never grants inventory, applies damage or advances a second clock.
    public sealed class CitizenSprites : IDisposable
    {
        public enum CitizenAppearance { AdultMale, AdultFemale, Boy, Girl, Warrior }
        public enum CitizenDirection { South, West, East, North }
        public enum CitizenAction { Idle, Walk, Carry, Chop, Mine, Farm, Process, Craft, Build, Attack, Flee, Eat, Rest, Socialize, Play, Learn, Drink, Fish, Tend, Brew, Patrol, Haul, Return, Caravan, Hunt }
        public struct Pose
        {
            // Row is a semantic compatibility hint, not an atlas row.
            public bool Warrior, FlipX;
            public CitizenAppearance Appearance;
            public CitizenDirection Direction;
            public CitizenAction Action;
            public UnitTask SourceTask;
            public int Row, Frame, ToolFrame;
            public Vector2 StrikeOffset, UpperOffset;
            public float HeightScale;
            public bool DerivedGesture;
        }
        private readonly OriginalCitizenAtlas atlas;
        private readonly Material material;
        private readonly QuadBatch batch;
        private readonly Dictionary<int, Vector2> facing = new Dictionary<int, Vector2>(1024);
        private readonly Dictionary<int, UnitData> unitLookup = new Dictionary<int, UnitData>(1024);
        private struct RenderEntry { public UnitData Unit; public Pose Pose; public Rect Uv; public Color Tint; public Vector2 Offset; public bool Mounted; }
        private sealed class DepthOrder : IComparer<RenderEntry>
        {
            public int Compare(RenderEntry a, RenderEntry b)
            { int depth = (b.Unit.Y + b.Offset.y).CompareTo(a.Unit.Y + a.Offset.y); return depth != 0 ? depth : a.Unit.Id.CompareTo(b.Unit.Id); }
        }
        private static readonly DepthOrder Depth = new DepthOrder();
        private readonly List<RenderEntry> entries = new List<RenderEntry>(1024);
        public int AtlasWidth => atlas.Texture.width;
        public int AtlasHeight => atlas.Texture.height;

        public CitizenSprites(Transform parent)
        {
            atlas = new OriginalCitizenAtlas();
            material = new Material(Shader.Find("Sprites/Default")) { mainTexture = atlas.Texture, name = "Original citizen animation atlas" };
            // Kept for production mesh inspection compatibility; now contains all four peoples.
            batch = new QuadBatch("Animated human citizens", parent, material, 50);
        }
        public static bool IsCivilized(SpeciesType species) { return (int)species >= 0 && (int)species < 4; }
        public static CitizenAppearance AppearanceFor(UnitData unit)
        {
            if (unit.IsChild) return unit.Sex == Sex.Female ? CitizenAppearance.Girl : CitizenAppearance.Boy;
            return unit.Sex == Sex.Female ? CitizenAppearance.AdultFemale : CitizenAppearance.AdultMale;
        }
        public void Reset() { facing.Clear(); unitLookup.Clear(); entries.Clear(); batch.Clear(); batch.Submit(); }
        public Vector2 Facing(int unitId) { return facing.TryGetValue(unitId, out Vector2 value) ? value : Vector2.down; }
        public void Begin(WorldState world)
        {
            batch.Clear(); unitLookup.Clear(); entries.Clear();
            if (facing.Count > world.Units.Count * 2 + 64) facing.Clear();
            for (int i = 0; i < world.Units.Count; i++) unitLookup[world.Units[i].Id] = world.Units[i];
        }
        public static CitizenDirection DirectionFor(Vector2 vector, CitizenDirection previous = CitizenDirection.South)
        {
            float ax = Mathf.Abs(vector.x), ay = Mathf.Abs(vector.y);
            if (ax + ay < .0001f) return previous;
            if (Mathf.Abs(ax - ay) < .001f && Vector2.Dot(DirectionVector(previous), vector) > 0) return previous;
            return ay > ax ? (vector.y > 0 ? CitizenDirection.North : CitizenDirection.South) : (vector.x > 0 ? CitizenDirection.East : CitizenDirection.West);
        }
        public static Vector2 DirectionVector(CitizenDirection direction)
        { return direction == CitizenDirection.North ? Vector2.up : direction == CitizenDirection.South ? Vector2.down : direction == CitizenDirection.West ? Vector2.left : Vector2.right; }
        public void Add(WorldState world, UnitData unit, int colorIndex, Color tint, Vector2 presentationOffset = default(Vector2), bool mounted = false)
        {
            if (!IsCivilized(unit.Species)) return;
            Vector2 previous = Facing(unit.Id), delta = previous;
            if ((unit.Task == UnitTask.Fight || unit.Task == UnitTask.Hunt) && unit.TargetId != 0 && unitLookup.TryGetValue(unit.TargetId, out UnitData target))
                delta = new Vector2(target.X - unit.X, target.Y - unit.Y);
            else if (unit.Path != null && unit.PathCursor < unit.Path.Count)
            {
                int next = unit.Path[unit.PathCursor]; delta = new Vector2(next % world.Width + .5f - unit.X, next / world.Width + .5f - unit.Y);
            }
            else if (unit.Task == UnitTask.Gather && unit.ResourceCell >= 0 && unit.ResourceCell < world.Cells.Length)
                delta = new Vector2(unit.ResourceCell % world.Width + .5f - unit.X, unit.ResourceCell / world.Width + .5f - unit.Y);
            Vector2 direction = DirectionVector(DirectionFor(delta, DirectionFor(previous)));
            facing[unit.Id] = direction;
            Pose pose = ResolvePose(world, unit, direction);
            if (mounted) { pose.Frame = 1; pose.StrikeOffset = pose.UpperOffset = Vector2.zero; pose.HeightScale = 1; }
            WardrobeKey key = CitizenWardrobe.Resolve(unit);
            Rect uv = atlas.Region(unit.Species, (int)pose.Appearance, key.IsValid ? (int)key.Costume : 0, (int)pose.Direction, pose.Frame);
            entries.Add(new RenderEntry { Unit = unit, Pose = pose, Uv = uv, Tint = tint, Offset = presentationOffset, Mounted = mounted });
        }
        private void DrawPose(UnitData unit, Pose pose, Rect uv, Color tint, Vector2 offset, bool mounted)
        {
            OriginalCitizenTools.Tool tool = unit.IsChild || mounted ? OriginalCitizenTools.Tool.None : OriginalCitizenTools.ForAction(pose.Action);
            bool behind = pose.Direction == CitizenDirection.North;
            if (behind && tool != OriginalCitizenTools.Tool.None) DrawTool(unit, pose, offset, tool);
            DrawBody(unit, pose, uv, tint, offset, mounted);
            if (!behind && tool != OriginalCitizenTools.Tool.None) DrawTool(unit, pose, offset, tool);
        }
        private void DrawTool(UnitData unit, Pose pose, Vector2 offset, OriginalCitizenTools.Tool tool)
        {
            Vector2 hand = new Vector2(unit.X, unit.Y) + offset + pose.StrikeOffset + pose.UpperOffset + HandOffset(unit, DirectionVector(pose.Direction));
            float pixel = OriginalCitizenAtlas.RaceScale(unit.Species) / OriginalCitizenAtlas.PixelsPerUnit;
            batch.Add(hand.x - 16 * pixel, hand.y - 16 * pixel, 32 * pixel, 32 * pixel, atlas.ToolRegion(tool, pose.Direction, pose.ToolFrame), Color.white);
        }
        private void DrawBody(UnitData unit, Pose pose, Rect uv, Color tint, Vector2 offset, bool mounted)
        {
            float pixel = OriginalCitizenAtlas.RaceScale(unit.Species) / OriginalCitizenAtlas.PixelsPerUnit;
            float x = unit.X + offset.x + pose.StrikeOffset.x - 16 * pixel;
            float y = unit.Y + offset.y + pose.StrikeOffset.y - 2 * pixel;
            if (mounted)
            {
                uv.y += uv.height * 10 / 32; uv.height *= 22f / 32;
                batch.Add(x, y, 32 * pixel, 22 * pixel, uv, tint); return;
            }
            if (pose.UpperOffset.sqrMagnitude < .000001f)
            { batch.Add(x, y, 32 * pixel, 32 * pixel * pose.HeightScale, uv, tint); return; }
            int waist = unit.IsChild ? 8 : 11;
            Rect lower = new Rect(uv.x, uv.y, uv.width, uv.height * (waist + 1) / 32);
            Rect upper = new Rect(uv.x, uv.y + uv.height * waist / 32, uv.width, uv.height * (32 - waist) / 32);
            batch.Add(x, y, 32 * pixel, (waist + 1) * pixel * pose.HeightScale, lower, tint);
            batch.Add(x + pose.UpperOffset.x, y + waist * pixel * pose.HeightScale + pose.UpperOffset.y, 32 * pixel, (32 - waist) * pixel * pose.HeightScale, upper, tint);
        }
        public static float HeadHeight(UnitData unit) { return (unit.IsChild ? .90f : 1.20f) * OriginalCitizenAtlas.RaceScale(unit.Species); }
        public static Vector2 HandOffset(UnitData unit, Vector2 direction)
        {
            CitizenDirection side = DirectionFor(direction);
            float scale = OriginalCitizenAtlas.RaceScale(unit.Species) * (unit.IsChild ? .75f : 1);
            return new Vector2(side == CitizenDirection.West ? -.27f : side == CitizenDirection.North ? -.18f : .27f, .36f) * scale;
        }
        public static Vector2 ParcelOffset(UnitData unit, Vector2 direction)
        {
            CitizenDirection side = DirectionFor(direction);
            float scale = OriginalCitizenAtlas.RaceScale(unit.Species) * (unit.IsChild ? .75f : 1);
            return new Vector2(side == CitizenDirection.West || side == CitizenDirection.North ? -.40f : .40f, .04f) * scale;
        }
        public static Pose ResolvePose(WorldState world, UnitData unit, Vector2 direction)
        {
            bool moving = unit.Path != null && unit.PathCursor < unit.Path.Count;
            bool carrying = SimulationEngine.CarriedLoad(unit) > .001f;
            long elapsed = Math.Max(0, world.Tick - unit.TaskStartedTick);
            int phase = (int)((elapsed / 2 + (uint)unit.Id % 4) % 4);
            float wave = Mathf.Sin((elapsed + (uint)unit.Id % 8) * Mathf.PI / 4);
            Pose pose = new Pose { Appearance = AppearanceFor(unit), Direction = DirectionFor(direction), SourceTask = unit.Task, Action = CitizenAction.Idle, Frame = 1, ToolFrame = phase, HeightScale = 1, FlipX = false, Warrior = false };
            bool hit = (unit.Task == UnitTask.Fight || unit.Task == UnitTask.Hunt) && !unit.IsChild && unit.AttackTimer > .5001f && unit.AttackTimer <= .8001f;
            if (hit)
            {
                pose.Action = CitizenAction.Attack; pose.DerivedGesture = true;
                Vector2 aim = direction.sqrMagnitude > .0001f ? direction.normalized : Vector2.right;
                pose.StrikeOffset = aim * (.18f * Mathf.Clamp01((unit.AttackTimer - .5f) / .3f));
                pose.UpperOffset = aim * (.045f * Mathf.Clamp01((unit.AttackTimer - .5f) / .3f)); return pose;
            }
            if (moving)
            {
                pose.Frame = unit.Task == UnitTask.Flee ? (int)((elapsed + (uint)unit.Id % 4) % 4) : phase;
                pose.Row = carrying ? 5 : 1;
                pose.Action = carrying ? CitizenAction.Carry : unit.Task == UnitTask.Flee ? CitizenAction.Flee : unit.Task == UnitTask.Patrol ? CitizenAction.Patrol : unit.Task == UnitTask.Haul ? CitizenAction.Haul : unit.Task == UnitTask.Return ? CitizenAction.Return : unit.Task == UnitTask.Caravan ? CitizenAction.Caravan : unit.Task == UnitTask.Hunt ? CitizenAction.Hunt : CitizenAction.Walk;
                return pose;
            }
            if (unit.Task == UnitTask.Fight || unit.Task == UnitTask.Hunt) { pose.Action = unit.Task == UnitTask.Hunt ? CitizenAction.Hunt : CitizenAction.Idle; return pose; }
            ResourceNodeData node = unit.ResourceCell >= 0 && unit.ResourceCell < world.Cells.Length ? world.GetResourceAt(unit.ResourceCell % world.Width, unit.ResourceCell / world.Width) : default(ResourceNodeData);
            switch (unit.Task)
            {
                case UnitTask.Gather:
                    if (!unit.IsChild && node.Amount > 0 && world.Cells[unit.ResourceCell].Fire <= .1f) { pose.Action = node.Type == ResourceType.Tree ? CitizenAction.Chop : CitizenAction.Mine; pose.Row = node.Type == ResourceType.Tree ? 3 : 2; WorkGesture(ref pose, wave, direction); }
                    break;
                case UnitTask.Farm: pose.Action = CitizenAction.Farm; pose.Row = 2; WorkGesture(ref pose, wave * (unit.IsChild ? .5f : 1), direction); break;
                case UnitTask.Process: if (!unit.IsChild && unit.WorkTimer > 0) { pose.Action = CitizenAction.Process; pose.Row = 2; WorkGesture(ref pose, wave, direction); } break;
                case UnitTask.CraftEquipment: if (!unit.IsChild && unit.EquipmentProgress > 0 && !carrying) { pose.Action = CitizenAction.Craft; pose.Row = 2; WorkGesture(ref pose, wave, direction); } break;
                case UnitTask.BuildTransport:
                    if (!unit.IsChild && !carrying && IsTransportWorkOnSite(world, unit)) { pose.Action = CitizenAction.Build; pose.Row = 2; WorkGesture(ref pose, wave, direction); } break;
                case UnitTask.Eat: pose.Action = CitizenAction.Eat; pose.DerivedGesture = true; pose.UpperOffset = new Vector2(0, -.03f + wave * .015f); break;
                case UnitTask.Rest: pose.Action = CitizenAction.Rest; pose.DerivedGesture = true; pose.HeightScale = .76f; break;
                case UnitTask.Socialize: pose.Action = CitizenAction.Socialize; pose.DerivedGesture = true; pose.UpperOffset = new Vector2(wave * .018f, 0); break;
                case UnitTask.Play: pose.Action = CitizenAction.Play; pose.DerivedGesture = true; pose.StrikeOffset = new Vector2(0, Math.Max(0, wave) * .08f); break;
                case UnitTask.Learn: pose.Action = CitizenAction.Learn; pose.DerivedGesture = true; pose.HeightScale = .94f; break;
                case UnitTask.Drink: pose.Action = CitizenAction.Drink; pose.DerivedGesture = true; pose.UpperOffset = new Vector2(0, -.07f + wave * .012f); break;
                case UnitTask.Fish: pose.Action = CitizenAction.Fish; pose.DerivedGesture = true; pose.UpperOffset = new Vector2(wave * .018f, -.025f); break;
                case UnitTask.Tend: pose.Action = CitizenAction.Tend; if (unit.WorkTimer > 0) WorkGesture(ref pose, wave * .45f, direction); break;
                case UnitTask.Brew: pose.Action = CitizenAction.Brew; if (unit.WorkTimer > 0) WorkGesture(ref pose, wave * .45f, direction); break;
                case UnitTask.Flee: pose.Action = CitizenAction.Flee; break;
                case UnitTask.Patrol: pose.Action = CitizenAction.Patrol; break;
                case UnitTask.Haul: pose.Action = CitizenAction.Haul; break;
                case UnitTask.Return: pose.Action = CitizenAction.Return; break;
                case UnitTask.Caravan: pose.Action = CitizenAction.Caravan; break;
            }
            if (carrying && pose.Action == CitizenAction.Idle) { pose.Action = CitizenAction.Carry; pose.Row = 4; }
            return pose;
        }
        private static bool IsTransportWorkOnSite(WorldState world, UnitData unit)
        {
            if (unit.TransportProjectId == 0 || world.TransportProjects == null) return false;
            for (int i = 0; i < world.TransportProjects.Count; i++)
            {
                TransportProjectData project = world.TransportProjects[i];
                if (project.Id != unit.TransportProjectId || project.WorkerId != unit.Id || project.Delivered + .00001f < project.Required) continue;
                float dx = unit.X - (project.BankCell % world.Width + .5f), dy = unit.Y - (project.BankCell / world.Width + .5f);
                return dx * dx + dy * dy < .4f;
            }
            return false;
        }
        private static void WorkGesture(ref Pose pose, float wave, Vector2 direction)
        {
            pose.DerivedGesture = true; float lean = direction.x < -.001f ? -1 : 1;
            pose.UpperOffset = new Vector2(lean * (.025f + wave * .035f), -.025f - Mathf.Abs(wave) * .025f);
        }
        public static string ActionLabel(CitizenAction action)
        {
            switch (action)
            {
                case CitizenAction.Walk: return "Ходьба"; case CitizenAction.Carry: return "Несёт груз"; case CitizenAction.Chop: return "Рубка"; case CitizenAction.Mine: return "Добыча";
                case CitizenAction.Farm: return "Работа на поле"; case CitizenAction.Process: return "Обработка"; case CitizenAction.Craft: return "Ремесло"; case CitizenAction.Build: return "Стройка";
                case CitizenAction.Attack: return "Реальный удар"; case CitizenAction.Flee: return "Бегство"; case CitizenAction.Eat: return "Еда"; case CitizenAction.Rest: return "Отдых";
                case CitizenAction.Socialize: return "Общение"; case CitizenAction.Play: return "Игра"; case CitizenAction.Learn: return "Учёба"; case CitizenAction.Drink: return "Питьё";
                case CitizenAction.Fish: return "Рыбалка"; case CitizenAction.Tend: return "Уход за стадом"; case CitizenAction.Brew: return "Пивоварение"; case CitizenAction.Patrol: return "Обход";
                case CitizenAction.Haul: return "Переноска"; case CitizenAction.Return: return "Возвращение"; case CitizenAction.Caravan: return "Караван"; case CitizenAction.Hunt: return "Охота";
                default: return "Покой";
            }
        }
        public Sprite CreatePortrait() { return CreateCitizenSprite(SpeciesType.Human, CitizenAppearance.AdultMale, CitizenDirection.South, 1); }
        public Sprite CreateAppearanceSprite(CitizenAppearance appearance, int row = 0, int frame = 0)
        { return CreateCitizenSprite(SpeciesType.Human, appearance, CitizenDirection.South, frame); }
        // UI/native gallery sees the exact production atlas. Caller owns returned Sprite.
        public Sprite CreateCitizenSprite(SpeciesType race, CitizenAppearance appearance, CitizenDirection direction, int frame = 1, UnitProfession profession = UnitProfession.None)
        {
            if (!IsCivilized(race)) throw new ArgumentOutOfRangeException(nameof(race));
            if (appearance == CitizenAppearance.Warrior) appearance = CitizenAppearance.AdultMale;
            int costume = profession == UnitProfession.Guard || (int)appearance >= 2 ? 0 : (int)profession;
            Rect uv = atlas.Region(race, (int)appearance, costume, (int)direction, frame);
            Rect rect = new Rect(Mathf.RoundToInt(uv.x * atlas.Texture.width), Mathf.RoundToInt(uv.y * atlas.Texture.height), 32, 32);
            return Sprite.Create(atlas.Texture, rect, new Vector2(.5f, 2f / 32), OriginalCitizenAtlas.PixelsPerUnit);
        }
        public void Submit()
        {
            entries.Sort(Depth);
            for (int i = 0; i < entries.Count; i++)
            { RenderEntry entry = entries[i]; DrawPose(entry.Unit, entry.Pose, entry.Uv, entry.Tint, entry.Offset, entry.Mounted); }
            batch.Submit();
        }
        public void Dispose() { batch.Dispose(); UnityEngine.Object.Destroy(material); atlas.Dispose(); }
    }
}
