using System.Net;

namespace VibeTrade.Backend.Features.Analytics;

/// <summary>Determina la IP del cliente desde el request (proxy-aware), sin confiar en el cuerpo.</summary>
public static class RequestIpResolver
{
    public static string? Resolve(HttpRequest request)
    {
        var forwarded = request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            // El primer valor es el cliente original; el resto son proxies.
            var first = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (Normalize(first, out var fromHeader))
                return fromHeader;
        }

        var remote = request.HttpContext.Connection.RemoteIpAddress;
        if (remote is not null)
        {
            var ip = remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4().ToString() : remote.ToString();
            if (Normalize(ip, out var normalized))
                return normalized;
        }

        return null;
    }

    private static bool Normalize(string? raw, out string normalized)
    {
        normalized = "";
        var value = (raw ?? "").Trim();
        if (value.Length == 0)
            return false;

        // Quita el puerto opcional de IPv4 ("1.2.3.4:5678").
        if (value.Count(c => c == ':') == 1 && value.Contains('.'))
            value = value.Split(':')[0];

        if (!IPAddress.TryParse(value, out var parsed))
            return false;

        var text = parsed.ToString();
        normalized = text.Length <= 45 ? text : text[..45];
        return normalized.Length > 0;
    }
}
