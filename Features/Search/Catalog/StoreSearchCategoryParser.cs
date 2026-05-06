using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Features.Search.Catalog;

internal static class StoreSearchCategoryParser
{
    public static IReadOnlyList<string> ParseCategories(IReadOnlyList<string>? categories) =>
        CatalogJsonColumnParsing
            .StringListOrEmpty(categories)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
}
