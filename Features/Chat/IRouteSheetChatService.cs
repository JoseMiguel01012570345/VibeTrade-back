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

    /// <summary>
    /// Aviso in-app a cuentas registradas cuyo teléfono figura en <paramref name="rawPhones"/> (p. ej. tramos de la hoja).
    /// Requiere acceso al hilo.
    /// </summary>
    Task<int> NotifyPreselectedTransportistasAsync(
        string editorUserId,
        string threadId,
        string routeSheetId,
        IReadOnlyList<string> rawPhones,
        CancellationToken cancellationToken = default);
}
