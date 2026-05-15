namespace VibeTrade.Backend.Features.Market.Interfaces;

/// <summary>Actores o negocios con barra de confianza (flow-ui).</summary>
public interface ITrustRatedEntity
{
    int TrustScore { get; }
}
