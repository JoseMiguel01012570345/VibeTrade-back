namespace VibeTrade.Backend.Features.Payments.Dtos;

public sealed record BasisLineDto(
    string Category,
    string Label,
    string CurrencyLower,
    long AmountMinor,
    string? RouteSheetId,
    string? RouteStopId,
    string? MerchandiseLineId = null);

public sealed record CurrencyTotalsDto(
    string CurrencyLower,
    long SubtotalMinor,
    long ClimateMinor,
    long StripeFeeMinor,
    long TotalMinor,
    IReadOnlyList<BasisLineDto> Lines);

public sealed record BreakdownDto(
    bool Ok,
    IReadOnlyList<string> Errors,
    IReadOnlyList<CurrencyTotalsDto> ByCurrency);

public sealed record ServicePaymentPickDto(
    string ServiceItemId,
    int EntryMonth,
    int EntryDay);
