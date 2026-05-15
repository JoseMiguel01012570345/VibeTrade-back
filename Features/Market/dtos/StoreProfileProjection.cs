using VibeTrade.Backend.Features.Market.Interfaces;

namespace VibeTrade.Backend.Features.Market.Dtos;

/// <summary>Proyección de dominio (no es entidad EF). Construida desde vistas de tienda u otros mapeos explícitos.</summary>
public sealed class StoreProfileProjection : CommercialActorBase, IStoreProfile
{
    public required IReadOnlyList<string> Categories { get; init; }
    public bool Verified { get; init; }
    public bool TransportIncluded { get; init; }
}
