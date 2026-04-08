using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;

namespace VibeTrade.Backend.Features.Auth;

public sealed class AuthService(IHostEnvironment hostEnvironment, IConfiguration configuration) : IAuthService
{
    private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SessionTtl = TimeSpan.FromDays(7);

    private readonly ConcurrentDictionary<string, PendingOtp> _pending = new();
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();
    private readonly ConcurrentDictionary<string, JsonElement> _adHocProfiles = new();

    private sealed record PendingOtp(string Code, DateTimeOffset Expires, int CodeLength);

    private sealed record SessionEntry(JsonElement User, DateTimeOffset Expires);

    public RequestCodeResult RequestCode(string phoneRaw)
    {
        var digits = DigitsOnly(phoneRaw);
        var code = Random.Shared.Next(1_000_000, 9_999_999).ToString();
        Console.WriteLine("RequestCode: " + digits + " " + code);
        PutPending(digits, code, code.Length);
        return new RequestCodeResult(code.Length, (int)PendingTtl.TotalSeconds, DevCodeMaybe(code));
    }

    public VerifyResult? Verify(string phoneRaw, string code)
    {
        var digits = DigitsOnly(phoneRaw);
        if (!_pending.TryGetValue(digits, out var pending))
            return null;
        if (DateTimeOffset.UtcNow > pending.Expires)
        {
            _pending.TryRemove(digits, out _);
            return null;
        }

        var normalizedCode = DigitsOnly(code);
        if (normalizedCode != pending.Code)
            return null;

        _pending.TryRemove(digits, out _);

        JsonElement userEl;
        if (_adHocProfiles.TryGetValue(digits, out var adHoc))
            userEl = adHoc;
        else
        {
            var newUser = BuildAdHocUserElement(digits);
            _adHocProfiles[digits] = newUser;
            userEl = newUser;
        }

        var token = Guid.NewGuid().ToString("N");
        _sessions[token] = new SessionEntry(userEl, DateTimeOffset.UtcNow.Add(SessionTtl));
        PruneSessions();

        return new VerifyResult(token, userEl);
    }

    public bool TryGetUserByToken(string? bearerToken, out JsonElement user)
    {
        user = default;
        var token = ParseBearer(bearerToken);
        if (string.IsNullOrEmpty(token))
            return false;
        if (!_sessions.TryGetValue(token, out var entry) || DateTimeOffset.UtcNow > entry.Expires)
        {
            _sessions.TryRemove(token, out _);
            return false;
        }

        user = entry.User;
        return true;
    }

    public bool RevokeSession(string? bearerToken)
    {
        var token = ParseBearer(bearerToken);
        if (string.IsNullOrEmpty(token))
            return false;
        return _sessions.TryRemove(token, out _);
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
        if (!_sessions.TryGetValue(token, out var entry) || DateTimeOffset.UtcNow > entry.Expires)
        {
            _sessions.TryRemove(token, out _);
            return false;
        }

        var root = JsonNode.Parse(entry.User.GetRawText())!.AsObject();
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
        _sessions[token] = new SessionEntry(updatedUser, entry.Expires);
        return true;
    }

    public bool TrySyncSessionFromSnapshot(string? bearerToken, UserProfileSnapshot snapshot, out JsonElement updatedUser)
    {
        updatedUser = default;
        var token = ParseBearer(bearerToken);
        if (string.IsNullOrEmpty(token))
            return false;
        if (!_sessions.TryGetValue(token, out var entry) || DateTimeOffset.UtcNow > entry.Expires)
        {
            _sessions.TryRemove(token, out _);
            return false;
        }

        var root = JsonNode.Parse(entry.User.GetRawText())!.AsObject();
        root["name"] = snapshot.DisplayName;
        root["email"] = snapshot.Email is { } e ? e : null;
        root["instagram"] = snapshot.Instagram is { } i ? i : null;
        root["telegram"] = snapshot.Telegram is { } t ? t : null;
        root["xAccount"] = snapshot.XAccount is { } x ? x : null;
        root["avatarUrl"] = snapshot.AvatarUrl is { } a ? a : null;

        using var doc = JsonDocument.Parse(root.ToJsonString());
        updatedUser = doc.RootElement.Clone();
        _sessions[token] = new SessionEntry(updatedUser, entry.Expires);
        return true;
    }

    public bool TrySetSessionUserId(string? bearerToken, string userId, out JsonElement updatedUser)
    {
        updatedUser = default;
        var token = ParseBearer(bearerToken);
        if (string.IsNullOrEmpty(token))
            return false;
        if (!_sessions.TryGetValue(token, out var entry) || DateTimeOffset.UtcNow > entry.Expires)
        {
            _sessions.TryRemove(token, out _);
            return false;
        }

        var root = JsonNode.Parse(entry.User.GetRawText())!.AsObject();
        root["id"] = userId;
        using var doc = JsonDocument.Parse(root.ToJsonString());
        updatedUser = doc.RootElement.Clone();
        _sessions[token] = new SessionEntry(updatedUser, entry.Expires);

        // Best-effort: keep ad-hoc cache aligned by phone digits (if present).
        var phone = root["phone"]?.GetValue<string>();
        var digits = DigitsOnly(phone);
        if (!string.IsNullOrEmpty(digits))
            _adHocProfiles[digits] = updatedUser;

        return true;
    }

    private void PutPending(string phoneDigits, string code, int len)
    {
        _pending[phoneDigits] = new PendingOtp(code, DateTimeOffset.UtcNow.Add(PendingTtl), len);
    }

    private string? DevCodeMaybe(string code)
    {
        var expose = configuration.GetValue("Auth:ExposeDevCodes", hostEnvironment.IsDevelopment());
        return expose ? code : null;
    }

    private static JsonElement BuildAdHocUserElement(string phoneDigits)
    {
        // Invariant identity: use phoneDigits as user id (unique in DB).
        var id = phoneDigits;
        var prettyPhone = FormatArMobile(phoneDigits);
        var user = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["name"] = "Nuevo usuario",
            ["phone"] = prettyPhone,
            ["trustScore"] = 75,
        };
        return JsonSerializer.SerializeToElement(user);
    }

    private static string FormatArMobile(string d)
    {
        if (d.Length >= 8)
        {
            var tail8 = d[^8..];
            return $"{d.Substring(0, 2)} {tail8[..4]}-{tail8[4..]}";
        }

        return $"{d.Substring(0, 2)} {d}";
    }

    private void PruneSessions()
    {
        if (_sessions.Count < 2000)
            return;
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _sessions)
        {
            if (now > kv.Value.Expires)
                _sessions.TryRemove(kv.Key, out _);
        }
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
