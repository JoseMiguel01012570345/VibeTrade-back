using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Users.Dtos;
using VibeTrade.Backend.Features.Users.Interfaces;

namespace VibeTrade.Backend.Features.Users;

public sealed class UsersAdminService(AppDbContext db) : IUsersAdminService
{
    private const int MinPasswordLength = 8;

    public async Task<IReadOnlyList<AdminUserDto>> ListAsync(string? search, CancellationToken cancellationToken)
    {
        var q = (search ?? "").Trim().ToLowerInvariant();

        var query = db.UserAccounts.AsNoTracking().AsQueryable();
        if (q.Length > 0)
        {
            query = query.Where(u =>
                (u.Email != null && u.Email.ToLower().Contains(q))
                || u.DisplayName.ToLower().Contains(q)
                || (u.Username != null && u.Username.ToLower().Contains(q))
                || (u.PhoneDigits != null && u.PhoneDigits.Contains(q)));
        }

        var rows = await query
            .OrderByDescending(u => u.CreatedAt)
            .Take(500)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
            return Array.Empty<AdminUserDto>();

        var ids = rows.Select(r => r.Id).ToList();
        var storeOwners = await db.Stores.AsNoTracking()
            .Where(s => ids.Contains(s.OwnerUserId))
            .Select(s => s.OwnerUserId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var affiliateOwners = await db.Affiliates.AsNoTracking()
            .Where(a => ids.Contains(a.OwnerUserId))
            .Select(a => a.OwnerUserId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ownerSet = storeOwners.ToHashSet(StringComparer.Ordinal);
        var affiliateSet = affiliateOwners.ToHashSet(StringComparer.Ordinal);

        return rows
            .Select(r => ToDto(r, ownerSet.Contains(r.Id), affiliateSet.Contains(r.Id)))
            .ToList();
    }

    public async Task<(AdminUserDto? Value, UsersOpError? Error)> CreateAsync(
        CreateUserRequest body,
        CancellationToken cancellationToken)
    {
        var email = AuthUtils.NormalizeEmail(body.Email);
        if (email.Length < 5 || !email.Contains('@'))
            return (null, new UsersOpError("invalid_email", "Email inválido."));

        var password = body.Password ?? "";
        if (password.Length < MinPasswordLength)
            return (null, new UsersOpError("invalid_password", $"La contraseña debe tener al menos {MinPasswordLength} caracteres."));

        var emailTaken = await db.UserAccounts.AsNoTracking()
            .AnyAsync(u => u.Email != null && u.Email.ToLower() == email, cancellationToken)
            .ConfigureAwait(false);
        if (emailTaken)
            return (null, new UsersOpError("email_taken", "Ese email ya está registrado."));

        var now = DateTimeOffset.UtcNow;
        var displayName = string.IsNullOrWhiteSpace(body.DisplayName)
            ? email.Split('@')[0]
            : body.DisplayName.Trim();

        var row = new UserAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = displayName,
            Email = email,
            PasswordHash = AuthUtils.HashPassword(password),
            EmailVerifiedAt = now,
            TrustScore = 50,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var digits = AuthUtils.DigitsOnly(body.Phone);
        if (digits.Length > 0)
        {
            var phoneTaken = await db.UserAccounts.AsNoTracking()
                .AnyAsync(u => u.PhoneDigits == digits, cancellationToken)
                .ConfigureAwait(false);
            if (!phoneTaken)
            {
                row.PhoneDigits = digits;
                row.PhoneDisplay = AuthUtils.FormatPhoneForDisplay(body.Phone, digits);
                row.PhoneVerifiedAt = now;
            }
        }

        db.UserAccounts.Add(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (ToDto(row, ownsStore: false, hasAffiliations: false), null);
    }

    public async Task<(AdminUserDto? Value, UsersOpError? Error)> UpdateAsync(
        string id,
        UpdateUserRequest body,
        CancellationToken cancellationToken)
    {
        var row = await db.UserAccounts.FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (row is null)
            return (null, new UsersOpError("not_found", "Usuario no encontrado."));

        if (body.Email is not null)
        {
            var email = AuthUtils.NormalizeEmail(body.Email);
            if (email.Length > 0)
            {
                if (!email.Contains('@'))
                    return (null, new UsersOpError("invalid_email", "Email inválido."));
                var taken = await db.UserAccounts.AsNoTracking()
                    .AnyAsync(u => u.Email != null && u.Email.ToLower() == email && u.Id != id, cancellationToken)
                    .ConfigureAwait(false);
                if (taken)
                    return (null, new UsersOpError("email_taken", "Ese email ya está en uso."));
                row.Email = email;
            }
            else
            {
                row.Email = null;
            }
        }

        if (body.DisplayName is not null)
            row.DisplayName = body.DisplayName.Trim();

        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (await ToDtoWithDerivedAsync(row, cancellationToken).ConfigureAwait(false), null);
    }

    public async Task<(AdminUserDto? Value, UsersOpError? Error)> SetRolesAsync(
        string id,
        SetUserRolesRequest body,
        CancellationToken cancellationToken)
    {
        var row = await db.UserAccounts.FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (row is null)
            return (null, new UsersOpError("not_found", "Usuario no encontrado."));

        row.Roles = RoleNames.SanitizeAssignable(body.Roles);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (await ToDtoWithDerivedAsync(row, cancellationToken).ConfigureAwait(false), null);
    }

    public async Task<UsersOpError?> SetPasswordAsync(
        string id,
        SetUserPasswordRequest body,
        CancellationToken cancellationToken)
    {
        var row = await db.UserAccounts.FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (row is null)
            return new UsersOpError("not_found", "Usuario no encontrado.");

        var password = body.NewPassword ?? "";
        if (password.Length < MinPasswordLength)
            return new UsersOpError("invalid_password", $"La contraseña debe tener al menos {MinPasswordLength} caracteres.");

        row.PasswordHash = AuthUtils.HashPassword(password);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return null;
    }

    public async Task<UsersOpError?> DeleteAsync(
        string id,
        string requestingUserId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(id, requestingUserId, StringComparison.Ordinal))
            return new UsersOpError("cannot_delete_self", "No puedes eliminar tu propia cuenta.");

        var row = await db.UserAccounts.FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (row is null)
            return new UsersOpError("not_found", "Usuario no encontrado.");

        var ownsStore = await db.Stores.AsNoTracking()
            .AnyAsync(s => s.OwnerUserId == id, cancellationToken)
            .ConfigureAwait(false);
        if (ownsStore)
            return new UsersOpError("owns_store", "No se puede eliminar un usuario que posee una tienda.");

        db.UserAccounts.Remove(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return null;
    }

    private async Task<AdminUserDto> ToDtoWithDerivedAsync(UserAccount row, CancellationToken cancellationToken)
    {
        var ownsStore = await db.Stores.AsNoTracking()
            .AnyAsync(s => s.OwnerUserId == row.Id, cancellationToken)
            .ConfigureAwait(false);
        var hasAffiliations = await db.Affiliates.AsNoTracking()
            .AnyAsync(a => a.OwnerUserId == row.Id, cancellationToken)
            .ConfigureAwait(false);
        return ToDto(row, ownsStore, hasAffiliations);
    }

    private static AdminUserDto ToDto(UserAccount row, bool ownsStore, bool hasAffiliations)
    {
        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in row.Roles)
        {
            if (!string.IsNullOrWhiteSpace(r))
                roles.Add(r.Trim().ToLowerInvariant());
        }
        if (ownsStore)
            roles.Add(RoleNames.SuperAdmin);
        if (hasAffiliations)
            roles.Add(RoleNames.Afiliado);

        return new AdminUserDto(
            row.Id,
            row.DisplayName,
            row.Email,
            row.Username,
            row.PhoneDisplay,
            roles.ToList(),
            row.TrustScore,
            ownsStore,
            row.CreatedAt);
    }
}
