using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Infrastructure.DataRepair;

/// <summary>
/// One-off repair: make user id invariant by setting <c>user_accounts.Id = PhoneDigits</c> (when present),
/// and update dependent rows (<c>stores.OwnerUserId</c>).
/// Idempotent and safe to run at startup.
/// </summary>
public static class UserIdPhoneDigitsRepair
{
    public static async Task RunAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        // Find rows where Id differs from PhoneDigits and PhoneDigits is present.
        var pairs = await db.UserAccounts.AsNoTracking()
            .Where(x => x.PhoneDigits != null && x.PhoneDigits != "" && x.Id != x.PhoneDigits)
            .Select(x => new { OldId = x.Id, NewId = x.PhoneDigits! })
            .ToListAsync(cancellationToken);

        if (pairs.Count == 0)
            return;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var p in pairs)
        {
            // Goal for each phoneDigits (NewId): ensure there is exactly one canonical row with:
            // - user_accounts.Id = NewId
            // - user_accounts.PhoneDigits = NewId
            //
            // Then remap stores.OwnerUserId from OldId to NewId and delete OldId.

            var existsCanonical = await db.UserAccounts.AsNoTracking()
                .AnyAsync(x => x.Id == p.NewId, cancellationToken);

            // If there is a row already holding this PhoneDigits but with a different Id, we must merge it into canonical.
            var phoneHolderId = await db.UserAccounts.AsNoTracking()
                .Where(x => x.PhoneDigits == p.NewId)
                .Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (!existsCanonical)
            {
                if (!string.IsNullOrEmpty(phoneHolderId))
                {
                    // Create canonical row copying from the phone-holder, but WITHOUT PhoneDigits (avoid unique conflict).
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $@"INSERT INTO user_accounts
                           (""Id"", ""PhoneDigits"", ""AvatarUrl"", ""DisplayName"", ""Email"", ""PhoneDisplay"",
                            ""Instagram"", ""Telegram"", ""XAccount"", ""TrustScore"", ""CreatedAt"", ""UpdatedAt"")
                           SELECT {p.NewId}, NULL, ""AvatarUrl"", ""DisplayName"", ""Email"", ""PhoneDisplay"",
                                  ""Instagram"", ""Telegram"", ""XAccount"", ""TrustScore"", ""CreatedAt"", ""UpdatedAt""
                           FROM user_accounts
                           WHERE ""Id"" = {phoneHolderId};",
                        cancellationToken);
                }
                else
                {
                    // Create minimal canonical row.
                    var now = DateTimeOffset.UtcNow;
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $@"INSERT INTO user_accounts
                           (""Id"", ""PhoneDigits"", ""DisplayName"", ""TrustScore"", ""CreatedAt"", ""UpdatedAt"")
                           VALUES ({p.NewId}, NULL, 'Usuario', 75, {now}, {now});",
                        cancellationToken);
                }
            }

            // Remap stores pointing at the phone-holder id (if any) to canonical.
            if (!string.IsNullOrEmpty(phoneHolderId) && phoneHolderId != p.NewId)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $@"UPDATE stores
                       SET ""OwnerUserId"" = {p.NewId}
                       WHERE ""OwnerUserId"" = {phoneHolderId};",
                    cancellationToken);

                await db.Database.ExecuteSqlInterpolatedAsync(
                    $@"DELETE FROM user_accounts
                       WHERE ""Id"" = {phoneHolderId};",
                    cancellationToken);
            }

            // Remap stores pointing at OldId to canonical.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE stores
                   SET ""OwnerUserId"" = {p.NewId}
                   WHERE ""OwnerUserId"" = {p.OldId};",
                cancellationToken);

            // Delete OldId if still present (it might already be gone or equal to canonical).
            if (p.OldId != p.NewId)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $@"DELETE FROM user_accounts
                       WHERE ""Id"" = {p.OldId};",
                    cancellationToken);
            }

            // Finally, set PhoneDigits on canonical (now unique slot is free).
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE user_accounts
                   SET ""PhoneDigits"" = {p.NewId}
                   WHERE ""Id"" = {p.NewId};",
                cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }
}

