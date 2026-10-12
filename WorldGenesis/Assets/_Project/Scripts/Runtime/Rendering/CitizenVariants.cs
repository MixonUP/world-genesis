using System;
using UnityEngine;

namespace WorldGenesis.Runtime
{
    // Startup-only derivatives of the imported CC0 pawn frames. Source PNGs remain unchanged.
    // The shared head component is followed in every frame, so tools and strike arcs retain
    // their original palette, timing and position. No simulation or random state is touched.
    internal static class CitizenVariants
    {
        private const int Size = 192;
        private static readonly Color32 Outline = new Color32(22, 28, 46, 255);
        private static readonly Color32 Brown = new Color32(112, 62, 45, 255);
        private static readonly Color32 Light = new Color32(178, 108, 65, 255);
        private static readonly Color32 Dark = new Color32(70, 43, 33, 255);

        public static void Adapt(Color32[] source, Color32[] output, bool female, bool child)
        {
            Array.Copy(source, output, source.Length);
            if (female) FeminineSilhouette(source, output, child);
            if (!child) return;
            // Retain the planted feet/shadow, shorten the torso, preserve the head's pixels.
            // This changes proportions as well as the draw scale; children are not tiny adults.
            Color32[] adult = (Color32[])output.Clone();
            Array.Clear(output, 0, output.Length);
            for (int y = 0; y < Size; y++)
            {
                int targetY = y < 64 ? y : y < 90 ? 64 + (y - 64) * 17 / 26 : y - 9;
                for (int x = 0; x < Size; x++)
                {
                    Color32 c = adult[y * Size + x];
                    if (c.a > 0) output[targetY * Size + x] = c;
                }
            }
        }

        private static void FeminineSilhouette(Color32[] source, Color32[] output, bool child)
        {
            // Hair and axe highlights share source colours. A connected head mask avoids
            // recolouring the axe in its swing frames. This allocation happens at atlas creation.
            bool[] visited = new bool[Size * Size];
            int[] queue = new int[Size * Size], head = new int[Size * Size];
            int best = 0;
            for (int y = 82; y < 143; y++) for (int x = 61; x < 134; x++)
            {
                int start = y * Size + x;
                if (visited[start] || !Hair(source[start])) continue;
                int read = 0, count = 1; queue[0] = start; visited[start] = true;
                while (read < count)
                {
                    int index = queue[read++], px = index % Size, py = index / Size;
                    if (px > 60) Visit(index - 1, source, visited, queue, ref count);
                    if (px < 134) Visit(index + 1, source, visited, queue, ref count);
                    if (py > 81) Visit(index - Size, source, visited, queue, ref count);
                    if (py < 143) Visit(index + Size, source, visited, queue, ref count);
                }
                if (count > best) { best = count; Array.Copy(queue, head, count); }
            }
            if (best == 0) return;
            int minX = Size, maxX = 0, minY = Size, maxY = 0;
            for (int i = 0; i < best; i++)
            {
                int index = head[i], x = index % Size, y = index / Size;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                Color32 c = source[index];
                output[index] = c.r == 239 ? Light : c.r == 213 ? Brown : c.r == 255 ? new Color32(221, 159, 95, 255) : Dark;
            }
            int cx = (minX + maxX) / 2;
            Color32[] decoration = new Color32[Size * Size];
            if (child)
            {
                // Two short tied plaits, readable even when a child's body is smaller.
                Ellipse(decoration, minX + 2, minY + 7, 5, 9, Outline, false);
                Ellipse(decoration, minX + 2, minY + 8, 3, 7, Brown, false);
                Ellipse(decoration, maxX - 2, minY + 7, 5, 9, Outline, false);
                Ellipse(decoration, maxX - 2, minY + 8, 3, 7, Brown, false);
            }
            else
            {
                // Bun behind the crown; a braid projects beside the shoulder, not over tools.
                Ellipse(decoration, cx - 9, maxY + 3, 9, 7, Outline, false);
                Ellipse(decoration, cx - 9, maxY + 4, 7, 5, Brown, false);
                Ellipse(decoration, cx - 10, maxY + 5, 4, 2, Light, false);
                for (int i = 0; i < 4; i++)
                {
                    int bx = minX + 4 - i % 2, by = minY + 6 - i * 4;
                    Ellipse(decoration, bx, by, 4, 4, Outline, false);
                    Ellipse(decoration, bx, by + 1, 2, 3, Brown, false);
                    Ellipse(decoration, bx - 1, by + 1, 1, 2, Light, false);
                }
                // A light work apron changes the torso silhouette/palette in every occupation.
                for (int y = minY - 13; y < minY - 1; y++) for (int x = cx - 5; x <= cx + 7; x++)
                {
                    if (x < 0 || y < 0 || x >= Size || y >= Size) continue;
                    Color32 c = source[y * Size + x];
                    if (Cloth(c)) output[y * Size + x] = x < cx - 2 ? new Color32(161, 146, 112, 255) : new Color32(222, 211, 166, 255);
                }
            }
            for (int i = 0; i < output.Length; i++) if (source[i].a == 0 && decoration[i].a > 0) output[i] = decoration[i];
        }
        private static bool Hair(Color32 c)
        {
            return c.a == 255 && ((c.r == 239 && c.g == 225 && c.b == 171) ||
                (c.r == 213 && c.g == 181 && c.b == 131) || (c.r == 151 && c.g == 123 && c.b == 107) ||
                (c.r == 255 && c.g == 255 && c.b == 255));
        }
        private static bool Cloth(Color32 c)
        {
            // Exact two-tone cloth palettes of all four CC0 pawn sheets. Do not assume
            // blue > red: that would silently remove the apron in red/yellow settlements.
            if (c.a != 255) return false;
            return (c.r == 64 && c.g == 78 && c.b == 117) || (c.r == 62 && c.g == 134 && c.b == 152) ||
                (c.r == 105 && c.g == 61 && c.b == 91) || (c.r == 182 && c.g == 85 && c.b == 85) ||
                (c.r == 67 && c.g == 61 && c.b == 91) || (c.r == 119 && c.g == 83 && c.b == 150) ||
                (c.r == 111 && c.g == 90 && c.b == 72) || (c.r == 179 && c.g == 166 && c.b == 69);
        }
        private static void Visit(int index, Color32[] source, bool[] visited, int[] queue, ref int count)
        {
            if (visited[index] || !Hair(source[index])) return;
            visited[index] = true; queue[count++] = index;
        }
        private static void Ellipse(Color32[] pixels, int cx, int cy, int rx, int ry, Color32 color, bool behind)
        {
            for (int y = cy - ry; y <= cy + ry; y++) for (int x = cx - rx; x <= cx + rx; x++)
            {
                if (x < 0 || y < 0 || x >= Size || y >= Size || (x - cx) * (x - cx) * ry * ry + (y - cy) * (y - cy) * rx * rx > rx * rx * ry * ry) continue;
                int index = y * Size + x;
                if (!behind || pixels[index].a == 0) pixels[index] = color;
            }
        }
    }
}
