namespace VibeTrade.Backend.Features.EmergentOffers;

/// <summary>
/// Reglas de negocio para que un usuario pueda suscribirse como transportista a una publicación <c>emo_*</c>.
/// </summary>
public interface IEmergentOfferCarrierSubscriptionService
{
    /// <summary>
    /// Indica si el usuario puede suscribirse como transportista a esta publicación emergente.
    /// </summary>
    /// <param name="viewerUserId">Usuario autenticado; null si no hay sesión.</param>
    /// <param name="emergentOfferId">Id <c>emo_*</c>.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<EmergentCarrierSubscriptionStatus> GetStatusAsync(
        string? viewerUserId,
        string emergentOfferId,
        CancellationToken cancellationToken = default);
}

/// <summary>Resultado de <see cref="IEmergentOfferCarrierSubscriptionService.GetStatusAsync"/>.</summary>
public readonly record struct EmergentCarrierSubscriptionStatus(
    bool CanSubscribe,
    string? ReasonCode,
    string? Message);
