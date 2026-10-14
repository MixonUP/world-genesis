using UnityEngine;
using UnityEngine.UI;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    public sealed partial class GameHud
    {
        private RectTransform expansionPanel;
        private Text expansionSummary;
        private Button expansionTargetButton;
        private int expansionTargetIndex, expansionHomeId, expansionTargetId;
        private void CreateExpansionControls()
        {
            expansionPanel = PanelFixed(root.transform, "Settlement commands", new Vector2(-272, -95), new Vector2(276, 380), panel, true);
            Label(expansionPanel, "ПОСЕЛЕНИЕ И ПОХОДЫ", 17, PixelArt.Gold, new Vector2(14, -12), new Vector2(250, 28));
            expansionTargetButton = ButtonAt(expansionPanel, "Сосед: —", new Vector2(14, -49), new Vector2(248, 32), () => { expansionTargetIndex++; Refresh(true); });
            ButtonAt(expansionPanel, "Война / мир с соседом", new Vector2(14, -89), new Vector2(248, 32), () => {
                if (expansionTargetId == 0) { ShowToast("Нет независимого соседа"); return; }
                bool war = !DeclaredWar(game.World, expansionHomeId, expansionTargetId);
                game.Engine.SetDiplomacy(expansionHomeId, expansionTargetId, war);
                ShowToast(war ? "Война объявлена. Вооружённые отряды готовятся самостоятельно." : "Заключён мир. Отряды возвращаются."); Refresh(true);
            });
            ButtonAt(expansionPanel, "Собрать вооружённый отряд", new Vector2(14, -129), new Vector2(248, 32), () => {
                ShowToast(game.Engine.TryStartWarParty(expansionHomeId, expansionTargetId) ? "Жители идут за настоящим оружием" : "Нужны война, железное вооружение, 3 оружия в одном здании, 12 жителей и запас еды"); Refresh(true);
            });
            ButtonAt(expansionPanel, "Отправить переселенцев", new Vector2(14, -169), new Vector2(248, 32), () => {
                int site = game.Engine.FindSettlementExpansionSite(expansionHomeId);
                ShowToast(site >= 0 && game.Engine.TryStartSettlementExpedition(expansionHomeId, site) ? "Шесть переселенцев собирают припасы" : "Нужны свободные взрослые, доски и провизия, доступное место для нового города"); Refresh(true);
            });
            expansionSummary = Label(expansionPanel, "", 13, muted, new Vector2(14, -217), new Vector2(248, 148));
            expansionPanel.gameObject.SetActive(false);
        }
        private static bool DeclaredWar(WorldState world, int a, int b)
        {
            a = world.SovereignOf(a); b = world.SovereignOf(b);
            foreach (SettlementRelation r in world.Relations) if (r.AtWar && (r.FirstId == a && r.SecondId == b || r.FirstId == b && r.SecondId == a)) return true;
            return false;
        }
        private void RefreshExpansionControls(WorldState world)
        {
            BuildingData hall = game.SelectedUnitId == 0 && game.SelectedShipId == 0 && world.InBounds(game.InspectX, game.InspectY) ? world.GetBuilding(world.Cells[world.Index(game.InspectX, game.InspectY)].BuildingId) : null;
            expansionPanel.gameObject.SetActive(hall != null && hall.Type == BuildingType.Hall);
            if (!expansionPanel.gameObject.activeSelf) return;
            SettlementData home = world.GetSettlement(hall.SettlementId); expansionHomeId = home.Id;
            int count = 0; foreach (SettlementData s in world.Settlements) if (world.SovereignOf(s.Id) != world.SovereignOf(home.Id) && s.SovereignSettlementId == 0) count++;
            expansionTargetId = 0; int slot = count == 0 ? -1 : expansionTargetIndex % count;
            foreach (SettlementData s in world.Settlements) if (world.SovereignOf(s.Id) != world.SovereignOf(home.Id) && s.SovereignSettlementId == 0 && slot-- == 0) { expansionTargetId = s.Id; break; }
            SettlementData target = world.GetSettlement(expansionTargetId);
            expansionTargetButton.GetComponentInChildren<Text>().text = "Сосед: " + (target == null ? "—" : target.Name) + " ›";
            expansionSummary.text = ExpansionDescription(world, home) + (target == null ? "" : "\nОтношения: " + (DeclaredWar(world, home.Id, target.Id) ? "война" : "мир"));
        }
        public static string ExpansionDescription(WorldState world, SettlementData city)
        {
            SettlementData sovereign = world.GetSettlement(city.SovereignSettlementId);
            string text = "Владение: " + (sovereign == null ? "независимое" : sovereign.Name) + "\nРадиус территории: " + city.TerritoryRadius + "\nОснованных колоний: ";
            int children = 0; foreach (SettlementData s in world.Settlements) if (s.ParentSettlementId == city.Id) children++;
            text += children;
            foreach (SettlementExpeditionData e in world.SettlementExpeditions) if (e.SourceSettlementId == city.Id && ExpansionCatalog.Active(e)) text += "\nПереселенцы: " + ExpansionCatalog.PhaseName(e.Phase);
            if (city.OccupierSettlementId != 0) text += "\nЗахват центра: " + (city.OccupationSeconds / 20f * 100).ToString("0") + "%";
            return text;
        }
    }
}
