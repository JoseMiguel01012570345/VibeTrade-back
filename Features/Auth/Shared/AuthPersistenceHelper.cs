using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Auth.Dtos;

namespace VibeTrade.Backend.Features.Auth.Shared;

internal static class AuthPersistenceHelper
{
    internal static readonly TimeSpan OtpPendingTtl = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan CredentialsPendingTtl = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan SessionTtl = TimeSpan.FromHours(16);
    internal const int RegistrationCodeLength = 7;

    internal static string GenerateCode() =>
        Random.Shared.Next(1_000_000, 9_999_999).ToString();

    internal static string? DevCodeMaybe(IConfiguration configuration, IHostEnvironment hostEnvironment, string code)
    {
        var expose = configuration.GetValue("Auth:ExposeDevCodes", hostEnvironment.IsDevelopment());
        return expose ? code : null;
    }

    internal static UserProfileSnapshot ToSnapshot(UserAccount row) =>
        new(
            row.Id,
            row.DisplayName,
            row.Username,
            row.Email,
            row.PhoneDisplay,
            row.PhoneDigits,
            row.AvatarUrl,
            row.Instagram,
            row.Telegram,
            row.XAccount,
            row.TrustScore);

    internal static async Task<UserProfileSnapshot?> GetProfileSnapshotAsync(
        AppDbContext db,
        string? phoneDigits,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneDigits))
            return null;
        var digits = AuthUtils.DigitsOnly(phoneDigits);
        if (string.IsNullOrEmpty(digits))
            return null;
        var row = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PhoneDigits == digits, cancellationToken);
        return row is null ? null : ToSnapshot(row);
    }

    internal static async Task<string> CreateSessionAsync(
        AppDbContext db,
        SessionUser sessionUser,
        CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        await db.AuthSessions.AddAsync(
            new AuthSessionRow
            {
                Token = token,
                User = sessionUser,
                ExpiresAt = now.Add(SessionTtl),
                CreatedAt = now,
            },
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        PruneExpiredSessions(db);
        return token;
    }

    internal static async Task UpsertPhoneOtpAsync(
        AppDbContext db,
        string digits,
        string code,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var expiresAt = now.Add(CredentialsPendingTtl);
        var existing = await db.AuthPendingOtps.FindAsync([digits], cancellationToken);
        if (existing is not null)
        {
            existing.Code = code;
            existing.CodeLength = RegistrationCodeLength;
            existing.ExpiresAt = expiresAt;
            existing.CreatedAt = now;
        }
        else
        {
            db.AuthPendingOtps.Add(new AuthPendingOtpRow
            {
                PhoneDigits = digits,
                Code = code,
                CodeLength = RegistrationCodeLength,
                ExpiresAt = expiresAt,
                CreatedAt = now,
            });
        }
    }

    internal static void PruneExpiredSessions(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.AuthSessions.Where(s => s.ExpiresAt < now).ExecuteDelete();
    }

    internal static void PruneExpiredPendingOtps(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.AuthPendingOtps.Where(p => p.ExpiresAt < now).ExecuteDelete();
    }

    internal static void PruneExpiredCredentialsPending(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.AuthPendingRegistrations.Where(r => r.ExpiresAt < now).ExecuteDelete();
        db.AuthPendingEmailOtps.Where(e => e.ExpiresAt < now).ExecuteDelete();
        db.AuthPendingPasswordResets.Where(p => p.ExpiresAt < now).ExecuteDelete();
        db.AuthPendingOtps.Where(p => p.ExpiresAt < now).ExecuteDelete();
    }
}
