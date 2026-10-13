using System.Text;
using UnityEngine;
using UnityEngine.UI;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    public sealed partial class GameHud
    {
        private Button occupantsButton, returnToBuildingButton;
        private Text inspectorHint;
        private int occupantsBuildingId;

        private void CreateBuildingAccessControls(RectTransform inspector)
        {
            occupantsButton = ButtonAt(inspector, "Внутри →", new Vector2(17, -340), new Vector2(100, 23), InspectNextOccupant);
            returnToBuildingButton = ButtonAt(inspector, "К зданию", new Vector2(122, -340), new Vector2(97, 23), () =>
            {
                BuildingData building = game.World.GetBuilding(occupantsBuildingId);
                if (building != null) game.InspectSite(building.X, building.Y);
            });
            occupantsButton.gameObject.SetActive(false); returnToBuildingButton.gameObject.SetActive(false);
        }

        private void RefreshBuildingAccessControls(WorldState world)
        {
            UnitData selected = world.GetUnit(game.SelectedUnitId);
            BuildingData building = selected != null ? world.GetBuilding(selected.InsideBuildingId) :
                world.InBounds(game.InspectX, game.InspectY) ? world.GetBuilding(world.Cells[world.Index(game.InspectX, game.InspectY)].BuildingId) : null;
            occupantsBuildingId = building == null ? 0 : building.Id;
            int count = 0;
            if (building != null) foreach (UnitData unit in world.Units) if (unit.Health > 0 && unit.InsideBuildingId == building.Id) count++;
            bool show = count > 0;
            occupantsButton.gameObject.SetActive(show);
            returnToBuildingButton.gameObject.SetActive(show && selected != null);
            inspectorHint.gameObject.SetActive(!show);
            if (show) occupantsButton.GetComponentInChildren<Text>().text = "Внутри: " + count + " →";
        }

        private void InspectNextOccupant()
        {
            int id = NextOccupantId(game.World, occupantsBuildingId, game.SelectedUnitId);
            if (id != 0) game.InspectUnit(id);
        }

        // Returns only live, actual occupants. A queued visitor is still outside.
        public static int NextOccupantId(WorldState world, int buildingId, int afterUnitId)
        {
            if (world == null || buildingId <= 0 || world.GetBuilding(buildingId) == null) return 0;
            int first = 0; bool next = afterUnitId == 0;
            foreach (UnitData unit in world.Units)
            {
                if (unit.Health <= 0 || unit.InsideBuildingId != buildingId) continue;
                if (first == 0) first = unit.Id;
                if (next) return unit.Id;
                if (unit.Id == afterUnitId) next = true;
            }
            return first;
        }

        public static string BuildingPresenceDescription(WorldState world, UnitData unit)
        {
            BuildingData building = world.GetBuilding(unit.InsideBuildingId);
            if (building != null) return "\nВнутри: " + ResourceArt.BuildingName(building.Type);
            building = world.GetBuilding(unit.EnteringBuildingId);
            return building == null ? "" : "\nУ входа: " + ResourceArt.BuildingName(building.Type);
        }

        public static string BuildingAccessDescription(WorldState world, BuildingData building)
        {
            if (building == null || building.EntranceCell < 0 || building.EntranceCell >= world.Cells.Length) return "";
            StringBuilder text = new StringBuilder("<b>ВХОД И ЖИТЕЛИ</b>\nМест внутри: ");
            text.Append(BuildingAccess.Capacity(building)).Append("\nДверь: ");
            text.Append(building.EntranceCell % world.Width).Append(", ").Append(building.EntranceCell / world.Width);
            int count = 0;
            foreach (UnitData unit in world.Units)
            {
                if (unit.Health <= 0 || unit.InsideBuildingId != building.Id) continue;
                count++; if (count <= 8) text.Append('\n').Append(unit.Name).Append(" · ").Append(TaskName(world, unit));
            }
            if (count == 0) text.Append("\nВнутри никого нет");
            if (count > 8) text.Append("\nИ ещё ").Append(count - 8).Append(" жителей");
            return text.ToString();
        }
    }
}
