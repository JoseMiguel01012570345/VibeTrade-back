using VibeTrade.Backend.Features.Trust.Dtos;

namespace VibeTrade.Backend.Features.Trust.Interfaces;

/// <summary>
/// Gestiona el gate de confianza (wiki cap. 08/10): consulta el estado de la barra y procesa el
/// pago de la mensualidad que rehabilita las interacciones al cruzar el umbral hacia arriba.
/// </summary>
public interface IMensualidadService
{
    /// <summary>Estado actual de la barra de confianza del usuario.</summary>
    Task<TrustStatusDto?> GetStatusAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Procesa el pago de la mensualidad. Si el usuario está bajo umbral, restaura su puntaje al
    /// umbral y rehabilita las interacciones; en caso contrario devuelve el estado sin cambios.
    /// </summary>
    Task<MensualidadPayResponse?> PayAsync(
        string userId,
        MensualidadPayRequest request,
        CancellationToken cancellationToken = default);
}
