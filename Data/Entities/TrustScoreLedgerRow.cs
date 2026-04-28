namespace VibeTrade.Backend.Data.Entities;

/// <summary>Historial persistente de cambios en la barra de confianza (usuario o tienda).</summary>
public sealed class TrustScoreLedgerRow
{
    public string Id { get; set; } = "";

    /// <summary><c>user</c> o <c>store</c> (constantes en <c>VibeTrade.Backend.Features.Trust.TrustLedgerSubjects</c>).</summary>
    public string SubjectType { get; set; } = "";

    public string SubjectId { get; set; } = "";

    public int Delta { get; set; }

    public int BalanceAfter { get; set; }

    public string Reason { get; set; } = "";

    public DateTimeOffset CreatedAtUtc { get; set; }
}
