namespace VibeTrade.Backend.Features.Agreements.Dtos;

public static class TradeAgreementWriteErrors
{
    /// <summary>Ya hay otro acuerdo activo en el hilo con el mismo nombre (comparación sin distinguir mayúsculas).</summary>
    public const string DuplicateAgreementTitle = "duplicate_agreement_title";

    /// <summary>La hoja de ruta ya está vinculada a otro acuerdo activo en el mismo hilo.</summary>
    public const string RouteSheetAlreadyLinked = "route_sheet_already_linked";

    /// <summary>El acuerdo mezcla monedas en ítems cobrables o tramos vinculados.</summary>
    public const string SingleAgreementCurrency = "single_agreement_currency";
}
