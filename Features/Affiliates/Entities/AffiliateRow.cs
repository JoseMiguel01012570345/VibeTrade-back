namespace VibeTrade.Backend.Features.Affiliates.Entities;

/// <summary>
/// Afiliado que refiere ventas mediante un <see cref="Code"/>. Genera comisión (fija o porcentual)
/// que se registra como deuda de afiliado al pasar el pedido a «En tránsito» (wiki cap. 11).
/// </summary>
public sealed class AffiliateRow
{
    public string Id { get; set; } = "";

    /// <summary>Código público del afiliado (se introduce en el checkout). Único.</summary>
    public string Code { get; set; } = "";

    /// <summary>Usuario dueño del afiliado (para su panel).</summary>
    public string OwnerUserId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary><see cref="AffiliateCommissionKinds"/>.</summary>
    public string CommissionKind { get; set; } = AffiliateCommissionKinds.Percent;

    /// <summary>Valor de la comisión: monto (fixed) o porcentaje 0-100 (percent).</summary>
    public decimal CommissionValue { get; set; }

    /// <summary>Moneda del monto fijo (para <see cref="AffiliateCommissionKinds.Fixed"/>).</summary>
    public string? CommissionCurrencyCode { get; set; }

    /// <summary>Contador de visitas atribuidas al enlace del afiliado.</summary>
    public long Visits { get; set; }

    public bool Active { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
