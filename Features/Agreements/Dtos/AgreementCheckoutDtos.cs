namespace VibeTrade.Backend.Features.Agreements.Dtos;

public sealed record AgreementPaymentStatusDto(
    string Currency,
    string Status,
    long TotalAmountMinor,
    string GatewayTransactionId,
    DateTimeOffset? CompletedAtUtc);

/// <summary>Resultado POST execute: transacción de pasarela, éxito, client_secret opcional, mensaje de error, Accepted, código error.</summary>
public sealed record AgreementExecutePaymentResultDto(
    string GatewayTransactionId,
    bool Succeeded,
    string? ClientSecretForConfirmation,
    string? PaymentErrorMessage,
    bool Accepted,
    string? ErrorCode,
    string? AgreementCurrencyPaymentId = null);
