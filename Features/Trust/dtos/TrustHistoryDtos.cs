namespace VibeTrade.Backend.Features.Trust.Dtos;

public sealed record TrustHistoryItemDto(
    string Id,
    DateTimeOffset At,
    int Delta,
    int BalanceAfter,
    string Reason);

public sealed record TrustAdjustRequest(int Delta, string? Reason);

public sealed record TrustAdjustResponse(int TrustScore, TrustHistoryItemDto Entry);

/// <summary>
/// Estado de la barra de confianza para el gate de interacciones (wiki cap. 08/10).
/// <c>State</c> es <c>active</c> o <c>blocked</c>; <c>InteractionsEnabled</c> es <c>false</c> bajo umbral.
/// </summary>
public sealed record TrustStatusDto(
    int TrustScore,
    int Threshold,
    string State,
    bool InteractionsEnabled,
    bool MensualidadRequired);

public sealed record MensualidadPayRequest(string? PaymentMethod, string? PaymentReference);

public sealed record MensualidadPayResponse(
    bool Success,
    TrustStatusDto Status,
    bool CrossedThresholdUp,
    TrustHistoryItemDto? Entry);
