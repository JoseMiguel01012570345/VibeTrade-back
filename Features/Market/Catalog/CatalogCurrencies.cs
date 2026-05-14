namespace VibeTrade.Backend.Features.Market.Catalog;

/// <summary>Códigos de moneda para fichas, hojas de ruta y selectores (una sola lista en servidor).</summary>
public static class CatalogCurrencies
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "CUP",
        "USD",
        "EUR",
        "CAD",
        "GBP",
    };
}
