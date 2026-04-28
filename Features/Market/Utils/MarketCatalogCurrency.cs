using System.Collections.Generic;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Features.Market.Utils;

internal static class MarketCatalogCurrency
{
    public static void ThrowIfProductCurrencyInvalid(StoreProductPutRequest p, string id)
    {
        if (string.IsNullOrWhiteSpace(p.MonedaPrecio))
            throw new CatalogCurrencyValidationException(
                $"Producto \"{id}\": la moneda del precio es obligatoria.");
        if (!CatalogItemHasAtLeastOneAcceptedMoneda(p))
            throw new CatalogCurrencyValidationException(
                $"Producto \"{id}\": indicá al menos una moneda aceptada para el pago.");
    }

    public static void ThrowIfServiceCurrencyInvalid(StoreServicePutRequest s, string id)
    {
        if (!CatalogItemHasAtLeastOneAcceptedMoneda(s))
            throw new CatalogCurrencyValidationException(
                $"Servicio \"{id}\": indicá al menos una moneda aceptada para el pago.");
    }

    public static List<string> BuildMonedasList(StoreProductPutRequest p) =>
        BuildMonedasList(p.Monedas, p.Moneda);

    public static List<string> BuildMonedasList(StoreServicePutRequest s) =>
        BuildMonedasList(s.Monedas, s.Moneda);

    public static List<string> BuildMonedasList(IReadOnlyList<string>? monedas, string? moneda)
    {
        if (monedas is { Count: > 0 })
        {
            var withAny = new List<string>(monedas.Count);
            foreach (var c in monedas)
            {
                if (!string.IsNullOrWhiteSpace(c))
                    withAny.Add(c.Trim());
            }

            if (withAny.Count > 0)
                return withAny;
        }

        if (!string.IsNullOrWhiteSpace(moneda))
            return new List<string> { moneda.Trim() };

        return new List<string>();
    }

    public static bool CatalogItemHasAtLeastOneAcceptedMoneda(StoreProductPutRequest p) =>
        CatalogItemHasAtLeastOneAcceptedMoneda(p.Monedas, p.Moneda);

    public static bool CatalogItemHasAtLeastOneAcceptedMoneda(StoreServicePutRequest s) =>
        CatalogItemHasAtLeastOneAcceptedMoneda(s.Monedas, s.Moneda);

    private static bool CatalogItemHasAtLeastOneAcceptedMoneda(IReadOnlyList<string>? monedas, string? moneda)
    {
        if (monedas is { Count: > 0 })
        {
            foreach (var c in monedas)
            {
                if (!string.IsNullOrWhiteSpace(c))
                    return true;
            }
        }

        return !string.IsNullOrWhiteSpace(moneda);
    }
}
