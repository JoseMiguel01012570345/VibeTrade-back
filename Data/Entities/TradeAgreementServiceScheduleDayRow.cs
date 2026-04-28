namespace VibeTrade.Backend.Data.Entities;

public sealed class TradeAgreementServiceScheduleDayRow
{
    public string ServiceItemId { get; set; } = "";

    public TradeAgreementServiceItemRow ServiceItem { get; set; } = null!;

    public int Month { get; set; }

    public int CalendarDay { get; set; }
}
