using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Affiliates.Dtos;
using VibeTrade.Backend.Features.Affiliates.Entities;
using VibeTrade.Backend.Features.Affiliates.Interfaces;

namespace VibeTrade.Backend.Features.Affiliates;

public sealed class AffiliateService(AppDbContext db) : IAffiliateService
{
    public async Task<AffiliateRow?> FindActiveByCodeAsync(string? code, CancellationToken cancellationToken = default)
    {
        var c = (code ?? "").Trim();
        if (c.Length == 0)
            return null;
        return await db.Affiliates.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Active && a.Code == c, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<(string? AffiliateId, decimal Commission, string? CurrencyCode)> ResolveCommissionAsync(
        string? code,
        decimal subtotal,
        decimal deliveryFee,
        string orderCurrencyCode,
        CancellationToken cancellationToken = default)
    {
        var affiliate = await FindActiveByCodeAsync(code, cancellationToken).ConfigureAwait(false);
        if (affiliate is null)
            return (null, 0m, null);

        // La comisión fija solo aplica si su moneda coincide con la del pedido (sin proveedor FX en VibeTrade).
        if (string.Equals(affiliate.CommissionKind, AffiliateCommissionKinds.Fixed, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(affiliate.CommissionCurrencyCode)
            && !string.Equals(affiliate.CommissionCurrencyCode!.Trim(), (orderCurrencyCode ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return (affiliate.Id, 0m, null);
        }

        var commission = AffiliateCommissionMath.ComputeCommission(subtotal, deliveryFee, affiliate);
        return (affiliate.Id, commission, orderCurrencyCode);
    }

    public async Task RegisterVisitAsync(string? code, CancellationToken cancellationToken = default)
    {
        var c = (code ?? "").Trim();
        if (c.Length == 0)
            return;
        await db.Affiliates
            .Where(a => a.Code == c)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Visits, a => a.Visits + 1), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AffiliateDashboardDto>> GetDashboardsForOwnerAsync(
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var owner = (ownerUserId ?? "").Trim();
        if (owner.Length < 2)
            return Array.Empty<AffiliateDashboardDto>();

        var mine = await db.Affiliates.AsNoTracking()
            .Where(a => a.OwnerUserId == owner)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (mine.Count == 0)
            return Array.Empty<AffiliateDashboardDto>();

        var codes = mine.Select(a => a.Code).ToArray();
        var attributed = await db.Orders.AsNoTracking()
            .Where(o => o.AffiliateCodeSnapshot != null && codes.Contains(o.AffiliateCodeSnapshot))
            .Select(o => new { o.AffiliateCodeSnapshot, o.CurrencyCode, o.AffiliateCommissionAmount })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return mine.Select(a =>
        {
            var forCode = attributed.Where(o => string.Equals(o.AffiliateCodeSnapshot, a.Code, StringComparison.Ordinal)).ToList();
            var totals = forCode
                .Where(o => o.AffiliateCommissionAmount is > 0)
                .GroupBy(o => (o.CurrencyCode ?? "").Trim().ToUpperInvariant(), StringComparer.Ordinal)
                .Select(g => new AffiliateCommissionTotalDto(
                    g.Key,
                    Math.Round(g.Sum(x => x.AffiliateCommissionAmount ?? 0), 2, MidpointRounding.AwayFromZero)))
                .OrderBy(x => x.CurrencyCode, StringComparer.Ordinal)
                .ToList();
            return new AffiliateDashboardDto(
                a.Id,
                a.Code,
                a.DisplayName,
                a.CommissionKind,
                a.CommissionValue,
                a.CommissionCurrencyCode,
                a.Visits,
                forCode.Count,
                totals);
        }).ToList();
    }
}
