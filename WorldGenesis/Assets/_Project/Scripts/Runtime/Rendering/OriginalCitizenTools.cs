using System;
using UnityEngine;

namespace WorldGenesis.Runtime
{
    // Original pixel working props, generated once. These represent primitive tools
    // during a verified action, not issued weapons, inventory or a combat bonus.
    public static class OriginalCitizenTools
    {
        public enum Tool { None, Axe, Pickaxe, Hoe, Hammer }
        public const int FrameCount = 4 * 4 * 4;
        private static readonly Color32 Ink = new Color32(48, 43, 40, 255);
        private static readonly Color32 Wood = new Color32(148, 111, 73, 255);
        private static readonly Color32 Metal = new Color32(133, 147, 148, 255);
        private static readonly Color32 Edge = new Color32(200, 205, 181, 255);
        private static readonly float[] Swing = { -.65f, -.12f, .60f, .18f };
        public static Tool ForAction(CitizenSprites.CitizenAction action)
        {
            if (action == CitizenSprites.CitizenAction.Chop) return Tool.Axe;
            if (action == CitizenSprites.CitizenAction.Mine) return Tool.Pickaxe;
            if (action == CitizenSprites.CitizenAction.Farm) return Tool.Hoe;
            if (action == CitizenSprites.CitizenAction.Process || action == CitizenSprites.CitizenAction.Craft || action == CitizenSprites.CitizenAction.Build) return Tool.Hammer;
            return Tool.None;
        }
        public static void Paint(Color32[] pixels, Tool tool, CitizenSprites.CitizenDirection direction, int frame)
        {
            if (pixels == null || pixels.Length != 32 * 32) throw new ArgumentException("Tool frame must be 32x32.");
            Array.Clear(pixels, 0, pixels.Length); if (tool == Tool.None) return;
            Vector2 facing = CitizenSprites.DirectionVector(direction);
            float angle = Mathf.Atan2(facing.y, facing.x) - Mathf.PI * .5f + Swing[Mathf.Clamp(frame, 0, 3)];
            float cosine = Mathf.Cos(angle), sine = Mathf.Sin(angle);
            Line(pixels, 0, -2, 0, 13, 1, Ink, cosine, sine);
            Line(pixels, 0, -1, 0, 12, 0, Wood, cosine, sine);
            switch (tool)
            {
                case Tool.Axe:
                    Line(pixels, 0, 11, 5, 11, 2, Ink, cosine, sine);
                    Line(pixels, 1, 11, 4, 11, 1, Metal, cosine, sine);
                    Line(pixels, 5, 9, 5, 13, 0, Edge, cosine, sine); break;
                case Tool.Pickaxe:
                    Line(pixels, -6, 10, 0, 13, 1, Ink, cosine, sine); Line(pixels, 0, 13, 6, 10, 1, Ink, cosine, sine);
                    Line(pixels, -5, 11, 0, 13, 0, Metal, cosine, sine); Line(pixels, 0, 13, 5, 11, 0, Edge, cosine, sine); break;
                case Tool.Hoe:
                    Line(pixels, -1, 12, 5, 12, 1, Ink, cosine, sine); Line(pixels, 4, 9, 4, 13, 1, Ink, cosine, sine);
                    Line(pixels, 0, 12, 4, 12, 0, Metal, cosine, sine); Line(pixels, 5, 9, 5, 12, 0, Edge, cosine, sine); break;
                case Tool.Hammer:
                    Line(pixels, -4, 12, 4, 12, 2, Ink, cosine, sine);
                    Line(pixels, -3, 12, 3, 12, 1, Metal, cosine, sine); Line(pixels, -3, 13, 3, 13, 0, Edge, cosine, sine); break;
            }
        }
        private static void Line(Color32[] pixels, int x0, int y0, int x1, int y1, int radius, Color32 color, float cosine, float sine)
        {
            int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            for (int step = 0; step <= steps; step++)
            {
                float t = steps == 0 ? 0 : (float)step / steps;
                float sx = Mathf.Lerp(x0, x1, t), sy = Mathf.Lerp(y0, y1, t);
                int cx = Mathf.RoundToInt(16 + sx * cosine - sy * sine), cy = Mathf.RoundToInt(16 + sx * sine + sy * cosine);
                for (int y = cy - radius; y <= cy + radius; y++) for (int x = cx - radius; x <= cx + radius; x++)
                    if (x >= 1 && x < 31 && y >= 1 && y < 31) pixels[y * 32 + x] = color;
            }
        }
    }
}
