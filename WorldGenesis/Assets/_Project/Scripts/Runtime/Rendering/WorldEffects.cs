using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    // Event presentation only: this class never writes health, terrain or world RNG.
    public sealed class WorldEffects : IDisposable
    {
        private struct Particle { public Vector2 Position, Velocity; public float Life, Duration, Size, Gravity; public Color Color; }
        private struct Bolt { public Vector2 Position; public float Life; public int Seed; }
        private readonly List<Particle> particles = new List<Particle>(1024);
        private readonly List<Bolt> bolts = new List<Bolt>(16);
        private readonly QuadBatch batch;
        private long lastEventId;
        private float fireClock;
        public int ActiveParticles => particles.Count;
        public WorldEffects(Transform parent, Material material) { batch = new QuadBatch("World effects • event owned", parent, material, 70); }
        public void Reset(WorldState world)
        {
            particles.Clear(); bolts.Clear(); lastEventId = 0;
            foreach (WorldEvent item in world.Events) lastEventId = Math.Max(lastEventId, item.Id);
            batch.Clear(); batch.Submit();
        }
        public void Update(WorldState world, float deltaTime, bool paused)
        {
            foreach (WorldEvent item in world.Events)
            {
                if (item.Id <= lastEventId) continue;
                lastEventId = item.Id; Emit(item);
            }
            float dt = Mathf.Min(deltaTime, .05f);
            batch.Clear();
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                Particle p = particles[i]; p.Life -= dt;
                if (p.Life <= 0) { particles.RemoveAt(i); continue; }
                p.Velocity.y -= p.Gravity * dt; p.Position += p.Velocity * dt;
                Color color = p.Color; color.a *= Mathf.Clamp01(p.Life / Mathf.Min(p.Duration * .5f, .5f));
                batch.Sprite(p.Position.x, p.Position.y, p.Size, p.Size, 12, color); particles[i] = p;
            }
            for (int i = bolts.Count - 1; i >= 0; i--)
            {
                Bolt bolt = bolts[i]; bolt.Life -= dt;
                if (bolt.Life <= 0) { bolts.RemoveAt(i); continue; }
                System.Random random = new System.Random(bolt.Seed); float lastX = bolt.Position.x;
                for (int segment = 0; segment < 30; segment++)
                {
                    float x = bolt.Position.x + (float)(random.NextDouble() - .5) * (segment / 30f) * 2;
                    float y = bolt.Position.y + segment * .27f;
                    for (int j = 0; j < 4; j++) batch.Sprite(Mathf.Lerp(lastX, x, j / 4f), y + j * .07f, .12f, .18f, 12, new Color(.85f, .96f, 1, bolt.Life / .28f));
                    lastX = x;
                }
                bolts[i] = bolt;
            }
            fireClock += dt;
            int frame = (int)(fireClock * 7);
            for (int y = 0; y < world.Height; y++) for (int x = 0; x < world.Width; x++)
            {
                TileData tile = world.Cells[world.Index(x, y)]; if (tile.Fire <= 0) continue;
                uint hash = PixelArt.Hash(x, y, frame); float height = .3f + (hash % 10) * .075f;
                batch.Sprite(x + .5f, y + .1f, .5f, height, 12, PixelArt.Hex("ef863f"));
                batch.Sprite(x + .5f, y + .1f, .2f, height * .62f, 12, PixelArt.Hex("ffe19a"));
            }
            batch.Submit();
        }
        private void Emit(WorldEvent item)
        {
            Vector2 center = new Vector2(item.X, item.Y);
            System.Random random = new System.Random(unchecked((int)item.Id * 331 + 19));
            if (item.Kind == EventKind.Lightning) bolts.Add(new Bolt { Position = center, Life = .28f, Seed = (int)item.Id });
            int count = item.Kind == EventKind.Meteor ? 110 : item.Kind == EventKind.Rain ? 80 : item.Kind == EventKind.Lightning ? 42 : item.Kind == EventKind.Heal || item.Kind == EventKind.Bless ? 30 : item.Kind == EventKind.Founded || item.Kind == EventKind.Built ? 18 : item.Kind == EventKind.Fire ? 20 : 0;
            Color color = item.Kind == EventKind.Rain ? PixelArt.Hex("b1d9e4") : item.Kind == EventKind.Heal ? PixelArt.Teal : item.Kind == EventKind.Bless || item.Kind == EventKind.Founded ? PixelArt.Gold : PixelArt.Hex("f9b261");
            for (int i = 0; i < count && particles.Count < 1200; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2, speed = 1 + (float)random.NextDouble() * 5;
                Vector2 position = center;
                Vector2 velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
                float gravity = 4, duration = .4f + (float)random.NextDouble() * .65f, size = .12f + (float)random.NextDouble() * .17f;
                if (item.Kind == EventKind.Rain) { position += new Vector2((float)random.NextDouble() * 8 - 4, (float)random.NextDouble() * 6 + 1); velocity = new Vector2(-1, -9); gravity = 0; size = .1f; }
                if (item.Kind == EventKind.Heal || item.Kind == EventKind.Bless) { velocity *= .3f; velocity.y = 1.1f; gravity = 0; duration = 1.2f; }
                if (item.Kind == EventKind.Meteor) { speed *= 1.7f; velocity *= 1.7f; size *= 1.5f; }
                particles.Add(new Particle { Position = position, Velocity = velocity, Life = duration, Duration = duration, Color = color, Gravity = gravity, Size = size });
            }
        }
        public void Dispose() { batch.Dispose(); particles.Clear(); bolts.Clear(); }
    }
}
