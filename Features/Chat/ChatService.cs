using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Recommendations;

namespace VibeTrade.Backend.Features.Chat;

public sealed class ChatService(AppDbContext db, IHubContext<ChatHub> hub) : IChatService
{
    /// <summary>
    /// Grupo SignalR por usuario (todos los clientes del participante). Usado para eventos de chat
    /// aunque el cliente haya salido del grupo del hilo (<c>thread:*</c>).
    /// </summary>
    private static string HubUserGroup(string userId) => $"user:{userId}";

    private async Task HubSendToThreadParticipantsAsync(
        ChatThreadRow thread,
        string method,
        object payload,
        CancellationToken cancellationToken)
    {
        await hub.Clients.Group(HubUserGroup(thread.BuyerUserId)).SendAsync(method, payload, cancellationToken);
        await hub.Clients.Group(HubUserGroup(thread.SellerUserId)).SendAsync(method, payload, cancellationToken);
    }

    public static bool UserCanSeeThread(string userId, ChatThreadRow t) =>
        t.DeletedAtUtc is null
        && (t.InitiatorUserId == userId
            || (t.FirstMessageSentAtUtc is not null
                && (t.BuyerUserId == userId || t.SellerUserId == userId)));

    public async Task<bool> IsUserSellerForOfferAsync(
        string userId,
        string offerId,
        CancellationToken cancellationToken = default)
    {
        var (_, sellerUserId) = await ResolveOfferStoreAsync((offerId ?? "").Trim(), cancellationToken);
        return sellerUserId is not null && sellerUserId == userId;
    }

    public async Task<string?> GetSellerUserIdForOfferAsync(
        string offerId,
        CancellationToken cancellationToken = default)
    {
        var (_, sellerUserId) = await ResolveOfferStoreAsync((offerId ?? "").Trim(), cancellationToken);
        return string.IsNullOrWhiteSpace(sellerUserId) ? null : sellerUserId.Trim();
    }

    public async Task NotifyOfferCommentAsync(
        string recipientUserId,
        string offerId,
        string textPreview,
        string authorLabel,
        int authorTrust,
        string senderUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipientUserId))
            return;

        var preview = textPreview.Length > 500 ? textPreview[..500] + "…" : textPreview;
        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        var rid = recipientUserId.Trim();
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = rid,
            ThreadId = null,
            MessageId = null,
            OfferId = offerId,
            MessagePreview = preview,
            AuthorStoreName = authorLabel,
            AuthorTrustScore = authorTrust,
            SenderUserId = senderUserId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ReadAtUtc = null,
            Kind = "offer_comment",
        });
        await db.SaveChangesAsync(cancellationToken);

        await hub.Clients.Group(HubUserGroup(rid)).SendAsync(
            "notificationCreated",
            new { kind = "offer_comment", offerId },
            cancellationToken);
    }

    public async Task NotifyOfferLikeAsync(
        string sellerUserId,
        string offerId,
        string likerLabel,
        int likerTrust,
        string likerSenderUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sellerUserId))
            return;

        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        var rid = sellerUserId.Trim();
        var oid = (offerId ?? "").Trim();
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = rid,
            ThreadId = null,
            MessageId = null,
            OfferId = oid,
            MessagePreview = "Le dio me gusta a tu oferta.",
            AuthorStoreName = likerLabel,
            AuthorTrustScore = likerTrust,
            SenderUserId = likerSenderUserId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ReadAtUtc = null,
            Kind = "offer_like",
        });
        await db.SaveChangesAsync(cancellationToken);

        await hub.Clients.Group(HubUserGroup(rid)).SendAsync(
            "notificationCreated",
            new { kind = "offer_like", offerId = oid },
            cancellationToken);
    }

    public async Task NotifyQaCommentLikeAsync(
        string commentAuthorUserId,
        string offerId,
        string likerLabel,
        int likerTrust,
        string likerSenderUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commentAuthorUserId))
            return;

        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        var rid = commentAuthorUserId.Trim();
        var oid = (offerId ?? "").Trim();
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = rid,
            ThreadId = null,
            MessageId = null,
            OfferId = oid,
            MessagePreview = "Le dio me gusta a tu comentario.",
            AuthorStoreName = likerLabel,
            AuthorTrustScore = likerTrust,
            SenderUserId = likerSenderUserId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ReadAtUtc = null,
            Kind = "qa_comment_like",
        });
        await db.SaveChangesAsync(cancellationToken);

        await hub.Clients.Group(HubUserGroup(rid)).SendAsync(
            "notificationCreated",
            new { kind = "qa_comment_like", offerId = oid },
            cancellationToken);
    }

    public Task BroadcastOfferCommentsUpdatedAsync(string offerId, CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return Task.CompletedTask;
        return hub.Clients.Group(HubOfferGroup(oid)).SendAsync(
            "offerCommentsUpdated",
            new { offerId = oid },
            cancellationToken);
    }

    private static string HubOfferGroup(string offerId) => $"offer:{offerId}";

    private async Task<(string? storeId, string? sellerUserId)> ResolveOfferStoreAsync(
        string offerId,
        CancellationToken cancellationToken)
    {
        if (RecommendationBatchOfferLoader.IsEmergentPublicationId(offerId))
        {
            var em = await db.EmergentOffers.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == offerId && x.RetractedAtUtc == null, cancellationToken);
            if (em is null)
                return (null, null);
            return await ResolveOfferStoreAsync(em.OfferId, cancellationToken);
        }

        var p = await db.StoreProducts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == offerId, cancellationToken);
        if (p is not null)
        {
            var st = await db.Stores.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == p.StoreId, cancellationToken);
            if (st is null)
                return (null, null);
            var owner = string.IsNullOrWhiteSpace(st.OwnerUserId) ? null : st.OwnerUserId.Trim();
            return (p.StoreId, owner);
        }

        var s = await db.StoreServices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == offerId, cancellationToken);
        if (s is null)
            return (null, null);
        var st2 = await db.Stores.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == s.StoreId, cancellationToken);
        if (st2 is null)
            return (null, null);
        var owner2 = string.IsNullOrWhiteSpace(st2.OwnerUserId) ? null : st2.OwnerUserId.Trim();
        return (s.StoreId, owner2);
    }

    /// <summary>
    /// Compradores solo pueden crear un hilo nuevo si el producto/servicio de catálogo está publicado.
    /// Publicaciones <c>emo_*</c> se resuelven a la oferta base del hilo.
    /// </summary>
    private async Task<bool> OfferIsListedForBuyerChatAsync(
        string offerId,
        CancellationToken cancellationToken)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return false;

        if (RecommendationBatchOfferLoader.IsEmergentPublicationId(oid))
        {
            var em = await db.EmergentOffers.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == oid && x.RetractedAtUtc == null, cancellationToken);
            if (em is null)
                return false;
            return await OfferIsListedForBuyerChatAsync(em.OfferId, cancellationToken);
        }

        var p = await db.StoreProducts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == oid, cancellationToken);
        if (p is not null)
            return p.Published;

        var s = await db.StoreServices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == oid, cancellationToken);
        if (s is not null)
            return s.Published != false;

        return false;
    }

    private static ChatThreadDto MapThread(
        ChatThreadRow t,
        string? buyerDisplayName = null,
        string? buyerAvatarUrl = null) => new(
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
        buyerAvatarUrl);

    private sealed record BuyerPublicFields(string? DisplayName, string? AvatarUrl);

    private async Task<BuyerPublicFields> GetBuyerPublicFieldsAsync(
        string buyerUserId,
        CancellationToken cancellationToken)
    {
        var row = await db.UserAccounts.AsNoTracking()
            .Where(u => u.Id == buyerUserId)
            .Select(u => new { u.DisplayName, u.AvatarUrl })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
            return new BuyerPublicFields(null, null);
        var dn = string.IsNullOrWhiteSpace(row.DisplayName) ? null : row.DisplayName.Trim();
        var av = string.IsNullOrWhiteSpace(row.AvatarUrl) ? null : row.AvatarUrl.Trim();
        return new BuyerPublicFields(dn, av);
    }

    private async Task<ChatThreadDto> MapThreadWithBuyerLabelAsync(
        ChatThreadRow t,
        CancellationToken cancellationToken)
    {
        var f = await GetBuyerPublicFieldsAsync(t.BuyerUserId, cancellationToken);
        return MapThread(t, f.DisplayName, f.AvatarUrl);
    }

    private static ChatMessageDto MapMessage(ChatMessageRow m, string? senderDisplayLabel = null)
    {
        return new ChatMessageDto(
            m.Id,
            m.ThreadId,
            m.SenderUserId,
            m.Payload,
            m.Status,
            m.CreatedAtUtc,
            m.UpdatedAtUtc,
            senderDisplayLabel);
    }

    public async Task<ChatThreadDto?> CreateOrGetThreadForBuyerAsync(
        string buyerUserId,
        string offerId,
        bool purchaseIntent = true,
        CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 4)
            return null;

        var (storeId, sellerUserId) = await ResolveOfferStoreAsync(oid, cancellationToken);
        if (storeId is null || sellerUserId is null)
            return null;

        if (buyerUserId == sellerUserId)
            return null;

        var existing = await db.ChatThreads
            .FirstOrDefaultAsync(
                x => x.OfferId == oid && x.BuyerUserId == buyerUserId && x.DeletedAtUtc == null,
                cancellationToken);
        if (existing is not null)
        {
            if (purchaseIntent && !existing.PurchaseMode)
            {
                existing.PurchaseMode = true;
                await db.SaveChangesAsync(cancellationToken);
            }

            return await MapThreadWithBuyerLabelAsync(existing, cancellationToken);
        }

        if (!await OfferIsListedForBuyerChatAsync(oid, cancellationToken))
            return null;

        var id = "cth_" + Guid.NewGuid().ToString("N")[..16];
        var now = DateTimeOffset.UtcNow;
        var row = new ChatThreadRow
        {
            Id = id,
            OfferId = oid,
            StoreId = storeId,
            BuyerUserId = buyerUserId,
            SellerUserId = sellerUserId,
            InitiatorUserId = buyerUserId,
            FirstMessageSentAtUtc = null,
            CreatedAtUtc = now,
            PurchaseMode = purchaseIntent,
        };
        db.ChatThreads.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        var created = await MapThreadWithBuyerLabelAsync(row, cancellationToken);
        await NotifyThreadCreatedAsync(created, cancellationToken);
        return created;
    }

    /// <summary>
    /// Solo el comprador recibe el hilo al crearlo; el vendedor lo recibe tras el primer mensaje
    /// (<see cref="InsertChatMessageAsync"/>).
    /// </summary>
    private async Task NotifyThreadCreatedAsync(ChatThreadDto dto, CancellationToken cancellationToken)
    {
        await hub.Clients.Group($"user:{dto.BuyerUserId}").SendAsync(
            "threadCreated",
            new { thread = dto },
            cancellationToken);
    }

    public async Task<ChatThreadDto?> GetThreadIfVisibleAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || !UserCanSeeThread(userId, t))
            return null;
        return await MapThreadWithBuyerLabelAsync(t, cancellationToken);
    }

    public async Task<ChatThreadDto?> GetThreadByOfferIfVisibleAsync(
        string userId,
        string offerId,
        CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        ChatThreadRow? t;
        var (_, sellerUserId) = await ResolveOfferStoreAsync(oid, cancellationToken);
        if (sellerUserId is null)
            return null;

        if (userId == sellerUserId)
        {
            t = await db.ChatThreads.AsNoTracking()
                .Where(x => x.OfferId == oid && x.SellerUserId == sellerUserId && x.DeletedAtUtc == null)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            t = await db.ChatThreads.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.OfferId == oid && x.BuyerUserId == userId && x.DeletedAtUtc == null,
                    cancellationToken);
        }

        if (t is null || !UserCanSeeThread(userId, t))
            return null;
        return await MapThreadWithBuyerLabelAsync(t, cancellationToken);
    }

    public async Task<IReadOnlyList<ChatThreadSummaryDto>> ListThreadsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var threads = await db.ChatThreads.AsNoTracking()
            .Where(t =>
                t.DeletedAtUtc == null
                && (t.InitiatorUserId == userId
                    || (t.FirstMessageSentAtUtc != null
                        && (t.BuyerUserId == userId || t.SellerUserId == userId))))
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var ids = threads.Select(t => t.Id).ToList();
        if (ids.Count == 0)
            return Array.Empty<ChatThreadSummaryDto>();

        var allMsgs = await db.ChatMessages.AsNoTracking()
            .Where(m => ids.Contains(m.ThreadId) && m.DeletedAtUtc == null)
            .ToListAsync(cancellationToken);
        var lastPerThread = allMsgs
            .GroupBy(m => m.ThreadId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAtUtc).First());

        var buyerIds = threads
            .Select(x => x.BuyerUserId)
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();
        var buyerRows = await db.UserAccounts.AsNoTracking()
            .Where(u => buyerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.AvatarUrl })
            .ToListAsync(cancellationToken);
        var buyerById = buyerRows.ToDictionary(x => x.Id);

        return threads.Select(t =>
        {
            lastPerThread.TryGetValue(t.Id, out var lastMsg);
            string? pv = null;
            if (lastMsg is not null)
                pv = PreviewFromPayload(lastMsg.Payload);
            string? bdn = null;
            string? bav = null;
            if (buyerById.TryGetValue(t.BuyerUserId, out var bu))
            {
                bdn = string.IsNullOrWhiteSpace(bu.DisplayName) ? null : bu.DisplayName.Trim();
                bav = string.IsNullOrWhiteSpace(bu.AvatarUrl) ? null : bu.AvatarUrl.Trim();
            }

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
                bav);
        }).ToList();
    }

    private static string PreviewFromPayload(ChatMessagePayload payload)
    {
        return payload switch
        {
            ChatTextPayload p => PreviewText(p.Text),
            ChatAudioPayload => "Nota de voz",
            ChatImagePayload p => string.IsNullOrWhiteSpace(p.Caption) ? "Foto" : p.Caption!.Trim(),
            ChatDocPayload p => string.IsNullOrWhiteSpace(p.Name) ? "Documento" : p.Name.Trim(),
            ChatDocsBundlePayload p => p.Documents.Count switch
            {
                0 => "Documento",
                1 => string.IsNullOrWhiteSpace(p.Documents[0].Name) ? "Documento" : p.Documents[0].Name.Trim(),
                var n => $"{n} documentos",
            },
            ChatAgreementPayload p => string.IsNullOrWhiteSpace(p.Title)
                ? "Acuerdo"
                : $"Acuerdo: {p.Title.Trim()}",
            ChatSystemTextPayload p => PreviewText(p.Text),
            ChatCertificatePayload p => string.IsNullOrWhiteSpace(p.Title)
                ? "Certificado"
                : p.Title.Trim(),
            _ => "Mensaje",
        };

        static string PreviewText(string tx)
        {
            tx = tx.Trim();
            return tx.Length == 0 ? "Mensaje" : tx;
        }
    }

    public async Task<IReadOnlyList<ChatMessageDto>> ListMessagesAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !UserCanSeeThread(userId, t))
            return Array.Empty<ChatMessageDto>();

        var msgs = await db.ChatMessages.AsNoTracking()
            .Where(m => m.ThreadId == threadId && m.DeletedAtUtc == null)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var labelCache = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var uid in msgs.Select(m => m.SenderUserId).Distinct())
        {
            labelCache[uid] = await GetParticipantAuthorLabelAsync(t, uid, cancellationToken);
        }

        return msgs.Select(m => MapMessage(m, labelCache[m.SenderUserId])).ToList();
    }

    private static bool IsAllowedPersistedMediaUrl(string url)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0 || !url.StartsWith("/", StringComparison.Ordinal))
            return false;
        if (url.Contains("..", StringComparison.Ordinal))
            return false;
        return url.StartsWith("/api/v1/media/", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string>? ReadReplyToIds(JsonElement payload)
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

    private async Task<IReadOnlyList<ReplyQuoteDto>?> BuildReplyQuotesAsync(
        ChatThreadRow thread,
        JsonElement requestPayload,
        CancellationToken cancellationToken)
    {
        var ids = ReadReplyToIds(requestPayload);
        if (ids is null || ids.Count == 0)
            return null;

        var list = new List<ReplyQuoteDto>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            var row = await db.ChatMessages.AsNoTracking()
                .FirstOrDefaultAsync(
                    m => m.Id == id && m.ThreadId == thread.Id && m.DeletedAtUtc == null,
                    cancellationToken);
            if (row is null)
                continue;
            var author = await GetParticipantAuthorLabelAsync(thread, row.SenderUserId, cancellationToken);
            var preview = PreviewFromPayload(row.Payload);
            list.Add(new ReplyQuoteDto
            {
                MessageId = row.Id,
                Author = author,
                Preview = preview,
                AtUtc = row.CreatedAtUtc,
            });
        }

        return list.Count == 0 ? null : list;
    }

    private async Task<string> GetParticipantAuthorLabelAsync(
        ChatThreadRow thread,
        string senderUserId,
        CancellationToken cancellationToken)
    {
        if (senderUserId == thread.BuyerUserId)
        {
            var acc = await db.UserAccounts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == senderUserId, cancellationToken);
            return string.IsNullOrWhiteSpace(acc?.DisplayName)
                ? "Comprador"
                : acc!.DisplayName.Trim();
        }

        if (senderUserId == thread.SellerUserId)
        {
            var store = await db.Stores.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == thread.StoreId, cancellationToken);
            return string.IsNullOrWhiteSpace(store?.Name)
                ? "Tienda"
                : store!.Name.Trim();
        }

        return "Participante";
    }

    private async Task<ChatMessageDto> InsertChatMessageAsync(
        ChatThreadRow thread,
        string senderUserId,
        ChatMessagePayload payloadObj,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var sellerHadNotSeenThreadYet = thread.FirstMessageSentAtUtc is null;
        if (thread.FirstMessageSentAtUtc is null)
            thread.FirstMessageSentAtUtc = now;

        var msgId = "cmg_" + Guid.NewGuid().ToString("N")[..16];
        var row = new ChatMessageRow
        {
            Id = msgId,
            ThreadId = thread.Id,
            SenderUserId = senderUserId,
            Payload = payloadObj,
            Status = ChatMessageStatus.Sent,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.ChatMessages.Add(row);

        var recipientId = senderUserId == thread.BuyerUserId ? thread.SellerUserId : thread.BuyerUserId;
        var preview = PreviewFromPayload(payloadObj);
        await NotifyRecipientAsync(recipientId, thread, row, preview, senderUserId, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        // El vendedor no recibe threadCreated al pulsar «Comprar»; solo cuando hay al menos un mensaje.
        if (sellerHadNotSeenThreadYet)
        {
            var threadDto = await MapThreadWithBuyerLabelAsync(thread, cancellationToken);
            await hub.Clients.Group(HubUserGroup(thread.SellerUserId)).SendAsync(
                "threadCreated",
                new { thread = threadDto },
                cancellationToken);
        }

        var senderLabel = await GetParticipantAuthorLabelAsync(thread, senderUserId, cancellationToken);
        var dto = MapMessage(row, senderLabel);
        await HubSendToThreadParticipantsAsync(
            thread,
            "messageCreated",
            new { message = dto },
            cancellationToken);
        return dto;
    }

    public async Task<ChatMessageDto?> PostMessageAsync(
        string senderUserId,
        string threadId,
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !UserCanSeeThread(senderUserId, t))
            return null;

        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("type", out var typeEl)
            || typeEl.ValueKind != JsonValueKind.String)
            return null;

        var type = typeEl.GetString();
        if (string.IsNullOrEmpty(type))
            return null;

        if (senderUserId != t.BuyerUserId && senderUserId != t.SellerUserId)
            return null;

        return type switch
        {
            "text" => await PostTextChatMessageAsync(senderUserId, t, payload, cancellationToken),
            "audio" => await PostAudioChatMessageAsync(senderUserId, t, payload, cancellationToken),
            "image" => await PostImageChatMessageAsync(senderUserId, t, payload, cancellationToken),
            "doc" => await PostSingleDocChatMessageAsync(senderUserId, t, payload, cancellationToken),
            "docs" => await PostDocsBundleChatMessageAsync(senderUserId, t, payload, cancellationToken),
            _ => null,
        };
    }

    private async Task<ChatMessageDto?> PostTextChatMessageAsync(
        string senderUserId,
        ChatThreadRow t,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        if (!payload.TryGetProperty("text", out var textEl))
            return null;
        var text = (textEl.GetString() ?? "").Trim();
        if (text.Length == 0 || text.Length > 12_000)
            return null;

        string? offerQaId = null;
        if (payload.TryGetProperty("offerQaId", out var oqEl) && oqEl.ValueKind == JsonValueKind.String)
        {
            var oqs = oqEl.GetString();
            if (!string.IsNullOrWhiteSpace(oqs))
                offerQaId = oqs!.Trim();
        }

        var quotes = await BuildReplyQuotesAsync(t, payload, cancellationToken);
        var payloadObj = new ChatTextPayload
        {
            Text = text,
            OfferQaId = offerQaId,
            ReplyQuotes = quotes,
        };
        return await InsertChatMessageAsync(t, senderUserId, payloadObj, cancellationToken);
    }

    private async Task<ChatMessageDto?> PostAudioChatMessageAsync(
        string senderUserId,
        ChatThreadRow t,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        if (!payload.TryGetProperty("url", out var urlEl) || urlEl.ValueKind != JsonValueKind.String)
            return null;
        var url = urlEl.GetString() ?? "";
        if (!IsAllowedPersistedMediaUrl(url))
            return null;

        if (!payload.TryGetProperty("seconds", out var secEl))
            return null;
        var seconds = secEl.TryGetInt32(out var s) ? s : 0;
        if (seconds is < 1 or > 3600)
            return null;

        var quotes = await BuildReplyQuotesAsync(t, payload, cancellationToken);
        var payloadObj = new ChatAudioPayload
        {
            Url = url,
            Seconds = seconds,
            ReplyQuotes = quotes,
        };
        return await InsertChatMessageAsync(t, senderUserId, payloadObj, cancellationToken);
    }

    private async Task<ChatMessageDto?> PostImageChatMessageAsync(
        string senderUserId,
        ChatThreadRow t,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        ChatImagePayload parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ChatImagePayload>(payload.GetRawText(), ChatMessageJson.Options)
                     ?? throw new JsonException();
        }
        catch
        {
            return null;
        }

        if (parsed.Images.Count == 0)
            return null;

        foreach (var img in parsed.Images)
        {
            if (!IsAllowedPersistedMediaUrl(img.Url))
                return null;
        }

        if (parsed.EmbeddedAudio is not null)
        {
            if (!IsAllowedPersistedMediaUrl(parsed.EmbeddedAudio.Url))
                return null;
            if (parsed.EmbeddedAudio.Seconds is < 1 or > 3600)
                return null;
        }

        if (parsed.Caption is { Length: > 4000 })
            return null;

        var quotes = await BuildReplyQuotesAsync(t, payload, cancellationToken);
        var payloadObj = new ChatImagePayload
        {
            Images = parsed.Images,
            Caption = parsed.Caption,
            EmbeddedAudio = parsed.EmbeddedAudio,
            ReplyQuotes = quotes,
        };
        return await InsertChatMessageAsync(t, senderUserId, payloadObj, cancellationToken);
    }

    private async Task<ChatMessageDto?> PostSingleDocChatMessageAsync(
        string senderUserId,
        ChatThreadRow t,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        ChatDocPayload parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ChatDocPayload>(payload.GetRawText(), ChatMessageJson.Options)
                     ?? throw new JsonException();
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(parsed.Name) || parsed.Name.Length > 500)
            return null;

        if (parsed.Url is not null && !IsAllowedPersistedMediaUrl(parsed.Url))
            return null;

        if (parsed.Kind is not ("pdf" or "doc" or "other"))
            return null;

        if (parsed.Caption is { Length: > 4000 })
            return null;

        var quotes = await BuildReplyQuotesAsync(t, payload, cancellationToken);
        var payloadObj = new ChatDocPayload
        {
            Name = parsed.Name.Trim(),
            Size = parsed.Size.Trim(),
            Kind = parsed.Kind,
            Url = parsed.Url,
            Caption = parsed.Caption,
            ReplyQuotes = quotes,
        };
        return await InsertChatMessageAsync(t, senderUserId, payloadObj, cancellationToken);
    }

    private async Task<ChatMessageDto?> PostDocsBundleChatMessageAsync(
        string senderUserId,
        ChatThreadRow t,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        ChatDocsBundlePayload parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ChatDocsBundlePayload>(payload.GetRawText(), ChatMessageJson.Options)
                     ?? throw new JsonException();
        }
        catch
        {
            return null;
        }

        if (parsed.Documents.Count == 0)
            return null;

        foreach (var d in parsed.Documents)
        {
            if (string.IsNullOrWhiteSpace(d.Name))
                return null;
            if (d.Url is not null && !IsAllowedPersistedMediaUrl(d.Url))
                return null;
        }

        if (parsed.EmbeddedAudio is not null)
        {
            if (!IsAllowedPersistedMediaUrl(parsed.EmbeddedAudio.Url))
                return null;
            if (parsed.EmbeddedAudio.Seconds is < 1 or > 3600)
                return null;
        }

        if (parsed.Caption is { Length: > 4000 })
            return null;

        var quotes = await BuildReplyQuotesAsync(t, payload, cancellationToken);
        var payloadObj = new ChatDocsBundlePayload
        {
            Documents = parsed.Documents,
            Caption = parsed.Caption,
            EmbeddedAudio = parsed.EmbeddedAudio,
            ReplyQuotes = quotes,
        };
        return await InsertChatMessageAsync(t, senderUserId, payloadObj, cancellationToken);
    }

    public async Task<ChatMessageDto?> UpdateMessageStatusAsync(
        string userId,
        string threadId,
        string messageId,
        ChatMessageStatus status,
        CancellationToken cancellationToken = default)
    {
        if (status is not (ChatMessageStatus.Delivered or ChatMessageStatus.Read))
            return null;

        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !UserCanSeeThread(userId, t))
            return null;

        var m = await db.ChatMessages.FirstOrDefaultAsync(
            x => x.Id == messageId && x.ThreadId == threadId && x.DeletedAtUtc == null,
            cancellationToken);
        if (m is null)
            return null;

        var recipientId = m.SenderUserId == t.BuyerUserId ? t.SellerUserId : t.BuyerUserId;
        if (userId != recipientId)
            return null;

        var now = DateTimeOffset.UtcNow;
        if (status == ChatMessageStatus.Delivered)
        {
            if (m.Status == ChatMessageStatus.Delivered || m.Status == ChatMessageStatus.Read)
                return MapMessage(m);
            if (m.Status != ChatMessageStatus.Sent)
                return null;
            m.Status = ChatMessageStatus.Delivered;
            m.UpdatedAtUtc = now;
        }
        else
        {
            if (m.Status == ChatMessageStatus.Read)
                return MapMessage(m);
            if (m.Status is not (ChatMessageStatus.Sent or ChatMessageStatus.Delivered))
                return null;
            m.Status = ChatMessageStatus.Read;
            m.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        var dto = MapMessage(m);
        await HubSendToThreadParticipantsAsync(
            t,
            "messageStatusChanged",
            new
            {
                messageId = m.Id,
                threadId,
                status = status == ChatMessageStatus.Delivered ? "delivered" : "read",
                updatedAtUtc = m.UpdatedAtUtc,
            },
            cancellationToken);
        return dto;
    }

    private async Task NotifyRecipientAsync(
        string recipientUserId,
        ChatThreadRow thread,
        ChatMessageRow message,
        string textPreview,
        string senderUserId,
        CancellationToken cancellationToken)
    {
        var store = await db.Stores.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == thread.StoreId, cancellationToken);
        var senderAccount = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == senderUserId, cancellationToken);

        string authorLabel;
        int trust;
        if (senderUserId == thread.SellerUserId && store is not null)
        {
            authorLabel = store.Name;
            trust = store.TrustScore;
        }
        else
        {
            authorLabel = string.IsNullOrWhiteSpace(senderAccount?.DisplayName)
                ? "Comprador"
                : senderAccount!.DisplayName.Trim();
            trust = senderAccount?.TrustScore ?? 0;
        }

        var preview = textPreview.Length > 500 ? textPreview[..500] + "…" : textPreview;
        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = recipientUserId,
            ThreadId = thread.Id,
            MessageId = message.Id,
            MessagePreview = preview,
            AuthorStoreName = authorLabel,
            AuthorTrustScore = trust,
            SenderUserId = senderUserId,
            CreatedAtUtc = message.CreatedAtUtc,
            ReadAtUtc = null,
        });
    }

    public async Task<IReadOnlyList<ChatNotificationDto>> ListNotificationsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var list = await db.ChatNotifications.AsNoTracking()
            .Where(n => n.RecipientUserId == userId)
            .Where(n =>
                n.OfferId != null && n.ThreadId == null
                || (n.ThreadId != null
                    && db.ChatThreads.Any(t => t.Id == n.ThreadId && t.DeletedAtUtc == null)))
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        return list.Select(n => new ChatNotificationDto(
            n.Id,
            n.ThreadId,
            n.MessageId,
            n.OfferId,
            n.MessagePreview,
            n.AuthorStoreName,
            n.AuthorTrustScore,
            n.SenderUserId,
            n.CreatedAtUtc,
            n.ReadAtUtc,
            n.Kind)).ToList();
    }

    public async Task MarkNotificationsReadAsync(
        string userId,
        IReadOnlyList<string>? notificationIds,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (notificationIds is { Count: > 0 })
        {
            var rows = await db.ChatNotifications
                .Where(n => n.RecipientUserId == userId && notificationIds.Contains(n.Id))
                .ToListAsync(cancellationToken);
            foreach (var r in rows)
                r.ReadAtUtc = now;
        }
        else
        {
            var rows = await db.ChatNotifications
                .Where(n => n.RecipientUserId == userId && n.ReadAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var r in rows)
                r.ReadAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteThreadAsync(string userId, string threadId, CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        if (tid.Length < 4)
            return false;

        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !UserCanSeeThread(userId, t))
            return false;

        var now = DateTimeOffset.UtcNow;
        t.DeletedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        await db.ChatMessages
            .Where(m => m.ThreadId == tid)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.DeletedAtUtc, now),
                cancellationToken);

        return true;
    }

    public async Task SyncOfferQaAnswersForOfferAsync(string offerId, CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 4)
            return;

        var qaList = await GetOfferQaListForOfferAsync(oid, cancellationToken);
        if (qaList is null || qaList.Count == 0)
            return;

        var threads = await GetActiveThreadsForOfferAsync(oid, cancellationToken);
        if (threads.Count == 0)
            return;

        var sellerMsgsCache = await BuildSellerOfferQaMessagesCacheAsync(threads, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var hubRows = new List<ChatMessageRow>();

        foreach (var c in qaList)
        {
            if (!TryGetOfferQaSyncEntry(c, out var qaId, out var answer, out var buyerId))
                continue;

            var thread = threads.FirstOrDefault(t => t.BuyerUserId == buyerId);
            if (thread is null)
                continue;

            if (!sellerMsgsCache.TryGetValue(thread.Id, out var sellerMsgs))
                continue;

            if (SellerThreadAlreadyHasOfferQaAnswer(sellerMsgs, qaId))
                continue;

            var row = CreateAndStageOfferQaAnswerMessageRow(thread, qaId, answer, now, sellerMsgs, hubRows);

            await NotifyRecipientAsync(
                thread.BuyerUserId,
                thread,
                row,
                answer,
                thread.SellerUserId,
                cancellationToken);
        }

        if (hubRows.Count == 0)
            return;

        await db.SaveChangesAsync(cancellationToken);

        // Si el primer mensaje del hilo viene de esta sincronización (no de InsertChatMessageAsync), el vendedor aún no recibió threadCreated.
        foreach (var g in hubRows.GroupBy(r => r.ThreadId))
        {
            var tid = g.Key;
            var thread = threads.FirstOrDefault(t => t.Id == tid);
            if (thread is null)
                continue;
            var totalMsgs = await db.ChatMessages.AsNoTracking()
                .CountAsync(m => m.ThreadId == tid && m.DeletedAtUtc == null, cancellationToken);
            if (totalMsgs != g.Count())
                continue;
            var threadDto = await MapThreadWithBuyerLabelAsync(thread, cancellationToken);
            await hub.Clients.Group(HubUserGroup(thread.SellerUserId)).SendAsync(
                "threadCreated",
                new { thread = threadDto },
                cancellationToken);
        }

        await BroadcastNewOfferQaMessagesToHubAsync(hubRows, threads, cancellationToken);
    }

    private async Task<IReadOnlyList<OfferQaComment>?> GetOfferQaListForOfferAsync(
        string offerId,
        CancellationToken cancellationToken)
    {
        var product = await db.StoreProducts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == offerId, cancellationToken);
        if (product is not null)
            return product.OfferQa;

        var service = await db.StoreServices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == offerId, cancellationToken);
        return service?.OfferQa;
    }

    private async Task<List<ChatThreadRow>> GetActiveThreadsForOfferAsync(
        string offerId,
        CancellationToken cancellationToken) =>
        await db.ChatThreads
            .Where(x => x.OfferId == offerId && x.DeletedAtUtc == null)
            .ToListAsync(cancellationToken);

    private async Task<Dictionary<string, List<ChatMessageRow>>> BuildSellerOfferQaMessagesCacheAsync(
        List<ChatThreadRow> threads,
        CancellationToken cancellationToken)
    {
        var sellerMsgsCache = new Dictionary<string, List<ChatMessageRow>>(StringComparer.Ordinal);
        foreach (var th in threads)
        {
            var list = await db.ChatMessages.AsNoTracking()
                .Where(m =>
                    m.ThreadId == th.Id
                    && m.SenderUserId == th.SellerUserId
                    && m.DeletedAtUtc == null)
                .ToListAsync(cancellationToken);
            sellerMsgsCache[th.Id] = list;
        }

        return sellerMsgsCache;
    }

    private static bool TryGetOfferQaSyncEntry(
        OfferQaComment c,
        out string qaId,
        out string answer,
        out string buyerId)
    {
        qaId = (c.Id ?? "").Trim();
        answer = (c.Answer ?? "").Trim();
        buyerId = (c.AskedBy?.Id ?? "").Trim();
        if (string.IsNullOrEmpty(qaId) || answer.Length == 0)
            return false;
        if (string.IsNullOrEmpty(buyerId) || buyerId.Equals("guest", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private bool SellerThreadAlreadyHasOfferQaAnswer(IReadOnlyList<ChatMessageRow> sellerMsgs, string qaId) =>
        sellerMsgs.Any(m => m.Payload is ChatTextPayload text && text.OfferQaId == qaId);

    private ChatMessageRow CreateAndStageOfferQaAnswerMessageRow(
        ChatThreadRow thread,
        string qaId,
        string answer,
        DateTimeOffset now,
        List<ChatMessageRow> sellerMsgs,
        List<ChatMessageRow> hubRows)
    {
        var msgId = "cmg_" + Guid.NewGuid().ToString("N")[..16];
        var payloadObj = new ChatTextPayload
        {
            Text = answer,
            OfferQaId = qaId,
            ReplyQuotes = null,
        };

        if (thread.FirstMessageSentAtUtc is null)
            thread.FirstMessageSentAtUtc = now;

        var row = new ChatMessageRow
        {
            Id = msgId,
            ThreadId = thread.Id,
            SenderUserId = thread.SellerUserId,
            Payload = payloadObj,
            Status = ChatMessageStatus.Sent,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.ChatMessages.Add(row);
        sellerMsgs.Add(row);
        hubRows.Add(row);
        return row;
    }

    private async Task BroadcastNewOfferQaMessagesToHubAsync(
        IReadOnlyList<ChatMessageRow> hubRows,
        IReadOnlyList<ChatThreadRow> threads,
        CancellationToken cancellationToken)
    {
        var byId = threads.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var row in hubRows)
        {
            if (!byId.TryGetValue(row.ThreadId, out var thread))
                continue;
            var senderLabel = await GetParticipantAuthorLabelAsync(thread, row.SenderUserId, cancellationToken);
            var dto = MapMessage(row, senderLabel);
            await HubSendToThreadParticipantsAsync(
                thread,
                "messageCreated",
                new { message = dto },
                cancellationToken);
        }
    }

    public async Task<ChatMessageDto?> PostAgreementAnnouncementAsync(
        string sellerUserId,
        string threadId,
        string agreementId,
        string title,
        string status,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !UserCanSeeThread(sellerUserId, t))
            return null;
        if (sellerUserId != t.SellerUserId)
            return null;
        if (string.IsNullOrWhiteSpace(agreementId) || string.IsNullOrWhiteSpace(title))
            return null;
        if (status is not ("pending_buyer" or "accepted" or "rejected"))
            status = "pending_buyer";

        var payload = new ChatAgreementPayload
        {
            AgreementId = agreementId.Trim(),
            Title = title.Trim(),
            Body = "",
            Status = status,
        };
        return await InsertChatMessageAsync(t, sellerUserId, payload, cancellationToken);
    }

    public async Task<ChatMessageDto?> PostSystemThreadNoticeAsync(
        string actorUserId,
        string threadId,
        string text,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !UserCanSeeThread(actorUserId, t))
            return null;
        if (actorUserId != t.BuyerUserId && actorUserId != t.SellerUserId)
            return null;
        var tx = (text ?? "").Trim();
        if (tx.Length == 0 || tx.Length > 12_000)
            return null;

        var payload = new ChatSystemTextPayload { Text = tx };
        return await InsertChatMessageAsync(t, actorUserId, payload, cancellationToken);
    }
}
