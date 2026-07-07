namespace VibeTrade.Backend.Features.Chat.Dtos;

/// <summary>Mapea bloque recibo unificado ↔ <see cref="ChatPaymentFeeReceiptData"/> (PDF / email / servicios).</summary>
public static class ChatUnifiedPlatformReceiptMapper
{
    public static ChatUnifiedPlatformPaymentFeeReceiptBlock FromPayload(ChatPaymentFeeReceiptData p) =>
        new()
        {
            AgreementId = p.AgreementId,
            AgreementTitle = p.AgreementTitle,
            PaymentId = p.PaymentId,
            CurrencyLower = p.CurrencyLower,
            SubtotalMinor = p.SubtotalMinor,
            ClimateMinor = p.ClimateMinor,
            ProcessorFeeMinorActual = p.ProcessorFeeMinorActual,
            ProcessorFeeMinorEstimated = p.ProcessorFeeMinorEstimated,
            TotalChargedMinor = p.TotalChargedMinor,
            PaymentFeePolicyUrl = p.PaymentFeePolicyUrl,
            Lines = p.Lines,
            InvoiceIssuerPlatform = p.InvoiceIssuerPlatform,
            InvoiceStoreName = p.InvoiceStoreName,
        };

    public static ChatPaymentFeeReceiptData ToData(ChatUnifiedPlatformPaymentFeeReceiptBlock b) =>
        new()
        {
            AgreementId = b.AgreementId,
            AgreementTitle = b.AgreementTitle,
            PaymentId = b.PaymentId,
            CurrencyLower = b.CurrencyLower,
            SubtotalMinor = b.SubtotalMinor,
            ClimateMinor = b.ClimateMinor,
            ProcessorFeeMinorActual = b.ProcessorFeeMinorActual,
            ProcessorFeeMinorEstimated = b.ProcessorFeeMinorEstimated,
            TotalChargedMinor = b.TotalChargedMinor,
            PaymentFeePolicyUrl = b.PaymentFeePolicyUrl,
            Lines = b.Lines,
            InvoiceIssuerPlatform = b.InvoiceIssuerPlatform,
            InvoiceStoreName = b.InvoiceStoreName,
        };
}
