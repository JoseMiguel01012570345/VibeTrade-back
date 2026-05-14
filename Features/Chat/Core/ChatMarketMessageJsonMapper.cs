using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Chat.Core;

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
            ChatUnifiedMessagePayload p => MapUnified(m.Id, from, at, read, statusStr, p),
            ChatTextPayload p => MapText(m.Id, from, at, read, statusStr, p),
            ChatAudioPayload p => MapAudio(m.Id, from, at, read, p),
            ChatImagePayload p => MapImage(m.Id, from, at, read, p),
            ChatDocPayload p => MapDoc(m.Id, from, at, read, p),
            ChatDocsBundlePayload p => MapDocs(m.Id, from, at, read, p),
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

    private static bool UnifiedHasUserFacingSlots(ChatUnifiedMessagePayload p) =>
        !string.IsNullOrWhiteSpace(p.Text)
        || p.Images is { Count: > 0 }
        || p.Documents is { Count: > 0 }
        || !string.IsNullOrWhiteSpace(p.VoiceUrl)
        || !string.IsNullOrWhiteSpace(p.Caption)
        || p.EmbeddedAudio is not null
        || !string.IsNullOrWhiteSpace(p.OfferQaId);

    private static ChatThreadMessageView? TryMapUnifiedPlatformDominant(
        string id,
        string from,
        long at,
        bool read,
        string chatStatus,
        ChatUnifiedMessagePayload p)
    {
        if (UnifiedHasUserFacingSlots(p))
            return null;

        if (p.PaymentFeeReceipt is { } r)
        {
            return new ChatThreadMessageView
            {
                Id = id,
                From = "system",
                Type = "payment_fee_receipt",
                At = at,
                Read = read,
                FromVibeTrade = true,
                PaymentFeeReceipt = ToPaymentFeeReceiptView(r),
            };
        }

        if (p.Agreement is { } ag)
        {
            return new ChatThreadMessageView
            {
                Id = id,
                From = from,
                Type = "agreement",
                AgreementId = ag.AgreementId,
                Title = ag.Title,
                At = at,
                Read = read,
                ChatStatus = chatStatus,
                FromVibeTrade = p.IssuedByVibeTradePlatform,
            };
        }

        if (p.Certificate is { } cert)
        {
            var txt = string.IsNullOrEmpty(cert.Body) ? cert.Title : $"{cert.Title}: {cert.Body}";
            return new ChatThreadMessageView
            {
                Id = id,
                From = "system",
                Type = "text",
                Text = txt,
                At = at,
                Read = read,
                ChatStatus = chatStatus,
                FromVibeTrade = true,
            };
        }

        if (!string.IsNullOrWhiteSpace(p.SystemText))
        {
            var platform = p.IssuedByVibeTradePlatform;
            return new ChatThreadMessageView
            {
                Id = id,
                From = platform ? "system" : from,
                Type = "text",
                Text = p.SystemText.Trim(),
                At = at,
                Read = read,
                ChatStatus = platform ? null : chatStatus,
                FromVibeTrade = platform,
            };
        }

        return null;
    }

    private static ChatPaymentFeeReceiptView ToPaymentFeeReceiptView(ChatUnifiedPlatformPaymentFeeReceiptBlock p) =>
        new()
        {
            AgreementId = p.AgreementId,
            AgreementTitle = p.AgreementTitle,
            PaymentId = p.PaymentId,
            CurrencyLower = p.CurrencyLower,
            SubtotalMinor = p.SubtotalMinor,
            ClimateMinor = p.ClimateMinor,
            StripeFeeMinorActual = p.StripeFeeMinorActual,
            StripeFeeMinorEstimated = p.StripeFeeMinorEstimated,
            TotalChargedMinor = p.TotalChargedMinor,
            StripePricingUrl = p.StripePricingUrl,
            Lines = p.Lines.Select(x => new ChatPaymentFeeReceiptLineView
            {
                Label = x.Label,
                AmountMinor = x.AmountMinor,
            }).ToList(),
            InvoiceIssuerPlatform = string.IsNullOrWhiteSpace(p.InvoiceIssuerPlatform)
                ? "VibeTrade"
                : p.InvoiceIssuerPlatform.Trim(),
            InvoiceStoreName = (p.InvoiceStoreName ?? "").Trim(),
        };

    private static ChatThreadMessageView MapUnified(
        string id,
        string from,
        long at,
        bool read,
        string chatStatus,
        ChatUnifiedMessagePayload p)
    {
        var dominant = TryMapUnifiedPlatformDominant(id, from, at, read, chatStatus, p);
        if (dominant is not null)
            return dominant;

        var v = new ChatThreadMessageView
        {
            Id = id,
            From = from,
            Type = "unified",
            At = at,
            Read = read,
            ChatStatus = chatStatus,
            FromVibeTrade = p.IssuedByVibeTradePlatform,
        };
        if (!string.IsNullOrWhiteSpace(p.Text))
            v.Text = p.Text.Trim();
        if (!string.IsNullOrWhiteSpace(p.OfferQaId))
            v.OfferQaId = p.OfferQaId.Trim();
        if (p.Images is { Count: > 0 } imgs)
            v.Images = imgs.Select(img => new ChatMessageImageView { Url = img.Url }).ToList();
        if (!string.IsNullOrWhiteSpace(p.Caption))
            v.Caption = p.Caption.Trim();
        if (p.EmbeddedAudio is { } ea)
        {
            v.EmbeddedAudio = new ChatMessageEmbeddedAudioView
            {
                Url = ea.Url,
                Seconds = ea.Seconds,
            };
        }
        if (p.Documents is { Count: > 0 } docs)
        {
            v.Documents = docs
                .Select(el => new ChatMessageDocView
                {
                    Name = el.Name,
                    Size = el.Size,
                    Kind = el.Kind,
                    Url = string.IsNullOrEmpty(el.Url) ? null : el.Url,
                })
                .ToList();
        }
        if (!string.IsNullOrWhiteSpace(p.VoiceUrl))
        {
            v.Url = p.VoiceUrl.Trim();
            v.Seconds = p.VoiceSeconds;
        }
        AppendReplyQuotes(v, p.RepliesTo);
        AppendReplyThreadMetadata(v, p);
        return v;
    }

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
        AppendReplyThreadMetadata(v, p);
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
        AppendReplyThreadMetadata(v, p);
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
        AppendReplyThreadMetadata(v, p);
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
        AppendReplyThreadMetadata(v, p);
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
        AppendReplyThreadMetadata(v, p);
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
                At = el.AtUtc.ToUnixTimeMilliseconds(),
            });
        }
        if (outList.Count > 0)
            obj.ReplyQuotes = outList;
    }

    private static void AppendReplyThreadMetadata(ChatThreadMessageView obj, ChatMessagePayload payload)
    {
        var ids = ResolveReplyToMessageIds(payload);
        if (ids is { Count: > 0 })
            obj.ReplyToMessageIds = ids;
    }

    private static IReadOnlyList<string>? ResolveReplyToMessageIds(ChatMessagePayload payload)
    {
        if (payload.ReplyToMessageIds is { Count: > 0 } persisted)
            return persisted;
        var quotes = payload switch
        {
            ChatUnifiedMessagePayload p => p.RepliesTo,
            ChatTextPayload p => p.ReplyQuotes,
            ChatImagePayload p => p.ReplyQuotes,
            ChatAudioPayload p => p.ReplyQuotes,
            ChatDocPayload p => p.ReplyQuotes,
            ChatDocsBundlePayload p => p.ReplyQuotes,
            _ => null,
        };
        if (quotes is null || quotes.Count == 0)
            return null;
        return quotes
            .Select(static q => q.MessageId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();
    }

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
