using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Chat.Utils;

internal static class ChatPostPayloadValidation
{
    public static bool TryParseAndValidateImagePayload(PostChatMessageBody b, out ChatImagePayload? parsed)
    {
        parsed = null;
        if (b.Images is not { Count: > 0 })
            return false;

        foreach (var img in b.Images)
        {
            if (!ChatMediaUrlRules.IsAllowedPersisted(img.Url))
                return false;
        }

        if (b.EmbeddedAudio is not null)
        {
            if (!ChatMediaUrlRules.IsAllowedPersisted(b.EmbeddedAudio.Url))
                return false;
            if (b.EmbeddedAudio.Seconds is < 1 or > 3600)
                return false;
        }

        if (b.Caption is { Length: > 4000 })
            return false;

        parsed = new ChatImagePayload
        {
            Images = b.Images,
            Caption = b.Caption,
            EmbeddedAudio = b.EmbeddedAudio,
        };
        return true;
    }

    public static bool TryParseAndValidateDocsBundlePayload(PostChatMessageBody b, out ChatDocsBundlePayload? parsed)
    {
        parsed = null;
        if (b.Documents is not { Count: > 0 })
            return false;

        foreach (var d in b.Documents)
        {
            if (string.IsNullOrWhiteSpace(d.Name))
                return false;
            if (d.Url is not null && !ChatMediaUrlRules.IsAllowedPersisted(d.Url))
                return false;
        }

        if (b.EmbeddedAudio is not null)
        {
            if (!ChatMediaUrlRules.IsAllowedPersisted(b.EmbeddedAudio.Url))
                return false;
            if (b.EmbeddedAudio.Seconds is < 1 or > 3600)
                return false;
        }

        if (b.Caption is { Length: > 4000 })
            return false;

        parsed = new ChatDocsBundlePayload
        {
            Documents = b.Documents,
            Caption = b.Caption,
            EmbeddedAudio = b.EmbeddedAudio,
        };
        return true;
    }
}
