using VibeTrade.Backend.Data.RouteSheets;

namespace VibeTrade.Backend.Features.Chat;

public interface IRouteSheetChatService
{
    Task<IReadOnlyList<RouteSheetPayload>?> ListForThreadAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default);

    Task<bool> UpsertAsync(
        string userId,
        string threadId,
        string routeSheetId,
        RouteSheetPayload payload,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string userId, string threadId, string routeSheetId, CancellationToken cancellationToken = default);

    /// <summary>Transportista con acuse pending: acepta o rechaza la última edición de la hoja.</summary>
    Task<bool> CarrierRespondToSheetEditAsync(
        string carrierUserId,
        string threadId,
        string routeSheetId,
        bool accept,
        CancellationToken cancellationToken = default);
}
