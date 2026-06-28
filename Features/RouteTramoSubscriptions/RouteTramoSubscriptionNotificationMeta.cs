using System.Text.Json;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions;

/// <summary>Meta JSON para notificaciones de aceptación de tramo (sin acoplar al servicio de notificaciones).</summary>
public static class RouteTramoSubscriptionNotificationMeta
{
    private static readonly JsonSerializerOptions AcceptMetaJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string? BuildAcceptMetaJson(
        string routeSheetId,
        string carrierUserId,
        IReadOnlyList<(string StopId, string? StoreServiceId)> stops)
    {
        if (stops.Count == 0)
            return null;
        var rs = (routeSheetId ?? "").Trim();
        var cu = (carrierUserId ?? "").Trim();
        if (rs.Length < 1 || cu.Length < 2)
            return null;
        var stopObjs = stops
            .Select(t => new
            {
                stopId = (t.StopId ?? "").Trim(),
                storeServiceId = string.IsNullOrWhiteSpace(t.StoreServiceId) ? null : t.StoreServiceId.Trim(),
            })
            .Where(x => x.stopId.Length > 0)
            .ToList();
        if (stopObjs.Count == 0)
            return null;
        var payload = new
        {
            routeSheetId = rs,
            carrierUserId = cu,
            stops = stopObjs,
        };
        return JsonSerializer.Serialize(payload, AcceptMetaJsonOptions);
    }
}
