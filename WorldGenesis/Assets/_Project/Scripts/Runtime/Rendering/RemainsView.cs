using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    public sealed class RemainsView : IDisposable
    {
        private readonly Texture2D atlas;
        private readonly Material material;
        private readonly QuadBatch batch;
        private readonly Dictionary<Vector2, float> halfWidths = new Dictionary<Vector2, float>();
        private int worldWidth;
        public RemainsView(Transform parent)
        {
            atlas = RemainsArt.CreateAtlas();
            material = new Material(Shader.Find("Sprites/Default")) { mainTexture = atlas, name = "Flat fading remains" };
            batch = new QuadBatch("Temporary ground remains", parent, material, 16);
        }
        public void Update(WorldState world)
        {
            batch.Clear(); halfWidths.Clear(); worldWidth = world.Width;
            if (world.Remains != null) foreach (RemainsData remains in world.Remains)
            {
                if (remains.RemainingSeconds <= 0) continue;
                Vector2 size = RemainsArt.Size(remains); Color tint = remains.WaterDeath ? PixelArt.Hex("a2bbc0") : Color.white; tint.a = RemainsArt.Alpha(remains);
                Vector2 point = new Vector2(remains.X, remains.Y); float halfWidth = size.x * .5f;
                if (!halfWidths.TryGetValue(point, out float previous) || previous < halfWidth) halfWidths[point] = halfWidth;
                // A flat ground layer: crops are underneath; living bodies, walls and roofs cover bones.
                batch.Add(remains.X - size.x * .5f, remains.Y - size.y * .5f, size.x, size.y, RemainsArt.Uv(RemainsArt.Glyph(remains)), tint);
            }
            batch.Submit();
        }
        public Vector2 AgriculturalDropPosition(AgriculturalDrop drop)
        {
            Vector2 position = new Vector2(drop.X, drop.Y);
            if (!halfWidths.TryGetValue(position, out float halfWidth)) return position;
            // The actual stock/pickup position stays at the death site. Only its .85-cell
            // icon sits next to the bones, leaving both real loot and skeleton readable.
            float offset = halfWidth + .55f;
            position.x += position.x + offset + .425f <= worldWidth ? offset : -offset;
            return position;
        }
        public void Dispose() { batch.Dispose(); UnityEngine.Object.Destroy(material); UnityEngine.Object.Destroy(atlas); }
    }
}
