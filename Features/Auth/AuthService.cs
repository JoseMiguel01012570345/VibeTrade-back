using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Auth;

public sealed class AuthService(
    IHostEnvironment hostEnvironment,
    IConfiguration configuration,
    IUserAccountSyncService userAccountSync,
    AppDbContext db) : IAuthService
{
    private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(16);

    public RequestCodeResult RequestCode(string phoneRaw)
    {
        var digits = DigitsOnly(phoneRaw);
        var code = Random.Shared.Next(1_000_000, 9_999_999).ToString();
        Console.WriteLine("\u001b[31mRequestCode: " + digits + " " + code + "\u001b[0m");
   
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(PendingTtl);

        var existing = db.AuthPendingOtps.Find(digits);
        if (existing is not null)
        {
            existing.Code = code;
            existing.CodeLength = code.Length;
            existing.ExpiresAt = expiresAt;
            existing.CreatedAt = now;
        }
        else
        {
            db.AuthPendingOtps.Add(
                new AuthPendingOtpRow
                {
                    PhoneDigits = digits,
                    Code = code,
                    CodeLength = code.Length,
                    ExpiresAt = expiresAt,
                    CreatedAt = now,
                });
        }

        db.SaveChanges();
        PruneExpiredPendingOtps();

        return new RequestCodeResult(code.Length, (int)PendingTtl.TotalSeconds, DevCodeMaybe(code));
    }

    public async Task<VerifyResult?> Verify(string phoneRaw, string code, CancellationToken cancellationToken)
    {
        var digits = DigitsOnly(phoneRaw);
        var pending = await db.AuthPendingOtps
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PhoneDigits == digits, cancellationToken);

        if (pending is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        if (now > pending.ExpiresAt)
        {
            await db.AuthPendingOtps.Where(p => p.PhoneDigits == digits)
                .ExecuteDeleteAsync(cancellationToken);
            return null;
        }

        var normalizedCode = DigitsOnly(code);
        if (normalizedCode != pending.Code)
            return null;

        await db.AuthPendingOtps.Where(p => p.PhoneDigits == digits)
            .ExecuteDeleteAsync(cancellationToken);

        var profile = await userAccountSync.GetProfileSnapshotAsync(digits, cancellationToken);
        SessionUser sessionUser = profile is null
            ? new SessionUser
            {
                Id = digits,
                Phone = digits,
                Name = "Usuario sin nombre",
            }
            : new SessionUser
            {
                Id = digits,
                Phone = digits,
                Name = profile.DisplayName,
                Email = profile.Email,
                AvatarUrl = profile.AvatarUrl,
                Instagram = profile.Instagram,
                Telegram = profile.Telegram,
                XAccount = profile.XAccount,
            };

        var token = Guid.NewGuid().ToString("N");
        var sessionNow = DateTimeOffset.UtcNow;
        await db.AuthSessions.AddAsync(
            new AuthSessionRow
            {
                Token = token,
                User = sessionUser,
                ExpiresAt = sessionNow.Add(SessionTtl),
                CreatedAt = sessionNow,
            },
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        PruneExpiredSessions();
        PruneExpiredPendingOtps();

        return new VerifyResult(token, sessionUser);
    }

    public bool TryGetUserByToken(string? bearerToken, out SessionUser? user)
    {
        user = null;
        var token = ParseBearer(bearerToken);
        if (string.IsNullOrEmpty(token))
            return false;

        var utcNow = DateTimeOffset.UtcNow;
        var row = db.AuthSessions.AsNoTracking()
            .FirstOrDefault(s => s.Token == token && s.ExpiresAt > utcNow);
        if (row is null)
        {
            db.AuthSessions.Where(s => s.Token == token).ExecuteDelete();
            return false;
        }

        user = row.User;
        return true;
    }

    public bool RevokeSession(string? bearerToken)
    {
        var token = ParseBearer(bearerToken);
        if (string.IsNullOrEmpty(token))
            return false;
        return db.AuthSessions.Where(s => s.Token == token).ExecuteDelete() > 0;
    }

    public bool TrySetAvatarUrl(string? bearerToken, string avatarUrl, out SessionUser? updatedUser) =>
        TryPatchUserProfile(bearerToken, null, null, null, null, null, avatarUrl, out updatedUser);

    public bool TryPatchUserProfile(
        string? bearerToken,
        string? name,
        string? email,
        string? instagram,
        string? telegram,
        string? xAccount,
        string? avatarUrl,
        out SessionUser? updatedUser)
    {
        updatedUser = null;
        var token = ParseBearer(bearerToken);
        if (string.IsNullOrEmpty(token))
            return false;

        var utcNow = DateTimeOffset.UtcNow;
        var row = db.AuthSessions.FirstOrDefault(s => s.Token == token && s.ExpiresAt > utcNow);
        if (row is null)
            return false;

        var u = row.User.Clone();
        if (name is not null)
            u.Name = name;
        if (email is not null)
            u.Email = string.IsNullOrEmpty(email) ? null : email;
        if (instagram is not null)
            u.Instagram = string.IsNullOrEmpty(instagram) ? null : instagram;
        if (telegram is not null)
            u.Telegram = string.IsNullOrEmpty(telegram) ? null : telegram;
        if (xAccount is not null)
            u.XAccount = string.IsNullOrEmpty(xAccount) ? null : xAccount;
        if (avatarUrl is not null)
            u.AvatarUrl = string.IsNullOrEmpty(avatarUrl) ? null : avatarUrl;

        row.User = u;
        updatedUser = u;
        db.SaveChanges();
        return true;
    }

    public bool TrySyncSessionFromSnapshot(string? bearerToken, UserProfileSnapshot snapshot, out SessionUser? updatedUser)
    {
        updatedUser = null;
        var token = ParseBearer(bearerToken);
        if (string.IsNullOrEmpty(token))
            return false;

        var utcNow = DateTimeOffset.UtcNow;
        var row = db.AuthSessions.FirstOrDefault(s => s.Token == token && s.ExpiresAt > utcNow);
        if (row is null)
            return false;

        var u = row.User.Clone();
        u.Name = snapshot.DisplayName;
        u.Email = snapshot.Email;
        u.Instagram = snapshot.Instagram;
        u.Telegram = snapshot.Telegram;
        u.XAccount = snapshot.XAccount;
        u.AvatarUrl = snapshot.AvatarUrl;
        u.TrustScore = snapshot.TrustScore;
        row.User = u;
        updatedUser = u;
        db.SaveChanges();
        return true;
    }

    public bool TrySetSessionUserId(string? bearerToken, string userId, out SessionUser? updatedUser)
    {
        updatedUser = null;
        var token = ParseBearer(bearerToken);
        if (string.IsNullOrEmpty(token))
            return false;

        var utcNow = DateTimeOffset.UtcNow;
        var row = db.AuthSessions.FirstOrDefault(s => s.Token == token && s.ExpiresAt > utcNow);
        if (row is null)
            return false;

        var u = row.User.Clone();
        u.Id = userId;
        row.User = u;
        updatedUser = u;
        db.SaveChanges();
        return true;
    }

    private string? DevCodeMaybe(string code)
    {
        var expose = configuration.GetValue("Auth:ExposeDevCodes", hostEnvironment.IsDevelopment());
        return expose ? code : null;
    }

    private void PruneExpiredSessions()
    {
        var now = DateTimeOffset.UtcNow;
        db.AuthSessions.Where(s => s.ExpiresAt < now).ExecuteDelete();
    }

    private void PruneExpiredPendingOtps()
    {
        var now = DateTimeOffset.UtcNow;
        db.AuthPendingOtps.Where(p => p.ExpiresAt < now).ExecuteDelete();
    }

    private static string DigitsOnly(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";
        return string.Concat(raw.Where(char.IsDigit));
    }

    private static string? ParseBearer(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization))
            return null;
        const string p = "Bearer ";
        if (!authorization.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            return null;
        return authorization[p.Length..].Trim();
    }
}
