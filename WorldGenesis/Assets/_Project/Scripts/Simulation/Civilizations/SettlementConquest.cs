using System;
using System.Collections.Generic;

namespace WorldGenesis.Simulation
{
    public sealed partial class SimulationEngine
    {
        private readonly HashSet<int> conquestTargets = new HashSet<int>();
        private void MigrateConquest() { World.ConquestVersion = 1; }

        public bool TryStartWarParty(int homeId, int targetId)
        {
            SettlementData home = World.GetSettlement(homeId), target = World.GetSettlement(targetId);
            if (home == null || target == null || !AtWar(homeId, targetId) || !World.HasTechnology(homeId, "d03_iron_arms") || home.Population < 12 || home.Food < home.Population || World.Tick < home.NextWarPartyTick) return false;
            foreach (UnitData u in World.Units) if (u.SettlementId == homeId && u.WarTargetSettlementId != 0) return false;
            BuildingData armory = null; ItemType weapon = ItemType.None;
            foreach (BuildingData b in World.Buildings)
            {
                if (!UsableBuilding(b, homeId)) continue;
                foreach (ItemType item in new[] { ItemType.Spear, ItemType.Sword })
                    if (AvailableForHaul(b, item) >= 3) { armory = b; weapon = item; break; }
                if (armory != null) break;
            }
            if (armory == null || !navigation.Find(World.Index(home.X, home.Y), World.Index(target.X, target.Y), scratchPath)) return false;
            var chosen = new List<UnitData>(6);
            foreach (UnitData u in World.Units)
            {
                if (u.SettlementId != homeId || u.Id == home.RulerId || u.Health < u.MaxHealth * .8f || u.Age < 16 || u.Age > 70 || u.ShipId != 0 || u.MaritimeOrderId != 0 || u.CaravanId != 0 || u.ExpeditionId != 0 || u.WarWeapon != ItemType.None || u.Hunger > .4f || u.Thirst > .4f || u.Fatigue > .45f || CarriedLoad(u) > .001f || u.InsideBuildingId != 0) continue;
                if (!navigation.Find(World.Index((int)u.X, (int)u.Y), World.Index(home.X, home.Y), scratchPath)) continue;
                chosen.Add(u);
                if (chosen.Count >= Math.Min(6, Math.Min(home.Population - 8, (int)AvailableForHaul(armory, weapon)))) break;
            }
            if (chosen.Count < 3) return false;
            foreach (UnitData u in chosen)
            {
                u.WarTargetSettlementId = targetId; u.WarArmoryId = armory.Id; u.WarOrderSinceTick = World.Tick; u.WarReturning = false;
                ChangeTask(u, UnitTask.WarMarch); ClearRoute(u); u.DecisionTimer = 0;
            }
            home.NextWarPartyTick = World.Tick + 1800;
            AddEvent(EventKind.War, home.X, home.Y, home.Name + ": отряд из " + chosen.Count + " жителей собирает оружие для похода на " + target.Name);
            return true;
        }

        private bool ChooseWarActivity(UnitData u, SettlementData home)
        {
            if (u.WarTargetSettlementId == 0 && u.WarWeapon == ItemType.None) return false;
            if (home == null || u.Health <= 0) { DropWarEquipment(u); return false; }
            if (!AtWar(home.Id, u.WarTargetSettlementId) || World.Tick - u.WarOrderSinceTick > 1800 || u.Health < u.MaxHealth * .5f || u.Hunger > .65f || u.Thirst > .65f) u.WarReturning = true;
            ChangeTask(u, UnitTask.WarMarch);
            BuildingData armory = World.GetBuilding(u.WarArmoryId);
            if (u.WarReturning)
            {
                if (!UsableBuilding(armory, home.Id)) armory = FindWarHall(home.Id);
                if (armory == null || !SetTarget(u, armory.X, armory.Y)) { DropWarEquipment(u); ChangeTask(u, UnitTask.Idle); return false; }
                u.WarArmoryId = armory.Id; return true;
            }
            if (u.WarWeapon == ItemType.None)
            {
                if (!UsableBuilding(armory, home.Id) || !SetTarget(u, armory.X, armory.Y)) { EndWarOrder(u); return false; }
                return true;
            }
            SettlementData enemy = World.GetSettlement(u.WarTargetSettlementId);
            if (enemy == null || !SetTarget(u, enemy.X, enemy.Y)) { u.WarReturning = true; ClearRoute(u); }
            return true;
        }

        private bool PerformWarActivity(UnitData u, SettlementData home)
        {
            if (u.Task != UnitTask.WarMarch) return false;
            BuildingData b = World.GetBuilding(u.WarArmoryId);
            if (u.WarReturning)
            {
                if (b != null && home != null && UsableBuilding(b, home.Id) && AtBuilding(u, b))
                {
                    if (u.WarWeapon != ItemType.None)
                    {
                        // The depot may have filled while the party was away. Equipment remains physical.
                        if (StorageCapacity.Accepts(b, u.WarWeapon) && FreeStorage(b) >= 1) b.Inventory[(int)u.WarWeapon] += 1;
                        else World.GroundStocks.Add(new GroundStockData { Item = u.WarWeapon, Amount = 1, X = u.X, Y = u.Y });
                    }
                    u.WarWeapon = ItemType.None; EndWarOrder(u);
                }
                else u.DecisionTimer = 0;
                return true;
            }
            if (u.WarWeapon == ItemType.None)
            {
                if (b == null || home == null || !UsableBuilding(b, home.Id)) { EndWarOrder(u); return true; }
                if (!AtBuilding(u, b)) { u.DecisionTimer = 0; return true; }
                ItemType gear = AvailableForHaul(b, ItemType.Spear) >= 1 ? ItemType.Spear : AvailableForHaul(b, ItemType.Sword) >= 1 ? ItemType.Sword : ItemType.None;
                if (gear == ItemType.None || !World.HasTechnology(home.Id, "d03_iron_arms")) { EndWarOrder(u); return true; }
                b.Inventory[(int)gear]--; u.WarWeapon = gear; ClearRoute(u); u.DecisionTimer = 0;
            }
            return true;
        }

        private BuildingData FindWarHall(int settlementId)
        { foreach (BuildingData b in World.Buildings) if (b.SettlementId == settlementId && b.Type == BuildingType.Hall && b.Health > 0) return b; return null; }

        private void EndWarOrder(UnitData u)
        { u.WarTargetSettlementId = u.WarArmoryId = 0; u.WarOrderSinceTick = 0; u.WarReturning = false; ClearRoute(u); ChangeTask(u, UnitTask.Idle); u.DecisionTimer = 0; }

        private void DropWarEquipment(UnitData u)
        {
            if (u.WarWeapon != ItemType.None) World.GroundStocks.Add(new GroundStockData { Item = u.WarWeapon, Amount = 1, X = u.X, Y = u.Y });
            u.WarWeapon = ItemType.None; EndWarOrder(u);
        }

        private void UpdateConquest(float dt)
        {
            // An inactive realm does not need one full resident scan for every town.
            // The cache is rebuilt from live orders; recruitment below remains active.
            conquestTargets.Clear();
            if (World.Laws.Warfare)
                foreach (UnitData u in World.Units)
                    if (u.Health > 0 && u.WarWeapon != ItemType.None && !u.WarReturning && u.WarTargetSettlementId != 0)
                        conquestTargets.Add(u.WarTargetSettlementId);
            foreach (SettlementData target in World.Settlements)
            {
                int attacker = 0, attackers = 0, defenders = 0; bool competing = false;
                if (conquestTargets.Contains(target.Id) && FindWarHall(target.Id) != null)
                foreach (UnitData u in World.Units)
                {
                    if (!IsCivilized(u.Species) || u.Age < 16 || u.Health <= 0 || u.ShipId != 0 || DistanceSquared(u.X, u.Y, target.X + .5f, target.Y + .5f) > 16) continue;
                    if (World.SovereignOf(u.SettlementId) == World.SovereignOf(target.Id)) { defenders++; continue; }
                    if (u.InsideBuildingId != 0 || u.WarWeapon == ItemType.None || u.WarReturning || u.WarTargetSettlementId != target.Id || !AtWar(u.SettlementId, target.Id)) continue;
                    int sovereign = World.SovereignOf(u.SettlementId);
                    if (attacker != 0 && attacker != sovereign) competing = true;
                    attacker = sovereign; attackers++;
                }
                if (attackers >= 3 && defenders == 0 && !competing)
                {
                    if (target.OccupierSettlementId != attacker) { target.OccupierSettlementId = attacker; target.OccupationSeconds = 0; }
                    target.OccupationSeconds += dt;
                    if (target.OccupationSeconds >= 20) CaptureSettlement(attacker, target);
                }
                else
                {
                    target.OccupationSeconds = Math.Max(0, target.OccupationSeconds - dt * 2);
                    if (target.OccupationSeconds == 0) target.OccupierSettlementId = 0;
                }
                if (World.Tick % 300 == 0 && target.EnemyId != 0) TryStartWarParty(target.Id, target.EnemyId);
            }
        }

        private void CaptureSettlement(int attackerId, SettlementData target)
        {
            SettlementData attacker = World.GetSettlement(attackerId);
            if (attacker == null || !AtWar(attacker.Id, target.Id)) return;
            var absorbed = new HashSet<int> { target.Id };
            if (target.SovereignSettlementId == 0)
                foreach (SettlementData s in World.Settlements) if (s.SovereignSettlementId == target.Id) absorbed.Add(s.Id);
            foreach (SettlementData s in World.Settlements) if (absorbed.Contains(s.Id))
            { s.SovereignSettlementId = attacker.Id; s.ColorIndex = attacker.ColorIndex; s.EnemyId = 0; s.OccupierSettlementId = 0; s.OccupationSeconds = 0; s.ConquestCount++; }
            World.Relations.RemoveAll(r => absorbed.Contains(r.FirstId) || absorbed.Contains(r.SecondId));
            foreach (UnitData u in World.Units)
            {
                if (u.WarTargetSettlementId != 0 && !AtWar(u.SettlementId, u.WarTargetSettlementId)) u.WarReturning = true;
                if (u.Task == UnitTask.Fight && World.GetUnit(u.TargetId) is UnitData enemy && !AtWar(u.SettlementId, enemy.SettlementId) && IsCivilized(enemy.Species))
                { u.TargetId = 0; ClearRoute(u); ChangeTask(u, UnitTask.Idle); u.DecisionTimer = 0; }
            }
            World.TerrainRevision++;
            AddEvent(EventKind.War, target.X, target.Y, attacker.Name + " захватывает " + target.Name + ". Жители и постройки сохранены.");
        }
    }
}
