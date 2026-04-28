namespace VibeTrade.Backend.Features.Market.Utils;

internal static class MarketWebsiteUrlNormalizer
{
    private const int MaxLen = 2048;

    /// <summary>Devuelve URL http(s) válida o null.</summary>
    public static string? TryNormalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var t = raw.Trim();
        if (t.Length > MaxLen)
            return null;

        if (!t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            t = "https://" + t;

        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;
        if (string.IsNullOrEmpty(uri.Host))
            return null;

        return uri.AbsoluteUri;
    }
}
