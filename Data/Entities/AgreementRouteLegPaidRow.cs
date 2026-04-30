namespace VibeTrade.Backend.Data.Entities;

/// <summary>Marca cuánto de un cobro monetario pertenece a un tramo (parada en hoja).</summary>
public sealed class AgreementRouteLegPaidRow
{
    public string Id { get; set; } = "";

    public string AgreementCurrencyPaymentId { get; set; } = "";

    public AgreementCurrencyPaymentRow AgreementCurrencyPayment { get; set; } = null!;

    /// <summary>Id de parada dentro del JSON (<see cref="Data.RouteSheets.RouteStopPayload.Id"/>).</summary>
    public string RouteSheetId { get; set; } = "";

    /// <summary>Id de parada (<c>stop_…</c>).</summary>
    public string RouteStopId { get; set; } = "";

    public long AmountMinor { get; set; }
}
