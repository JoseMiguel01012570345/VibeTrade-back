namespace VibeTrade.Backend.Features.Market;

public sealed class MarketWorkspaceIntegrity : IMarketWorkspaceIntegrity
{
    public void ValidateOrThrow(MarketWorkspaceState root)
    {
        if (root.Stores is null
            || root.Offers is null
            || root.StoreCatalogs is null
            || root.Threads is null
            || root.RouteOfferPublic is null)
        {
            throw new ArgumentException("Workspace missing a required top-level object property.");
        }

        if (root.OfferIds is null)
            throw new ArgumentException("offerIds must be an array.");
    }
}
