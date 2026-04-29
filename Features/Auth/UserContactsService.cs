using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Auth;

public sealed class UserContactsService(AppDbContext db) : IUserContactsService
{
    public async Task<IReadOnlyList<UserContactDto>> ListAsync(
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
            list.Add(ToDto(u, row.CreatedAt));
        }

        return list;
    }

    public async Task<UserContactDto> AddByPhoneAsync(
        string ownerUserId,
        string phoneRaw,
        CancellationToken cancellationToken = default)
    {
        var digits = DigitsOnly(phoneRaw);
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
            return ToDto(target, existing.CreatedAt);
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

        return ToDto(target, now);
    }

    public async Task<PlatformUserByPhoneDto?> ResolveByPhoneAsync(
        string requesterUserId,
        string phoneRaw,
        CancellationToken cancellationToken = default)
    {
        var digits = DigitsOnly(phoneRaw);
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

    public async Task<bool> RemoveAsync(
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

    private static UserContactDto ToDto(UserAccount u, DateTimeOffset createdAt) =>
        new(
            u.Id,
            u.DisplayName,
            u.PhoneDisplay,
            u.PhoneDigits,
            createdAt);

    private static string DigitsOnly(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";
        return string.Concat(raw.Where(char.IsDigit));
    }
}
