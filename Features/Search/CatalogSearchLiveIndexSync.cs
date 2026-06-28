using VibeTrade.Backend.Features.Search.Interfaces;

namespace VibeTrade.Backend.Features.Search;

/// <summary>
/// Indexación "on the fly": delega en <see cref="IStoreSearchIndexWriter"/> tras mutaciones de catálogo.
/// </summary>
public sealed class CatalogSearchLiveIndexSync(IStoreSearchIndexWriter writer) : ICatalogSearchLiveIndexSync
{
    public async Task SyncStoreAsync(string storeId, CancellationToken cancellationToken = default)
    {
        var sid = (storeId ?? "").Trim();
        if (sid.Length < 2)
            return;
        await writer.EnsureIndexAsync(cancellationToken);
        await writer.UpsertStoresAsync([sid], cancellationToken);
    }

    public async Task SyncStoresAsync(
        IReadOnlyCollection<string> storeIds,
        CancellationToken cancellationToken = default)
    {
        if (storeIds.Count == 0)
            return;

        var ids = storeIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Where(id => id.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0)
            return;

        await writer.EnsureIndexAsync(cancellationToken);
        await writer.UpsertStoresAsync(ids, cancellationToken);
    }
}
