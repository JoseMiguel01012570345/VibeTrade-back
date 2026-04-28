namespace VibeTrade.Backend.Data.Entities;

public sealed class TradeAgreementServiceScheduleOverrideRow
{
    public string ServiceItemId { get; set; } = "";

    public TradeAgreementServiceItemRow ServiceItem { get; set; } = null!;

    public int Month { get; set; }

    public int CalendarDay { get; set; }

    public string WindowStart { get; set; } = "";

    public string WindowEnd { get; set; } = "";
}
