namespace VibeTrade.Backend.Features.Chat;

public interface ITradeAgreementService
{
    Task<IReadOnlyList<TradeAgreementApiResponse>> ListForThreadAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default);

    Task<TradeAgreementApiResponse?> CreateAsync(
        string sellerUserId,
        string threadId,
        TradeAgreementDraftRequest draft,
        CancellationToken cancellationToken = default);

    Task<TradeAgreementApiResponse?> UpdateAsync(
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
    Task<TradeAgreementApiResponse?> SetRouteSheetLinkAsync(
        string sellerUserId,
        string threadId,
        string agreementId,
        string? routeSheetId,
        CancellationToken cancellationToken = default);
}
