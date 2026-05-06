using VibeTrade.Backend.Data.RouteSheets;

namespace VibeTrade.Backend.Features.Chat.Interfaces;

public interface IRouteSheetChatService
{
    Task<IReadOnlyList<RouteSheetPayload>?> ListForThreadAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Vista de hoja para invitación presel: el transportista autenticado tiene su teléfono en un tramo.
    /// No requiere acceso al hilo de chat.
    /// </summary>
    Task<RouteSheetPayload?> GetPreselPreviewForCarrierAsync(
        string carrierUserId,
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default);

    Task<RouteSheetMutationResult> UpsertAsync(
        string userId,
        string threadId,
        string routeSheetId,
        RouteSheetPayload payload,
        CancellationToken cancellationToken = default);

    Task<RouteSheetMutationResult> DeleteAsync(
        string userId,
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// La hoja está vinculada en BD a un acuerdo con al menos un cobro <c>succeeded</c>
    /// (no debe editarse, borrarse, publicarse ni notificarse como pos-edición).
    /// </summary>
    Task<bool> RouteSheetIsLockedByPaidAgreementAsync(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default);

    /// <summary>Transportista con acuse pending: acepta o rechaza la última edición de la hoja.</summary>
    Task<bool> CarrierRespondToSheetEditAsync(
        string carrierUserId,
        string threadId,
        string routeSheetId,
        bool accept,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Aviso in-app por tramos donde el teléfono cambió al guardar (<paramref name="invites"/>).
    /// Requiere acceso al hilo.
    /// </summary>
    Task<int> NotifyPreselectedTransportistasAsync(
        string editorUserId,
        string threadId,
        string routeSheetId,
        IReadOnlyList<RouteSheetPreselectedInvite> invites,
        CancellationToken cancellationToken = default);
}
