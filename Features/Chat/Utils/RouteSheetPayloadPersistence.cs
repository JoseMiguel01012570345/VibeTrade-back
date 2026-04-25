using System.Text.Json;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;

namespace VibeTrade.Backend.Features.Chat.Utils;

internal static class RouteSheetPayloadPersistence
{
    public static RouteSheetPayload ClonePayloadForEfUpdate(RouteSheetPayload payload) =>
        JsonSerializer.Deserialize<RouteSheetPayload>(
            JsonSerializer.Serialize(payload, RouteSheetJson.Options),
            RouteSheetJson.Options) ?? payload;

    public static void ApplyPayloadAndTouch(ChatRouteSheetRow row, RouteSheetPayload mutatingPayload, DateTimeOffset now)
    {
        row.Payload = ClonePayloadForEfUpdate(mutatingPayload);
        row.UpdatedAtUtc = now;
    }
}
