using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Recommendations;
using VibeTrade.Backend.Features.Chat.Utils;

namespace VibeTrade.Backend.Features.Chat;

public sealed partial class ChatService
{
    public async Task<IReadOnlyList<ChatMessageDto>> ListMessagesAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        if (tid.Length < 4)
            return Array.Empty<ChatMessageDto>();

        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (t is null || !await UserCanAccessThreadRowAsync(userId, t, cancellationToken))
            return Array.Empty<ChatMessageDto>();

        var msgs = await db.ChatMessages.AsNoTracking()
            .Where(m => m.ThreadId == tid && m.DeletedAtUtc == null)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var labelCache = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var uid in msgs.Select(m => m.SenderUserId).Distinct())
        {
            labelCache[uid] = await GetParticipantAuthorLabelAsync(t, uid, cancellationToken);
        }

        var myExpected = (await GetMessageRecipientUserIdsAsync(t, userId, cancellationToken))
            .ToList();

        return msgs.Select(
            m =>
            {
                var label = labelCache[m.SenderUserId];
                if (m.SenderUserId != userId)
                    return ChatMessageDtoFactory.FromRow(m, label);
                var gr = ChatGroupReceiptsJsonUtil.Parse(m.GroupReceiptsJson);
                IReadOnlyList<string> expected = ChatMessageStatusUpdateCore
                    .MergedExpectedIds(gr, myExpected);
                if (expected.Count <= 1)
                    return ChatMessageDtoFactory.FromRow(m, label);
                var display = ChatMessageStatusUpdateCore.OutgoingGroupDisplayStatus(expected, gr);
                return ChatMessageDtoFactory.FromRowWithStatus(m, display, label);
            }).ToList();
    }

    private async Task<IReadOnlyList<ReplyQuoteDto>?> BuildReplyQuotesAsync(
        ChatThreadRow thread,
        PostChatMessageBody body,
        CancellationToken cancellationToken)
    {
        var ids = ChatReplyToIdsFromPayload.ReadList(body);
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
            var preview = ChatMessagePreviewText.FromPayload(row.Payload);
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

        if (await IsUserActiveCarrierOnThreadAsync(senderUserId, thread.Id, cancellationToken))
        {
            var cAcc = await db.UserAccounts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == senderUserId, cancellationToken);
            return string.IsNullOrWhiteSpace(cAcc?.DisplayName) ? "Transportista" : cAcc!.DisplayName.Trim();
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

        var preview = ChatMessagePreviewText.FromPayload(payloadObj);
        var recipients = (await GetMessageRecipientUserIdsAsync(thread, senderUserId, cancellationToken))
            .ToList();
        AttachGroupReceiptsJsonForMultiRecipientMessage(row, recipients);
        if (recipients.Count > 0
            && await AllRecipientAccountsHaveValidSessionAsync(recipients, cancellationToken))
        {
            ChatMessageStatusUpdateCore.ApplyAllRecipientsSessionActiveAsDelivered(
                row, recipients, now);
        }

        await NotifyRecipientsInThreadForMessageAsync(
            recipients, thread, row, preview, senderUserId, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        await NotifySellerThreadCreatedOnFirstOutgoingMessageAsync(thread, sellerHadNotSeenThreadYet, cancellationToken);

        var senderLabel = await GetParticipantAuthorLabelAsync(thread, senderUserId, cancellationToken);
        var dto = ChatMessageDtoFactory.FromRow(row, senderLabel);
        await HubSendToThreadParticipantsAsync(
            thread,
            "messageCreated",
            new { message = dto },
            cancellationToken);
        return dto;
    }

    private static void AttachGroupReceiptsJsonForMultiRecipientMessage(
        ChatMessageRow row,
        IReadOnlyList<string> recipients)
    {
        if (recipients.Count <= 1)
            return;
        var gr = new ChatMessageGroupReceipts { ExpectedRecipientIds = new List<string>(recipients) };
        row.GroupReceiptsJson = ChatGroupReceiptsJsonUtil.Serialize(gr);
    }

    /// <summary>
    /// Cada integrante de <paramref name="recipients"/> con sesión de auth activa
    /// (<c>auth_sessions.ExpiresAt</c> y <c>User.id</c> en la entidad mapeada).
    /// </summary>
    private async Task<bool> AllRecipientAccountsHaveValidSessionAsync(
        IReadOnlyList<string> recipients,
        CancellationToken cancellationToken)
    {
        if (recipients is not { Count: > 0 })
            return false;
        var utcNow = DateTimeOffset.UtcNow;
        var sessionUsers = await db.AuthSessions.AsNoTracking()
            .Where(s => s.ExpiresAt > utcNow)
            .Select(s => s.User)
            .ToListAsync(cancellationToken);
        var onlineUserIds = new List<string>(sessionUsers.Count);
        foreach (var u in sessionUsers)
        {
            var id = (u.Id ?? "").Trim();
            if (id.Length > 0)
                onlineUserIds.Add(id);
        }

        return recipients.All(
            r => ChatMessageStatusUpdateCore.InExpectedList(r, onlineUserIds));
    }

    private async Task NotifyRecipientsInThreadForMessageAsync(
        IReadOnlyList<string> recipients,
        ChatThreadRow thread,
        ChatMessageRow row,
        string preview,
        string senderUserId,
        CancellationToken cancellationToken)
    {
        foreach (var rid in recipients)
            await NotifyRecipientAsync(rid, thread, row, preview, senderUserId, cancellationToken);
    }

    private async Task NotifySellerThreadCreatedOnFirstOutgoingMessageAsync(
        ChatThreadRow thread,
        bool sellerHadNotSeenThreadYet,
        CancellationToken cancellationToken)
    {
        if (!sellerHadNotSeenThreadYet)
            return;
        var threadDto = await MapThreadWithBuyerLabelAsync(thread, cancellationToken);
        await hub.Clients.Group(ChatHubGroupNames.ForUser(thread.SellerUserId)).SendAsync(
            "threadCreated",
            new { thread = threadDto },
            cancellationToken);
    }

    public async Task<ChatMessageDto?> PostMessageAsync(
        PostChatMessageArgs request,
        CancellationToken cancellationToken = default)
    {
        var senderUserId = request.SenderUserId;
        var tid = (request.ThreadId ?? "").Trim();
        if (tid.Length < 4)
            return null;

        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null
            || !await UserCanAccessThreadRowAsync(senderUserId, t, cancellationToken))
            return null;

        var body = request.Message;
        var type = (body.Type ?? "").Trim();
        if (type.Length == 0)
            return null;

        if (senderUserId != t.BuyerUserId
            && senderUserId != t.SellerUserId
            && !await IsUserActiveCarrierOnThreadAsync(senderUserId, t.Id, cancellationToken))
            return null;

        return type switch
        {
            "text" => await PostTextChatMessageAsync(senderUserId, t, body, cancellationToken),
            "audio" => await PostAudioChatMessageAsync(senderUserId, t, body, cancellationToken),
            "image" => await PostImageChatMessageAsync(senderUserId, t, body, cancellationToken),
            "doc" => await PostSingleDocChatMessageAsync(senderUserId, t, body, cancellationToken),
            "docs" => await PostDocsBundleChatMessageAsync(senderUserId, t, body, cancellationToken),
            _ => null,
        };
    }

    private async Task<ChatMessageDto?> PostTextChatMessageAsync(
        string senderUserId,
        ChatThreadRow t,
        PostChatMessageBody body,
        CancellationToken cancellationToken)
    {
        var text = (body.Text ?? "").Trim();
        if (text.Length == 0 || text.Length > 12_000)
            return null;

        string? offerQaId = null;
        if (body.OfferQaId is { } oqs && !string.IsNullOrWhiteSpace(oqs))
            offerQaId = oqs.Trim();

        var quotes = await BuildReplyQuotesAsync(t, body, cancellationToken);
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
        PostChatMessageBody body,
        CancellationToken cancellationToken)
    {
        var url = body.Url ?? "";
        if (!ChatMediaUrlRules.IsAllowedPersisted(url))
            return null;

        if (body.Seconds is not { } sec)
            return null;
        if (sec is < 1 or > 3600)
            return null;

        var quotes = await BuildReplyQuotesAsync(t, body, cancellationToken);
        var payloadObj = new ChatAudioPayload
        {
            Url = url,
            Seconds = sec,
            ReplyQuotes = quotes,
        };
        return await InsertChatMessageAsync(t, senderUserId, payloadObj, cancellationToken);
    }

    private async Task<ChatMessageDto?> PostImageChatMessageAsync(
        string senderUserId,
        ChatThreadRow t,
        PostChatMessageBody body,
        CancellationToken cancellationToken)
    {
        if (!ChatPostPayloadValidation.TryParseAndValidateImagePayload(body, out var parsed) || parsed is null)
            return null;
        var quotes = await BuildReplyQuotesAsync(t, body, cancellationToken);
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
        PostChatMessageBody body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 500)
            return null;
        if (body.Url is not null && !ChatMediaUrlRules.IsAllowedPersisted(body.Url))
            return null;
        if (body.Kind is not ("pdf" or "doc" or "other"))
            return null;
        if (string.IsNullOrWhiteSpace(body.Size))
            return null;
        if (body.Caption is { Length: > 4000 })
            return null;

        var quotes = await BuildReplyQuotesAsync(t, body, cancellationToken);
        var payloadObj = new ChatDocPayload
        {
            Name = body.Name.Trim(),
            Size = body.Size.Trim(),
            Kind = body.Kind!,
            Url = body.Url,
            Caption = body.Caption,
            ReplyQuotes = quotes,
        };
        return await InsertChatMessageAsync(t, senderUserId, payloadObj, cancellationToken);
    }

    private async Task<ChatMessageDto?> PostDocsBundleChatMessageAsync(
        string senderUserId,
        ChatThreadRow t,
        PostChatMessageBody body,
        CancellationToken cancellationToken)
    {
        if (!ChatPostPayloadValidation.TryParseAndValidateDocsBundlePayload(body, out var parsed) || parsed is null)
            return null;
        var quotes = await BuildReplyQuotesAsync(t, body, cancellationToken);
        var payloadObj = new ChatDocsBundlePayload
        {
            Documents = parsed.Documents,
            Caption = parsed.Caption,
            EmbeddedAudio = parsed.EmbeddedAudio,
            ReplyQuotes = quotes,
        };
        return await InsertChatMessageAsync(t, senderUserId, payloadObj, cancellationToken);
    }

    private async Task<(ChatThreadRow t, ChatMessageRow m, IReadOnlyList<string> expected, ChatMessageGroupReceipts parsedGr)?> TryGetMessageStatusUpdateContextAsync(
        string userId,
        string tid,
        string messageId,
        CancellationToken cancellationToken)
    {
        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null
            || !await UserCanAccessThreadRowAsync(userId, t, cancellationToken))
            return null;

        var m = await db.ChatMessages.FirstOrDefaultAsync(
            x => x.Id == messageId && x.ThreadId == tid && x.DeletedAtUtc == null,
            cancellationToken);
        if (m is null || string.Equals(m.SenderUserId, userId, StringComparison.Ordinal))
            return null;

        var fromRecipients = (await GetMessageRecipientUserIdsAsync(t, m.SenderUserId, cancellationToken))
            .ToList();
        var parsed = ChatGroupReceiptsJsonUtil.Parse(m.GroupReceiptsJson);
        IReadOnlyList<string> expected = ChatMessageStatusUpdateCore
            .MergedExpectedIds(parsed, fromRecipients);
        if (expected.Count == 0
            || !ChatMessageStatusUpdateCore.InExpectedList(userId, expected))
            return null;
        return (t, m, expected, parsed);
    }

    public async Task<ChatMessageDto?> UpdateMessageStatusAsync(
        UpdateChatMessageStatusArgs request,
        CancellationToken cancellationToken = default)
    {
        if (request.Status is not (ChatMessageStatus.Delivered or ChatMessageStatus.Read))
            return null;

        var tid = (request.ThreadId ?? "").Trim();
        if (tid.Length < 4)
            return null;

        var userId = request.UserId;
        var messageId = request.MessageId;
        var status = request.Status;
        var ctx = await TryGetMessageStatusUpdateContextAsync(userId, tid, messageId, cancellationToken);
        if (ctx is null)
            return null;
        var (t, m, expected, parsedGr) = ctx.Value;

        var now = DateTimeOffset.UtcNow;
        var before = m.Status;
        var groupReceiptsJsonBefore = m.GroupReceiptsJson;

        if (expected.Count == 1)
        {
            var paired = ChatMessageStatusUpdateCore.TryApplyPaired(m, status, now);
            if (paired is ChatMessageStatusUpdateCore.PairedApplyOutcome.RejectNull)
                return null;
            if (paired is ChatMessageStatusUpdateCore.PairedApplyOutcome.ReturnDtoWithoutSave)
                return ChatMessageDtoFactory.FromRow(m);
        }
        else
        {
            var canonical = ChatMessageStatusUpdateCore.CanonicalRecipientId(userId, expected);
            ChatMessageStatusUpdateCore.ApplyGroup(parsedGr, expected, canonical, status, m, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        var dto = ChatMessageDtoFactory.FromRow(m);
        await HubBroadcastMessageStatusIfChangedAsync(
            t, tid, m, before, groupReceiptsJsonBefore, cancellationToken);
        return dto;
    }

    private async Task HubBroadcastMessageStatusIfChangedAsync(
        ChatThreadRow t,
        string tid,
        ChatMessageRow m,
        ChatMessageStatus statusBefore,
        string? groupReceiptsJsonBefore,
        CancellationToken cancellationToken)
    {
        var jsonUnchanged = string.Equals(
            (groupReceiptsJsonBefore ?? string.Empty).Trim(),
            (m.GroupReceiptsJson ?? string.Empty).Trim(),
            StringComparison.Ordinal);
        if (m.Status == statusBefore && jsonUnchanged)
            return;

        var fromRecipients = (await GetMessageRecipientUserIdsAsync(t, m.SenderUserId, cancellationToken))
            .ToList();
        var grAfter = ChatGroupReceiptsJsonUtil.Parse(m.GroupReceiptsJson);
        var grBefore = ChatGroupReceiptsJsonUtil.Parse(groupReceiptsJsonBefore);
        IReadOnlyList<string> expectedAfter = ChatMessageStatusUpdateCore
            .MergedExpectedIds(grAfter, fromRecipients);
        IReadOnlyList<string> expectedBefore = ChatMessageStatusUpdateCore
            .MergedExpectedIds(grBefore, fromRecipients);

        string hubSt;
        if (expectedAfter.Count <= 1)
        {
            hubSt = m.Status == ChatMessageStatus.Read ? "read" : "delivered";
        }
        else
        {
            var dAfter = ChatMessageStatusUpdateCore.OutgoingGroupDisplayStatus(
                expectedAfter, grAfter);
            var dBefore = ChatMessageStatusUpdateCore.OutgoingGroupDisplayStatus(
                expectedBefore, grBefore);
            if (dAfter == dBefore)
                return;
            hubSt = dAfter == ChatMessageStatus.Read ? "read" : "delivered";
        }

        await HubSendToThreadParticipantsAsync(
            t,
            "messageStatusChanged",
            new
            {
                messageId = m.Id,
                threadId = tid,
                status = hubSt,
                updatedAtUtc = m.UpdatedAtUtc,
            },
            cancellationToken);
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
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var q = db.ChatNotifications.AsNoTracking()
            .Where(n => n.RecipientUserId == userId)
            .Where(n =>
                n.OfferId != null && n.ThreadId == null
                || (n.ThreadId != null
                    && db.ChatThreads.Any(t => t.Id == n.ThreadId && t.DeletedAtUtc == null)));
        if (fromUtc.HasValue)
            q = q.Where(n => n.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            q = q.Where(n => n.CreatedAtUtc <= toUtc.Value);
        var list = await q
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(fromUtc != null || toUtc != null ? 500 : 200)
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
            n.Kind,
            n.MetaJson)).ToList();
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

    /// <inheritdoc />
    public async Task<bool> SoftLeaveThreadAsPartyAsync(
        PartySoftLeaveArgs request,
        CancellationToken cancellationToken = default)
    {
        var tid = (request.ThreadId ?? "").Trim();
        var uid = (request.UserId ?? "").Trim();
        var reasonTrim = (request.Reason ?? "").Trim();
        if (tid.Length < 4 || uid.Length < 2 || reasonTrim.Length < 1)
            return false;

        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == tid && x.DeletedAtUtc == null, cancellationToken);
        if (t is null)
            return false;

        var isBuyer = string.Equals(uid, t.BuyerUserId, StringComparison.Ordinal);
        var isSeller = string.Equals(uid, t.SellerUserId, StringComparison.Ordinal);
        if (!isBuyer && !isSeller)
            return false;

        if (isBuyer && t.BuyerExpelledAtUtc is not null)
            return true;
        if (isSeller && t.SellerExpelledAtUtc is not null)
            return true;

        if (!await HasAcceptedNonDeletedTradeAgreementOnThreadAsync(tid, cancellationToken))
            return false;

        if (!await TryPostSystemNoticeForSoftLeaveAsync(uid, tid, isSeller, reasonTrim, cancellationToken))
            return false;

        var now = DateTimeOffset.UtcNow;
        ApplyPartyExpulsionToThread(t, uid, isBuyer, reasonTrim, now);
        await db.SaveChangesAsync(cancellationToken);
        await NotifyCounterpartyOfPartySoftLeaveAsync(t, uid, isSeller, reasonTrim, cancellationToken);
        await BroadcastPeerPartyExitedForSoftLeaveAsync(tid, t, uid, isSeller, cancellationToken);
        return true;
    }

    private async Task<bool> HasAcceptedNonDeletedTradeAgreementOnThreadAsync(
        string threadId,
        CancellationToken cancellationToken) =>
        await db.TradeAgreements.AsNoTracking()
            .AnyAsync(
                x => x.ThreadId == threadId
                    && x.Status == "accepted"
                    && x.DeletedAtUtc == null,
                cancellationToken);

    private async Task<bool> TryPostSystemNoticeForSoftLeaveAsync(
        string uid,
        string tid,
        bool isSeller,
        string reasonTrim,
        CancellationToken cancellationToken)
    {
        var notice = isSeller
            ? $"El vendedor salió del chat con un acuerdo ya aceptado. Motivo declarado: {reasonTrim}"
            : $"El comprador salió del chat con un acuerdo ya aceptado. Motivo declarado: {reasonTrim}";
        return await PostSystemThreadNoticeAsync(uid, tid, notice, cancellationToken) is not null;
    }

    private static void ApplyPartyExpulsionToThread(
        ChatThreadRow t,
        string uid,
        bool isBuyer,
        string reasonTrim,
        DateTimeOffset now)
    {
        if (isBuyer)
            t.BuyerExpelledAtUtc = now;
        else
            t.SellerExpelledAtUtc = now;
        t.PartyExitedUserId = uid;
        t.PartyExitedReason = reasonTrim.Length > 2000 ? reasonTrim[..2000] : reasonTrim;
        t.PartyExitedAtUtc = now;
    }

    private async Task BroadcastPeerPartyExitedForSoftLeaveAsync(
        string tid,
        ChatThreadRow t,
        string uid,
        bool isSeller,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            threadId = tid,
            leaverUserId = uid,
            reason = t.PartyExitedReason,
            atUtc = t.PartyExitedAtUtc,
            leaverRole = isSeller ? "seller" : "buyer",
        };
        await hub.Clients.Group(ChatHubGroupNames.ForThread(tid)).SendAsync("peerPartyExitedChat", payload, cancellationToken);
        await HubSendToThreadParticipantsAsync(t, "peerPartyExitedChat", payload, cancellationToken);
    }

    /// <summary>
    /// La otra parte puede no estar en el chat (sin SignalR al hilo): guardamos notificación in-app
    /// y <c>notificationCreated</c> al grupo de usuario.
    /// </summary>
    private async Task NotifyCounterpartyOfPartySoftLeaveAsync(
        ChatThreadRow t,
        string leaverUserId,
        bool leaverIsSeller,
        string reasonTrim,
        CancellationToken cancellationToken)
    {
        var recipient = leaverIsSeller
            ? (t.BuyerUserId ?? "").Trim()
            : (t.SellerUserId ?? "").Trim();
        if (recipient.Length < 2 || string.Equals(recipient, leaverUserId, StringComparison.Ordinal))
            return;

        var (authorLabel, trust) = await GetLeaverAuthorForPeerExitedAsync(t, leaverUserId, leaverIsSeller, cancellationToken);
        var body = leaverIsSeller
            ? $"El vendedor salió del chat con un acuerdo aceptado. Motivo: {reasonTrim}"
            : $"El comprador salió del chat con un acuerdo aceptado. Motivo: {reasonTrim}";
        if (body.Length > 500)
            body = body[..497] + "…";
        await AddPeerPartyExitedInAppNotificationAndHubAsync(
            recipient, t, leaverUserId, body, authorLabel, trust, cancellationToken);
    }

    private async Task<(string authorLabel, int trust)> GetLeaverAuthorForPeerExitedAsync(
        ChatThreadRow t,
        string leaverUserId,
        bool leaverIsSeller,
        CancellationToken cancellationToken)
    {
        if (leaverIsSeller)
        {
            var store = await db.Stores.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == t.StoreId, cancellationToken);
            return (
                string.IsNullOrWhiteSpace(store?.Name) ? "Vendedor" : store!.Name.Trim(),
                store?.TrustScore ?? 0);
        }

        var acc = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == leaverUserId, cancellationToken);
        return (
            string.IsNullOrWhiteSpace(acc?.DisplayName) ? "Comprador" : acc!.DisplayName.Trim(),
            acc?.TrustScore ?? 0);
    }

    private async Task AddPeerPartyExitedInAppNotificationAndHubAsync(
        string recipient,
        ChatThreadRow t,
        string leaverUserId,
        string body,
        string authorLabel,
        int trust,
        CancellationToken cancellationToken)
    {
        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = recipient,
            ThreadId = t.Id,
            MessageId = null,
            OfferId = null,
            MessagePreview = body,
            AuthorStoreName = authorLabel,
            AuthorTrustScore = trust,
            SenderUserId = leaverUserId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ReadAtUtc = null,
            Kind = "peer_party_exited",
        });
        await db.SaveChangesAsync(cancellationToken);
        await hub.Clients.Group(ChatHubGroupNames.ForUser(recipient)).SendAsync(
            "notificationCreated",
            new { kind = "peer_party_exited", threadId = t.Id },
            cancellationToken);
    }

    public async Task<bool> DeleteThreadAsync(string userId, string threadId, CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        if (tid.Length < 4)
            return false;

        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !ChatThreadAccess.UserCanSeeThread(userId, t))
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

    private async Task<List<ChatMessageRow>> StageOfferQaAnswerRowsFromProductListAsync(
        IReadOnlyList<OfferQaComment> qaList,
        List<ChatThreadRow> threads,
        IReadOnlyDictionary<string, List<ChatMessageRow>> sellerMsgsCache,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
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

        return hubRows;
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
        var hubRows = await StageOfferQaAnswerRowsFromProductListAsync(
            qaList, threads, sellerMsgsCache, now, cancellationToken);

        if (hubRows.Count == 0)
            return;

        await db.SaveChangesAsync(cancellationToken);

        await NotifySellersOfThreadCreatedWhenOfferQaSyncIsFirstMessageAsync(
            hubRows, threads, cancellationToken);

        await BroadcastNewOfferQaMessagesToHubAsync(hubRows, threads, cancellationToken);
    }

    private async Task NotifySellersOfThreadCreatedWhenOfferQaSyncIsFirstMessageAsync(
        IReadOnlyList<ChatMessageRow> hubRows,
        IReadOnlyList<ChatThreadRow> threads,
        CancellationToken cancellationToken)
    {
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
            await hub.Clients.Group(ChatHubGroupNames.ForUser(thread.SellerUserId)).SendAsync(
                "threadCreated",
                new { thread = threadDto },
                cancellationToken);
        }
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
            var dto = ChatMessageDtoFactory.FromRow(row, senderLabel);
            await HubSendToThreadParticipantsAsync(
                thread,
                "messageCreated",
                new { message = dto },
                cancellationToken);
        }
    }

    public async Task<ChatMessageDto?> PostAgreementAnnouncementAsync(
        PostAgreementAnnouncementArgs request,
        CancellationToken cancellationToken = default)
    {
        var sellerUserId = request.SellerUserId;
        var threadId = request.ThreadId;
        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !ChatThreadAccess.UserCanSeeThread(sellerUserId, t))
            return null;
        if (sellerUserId != t.SellerUserId)
            return null;
        if (string.IsNullOrWhiteSpace(request.AgreementId) || string.IsNullOrWhiteSpace(request.Title))
            return null;
        var st = request.Status;
        if (st is not ("pending_buyer" or "accepted" or "rejected"))
            st = "pending_buyer";

        var payload = new ChatAgreementPayload
        {
            AgreementId = request.AgreementId.Trim(),
            Title = request.Title.Trim(),
            Body = "",
            Status = st,
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
        if (t is null || t.DeletedAtUtc is not null)
            return null;
        var aid = (actorUserId ?? "").Trim();
        if (!string.Equals(aid, t.BuyerUserId, StringComparison.Ordinal)
            && !string.Equals(aid, t.SellerUserId, StringComparison.Ordinal))
            return null;
        if (!await UserCanAccessThreadRowAsync(aid, t, cancellationToken))
            return null;
        var tx = (text ?? "").Trim();
        if (tx.Length == 0 || tx.Length > 12_000)
            return null;

        var payload = new ChatSystemTextPayload { Text = tx };
        return await InsertChatMessageAsync(t, aid, payload, cancellationToken);
    }

    public async Task<ChatMessageDto?> PostAutomatedSystemThreadNoticeAsync(
        string threadId,
        string text,
        CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        if (tid.Length < 4)
            return null;

        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null)
            return null;

        var actorUserId = (t.SellerUserId ?? "").Trim();
        if (actorUserId.Length < 2)
            return null;

        var tx = (text ?? "").Trim();
        if (tx.Length == 0 || tx.Length > 12_000)
            return null;

        var payload = new ChatSystemTextPayload { Text = tx };
        return await InsertChatMessageAsync(t, actorUserId, payload, cancellationToken);
    }

    public async Task<ChatMessageDto?> PostAutomatedPaymentFeeReceiptAsync(
        string threadId,
        ChatPaymentFeeReceiptPayload payload,
        CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        if (tid.Length < 4)
            return null;
        if (payload.Lines is null)
            return null;

        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null)
            return null;

        var actorUserId = (t.SellerUserId ?? "").Trim();
        if (actorUserId.Length < 2)
            return null;

        return await InsertChatMessageAsync(t, actorUserId, payload, cancellationToken);
    }
}