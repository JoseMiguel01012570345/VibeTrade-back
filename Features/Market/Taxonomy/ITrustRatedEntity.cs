namespace VibeTrade.Backend.Features.Market.Taxonomy;

/// <summary>Actores o negocios con barra de confianza (flow-ui).</summary>
public interface ITrustRatedEntity
{
    int TrustScore { get; }
}
