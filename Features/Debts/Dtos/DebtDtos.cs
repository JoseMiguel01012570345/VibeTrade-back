namespace VibeTrade.Backend.Features.Debts.Dtos;

public sealed record WarehouseDebtDto(
    string Id,
    string StoreId,
    string OrderId,
    string OrderPublicNumber,
    decimal Amount,
    string CurrencyCode,
    bool Liquidated,
    bool Deleted,
    DateTimeOffset CreatedAtUtc);

public sealed record AffiliateDebtDto(
    string Id,
    string? AffiliateId,
    string AffiliateCode,
    string OrderId,
    string OrderPublicNumber,
    decimal Amount,
    string CurrencyCode,
    bool Liquidated,
    bool Deleted,
    DateTimeOffset CreatedAtUtc);

public sealed record CarrierDebtDto(
    string Id,
    string CarrierUserId,
    string OrderId,
    string OrderPublicNumber,
    string RouteSheetId,
    string RouteStopId,
    double TotalKm,
    decimal RatePerKm,
    decimal Amount,
    string CurrencyCode,
    bool Liquidated,
    bool Deleted,
    DateTimeOffset CreatedAtUtc);

/// <summary>Vista Finanzas: deudas por tipo + totales pendientes por moneda (fondos / restantes).</summary>
public sealed record DebtsOverviewDto(
    IReadOnlyList<WarehouseDebtDto> Warehouse,
    IReadOnlyList<AffiliateDebtDto> Affiliate,
    IReadOnlyList<CarrierDebtDto> Carrier,
    IReadOnlyList<DebtCurrencyTotalDto> PendingTotals,
    IReadOnlyList<DebtCurrencyTotalDto> LiquidatedTotals);

public sealed record DebtCurrencyTotalDto(string CurrencyCode, decimal Amount);

public sealed record LiquidateDebtsRequest(
    IReadOnlyList<string>? WarehouseDebtIds,
    IReadOnlyList<string>? AffiliateDebtIds);

public sealed record LiquidateDebtsResponse(int LiquidatedWarehouse, int LiquidatedAffiliate);

public sealed record DebtError(string Code, string Message);
