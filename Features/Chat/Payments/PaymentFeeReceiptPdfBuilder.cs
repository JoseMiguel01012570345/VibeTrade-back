using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Chat.Payments;

/// <summary>PDF del informe de pago (mismo contenido base que el mensaje de chat).</summary>
public static class PaymentFeeReceiptPdfBuilder
{
    static PaymentFeeReceiptPdfBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Build(ChatPaymentFeeReceiptPayload p)
    {
        var cur = (p.CurrencyLower ?? "").Trim().ToUpperInvariant();
        if (cur.Length == 0)
            cur = "???";

        string Money(long minor) => (minor / 100m).ToString("N2", CultureInfo.InvariantCulture) + " " + cur;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(36);
                page.Size(PageSizes.A4);

                page.Header().Column(h =>
                {
                    var issuer = string.IsNullOrWhiteSpace(p.InvoiceIssuerPlatform)
                        ? "VibeTrade"
                        : p.InvoiceIssuerPlatform.Trim();
                    var store = (p.InvoiceStoreName ?? "").Trim();
                    h.Item().Text("Informe de pago").FontSize(18).SemiBold();
                    h.Item().PaddingTop(4).Text($"Emisor: {issuer}").FontSize(11);
                    if (store.Length > 0)
                        h.Item().Text($"Tienda (chat): {store}").FontSize(11);
                    h.Item().PaddingTop(4).Text($"Acuerdo: {p.AgreementTitle}").FontSize(11);
                    h.Item().Text($"Id. acuerdo: {p.AgreementId}").FontSize(9).FontColor(Colors.Grey.Darken2);
                    h.Item().Text($"Id. pago: {p.PaymentId}").FontSize(9).FontColor(Colors.Grey.Darken2);
                });

                page.Content().PaddingTop(16).Column(col =>
                {
                    col.Item().Text("Desglose").SemiBold().FontSize(12);
                    col.Item().PaddingTop(6).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(1);
                        });

                        static IContainer CellStyle(IContainer c) =>
                            c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4);

                        foreach (var line in p.Lines)
                        {
                            var label = (line.Label ?? "").Trim();
                            if (label.Length == 0)
                                label = "—";
                            t.Cell().Element(CellStyle).Text(label).FontSize(10);
                            t.Cell().Element(CellStyle).AlignRight().Text(Money(line.AmountMinor)).FontSize(10);
                        }
                    });

                    col.Item().PaddingTop(12).Column(sum =>
                    {
                        sum.Item().Row(r =>
                        {
                            r.RelativeItem().Text("Subtotal").FontSize(10);
                            r.ConstantItem(120).AlignRight().Text(Money(p.SubtotalMinor)).FontSize(10);
                        });
                        sum.Item().PaddingTop(2).Row(r =>
                        {
                            r.RelativeItem().Text("Climate 0,05 % (referencia, no cobrado)").FontSize(10);
                            r.ConstantItem(120).AlignRight().Text(Money(p.ClimateMinor)).FontSize(10);
                        });
                        sum.Item().PaddingTop(2).Row(r =>
                        {
                            r.RelativeItem().Text("Tarifa Stripe liquidación (referencia)").FontSize(10);
                            r.ConstantItem(120).AlignRight().Text(Money(p.StripeFeeMinorActual)).FontSize(10);
                        });
                        sum.Item().PaddingTop(2).Row(r =>
                        {
                            r.RelativeItem().Text("Tarifa Stripe estimada antes del cobro (referencia)")
                                .FontSize(9)
                                .FontColor(Colors.Grey.Darken1);
                            r.ConstantItem(120).AlignRight().Text(Money(p.StripeFeeMinorEstimated)).FontSize(9)
                                .FontColor(Colors.Grey.Darken1);
                        });
                        sum.Item().PaddingTop(6).Row(r =>
                        {
                            r.RelativeItem().Text("Total cobrado al comprador (subtotal)").SemiBold().FontSize(11);
                            r.ConstantItem(120).AlignRight().Text(Money(p.TotalChargedMinor)).SemiBold()
                                .FontSize(11);
                        });
                    });

                    var pricing = (p.StripePricingUrl ?? "").Trim();
                    if (pricing.Length > 0)
                    {
                        col.Item().PaddingTop(20).Text("Políticas y precios Stripe").SemiBold().FontSize(10);
                        col.Item().PaddingTop(2).Hyperlink(pricing).Text(pricing).FontSize(9)
                            .FontColor(Colors.Blue.Medium);
                    }

                    col.Item().PaddingTop(24).Text("Documento generado automáticamente por VibeTrade.").FontSize(8)
                        .FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();
    }
}
