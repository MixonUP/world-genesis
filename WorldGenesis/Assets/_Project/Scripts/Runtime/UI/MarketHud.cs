using System.Text;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    public sealed partial class GameHud
    {
        public static string MarketDescription(WorldState world, int settlementId)
        {
            if (world == null || world.GetSettlement(settlementId) == null) return "";
            StringBuilder text = new StringBuilder("<b>РЫНОК И ЗАКАЗЫ</b>\nМедь 1 · серебро 10 · золото 100\n");
            int waiting = 0;
            foreach (MarketNeedData need in world.MarketNeeds)
            {
                if (need.SettlementId != settlementId || need.State == MarketNeedState.Satisfied) continue;
                if (waiting++ >= 4) continue;
                text.Append(MarketCatalog.Name(need.Item)).Append(" · цель ").Append(need.Target.ToString("0.#"))
                    .Append("\n").Append(string.IsNullOrEmpty(need.Reason) ? "Ожидает планирования" : need.Reason).Append("\n");
            }
            if (waiting == 0) text.Append("Все текущие заявки обеспечены\n");
            if (waiting > 4) text.Append("Ещё заявок: ").Append(waiting - 4).Append("\n");
            text.Append("\nПоследние денежные сделки:"); int shown = 0;
            for (int i = world.MarketTrades.Count - 1; i >= 0 && shown < 3; i--)
            {
                MarketTradeData trade = world.MarketTrades[i];
                if (trade.SettlementId != settlementId) continue;
                string kind = trade.Kind == MarketTradeKind.Wage ? "Оплата труда" : trade.Kind == MarketTradeKind.Procurement ? "Закупка общины" : trade.Kind == MarketTradeKind.MaritimeImport ? "Морской ввоз" : trade.Kind == MarketTradeKind.MaritimeExport ? "Морская продажа" : "Покупка жителя";
                text.Append("\n").Append(kind).Append(": ").Append(trade.CopperPrice).Append(" мед.");
                if (trade.CopperTax > 0) text.Append(" · налог ").Append(trade.CopperTax);
                shown++;
            }
            if (shown == 0) text.Append("\nЕщё нет");
            return text.ToString();
        }
    }
}
