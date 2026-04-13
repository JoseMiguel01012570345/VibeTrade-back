namespace VibeTrade.Backend.Features.Search;

internal static class StoreSearchWildcard
{
    /// <summary>Escapa * ? \ para consultas wildcard de Elasticsearch.</summary>
    public static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("?", "\\?", StringComparison.Ordinal);
    }
}
