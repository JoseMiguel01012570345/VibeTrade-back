using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Chat;

public sealed class ChatService(AppDbContext db, IHubContext<ChatHub> hub) : IChatService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static bool UserCanSeeThread(string userId, ChatThreadRow t) =>
        t.DeletedAtUtc is null
        && (t.InitiatorUserId == userId
            || (t.FirstMessageSentAtUtc is not null
                && (t.BuyerUserId == userId || t.SellerUserId == userId)));

    private async Task<(string? storeId, string? sellerUserId)> ResolveOfferStoreAsync(
        string offerId,
        CancellationToken cancellationToken)
    {
        var p = await db.StoreProducts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == offerId, cancellationToken);
        if (p is not null)
        {
            var st = await db.Stores.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == p.StoreId, cancellationToken);
            return st is null ? (null, null) : (p.StoreId, st.OwnerUserId);
        }

        var s = await db.StoreServices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == offerId, cancellationToken);
        if (s is null)
            return (null, null);
        var st2 = await db.Stores.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == s.StoreId, cancellationToken);
        return st2 is null ? (null, null) : (s.StoreId, st2.OwnerUserId);
    }

    private static ChatThreadDto MapThread(ChatThreadRow t) => new(
        t.Id,
        t.OfferId,
        t.StoreId,
        t.BuyerUserId,
        t.SellerUserId,
        t.InitiatorUserId,
        t.FirstMessageSentAtUtc,
        t.CreatedAtUtc,
        t.PurchaseMode);

    private static ChatMessageDto MapMessage(ChatMessageRow m)
    {
        var payload = JsonSerializer.Deserialize<ChatTextPayload>(m.PayloadJson, JsonOpts)
                      ?? new ChatTextPayload { Text = "" };
        return new ChatMessageDto(
            m.Id,
            m.ThreadId,
            m.SenderUserId,
            payload,
            m.Status,
            m.CreatedAtUtc,
            m.UpdatedAtUtc);
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

            return MapThread(existing);
        }

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
        var created = MapThread(row);
        await NotifyThreadCreatedAsync(created, cancellationToken);
        return created;
    }

    private async Task NotifyThreadCreatedAsync(ChatThreadDto dto, CancellationToken cancellationToken)
    {
        await hub.Clients.Group($"user:{dto.BuyerUserId}").SendAsync(
            "threadCreated",
            new { thread = dto },
            cancellationToken);
        await hub.Clients.Group($"user:{dto.SellerUserId}").SendAsync(
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
        return MapThread(t);
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
        return MapThread(t);
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

        return threads.Select(t =>
        {
            lastPerThread.TryGetValue(t.Id, out var lastMsg);
            string? pv = null;
            if (lastMsg is not null)
            {
                var payload = JsonSerializer.Deserialize<ChatTextPayload>(lastMsg.PayloadJson, JsonOpts);
                pv = payload is not null ? PreviewFromPayload(payload) : null;
            }
            return new ChatThreadSummaryDto(
                t.Id,
                t.OfferId,
                t.StoreId,
                t.CreatedAtUtc,
                lastMsg?.CreatedAtUtc,
                pv,
                t.PurchaseMode);
        }).ToList();
    }

    private static string PreviewFromPayload(ChatTextPayload payload)
    {
        var tx = (payload.Text ?? "").Trim();
        return tx.Length == 0 ? "Mensaje" : tx;
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

        return msgs.Select(MapMessage).ToList();
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
            || typeEl.GetString() != "text")
            return null;

        if (!payload.TryGetProperty("text", out var textEl))
            return null;
        var text = (textEl.GetString() ?? "").Trim();
        if (text.Length == 0 || text.Length > 12_000)
            return null;

        if (senderUserId != t.BuyerUserId && senderUserId != t.SellerUserId)
            return null;

        string? offerQaId = null;
        if (payload.TryGetProperty("offerQaId", out var oqEl) && oqEl.ValueKind == JsonValueKind.String)
        {
            var oqs = oqEl.GetString();
            if (!string.IsNullOrWhiteSpace(oqs))
                offerQaId = oqs.Trim();
        }

        var now = DateTimeOffset.UtcNow;
        var wasFirst = t.FirstMessageSentAtUtc is null;
        if (wasFirst)
            t.FirstMessageSentAtUtc = now;

        var msgId = "cmg_" + Guid.NewGuid().ToString("N")[..16];
        var payloadObj = new ChatTextPayload
        {
            Text = text,
            OfferQaId = offerQaId,
            ReplyQuotes = null,
        };

        var row = new ChatMessageRow
        {
            Id = msgId,
            ThreadId = threadId,
            SenderUserId = senderUserId,
            PayloadJson = JsonSerializer.Serialize(payloadObj, JsonOpts),
            Status = ChatMessageStatus.Sent,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.ChatMessages.Add(row);

        var recipientId = senderUserId == t.BuyerUserId ? t.SellerUserId : t.BuyerUserId;
        await NotifyRecipientAsync(
            recipientId,
            t,
            row,
            text,
            senderUserId,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        var dto = MapMessage(row);
        await hub.Clients.Group($"thread:{threadId}").SendAsync(
            "messageCreated",
            new { message = dto },
            cancellationToken);
        return dto;
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
        await hub.Clients.Group($"thread:{threadId}").SendAsync(
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
        var list = await (
            from n in db.ChatNotifications.AsNoTracking()
            join t in db.ChatThreads.AsNoTracking() on n.ThreadId equals t.Id
            where n.RecipientUserId == userId && t.DeletedAtUtc == null
            orderby n.CreatedAtUtc descending
            select n).Take(200).ToListAsync(cancellationToken);

        return list.Select(n => new ChatNotificationDto(
            n.Id,
            n.ThreadId,
            n.MessageId,
            n.MessagePreview,
            n.AuthorStoreName,
            n.AuthorTrustScore,
            n.SenderUserId,
            n.CreatedAtUtc,
            n.ReadAtUtc)).ToList();
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

        var product = await db.StoreProducts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == oid, cancellationToken);
        var qaJson = product?.OfferQaJson;
        if (qaJson is null)
        {
            var service = await db.StoreServices.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == oid, cancellationToken);
            qaJson = service?.OfferQaJson;
        }

        if (string.IsNullOrWhiteSpace(qaJson))
            return;

        var root = JsonNode.Parse(qaJson);
        if (root is not JsonArray arr)
            return;

        var threads = await db.ChatThreads
            .Where(x => x.OfferId == oid && x.DeletedAtUtc == null)
            .ToListAsync(cancellationToken);
        if (threads.Count == 0)
            return;

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

        var now = DateTimeOffset.UtcNow;
        var hubRows = new List<ChatMessageRow>();

        foreach (var node in arr)
        {
            if (node is not JsonObject o)
                continue;
            if (!o.TryGetPropertyValue("id", out var idNode) || idNode is not JsonValue idVal)
                continue;
            var qaId = idVal.GetValue<string>()?.Trim();
            if (string.IsNullOrEmpty(qaId))
                continue;

            if (!o.TryGetPropertyValue("answer", out var ansNode) || ansNode is not JsonValue ansVal)
                continue;
            var answer = (ansVal.GetValue<string>() ?? "").Trim();
            if (answer.Length == 0)
                continue;

            if (!o.TryGetPropertyValue("askedBy", out var abNode) || abNode is not JsonObject askedByObj)
                continue;
            if (!askedByObj.TryGetPropertyValue("id", out var bidNode) || bidNode is not JsonValue bidVal)
                continue;
            var buyerId = bidVal.GetValue<string>()?.Trim();
            if (string.IsNullOrEmpty(buyerId)
                || buyerId.Equals("guest", StringComparison.OrdinalIgnoreCase))
                continue;

            var thread = threads.FirstOrDefault(t => t.BuyerUserId == buyerId);
            if (thread is null)
                continue;

            if (!sellerMsgsCache.TryGetValue(thread.Id, out var sellerMsgs))
                continue;

            var already = sellerMsgs.Any(m =>
            {
                var pl = JsonSerializer.Deserialize<ChatTextPayload>(m.PayloadJson, JsonOpts);
                return pl?.OfferQaId == qaId;
            });
            if (already)
                continue;

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
                PayloadJson = JsonSerializer.Serialize(payloadObj, JsonOpts),
                Status = ChatMessageStatus.Sent,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            db.ChatMessages.Add(row);
            sellerMsgs.Add(row);
            hubRows.Add(row);

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

        foreach (var row in hubRows)
        {
            var dto = MapMessage(row);
            await hub.Clients.Group($"thread:{row.ThreadId}").SendAsync(
                "messageCreated",
                new { message = dto },
                cancellationToken);
        }
    }
}
