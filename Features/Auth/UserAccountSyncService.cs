using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Auth.Dtos;
using VibeTrade.Backend.Features.Auth.Interfaces;

namespace VibeTrade.Backend.Features.Auth;

public sealed class UserAccountSyncService(AppDbContext db) : IUserAccountSyncService
{
    public async Task UpsertFromSessionUserAsync(SessionUser user, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(user.Id))
            return;
        var id = user.Id.Trim();
        var now = DateTimeOffset.UtcNow;

        var phoneDisplay = user.Phone;
        var digits = AuthUtils.DigitsOnly(phoneDisplay);

        // Primary key is the session user id. However, our dev auth generates ids in-memory and may change across restarts,
        // while PhoneDigits is stable and unique. To avoid unique constraint violations, fall back to matching by PhoneDigits.
        var row = await db.UserAccounts.FindAsync([id], cancellationToken);
        if (row is null && !string.IsNullOrEmpty(digits))
        {
            row = await db.UserAccounts
                .FirstOrDefaultAsync(x => x.PhoneDigits == digits, cancellationToken);
        }

        if (row is null)
        {
            row = new UserAccount
            {
                Id = id,
                CreatedAt = now,
            };
            db.UserAccounts.Add(row);
        }

        row.DisplayName = user.Name ?? row.DisplayName;
        row.Email = user.Email ?? row.Email;
        row.PhoneDisplay = phoneDisplay ?? row.PhoneDisplay;

        if (!string.IsNullOrEmpty(digits))
        {
            // Only set/update PhoneDigits when it won't collide with another row.
            var inUseByOther = await db.UserAccounts
                .AsNoTracking()
                .AnyAsync(x => x.PhoneDigits == digits && x.Id != row.Id, cancellationToken);
            if (!inUseByOther)
                row.PhoneDigits = digits;
        }

        row.AvatarUrl = user.AvatarUrl ?? row.AvatarUrl;
        row.Instagram = user.Instagram ?? row.Instagram;
        row.Telegram = user.Telegram ?? row.Telegram;
        row.XAccount = user.XAccount ?? row.XAccount;
        if (user.TrustScore is { } ts)
            row.TrustScore = ts;
        row.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetAvatarUrlAsync(string userId, string avatarUrl, CancellationToken cancellationToken = default)
    {
        var row = await db.UserAccounts.FindAsync([userId], cancellationToken);
        if (row is null)
            return;
        row.AvatarUrl = avatarUrl;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task PatchProfileAsync(
        string userId,
        string? displayName,
        string? email,
        string? instagram,
        string? telegram,
        string? xAccount,
        string? avatarUrl,
        string? phoneDigitsForLookup = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var row = await db.UserAccounts.FindAsync([userId], cancellationToken);
        if (row is null && !string.IsNullOrEmpty(phoneDigitsForLookup))
        {
            row = await db.UserAccounts
                .FirstOrDefaultAsync(x => x.PhoneDigits == phoneDigitsForLookup, cancellationToken);
        }

        if (row is null)
        {
            row = new UserAccount
            {
                Id = userId,
                CreatedAt = now,
                DisplayName = displayName ?? "",
            };
            if (email is not null)
                row.Email = string.IsNullOrWhiteSpace(email) ? null : email;
            if (instagram is not null)
                row.Instagram = string.IsNullOrWhiteSpace(instagram) ? null : instagram;
            if (telegram is not null)
                row.Telegram = string.IsNullOrWhiteSpace(telegram) ? null : telegram;
            if (xAccount is not null)
                row.XAccount = string.IsNullOrWhiteSpace(xAccount) ? null : xAccount;
            if (avatarUrl is not null)
                row.AvatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl;
            row.UpdatedAt = now;
            db.UserAccounts.Add(row);
        }
        else
        {
            if (displayName is not null)
                row.DisplayName = displayName;
            if (email is not null)
                row.Email = string.IsNullOrWhiteSpace(email) ? null : email;
            if (instagram is not null)
                row.Instagram = string.IsNullOrWhiteSpace(instagram) ? null : instagram;
            if (telegram is not null)
                row.Telegram = string.IsNullOrWhiteSpace(telegram) ? null : telegram;
            if (xAccount is not null)
                row.XAccount = string.IsNullOrWhiteSpace(xAccount) ? null : xAccount;
            if (avatarUrl is not null)
                row.AvatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserProfileSnapshot?> GetProfileSnapshotAsync(string? phoneDigits = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneDigits))
            return null;
        var digits = AuthUtils.DigitsOnly(phoneDigits);
        if (string.IsNullOrEmpty(digits))
            return null;
        var row = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PhoneDigits == digits, cancellationToken);

        if (row is null)
            return null;
        return ToSnapshot(row);
    }

    public async Task<UserProfileSnapshot?> GetProfileSnapshotByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;
        var row = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (row is null)
            return null;
        return ToSnapshot(row);
    }

    public async Task<bool> PhoneHasRegisteredAccountAsync(
        string? phoneRaw,
        CancellationToken cancellationToken = default)
    {
        var digits = AuthUtils.DigitsOnly(phoneRaw);
        if (string.IsNullOrEmpty(digits))
            return false;
        return await db.UserAccounts.AsNoTracking()
            .AnyAsync(x => x.PhoneDigits == digits, cancellationToken);
    }

    public async Task<string?> GetAvatarUrlAsync(string userId, CancellationToken cancellationToken = default)
    {
        var url = await db.UserAccounts.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.AvatarUrl)
            .FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(url) ? null : url;
    }

    private static UserProfileSnapshot ToSnapshot(UserAccount row) =>
        new(
            row.Id,
            row.DisplayName,
            row.Email,
            row.AvatarUrl,
            row.Instagram,
            row.Telegram,
            row.XAccount,
            row.TrustScore);
}
