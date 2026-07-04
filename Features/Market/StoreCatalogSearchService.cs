using System.Text;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Market.Dtos;
using VibeTrade.Backend.Features.Market.Entities;
using VibeTrade.Backend.Features.Market.Interfaces;
using VibeTrade.Backend.Features.Offers.Interfaces;

namespace VibeTrade.Backend.Features.Market;

public sealed class StoreCatalogSearchService(AppDbContext db, IOfferService offerService)
    : IStoreCatalogSearchService
{
    private const int MaxResultsPerKind = 200;

    public async Task<StoreCatalogSearchResponse?> SearchPublishedCatalogAsync(
        string storeId,
        string? query,
        CancellationToken cancellationToken = default)
    {
        var sid = (storeId ?? "").Trim();
        if (sid.Length < 2)
            return null;

        var storeExists = await db.Stores.AsNoTracking()
            .AnyAsync(s => s.Id == sid && s.DeletedAtUtc == null, cancellationToken)
            .ConfigureAwait(false);
        if (!storeExists)
            return null;

        var q = (query ?? "").Trim();
        if (q.Length > 80)
            q = q[..80];

        if (q.Length < 1)
        {
            return new StoreCatalogSearchResponse();
        }

        var productRows = await SearchProductsAsync(sid, q, cancellationToken).ConfigureAwait(false);
        var serviceRows = await SearchServicesAsync(sid, q, cancellationToken).ConfigureAwait(false);

        return new StoreCatalogSearchResponse
        {
            Products = productRows.Select(offerService.ProductCatalogRowFromEntity).ToList(),
            Services = serviceRows.Select(offerService.ServiceCatalogRowFromEntity).ToList(),
        };
    }

    private async Task<List<StoreProductRow>> SearchProductsAsync(
        string storeId,
        string query,
        CancellationToken cancellationToken)
    {
        var ids = await CollectMatchingIdsAsync(
            storeId,
            query,
            db.StoreProducts.AsNoTracking()
                .Where(p => p.StoreId == storeId && p.Published && p.DeletedAtUtc == null),
            (pat) => (StoreProductRow p) =>
                EF.Functions.ILike(p.Name, pat) ||
                EF.Functions.ILike(p.ShortDescription, pat) ||
                EF.Functions.ILike(p.Category, pat) ||
                (p.Model != null && EF.Functions.ILike(p.Model, pat)),
            p => p.Id,
            p => p.Name,
            cancellationToken).ConfigureAwait(false);

        if (ids.Count == 0)
            return [];

        return await db.StoreProducts.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<List<StoreServiceRow>> SearchServicesAsync(
        string storeId,
        string query,
        CancellationToken cancellationToken)
    {
        var ids = await CollectMatchingIdsAsync(
            storeId,
            query,
            db.StoreServices.AsNoTracking()
                .Where(s => s.StoreId == storeId
                    && (s.Published == null || s.Published == true)
                    && s.DeletedAtUtc == null),
            (pat) => (StoreServiceRow s) =>
                EF.Functions.ILike(s.NombreServicio, pat) ||
                EF.Functions.ILike(s.Descripcion, pat) ||
                EF.Functions.ILike(s.Category, pat),
            s => s.Id,
            s => s.NombreServicio,
            cancellationToken).ConfigureAwait(false);

        if (ids.Count == 0)
            return [];

        return await db.StoreServices.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .OrderBy(s => s.NombreServicio)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<HashSet<string>> CollectMatchingIdsAsync<T>(
        string storeId,
        string query,
        IQueryable<T> baseQuery,
        Func<string, System.Linq.Expressions.Expression<Func<T, bool>>> matchPattern,
        System.Linq.Expressions.Expression<Func<T, string>> idSelector,
        System.Linq.Expressions.Expression<Func<T, string>> orderSelector,
        CancellationToken cancellationToken)
    {
        _ = storeId;
        var patterns = BuildIlikePatterns(query);
        var likePrefix = IlikePrefix(query);
        var allPatterns = new List<string>(patterns.Count + 1) { likePrefix };
        allPatterns.AddRange(patterns);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pat in allPatterns.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ids.Count >= MaxResultsPerKind)
                break;

            var remaining = MaxResultsPerKind - ids.Count;
            var batch = await baseQuery
                .Where(matchPattern(pat))
                .OrderBy(orderSelector)
                .Select(idSelector)
                .Take(remaining)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var id in batch)
            {
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id);
            }
        }

        return ids;
    }

    private static string IlikePrefix(string trimmedQuery)
    {
        var sb = new StringBuilder(trimmedQuery.Length + 1);
        foreach (var c in trimmedQuery)
        {
            if (c is '%' or '_') continue;
            sb.Append(c);
        }

        var core = sb.ToString();
        return core.Length == 0 ? "%" : $"{core}%";
    }

    private static IReadOnlyList<string> BuildIlikePatterns(string q)
    {
        q = (q ?? "").Trim();
        if (q.Length == 0)
            return Array.Empty<string>();
        if (q.Length > 80)
            q = q[..80];

        var patterns = new List<string>(12);
        if (q.Length is >= 3 and <= 10)
        {
            for (var i = 0; i < q.Length && patterns.Count < 9; i++)
            {
                var one = q.ToCharArray();
                one[i] = '_';
                patterns.Add($"%{new string(one)}%");
            }
        }

        var escaped = new StringBuilder(q.Length);
        foreach (var c in q)
        {
            if (c is '%' or '_') continue;
            escaped.Append(c);
        }

        var core = escaped.ToString();
        if (core.Length > 0)
            patterns.Add($"%{core}%");

        return patterns.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
