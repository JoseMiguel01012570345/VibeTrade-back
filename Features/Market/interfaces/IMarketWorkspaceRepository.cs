namespace VibeTrade.Backend.Features.Market.Interfaces;

public interface IMarketWorkspaceRepository
{
    Task<MarketWorkspaceState?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(MarketWorkspaceState document, CancellationToken cancellationToken = default);
}
