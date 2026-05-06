namespace VibeTrade.Backend.Features.Chat;

/// <summary>Relación con ofertas: verificar vendedor y sincronizar QA.</summary>
public interface IOfferRelationService
{
    Task<bool> IsUserSellerForOfferAsync(string userId, string offerId, CancellationToken cancellationToken = default);

    Task<string?> GetSellerUserIdForOfferAsync(string offerId, CancellationToken cancellationToken = default);

    Task SyncOfferQaAnswersForOfferAsync(string offerId, CancellationToken cancellationToken = default);
}
