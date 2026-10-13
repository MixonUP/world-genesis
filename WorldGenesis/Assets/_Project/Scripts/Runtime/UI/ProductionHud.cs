using System.Text;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    public sealed partial class GameHud
    {
        private static string IndustryProfessionName(UnitProfession profession)
        {
            switch (profession)
            {
                case UnitProfession.Smelter: return "плавильщик";
                case UnitProfession.Blacksmith: return "кузнец";
                case UnitProfession.Tanner: return "кожевник";
                case UnitProfession.Weaver: return "ткач";
                case UnitProfession.Tailor: return "портной";
                case UnitProfession.MintWorker: return "чеканщик";
                default: return "";
            }
        }

        public static string ManufacturedEquipmentDescription(UnitData unit)
        {
            StringBuilder text = new StringBuilder();
            if (unit.WorkTool == ItemType.None) text.Append("Примитивные рабочие инструменты");
            else text.Append(ResourceArt.ItemName(unit.WorkTool)).Append(" · работа +")
                .Append(((IndustryCatalog.ToolMultiplier(unit.WorkTool) - 1) * 100).ToString("0")).Append('%');
            text.Append('\n');
            if (unit.Clothes == ItemType.None) text.Append("Повседневная одежда; нового комплекта нет");
            else text.Append(ResourceArt.ItemName(unit.Clothes)).Append(" · утомление −")
                .Append(((1 - IndustryCatalog.ClothingFatigueMultiplier(unit.Clothes)) * 100).ToString("0")).Append('%');
            return text.ToString();
        }

        public static string ProductionDescription(WorldState world, BuildingData building)
        {
            if (world == null || building == null || !IndustryCatalog.IsWorkshop(building.Type)) return "";
            StringBuilder text = new StringBuilder("<b>ПРОИЗВОДСТВО</b>\n");
            IndustryRecipe recipe = IndustryCatalog.Get(building.ActiveIndustryRecipe);
            text.Append(IndustryCatalog.WaitingReason(world, building));
            if (recipe != null)
            {
                text.Append("\nЗаказ: ").Append(IndustryCatalog.Name(recipe.Id));
                UnitData worker = world.GetUnit(building.IndustryWorkerId);
                if (worker != null) text.Append("\nРаботник: ").Append(worker.Name).Append(worker.InsideBuildingId == building.Id ? " · внутри" : " · снаружи");
                text.Append("\nОбработка ").Append(building.IndustryProgress.ToString("0.0"))
                    .Append(" / ").Append(recipe.Seconds.ToString("0.#")).Append(" с");
                text.Append("\nСырьё в мастерской:");
                foreach (ItemAmount input in recipe.Inputs)
                    text.Append("\n  ").Append(ResourceArt.ItemName(input.Item)).Append(' ')
                        .Append(building.Inventory[(int)input.Item].ToString("0.##")).Append(" / ").Append(input.Amount.ToString("0.##"));
                foreach (AgriculturalAmount input in recipe.Agriculture)
                    text.Append("\n  ").Append(AgricultureArt.ProductName(input.Product)).Append(' ')
                        .Append(building.AgriculturalStock[(int)input.Product].ToString("0.##")).Append(" / ").Append(input.Amount.ToString("0.##"));
                text.Append("\nВыход партии: ").Append(ResourceArt.ItemName(recipe.Output)).Append(" × ").Append(recipe.OutputAmount.ToString("0.##"));
            }
            else
            {
                text.Append("\nОткрытые рецепты:"); bool any = false;
                foreach (IndustryRecipe candidate in IndustryCatalog.All)
                {
                    if (candidate.Building != building.Type || !world.HasTechnology(building.SettlementId, candidate.Technology)) continue;
                    text.Append("\n  ").Append(IndustryCatalog.Name(candidate.Id)); any = true;
                }
                if (!any) text.Append(" нет, нужны исследования");
            }
            text.Append("\nГотовых партий: ").Append(building.CompletedIndustryBatches);
            if (building.Type == BuildingType.Carpentry)
                text.Append("\nОсадные комплекты: изготовление после исследований. Развёртывание и экипажи ещё недоступны.");
            return text.ToString();
        }
    }
}
