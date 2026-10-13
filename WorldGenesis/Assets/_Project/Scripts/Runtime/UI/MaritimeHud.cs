using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    public sealed partial class GameHud
    {
        private RectTransform maritimePanel;
        private Text maritimeHeading, maritimeHint;
        private readonly List<Button> maritimeButtons = new List<Button>();
        private InputField maritimeQuantity;
        private int maritimePage, maritimePerson, maritimePort, maritimeTarget, maritimeWarehouse, maritimeGood, maritimeHull, maritimeAboard, previousMaritimeShip = -1;
        private readonly List<int> marinePeople = new List<int>(), marinePorts = new List<int>(), marineTargets = new List<int>(), marineWarehouses = new List<int>();
        private readonly List<int> marineGoods = new List<int>();
        private int marineSelectedShip, marineSelectedBuilding;
        public string MaritimeText => maritimeHint == null ? "" : maritimeHint.text;
        public bool MaritimePanelVisible => maritimePanel != null && maritimePanel.gameObject.activeSelf;
        private void CreateMaritimeControls()
        {
            maritimePanel = PanelFixed(root.transform, "Maritime commands", new Vector2(-272, -95), new Vector2(276, 450), panel, true);
            maritimeHeading = Label(maritimePanel, "МОРСКОЕ ДЕЛО", 17, PixelArt.Gold, new Vector2(14, -12), new Vector2(248, 28));
            string[] tabs = { "Рейс", "Трюм", "Команда" };
            for (int i = 0; i < 3; i++) { int page = i; ButtonAt(maritimePanel, tabs[i], new Vector2(14 + i * 84, -45), new Vector2(80, 28), () => { maritimePage = page; Refresh(true); }); }
            for (int i = 0; i < 8; i++) { int action = i; maritimeButtons.Add(ButtonAt(maritimePanel, "", new Vector2(14, -82 - i * 34), new Vector2(248, 29), () => MaritimeAction(action))); }
            maritimeQuantity = Input(maritimePanel, new Vector2(173, -354), new Vector2(89, 28), "10");
            maritimeQuantity.contentType = InputField.ContentType.DecimalNumber;
            Label(maritimePanel, "Количество товара:", 12, muted, new Vector2(14, -360), new Vector2(152, 25));
            maritimeHint = Label(maritimePanel, "", 12, muted, new Vector2(14, -390), new Vector2(248, 52));
            maritimePanel.gameObject.SetActive(false);
        }
        private static int Choice(List<int> list, int index) { return list.Count == 0 ? 0 : list[(index % list.Count + list.Count) % list.Count]; }
        private BuildingData MaritimeSelectedBuilding(WorldState world)
        {
            if (game.SelectedShipId != 0 || game.SelectedUnitId != 0 || !world.InBounds(game.InspectX, game.InspectY)) return null;
            BuildingData b = world.GetBuilding(world.Cells[world.Index(game.InspectX, game.InspectY)].BuildingId);
            return b != null && MaritimeArt.SupportsBuilding(b.Type) ? b : null;
        }
        private void RefreshMaritimeControls(WorldState world)
        {
            if (maritimePanel == null) return;
            ShipData ship = world.GetShip(game.SelectedShipId); BuildingData selected = MaritimeSelectedBuilding(world);
            maritimePanel.gameObject.SetActive(ship != null || selected != null); if (!maritimePanel.gameObject.activeSelf) return;
            marineSelectedShip = ship == null ? 0 : ship.Id; marineSelectedBuilding = selected == null ? 0 : selected.Id;
            int owner = ship == null ? selected.SettlementId : ship.OwnerSettlementId;
            BuildingData dock = ship == null ? selected : world.GetBuilding(ship.DockedPortId);
            if (owner == 0 && dock != null) owner = dock.SettlementId;
            marinePeople.Clear(); marinePorts.Clear(); marineTargets.Clear(); marineWarehouses.Clear(); marineGoods.Clear();
            foreach (UnitData u in world.Units) if (u.Health > 0 && u.SettlementId == owner && (int)u.Species < 4 && u.ShipId == 0) marinePeople.Add(u.Id);
            foreach (BuildingData b in world.Buildings) if (b.Health > 0 && b.Type == BuildingType.Port) marinePorts.Add(b.Id);
            foreach (ShipData other in world.Ships) if (other.Id != marineSelectedShip && MaritimeArt.IsVisible(other)) marineTargets.Add(other.Id);
            int networkPort = dock != null && dock.Type == BuildingType.Port ? dock.Id : 0;
            if (selected != null && selected.Type == BuildingType.HarborWarehouse)
                foreach (int portId in marinePorts) if (game.Engine.GetHarborWarehouses(portId).Contains(selected)) { networkPort = portId; break; }
            if (networkPort != 0) foreach (BuildingData warehouse in game.Engine.GetHarborWarehouses(networkPort)) marineWarehouses.Add(warehouse.Id);
            BuildingData store = world.GetBuilding(Choice(marineWarehouses, maritimeWarehouse));
            marineGoods.Add(-1); marineGoods.Add(-2); // finite provisions share the hold
            for (int i = 1; i < ResourceCatalog.ItemCount; i++)
            { bool present = ship == null || ship.Cargo[i] > .001f; foreach (int id in marineWarehouses) if (world.GetBuilding(id).Inventory[i] > .001f) present = true; if (present) marineGoods.Add(i); }
            for (int i = 1; i < FishingCatalog.AgriculturalProductCount; i++)
            { bool present = ship == null || ship.AgriculturalCargo[i] > .001f; foreach (int id in marineWarehouses) if (world.GetBuilding(id).AgriculturalStock[i] > .001f) present = true; if (present) marineGoods.Add(100 + i); }
            UnitData person = world.GetUnit(Choice(marinePeople, maritimePerson));
            string personName = person == null ? "Нет доступных жителей" : person.Name + (person.IsChild ? " · ребёнок" : "");
            string storeName = store == null ? "Нет связанного склада" : "Склад #" + store.Id + " · " + StorageCapacity.TotalUsed(store).ToString("0") + "/" + StorageCapacity.Capacity(store).ToString("0");
            string goodName = MarineGoodName(Choice(marineGoods, maritimeGood));
            foreach (Button button in maritimeButtons) { button.gameObject.SetActive(false); button.interactable = true; }
            maritimeHeading.text = ship == null ? "ПОРТОВЫЕ ПРИКАЗЫ" : maritimePage == 0 ? "РЕЙС И МОРСКОЙ БОЙ" : maritimePage == 1 ? "ГРУЗ И ПРОВИЗИЯ" : "ЭКИПАЖ И ПАССАЖИРЫ";
            if (ship == null)
            {
                MarineButton(0, "Житель: " + personName, person != null);
                MarineButton(1, "Корпус: " + MaritimeCatalog.Get((ShipKind)(maritimeHull % 6)).Name);
                MarineButton(2, selected.Type == BuildingType.Shipyard ? "Начать строительство" : "Открыть верфь", selected.Type == BuildingType.Shipyard ? person != null && !person.IsChild : FindShipyard(world, owner) != null);
                MarineButton(3, storeName, store != null); MarineButton(4, goodName + " →", true);
                MarineButton(5, "Доставить товар на склад", person != null && store != null);
                ShipData docked = FirstDocked(world, networkPort);
                MarineButton(6, docked == null ? "У причала нет кораблей" : "Корабль: " + MaritimeCatalog.Get(docked.Kind).Name, docked != null);
                MarineButton(7, "Заявка на строительство: отменить", selected.MaritimeOrderId > 0);
                maritimeHint.text = networkPort == 0 ? "Склад соединяется с портом через доступные соседние секции." : "Сеть: " + marineWarehouses.Count + " секц. · " + game.Engine.HarborUsed(networkPort).ToString("0") + "/" + game.Engine.HarborCapacity(networkPort).ToString("0") + "\nТовары доставляет выбранный житель.";
            }
            else if (maritimePage == 0)
            {
                BuildingData targetPort = world.GetBuilding(Choice(marinePorts, maritimePort));
                ShipData target = world.GetShip(Choice(marineTargets, maritimeTarget));
                MarineButton(0, targetPort == null ? "Нет портов назначения" : "Порт #" + targetPort.Id + " · " + (world.GetSettlement(targetPort.SettlementId)?.Name ?? ""), targetPort != null);
                MarineButton(1, "Перейти в выбранный порт", targetPort != null && ship.OwnerSettlementId != 0);
                MarineButton(2, "Начать ловлю рядом", MaritimeCatalog.Get(ship.Kind).FishingGrade > 0);
                MarineButton(3, "Торговый рейс за рубеж", ship.Kind == ShipKind.Merchant);
                MarineButton(4, target == null ? "Нет морских целей" : "Цель: " + MaritimeCatalog.Get(target.Kind).Name + " #" + target.Id, target != null);
                MarineButton(5, "Атаковать выбранный корабль", target != null && ship.Kind == ShipKind.Warship);
                MarineButton(6, "Попытаться захватить", target != null && ship.Kind == ShipKind.Warship);
                MarineButton(7, "Следующий корабль у причала", FirstDocked(world, ship.DockedPortId) != null);
                maritimeHint.text = game.Engine.ShipDepartureReason(ship.Id) + "\n" + (ship.LastReason ?? "");
            }
            else if (maritimePage == 1)
            {
                bool foreign = ship.OwnerSettlementId == 0, docked = ship.Phase == ShipPhase.Docked;
                MarineButton(0, storeName, store != null); MarineButton(1, goodName + " →");
                bool transferable = docked && store != null && (!foreign || Choice(marineGoods, maritimeGood) > 0 && person != null && !person.IsChild);
                MarineButton(2, foreign ? "Купить у странника" : "Склад → трюм", transferable);
                MarineButton(3, foreign ? "Продать страннику" : "Трюм → склад", transferable);
                MarineButton(4, "Житель: " + personName, person != null);
                MarineButton(5, "Доставить товар на склад", person != null && store != null);
                MarineButton(6, "Принести еду на корабль", !foreign && person != null && !person.IsChild && (docked || ship.Phase == ShipPhase.Stranded));
                MarineButton(7, "Принести воду на корабль", !foreign && person != null && !person.IsChild && (docked || ship.Phase == ShipPhase.Stranded));
                int selectedGood = Choice(marineGoods, maritimeGood);
                float networkStock = selectedGood == -1 || selectedGood == -2 ? game.Engine.HarborProvisionStock(networkPort, selectedGood == -1 ? MaritimeProvision.Food : MaritimeProvision.Water) : selectedGood >= 100 ? game.Engine.HarborAgricultureStock(networkPort, (AgriculturalProduct)(selectedGood - 100)) : game.Engine.HarborStock(networkPort, (ItemType)selectedGood);
                maritimeHint.text = "Трюм " + MaritimeCatalog.UsedHold(ship).ToString("0.#") + "/" + MaritimeCatalog.Get(ship.Kind).Capacity.ToString("0") + " · в сети " + networkStock.ToString("0.#") + "\n" + (foreign ? "Сделка — в выбранной секции склада." : "Погрузка из всех связанных секций.");
            }
            else
            {
                bool ownDock = (ship.Phase == ShipPhase.Docked || ship.Phase == ShipPhase.Stranded) && ship.OwnerSettlementId != 0;
                MarineButton(0, "Житель: " + personName, person != null);
                MarineButton(1, "Назначить капитаном", ownDock && person != null && !person.IsChild);
                MarineButton(2, "Нанять моряком", ownDock && person != null && !person.IsChild);
                MarineButton(3, "Назначить морским стражем", ownDock && person != null && !person.IsChild);
                MarineButton(4, "Посадить пассажиром", ownDock && person != null);
                int aboard = NextShipPerson(world, ship, maritimeAboard); UnitData onboard = world.GetUnit(aboard);
                MarineButton(5, onboard == null ? "На борту никого" : "На борту: " + onboard.Name + " →", onboard != null);
                MarineButton(6, "Осмотреть жителя на борту", onboard != null);
                MarineButton(7, "Высадить выбранного жителя", onboard != null && ship.Phase == ShipPhase.Docked && !ship.PrisonerIds.Contains(aboard));
                maritimeHint.text = "Экипаж " + ship.CrewIds.Count + "/" + MaritimeCatalog.Get(ship.Kind).MaxCrew + "; пассажиры " + ship.PassengerIds.Count + "/" + MaritimeCatalog.Get(ship.Kind).PassengerCapacity + "\nЖители подходят к причалу и садятся на борт.";
            }
        }
        private void MarineButton(int index, string text, bool enabled = true) { Button b = maritimeButtons[index]; b.gameObject.SetActive(true); b.interactable = enabled; b.GetComponentInChildren<Text>().text = text; }
        private static BuildingData FindShipyard(WorldState w, int owner) { foreach (BuildingData b in w.Buildings) if (b.Type == BuildingType.Shipyard && b.SettlementId == owner && b.Health > 0) return b; return null; }
        private static ShipData FirstDocked(WorldState w, int port) { if (port == 0) return null; foreach (ShipData s in w.Ships) if (s.DockedPortId == port && MaritimeArt.IsVisible(s)) return s; return null; }
        public static int NextShipPerson(WorldState world, ShipData ship, int index)
        {
            if (ship == null) return 0; List<int> ids = new List<int>();
            foreach (List<int> list in new[] { ship.CrewIds, ship.PassengerIds, ship.PrisonerIds }) foreach (int id in list) { UnitData u = world.GetUnit(id); if (u != null && u.Health > 0 && u.ShipId == ship.Id) ids.Add(id); }
            return Choice(ids, index);
        }
        private static string MarineGoodName(int good) { return good == -1 ? "Еда (провизия)" : good == -2 ? "Питьевая вода" : good >= 100 ? AgricultureArt.ProductName((AgriculturalProduct)(good - 100)) : ResourceArt.ItemName((ItemType)good); }
        private void MaritimeAction(int action)
        {
            WorldState world = game.World; ShipData ship = world.GetShip(marineSelectedShip); BuildingData selected = world.GetBuilding(marineSelectedBuilding);
            int person = Choice(marinePeople, maritimePerson), warehouse = Choice(marineWarehouses, maritimeWarehouse), good = Choice(marineGoods, maritimeGood);
            bool ok = false, command = false; float amount = 0;
            float.TryParse(maritimeQuantity.text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out amount);
            if (amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount)) amount = 0;
            if (ship == null && selected != null)
            {
                if (action == 0) maritimePerson++; else if (action == 1) maritimeHull++; else if (action == 3) maritimeWarehouse++; else if (action == 4) maritimeGood++;
                else if (action == 2) { if (selected.Type == BuildingType.Shipyard) { command = true; ok = game.Engine.TryStartShipConstruction(person, selected.Id, (ShipKind)(maritimeHull % 6)); } else { BuildingData yard = FindShipyard(world, selected.SettlementId); if (yard != null) game.InspectSite(yard.X, yard.Y); } }
                else if (action == 5) { command = true; ok = SupplyHarbor(person, warehouse, good, amount); }
                else if (action == 6) { int port = selected.Type == BuildingType.Port ? selected.Id : 0; if (port == 0) foreach (int id in marinePorts) if (game.Engine.GetHarborWarehouses(id).Exists(b => b.Id == selected.Id)) { port = id; break; } ShipData next = FirstDocked(world, port); if (next != null) game.InspectShip(next.Id); }
                else if (action == 7) { command = true; ok = game.Engine.TryCancelShipConstruction(selected.MaritimeOrderId); }
            }
            else if (ship != null && maritimePage == 0)
            {
                if (action == 0) maritimePort++; else if (action == 4) maritimeTarget++;
                else if (action == 1) { command = true; ok = game.Engine.TrySailToPort(ship.Id, Choice(marinePorts, maritimePort)); }
                else if (action == 2) { command = true; ok = game.Engine.TryStartShipFishing(ship.Id, NearestFishingCell(world, ship)); }
                else if (action == 3) { command = true; ok = game.Engine.TryStartForeignVoyage(ship.Id); }
                else if (action == 5 || action == 6) { command = true; ok = game.Engine.TryAttackShip(ship.Id, Choice(marineTargets, maritimeTarget), action == 6); }
                else if (action == 7) { ShipData next = null; foreach (ShipData s in world.Ships) if (s.DockedPortId == ship.DockedPortId && MaritimeArt.IsVisible(s) && (next == null || s.Id > ship.Id && next.Id <= ship.Id)) next = s; if (next != null) game.InspectShip(next.Id); }
            }
            else if (ship != null && maritimePage == 1)
            {
                if (action == 0) maritimeWarehouse++; else if (action == 1) maritimeGood++; else if (action == 4) maritimePerson++;
                else if (action == 2 || action == 3) { command = true; ok = TransferMarineGood(ship, person, warehouse, good, amount, action == 2); }
                else if (action == 5) { command = true; ok = SupplyHarbor(person, warehouse, good, amount); }
                else if (action == 6 || action == 7) { command = true; ok = game.Engine.TrySupplyShip(person, ship.Id, action == 6 ? MaritimeProvision.Food : MaritimeProvision.Water, amount); }
            }
            else if (ship != null)
            {
                if (action == 0) maritimePerson++;
                else if (action >= 1 && action <= 3) { command = true; ok = game.Engine.TryAssignCrew(person, ship.Id, action == 1, action == 3); }
                else if (action == 4) { command = true; ok = game.Engine.TryBoardPassenger(person, ship.Id); }
                else if (action == 5) maritimeAboard++;
                else if (action == 6) { int id = NextShipPerson(world, ship, maritimeAboard); if (id != 0) game.InspectUnit(id); }
                else if (action == 7) { command = true; ok = game.Engine.TryDisembark(NextShipPerson(world, ship, maritimeAboard)); }
            }
            if (command) ShowToast(ok ? "Морской приказ принят" : ship != null && !string.IsNullOrEmpty(ship.LastReason) ? ship.LastReason : "Приказ отклонён: проверьте жителей, технологию, причал, товар и место.");
            Refresh(true);
        }
        private bool SupplyHarbor(int person, int store, int good, float quantity) { return game.Engine.TrySupplyHarbor(person, store, good > 0 && good < 100 ? (ItemType)good : ItemType.None, good >= 100 ? (AgriculturalProduct)(good - 100) : AgriculturalProduct.None, good == -1 ? MaritimeProvision.Food : good == -2 ? MaritimeProvision.Water : MaritimeProvision.None, quantity); }
        private bool TransferMarineGood(ShipData ship, int person, int store, int good, float quantity, bool toShip)
        {
            if (ship.OwnerSettlementId == 0) return good > 0 && good < 100 ? game.Engine.TryStartShipTrade(person, ship.Id, store, (ItemType)good, quantity, toShip) : good >= 100 && game.Engine.TryStartShipAgricultureTrade(person, ship.Id, store, (AgriculturalProduct)(good - 100), quantity, toShip);
            return good == -1 || good == -2 ? game.Engine.TryTransferHarborProvisions(ship.Id, good == -1 ? quantity : 0, good == -2 ? quantity : 0, toShip) : good >= 100 ? game.Engine.TryTransferHarborAgriculture(ship.Id, (AgriculturalProduct)(good - 100), quantity, toShip) : game.Engine.TryTransferHarborCargo(ship.Id, (ItemType)good, quantity, toShip);
        }
        private static int NearestFishingCell(WorldState w, ShipData ship)
        {
            float nearest = float.MaxValue; int best = -1;
            for (int i = 0; i < w.Cells.Length; i++) { if (!MaritimeCatalog.IsWater(w, i, ship.Kind) || w.Infrastructure[i].Fish < 1) continue; float dx = i % w.Width + .5f - ship.X, dy = i / w.Width + .5f - ship.Y, distance = dx * dx + dy * dy; if (distance < nearest) { best = i; nearest = distance; } }
            return best;
        }
        private bool TryDescribeSelectedShip(WorldState world)
        {
            ShipData ship = world.GetShip(game.SelectedShipId); if (ship == null) return false;
            if (previousMaritimeShip != ship.Id) { previousMaritimeShip = ship.Id; inspectorScroll.verticalNormalizedPosition = 1; }
            inspectorTitle.text = MaritimeCatalog.Get(ship.Kind).Name + " #" + ship.Id;
            inspectorBody.text = ShipDescription(game.Engine, ship); return true;
        }
        public static string ShipDescription(SimulationEngine engine, ShipData ship)
        {
            if (ship == null) return ""; WorldState world = engine.World; ShipDefinition def = MaritimeCatalog.Get(ship.Kind); UnitData captain = world.GetUnit(ship.CaptainUnitId);
            StringBuilder t = new StringBuilder(); t.Append(MaritimeCatalog.PhaseName(ship.Phase)).Append("\n").Append(world.GetSettlement(ship.OwnerSettlementId)?.Name ?? "Заморские торговцы");
            t.Append("\nКорпус ").Append(ship.Health.ToString("0")).Append('/').Append(ship.MaxHealth.ToString("0"));
            t.Append("\n\n<b>ТРЮМ ").Append(MaritimeCatalog.UsedHold(ship).ToString("0.#")).Append('/').Append(def.Capacity.ToString("0")).Append("</b>\nСвободно ").Append(MaritimeCatalog.FreeHold(ship).ToString("0.#"));
            t.Append("\nЕда ").Append(ship.Food.ToString("0.##")).Append(" · вода ").Append(ship.Water.ToString("0.##"));
            t.Append("\nАвтономность ").Append((engine.ShipEnduranceSeconds(ship.Id) / SubsistenceRules.DaySeconds).ToString("0.##")).Append(" дней");
            for (int i = 1; i < ship.Cargo.Length; i++) if (ship.Cargo[i] > .001f) t.Append('\n').Append(ResourceArt.ItemName((ItemType)i)).Append(' ').Append(ship.Cargo[i].ToString("0.##"));
            for (int i = 1; i < ship.AgriculturalCargo.Length; i++) if (ship.AgriculturalCargo[i] > .001f) t.Append('\n').Append(AgricultureArt.ProductName((AgriculturalProduct)i)).Append(' ').Append(ship.AgriculturalCargo[i].ToString("0.##"));
            t.Append("\n\n<b>ЛЮДИ НА БОРТУ</b>\nКапитан: ").Append(captain != null && captain.ShipId == ship.Id ? captain.Name : "не назначен");
            t.Append("\nЭкипаж ").Append(ship.CrewIds.Count).Append('/').Append(def.MaxCrew).Append(" · минимум ").Append(def.MinCrew);
            t.Append("\nПассажиры ").Append(ship.PassengerIds.Count).Append('/').Append(def.PassengerCapacity).Append(" · пленные ").Append(ship.PrisonerIds.Count);
            foreach (int id in ship.CrewIds) { UnitData unit = world.GetUnit(id); if (unit != null && unit.ShipId == ship.Id) t.Append('\n').Append(unit.Name).Append(" · ").Append(ProfessionName(unit)); }
            t.Append("\n\n").Append(engine.ShipDepartureReason(ship.Id)); if (!string.IsNullOrEmpty(ship.LastReason)) t.Append('\n').Append(ship.LastReason);
            if (ship.DockedPortId != 0) t.Append("\nПричал #").Append(ship.DockedPortId);
            return t.ToString();
        }
        private string MaritimeBuildingDescription(WorldState world, BuildingData building)
        {
            if (!MaritimeArt.SupportsBuilding(building.Type)) return "";
            StringBuilder t = new StringBuilder("<b>МОРСКОЕ ДЕЛО</b>\n");
            if (building.Type == BuildingType.Port)
            {
                List<BuildingData> stores = game.Engine.GetHarborWarehouses(building.Id);
                t.Append("Связанные секции: ").Append(stores.Count).Append("\nЗапас ").Append(game.Engine.HarborUsed(building.Id).ToString("0.#")).Append('/').Append(game.Engine.HarborCapacity(building.Id).ToString("0"));
                t.Append("\nВыход в океан: ").Append(game.Engine.HasOceanRoute(building.Id) ? "есть" : "нет");
                foreach (ShipData ship in world.Ships) if (ship.DockedPortId == building.Id) t.Append("\nУ причала: ").Append(MaritimeCatalog.Get(ship.Kind).Name).Append(" #").Append(ship.Id);
            }
            else if (building.Type == BuildingType.HarborWarehouse)
            { t.Append("Общий запас ").Append(StorageCapacity.TotalUsed(building).ToString("0.#")).Append('/').Append(StorageCapacity.Capacity(building).ToString("0")).Append("\nЕда ").Append(building.PortFoodStored.ToString("0.#")).Append(" · вода ").Append(building.WaterStored.ToString("0.#")); int ports = 0; foreach (BuildingData p in world.Buildings) if (p.Type == BuildingType.Port && game.Engine.GetHarborWarehouses(p.Id).Contains(building)) { ports++; t.Append("\nПодключён к порту #").Append(p.Id); } if (ports == 0) t.Append("\nНе подключён к доступному порту"); }
            else if (building.Type == BuildingType.Shipyard)
            {
                ShipConstructionData order = world.GetShipOrder(building.MaritimeOrderId);
                if (order == null) t.Append("Нет заказа на корпус");
                else { t.Append(MaritimeCatalog.Get(order.Kind).Name).Append("\nГотовность ").Append((100 * order.Progress / MaritimeCatalog.Get(order.Kind).Seconds).ToString("0")).Append('%'); if (!string.IsNullOrEmpty(order.LastReason)) t.Append('\n').Append(order.LastReason); }
            }
            else t.Append("Управление портовой торговлей");
            return t.ToString();
        }
        private static string MaritimeProfessionName(UnitProfession profession)
        {
            switch (profession) { case UnitProfession.Shipwright: return "корабельный плотник"; case UnitProfession.Dockworker: return "портовый грузчик"; case UnitProfession.HarborMaster: return "начальник порта"; case UnitProfession.Sailor: return "моряк"; case UnitProfession.NavalGuard: return "морской страж"; case UnitProfession.Captain: return "капитан"; default: return ""; }
        }
    }
}
