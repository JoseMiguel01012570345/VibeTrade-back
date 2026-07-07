namespace VibeTrade.Backend.Features.Affiliates;

/// <summary>Modo de comisión del afiliado (wiki cap. 11): monto fijo por venta o porcentaje del total.</summary>
public static class AffiliateCommissionKinds
{
    /// <summary>Monto fijo por pedido (acotado al total cobrado).</summary>
    public const string Fixed = "fixed";

    /// <summary>Porcentaje del total cobrado (subtotal + envío).</summary>
    public const string Percent = "percent";

    public static bool IsKnown(string? raw)
    {
        var s = (raw ?? "").Trim();
        return s is Fixed or Percent;
    }
}
