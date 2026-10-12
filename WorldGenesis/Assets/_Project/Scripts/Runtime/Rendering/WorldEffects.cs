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
            // Ongoing terrain fire is a simulation state, not a real-time particle lifetime.
            for (int y = 0; y < world.Height; y++) for (int x = 0; x < world.Width; x++)
            {
                TileData tile = world.Cells[world.Index(x, y)]; if (tile.Fire <= 0) continue;
                uint hash = PixelArt.Hash(x, y, world.Seed);
                int phase = (int)((world.Tick / 2 + hash % 4) % 4);
                float flicker = phase == 0 ? .88f : phase == 1 ? 1.04f : phase == 2 ? .94f : 1.12f;
                float force = Mathf.Clamp01(tile.Fire), height = (.62f + .52f * force) * flicker;
                float center = x + .5f + (((hash >> 9) % 100) / 100f - .5f) * .16f;
                if (force > .25f)
                {
                    batch.Add(center - .40f, y + .02f, .43f, height * .66f, PixelArt.Uv(PixelArt.GroundFlame), new Color(.92f, .75f, .67f), (phase & 1) == 0);
                    batch.Add(center + .12f, y + .04f, .35f, height * .52f, PixelArt.Uv(PixelArt.GroundFlame), Color.white, (hash & 1) != 0);
                }
                batch.Add(center - .29f + (phase - 1.5f) * .025f, y + .02f, .58f, height, PixelArt.Uv(PixelArt.GroundFlame), Color.white, (phase & 2) != 0);
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
