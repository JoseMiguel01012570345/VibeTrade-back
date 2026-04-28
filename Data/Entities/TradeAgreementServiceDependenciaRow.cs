namespace VibeTrade.Backend.Data.Entities;

public sealed class TradeAgreementServiceDependenciaRow
{
    public string Id { get; set; } = "";

    public string ServiceItemId { get; set; } = "";

    public TradeAgreementServiceItemRow ServiceItem { get; set; } = null!;

    public int SortOrder { get; set; }

    public string Text { get; set; } = "";
}
