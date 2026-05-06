namespace VibeTrade.Backend.Features.Chat.Dtos;

public sealed record AgreementPaymentStatusDto(
    string Currency,
    string Status,
    long TotalAmountMinor,
    string StripePaymentIntentId,
    DateTimeOffset? CompletedAtUtc);

/// <summary>Resultado POST execute: Stripe PaymentIntent, éxito, client_secret opcional (3DS), mensaje Stripe, Accepted, código error.</summary>
public sealed record AgreementExecutePaymentResultDto(
    string PaymentIntentId,
    bool Succeeded,
    string? ClientSecretForConfirmation,
    string? StripeErrorMessage,
    bool Accepted,
    string? ErrorCode,
    string? AgreementCurrencyPaymentId = null);
