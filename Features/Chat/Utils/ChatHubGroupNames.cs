namespace VibeTrade.Backend.Features.Chat.Utils;

public static class ChatHubGroupNames
{
    public static string ForUser(string userId) => $"user:{(userId ?? "").Trim()}";
    public static string ForOffer(string offerId) => $"offer:{(offerId ?? "").Trim()}";
    public static string ForThread(string threadId) => $"thread:{(threadId ?? "").Trim()}";
}
