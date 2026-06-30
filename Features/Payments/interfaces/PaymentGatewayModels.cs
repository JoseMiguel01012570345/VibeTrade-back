namespace VibeTrade.Backend.Features.Payments.Interfaces;

public sealed record PaymentTransferRequest(
    string SourceAccountId,
    string DestinationAccountId,
    string Currency,
    long AmountMinor,
    string? Description = null,
    string? IdempotencyKey = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record PaymentTransferResult(
    bool Success,
    string? TransactionId,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record PaymentBalanceQuery(string AccountId, string Currency);

public sealed record PaymentBalanceResult(
    bool Success,
    long AvailableMinor,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record PaymentTransactionResult(
    string TransactionId,
    string Status,
    string SourceAccountId,
    string DestinationAccountId,
    string Currency,
    long AmountMinor,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyDictionary<string, string>? Metadata);
