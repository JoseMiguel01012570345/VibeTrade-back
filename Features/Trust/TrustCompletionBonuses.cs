namespace VibeTrade.Backend.Features.Trust;

public static class TrustCompletionBonuses
{
    public const int CarrierPerTramoAccepted = 2;
    public const int StoreOnAgreementCompleted = 4;
    public const int BuyerOnPurchaseCompleted = 2;

    public const string CarrierTramoReason =
        "Tramo entregado: evidencia aceptada (demo)";

    public const string StoreAgreementReason =
        "Acuerdo completado: compra cerrada (demo)";

    public const string BuyerPurchaseReason =
        "Compra completada (demo)";

    public const string AgreementCompletionLedgerPrefix = "Acuerdo completado";
}
