namespace VibeTrade.Backend.Data.RouteSheets;

/// <summary>Acuse post-edición de hoja (mismo contrato que <c>routeSheetEditAcks</c> en el cliente).</summary>
public sealed class RouteSheetEditAckPayload
{
    public int Revision { get; set; }

    /// <summary>userId transportista → pending | accepted | rejected</summary>
    public Dictionary<string, string> ByCarrier { get; set; } = new(StringComparer.Ordinal);
}
