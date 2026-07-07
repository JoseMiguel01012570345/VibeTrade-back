namespace VibeTrade.Backend.Features.Payments.Interfaces;

public sealed record PaymentMethodResolve(
    bool Success,
    string? PaymentMethodId,
    string? CardBrand,
    string? CardLast4,
    string? ErrorMessage,
    string? ErrorCode,
    bool Accepted);
