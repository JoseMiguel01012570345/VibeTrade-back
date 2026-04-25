using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Chat.Utils;

/// <summary>Reglas de visibilidad de hilo; antes en <c>ChatService</c>.</summary>
public static class ChatThreadAccess
{
    public static bool UserCanSeeThread(string userId, ChatThreadRow t) =>
        t.DeletedAtUtc is null
        && (t.InitiatorUserId == userId
            || (t.FirstMessageSentAtUtc is not null
                && (t.BuyerUserId == userId || t.SellerUserId == userId)));

    /// <summary>Trim, igualdad ordinal o mismos dígitos (≥6 c/u).</summary>
    public static bool UserIdsMatchLoose(string viewerId, string? storedCarrierId)
    {
        var a = (viewerId ?? "").Trim();
        var b = (storedCarrierId ?? "").Trim();
        if (a.Length == 0 || b.Length == 0)
            return false;
        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;
        static string Digits(string s) => string.Concat(s.Where(char.IsDigit));
        var da = Digits(a);
        var db = Digits(b);
        return da.Length >= 6 && db.Length >= 6 && string.Equals(da, db, StringComparison.Ordinal);
    }
}
