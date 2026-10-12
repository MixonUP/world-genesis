using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    // Pixel Frog's original CC0 sheets. Presentation reads simulation state only.
    // Idle/run have one authored facing; vertical attacks are separate authored clips.
    public sealed class CitizenSprites : IDisposable
    {
        public enum CitizenAppearance { AdultMale, AdultFemale, Boy, Girl, Warrior }
        public struct Pose
        {
            public bool Warrior, FlipX;
            public CitizenAppearance Appearance;
            public int Row, Frame;
            public Vector2 StrikeOffset;
        }

        private const int FramePixels = 192, FramesPerRow = 6, FramesPerColor = 144;
        private const int AtlasColumns = 24;
        private const float PixelScale = 2.8f / FramePixels;
        private static readonly string[] ColorNames = { "Blue", "Red", "Purple", "Yellow" };
        private readonly Texture2D atlas;
        private readonly Material material;
        private readonly QuadBatch batch;
        private readonly RectInt[] crops = new RectInt[2];
        private readonly Rect[] frames = new Rect[FramesPerColor * 4];
        private readonly Dictionary<int, Vector2> facing = new Dictionary<int, Vector2>(1024);
        private readonly Dictionary<int, UnitData> unitLookup = new Dictionary<int, UnitData>(1024);
        public int AtlasWidth => atlas.width;
        public int AtlasHeight => atlas.height;

        public CitizenSprites(Transform parent)
        {
            Texture2D[] sources = new Texture2D[8];
            Color32[][] pixels = new Color32[8][];
            for (int color = 0; color < 4; color++) for (int role = 0; role < 2; role++)
            {
                int index = color * 2 + role;
                string path = "TinySwordsCC0/" + (role == 0 ? "Pawn_" : "Warrior_") + ColorNames[color];
                UnityEngine.Object resource = Resources.Load(path);
                Texture2D source = resource as Texture2D;
                if (source == null || !source.isReadable || source.width != 1152 || source.height != (role == 0 ? 1152 : 1536))
                    throw new InvalidOperationException("Missing or incorrectly imported readable 192px citizen sheet: " + path +
                        (source == null ? (resource == null ? " (not found)" : " (loaded " + resource.GetType().Name + ", expected Texture2D; check textureShape)") : " (" + source.width + "x" + source.height + ", readable=" + source.isReadable + ")"));
                sources[index] = source; pixels[index] = source.GetPixels32();
            }
            // A shared alpha union per role retains every original pixel, including weapon arcs,
            // while all frames retain the same pivot. Original resource PNGs are never modified.
            for (int role = 0; role < 2; role++)
            {
                int minX = FramePixels, minY = FramePixels, maxX = -1, maxY = -1;
                for (int color = 0; color < 4; color++)
                {
                    int index = color * 2 + role;
                    Color32[] data = pixels[index]; int width = sources[index].width;
                    for (int p = 0; p < data.Length; p++)
                    {
                        if (data[p].a == 0) continue;
                        int x = p % width % FramePixels, y = p / width % FramePixels;
                        minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    }
                }
                if (maxX < minX) throw new InvalidOperationException("Citizen sheet has no visible pixels.");
                crops[role] = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            }
            // Derived hair silhouettes extend beyond the original pawn crown. All pawn
            // variants share this pivot/crop; the unchanged source pixels stay lossless.
            RectInt pawnCrop = crops[0];
            crops[0] = new RectInt(Math.Max(0, pawnCrop.x - 3), Math.Max(0, pawnCrop.y - 2), pawnCrop.width + 6, pawnCrop.height + 13);
            int cellWidth = Math.Max(crops[0].width, crops[1].width) + 2;
            int cellHeight = Math.Max(crops[0].height, crops[1].height) + 2;
            int widthPixels = AtlasColumns * cellWidth, heightPixels = (frames.Length + AtlasColumns - 1) / AtlasColumns * cellHeight;
            if (SystemInfo.maxTextureSize > 0 && (widthPixels > SystemInfo.maxTextureSize || heightPixels > SystemInfo.maxTextureSize))
                throw new InvalidOperationException("Citizen atlas exceeds this graphics device's texture limit.");
            atlas = new Texture2D(widthPixels, heightPixels, TextureFormat.RGBA32, false)
            { name = "Tiny Swords CC0 • original and adapted citizens", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            Color32[] packed = new Color32[widthPixels * heightPixels];
            for (int color = 0; color < 4; color++) for (int role = 0; role < 2; role++)
            {
                int sourceIndex = color * 2 + role, rows = role == 0 ? 6 : 8;
                RectInt crop = crops[role];
                for (int row = 0; row < rows; row++) for (int frame = 0; frame < FramesPerRow; frame++)
                {
                    int index = FrameIndex(color, role != 0, row, frame);
                    int destinationX = index % AtlasColumns * cellWidth + 1, destinationY = index / AtlasColumns * cellHeight + 1;
                    int sourceX = frame * FramePixels + crop.x, sourceY = (rows - 1 - row) * FramePixels + crop.y;
                    for (int y = 0; y < crop.height; y++)
                        Array.Copy(pixels[sourceIndex], (sourceY + y) * 1152 + sourceX, packed, (destinationY + y) * widthPixels + destinationX, crop.width);
                    frames[index] = new Rect((float)destinationX / widthPixels, (float)destinationY / heightPixels, (float)crop.width / widthPixels, (float)crop.height / heightPixels);
                }
            }
            Color32[] sourceFrame = new Color32[FramePixels * FramePixels], adapted = new Color32[FramePixels * FramePixels];
            for (int color = 0; color < 4; color++) for (int variant = 0; variant < 3; variant++)
            {
                bool child = variant > 0, female = variant != 1;
                int rowCount = child ? 2 : 6;
                for (int row = 0; row < rowCount; row++) for (int frame = 0; frame < FramesPerRow; frame++)
                {
                    int sourceX = frame * FramePixels, sourceY = (5 - row) * FramePixels;
                    for (int y = 0; y < FramePixels; y++) Array.Copy(pixels[color * 2], (sourceY + y) * 1152 + sourceX, sourceFrame, y * FramePixels, FramePixels);
                    CitizenVariants.Adapt(sourceFrame, adapted, female, child);
                    int index = color * FramesPerColor + (variant == 0 ? 84 : variant == 1 ? 120 : 132) + row * FramesPerRow + frame;
                    int dx = index % AtlasColumns * cellWidth + 1, dy = index / AtlasColumns * cellHeight + 1;
                    RectInt crop = crops[0];
                    for (int y = 0; y < crop.height; y++) Array.Copy(adapted, (crop.y + y) * FramePixels + crop.x, packed, (dy + y) * widthPixels + dx, crop.width);
                    frames[index] = new Rect((float)dx / widthPixels, (float)dy / heightPixels, (float)crop.width / widthPixels, (float)crop.height / heightPixels);
                }
            }
            atlas.SetPixels32(packed); atlas.Apply(false, true);
            for (int i = 0; i < sources.Length; i++) Resources.UnloadAsset(sources[i]);
            material = new Material(Shader.Find("Sprites/Default")) { mainTexture = atlas, name = "Citizen animation atlas" };
            batch = new QuadBatch("Animated human citizens", parent, material, 4);
        }

        private static int FrameIndex(int color, bool warrior, int row, int frame)
        { return color * FramesPerColor + (warrior ? 36 : 0) + row * FramesPerRow + frame; }
        private static int FrameIndex(int color, CitizenAppearance appearance, int row, int frame)
        {
            int start = appearance == CitizenAppearance.Warrior ? 36 : appearance == CitizenAppearance.AdultFemale ? 84 : appearance == CitizenAppearance.Boy ? 120 : appearance == CitizenAppearance.Girl ? 132 : 0;
            return color * FramesPerColor + start + row * FramesPerRow + frame;
        }
        public static CitizenAppearance AppearanceFor(UnitData unit)
        {
            if (unit.IsChild) return unit.Sex == Sex.Female ? CitizenAppearance.Girl : CitizenAppearance.Boy;
            if (unit.Profession == UnitProfession.Guard) return CitizenAppearance.Warrior;
            return unit.Sex == Sex.Female ? CitizenAppearance.AdultFemale : CitizenAppearance.AdultMale;
        }

        public void Reset() { facing.Clear(); unitLookup.Clear(); batch.Clear(); batch.Submit(); }
        public Vector2 Facing(int unitId) { return facing.TryGetValue(unitId, out Vector2 value) ? value : Vector2.right; }

        public void Begin(WorldState world)
        {
            batch.Clear(); unitLookup.Clear();
            // Also bounds the presentation cache after deaths and worlds with high turnover.
            if (facing.Count > world.Units.Count * 2 + 64) facing.Clear();
            for (int i = 0; i < world.Units.Count; i++) unitLookup[world.Units[i].Id] = world.Units[i];
        }

        public void Add(WorldState world, UnitData unit, int colorIndex, Color tint)
        {
            if (!facing.TryGetValue(unit.Id, out Vector2 direction)) direction = Vector2.right;
            if (unit.Task == UnitTask.Fight && unit.TargetId != 0 && unitLookup.TryGetValue(unit.TargetId, out UnitData target))
            {
                Vector2 delta = new Vector2(target.X - unit.X, target.Y - unit.Y);
                if (delta.sqrMagnitude > .0001f) direction = delta;
            }
            else if (unit.PathCursor < unit.Path.Count)
            {
                int next = unit.Path[unit.PathCursor];
                Vector2 delta = new Vector2(next % world.Width + .5f - unit.X, next / world.Width + .5f - unit.Y);
                if (delta.sqrMagnitude > .0001f) direction = delta;
            }
            facing[unit.Id] = direction;
            Pose pose = ResolvePose(world, unit, direction);
            RectInt crop = crops[pose.Warrior ? 1 : 0];
            // Match the established settlement palette: gold, teal, blue, red, purple.
            int faction = (int)((uint)colorIndex % 5);
            int sheetColor = faction == 0 ? 3 : faction == 3 ? 1 : faction == 4 ? 2 : 0;
            Rect uv = frames[FrameIndex(sheetColor, pose.Appearance, pose.Row, pose.Frame)];
            float scale = PixelScale * (unit.IsChild ? .70f : 1);
            // Reflection must mirror the crop around the original sheet pivot, not its own centre.
            float offsetX = pose.FlipX ? FramePixels - crop.xMax - 96 : crop.x - 96;
            float offsetY = crop.y - (pose.Warrior ? 56 : 64);
            batch.Add(unit.X + offsetX * scale + pose.StrikeOffset.x, unit.Y + offsetY * scale + pose.StrikeOffset.y, crop.width * scale, crop.height * scale, uv, tint, pose.FlipX);
        }

        // Pure state-to-frame mapping, exposed so checks can verify pause, travel, work and hits.
        // No frame advances with wall-clock time, and no animation applies a gameplay effect.
        public static Pose ResolvePose(WorldState world, UnitData unit, Vector2 direction)
        {
            // Guard is reserved content. The simulation no longer assigns it automatically;
            // legacy assignments are demobilized until real recruitment/gear exists.
            bool warrior = unit.Profession == UnitProfession.Guard && unit.Age >= 16;
            bool moving = unit.Path != null && unit.PathCursor < unit.Path.Count;
            bool carrying = unit.CarriedFood + unit.CarriedWood + unit.CargoAmount > .1f;
            long elapsedTicks = Math.Max(0, world.Tick - unit.TaskStartedTick);
            int phase = (int)((elapsedTicks + unit.Id * 7L) % FramesPerRow);
            Pose pose = new Pose { Warrior = warrior, Appearance = AppearanceFor(unit), FlipX = direction.x < -.001f, Frame = phase, Row = moving ? 1 : 0 };
            if (unit.IsChild)
            {
                // Children use their own proportions and empty-handed clips. Light field
                // work is a small body gesture; actual delivery cargo remains a separate prop.
                if (!moving && unit.Task == UnitTask.Farm) pose.StrikeOffset = new Vector2(0, phase < 3 ? -.035f : .02f);
                if (!moving && unit.Task == UnitTask.Play) pose.StrikeOffset = new Vector2(0, phase == 2 || phase == 3 ? .09f : 0);
                if (!moving && (unit.Task == UnitTask.Rest || unit.Task == UnitTask.Learn)) pose.Frame = 0;
                return pose;
            }
            if (warrior)
            {
                // The simulation resets cooldown to .8 only after a real hit. Display impact
                // and recovery once; do not invent repeated strikes during idle cooldown.
                if (unit.Task == UnitTask.Fight && unit.AttackTimer > .5001f && unit.AttackTimer <= .8001f)
                {
                    pose.Row = Mathf.Abs(direction.y) > Mathf.Abs(direction.x) ? (direction.y > 0 ? 6 : 4) : 2;
                    pose.Frame = Math.Min(5, 3 + Mathf.FloorToInt((.8f - unit.AttackTimer + .0001f) * 10));
                }
                return pose;
            }
            if (unit.Task == UnitTask.Fight)
            {
                // Civilian militia keeps the unarmoured pawn. The simulation applies damage
                // before setting this cooldown: show one short body lunge, never a free weapon.
                if (unit.AttackTimer > .5001f && unit.AttackTimer <= .8001f)
                    pose.StrikeOffset = (direction.sqrMagnitude > .0001f ? direction.normalized : Vector2.right) * (.18f * Mathf.Clamp01((unit.AttackTimer - .5f) / .3f));
                return pose;
            }
            ResourceNodeData node = unit.ResourceCell >= 0 && unit.ResourceCell < world.Cells.Length ? world.GetResourceAt(unit.ResourceCell % world.Width, unit.ResourceCell / world.Width) : default(ResourceNodeData);
            if (!moving && unit.Task == UnitTask.Gather && node.Amount > 0 && world.Cells[unit.ResourceCell].Fire <= .1f)
                pose.Row = node.Type == ResourceType.Tree ? 3 : 2;
            else if (!moving && unit.Task == UnitTask.Process && unit.WorkTimer > 0)
                pose.Row = 2;
            else if (!moving && unit.Task == UnitTask.Farm)
                pose.Row = 2; // Authored hammer/build loop reused as a work gesture; not a farming clip.
            else if (carrying && unit.Task != UnitTask.Eat && unit.Task != UnitTask.Rest && unit.Task != UnitTask.Socialize)
                pose.Row = moving ? 5 : 4;
            if (unit.Task == UnitTask.Rest && !moving) pose.Frame = 0;
            return pose;
        }

        public Sprite CreatePortrait()
        {
            Rect uv = frames[0]; RectInt crop = crops[0];
            // Portrait excludes animation's deliberately large transparent weapon margins.
            Rect rect = new Rect(uv.x * atlas.width + 62 - crop.x, uv.y * atlas.height + 63 - crop.y, 68, 66);
            return Sprite.Create(atlas, rect, new Vector2(.5f, .5f), 64);
        }
        // The caller owns the returned Sprite. Used by UI/controlled native art inspection;
        // it exposes the same atlas frame used by citizens, not a separately drawn mock-up.
        public Sprite CreateAppearanceSprite(CitizenAppearance appearance, int row = 0, int frame = 0)
        {
            int rows = appearance == CitizenAppearance.Warrior ? 8 : appearance == CitizenAppearance.Boy || appearance == CitizenAppearance.Girl ? 2 : 6;
            row = Mathf.Clamp(row, 0, rows - 1); frame = Mathf.Clamp(frame, 0, FramesPerRow - 1);
            Rect uv = frames[FrameIndex(0, appearance, row, frame)];
            Rect rect = new Rect(Mathf.RoundToInt(uv.x * atlas.width), Mathf.RoundToInt(uv.y * atlas.height), Mathf.RoundToInt(uv.width * atlas.width), Mathf.RoundToInt(uv.height * atlas.height));
            return Sprite.Create(atlas, rect, new Vector2(.5f, .5f), 64);
        }

        public void Submit() { batch.Submit(); }
        public void Dispose() { batch.Dispose(); UnityEngine.Object.Destroy(material); UnityEngine.Object.Destroy(atlas); }
    }
}
