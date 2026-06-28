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
            StripeFeeMinorActual = p.StripeFeeMinorActual,
            StripeFeeMinorEstimated = p.StripeFeeMinorEstimated,
            TotalChargedMinor = p.TotalChargedMinor,
            StripePricingUrl = p.StripePricingUrl,
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
            StripeFeeMinorActual = b.StripeFeeMinorActual,
            StripeFeeMinorEstimated = b.StripeFeeMinorEstimated,
            TotalChargedMinor = b.TotalChargedMinor,
            StripePricingUrl = b.StripePricingUrl,
            Lines = b.Lines,
            InvoiceIssuerPlatform = b.InvoiceIssuerPlatform,
            InvoiceStoreName = b.InvoiceStoreName,
        };
}
