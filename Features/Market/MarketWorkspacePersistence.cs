using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Market;

/// <summary>Persistencia del workspace de mercado (extraída de <see cref="MarketService"/> para evitar acoplamiento entre features).</summary>
public static class MarketWorkspacePersistence
{
    public static async Task<MarketWorkspaceState?> GetPersistedWorkspaceAsync(
        AppDbContext context,
        CancellationToken cancellationToken = default)
    {
        var row = await context.MarketWorkspaces.AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        return row.State;
    }

    public static void ValidateWorkspaceForPersist(MarketWorkspaceState root)
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

    public static async Task SavePersistedWorkspaceAsync(
        AppDbContext context,
        MarketWorkspaceState document,
        CancellationToken cancellationToken = default)
    {
        var row = await context.MarketWorkspaces.FirstOrDefaultAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            context.MarketWorkspaces.Add(new MarketWorkspaceRow
            {
                State = document,
                UpdatedAt = now,
            });
        }
        else
        {
            row.State = document;
            row.UpdatedAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
