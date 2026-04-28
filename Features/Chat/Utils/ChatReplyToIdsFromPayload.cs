namespace VibeTrade.Backend.Features.Chat.Utils;

public static class ChatReplyToIdsFromPayload
{
    public static IReadOnlyList<string>? ReadList(PostChatMessageBody body) =>
        body.ReplyToIds is { Count: > 0 } l ? l : null;
}
