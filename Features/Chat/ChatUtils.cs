using System.Text.Json;
using System.Text.Json.Serialization;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Utils;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Features.Chat;

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

public static class ChatHubGroupNames
{
    public static string ForUser(string userId) => $"user:{(userId ?? "").Trim()}";
    public static string ForOffer(string offerId) => $"offer:{(offerId ?? "").Trim()}";
    public static string ForThread(string threadId) =>
        $"thread:{ChatThreadIds.NormalizePersistedId(threadId)}";
}

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

public static class ChatMessageDtoFactory
{
    public static ChatMessageDto FromRow(ChatMessageRow m, string? senderDisplayLabel = null) =>
        new(
            m.Id,
            m.ThreadId,
            m.SenderUserId,
            m.Payload,
            m.Status,
            m.CreatedAtUtc,
            m.UpdatedAtUtc,
            senderDisplayLabel);

    public static ChatMessageDto FromRowWithStatus(
        ChatMessageRow m,
        ChatMessageStatus displayStatus,
        string? senderDisplayLabel = null) =>
        new(
            m.Id,
            m.ThreadId,
            m.SenderUserId,
            m.Payload,
            displayStatus,
            m.CreatedAtUtc,
            m.UpdatedAtUtc,
            senderDisplayLabel);

    public static ChatThreadDto FromThread(
        ChatThreadRow t,
        string? buyerDisplayName = null,
        string? buyerAvatarUrl = null) =>
        new(
            t.Id,
            t.OfferId,
            t.StoreId,
            t.BuyerUserId,
            t.SellerUserId,
            t.InitiatorUserId,
            t.FirstMessageSentAtUtc,
            t.CreatedAtUtc,
            t.PurchaseMode,
            buyerDisplayName,
            buyerAvatarUrl,
            string.IsNullOrWhiteSpace(t.PartyExitedUserId) ? null : t.PartyExitedUserId.Trim(),
            string.IsNullOrWhiteSpace(t.PartyExitedReason) ? null : t.PartyExitedReason.Trim(),
            t.PartyExitedAtUtc,
            t.IsSocialGroup,
            string.IsNullOrWhiteSpace(t.SocialGroupTitle) ? null : t.SocialGroupTitle.Trim());
}

public static class ChatMessagePreviewText
{
    public static string FromPayload(ChatMessagePayload payload) =>
        payload is ChatUnifiedMessagePayload u ? PreviewUnified(u) : "Mensaje";

    private static string PreviewUnified(ChatUnifiedMessagePayload p)
    {
        if (!string.IsNullOrWhiteSpace(p.Text))
            return PreviewText(p.Text!);
        if (p.Images is { Count: > 0 })
            return string.IsNullOrWhiteSpace(p.Caption) ? "Foto" : p.Caption!.Trim();
        if (p.Documents is { Count: > 0 } docs)
            return docs.Count switch
            {
                1 => string.IsNullOrWhiteSpace(docs[0].Name) ? "Documento" : docs[0].Name.Trim(),
                var n => $"{n} documentos",
            };
        if (!string.IsNullOrWhiteSpace(p.VoiceUrl))
            return "Nota de voz";
        if (p.PaymentFeeReceipt is { } r)
            return string.IsNullOrWhiteSpace(r.AgreementTitle)
                ? "Recibo de pago (PDF)"
                : $"Recibo de pago: {r.AgreementTitle.Trim()}";
        if (p.Agreement is { } ag)
            return string.IsNullOrWhiteSpace(ag.Title)
                ? "Acuerdo"
                : $"Acuerdo: {ag.Title.Trim()}";
        if (p.Certificate is { } cert)
            return string.IsNullOrWhiteSpace(cert.Title)
                ? "Certificado"
                : cert.Title.Trim();
        if (!string.IsNullOrWhiteSpace(p.SystemText))
            return PreviewText(p.SystemText!);
        return "Mensaje";
    }

    private static string PreviewText(string tx)
    {
        tx = tx.Trim();
        return tx.Length == 0 ? "Mensaje" : tx;
    }
}

public static class ChatMessageStatusUpdateCore
{
    public static bool InExpectedList(string userId, IReadOnlyList<string> expected) =>
        expected.Any(
            e => string.Equals((e ?? "").Trim(), userId, StringComparison.Ordinal)
                || ChatThreadAccess.UserIdsMatchLoose(userId, (e ?? "").Trim()));

    public static string CanonicalRecipientId(string userId, IReadOnlyList<string> expected) =>
        expected.First(
            e => string.Equals((e ?? "").Trim(), userId, StringComparison.Ordinal)
                || ChatThreadAccess.UserIdsMatchLoose(userId, (e ?? "").Trim()));

    public static IReadOnlyList<string> MergedExpectedIds(ChatMessageGroupReceipts parsed, IReadOnlyList<string> fromRecipients)
    {
        var fromJson = parsed.ExpectedRecipientIds
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (fromJson.Count == 0)
            return fromRecipients;

        var merged = new List<string>(fromJson);
        foreach (var r in fromRecipients)
        {
            var rt = (r ?? "").Trim();
            if (rt.Length == 0)
                continue;
            if (merged.Any(e => RecipientIdsSamePerson(e, rt)))
                continue;
            merged.Add(rt);
        }
        return merged;
    }

    private static bool RecipientIdsSamePerson(string a, string b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal)
        || ChatThreadAccess.UserIdsMatchLoose((a ?? "").Trim(), b);

    public enum PairedApplyOutcome
    {
        RejectNull,
        ReturnDtoWithoutSave,
        MutatedSave,
    }

    public static PairedApplyOutcome TryApplyPaired(
        ChatMessageRow m,
        ChatMessageStatus request,
        DateTimeOffset now)
    {
        if (request == ChatMessageStatus.Delivered)
        {
            if (m.Status is ChatMessageStatus.Delivered or ChatMessageStatus.Read)
                return PairedApplyOutcome.ReturnDtoWithoutSave;
            if (m.Status != ChatMessageStatus.Sent)
                return PairedApplyOutcome.RejectNull;
            m.Status = ChatMessageStatus.Delivered;
            m.UpdatedAtUtc = now;
            return PairedApplyOutcome.MutatedSave;
        }
        if (m.Status == ChatMessageStatus.Read)
            return PairedApplyOutcome.ReturnDtoWithoutSave;
        if (m.Status is not (ChatMessageStatus.Sent or ChatMessageStatus.Delivered))
            return PairedApplyOutcome.RejectNull;
        m.Status = ChatMessageStatus.Read;
        m.UpdatedAtUtc = now;
        return PairedApplyOutcome.MutatedSave;
    }

    public static void ApplyGroup(
        ChatMessageGroupReceipts receipts,
        IReadOnlyList<string> expected,
        string canonical,
        ChatMessageStatus request,
        ChatMessageRow m,
        DateTimeOffset now)
    {
        if (receipts.ExpectedRecipientIds.Count == 0)
            receipts.ExpectedRecipientIds = new List<string>(expected);
        if (request == ChatMessageStatus.Delivered)
        {
            if (!receipts.DeliveredUserIds.Any(d => string.Equals((d ?? "").Trim(), canonical, StringComparison.Ordinal)))
                receipts.DeliveredUserIds.Add(canonical);
        }
        else
        {
            if (!receipts.ReadUserIds.Any(d => string.Equals((d ?? "").Trim(), canonical, StringComparison.Ordinal)))
                receipts.ReadUserIds.Add(canonical);
            if (!receipts.DeliveredUserIds.Any(d => string.Equals((d ?? "").Trim(), canonical, StringComparison.Ordinal)))
                receipts.DeliveredUserIds.Add(canonical);
        }
        m.GroupReceiptsJson = ChatGroupReceiptsJsonUtil.Serialize(receipts);
        var allD = expected.All(
            e => receipts.DeliveredUserIds.Any(
                d => string.Equals((d ?? "").Trim(), (e ?? "").Trim(), StringComparison.Ordinal)));
        var allR = expected.All(
            e => receipts.ReadUserIds.Any(
                d => string.Equals((d ?? "").Trim(), (e ?? "").Trim(), StringComparison.Ordinal)));
        if (allR)
        {
            m.Status = ChatMessageStatus.Read;
            m.UpdatedAtUtc = now;
        }
        else if (allD)
        {
            if (m.Status == ChatMessageStatus.Sent)
            {
                m.Status = ChatMessageStatus.Delivered;
                m.UpdatedAtUtc = now;
            }
        }
        else
        {
            m.UpdatedAtUtc = now;
        }
    }

    public static ChatMessageStatus OutgoingGroupDisplayStatus(
        IReadOnlyList<string> expected,
        ChatMessageGroupReceipts receipts)
    {
        if (expected is not { Count: > 1 })
            return ChatMessageStatus.Sent;
        if (AllRecipientsRead(expected, receipts))
            return ChatMessageStatus.Read;
        if (AllRecipientsHaveDeliveryReceipt(expected, receipts))
            return ChatMessageStatus.Delivered;
        return ChatMessageStatus.Sent;
    }

    public static bool AllRecipientsHaveDeliveryReceipt(
        IReadOnlyList<string> expected,
        ChatMessageGroupReceipts r) =>
        expected.All(
            e => r.DeliveredUserIds.Any(
                d => string.Equals((d ?? "").Trim(), (e ?? "").Trim(), StringComparison.Ordinal)));

    private static bool AllRecipientsRead(
        IReadOnlyList<string> expected,
        ChatMessageGroupReceipts r) =>
        expected.All(
            e => r.ReadUserIds.Any(
                d => string.Equals((d ?? "").Trim(), (e ?? "").Trim(), StringComparison.Ordinal)));

    public static void ApplyAllRecipientsSessionActiveAsDelivered(
        ChatMessageRow m,
        IReadOnlyList<string> expected,
        DateTimeOffset now)
    {
        if (expected is not { Count: > 0 })
            return;
        if (m.Status != ChatMessageStatus.Sent)
            return;

        if (expected.Count == 1)
        {
            m.Status = ChatMessageStatus.Delivered;
            m.UpdatedAtUtc = now;
            return;
        }

        var receipts = ChatGroupReceiptsJsonUtil.Parse(m.GroupReceiptsJson);
        if (receipts.ExpectedRecipientIds.Count == 0)
            receipts.ExpectedRecipientIds = new List<string>(expected);
        foreach (var e in expected)
        {
            if (string.IsNullOrWhiteSpace(e))
                continue;
            var canonical = CanonicalRecipientId((e ?? "").Trim(), expected);
            if (receipts.DeliveredUserIds.Any(
                    d => string.Equals((d ?? "").Trim(), canonical, StringComparison.Ordinal)))
                continue;
            receipts.DeliveredUserIds.Add(canonical);
        }

        m.GroupReceiptsJson = ChatGroupReceiptsJsonUtil.Serialize(receipts);
        var allD = expected.All(
            e2 => receipts.DeliveredUserIds.Any(
                d => string.Equals((d ?? "").Trim(), (e2 ?? "").Trim(), StringComparison.Ordinal)));
        var allR = expected.All(
            e2 => receipts.ReadUserIds.Any(
                d => string.Equals((d ?? "").Trim(), (e2 ?? "").Trim(), StringComparison.Ordinal)));
        if (allR)
        {
            m.Status = ChatMessageStatus.Read;
            m.UpdatedAtUtc = now;
        }
        else if (allD)
        {
            m.Status = ChatMessageStatus.Delivered;
            m.UpdatedAtUtc = now;
        }
    }
}

internal static class ChatPostPayloadValidation
{
    public static bool TryParseAndValidateImagePayload(
        PostChatMessageBody b,
        out IReadOnlyList<ChatImageDto>? images,
        out string? caption,
        out ChatEmbeddedAudioDto? embeddedAudio)
    {
        images = null;
        caption = null;
        embeddedAudio = null;
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

        images = b.Images;
        caption = b.Caption;
        embeddedAudio = b.EmbeddedAudio;
        return true;
    }

    public static bool TryParseAndValidateDocsBundlePayload(
        PostChatMessageBody b,
        out IReadOnlyList<ChatDocumentDto>? documents,
        out string? caption,
        out ChatEmbeddedAudioDto? embeddedAudio)
    {
        documents = null;
        caption = null;
        embeddedAudio = null;
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

        documents = b.Documents;
        caption = b.Caption;
        embeddedAudio = b.EmbeddedAudio;
        return true;
    }
}

public static class ChatReplyToIdsFromPayload
{
    public static IReadOnlyList<string>? ReadList(PostChatMessageBody body) =>
        body.ReplyToIds is { Count: > 0 } l ? l : null;
}

public static class ChatThreadAccess
{
    private static string NormId(string? s) => (s ?? "").Trim();

    public static bool UserCanSeeThread(string userId, ChatThreadRow t) =>
        t.DeletedAtUtc is null
        && (NormId(t.InitiatorUserId) == NormId(userId)
            || (t.IsSocialGroup
                && (NormId(t.BuyerUserId) == NormId(userId)
                    || NormId(t.SellerUserId) == NormId(userId)))
            || (t.FirstMessageSentAtUtc is not null
                && (NormId(t.BuyerUserId) == NormId(userId)
                    || NormId(t.SellerUserId) == NormId(userId))));

    public static bool UserIdsMatchLoose(string viewerId, string? storedCarrierId)
    {
        var a = (viewerId ?? "").Trim();
        var b = (storedCarrierId ?? "").Trim();
        if (a.Length == 0 || b.Length == 0)
            return false;
        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;
        static string Digits(string s) => string.Concat(s.Where(char.IsDigit));
        var da = Digits(a);
        var db = Digits(b);
        return da.Length >= 6 && db.Length >= 6 && string.Equals(da, db, StringComparison.Ordinal);
    }
}

public static class ChatThreadSummaryMapper
{
    public static ChatThreadSummaryDto ToDto(
        ChatThreadRow t,
        ChatMessageRow? lastMsg,
        string? buyerDisplayName,
        string? buyerAvatarUrl)
    {
        var pv = lastMsg is not null
            ? ChatMessagePreviewText.FromPayload(lastMsg.Payload)
            : (string?)null;
        var bdn = string.IsNullOrWhiteSpace(buyerDisplayName) ? null : buyerDisplayName.Trim();
        var bav = string.IsNullOrWhiteSpace(buyerAvatarUrl) ? null : buyerAvatarUrl.Trim();

        return new ChatThreadSummaryDto(
            t.Id,
            t.OfferId,
            t.StoreId,
            t.CreatedAtUtc,
            lastMsg?.CreatedAtUtc,
            pv,
            t.PurchaseMode,
            t.BuyerUserId,
            t.SellerUserId,
            bdn,
            bav,
            string.IsNullOrWhiteSpace(t.PartyExitedUserId) ? null : t.PartyExitedUserId.Trim(),
            string.IsNullOrWhiteSpace(t.PartyExitedReason) ? null : t.PartyExitedReason.Trim(),
            t.PartyExitedAtUtc,
            t.IsSocialGroup,
            string.IsNullOrWhiteSpace(t.SocialGroupTitle) ? null : t.SocialGroupTitle.Trim());
    }
}

internal static class RouteSheetPayloadPersistence
{
    public static RouteSheetPayload ClonePayloadForEfUpdate(RouteSheetPayload payload) =>
        JsonSerializer.Deserialize<RouteSheetPayload>(
            JsonSerializer.Serialize(payload, RouteSheetJson.Options),
            RouteSheetJson.Options) ?? payload;

    public static void ApplyPayloadAndTouch(ChatRouteSheetRow row, RouteSheetPayload mutatingPayload, DateTimeOffset now)
    {
        row.Payload = ClonePayloadForEfUpdate(mutatingPayload);
        row.UpdatedAtUtc = now;
    }
}

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

        return m.Payload is ChatUnifiedMessagePayload p
            ? MapUnified(m.Id, from, at, read, statusStr, p)
            : TextFallback(m.Id, from, at, read, statusStr, "");
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
        if (payload is ChatUnifiedMessagePayload { RepliesTo: { Count: > 0 } quotes })
        {
            return quotes
                .Select(static q => q.MessageId)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();
        }

        return null;
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

/// <summary>Serialización compartida para <see cref="ChatMessagePayload"/> (DB, API).</summary>
public static class ChatMessageJson
{
    /// <summary>Web defaults sin converter recursivo: usado al serializar tipos concretos de payload.</summary>
    internal static readonly JsonSerializerOptions ConcreteDeserializeOptions = new(JsonSerializerDefaults.Web);

    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);

    public static ChatMessagePayload DeserializePayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ChatUnifiedMessagePayload { Text = "" };

        try
        {
            return JsonSerializer.Deserialize<ChatMessagePayload>(json, Options)
                ?? new ChatUnifiedMessagePayload { Text = "" };
        }
        catch
        {
            return new ChatUnifiedMessagePayload { Text = "" };
        }
    }
}

/// <summary>
/// Serializa y deserializa <see cref="ChatUnifiedMessagePayload"/> en <c>PayloadJson</c> (jsonb).
/// Sin discriminador <c>type</c> en JSON: el cuerpo es el propio objeto unificado.
/// </summary>
internal sealed class ChatMessagePayloadJsonConverter : JsonConverter<ChatMessagePayload>
{
    private static readonly JsonSerializerOptions SerializeOptions = ChatMessageJson.ConcreteDeserializeOptions;

    public override ChatMessagePayload Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        try
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return EmptyFallback();

            var json = doc.RootElement.GetRawText();
            var x = JsonSerializer.Deserialize<ChatUnifiedMessagePayload>(json, SerializeOptions);
            return x ?? EmptyFallback();
        }
        catch
        {
            return EmptyFallback();
        }
    }

    public override void Write(Utf8JsonWriter writer, ChatMessagePayload value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var unified = value as ChatUnifiedMessagePayload ?? EmptyFallback();
        JsonSerializer.Serialize(writer, unified, typeof(ChatUnifiedMessagePayload), SerializeOptions);
    }

    private static ChatUnifiedMessagePayload EmptyFallback() => new() { Text = "" };
}
