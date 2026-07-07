namespace VibeTrade.Backend.Features.Affiliates.Dtos;

public sealed record AffiliateDashboardDto(
    string AffiliateId,
    string Code,
    string DisplayName,
    string CommissionKind,
    decimal CommissionValue,
    string? CommissionCurrencyCode,
    long Visits,
    int SalesCount,
    IReadOnlyList<AffiliateCommissionTotalDto> CommissionTotals);

public sealed record AffiliateCommissionTotalDto(string CurrencyCode, decimal Amount);
