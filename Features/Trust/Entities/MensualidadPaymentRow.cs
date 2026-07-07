namespace VibeTrade.Backend.Features.Trust.Entities;

/// <summary>
/// Pago de mensualidad de la plataforma (wiki cap. 08). Registrado cuando un usuario bajo el umbral
/// de confianza paga para rehabilitar sus interacciones; deja rastro del puntaje antes/después.
/// </summary>
public sealed class MensualidadPaymentRow
{
    public string Id { get; set; } = "";

    public string UserId { get; set; } = "";

    public string? PaymentMethod { get; set; }

    public string? PaymentReference { get; set; }

    /// <summary>Puntaje antes de aplicar el pago.</summary>
    public int TrustScoreBefore { get; set; }

    /// <summary>Puntaje tras aplicar el pago (mínimo, el umbral).</summary>
    public int TrustScoreAfter { get; set; }

    public DateTimeOffset PaidAtUtc { get; set; }
}
