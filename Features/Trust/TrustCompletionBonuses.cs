namespace VibeTrade.Backend.Features.Trust;

public static class TrustCompletionBonuses
{
    public const int CarrierPerTramoAccepted = 2;
    public const int StoreOnAgreementCompleted = 4;
    public const int BuyerOnPurchaseCompleted = 2;

    public const string CarrierTramoReason =
        "Tramo entregado: evidencia aceptada — tienda transportista";

    public const string StoreAgreementReason =
        "Acuerdo completado: compra cerrada";

    public const string BuyerPurchaseReason =
        "Compra completada";

    public const string AgreementCompletionLedgerPrefix = "Acuerdo completado";
}
