using System.Text.Json;
using System.Text.Json.Nodes;
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
        Console.WriteLine("RequestCode: " + digits + " " + code);
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

        JsonElement userEl;
        var row = await userAccountSync.GetProfileSnapshotAsync(digits, cancellationToken);
        if (row is null)
        {
            userEl = JsonSerializer.SerializeToElement(new
            {
                id = digits,
                phone = digits,
                name = "Usuario sin nombre",
                email = (string?)null,
                avatarUrl = (string?)null,
                instagram = (string?)null,
                telegram = (string?)null,
                xAccount = (string?)null,
            });
        }
        else
        {
            userEl = JsonSerializer.SerializeToElement(new
            {
                id = digits,
                phone = digits,
                name = row.DisplayName,
                email = row.Email,
                avatarUrl = row.AvatarUrl,
                instagram = row.Instagram,
                telegram = row.Telegram,
                xAccount = row.XAccount,
            });
        }

        var token = Guid.NewGuid().ToString("N");
        var sessionNow = DateTimeOffset.UtcNow;
        await db.AuthSessions.AddAsync(
            new AuthSessionRow
            {
                Token = token,
                UserJson = userEl.GetRawText(),
                ExpiresAt = sessionNow.Add(SessionTtl),
                CreatedAt = sessionNow,
            },
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        PruneExpiredSessions();
        PruneExpiredPendingOtps();

        return new VerifyResult(token, userEl);
    }

    public bool TryGetUserByToken(string? bearerToken, out JsonElement user)
    {
        user = default;
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

        using var doc = JsonDocument.Parse(row.UserJson);
        user = doc.RootElement.Clone();
        return true;
    }

    public bool RevokeSession(string? bearerToken)
    {
        var token = ParseBearer(bearerToken);
        if (string.IsNullOrEmpty(token))
            return false;
        return db.AuthSessions.Where(s => s.Token == token).ExecuteDelete() > 0;
    }

    public bool TrySetAvatarUrl(string? bearerToken, string avatarUrl, out JsonElement updatedUser) =>
        TryPatchUserProfile(bearerToken, null, null, null, null, null, avatarUrl, out updatedUser);

    public bool TryPatchUserProfile(
        string? bearerToken,
        string? name,
        string? email,
        string? instagram,
        string? telegram,
        string? xAccount,
        string? avatarUrl,
        out JsonElement updatedUser)
    {
        updatedUser = default;
        var token = ParseBearer(bearerToken);
        if (string.IsNullOrEmpty(token))
            return false;

        var utcNow = DateTimeOffset.UtcNow;
        var row = db.AuthSessions.FirstOrDefault(s => s.Token == token && s.ExpiresAt > utcNow);
        if (row is null)
            return false;

        var root = JsonNode.Parse(row.UserJson)!.AsObject();
        if (name is not null)
            root["name"] = name;
        if (email is not null)
            root["email"] = string.IsNullOrEmpty(email) ? null : email;
        if (instagram is not null)
            root["instagram"] = string.IsNullOrEmpty(instagram) ? null : instagram;
        if (telegram is not null)
            root["telegram"] = string.IsNullOrEmpty(telegram) ? null : telegram;
        if (xAccount is not null)
            root["xAccount"] = string.IsNullOrEmpty(xAccount) ? null : xAccount;
        if (avatarUrl is not null)
            root["avatarUrl"] = string.IsNullOrEmpty(avatarUrl) ? null : avatarUrl;

        using var doc = JsonDocument.Parse(root.ToJsonString());
        updatedUser = doc.RootElement.Clone();
        row.UserJson = updatedUser.GetRawText();
        db.SaveChanges();
        return true;
    }

    public bool TrySyncSessionFromSnapshot(string? bearerToken, UserProfileSnapshot snapshot, out JsonElement updatedUser)
    {
        updatedUser = default;
        var token = ParseBearer(bearerToken);
        if (string.IsNullOrEmpty(token))
            return false;

        var utcNow = DateTimeOffset.UtcNow;
        var row = db.AuthSessions.FirstOrDefault(s => s.Token == token && s.ExpiresAt > utcNow);
        if (row is null)
            return false;

        var root = JsonNode.Parse(row.UserJson)!.AsObject();
        root["name"] = snapshot.DisplayName;
        root["email"] = snapshot.Email is { } e ? e : null;
        root["instagram"] = snapshot.Instagram is { } i ? i : null;
        root["telegram"] = snapshot.Telegram is { } t ? t : null;
        root["xAccount"] = snapshot.XAccount is { } x ? x : null;
        root["avatarUrl"] = snapshot.AvatarUrl is { } a ? a : null;

        using var doc = JsonDocument.Parse(root.ToJsonString());
        updatedUser = doc.RootElement.Clone();
        row.UserJson = updatedUser.GetRawText();
        db.SaveChanges();
        return true;
    }

    public bool TrySetSessionUserId(string? bearerToken, string userId, out JsonElement updatedUser)
    {
        updatedUser = default;
        var token = ParseBearer(bearerToken);
        if (string.IsNullOrEmpty(token))
            return false;

        var utcNow = DateTimeOffset.UtcNow;
        var row = db.AuthSessions.FirstOrDefault(s => s.Token == token && s.ExpiresAt > utcNow);
        if (row is null)
            return false;

        var root = JsonNode.Parse(row.UserJson)!.AsObject();
        root["id"] = userId;
        using var doc = JsonDocument.Parse(root.ToJsonString());
        updatedUser = doc.RootElement.Clone();
        row.UserJson = updatedUser.GetRawText();
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
