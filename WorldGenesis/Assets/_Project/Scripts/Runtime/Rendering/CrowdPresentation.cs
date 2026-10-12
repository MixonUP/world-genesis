using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    // Small, bounded presentation offsets only. Never changes a unit, path or simulation RNG.
    internal sealed class CrowdPresentation
    {
        private int[] occupants;
        private readonly Dictionary<int, Vector2> offsets = new Dictionary<int, Vector2>(1024);
        private long previousTick = -1;
        private int previousRevision = -1;
        public void Reset() { offsets.Clear(); previousTick = -1; previousRevision = -1; }
        public Vector2 Offset(int unitId) { return offsets.TryGetValue(unitId, out Vector2 value) ? value : Vector2.zero; }
        public void Prepare(WorldState world)
        {
            if (previousTick == world.Tick && previousRevision == world.Revision) return;
            if (occupants == null || occupants.Length != world.Cells.Length) occupants = new int[world.Cells.Length];
            else Array.Clear(occupants, 0, occupants.Length);
            if (offsets.Count > world.Units.Count * 2 + 64) offsets.Clear();
            for (int i = 0; i < world.Units.Count; i++)
            {
                UnitData unit = world.Units[i];
                if ((int)unit.Species >= 4 || unit.Health <= 0 || !world.InBounds((int)unit.X, (int)unit.Y)) continue;
                occupants[world.Index((int)unit.X, (int)unit.Y)]++;
            }
            float step = previousTick < 0 ? 1 : Mathf.Min(1, (world.Tick - previousTick) * SimulationEngine.TickSeconds * 2.5f);
            for (int i = 0; i < world.Units.Count; i++)
            {
                UnitData unit = world.Units[i];
                if ((int)unit.Species >= 4 || unit.Health <= 0 || !world.InBounds((int)unit.X, (int)unit.Y)) continue;
                int x = (int)unit.X, y = (int)unit.Y, count = occupants[world.Index(x, y)];
                Vector2 target = Vector2.zero;
                if (count > 1)
                {
                    float angle = unit.Id * 2.39996323f;
                    float radius = Mathf.Min(.28f, .13f + count * .025f);
                    target = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * .72f);
                    target.x = Mathf.Clamp(unit.X + target.x, x + .08f, x + .92f) - unit.X;
                    target.y = Mathf.Clamp(unit.Y + target.y, y + .08f, y + .92f) - unit.Y;
                    target = Vector2.ClampMagnitude(target, .28f);
                }
                Vector2 old = Offset(unit.Id);
                Vector2 next = Vector2.MoveTowards(old, target, step * .28f);
                // Changing cells cannot carry a visual offset through water or a closed wall.
                if (!world.InBounds((int)(unit.X + next.x), (int)(unit.Y + next.y)) ||
                    (int)(unit.X + next.x) != x || (int)(unit.Y + next.y) != y) next = Vector2.zero;
                offsets[unit.Id] = next;
            }
            previousTick = world.Tick; previousRevision = world.Revision;
        }
    }
}
