using System;

namespace WorldGenesis.Simulation
{
    public sealed partial class BuildingData
    {
        // v0 means no historical invoice. Never infer a paid cost from type or level.
        public int ConstructionLedgerVersion;
        public float[] PaidConstructionMaterials = new float[ResourceCatalog.ItemCount];
        public bool ConstructionSalvaged;
        public int ConstructionRecoveryPercent;
    }
    public sealed partial class WorldState
    {
        private bool ValidateBuildingSalvage(out string error)
        {
            error = null;
            foreach (BuildingData b in Buildings)
            {
                if (b.ConstructionLedgerVersion < 0 || b.ConstructionLedgerVersion > 1 || b.ConstructionSalvaged && b.Health > 0 || !b.ConstructionSalvaged && b.ConstructionRecoveryPercent != 0)
                { error = "Invalid construction invoice version/state."; return false; }
                if (b.ConstructionLedgerVersion == 0)
                {
                    if (b.ConstructionRecoveryPercent != 0) { error = "Legacy construction has no recovery roll."; return false; }
                    if (b.PaidConstructionMaterials != null)
                        foreach (float paid in b.PaidConstructionMaterials) if (paid != 0) { error = "Legacy building cannot claim paid materials."; return false; }
                    continue;
                }
                if (b.PaidConstructionMaterials == null || b.PaidConstructionMaterials.Length != ResourceCatalog.ItemCount || b.PaidConstructionMaterials[0] != 0)
                { error = "Invalid construction invoice array."; return false; }
                bool hasPaid = false;
                foreach (float paid in b.PaidConstructionMaterials) { if (!Stock(paid)) { error = "Invalid paid construction amount."; return false; } if (paid > 0) hasPaid = true; }
                if (b.ConstructionSalvaged && (hasPaid ? b.ConstructionRecoveryPercent < 10 || b.ConstructionRecoveryPercent > 50 : b.ConstructionRecoveryPercent != 0))
                { error = "Invalid construction recovery percentage."; return false; }
            }
            return true;
        }
    }
    public sealed partial class SimulationEngine
    {
        // Call only after a successful actual stock debit, once per input and building/upgrade.
        private void RecordConstructionCost(BuildingData building, ItemType item, float amount)
        {
            if (building == null || building.ConstructionSalvaged || (int)item <= 0 || (int)item >= ResourceCatalog.ItemCount || float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0)
                throw new ArgumentException("Construction costs require an existing building and finite actual payment.");
            if (amount == 0) return;
            if (building.ConstructionLedgerVersion == 0)
            { building.PaidConstructionMaterials = new float[ResourceCatalog.ItemCount]; building.ConstructionLedgerVersion = 1; }
            building.PaidConstructionMaterials[(int)item] += amount;
        }
        // Runs on every destruction cause, before the building is removed. Contents have a separate ledger.
        private void SalvageConstructionMaterials(BuildingData building)
        {
            if (building.ConstructionSalvaged) return;
            building.ConstructionSalvaged = true;
            if (building.ConstructionLedgerVersion != 1) return;
            bool hasPaid = false;
            for (int i = 1; i < building.PaidConstructionMaterials.Length; i++) if (building.PaidConstructionMaterials[i] > 0) { hasPaid = true; break; }
            if (!hasPaid) return;
            building.ConstructionRecoveryPercent = RandomRange(10, 51);
            float recovery = building.ConstructionRecoveryPercent / 100f;
            float x = building.X + .5f, y = building.Y + .5f;
            for (int i = 1; i < building.PaidConstructionMaterials.Length; i++)
            {
                float amount = building.PaidConstructionMaterials[i] * recovery;
                if (amount <= 0) continue;
                GroundStockData pile = null;
                for (int p = 0; p < World.GroundStocks.Count; p++)
                {
                    GroundStockData candidate = World.GroundStocks[p];
                    if (!candidate.IsFood && candidate.OwnerId == 0 && candidate.Item == (ItemType)i && candidate.X == x && candidate.Y == y) { pile = candidate; break; }
                }
                if (pile == null) World.GroundStocks.Add(new GroundStockData { Item = (ItemType)i, Amount = amount, X = x, Y = y });
                else pile.Amount += amount;
            }
            World.ResourceRevision++;
            AddEvent(EventKind.Built, x, y, "После разрушения возвращено " + building.ConstructionRecoveryPercent + "% оплаченных строительных материалов.");
        }
    }
}
