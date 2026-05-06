namespace VibeTrade.Backend.Features.Chat.Interfaces;

public interface ITradeAgreementService
{
    Task<IReadOnlyList<TradeAgreementApiResponse>> ListForThreadAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default);

    /// <returns>Acuerdo creado; (<c>null</c>, error en <see cref="TradeAgreementWriteErrors.DuplicateAgreementTitle" />) si el título ya existe en el hilo; ambos null si falla validación o permiso.</returns>
    Task<(TradeAgreementApiResponse? Agreement, string? ErrorCode)> CreateAsync(
        string sellerUserId,
        string threadId,
        TradeAgreementDraftRequest draft,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="CreateAsync"/>
    Task<(TradeAgreementApiResponse? Agreement, string? ErrorCode)> UpdateAsync(
        string sellerUserId,
        string threadId,
        string agreementId,
        TradeAgreementDraftRequest draft,
        CancellationToken cancellationToken = default);

    Task<TradeAgreementApiResponse?> RespondAsync(
        string buyerUserId,
        string threadId,
        string agreementId,
        bool accept,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string sellerUserId,
        string threadId,
        string agreementId,
        CancellationToken cancellationToken = default);

    /// <summary>Vincula o desvincula la hoja de ruta del hilo; persiste <see cref="TradeAgreementRow.RouteSheetId" />.</summary>
    Task<TradeAgreementRouteSheetLinkOutcome> SetRouteSheetLinkAsync(
        string sellerUserId,
        string threadId,
        string agreementId,
        string? routeSheetId,
        CancellationToken cancellationToken = default);
}
