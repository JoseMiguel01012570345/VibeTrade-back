using System.Text.Json.Nodes;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Chat;

/// <summary>
/// Alinea <see cref="ChatMessageDto"/> con el shape de <c>Message</c> del market store (front).
/// </summary>
public static class ChatMarketMessageJsonMapper
{
    public static JsonObject ToMarketMessage(ChatMessageDto m, string viewerUserId)
    {
        var from = m.SenderUserId == viewerUserId ? "me" : "other";
        var at = m.CreatedAtUtc.ToUnixTimeMilliseconds();
        var read = from == "me" ? m.Status == ChatMessageStatus.Read : true;
        var statusStr = ChatStatusToApiString(m.Status);

        return m.Payload switch
        {
            ChatTextPayload p => MapText(m.Id, from, at, read, statusStr, p),
            ChatAudioPayload p => MapAudio(m.Id, from, at, read, p),
            ChatImagePayload p => MapImage(m.Id, from, at, read, p),
            ChatDocPayload p => MapDoc(m.Id, from, at, read, p),
            ChatDocsBundlePayload p => MapDocs(m.Id, from, at, read, p),
            ChatAgreementPayload p => MapAgreement(m.Id, from, at, read, statusStr, p),
            ChatSystemTextPayload p => MapSystemText(m.Id, at, read, p),
            ChatCertificatePayload p => MapCertificate(m.Id, from, at, read, statusStr, p),
            _ => TextFallback(m.Id, from, at, read, statusStr, ""),
        };
    }

    private static string ChatStatusToApiString(ChatMessageStatus s) => s switch
    {
        ChatMessageStatus.Pending => "pending",
        ChatMessageStatus.Sent => "sent",
        ChatMessageStatus.Delivered => "delivered",
        ChatMessageStatus.Read => "read",
        ChatMessageStatus.Error => "error",
        _ => "sent",
    };

    private static JsonObject MapText(
        string id,
        string from,
        long at,
        bool read,
        string chatStatus,
        ChatTextPayload p)
    {
        var obj = new JsonObject
        {
            ["id"] = id,
            ["from"] = from,
            ["type"] = "text",
            ["text"] = p.Text,
            ["at"] = at,
            ["read"] = read,
            ["chatStatus"] = chatStatus,
        };
        if (!string.IsNullOrEmpty(p.OfferQaId))
            obj["offerQaId"] = p.OfferQaId;
        AppendReplyQuotes(obj, p.ReplyQuotes);
        return obj;
    }

    private static JsonObject MapAudio(string id, string from, long at, bool read, ChatAudioPayload p)
    {
        var obj = new JsonObject
        {
            ["id"] = id,
            ["from"] = from,
            ["type"] = "audio",
            ["url"] = p.Url,
            ["seconds"] = p.Seconds,
            ["at"] = at,
            ["read"] = read,
        };
        AppendReplyQuotes(obj, p.ReplyQuotes);
        return obj;
    }

    private static JsonObject MapImage(string id, string from, long at, bool read, ChatImagePayload p)
    {
        var images = new JsonArray();
        foreach (var img in p.Images)
            images.Add(new JsonObject { ["url"] = img.Url });

        var obj = new JsonObject
        {
            ["id"] = id,
            ["from"] = from,
            ["type"] = "image",
            ["images"] = images,
            ["at"] = at,
            ["read"] = read,
        };
        if (!string.IsNullOrEmpty(p.Caption))
            obj["caption"] = p.Caption;
        if (p.EmbeddedAudio is { } ea)
        {
            obj["embeddedAudio"] = new JsonObject
            {
                ["url"] = ea.Url,
                ["seconds"] = ea.Seconds,
            };
        }
        AppendReplyQuotes(obj, p.ReplyQuotes);
        return obj;
    }

    private static JsonObject MapDoc(string id, string from, long at, bool read, ChatDocPayload p)
    {
        var obj = new JsonObject
        {
            ["id"] = id,
            ["from"] = from,
            ["type"] = "doc",
            ["name"] = p.Name,
            ["size"] = p.Size,
            ["kind"] = p.Kind,
            ["at"] = at,
            ["read"] = read,
        };
        if (!string.IsNullOrEmpty(p.Url))
            obj["url"] = p.Url;
        if (!string.IsNullOrEmpty(p.Caption))
            obj["caption"] = p.Caption;
        AppendReplyQuotes(obj, p.ReplyQuotes);
        return obj;
    }

    private static JsonObject MapDocs(string id, string from, long at, bool read, ChatDocsBundlePayload p)
    {
        var docs = new JsonArray();
        foreach (var el in p.Documents)
        {
            var one = new JsonObject
            {
                ["name"] = el.Name,
                ["size"] = el.Size,
                ["kind"] = el.Kind,
            };
            if (!string.IsNullOrEmpty(el.Url))
                one["url"] = el.Url;
            docs.Add(one);
        }

        var obj = new JsonObject
        {
            ["id"] = id,
            ["from"] = from,
            ["type"] = "docs",
            ["documents"] = docs,
            ["at"] = at,
            ["read"] = read,
        };
        if (!string.IsNullOrEmpty(p.Caption))
            obj["caption"] = p.Caption;
        if (p.EmbeddedAudio is { } ea)
        {
            obj["embeddedAudio"] = new JsonObject
            {
                ["url"] = ea.Url,
                ["seconds"] = ea.Seconds,
            };
        }
        AppendReplyQuotes(obj, p.ReplyQuotes);
        return obj;
    }

    private static void AppendReplyQuotes(JsonObject obj, IReadOnlyList<ReplyQuoteDto>? quotes)
    {
        if (quotes is null || quotes.Count == 0)
            return;
        var outArr = new JsonArray();
        foreach (var el in quotes)
        {
            if (string.IsNullOrEmpty(el.MessageId) || el.Author is null || el.Preview is null)
                continue;
            outArr.Add(new JsonObject
            {
                ["id"] = el.MessageId,
                ["author"] = el.Author,
                ["preview"] = el.Preview,
            });
        }
        if (outArr.Count > 0)
            obj["replyQuotes"] = outArr;
    }

    private static JsonObject MapAgreement(
        string id,
        string from,
        long at,
        bool read,
        string chatStatus,
        ChatAgreementPayload p)
    {
        return new JsonObject
        {
            ["id"] = id,
            ["from"] = from,
            ["type"] = "agreement",
            ["agreementId"] = p.AgreementId,
            ["title"] = p.Title,
            ["at"] = at,
            ["read"] = read,
            ["chatStatus"] = chatStatus,
        };
    }

    private static JsonObject MapCertificate(
        string id,
        string from,
        long at,
        bool read,
        string chatStatus,
        ChatCertificatePayload p)
    {
        return new JsonObject
        {
            ["id"] = id,
            ["from"] = from,
            ["type"] = "text",
            ["text"] = string.IsNullOrEmpty(p.Body) ? p.Title : $"{p.Title}: {p.Body}",
            ["at"] = at,
            ["read"] = read,
            ["chatStatus"] = chatStatus,
        };
    }

    private static JsonObject MapSystemText(string id, long at, bool read, ChatSystemTextPayload p)
    {
        return new JsonObject
        {
            ["id"] = id,
            ["from"] = "system",
            ["type"] = "text",
            ["text"] = p.Text,
            ["at"] = at,
            ["read"] = read,
        };
    }

    private static JsonObject TextFallback(
        string id,
        string from,
        long at,
        bool read,
        string chatStatus,
        string text)
    {
        return new JsonObject
        {
            ["id"] = id,
            ["from"] = from,
            ["type"] = "text",
            ["text"] = text,
            ["at"] = at,
            ["read"] = read,
            ["chatStatus"] = chatStatus,
        };
    }
}
