namespace VibeTrade.Backend.Data.Entities;

public sealed class TradeAgreementServicePaymentEntryRow
{
    public string Id { get; set; } = "";

    public string ServiceItemId { get; set; } = "";

    public TradeAgreementServiceItemRow ServiceItem { get; set; } = null!;

    public int SortOrder { get; set; }

    public int Month { get; set; }

    public int Day { get; set; }

    public string Amount { get; set; } = "";
}
