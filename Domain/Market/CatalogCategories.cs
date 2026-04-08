namespace VibeTrade.Backend.Domain.Market;

/// <summary>Categorías permitidas al crear fichas de producto y servicio (alineado al cliente flow-ui).</summary>
public static class CatalogCategories
{
    public static readonly IReadOnlyList<string> ProductAndService = new[]
    {
        "Cosechas",
        "Insumos",
        "Mercancías",
        "Alimentos",
        "B2B",
        "Servicios",
        "Asesoría",
        "Logística",
        "Transportista",
    };
}
