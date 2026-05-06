namespace VibeTrade.Backend.Features.Search.Catalog;

internal static class CatalogSearchIds
{
    public static string Store(string storeId) => $"store:{storeId}";

    public static string Product(string productId) => $"product:{productId}";

    public static string Service(string serviceId) => $"service:{serviceId}";

    public static string Emergent(string emergentPublicationId) => $"emergent:{emergentPublicationId}";
}
