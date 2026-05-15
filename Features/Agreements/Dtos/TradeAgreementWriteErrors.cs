namespace VibeTrade.Backend.Features.Agreements.Dtos;

public static class TradeAgreementWriteErrors
{
    /// <summary>Ya hay otro acuerdo activo en el hilo con el mismo nombre (comparación sin distinguir mayúsculas).</summary>
    public const string DuplicateAgreementTitle = "duplicate_agreement_title";
}
