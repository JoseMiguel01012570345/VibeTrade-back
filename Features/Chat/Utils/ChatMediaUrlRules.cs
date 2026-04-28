namespace VibeTrade.Backend.Features.Chat.Utils;

public static class ChatMediaUrlRules
{
    public static bool IsAllowedPersisted(string url)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0 || !url.StartsWith("/", StringComparison.Ordinal))
            return false;
        if (url.Contains("..", StringComparison.Ordinal))
            return false;
        return url.StartsWith("/api/v1/media/", StringComparison.Ordinal);
    }
}
