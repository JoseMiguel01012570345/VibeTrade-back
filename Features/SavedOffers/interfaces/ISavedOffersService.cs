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
