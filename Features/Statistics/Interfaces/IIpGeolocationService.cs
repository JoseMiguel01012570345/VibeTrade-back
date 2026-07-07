namespace VibeTrade.Backend.Features.Statistics.Interfaces;

/// <summary>
/// Etiqueta legible para una IP (país/ciudad si hubiera un proveedor; por ahora la propia IP).
/// Se aísla tras interfaz para poder enchufar un proveedor real sin tocar el servicio.
/// </summary>
public interface IIpGeolocationService
{
    string GetDisplayLabel(string ipAddress);
}
