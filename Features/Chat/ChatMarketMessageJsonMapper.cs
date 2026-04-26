using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Chat;

/// <summary>
/// Alinea <see cref="ChatMessageDto"/> con el shape de <c>Message</c> del market store (front).
/// </summary>
public static class ChatMarketMessageJsonMapper
{
    public static ChatThreadMessageView ToMarketMessage(ChatMessageDto m, string viewerUserId)
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

    private static ChatThreadMessageView MapText(
        string id,
        string from,
        long at,
        bool read,
        string chatStatus,
        ChatTextPayload p)
    {
        var v = new ChatThreadMessageView
        {
            Id = id,
            From = from,
            Type = "text",
            Text = p.Text,
            At = at,
            Read = read,
            ChatStatus = chatStatus,
        };
        if (!string.IsNullOrEmpty(p.OfferQaId))
            v.OfferQaId = p.OfferQaId;
        AppendReplyQuotes(v, p.ReplyQuotes);
        return v;
    }

    private static ChatThreadMessageView MapAudio(string id, string from, long at, bool read, ChatAudioPayload p)
    {
        var v = new ChatThreadMessageView
        {
            Id = id,
            From = from,
            Type = "audio",
            Url = p.Url,
            Seconds = p.Seconds,
            At = at,
            Read = read,
        };
        AppendReplyQuotes(v, p.ReplyQuotes);
        return v;
    }

    private static ChatThreadMessageView MapImage(string id, string from, long at, bool read, ChatImagePayload p)
    {
        var v = new ChatThreadMessageView
        {
            Id = id,
            From = from,
            Type = "image",
            Images = p.Images.Select(img => new ChatMessageImageView { Url = img.Url }).ToList(),
            At = at,
            Read = read,
        };
        if (!string.IsNullOrEmpty(p.Caption))
            v.Caption = p.Caption;
        if (p.EmbeddedAudio is { } ea)
        {
            v.EmbeddedAudio = new ChatMessageEmbeddedAudioView
            {
                Url = ea.Url,
                Seconds = ea.Seconds,
            };
        }
        AppendReplyQuotes(v, p.ReplyQuotes);
        return v;
    }

    private static ChatThreadMessageView MapDoc(string id, string from, long at, bool read, ChatDocPayload p)
    {
        var v = new ChatThreadMessageView
        {
            Id = id,
            From = from,
            Type = "doc",
            Name = p.Name,
            Size = p.Size,
            Kind = p.Kind,
            At = at,
            Read = read,
        };
        if (!string.IsNullOrEmpty(p.Url))
            v.Url = p.Url;
        if (!string.IsNullOrEmpty(p.Caption))
            v.Caption = p.Caption;
        AppendReplyQuotes(v, p.ReplyQuotes);
        return v;
    }

    private static ChatThreadMessageView MapDocs(string id, string from, long at, bool read, ChatDocsBundlePayload p)
    {
        var docs = p.Documents
            .Select(el => new ChatMessageDocView
            {
                Name = el.Name,
                Size = el.Size,
                Kind = el.Kind,
                Url = string.IsNullOrEmpty(el.Url) ? null : el.Url,
            })
            .ToList();
        var v = new ChatThreadMessageView
        {
            Id = id,
            From = from,
            Type = "docs",
            Documents = docs,
            At = at,
            Read = read,
        };
        if (!string.IsNullOrEmpty(p.Caption))
            v.Caption = p.Caption;
        if (p.EmbeddedAudio is { } ea)
        {
            v.EmbeddedAudio = new ChatMessageEmbeddedAudioView
            {
                Url = ea.Url,
                Seconds = ea.Seconds,
            };
        }
        AppendReplyQuotes(v, p.ReplyQuotes);
        return v;
    }

    private static void AppendReplyQuotes(ChatThreadMessageView obj, IReadOnlyList<ReplyQuoteDto>? quotes)
    {
        if (quotes is null || quotes.Count == 0)
            return;
        var outList = new List<ChatReplyQuoteView>();
        foreach (var el in quotes)
        {
            if (string.IsNullOrEmpty(el.MessageId) || el.Author is null || el.Preview is null)
                continue;
            outList.Add(new ChatReplyQuoteView
            {
                Id = el.MessageId,
                Author = el.Author,
                Preview = el.Preview,
            });
        }
        if (outList.Count > 0)
            obj.ReplyQuotes = outList;
    }

    private static ChatThreadMessageView MapAgreement(
        string id,
        string from,
        long at,
        bool read,
        string chatStatus,
        ChatAgreementPayload p) =>
        new()
        {
            Id = id,
            From = from,
            Type = "agreement",
            AgreementId = p.AgreementId,
            Title = p.Title,
            At = at,
            Read = read,
            ChatStatus = chatStatus,
        };

    private static ChatThreadMessageView MapCertificate(
        string id,
        string from,
        long at,
        bool read,
        string chatStatus,
        ChatCertificatePayload p) =>
        new()
        {
            Id = id,
            From = from,
            Type = "text",
            Text = string.IsNullOrEmpty(p.Body) ? p.Title : $"{p.Title}: {p.Body}",
            At = at,
            Read = read,
            ChatStatus = chatStatus,
        };

    private static ChatThreadMessageView MapSystemText(string id, long at, bool read, ChatSystemTextPayload p) =>
        new()
        {
            Id = id,
            From = "system",
            Type = "text",
            Text = p.Text,
            At = at,
            Read = read,
        };

    private static ChatThreadMessageView TextFallback(
        string id,
        string from,
        long at,
        bool read,
        string chatStatus,
        string text) =>
        new()
        {
            Id = id,
            From = from,
            Type = "text",
            Text = text,
            At = at,
            Read = read,
            ChatStatus = chatStatus,
        };
}
