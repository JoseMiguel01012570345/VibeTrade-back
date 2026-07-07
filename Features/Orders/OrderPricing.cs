using System.Globalization;

namespace VibeTrade.Backend.Features.Orders;

/// <summary>Utilidades de importes y distancia para el checkout de pedidos.</summary>
public static class OrderPricing
{
    /// <summary>Parsea un decimal tolerando coma decimal y espacios (igual criterio que Payments).</summary>
    public static bool TryParseDecimal(string? raw, out decimal value)
    {
        var t = (raw ?? "").Trim().Replace(",", ".", StringComparison.Ordinal).Replace('\u00a0', ' ');
        return decimal.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static string? NormalizeCurrency(string? raw)
    {
        var t = (raw ?? "").Trim().ToUpperInvariant();
        return t.Length is >= 3 and <= 8 ? t[..Math.Min(3, t.Length)] : null;
    }

    /// <summary>Distancia en km entre dos puntos WGS84 (Haversine).</summary>
    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusKm = 6371.0088;
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
                  * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusKm * c;
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
