using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    // Original authored sheets are retained unchanged. This startup-only packer aligns
    // one alpha union/pivot per appearance and composes the original wardrobe adapter.
    public sealed class OriginalCitizenAtlas : IDisposable
    {
        public const int Cell = 32, Frames = 4, Costumes = 8, Columns = 32;
        public const float PixelsPerUnit = 24;
        private const int FrameCount = 4 * 4 * Costumes * 4 * Frames;
        public Texture2D Texture { get; private set; }
        private readonly Vector2[] headAnchors = new Vector2[FrameCount];
        private static readonly string[] Names = { "Human", "Elf", "Dwarf", "Orc" };

        public OriginalCitizenAtlas()
        {
            int width = Columns * Cell, height = (FrameCount + OriginalCitizenTools.FrameCount) / Columns * Cell;
            if (SystemInfo.maxTextureSize > 0 && Math.Max(width, height) > SystemInfo.maxTextureSize)
                throw new InvalidOperationException("Original citizen atlas exceeds this graphics device's texture limit.");
            Color32[] packed = new Color32[width * height];
            Color32[] body = new Color32[Cell * Cell], back = new Color32[Cell * Cell], front = new Color32[Cell * Cell];
            for (int race = 0; race < 4; race++)
            {
                string path = "OriginalCitizens/" + Names[race];
                Texture2D source = Resources.Load<Texture2D>(path);
                if (source == null || !source.isReadable || source.width != source.height)
                    throw new InvalidOperationException("Missing readable square original citizen sheet: " + path);
                try
                {
                    Color32[] pixels = source.GetPixels32();
                    RectInt[] sourceFrames = Segment(pixels, source.width, source.height);
                    for (int appearance = 0; appearance < 4; appearance++)
                    {
                        int maxWidth = 0, maxHeight = 0;
                        for (int i = appearance * 16; i < appearance * 16 + 16; i++)
                        { maxWidth = Math.Max(maxWidth, sourceFrames[i].width); maxHeight = Math.Max(maxHeight, sourceFrames[i].height); }
                        for (int direction = 0; direction < 4; direction++) for (int frame = 0; frame < Frames; frame++)
                        {
                            int row = appearance * 2 + (direction == 1 || direction == 3 ? 1 : 0);
                            int column = frame + (direction == 1 || direction == 2 ? 4 : 0);
                            Normalize(pixels, source.width, appearance, sourceFrames[row * 8 + column], maxWidth, maxHeight, body);
                            int headTop = CitizenWardrobe.FindHeadTop(body);
                            for (int costume = 0; costume < Costumes; costume++)
                            {
                                CitizenWardrobe.Paint(back, (SpeciesType)race, (WardrobeAppearance)appearance, (UnitProfession)costume, (WardrobeFacing)direction, frame, false, false, headTop);
                                CitizenWardrobe.Paint(front, (SpeciesType)race, (WardrobeAppearance)appearance, (UnitProfession)costume, (WardrobeFacing)direction, frame, true, false, headTop);
                                int index = Index((SpeciesType)race, appearance, costume, direction, frame);
                                int left = Cell, right = -1;
                                for (int x = 8; x <= 24; x++) if (body[Math.Max(0, headTop - 1) * Cell + x].a >= 128) { left = Math.Min(left, x); right = Math.Max(right, x); }
                                headAnchors[index] = new Vector2(right >= left ? (left + right + 1) * .5f : 16, headTop + 1);
                                int dx = index % Columns * Cell, dy = index / Columns * Cell;
                                for (int y = 0; y < Cell; y++) for (int x = 0; x < Cell; x++)
                                {
                                    int p = y * Cell + x;
                                    Color32 result = Over(body[p], back[p]);
                                    packed[(dy + y) * width + dx + x] = Over(front[p], result);
                                }
                            }
                        }
                    }
                }
                finally { Resources.UnloadAsset(source); }
            }
            for (int tool = 1; tool <= 4; tool++) for (int direction = 0; direction < 4; direction++) for (int frame = 0; frame < Frames; frame++)
            {
                OriginalCitizenTools.Paint(body, (OriginalCitizenTools.Tool)tool, (CitizenSprites.CitizenDirection)direction, frame);
                int index = ToolIndex((OriginalCitizenTools.Tool)tool, direction, frame);
                int dx = index % Columns * Cell, dy = index / Columns * Cell;
                for (int y = 0; y < Cell; y++) Array.Copy(body, y * Cell, packed, (dy + y) * width + dx, Cell);
            }
            Texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { name = "World Genesis • original four-direction citizens", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            Texture.SetPixels32(packed); Texture.Apply(false, true);
        }

        private static Color32 Over(Color32 top, Color32 bottom)
        {
            if (top.a == 0) return bottom;
            if (top.a == 255 || bottom.a == 0) return top;
            float alpha = top.a / 255f, under = bottom.a / 255f * (1 - alpha), total = alpha + under;
            return new Color32((byte)((top.r * alpha + bottom.r * under) / total), (byte)((top.g * alpha + bottom.g * under) / total), (byte)((top.b * alpha + bottom.b * under) / total), (byte)(total * 255));
        }

        private static List<Vector2Int> Bands(bool[] occupied)
        {
            List<Vector2Int> result = new List<Vector2Int>();
            for (int start = 0; start < occupied.Length; start++)
            {
                if (!occupied[start]) continue;
                int end = start + 1; while (end < occupied.Length && occupied[end]) end++;
                // Stray antialiasing flecks cannot become a separate character or row.
                if (end - start >= 4) result.Add(new Vector2Int(start, end));
                start = end;
            }
            return result;
        }
        private static RectInt[] Segment(Color32[] source, int width, int height)
        {
            bool[] rows = new bool[height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                if (source[y * width + x].a >= 128) { rows[y] = true; break; }
            List<Vector2Int> bands = Bands(rows);
            if (bands.Count != 8) throw new InvalidOperationException("Original citizen atlas must have eight separated alpha rows; found " + bands.Count);
            RectInt[] result = new RectInt[64];
            for (int row = 0; row < 8; row++)
            {
                Vector2Int band = bands[7 - row]; // Image file rows are top-down, Unity pixels bottom-up.
                bool[] columns = new bool[width];
                for (int x = 0; x < width; x++) for (int y = band.x; y < band.y; y++)
                    if (source[y * width + x].a >= 128) { columns[x] = true; break; }
                List<Vector2Int> figures = Bands(columns);
                if (figures.Count != 8) throw new InvalidOperationException("Original citizen row " + row + " must have eight separated figures; found " + figures.Count);
                for (int column = 0; column < 8; column++)
                {
                    Vector2Int figure = figures[column]; int bottom = band.y, top = band.x;
                    for (int y = band.x; y < band.y; y++) for (int x = figure.x; x < figure.y; x++)
                        if (source[y * width + x].a >= 128) { bottom = Math.Min(bottom, y); top = Math.Max(top, y); }
                    result[row * 8 + column] = new RectInt(figure.x, bottom, figure.y - figure.x, top - bottom + 1);
                }
            }
            return result;
        }
        private static void Normalize(Color32[] source, int width, int appearance, RectInt figure, int maxWidth, int maxHeight, Color32[] target)
        {
            Array.Clear(target, 0, target.Length);
            bool child = appearance >= 2;
            // A shared scale per appearance preserves authored differences between steps.
            // Every frame uses the same foot pivot rather than the generated sheet's spacing.
            float scale = Math.Min((child ? 21f : 28f) / maxHeight, (child ? 20f : 24f) / maxWidth);
            float center = figure.x + figure.width * .5f;
            for (int y = 2; y < Cell - 1; y++) for (int x = 1; x < Cell - 1; x++)
            {
                int px = Mathf.FloorToInt(center + (x + .5f - 16) / scale);
                int py = Mathf.FloorToInt(figure.y + (y + .5f - 2) / scale);
                if (px < figure.xMin || px >= figure.xMax || py < figure.yMin || py >= figure.yMax) continue;
                Color32 color = source[py * width + px];
                // Keep silhouettes crisp at the logical pixel scale; no bilinear resampling.
                if (color.a >= 128) { color.a = 255; target[y * Cell + x] = color; }
            }
        }

        private static int Index(SpeciesType race, int appearance, int costume, int direction, int frame)
        { return (((Mathf.Clamp((int)race, 0, 3) * 4 + Mathf.Clamp(appearance, 0, 3)) * Costumes + Mathf.Clamp(costume, 0, Costumes - 1)) * 4 + Mathf.Clamp(direction, 0, 3)) * Frames + Mathf.Clamp(frame, 0, Frames - 1); }
        public Rect Region(SpeciesType race, int appearance, int costume, int direction, int frame)
        {
            int index = Index(race, appearance, costume, direction, frame);
            return new Rect((float)(index % Columns * Cell) / Texture.width, (float)(index / Columns * Cell) / Texture.height, (float)Cell / Texture.width, (float)Cell / Texture.height);
        }
        public Vector2 HeadAnchor(SpeciesType race, int appearance, int costume, int direction, int frame)
        { return headAnchors[Index(race, appearance, costume, direction, frame)]; }
        private static int ToolIndex(OriginalCitizenTools.Tool tool, int direction, int frame) { return FrameCount + (((int)tool - 1) * 4 + direction) * Frames + frame; }
        public Rect ToolRegion(OriginalCitizenTools.Tool tool, CitizenSprites.CitizenDirection direction, int frame)
        {
            int index = ToolIndex(tool, (int)direction, Mathf.Clamp(frame, 0, 3));
            return new Rect((float)(index % Columns * Cell) / Texture.width, (float)(index / Columns * Cell) / Texture.height, (float)Cell / Texture.width, (float)Cell / Texture.height);
        }
        public static float RaceScale(SpeciesType species) { return species == SpeciesType.Dwarf ? .88f : species == SpeciesType.Elf ? 1.04f : species == SpeciesType.Orc ? 1.02f : 1; }
        public void Dispose() { if (Texture != null) UnityEngine.Object.Destroy(Texture); }
    }
}
