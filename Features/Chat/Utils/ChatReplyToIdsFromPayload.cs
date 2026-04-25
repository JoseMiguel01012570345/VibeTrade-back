using System.Text.Json;

namespace VibeTrade.Backend.Features.Chat.Utils;

public static class ChatReplyToIdsFromPayload
{
    public static IReadOnlyList<string>? ReadList(JsonElement payload)
    {
        if (!payload.TryGetProperty("replyToIds", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<string>();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String)
                continue;
            var s = el.GetString();
            if (!string.IsNullOrWhiteSpace(s))
                list.Add(s.Trim());
        }
        return list.Count == 0 ? null : list;
    }
}
