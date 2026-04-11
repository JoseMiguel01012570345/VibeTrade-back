using System.Text.RegularExpressions;

namespace VibeTrade.Backend.Features.Market.Utils;

internal static class MarketStoreNameNormalizer
{
    /// <summary>Misma regla que <c>normStoreName</c> en el cliente.</summary>
    public static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var collapsed = Regex.Replace(name.Trim(), @"\s+", " ");
        if (collapsed.Length == 0)
            return null;
        return collapsed.ToLowerInvariant();
    }
}
