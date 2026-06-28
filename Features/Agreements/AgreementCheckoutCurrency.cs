using VibeTrade.Backend.Features.Payments;

namespace VibeTrade.Backend.Features.Agreements;

/// <summary>Reglas de moneda única en checkout de acuerdos (sin acoplar otras features al servicio).</summary>
public static class AgreementCheckoutCurrency
{
    public const string MultipleAgreementCurrenciesMessage =
        "El acuerdo debe cobrarse en una sola moneda; unifica mercadería, servicios y transporte.";

    public const string MissingBillableCurrencyMessage =
        "El acuerdo debe indicar la moneda de los ítems cobrables antes de pagar.";

    public const string RouteStopCurrencyMismatchMessage =
        "Los tramos deben usar la misma moneda de pago que el resto del acuerdo vinculado.";

    /// <summary>Compat: mensaje histórico de mercadería multi-moneda.</summary>
    public const string MultipleMerchandiseCurrenciesMessage = MultipleAgreementCurrenciesMessage;

    public const string MissingMerchandiseCurrencyMessage = MissingBillableCurrencyMessage;

    public static HashSet<string> CollectBillableCurrencies(
        TradeAgreementRow ag,
        RouteSheetPayload? routeSheetPayload)
    {
        var monedas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectMerchandiseCurrencies(ag, monedas);
        CollectServiceCurrencies(ag, monedas);
        CollectRouteCurrencies(ag, routeSheetPayload, monedas);
        return monedas;
    }

    public static bool TryResolveSingleAgreementCurrency(
        TradeAgreementRow ag,
        RouteSheetPayload? routeSheetPayload,
        out string? currency,
        out string? errorMessage)
    {
        currency = null;
        errorMessage = null;

        var monedas = CollectBillableCurrencies(ag, routeSheetPayload);
        if (monedas.Count == 0)
        {
            errorMessage = MissingBillableCurrencyMessage;
            return false;
        }

        if (monedas.Count > 1)
        {
            errorMessage = MultipleAgreementCurrenciesMessage;
            return false;
        }

        currency = monedas.First();
        if (ag.IncludeMerchandise
            && routeSheetPayload is not null
            && !string.IsNullOrWhiteSpace(ag.RouteSheetId))
        {
            var routeErr = ValidateRoutePayloadCurrency(routeSheetPayload, currency);
            if (routeErr is not null)
            {
                errorMessage = routeErr;
                return false;
            }
        }

        return true;
    }

    public static string? ValidateRoutePayloadCurrency(
        RouteSheetPayload payload,
        string requiredCurrency)
    {
        var required = PaymentCheckoutComputation.NormalizeCurrencyFirst(requiredCurrency);
        if (string.IsNullOrEmpty(required))
            return MissingBillableCurrencyMessage;

        foreach (var p in payload.Paradas ?? [])
        {
            var mon = PaymentCheckoutComputation.NormalizeCurrencyFirst(p.MonedaPago)
                      ?? PaymentCheckoutComputation.NormalizeCurrencyFirst(payload.MonedaPago);
            if (string.IsNullOrEmpty(mon))
                return "Indica la moneda de pago en cada tramo.";
            if (!string.Equals(mon, required, StringComparison.OrdinalIgnoreCase))
                return RouteStopCurrencyMismatchMessage;
        }

        return null;
    }

    private static void CollectMerchandiseCurrencies(
        TradeAgreementRow ag,
        ISet<string> monedas)
    {
        if (!ag.IncludeMerchandise)
            return;

        foreach (var m in ag.MerchandiseLines.OrderBy(x => x.SortOrder))
        {
            if (!AgreementUtils.TryParsePositiveDecimal(m.Cantidad, out _))
                continue;
            if (!AgreementUtils.TryParsePositiveDecimal(m.ValorUnitario, out _))
                continue;
            var mon = PaymentCheckoutComputation.NormalizeCurrencyFirst(m.Moneda ?? ag.MerchandiseMeta?.Moneda);
            if (string.IsNullOrEmpty(mon))
                continue;
            monedas.Add(mon);
        }
    }

    private static void CollectServiceCurrencies(
        TradeAgreementRow ag,
        ISet<string> monedas)
    {
        if (!ag.IncludeService)
            return;

        foreach (var svc in ag.ServiceItems.OrderBy(x => x.SortOrder))
        {
            if (!svc.Configured)
                continue;
            foreach (var entry in svc.PaymentEntries.OrderBy(x => x.SortOrder))
            {
                if (!AgreementUtils.TryParsePositiveDecimal(entry.Amount, out _))
                    continue;
                var mon = PaymentCheckoutComputation.NormalizeCurrencyFirst(entry.Moneda);
                if (string.IsNullOrEmpty(mon))
                    continue;
                monedas.Add(mon);
            }
        }
    }

    private static void CollectRouteCurrencies(
        TradeAgreementRow ag,
        RouteSheetPayload? routeSheetPayload,
        ISet<string> monedas)
    {
        var rsId = (ag.RouteSheetId ?? "").Trim();
        if (rsId.Length == 0 || routeSheetPayload?.Paradas is not { Count: > 0 } stops)
            return;

        foreach (var p in stops.OrderBy(x => x.Orden))
        {
            if (!TryParseRouteStopBillableCurrency(p, routeSheetPayload, out var mon))
                continue;
            monedas.Add(mon);
        }
    }

    private static bool TryParseRouteStopBillableCurrency(
        RouteStopPayload p,
        RouteSheetPayload routeSheetPayload,
        out string currencyLower)
    {
        currencyLower = "";
        if (!AgreementUtils.TryParsePositiveDecimal(p.PrecioTransportista ?? "", out _))
            return false;

        var mon = PaymentCheckoutComputation.NormalizeCurrencyFirst(p.MonedaPago)
                  ?? PaymentCheckoutComputation.NormalizeCurrencyFirst(routeSheetPayload.MonedaPago ?? "");
        if (string.IsNullOrEmpty(mon))
            return false;

        currencyLower = mon;
        return true;
    }
}
