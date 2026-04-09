namespace VibeTrade.Backend.Domain.Market;

/// <summary>Códigos de moneda para fichas de producto y servicio (selectores en flow-ui).</summary>
public static class CatalogCurrencies
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "CUP",
        "USD",
        "EUR",
    };
}
