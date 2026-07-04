namespace VibeTrade.Backend.Features.Payments.Dtos;

public sealed record BasisLineDto(
    string Category,
    string Label,
    string CurrencyLower,
    long AmountMinor,
    string? RouteSheetId,
    string? RouteStopId);

public sealed record CurrencyTotalsDto(
    string CurrencyLower,
    long SubtotalMinor,
    long ClimateMinor,
    long ProcessorFeeMinor,
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

public sealed record ExecutePaymentBody(
    string Currency,
    string PaymentMethodId,
    string? IdempotencyKey,
    IReadOnlyList<ServicePaymentPickDto>? SelectedServicePayments,
    IReadOnlyList<string>? SelectedRoutePathIds);

public sealed record CheckoutBreakdownBody(
    IReadOnlyList<ServicePaymentPickDto>? SelectedServicePayments,
    IReadOnlyList<string>? SelectedRoutePathIds);
