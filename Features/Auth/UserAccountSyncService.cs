using System.Text.Json;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Auth;

public sealed class UserAccountSyncService(AppDbContext db) : IUserAccountSyncService
{
    public async Task UpsertFromSessionUserAsync(JsonElement user, CancellationToken cancellationToken = default)
    {
        if (!user.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
            return;
        var id = idEl.GetString()!;
        var now = DateTimeOffset.UtcNow;

        var phoneDisplay = user.TryGetProperty("phone", out var ph) && ph.ValueKind == JsonValueKind.String
            ? ph.GetString()
            : null;
        var digits = DigitsOnly(phoneDisplay);

        var row = await db.UserAccounts.FindAsync([id], cancellationToken);
        if (row is null)
        {
            row = new UserAccount
            {
                Id = id,
                CreatedAt = now,
            };
            db.UserAccounts.Add(row);
        }

        row.DisplayName = GetString(user, "name") ?? row.DisplayName;
        row.Email = GetString(user, "email") ?? row.Email;
        row.PhoneDisplay = phoneDisplay ?? row.PhoneDisplay;
        row.PhoneDigits = string.IsNullOrEmpty(digits) ? row.PhoneDigits : digits;
        row.AvatarUrl = GetString(user, "avatarUrl") ?? row.AvatarUrl;
        row.Instagram = GetString(user, "instagram") ?? row.Instagram;
        row.Telegram = GetString(user, "telegram") ?? row.Telegram;
        row.XAccount = GetString(user, "xAccount") ?? row.XAccount;
        if (user.TryGetProperty("trustScore", out var ts) && ts.TryGetInt32(out var ti))
            row.TrustScore = ti;
        row.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string DigitsOnly(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";
        return string.Concat(raw.Where(char.IsDigit));
    }
}
