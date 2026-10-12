using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    // Authored pixel recipes. Rendering randomness never consumes the world's RNG.
    public static class PixelArt
    {
        public const int Frame = 32;
        public const int AtlasWidth = 256, AtlasHeight = 512;
        // Must precede every field initializer that calls Hex, including the terrain palette.
        private static readonly Dictionary<string, Color> PaletteCache = new Dictionary<string, Color>(128, StringComparer.Ordinal);
        private static readonly Color[] TerrainPalette = { Hex("163f5c"), Hex("28798b"), Hex("ddc992"), Hex("7eaf70"), Hex("437952"), Hex("c9a775"), Hex("cddfdc"), Hex("778487") };
        public static readonly Color Ink = Hex("101d29");
        public static readonly Color Gold = Hex("d5b36d");
        public static readonly Color Teal = Hex("72d4bf");
        public static readonly Color[] Factions = { Hex("f2bf73"), Hex("73d8bb"), Hex("a6b8ed"), Hex("ed8677"), Hex("d5a9d9"), Hex("d4d979") };
        public static Color Hex(string hex)
        {
            if (PaletteCache.TryGetValue(hex, out Color color)) return color;
            ColorUtility.TryParseHtmlString("#" + hex, out color);
            PaletteCache.Add(hex, color); return color;
        }
        public static uint Hash(int x, int y, int seed = 0) { unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1447); h = (h ^ (h >> 13)) * 1274126177; return h ^ (h >> 16); } }
        public static Color Terrain(TerrainType terrain)
        {
            int index = (int)terrain;
            return (uint)index < TerrainPalette.Length ? TerrainPalette[index] : Color.magenta;
        }

        public static Texture2D CreateAtlas()
        {
            Texture2D atlas = new Texture2D(AtlasWidth, AtlasHeight, TextureFormat.RGBA32, false);
            atlas.name = "World Genesis • authored pixel atlas";
            atlas.filterMode = FilterMode.Point;
            atlas.wrapMode = TextureWrapMode.Clamp;
            Color32[] pixels = new Color32[AtlasWidth * AtlasHeight];
            Action<int, int, int, Color> dot = (sprite, x, y, color) => { if (x >= 0 && x < 32 && y >= 0 && y < 32) pixels[(sprite / 8 * 32 + y) * 256 + sprite % 8 * 32 + x] = color; };
            Action<int, int, int, int, int, Color> rect = (sprite, x, y, w, h, color) => { for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++) dot(sprite, xx, yy, color); };
            Action<int, int, int, int, int, Color> ellipse = (sprite, cx, cy, rx, ry, color) => { for (int y = -ry; y <= ry; y++) for (int x = -rx; x <= rx; x++) if ((float)x * x / (rx * rx) + (float)y * y / (ry * ry) <= 1) dot(sprite, cx + x, cy + y, color); };
            Color shadow = new Color(.06f, .12f, .14f, .32f);
            // Broadleaf tree: offset crown clusters and a lit top contour.
            ellipse(0, 16, 5, 10, 3, shadow); rect(0, 14, 5, 4, 13, Hex("655443")); rect(0, 16, 6, 2, 12, Hex("977852"));
            ellipse(0, 15, 19, 11, 9, Hex("244d43")); ellipse(0, 11, 21, 7, 6, Hex("346e4c")); ellipse(0, 20, 20, 8, 7, Hex("3f8051")); ellipse(0, 15, 26, 7, 5, Hex("56935c")); ellipse(0, 11, 26, 4, 3, Hex("7aae68"));
            // Fir.
            ellipse(1, 16, 4, 9, 3, shadow); rect(1, 15, 4, 3, 9, Hex("84634a"));
            for (int layer = 0; layer < 3; layer++) for (int y = 0; y < 11; y++) { int half = 10 - y; for (int x = -half; x <= half; x++) dot(1, 16 + x, 7 + layer * 6 + y, x < 0 ? Hex("367b66") : Hex("245647")); }
            rect(1, 13, 20, 2, 5, Hex("75a383"));
            // Four architecture silhouettes: hall, house, farm, tower.
            for (int s = 2; s <= 5; s++)
            {
                ellipse(s, 16, 4, 13, 3, shadow);
                if (s == 4) { rect(s, 3, 5, 25, 16, Hex("785a3b")); for (int x = 5; x < 28; x += 4) { rect(s, x, 6, 2, 14, Hex("c8ab58")); rect(s, x, 14, 1, 6, Hex("e3c976")); } continue; }
                int wallX = s == 5 ? 9 : 6, wallWidth = s == 5 ? 14 : 21, wallHeight = s == 5 ? 22 : 13;
                rect(s, wallX, 5, wallWidth, wallHeight, Hex("c6bda0")); rect(s, wallX, 5, 3, wallHeight, Hex("8b927e"));
                rect(s, 14, 5, 5, 7, Hex("394956")); rect(s, 9, 11, 3, 4, Hex("49616a")); rect(s, 22, 11, 3, 4, Hex("efd288"));
                if (s == 5) { rect(s, 7, 24, 18, 5, Hex("a4aba0")); for (int x = 8; x < 26; x += 6) rect(s, x, 28, 3, 3, Hex("c6c9ad")); }
                else { for (int y = 0; y < 11; y++) for (int x = y / 2; x < 28 - y / 2; x++) dot(s, x + 2, 16 + y, y % 3 == 0 ? Hex("7b4c44") : Hex(s == 2 ? "ac6650" : "a7774e")); rect(s, 20, 23, 3, 7, Hex("657278")); }
            }
            // Human, elf, dwarf, orc: white garments carry a faction tint at draw time.
            Color[] skin = { Hex("edc392"), Hex("d8caa7"), Hex("d1ad86"), Hex("9bae70") };
            for (int s = 6; s < 10; s++)
            {
                ellipse(s, 16, 5, 5, 2, shadow); rect(s, 13, 5, 2, 4, Hex("39464a")); rect(s, 17, 5, 2, 4, Hex("39464a"));
                rect(s, 12, 9, 8, 6, Color.white); rect(s, 11, 10, 2, 4, skin[s - 6]); rect(s, 20, 10, 2, 4, skin[s - 6]);
                rect(s, 13, 15, 6, 6, skin[s - 6]); rect(s, 13, 20, 6, 2, Hex(s == 7 ? "dae2c5" : s == 8 ? "915239" : "625140")); dot(s, 17, 17, Ink);
                if (s == 7) { dot(s, 11, 17, skin[1]); dot(s, 20, 17, skin[1]); }
                if (s == 8) rect(s, 13, 14, 6, 3, Hex("a36640"));
            }
            ellipse(10, 16, 5, 7, 2, shadow); rect(10, 10, 5, 2, 5, Ink); rect(10, 20, 5, 2, 5, Ink); ellipse(10, 16, 12, 8, 5, Hex("e7e3c8")); rect(10, 21, 11, 5, 5, Hex("89999a")); dot(10, 24, 14, Ink);
            ellipse(11, 16, 5, 8, 2, shadow); rect(11, 10, 5, 2, 7, Hex("526373")); rect(11, 21, 5, 2, 7, Hex("526373")); ellipse(11, 16, 12, 9, 4, Hex("758994")); rect(11, 23, 12, 6, 5, Hex("9bafb2")); rect(11, 24, 16, 2, 3, Hex("526373")); dot(11, 27, 15, Ink);
            // White pixels form particles, flames and cursor mesh.
            rect(12, 1, 1, 30, 30, Color.white);
            // Faceted mountain with a broken snow line, and a low rubble silhouette.
            ellipse(13, 16, 4, 14, 3, shadow);
            for (int y = 0; y < 25; y++)
            {
                int center = 16 - y / 9, half = (25 - y) * 3 / 5;
                for (int x = -half; x <= half; x++)
                {
                    Color rock = x < -1 ? Hex("9ca997") : x < 3 ? Hex("778987") : Hex("526b76");
                    if (y > 17 + (x > 1 ? 2 : 0) + (Math.Abs(x) % 3)) rock = x < 1 ? Hex("e1e7d7") : Hex("afc5c4");
                    if (y < 7 && (x + y * 3) % 11 == 0) rock = Hex("62796f");
                    dot(13, center + x, y + 5, rock);
                }
            }
            ellipse(14, 16, 6, 11, 3, shadow); ellipse(14, 15, 12, 9, 7, Hex("647981")); ellipse(14, 12, 15, 6, 4, Hex("9baba2")); ellipse(14, 21, 10, 6, 4, Hex("7b8d8b"));
            // Terrain tool glyphs, built from the same visual alphabet as the map.
            for (int s = 15; s <= 16; s++)
            {
                ellipse(s, 16, 14, 13, 10, Hex(s == 15 ? "20536e" : "348c99"));
                for (int y = 9; y <= 20; y += 5) { rect(s, 6, y, 7, 2, Hex("79bdc6")); rect(s, 13, y - 1, 8, 2, Hex("79bdc6")); rect(s, 21, y, 4, 2, Hex("79bdc6")); }
            }
            ellipse(17, 16, 11, 13, 6, Hex("c9ac74")); ellipse(17, 14, 15, 11, 6, Hex("e6d399")); rect(17, 8, 18, 7, 2, Hex("fff0be")); dot(17, 20, 14, Hex("af905b")); dot(17, 23, 12, Hex("af905b"));
            ellipse(18, 16, 7, 12, 3, Hex("4f7857"));
            for (int x = 6; x <= 25; x += 4) { rect(18, x, 8, 2, 8 + x % 7, Hex("90bd78")); rect(18, x - 2, 13, 2, 5, Hex("6b9d63")); rect(18, x + 2, 16, 2, 4, Hex("b1ce83")); }
            ellipse(19, 16, 7, 13, 3, Hex("c5a16d")); rect(19, 14, 7, 5, 20, Hex("739963")); rect(19, 7, 14, 9, 4, Hex("668b56")); rect(19, 7, 15, 3, 9, Hex("84a866")); rect(19, 18, 18, 8, 3, Hex("668b56")); rect(19, 23, 20, 3, 6, Hex("84a866")); rect(19, 16, 8, 1, 17, Hex("a5b881"));
            rect(20, 15, 3, 2, 26, Hex("dcebef")); rect(20, 3, 15, 26, 2, Hex("dcebef"));
            for (int i = -10; i <= 10; i++) { dot(20, 16 + i, 16 + i, Hex("98c7d0")); dot(20, 16 + i, 16 - i, Hex("98c7d0")); }
            for (int s = 21; s <= 27; s++) ellipse(s, 16, 5, 10, 2, shadow);
            for (int y = 0; y < 24; y++)
            {
                int half = y < 8 ? 4 + y / 2 : (26 - y) / 3;
                for (int x = -half; x <= half; x++) { dot(21, 16 + x + (y > 16 ? (y - 16) / 3 : 0), 5 + y, Hex("e87943")); if (Math.Abs(x) < half / 2 && y < 16) dot(21, 16 + x, 5 + y, Hex("ffd889")); }
            }
            ellipse(22, 16, 21, 12, 5, Hex("d4e4df")); ellipse(22, 12, 25, 6, 4, Hex("ecf0da"));
            for (int x = 7; x < 28; x += 6) { rect(22, x, 8, 2, 6, Hex("6db6cd")); rect(22, x - 1, 4, 2, 4, Hex("6db6cd")); }
            for (int y = 3; y < 30; y++) { int x = y < 15 ? 11 + y / 2 : 7 + y / 2; rect(23, x, y, y < 17 ? 5 : 7, 1, Hex("f3dc87")); } rect(23, 14, 14, 11, 3, Hex("fff5cb"));
            for (int k = 0; k < 16; k++) { rect(24, 13 + k, 14 + k, 4, 2, Hex(k < 8 ? "e58751" : "b2573e")); }
            ellipse(24, 12, 11, 9, 8, Hex("704f46")); ellipse(24, 10, 14, 7, 5, Hex("a7795b")); ellipse(24, 8, 16, 3, 2, Hex("c09c70"));
            rect(25, 12, 5, 8, 24, Hex("7ad4ad")); rect(25, 4, 13, 24, 8, Hex("7ad4ad")); rect(25, 14, 7, 3, 19, Hex("baf1c5"));
            for (int y = -12; y <= 12; y++) for (int x = -12; x <= 12; x++) if (Math.Abs(x) + Math.Abs(y) < 13 && (Math.Abs(x) < 3 || Math.Abs(y) < 3 || Math.Abs(x) + Math.Abs(y) < 7)) dot(26, 16 + x, 16 + y, Hex("efd790"));
            ellipse(27, 16, 19, 10, 9, Hex("b7c789")); rect(27, 10, 7, 13, 6, Hex("b7c789")); rect(27, 9, 17, 5, 4, Ink); rect(27, 19, 17, 5, 4, Ink); rect(27, 15, 12, 3, 4, Hex("697e56")); for (int x = 11; x < 24; x += 4) rect(27, x, 6, 2, 5, Ink);
            // Open book is the world-law symbol; on/off remains explicit in UI text.
            rect(28, 3, 6, 26, 20, Hex("6d827a")); rect(28, 4, 8, 11, 20, Hex("e4d4aa")); rect(28, 17, 8, 11, 20, Hex("cbbd96"));
            for (int y = 12; y < 26; y += 4) { rect(28, 6, y, 7, 1, Hex("9f986f")); rect(28, 19, y, 7, 1, Hex("a29873")); }
            ResourceArt.PaintAtlas(pixels, AtlasWidth);
            atlas.SetPixels32(pixels); atlas.Apply(false, true); return atlas;
        }

        public static Rect Uv(int sprite) { return new Rect((sprite % 8 * Frame + .25f) / AtlasWidth, (sprite / 8 * Frame + .25f) / AtlasHeight, 31.5f / AtlasWidth, 31.5f / AtlasHeight); }
    }

    public sealed class QuadBatch : IDisposable
    {
        private readonly GameObject root;
        private readonly Mesh mesh;
        private readonly List<Vector3> vertices = new List<Vector3>(4096);
        private readonly List<Vector2> uv = new List<Vector2>(4096);
        private readonly List<Color32> colors = new List<Color32>(4096);
        private readonly List<int> triangles = new List<int>(6144);
        public QuadBatch(string name, Transform parent, Material material, int order)
        {
            root = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); root.transform.SetParent(parent, false);
            mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 }; mesh.MarkDynamic();
            root.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = root.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.sortingOrder = order;
        }
        public void Clear() { vertices.Clear(); uv.Clear(); colors.Clear(); triangles.Clear(); }
        public void Sprite(float x, float y, float width, float height, int sprite, Color color) { Rect r = PixelArt.Uv(sprite); Add(x - width * .5f, y, width, height, r, color); }
        public void Add(float x, float y, float width, float height, Rect region, Color color, bool flipX = false)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(x, y)); vertices.Add(new Vector3(x + width, y)); vertices.Add(new Vector3(x + width, y + height)); vertices.Add(new Vector3(x, y + height));
            float left = flipX ? region.xMax : region.xMin, right = flipX ? region.xMin : region.xMax;
            uv.Add(new Vector2(left, region.yMin)); uv.Add(new Vector2(right, region.yMin)); uv.Add(new Vector2(right, region.yMax)); uv.Add(new Vector2(left, region.yMax));
            for (int i = 0; i < 4; i++) colors.Add(color);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2); triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }
        public void OrientedSprite(Vector2 center, float width, float height, float radians, int sprite, Color color)
        {
            Rect region = PixelArt.Uv(sprite); int start = vertices.Count;
            float cosine = Mathf.Cos(radians), sine = Mathf.Sin(radians);
            Vector2 right = new Vector2(cosine, sine) * (width * .5f), up = new Vector2(-sine, cosine) * (height * .5f);
            vertices.Add(center - right - up); vertices.Add(center + right - up); vertices.Add(center + right + up); vertices.Add(center - right + up);
            uv.Add(new Vector2(region.xMin, region.yMin)); uv.Add(new Vector2(region.xMax, region.yMin)); uv.Add(new Vector2(region.xMax, region.yMax)); uv.Add(new Vector2(region.xMin, region.yMax));
            for (int i = 0; i < 4; i++) colors.Add(color);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2); triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }
        public void Submit() { mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); }
        public void Dispose() { UnityEngine.Object.Destroy(mesh); UnityEngine.Object.Destroy(root); }
    }
}
