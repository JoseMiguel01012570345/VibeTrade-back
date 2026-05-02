namespace VibeTrade.Backend.Data.Entities;

public static class CarrierOwnershipActions
{
    public const string Granted = "granted";
    public const string Released = "released";
}

/// <summary>Auditoría append-only de cesiones de ownership entre transportistas.</summary>
public sealed class CarrierOwnershipEventRow
{
    public string Id { get; set; } = "";

    public string ThreadId { get; set; } = "";

    public string RouteSheetId { get; set; } = "";

    public string RouteStopId { get; set; } = "";

    public string CarrierUserId { get; set; } = "";

    /// <summary><see cref="CarrierOwnershipActions"/>.</summary>
    public string Action { get; set; } = "";

    public DateTimeOffset AtUtc { get; set; }

    /// <summary>Motivo corto legible (p. ej. <c>payment_success</c>, <c>handoff_from_previous</c>, <c>carrier_withdraw</c>).</summary>
    public string Reason { get; set; } = "";
}
