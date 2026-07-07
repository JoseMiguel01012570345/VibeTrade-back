using VibeTrade.Backend.Features.Affiliates.Dtos;
using VibeTrade.Backend.Features.Affiliates.Entities;

namespace VibeTrade.Backend.Features.Affiliates.Interfaces;

/// <summary>Resolución de afiliados por código y cálculo de comisión para el checkout + panel del afiliado.</summary>
public interface IAffiliateService
{
    /// <summary>Afiliado activo por código, o null.</summary>
    Task<AffiliateRow?> FindActiveByCodeAsync(string? code, CancellationToken cancellationToken = default);

    /// <summary>Comisión a snapshotear en el pedido (0 si el código no es válido/activo).</summary>
    Task<(string? AffiliateId, decimal Commission, string? CurrencyCode)> ResolveCommissionAsync(
        string? code,
        decimal subtotal,
        decimal deliveryFee,
        string orderCurrencyCode,
        CancellationToken cancellationToken = default);

    /// <summary>Incrementa el contador de visitas del afiliado (para su dashboard).</summary>
    Task RegisterVisitAsync(string? code, CancellationToken cancellationToken = default);

    /// <summary>Dashboard del afiliado dueño: visitas, ventas atribuidas y comisiones por moneda.</summary>
    Task<IReadOnlyList<AffiliateDashboardDto>> GetDashboardsForOwnerAsync(
        string ownerUserId,
        CancellationToken cancellationToken = default);
}
