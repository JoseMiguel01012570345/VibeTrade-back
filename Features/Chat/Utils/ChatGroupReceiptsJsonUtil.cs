using System.Text.Json;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Chat.Utils;

public static class ChatGroupReceiptsJsonUtil
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static ChatMessageGroupReceipts Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ChatMessageGroupReceipts();
        try
        {
            return JsonSerializer.Deserialize<ChatMessageGroupReceipts>(json, Options)
                ?? new ChatMessageGroupReceipts();
        }
        catch
        {
            return new ChatMessageGroupReceipts();
        }
    }

    public static string Serialize(ChatMessageGroupReceipts r) =>
        JsonSerializer.Serialize(r, Options);
}
