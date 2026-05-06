namespace VibeTrade.Backend.Features.SavedOffers.Interfaces;

public enum SavedOfferMutationError
{
    None,
    /// <summary>No existe un producto o servicio con ese id en el catálogo relacional.</summary>
    NotFound,
    /// <summary>La oferta pertenece a una tienda del propio usuario.</summary>
    OwnProduct,
    /// <summary>No hay fila de cuenta para el id de sesión.</summary>
    UserNotFound,
}

public interface ISavedOffersService
{
    /// <summary>Ids guardados que siguen siendo válidos y no son del propio usuario (para bootstrap).</summary>
    Task<IReadOnlyList<string>> GetFilteredForBootstrapAsync(string viewerUserId, CancellationToken cancellationToken = default);

    Task<(SavedOfferMutationError Error, IReadOnlyList<string> SavedOfferIds)> TryAddAsync(
        string userId,
        string productId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>?> TryRemoveAsync(
        string userId,
        string productId,
        CancellationToken cancellationToken = default);
}
