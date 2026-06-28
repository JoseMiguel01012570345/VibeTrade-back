using MediatR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Auth.Dtos;
using VibeTrade.Backend.Features.Auth.Interfaces;
using VibeTrade.Backend.Features.Auth.Login;
using VibeTrade.Backend.Features.Auth.Register;
using VibeTrade.Backend.Features.Auth.SendOtp;
using VibeTrade.Backend.Features.Auth.Shared;
using VibeTrade.Backend.Features.Auth.VerifyOtp;

namespace VibeTrade.Backend.Features.Auth;

public sealed class AuthService(
    IMediator mediator,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration,
    AppDbContext db) : IAuthService
{

    public static IReadOnlyList<SignInCountryDto> SignInCountries { get; } =
    [
        new("Cuba", "CU", "+53", "🇨🇺"),
        new("Argentina", "AR", "+54", "🇦🇷"),
        new("Colombia", "CO", "+57", "🇨🇴"),
        new("España", "ES", "+34", "🇪🇸"),
        new("México", "MX", "+52", "🇲🇽"),
        new("Chile", "CL", "+56", "🇨🇱"),
        new("Perú", "PE", "+51", "🇵🇪"),
        new("Estados Unidos", "US", "+1", "🇺🇸"),
    ];

    public RequestCodeResult RequestCode(string phoneRaw) =>
        mediator.Send(new SendOtpCommand(phoneRaw)).GetAwaiter().GetResult();

    public Task<VerifyResult?> Verify(string phoneRaw, string code, CancellationToken cancellationToken) =>
        mediator.Send(new VerifyOtpCommand(phoneRaw, code), cancellationToken);

    public bool TryGetUserByToken(string? bearerToken, out SessionUser? user)
    {
        user = null;
        var token = AuthUtils.TryParseBearerToken(bearerToken);
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
        var token = AuthUtils.TryParseBearerToken(bearerToken);
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
        var token = AuthUtils.TryParseBearerToken(bearerToken);
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
        var token = AuthUtils.TryParseBearerToken(bearerToken);
        if (string.IsNullOrEmpty(token))
            return false;

        var utcNow = DateTimeOffset.UtcNow;
        var row = db.AuthSessions.FirstOrDefault(s => s.Token == token && s.ExpiresAt > utcNow);
        if (row is null)
            return false;

        var u = row.User.Clone();
        u.Name = snapshot.DisplayName;
        u.Username = snapshot.Username;
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
        var token = AuthUtils.TryParseBearerToken(bearerToken);
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

    public Task<LoginResult?> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
        mediator.Send(new LoginCommand(email, password), cancellationToken);

    public Task<RegisterStartResult?> StartRegistrationAsync(
        string password,
        string email,
        string username,
        string phoneRaw,
        CancellationToken cancellationToken) =>
        mediator.Send(new RegisterCommand(password, email, username, phoneRaw), cancellationToken);

    public async Task<VerifyPhoneResult?> VerifyRegistrationPhoneAsync(
        string registrationId,
        string code,
        CancellationToken cancellationToken)
    {
        var pending = await GetValidRegistrationAsync(registrationId, cancellationToken);
        if (pending is null)
            return null;

        var otp = await db.AuthPendingOtps.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PhoneDigits == pending.PhoneDigits, cancellationToken);
        if (otp is null || DateTimeOffset.UtcNow > otp.ExpiresAt)
            return null;

        if (AuthUtils.DigitsOnly(code) != otp.Code)
            return null;

        await db.AuthPendingOtps.Where(p => p.PhoneDigits == pending.PhoneDigits)
            .ExecuteDeleteAsync(cancellationToken);

        pending.PhoneVerified = true;
        var emailCode = AuthPersistenceHelper.GenerateCode();
        var now = DateTimeOffset.UtcNow;
        await UpsertEmailOtpAsync(
            pending.RegistrationId,
            "register",
            emailCode,
            now,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        AuthPersistenceHelper.PruneExpiredCredentialsPending(db);

        Console.WriteLine("\u001b[31mRegister email OTP: " + pending.Email + " " + emailCode + "\u001b[0m");
        return new VerifyPhoneResult(
            AuthPersistenceHelper.RegistrationCodeLength,
            (int)AuthPersistenceHelper.CredentialsPendingTtl.TotalSeconds,
            AuthPersistenceHelper.DevCodeMaybe(configuration, hostEnvironment, emailCode));
    }

    public async Task<VerifyResult?> VerifyRegistrationEmailAsync(
        string registrationId,
        string code,
        CancellationToken cancellationToken)
    {
        var pending = await GetValidRegistrationAsync(registrationId, cancellationToken);
        if (pending is null || !pending.PhoneVerified)
            return null;

        var emailOtp = await db.AuthPendingEmailOtps.AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.Key == pending.RegistrationId && e.Purpose == "register",
                cancellationToken);
        if (emailOtp is null || DateTimeOffset.UtcNow > emailOtp.ExpiresAt)
            return null;

        if (AuthUtils.DigitsOnly(code) != emailOtp.Code)
            return null;

        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid().ToString("N");

        var account = new UserAccount
        {
            Id = userId,
            DisplayName = pending.Username,
            Username = pending.Username,
            Email = pending.Email,
            PasswordHash = pending.PasswordHash,
            PhoneDigits = pending.PhoneDigits,
            PhoneDisplay = pending.PhoneDisplay,
            EmailVerifiedAt = now,
            PhoneVerifiedAt = now,
            TrustScore = 50,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.UserAccounts.Add(account);

        await db.AuthPendingEmailOtps.Where(e => e.Key == pending.RegistrationId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.AuthPendingRegistrations.Where(r => r.RegistrationId == registrationId)
            .ExecuteDeleteAsync(cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        AuthPersistenceHelper.PruneExpiredCredentialsPending(db);

        var snapshot = AuthPersistenceHelper.ToSnapshot(account);
        var sessionUser = AuthUtils.CreateSessionUserFromSnapshot(
            snapshot,
            AuthUtils.FormatPhoneForDisplay(pending.PhoneDisplay, pending.PhoneDigits));
        var token = await AuthPersistenceHelper.CreateSessionAsync(db, sessionUser, cancellationToken);
        return new VerifyResult(token, sessionUser);
    }

    public async Task<ForgotPasswordResult?> RequestPasswordResetAsync(
        string email,
        string newPassword,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return null;

        var normalizedEmail = AuthUtils.NormalizeEmail(email);
        var account = await db.UserAccounts
            .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == normalizedEmail, cancellationToken);
        if (account is null)
            return null;

        var code = AuthPersistenceHelper.GenerateCode();
        var now = DateTimeOffset.UtcNow;
        var hash = AuthUtils.HashPassword(newPassword);

        var existing = await db.AuthPendingPasswordResets.FindAsync([normalizedEmail], cancellationToken);
        if (existing is not null)
        {
            existing.NewPasswordHash = hash;
            existing.Code = code;
            existing.ExpiresAt = now.Add(AuthPersistenceHelper.CredentialsPendingTtl);
            existing.CreatedAt = now;
        }
        else
        {
            db.AuthPendingPasswordResets.Add(new AuthPendingPasswordResetRow
            {
                Email = normalizedEmail,
                NewPasswordHash = hash,
                Code = code,
                ExpiresAt = now.Add(AuthPersistenceHelper.CredentialsPendingTtl),
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        AuthPersistenceHelper.PruneExpiredCredentialsPending(db);

        Console.WriteLine("\u001b[31mPassword reset OTP: " + normalizedEmail + " " + code + "\u001b[0m");
        return new ForgotPasswordResult(
            AuthPersistenceHelper.RegistrationCodeLength,
            (int)AuthPersistenceHelper.CredentialsPendingTtl.TotalSeconds,
            AuthPersistenceHelper.DevCodeMaybe(configuration, hostEnvironment, code));
    }

    public async Task<bool> ConfirmPasswordResetAsync(string email, string code, CancellationToken cancellationToken)
    {
        var normalizedEmail = AuthUtils.NormalizeEmail(email);
        var pending = await db.AuthPendingPasswordResets
            .FirstOrDefaultAsync(p => p.Email == normalizedEmail, cancellationToken);
        if (pending is null || DateTimeOffset.UtcNow > pending.ExpiresAt)
            return false;

        if (AuthUtils.DigitsOnly(code) != pending.Code)
            return false;

        var account = await db.UserAccounts
            .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == normalizedEmail, cancellationToken);
        if (account is null)
            return false;

        account.PasswordHash = pending.NewPasswordHash;
        account.UpdatedAt = DateTimeOffset.UtcNow;

        await db.AuthPendingPasswordResets.Where(p => p.Email == normalizedEmail)
            .ExecuteDeleteAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task UpsertFromSessionUserAsync(SessionUser user, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(user.Id))
            return;
        var id = user.Id.Trim();
        var now = DateTimeOffset.UtcNow;

        var phoneDisplay = user.Phone;
        var digits = AuthUtils.DigitsOnly(phoneDisplay);

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
        if (user.Username is not null && row.Username is null)
            row.Username = user.Username;
        row.PhoneDisplay = phoneDisplay ?? row.PhoneDisplay;

        if (!string.IsNullOrEmpty(digits))
        {
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

    public async Task<string?> GetAvatarUrlAsync(string userId, CancellationToken cancellationToken = default)
    {
        var url = await db.UserAccounts.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.AvatarUrl)
            .FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(url) ? null : url;
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

    public async Task<UserProfileSnapshot?> GetProfileSnapshotAsync(
        string? phoneDigits = null,
        CancellationToken cancellationToken = default)
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
        return AuthPersistenceHelper.ToSnapshot(row);
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
        return AuthPersistenceHelper.ToSnapshot(row);
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

    public async Task<bool> EmailHasRegisteredAccountAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        var normalized = AuthUtils.NormalizeEmail(email);
        if (normalized.Length == 0)
            return false;
        return await db.UserAccounts.AsNoTracking()
            .AnyAsync(u => u.Email != null && u.Email.ToLower() == normalized, cancellationToken);
    }

    public async Task<bool> UsernameHasRegisteredAccountAsync(
        string? username,
        CancellationToken cancellationToken = default)
    {
        var normalized = AuthUtils.NormalizeUsername(username);
        if (normalized is null)
            return false;
        return await db.UserAccounts.AsNoTracking()
            .AnyAsync(
                u => u.Username != null && u.Username.ToLower() == normalized.ToLower(),
                cancellationToken);
    }

    public async Task<string?> TrySetUsernameAsync(
        string userId,
        string username,
        CancellationToken cancellationToken = default)
    {
        if (!AuthUtils.IsValidUsername(username))
            return "invalid_username";

        var row = await db.UserAccounts.FindAsync([userId], cancellationToken);
        if (row is null)
            return "user_not_found";

        if (!string.IsNullOrEmpty(row.Username))
            return "username_already_set";

        var taken = await db.UserAccounts.AsNoTracking()
            .AnyAsync(
                u => u.Username != null
                    && u.Username.ToLower() == username.ToLower()
                    && u.Id != userId,
                cancellationToken);
        if (taken)
            return "username_taken";

        row.Username = username;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<IReadOnlyList<UserContactDto>> ListContactsAsync(
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.UserContacts.AsNoTracking()
            .Where(c => c.OwnerUserId == ownerUserId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.ContactUserId, c.CreatedAt })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return Array.Empty<UserContactDto>();

        var ids = rows.Select(r => r.ContactUserId).Distinct().ToList();
        var users = await db.UserAccounts.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var list = new List<UserContactDto>(rows.Count);
        foreach (var row in rows)
        {
            if (!users.TryGetValue(row.ContactUserId, out var u))
                continue;
            list.Add(ToContactDto(u, row.CreatedAt));
        }

        return list;
    }

    public async Task<UserContactDto> AddContactByPhoneAsync(
        string ownerUserId,
        string phoneRaw,
        CancellationToken cancellationToken = default)
    {
        var digits = AuthUtils.DigitsOnly(phoneRaw);
        if (string.IsNullOrEmpty(digits))
            throw new InvalidOperationException("Indica un número de teléfono.");

        var target = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(u => u.PhoneDigits == digits, cancellationToken);
        if (target is null)
            throw new InvalidOperationException("Ese número no está registrado en la plataforma.");

        if (target.Id == ownerUserId)
            throw new InvalidOperationException("No puedes añadirte a ti mismo como contacto.");

        var now = DateTimeOffset.UtcNow;
        var existing = await db.UserContacts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c => c.OwnerUserId == ownerUserId && c.ContactUserId == target.Id,
                cancellationToken);
        if (existing is not null)
        {
            if (existing.DeletedAtUtc is null)
                throw new InvalidOperationException("Ese contacto ya está en tu lista.");
            existing.DeletedAtUtc = null;
            existing.CreatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return ToContactDto(target, existing.CreatedAt);
        }

        var id = "uc_" + Guid.NewGuid().ToString("N");
        db.UserContacts.Add(new UserContactRow
        {
            Id = id,
            OwnerUserId = ownerUserId,
            ContactUserId = target.Id,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);

        return ToContactDto(target, now);
    }

    public async Task<PlatformUserByPhoneDto?> ResolveContactByPhoneAsync(
        string requesterUserId,
        string phoneRaw,
        CancellationToken cancellationToken = default)
    {
        var digits = AuthUtils.DigitsOnly(phoneRaw);
        if (string.IsNullOrEmpty(digits))
            throw new InvalidOperationException("Indica un número de teléfono.");

        var target = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(u => u.PhoneDigits == digits, cancellationToken);
        if (target is null)
            return null;

        if (string.Equals((target.Id ?? "").Trim(), (requesterUserId ?? "").Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException("No puedes usarte a ti mismo como transportista de contacto.");

        return new PlatformUserByPhoneDto(
            target.Id!,
            target.DisplayName ?? "",
            target.PhoneDisplay,
            target.PhoneDigits);
    }

    public async Task<bool> RemoveContactAsync(
        string ownerUserId,
        string contactUserId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.UserContacts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c => c.OwnerUserId == ownerUserId && c.ContactUserId == contactUserId,
                cancellationToken);
        if (row is null)
            return false;
        if (row.DeletedAtUtc is not null)
            return true;

        row.DeletedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<AuthPendingRegistrationRow?> GetValidRegistrationAsync(
        string registrationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(registrationId))
            return null;

        var pending = await db.AuthPendingRegistrations
            .FirstOrDefaultAsync(r => r.RegistrationId == registrationId.Trim(), cancellationToken);
        if (pending is null || DateTimeOffset.UtcNow > pending.ExpiresAt)
            return null;
        return pending;
    }

    private async Task UpsertEmailOtpAsync(
        string key,
        string purpose,
        string code,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var expiresAt = now.Add(AuthPersistenceHelper.CredentialsPendingTtl);
        var existing = await db.AuthPendingEmailOtps.FindAsync([key], cancellationToken);
        if (existing is not null)
        {
            existing.Purpose = purpose;
            existing.Code = code;
            existing.ExpiresAt = expiresAt;
            existing.CreatedAt = now;
        }
        else
        {
            db.AuthPendingEmailOtps.Add(new AuthPendingEmailOtpRow
            {
                Key = key,
                Purpose = purpose,
                Code = code,
                ExpiresAt = expiresAt,
                CreatedAt = now,
            });
        }
    }

    private static UserContactDto ToContactDto(UserAccount u, DateTimeOffset createdAt) =>
        new(
            u.Id,
            u.DisplayName,
            u.PhoneDisplay,
            u.PhoneDigits,
            createdAt);
}
