using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Notifications.BroadcastingDtos;
using VibeTrade.Backend.Features.Notifications.BroadcastingInterfaces;
using VibeTrade.Backend.Features.Recommendations.Core;
using VibeTrade.Backend.Utils;

namespace VibeTrade.Backend.Features.Notifications;

/// <summary>SignalR: grupos de hilo/usuario/oferta y envío a participantes.</summary>
public sealed class BroadcastingService(
    AppDbContext db,
    IHubContext<ChatHub> hub,
    IThreadAccessControlService threadAccess) : IBroadcastingService
{
    /// <summary>
    /// Comprador/vendedor que hizo soft-leave ya no debe recibir mensajes ni avisos del hilo
    /// (tampoco vía rol transportista en el mismo hilo).
    /// </summary>
    private static bool IsBuyerOrSellerExpelledFromThread(ChatThreadRow thread, string userId)
    {
        var uid = (userId ?? "").Trim();
        if (uid.Length < 2)
            return false;
        var buyer = (thread.BuyerUserId ?? "").Trim();
        var seller = (thread.SellerUserId ?? "").Trim();
        if (string.Equals(uid, buyer, StringComparison.Ordinal) && thread.BuyerExpelledAtUtc is not null)
            return true;
        if (string.Equals(uid, seller, StringComparison.Ordinal) && thread.SellerExpelledAtUtc is not null)
            return true;
        return false;
    }

    private async Task<HashSet<string>> GetThreadParticipantUserIdSetAsync(
        ChatThreadRow thread,
        CancellationToken cancellationToken)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (thread.BuyerExpelledAtUtc is null && !string.IsNullOrWhiteSpace(thread.BuyerUserId))
            set.Add(thread.BuyerUserId.Trim());
        if (thread.SellerExpelledAtUtc is null && !string.IsNullOrWhiteSpace(thread.SellerUserId))
            set.Add(thread.SellerUserId.Trim());
        var carriers = await ChatQueryHelpers.GetActiveCarrierUserIdsForThreadAsync(db, thread.Id, cancellationToken);
        foreach (var c in carriers)
        {
            if (string.IsNullOrWhiteSpace(c))
                continue;
            var cid = c.Trim();
            if (IsBuyerOrSellerExpelledFromThread(thread, cid))
                continue;
            set.Add(cid);
        }

        if (thread.IsSocialGroup)
        {
            var socialExtra = await ChatQueryHelpers.GetSocialGroupMemberUserIdsAsync(db, thread.Id, cancellationToken);
            foreach (var x in socialExtra)
            {
                if (!string.IsNullOrWhiteSpace(x))
                    set.Add(x.Trim());
            }
        }

        var participatedHereIds = await ChatQueryHelpers.GetParticipatedCarrierUserIdsForThreadAsync(db, thread.Id, cancellationToken);
        var activeElsewhereIds = await ChatQueryHelpers.GetActiveCarrierUserIdsElsewhereAsync(db, thread.Id, cancellationToken);
        foreach (var pid in participatedHereIds)
        {
            if (string.IsNullOrWhiteSpace(pid))
                continue;
            var p = pid.Trim();
            if (set.Contains(p))
                continue;
            if (IsBuyerOrSellerExpelledFromThread(thread, p))
                continue;
            if (activeElsewhereIds.Any(e =>
                    !string.IsNullOrWhiteSpace(e)
                    && (string.Equals(e.Trim(), p, StringComparison.Ordinal)
                        || ChatThreadAccess.UserIdsMatchLoose(p, e))))
                set.Add(p);
        }

        return set;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetThreadParticipantUserIdsForThreadRowAsync(
        ChatThreadRow thread,
        CancellationToken cancellationToken = default)
    {
        var set = await GetThreadParticipantUserIdSetAsync(thread, cancellationToken);
        return set.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetMessageRecipientUserIdsAsync(
        ChatThreadRow thread,
        string senderUserId,
        CancellationToken cancellationToken = default)
    {
        var set = await GetThreadParticipantUserIdSetAsync(thread, cancellationToken);
        var s = (senderUserId ?? "").Trim();
        set.Remove(s);
        return set.ToList();
    }

    /// <inheritdoc />
    public async Task HubSendToThreadParticipantsAsync(
        ChatThreadRow thread,
        string method,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var participants = await GetThreadParticipantUserIdSetAsync(thread, cancellationToken);
        foreach (var uid in participants)
            await hub.Clients.Group(ChatHubGroupNames.ForUser(uid)).SendAsync(method, payload, cancellationToken);
    }

    /// <inheritdoc />
    public Task BroadcastCarrierTelemetryUpdatedAsync(
        string threadId,
        string routeSheetId,
        string agreementId,
        string routeStopId,
        string carrierUserId,
        double lat,
        double lng,
        double? progressFraction,
        bool offRoute,
        DateTimeOffset reportedAtUtc,
        double? speedKmh,
        string? avatarUrl,
        CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var aid = (agreementId ?? "").Trim();
        var sid = (routeStopId ?? "").Trim();
        var cid = (carrierUserId ?? "").Trim();
        if (tid.Length < 4 || rsid.Length < 1 || aid.Length < 8 || sid.Length < 1 || cid.Length < 2)
            return Task.CompletedTask;

        return hub.Clients.Group(ChatHubGroupNames.ForThread(tid)).SendAsync(
            "carrierTelemetryUpdated",
            new
            {
                threadId = tid,
                routeSheetId = rsid,
                agreementId = aid,
                routeStopId = sid,
                carrierUserId = cid,
                lat,
                lng,
                progressFraction,
                offRoute,
                reportedAtUtc,
                speedKmh,
                avatarUrl,
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task BroadcastRouteTramoSubscriptionsChangedAsync(
        RouteTramoSubscriptionsBroadcastArgs request,
        CancellationToken cancellationToken = default)
    {
        var tid = (request.ThreadId ?? "").Trim();
        var rsid = (request.RouteSheetId ?? "").Trim();
        var ch = (request.Change ?? "").Trim().ToLowerInvariant();
        var aid = (request.ActorUserId ?? "").Trim();
        var eid = (request.EmergentPublicationOfferId ?? "").Trim();
        if (tid.Length < 4 || rsid.Length < 1 || ch.Length == 0)
            return Task.CompletedTask;

        var payload = new
        {
            threadId = tid,
            routeSheetId = rsid,
            change = ch,
            actorUserId = aid.Length > 0 ? aid : null,
            emergentOfferId = eid.Length >= 4 && OfferUtils.IsEmergentPublicationId(eid) ? eid : null,
        };

        var threadTask = hub.Clients.Group(ChatHubGroupNames.ForThread(tid)).SendAsync(
            "routeTramoSubscriptionsChanged",
            payload,
            cancellationToken);

        if (payload.emergentOfferId is null)
            return threadTask;

        return Task.WhenAll(
            threadTask,
            hub.Clients.Group(ChatHubGroupNames.ForOffer(payload.emergentOfferId)).SendAsync(
                "routeTramoSubscriptionsChanged",
                payload,
                cancellationToken));
    }

    /// <inheritdoc />
    public Task BroadcastOfferCommentsUpdatedAsync(string offerId, CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return Task.CompletedTask;
        return hub.Clients.Group(ChatHubGroupNames.ForOffer(oid)).SendAsync(
            "offerCommentsUpdated",
            new { offerId = oid },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task NotifyThreadCreatedToBuyerAsync(ChatThreadDto dto, CancellationToken cancellationToken = default)
    {
        await hub.Clients.Group(ChatHubGroupNames.ForUser(dto.BuyerUserId)).SendAsync(
            "threadCreated",
            new { thread = dto },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task NotifyThreadCreatedToUserAsync(string userId, ChatThreadDto dto, CancellationToken cancellationToken = default)
    {
        var trimmed = (userId ?? "").Trim();
        if (trimmed.Length < 2)
            return;
        await hub.Clients.Group(ChatHubGroupNames.ForUser(trimmed)).SendAsync(
            "threadCreated",
            new { thread = dto },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task BroadcastThreadCreatedToUsersAsync(
        IEnumerable<string> userIds,
        ChatThreadDto dto,
        CancellationToken cancellationToken = default)
    {
        foreach (var n in userIds)
        {
            var trimmed = (n ?? "").Trim();
            if (trimmed.Length < 2)
                continue;
            await hub.Clients.Group(ChatHubGroupNames.ForUser(trimmed)).SendAsync(
                    "threadCreated",
                    new { thread = dto },
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<string> ResolveDisplayNameForParticipantLeftAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var acc = await db.UserAccounts.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.DisplayName, u.PhoneDisplay, u.PhoneDigits })
            .FirstOrDefaultAsync(cancellationToken);
        if (acc is not null && !string.IsNullOrWhiteSpace(acc.DisplayName))
            return acc.DisplayName.Trim();
        return (acc?.PhoneDisplay ?? acc?.PhoneDigits ?? userId).Trim();
    }

    /// <inheritdoc />
    public async Task<bool> BroadcastParticipantLeftToOthersAsync(
        string leaverUserId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var tid = ChatThreadIds.NormalizePersistedId(threadId);
        if (tid.Length < 4)
            return false;
        var uid = (leaverUserId ?? "").Trim();
        if (uid.Length < 2)
            return false;

        var t = await db.ChatThreads
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null)
            return false;

        if (!await threadAccess.UserCanAccessThreadRowAsync(uid, t, cancellationToken))
            return false;

        if (t.IsSocialGroup)
        {
            var buyerId = (t.BuyerUserId ?? "").Trim();
            var sellerId = (t.SellerUserId ?? "").Trim();
            var now = DateTimeOffset.UtcNow;
            if (string.Equals(uid, buyerId, StringComparison.Ordinal))
                t.BuyerExpelledAtUtc = now;
            else if (string.Equals(uid, sellerId, StringComparison.Ordinal))
                t.SellerExpelledAtUtc = now;
            else
            {
                var extraRows = await db.ChatSocialGroupMembers
                    .Where(m => m.ThreadId == tid && m.UserId == uid)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (extraRows.Count == 0)
                    return false;
                db.ChatSocialGroupMembers.RemoveRange(extraRows);
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var buyerId = (t.BuyerUserId ?? "").Trim();
            var sellerId = (t.SellerUserId ?? "").Trim();
            var now = DateTimeOffset.UtcNow;
            if (string.Equals(uid, buyerId, StringComparison.Ordinal))
            {
                t.BuyerExpelledAtUtc = now;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(uid, sellerId, StringComparison.Ordinal))
            {
                t.SellerExpelledAtUtc = now;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var displayName = await ResolveDisplayNameForParticipantLeftAsync(uid, cancellationToken);
        var payload = new { threadId = tid, userId = uid, displayName };
        var others = await GetThreadParticipantUserIdSetAsync(t, cancellationToken);
        others.Remove(uid);
        foreach (var oid in others)
        {
            var rid = (oid ?? "").Trim();
            if (rid.Length < 2)
                continue;
            if (string.Equals(rid, uid, StringComparison.Ordinal))
                continue;
            await hub.Clients.Group(ChatHubGroupNames.ForUser(rid)).SendAsync(
                "participantLeft",
                payload,
                cancellationToken);
        }
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetThreadParticipantUserIdsAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var tid = ChatThreadIds.NormalizePersistedId(threadId);
        if (tid.Length < 4)
            return [];

        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (t is null || t.DeletedAtUtc is not null)
            return [];

        var set = await GetThreadParticipantUserIdSetAsync(t, cancellationToken).ConfigureAwait(false);
        return set.ToList();
    }

    /// <inheritdoc />
    public Task BroadcastChatMessageCreatedAsync(
        ChatThreadRow thread,
        ChatMessageDto messageDto,
        CancellationToken cancellationToken = default) =>
        HubSendToThreadParticipantsAsync(thread, "messageCreated", new { message = messageDto }, cancellationToken);

    /// <inheritdoc />
    public async Task BroadcastChatMessagesCreatedAsync(
        IReadOnlyList<(ChatThreadRow Thread, ChatMessageDto Message)> items,
        CancellationToken cancellationToken = default)
    {
        foreach (var (thread, messageDto) in items)
            await BroadcastChatMessageCreatedAsync(thread, messageDto, cancellationToken);
    }

    /// <inheritdoc />
    public async Task TryBroadcastChatMessageStatusChangedAsync(
        ChatThreadRow thread,
        string threadPersistedId,
        ChatMessageRow message,
        ChatMessageStatus statusBefore,
        string? groupReceiptsJsonBefore,
        CancellationToken cancellationToken = default)
    {
        var jsonUnchanged = string.Equals(
            (groupReceiptsJsonBefore ?? string.Empty).Trim(),
            (message.GroupReceiptsJson ?? string.Empty).Trim(),
            StringComparison.Ordinal);
        if (message.Status == statusBefore && jsonUnchanged)
            return;

        var fromRecipients = (await GetMessageRecipientUserIdsAsync(thread, message.SenderUserId, cancellationToken))
            .ToList();
        var grAfter = ChatGroupReceiptsJsonUtil.Parse(message.GroupReceiptsJson);
        var grBefore = ChatGroupReceiptsJsonUtil.Parse(groupReceiptsJsonBefore);
        IReadOnlyList<string> expectedAfter = ChatMessageStatusUpdateCore
            .MergedExpectedIds(grAfter, fromRecipients);
        IReadOnlyList<string> expectedBefore = ChatMessageStatusUpdateCore
            .MergedExpectedIds(grBefore, fromRecipients);

        string hubSt;
        if (expectedAfter.Count <= 1)
        {
            hubSt = message.Status == ChatMessageStatus.Read ? "read" : "delivered";
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
            thread,
            "messageStatusChanged",
            new
            {
                messageId = message.Id,
                threadId = threadPersistedId,
                status = hubSt,
                updatedAtUtc = message.UpdatedAtUtc,
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task BroadcastPeerPartyExitedChatAsync(
        ChatThreadRow thread,
        string threadPersistedId,
        string leaverUserId,
        string? partyExitedReason,
        DateTimeOffset? partyExitedAtUtc,
        bool leaverIsSeller,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            threadId = threadPersistedId,
            leaverUserId,
            reason = partyExitedReason,
            atUtc = partyExitedAtUtc,
            leaverRole = leaverIsSeller ? "seller" : "buyer",
        };
        return HubSendToThreadParticipantsAsync(thread, "peerPartyExitedChat", payload, cancellationToken);
    }

    /// <inheritdoc />
    public async Task TryNotifySellersThreadCreatedAfterQaMessageInsertSyncAsync(
        IReadOnlyList<ChatMessageRow> hubRows,
        IReadOnlyList<ChatThreadRow> threads,
        Func<ChatThreadRow, CancellationToken, Task<ChatThreadDto>> mapThreadWithBuyerLabel,
        CancellationToken cancellationToken = default)
    {
        var byThreadId = threads.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var g in hubRows.GroupBy(r => r.ThreadId))
        {
            var tid = g.Key;
            if (!byThreadId.TryGetValue(tid, out var thread))
                continue;
            var totalMsgs = await db.ChatMessages.AsNoTracking()
                .CountAsync(m => m.ThreadId == tid && m.DeletedAtUtc == null, cancellationToken);
            if (totalMsgs != g.Count())
                continue;
            var threadDto = await mapThreadWithBuyerLabel(thread, cancellationToken);
            await NotifyThreadCreatedToUserAsync(thread.SellerUserId, threadDto, cancellationToken);
        }
    }
}
