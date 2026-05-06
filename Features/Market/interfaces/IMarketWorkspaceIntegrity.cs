namespace VibeTrade.Backend.Features.Market.Interfaces;

public interface IMarketWorkspaceIntegrity
{
    /// <summary>Valida forma mínima del workspace antes de persistir.</summary>
    void ValidateOrThrow(MarketWorkspaceState document);
}
