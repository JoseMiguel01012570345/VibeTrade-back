namespace VibeTrade.Backend.Features.Trust;

public sealed record TrustHistoryItemDto(
    string Id,
    DateTimeOffset At,
    int Delta,
    int BalanceAfter,
    string Reason);

public sealed record TrustAdjustRequest(int Delta, string? Reason);

public sealed record TrustAdjustResponse(int TrustScore, TrustHistoryItemDto Entry);
