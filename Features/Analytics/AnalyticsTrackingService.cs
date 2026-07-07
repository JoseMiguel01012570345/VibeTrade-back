using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Analytics.Dtos;
using VibeTrade.Backend.Features.Analytics.Interfaces;

namespace VibeTrade.Backend.Features.Analytics;

public sealed class AnalyticsTrackingService(AppDbContext db) : IAnalyticsTrackingService
{
    private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> RateLimits = new();
    private const int MaxEventsPerMinute = 120;
    private const int SessionKeyMaxLen = 64;
    private const int PathMaxLen = 512;

    public async Task RecordPageViewAsync(
        PageViewRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return;
        if (!TryValidateSessionKey(request.SessionKey, out var sessionKey))
            return;
        if (!TryValidatePath(request.Path, out var path))
            return;
        if (!AllowRate(ipAddress, sessionKey))
            return;

        var now = DateTimeOffset.UtcNow;
        var ip = TruncateIp(ipAddress);
        var ua = Truncate(userAgent, 512);

        await UpsertSessionAsync(sessionKey, ip, ua, now, cancellationToken).ConfigureAwait(false);

        db.AnalyticsPageViews.Add(new AnalyticsPageViewRow
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionKey = sessionKey,
            IpAddress = ip,
            Path = path,
            ViewedAt = now,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordProductViewAsync(
        ProductViewRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return;
        if (!TryValidateSessionKey(request.SessionKey, out var sessionKey))
            return;
        var productId = (request.ProductId ?? "").Trim();
        if (productId.Length == 0 || productId.Length > 64)
            return;
        if (!AllowRate(ipAddress, sessionKey))
            return;

        var productExists = await db.StoreProducts.AsNoTracking()
            .AnyAsync(p => p.Id == productId, cancellationToken)
            .ConfigureAwait(false);
        if (!productExists)
            return;

        var now = DateTimeOffset.UtcNow;
        var ip = TruncateIp(ipAddress);

        db.ProductViewEvents.Add(new ProductViewEventRow
        {
            Id = Guid.NewGuid().ToString("N"),
            ProductId = productId,
            SessionKey = sessionKey,
            IpAddress = ip,
            ViewedAt = now,
        });

        await UpsertSessionAsync(sessionKey, ip, null, now, cancellationToken).ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task UpsertSessionAsync(
        string sessionKey,
        string ip,
        string? userAgent,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var session = await db.AnalyticsSessions
            .FirstOrDefaultAsync(s => s.SessionKey == sessionKey, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            db.AnalyticsSessions.Add(new AnalyticsSessionRow
            {
                Id = Guid.NewGuid().ToString("N"),
                SessionKey = sessionKey,
                IpAddress = ip,
                UserAgent = userAgent,
                FirstSeenAt = now,
                LastSeenAt = now,
            });
        }
        else
        {
            session.LastSeenAt = now;
            session.IpAddress = ip;
            if (userAgent is not null)
                session.UserAgent = userAgent;
        }
    }

    private static bool TryValidateSessionKey(string? raw, out string sessionKey)
    {
        sessionKey = (raw ?? "").Trim();
        return sessionKey.Length is > 0 and <= SessionKeyMaxLen;
    }

    private static bool TryValidatePath(string? raw, out string path)
    {
        path = (raw ?? "").Trim();
        if (path.Length == 0 || path.Length > PathMaxLen)
            return false;
        if (!path.StartsWith('/'))
            path = "/" + path;
        return true;
    }

    private static bool AllowRate(string ipAddress, string sessionKey)
    {
        var key = $"{ipAddress}|{sessionKey}";
        var now = DateTime.UtcNow;
        var entry = RateLimits.AddOrUpdate(
            key,
            _ => (1, now),
            (_, prev) => (now - prev.WindowStart).TotalMinutes >= 1
                ? (1, now)
                : (prev.Count + 1, prev.WindowStart));
        return entry.Count <= MaxEventsPerMinute;
    }

    private static string TruncateIp(string ip) => ip.Length <= 45 ? ip : ip[..45];

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
