using System.Text.Json;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Chat.Utils;

internal static class ChatPostPayloadValidation
{
    public static bool TryParseAndValidateImagePayload(JsonElement payload, out ChatImagePayload? parsed)
    {
        try
        {
            parsed = JsonSerializer.Deserialize<ChatImagePayload>(payload.GetRawText(), ChatMessageJson.Options)
                     ?? throw new JsonException();
        }
        catch
        {
            parsed = null;
            return false;
        }

        if (parsed.Images.Count == 0)
        {
            parsed = null;
            return false;
        }

        foreach (var img in parsed.Images)
        {
            if (!ChatMediaUrlRules.IsAllowedPersisted(img.Url))
            {
                parsed = null;
                return false;
            }
        }

        if (parsed.EmbeddedAudio is not null)
        {
            if (!ChatMediaUrlRules.IsAllowedPersisted(parsed.EmbeddedAudio.Url))
            {
                parsed = null;
                return false;
            }
            if (parsed.EmbeddedAudio.Seconds is < 1 or > 3600)
            {
                parsed = null;
                return false;
            }
        }

        if (parsed.Caption is { Length: > 4000 })
        {
            parsed = null;
            return false;
        }

        return true;
    }

    public static bool TryParseAndValidateDocsBundlePayload(JsonElement payload, out ChatDocsBundlePayload? parsed)
    {
        try
        {
            parsed = JsonSerializer.Deserialize<ChatDocsBundlePayload>(payload.GetRawText(), ChatMessageJson.Options)
                     ?? throw new JsonException();
        }
        catch
        {
            parsed = null;
            return false;
        }

        if (parsed.Documents.Count == 0)
        {
            parsed = null;
            return false;
        }

        foreach (var d in parsed.Documents)
        {
            if (string.IsNullOrWhiteSpace(d.Name))
            {
                parsed = null;
                return false;
            }
            if (d.Url is not null && !ChatMediaUrlRules.IsAllowedPersisted(d.Url))
            {
                parsed = null;
                return false;
            }
        }

        if (parsed.EmbeddedAudio is not null)
        {
            if (!ChatMediaUrlRules.IsAllowedPersisted(parsed.EmbeddedAudio.Url))
            {
                parsed = null;
                return false;
            }
            if (parsed.EmbeddedAudio.Seconds is < 1 or > 3600)
            {
                parsed = null;
                return false;
            }
        }

        if (parsed.Caption is { Length: > 4000 })
        {
            parsed = null;
            return false;
        }

        return true;
    }
}
