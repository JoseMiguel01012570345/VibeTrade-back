using VibeTrade.Backend.Features.Statistics.Interfaces;

namespace VibeTrade.Backend.Features.Statistics;

/// <summary>Implementación mínima: la etiqueta es la propia IP (sin dependencia de terceros).</summary>
public sealed class IpGeolocationService : IIpGeolocationService
{
    public string GetDisplayLabel(string ipAddress)
    {
        var ip = (ipAddress ?? "").Trim();
        return ip.Length == 0 ? "Desconocido" : ip;
    }
}
