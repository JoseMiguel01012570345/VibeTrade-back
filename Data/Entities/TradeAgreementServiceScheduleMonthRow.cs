namespace VibeTrade.Backend.Data.Entities;

public sealed class TradeAgreementServiceScheduleMonthRow
{
    public string ServiceItemId { get; set; } = "";

    public TradeAgreementServiceItemRow ServiceItem { get; set; } = null!;

    public int Month { get; set; }
}
