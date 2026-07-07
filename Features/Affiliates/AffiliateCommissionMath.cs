using VibeTrade.Backend.Features.Affiliates.Entities;

namespace VibeTrade.Backend.Features.Affiliates;

/// <summary>Cálculo de comisión del afiliado (adaptado de <c>AffiliateCommissionMath</c> de la referencia).</summary>
public static class AffiliateCommissionMath
{
    /// <summary>Comisión acotada al total cobrado (subtotal + envío). Redondeada a 2 decimales.</summary>
    public static decimal ComputeCommission(decimal subtotal, decimal deliveryFee, AffiliateRow affiliate)
    {
        var totalCharged = subtotal + deliveryFee;
        if (totalCharged <= 0 || affiliate.CommissionValue <= 0)
            return 0;

        var raw = affiliate.CommissionKind switch
        {
            AffiliateCommissionKinds.Fixed => affiliate.CommissionValue,
            AffiliateCommissionKinds.Percent => totalCharged * (affiliate.CommissionValue / 100m),
            _ => 0m,
        };

        return Math.Round(Math.Min(raw, totalCharged), 2, MidpointRounding.AwayFromZero);
    }
}
