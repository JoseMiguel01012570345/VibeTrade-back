using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Market;

public sealed class MarketWorkspaceRepository(AppDbContext db) : IMarketWorkspaceRepository
{
    public async Task<JsonDocument?> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await db.MarketWorkspaces.AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        return JsonDocument.Parse(row.Payload);
    }

    public async Task SaveAsync(JsonDocument document, CancellationToken cancellationToken = default)
    {
        var json = document.RootElement.GetRawText();
        var row = await db.MarketWorkspaces.FirstOrDefaultAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            db.MarketWorkspaces.Add(new MarketWorkspaceRow
            {
                Payload = json,
                UpdatedAt = now,
            });
        }
        else
        {
            row.Payload = json;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
