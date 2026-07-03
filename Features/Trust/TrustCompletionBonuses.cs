namespace VibeTrade.Backend.Features.Trust;

public static class TrustCompletionBonuses
{
    public const int CarrierPerTramoAccepted = 2;
    public const int StoreOnAgreementCompleted = 4;

    /// <summary>Bono de confianza al comprador por completar una compra de mercancía (wiki cap. 10: +3).</summary>
    public const int BuyerOnPurchaseCompleted = 3;

    public const string CarrierTramoReason =
        "Tramo entregado: evidencia aceptada — tienda transportista";

    public const string StoreAgreementReason =
        "Acuerdo completado: compra cerrada";

    public const string BuyerPurchaseReason =
        "Compra completada";

    public const string AgreementCompletionLedgerPrefix = "Acuerdo completado";
}

/// <summary>Penalizaciones de confianza por incumplimientos (wiki cap. 05/10).</summary>
public static class TrustPenalties
{
    /// <summary>Penalización a la tienda al invalidar un pedido de mercancía (wiki cap. 10).</summary>
    public const int StoreInvalidateOrder = -5;

    /// <summary>Penalización por miembro al salir del chat de servicios (wiki cap. 05/10: −3 × otros).</summary>
    public const int ChatExitPerOtherMember = -3;

    /// <summary>Penalización por tramo confirmado abandonado al retirarse un transportista (wiki cap. 09/10).</summary>
    public const int CarrierWithdrawPerConfirmedStop = -3;

    /// <summary>Penalización al transportista por evidencia vencida a las 24 h (wiki cap. 04/10).</summary>
    public const int CarrierEvidenceExpired = -3;

    public const string StoreInvalidateOrderReason = "Pedido de mercancía invalidado por la tienda";
    public const string ChatExitReason = "Salida del chat con miembros activos";
    public const string CarrierWithdrawReason = "Retiro de transportista con tramos confirmados";
    public const string CarrierEvidenceExpiredReason = "Evidencia de tramo vencida (24 h)";
}
