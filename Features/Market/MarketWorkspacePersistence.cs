using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Market;

/// <summary>
/// Persistencia del workspace de mercado y mapeos de filas de tienda al documento persistido.
/// </summary>
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

    public static StoreProfileWorkspaceData MinimalStub(string storeId) =>
        new()
        {
            Id = storeId,
            Name = "Tienda",
            Verified = false,
            TransportIncluded = false,
            TrustScore = 0,
            OwnerUserId = "",
            Categories = Array.Empty<string>(),
        };

    public static StoreProfileWorkspaceData FromStoreRow(StoreRow s) =>
        new()
        {
            Id = s.Id,
            Name = s.Name,
            OwnerUserId = s.OwnerUserId,
            Verified = s.Verified,
            TransportIncluded = s.TransportIncluded,
            TrustScore = s.TrustScore,
            AvatarUrl = string.IsNullOrEmpty(s.AvatarUrl) ? null : s.AvatarUrl,
            Categories = CatalogJsonColumnParsing.StringListOrEmpty(s.Categories).ToList(),
            Pitch = string.IsNullOrWhiteSpace(s.Pitch) ? null : s.Pitch.Trim(),
            WebsiteUrl = string.IsNullOrWhiteSpace(s.WebsiteUrl) ? null : s.WebsiteUrl.Trim(),
            PricePerKm = s.PricePerKm,
            PricePerKmCurrencyCode = string.IsNullOrWhiteSpace(s.PricePerKmCurrencyCode) ? null : s.PricePerKmCurrencyCode,
            Location = s.LocationLatitude is { } la && s.LocationLongitude is { } lo
                ? new StoreLocationPointBody { Lat = la, Lng = lo }
                : null,
        };
}
