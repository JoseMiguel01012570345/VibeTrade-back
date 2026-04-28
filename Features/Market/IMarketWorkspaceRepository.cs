namespace VibeTrade.Backend.Features.Market;

public interface IMarketWorkspaceRepository
{
    Task<MarketWorkspaceState?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(MarketWorkspaceState document, CancellationToken cancellationToken = default);
}
