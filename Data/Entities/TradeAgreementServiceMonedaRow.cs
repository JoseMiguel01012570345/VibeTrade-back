namespace VibeTrade.Backend.Data.Entities;

public sealed class TradeAgreementServiceMonedaRow
{
    public string Id { get; set; } = "";

    public string ServiceItemId { get; set; } = "";

    public TradeAgreementServiceItemRow ServiceItem { get; set; } = null!;

    public int SortOrder { get; set; }

    public string Code { get; set; } = "";
}
