using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Recommendations;
using VibeTrade.Backend.Utils;
using VibeTrade.Backend.Features.Chat.Utils;

namespace VibeTrade.Backend.Features.Chat;

public sealed partial class ChatService(
    AppDbContext db,
    IHubContext<ChatHub> hub,
    IPartySoftLeaveCoordinator partySoftLeave) : IChatService
{
    /// <summary>Oferta ficticia para hilos solo mensajería (lista/chat sin catálogo).</summary>
    private const string SocialThreadOfferId = "__vt_social__";

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

    private async Task<HashSet<string>> GetThreadParticipantUserIdsAsync(
        ChatThreadRow thread,
        CancellationToken cancellationToken)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (thread.BuyerExpelledAtUtc is null && !string.IsNullOrWhiteSpace(thread.BuyerUserId))
            set.Add(thread.BuyerUserId.Trim());
        if (thread.SellerExpelledAtUtc is null && !string.IsNullOrWhiteSpace(thread.SellerUserId))
            set.Add(thread.SellerUserId.Trim());
        var carriers = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x =>
                x.ThreadId == thread.Id
                && x.Status != "rejected"
                && x.Status != "withdrawn")
            .Select(x => x.CarrierUserId)
            .ToListAsync(cancellationToken);
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
            var socialExtra = await db.ChatSocialGroupMembers.AsNoTracking()
                .Where(x => x.ThreadId == thread.Id)
                .Select(x => x.UserId)
                .ToListAsync(cancellationToken);
            foreach (var x in socialExtra)
            {
                if (!string.IsNullOrWhiteSpace(x))
                    set.Add(x.Trim());
            }
        }

        var participatedHereIds = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x => x.ThreadId == thread.Id && x.Status != "rejected")
            .Select(x => x.CarrierUserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var activeElsewhereIds = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x =>
                x.ThreadId != thread.Id
                && x.Status != "rejected"
                && x.Status != "withdrawn")
            .Select(x => x.CarrierUserId)
            .Distinct()
            .ToListAsync(cancellationToken);
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

    private async Task<IReadOnlyList<string>> GetMessageRecipientUserIdsAsync(
        ChatThreadRow thread,
        string senderUserId,
        CancellationToken cancellationToken)
    {
        var set = await GetThreadParticipantUserIdsAsync(thread, cancellationToken);
        var s = (senderUserId ?? "").Trim();
        set.Remove(s);
        return set.ToList();
    }

    private async Task<bool> IsUserActiveCarrierOnThreadAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken) =>
        await db.RouteTramoSubscriptions.AsNoTracking()
            .AnyAsync(
                x =>
                    x.ThreadId == threadId
                    && x.CarrierUserId == userId
                    && x.Status != "rejected"
                    && x.Status != "withdrawn",
                cancellationToken);

    private async Task HubSendToThreadParticipantsAsync(
        ChatThreadRow thread,
        string method,
        object payload,
        CancellationToken cancellationToken)
    {
        var participants = await GetThreadParticipantUserIdsAsync(thread, cancellationToken);
        foreach (var uid in participants)
        {
            await hub.Clients.Group(ChatHubGroupNames.ForUser(uid)).SendAsync(method, payload, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<bool> UserCanAccessThreadRowAsync(
        string userId,
        ChatThreadRow thread,
        CancellationToken cancellationToken = default)
    {
        if (thread.DeletedAtUtc is not null)
            return false;
        var uid = (userId ?? "").Trim();
        if (uid.Length == 0)
            return false;
        var buyerId = (thread.BuyerUserId ?? "").Trim();
        var sellerId = (thread.SellerUserId ?? "").Trim();
        // Comprador / vendedor: si fue expulsado, sin acceso. Si no, acceso aunque aún no cumplan Initiator/FirstMessage.
        if (string.Equals(uid, buyerId, StringComparison.Ordinal))
        {
            if (thread.BuyerExpelledAtUtc is not null)
                return false;
            return true;
        }
        if (string.Equals(uid, sellerId, StringComparison.Ordinal))
        {
            if (thread.SellerExpelledAtUtc is not null)
                return false;
            return true;
        }
        if (thread.IsSocialGroup
            && await db.ChatSocialGroupMembers.AsNoTracking()
                .AnyAsync(m => m.ThreadId == thread.Id && m.UserId == uid, cancellationToken))
            return true;
        if (ChatThreadAccess.UserCanSeeThread(uid, thread))
            return true;
        if (await db.RouteTramoSubscriptions.AsNoTracking()
                .AnyAsync(
                    x => x.ThreadId == thread.Id
                        && x.CarrierUserId == uid
                        && x.Status != "rejected"
                        && x.Status != "withdrawn",
                    cancellationToken))
            return true;

        // Retirado/expulsado en este hilo pero con tramos activos en otro hilo: mantiene el chat acá.
        var carrierIdsThisThread = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x => x.ThreadId == thread.Id && x.Status != "rejected")
            .Select(x => x.CarrierUserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (carrierIdsThisThread.Any(cid =>
                !string.IsNullOrWhiteSpace(cid)
                && (string.Equals(cid.Trim(), uid, StringComparison.Ordinal)
                    || ChatThreadAccess.UserIdsMatchLoose(uid, cid))))
        {
            if (await db.RouteTramoSubscriptions.AsNoTracking()
                    .AnyAsync(
                        x => x.CarrierUserId == uid
                            && x.ThreadId != thread.Id
                            && x.Status != "rejected"
                            && x.Status != "withdrawn",
                        cancellationToken))
                return true;
            var otherCarrierIds = await db.RouteTramoSubscriptions.AsNoTracking()
                .Where(x =>
                    x.ThreadId != thread.Id
                    && x.Status != "rejected"
                    && x.Status != "withdrawn")
                .Select(x => x.CarrierUserId)
                .Distinct()
                .ToListAsync(cancellationToken);
            if (otherCarrierIds.Any(oid =>
                    !string.IsNullOrWhiteSpace(oid)
                    && (string.Equals(oid.Trim(), uid, StringComparison.Ordinal)
                        || ChatThreadAccess.UserIdsMatchLoose(uid, oid))))
                return true;
        }

        // Misma fila pero id guardado con otro formato (p. ej. prefijos / solo dígitos).
        var carrierIds = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x =>
                x.ThreadId == thread.Id
                && x.Status != "rejected"
                && x.Status != "withdrawn")
            .Select(x => x.CarrierUserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return carrierIds.Any(cid => ChatThreadAccess.UserIdsMatchLoose(uid, cid));
    }

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
        OfferCommentNotificationArgs request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RecipientUserId))
            return;

        var preview = request.TextPreview.Length > 500 ? request.TextPreview[..500] + "…" : request.TextPreview;
        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        var rid = request.RecipientUserId.Trim();
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = rid,
            ThreadId = null,
            MessageId = null,
            OfferId = request.OfferId,
            MessagePreview = preview,
            AuthorStoreName = request.AuthorLabel,
            AuthorTrustScore = request.AuthorTrust,
            SenderUserId = request.SenderUserId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ReadAtUtc = null,
            Kind = "offer_comment",
        });
        await db.SaveChangesAsync(cancellationToken);

        await hub.Clients.Group(ChatHubGroupNames.ForUser(rid)).SendAsync(
            "notificationCreated",
            new { kind = "offer_comment", offerId = request.OfferId },
            cancellationToken);
    }

    public async Task NotifyOfferLikeAsync(
        OfferLikeNotificationArgs request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SellerUserId))
            return;

        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        var rid = request.SellerUserId.Trim();
        var oid = (request.OfferId ?? "").Trim();
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = rid,
            ThreadId = null,
            MessageId = null,
            OfferId = oid,
            MessagePreview = "Le dio me gusta a tu oferta.",
            AuthorStoreName = request.LikerLabel,
            AuthorTrustScore = request.LikerTrust,
            SenderUserId = request.LikerSenderUserId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ReadAtUtc = null,
            Kind = "offer_like",
        });
        await db.SaveChangesAsync(cancellationToken);

        await hub.Clients.Group(ChatHubGroupNames.ForUser(rid)).SendAsync(
            "notificationCreated",
            new { kind = "offer_like", offerId = oid },
            cancellationToken);
    }

    public async Task NotifyQaCommentLikeAsync(
        QaCommentLikeNotificationArgs request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CommentAuthorUserId))
            return;

        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        var rid = request.CommentAuthorUserId.Trim();
        var oid = (request.OfferId ?? "").Trim();
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = rid,
            ThreadId = null,
            MessageId = null,
            OfferId = oid,
            MessagePreview = "Le dio me gusta a tu comentario.",
            AuthorStoreName = request.LikerLabel,
            AuthorTrustScore = request.LikerTrust,
            SenderUserId = request.LikerSenderUserId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ReadAtUtc = null,
            Kind = "qa_comment_like",
        });
        await db.SaveChangesAsync(cancellationToken);

        await hub.Clients.Group(ChatHubGroupNames.ForUser(rid)).SendAsync(
            "notificationCreated",
            new { kind = "qa_comment_like", offerId = oid },
            cancellationToken);
    }

    public async Task NotifyRouteTramoSubscriptionRequestAsync(
        RouteTramoSubscriptionRequestNotificationArgs request,
        CancellationToken cancellationToken = default)
    {
        var tid = (request.ThreadId ?? "").Trim();
        if (tid.Length < 4 || request.RecipientUserIds.Count == 0)
            return;

        var carrier = (request.CarrierUserId ?? "").Trim();
        var preview = request.MessagePreview.Length > 500 ? request.MessagePreview[..500] + "…" : request.MessagePreview;
        var now = DateTimeOffset.UtcNow;
        var meta = string.IsNullOrWhiteSpace(request.MetaJson) ? null : request.MetaJson.Trim();
        if (meta is { Length: > 4000 })
            meta = meta[..4000];

        AddRouteTramoSubscribeRequestNotificationRows(
            request.RecipientUserIds, tid, preview, request.AuthorLabel, request.AuthorTrust, carrier, meta, now);
        await db.SaveChangesAsync(cancellationToken);
        await SendRouteTramoSubscribeRequestHubToRecipientsAsync(request.RecipientUserIds, tid, cancellationToken);
    }

    private void AddRouteTramoSubscribeRequestNotificationRows(
        IReadOnlyCollection<string> recipientUserIds,
        string tid,
        string preview,
        string authorLabel,
        int authorTrust,
        string carrier,
        string? meta,
        DateTimeOffset now)
    {
        foreach (var raw in recipientUserIds)
        {
            var rid = (raw ?? "").Trim();
            if (rid.Length == 0)
                continue;
            var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
            db.ChatNotifications.Add(new ChatNotificationRow
            {
                Id = nid,
                RecipientUserId = rid,
                ThreadId = tid,
                MessageId = null,
                OfferId = null,
                MessagePreview = preview,
                AuthorStoreName = authorLabel,
                AuthorTrustScore = authorTrust,
                SenderUserId = carrier,
                CreatedAtUtc = now,
                ReadAtUtc = null,
                Kind = "route_tramo_subscribe",
                MetaJson = meta,
            });
        }
    }

    private async Task SendRouteTramoSubscribeRequestHubToRecipientsAsync(
        IReadOnlyCollection<string> recipientUserIds,
        string tid,
        CancellationToken cancellationToken)
    {
        foreach (var raw in recipientUserIds)
        {
            var rid = (raw ?? "").Trim();
            if (rid.Length == 0)
                continue;
            await hub.Clients.Group(ChatHubGroupNames.ForUser(rid)).SendAsync(
                "notificationCreated",
                new { kind = "route_tramo_subscribe", threadId = tid },
                cancellationToken);
        }
    }

    public async Task NotifyRouteTramoSubscriptionAcceptedAsync(
        RouteTramoSubscriptionAcceptedNotificationArgs request,
        CancellationToken cancellationToken = default)
    {
        var tid = (request.ThreadId ?? "").Trim();
        var cid = (request.CarrierUserId ?? "").Trim();
        if (tid.Length < 4 || cid.Length < 2)
            return;

        var preview = request.MessagePreview.Length > 500 ? request.MessagePreview[..500] + "…" : request.MessagePreview;
        var now = DateTimeOffset.UtcNow;
        var meta = string.IsNullOrWhiteSpace(request.MetaJson) ? null : request.MetaJson.Trim();
        await NotifyCarrierOfRouteTramoSubscriptionAcceptedCoreAsync(
            cid, tid, preview, request.DeciderLabel, request.DeciderTrust, request.DeciderUserId, now, meta, cancellationToken);
        await TryNotifyRouteTramoAcceptedSellerInboxAsync(
            tid,
            cid,
            request.SellerInboxUserId,
            request.SellerInboxPreview,
            request.SellerInboxSubjectLabel,
            request.SellerInboxSubjectTrust,
            now,
            meta,
            cancellationToken);
    }

    private async Task NotifyCarrierOfRouteTramoSubscriptionAcceptedCoreAsync(
        string carrierId,
        string threadId,
        string preview,
        string deciderLabel,
        int deciderTrust,
        string deciderUserId,
        DateTimeOffset now,
        string? metaJson,
        CancellationToken cancellationToken)
    {
        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = carrierId,
            ThreadId = threadId,
            MessageId = null,
            OfferId = null,
            MessagePreview = preview,
            AuthorStoreName = (deciderLabel ?? "").Trim().Length > 0 ? deciderLabel.Trim() : "Participante",
            AuthorTrustScore = deciderTrust,
            SenderUserId = (deciderUserId ?? "").Trim(),
            CreatedAtUtc = now,
            ReadAtUtc = null,
            Kind = "route_tramo_subscribe_accepted",
            MetaJson = metaJson,
        });
        await db.SaveChangesAsync(cancellationToken);
        await hub.Clients.Group(ChatHubGroupNames.ForUser(carrierId)).SendAsync(
            "notificationCreated",
            new { kind = "route_tramo_subscribe_accepted", threadId = threadId },
            cancellationToken);
    }

    private async Task TryNotifyRouteTramoAcceptedSellerInboxAsync(
        string tid,
        string carrierId,
        string? sellerInboxUserId,
        string? sellerInboxPreview,
        string? sellerInboxSubjectLabel,
        int sellerInboxSubjectTrust,
        DateTimeOffset now,
        string? metaJson,
        CancellationToken cancellationToken)
    {
        var sid = (sellerInboxUserId ?? "").Trim();
        var spv = (sellerInboxPreview ?? "").Trim();
        if (sid.Length == 0
            || spv.Length == 0
            || string.Equals(sid, carrierId, StringComparison.Ordinal))
            return;
        var sl = (sellerInboxSubjectLabel ?? "").Trim();
        if (sl.Length == 0)
            sl = "Transportista";
        var nid2 = "cn_" + Guid.NewGuid().ToString("N")[..16];
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid2,
            RecipientUserId = sid,
            ThreadId = tid,
            MessageId = null,
            OfferId = null,
            MessagePreview = spv.Length > 500 ? spv[..500] + "…" : spv,
            AuthorStoreName = sl,
            AuthorTrustScore = sellerInboxSubjectTrust,
            SenderUserId = carrierId,
            CreatedAtUtc = now,
            ReadAtUtc = null,
            Kind = "route_tramo_subscribe_accepted",
            MetaJson = metaJson,
        });
        await db.SaveChangesAsync(cancellationToken);
        await hub.Clients.Group(ChatHubGroupNames.ForUser(sid)).SendAsync(
            "notificationCreated",
            new { kind = "route_tramo_subscribe_accepted", threadId = tid },
            cancellationToken);
    }

    public async Task NotifyRouteTramoSubscriptionRejectedAsync(
        RouteTramoSubscriptionRejectedNotificationArgs request,
        CancellationToken cancellationToken = default)
    {
        var tid = (request.ThreadId ?? "").Trim();
        var cid = (request.CarrierUserId ?? "").Trim();
        if (tid.Length < 4 || cid.Length < 2)
            return;

        var oid = (request.RouteOfferId ?? "").Trim();
        var preview = request.MessagePreview.Length > 500 ? request.MessagePreview[..500] + "…" : request.MessagePreview;
        var now = DateTimeOffset.UtcNow;
        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = cid,
            ThreadId = tid,
            MessageId = null,
            OfferId = oid.Length > 0 ? oid : null,
            MessagePreview = preview,
            AuthorStoreName = (request.SellerLabel ?? "").Trim().Length > 0 ? request.SellerLabel.Trim() : "Vendedor",
            AuthorTrustScore = request.SellerTrust,
            SenderUserId = (request.SellerUserId ?? "").Trim(),
            CreatedAtUtc = now,
            ReadAtUtc = null,
            Kind = "route_tramo_subscribe_rejected",
            MetaJson = null,
        });

        await db.SaveChangesAsync(cancellationToken);

        await hub.Clients.Group(ChatHubGroupNames.ForUser(cid)).SendAsync(
            "notificationCreated",
            new
            {
                kind = "route_tramo_subscribe_rejected",
                threadId = tid,
                offerId = oid.Length > 0 ? oid : null,
            },
            cancellationToken);
    }

    public async Task NotifyRouteTramoSellerExpelledAsync(
        RouteTramoSellerExpelledNotificationArgs request,
        CancellationToken cancellationToken = default)
    {
        var tid = (request.ThreadId ?? "").Trim();
        var cid = (request.CarrierUserId ?? "").Trim();
        if (tid.Length < 4 || cid.Length < 2)
            return;

        var preview = request.MessagePreview.Length > 500 ? request.MessagePreview[..500] + "…" : request.MessagePreview;
        var r = (request.Reason ?? "").Trim();
        var meta = System.Text.Json.JsonSerializer.Serialize(new { reason = r });
        var oid = (request.RouteOfferId ?? "").Trim();
        var now = DateTimeOffset.UtcNow;
        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = cid,
            ThreadId = tid,
            MessageId = null,
            OfferId = oid.Length > 0 ? oid : null,
            MessagePreview = preview,
            AuthorStoreName = (request.SellerLabel ?? "").Trim().Length > 0 ? request.SellerLabel.Trim() : "Vendedor",
            AuthorTrustScore = request.SellerTrust,
            SenderUserId = (request.SellerUserId ?? "").Trim(),
            CreatedAtUtc = now,
            ReadAtUtc = null,
            Kind = "route_tramo_seller_expelled",
            MetaJson = meta,
        });
        await db.SaveChangesAsync(cancellationToken);
        await hub.Clients.Group(ChatHubGroupNames.ForUser(cid)).SendAsync(
            "notificationCreated",
            new
            {
                kind = "route_tramo_seller_expelled",
                threadId = tid,
                offerId = oid.Length > 0 ? oid : null,
            },
            cancellationToken);
    }

    public async Task NotifyRouteSheetPreselectedTransportistaAsync(
        RouteSheetPreselectedTransportistaNotificationArgs request,
        CancellationToken cancellationToken = default)
    {
        var rid = (request.RecipientUserId ?? "").Trim();
        var tid = (request.ThreadId ?? "").Trim();
        var oid = (request.OfferId ?? "").Trim();
        var rsid = (request.RouteSheetId ?? "").Trim();
        if (rid.Length < 2 || tid.Length < 4 || oid.Length < 2 || rsid.Length < 1)
            return;

        var preview = request.MessagePreview.Length > 500
            ? request.MessagePreview[..500] + "…"
            : request.MessagePreview;
        var stopIds = request.StopIds?
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        var metaDict = new Dictionary<string, object?> { ["routeSheetId"] = rsid };
        if (stopIds.Length > 0)
            metaDict["stopIds"] = stopIds;
        var meta = JsonSerializer.Serialize(metaDict, RouteSheetJson.Options);
        var now = DateTimeOffset.UtcNow;
        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = rid,
            ThreadId = tid,
            MessageId = null,
            OfferId = oid,
            MessagePreview = preview,
            AuthorStoreName = (request.AuthorLabel ?? "").Trim().Length > 0 ? request.AuthorLabel.Trim() : "Participante",
            AuthorTrustScore = request.AuthorTrust,
            SenderUserId = (request.SenderUserId ?? "").Trim(),
            CreatedAtUtc = now,
            ReadAtUtc = null,
            Kind = "route_sheet_presel",
            MetaJson = meta,
        });
        await db.SaveChangesAsync(cancellationToken);
        await hub.Clients.Group(ChatHubGroupNames.ForUser(rid)).SendAsync(
            "notificationCreated",
            new
            {
                kind = "route_sheet_presel",
                threadId = tid,
                offerId = oid,
                routeSheetId = rsid,
                stopIds = stopIds.Length > 0 ? stopIds : null,
            },
            cancellationToken);
    }

    public async Task NotifyRouteSheetPreselDeclinedByCarrierAsync(
        RouteSheetPreselDeclinedByCarrierNotificationArgs request,
        CancellationToken cancellationToken = default)
    {
        var sellerId = (request.SellerUserId ?? "").Trim();
        var tid = (request.ThreadId ?? "").Trim();
        var oid = (request.OfferId ?? "").Trim();
        var rsid = (request.RouteSheetId ?? "").Trim();
        var cid = (request.CarrierUserId ?? "").Trim();
        if (sellerId.Length < 2 || tid.Length < 4 || oid.Length < 2 || rsid.Length < 1 || cid.Length < 2)
            return;

        var preview = request.MessagePreview.Length > 500
            ? request.MessagePreview[..500] + "…"
            : request.MessagePreview;
        var meta = System.Text.Json.JsonSerializer.Serialize(new { routeSheetId = rsid, carrierUserId = cid });
        var now = DateTimeOffset.UtcNow;
        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = sellerId,
            ThreadId = tid,
            MessageId = null,
            OfferId = oid,
            MessagePreview = preview,
            AuthorStoreName = (request.CarrierDisplayName ?? "").Trim().Length > 0
                ? request.CarrierDisplayName.Trim()
                : "Transportista",
            AuthorTrustScore = request.CarrierTrustScore,
            SenderUserId = cid,
            CreatedAtUtc = now,
            ReadAtUtc = null,
            Kind = "route_sheet_presel_decl",
            MetaJson = meta,
        });
        await db.SaveChangesAsync(cancellationToken);
        await hub.Clients.Group(ChatHubGroupNames.ForUser(sellerId)).SendAsync(
            "notificationCreated",
            new
            {
                kind = "route_sheet_presel_decl",
                threadId = tid,
                offerId = oid,
                routeSheetId = rsid,
                carrierUserId = cid,
            },
            cancellationToken);
    }

    public async Task NotifyRouteLegHandoffReadyAsync(
        RouteLegHandoffReadyNotificationArgs request,
        CancellationToken cancellationToken = default)
    {
        var rid = (request.RecipientCarrierUserId ?? "").Trim();
        var tid = (request.ThreadId ?? "").Trim();
        var rsid = (request.RouteSheetId ?? "").Trim();
        var aid = (request.AgreementId ?? "").Trim();
        var sid = (request.RouteStopId ?? "").Trim();
        if (rid.Length < 2 || tid.Length < 4 || rsid.Length < 1 || aid.Length < 8 || sid.Length < 1)
            return;

        var preview = request.MessagePreview.Length > 500
            ? request.MessagePreview[..500] + "…"
            : request.MessagePreview;
        var meta = System.Text.Json.JsonSerializer.Serialize(new
        {
            routeSheetId = rsid,
            agreementId = aid,
            routeStopId = sid,
            threadId = tid,
        });
        var now = DateTimeOffset.UtcNow;
        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = rid,
            ThreadId = tid,
            MessageId = null,
            OfferId = null,
            MessagePreview = preview,
            AuthorStoreName = "Entrega",
            AuthorTrustScore = 0,
            SenderUserId = rid,
            CreatedAtUtc = now,
            ReadAtUtc = null,
            Kind = "rl_handoff_ready",
            MetaJson = meta,
        });
        await db.SaveChangesAsync(cancellationToken);
        await hub.Clients.Group(ChatHubGroupNames.ForUser(rid)).SendAsync(
            "notificationCreated",
            new
            {
                kind = "rl_handoff_ready",
                threadId = tid,
                routeSheetId = rsid,
                agreementId = aid,
                routeStopId = sid,
            },
            cancellationToken);
    }

    public async Task NotifyRouteOwnershipGrantedAsync(
        RouteOwnershipGrantedNotificationArgs request,
        CancellationToken cancellationToken = default)
    {
        var rid = (request.RecipientCarrierUserId ?? "").Trim();
        var tid = (request.ThreadId ?? "").Trim();
        var rsid = (request.RouteSheetId ?? "").Trim();
        var aid = (request.AgreementId ?? "").Trim();
        var sid = (request.RouteStopId ?? "").Trim();
        if (rid.Length < 2 || tid.Length < 4 || rsid.Length < 1 || aid.Length < 8 || sid.Length < 1)
            return;

        var preview = request.MessagePreview.Length > 500
            ? request.MessagePreview[..500] + "…"
            : request.MessagePreview;
        var meta = JsonSerializer.Serialize(new
        {
            routeSheetId = rsid,
            agreementId = aid,
            routeStopId = sid,
            threadId = tid,
        });
        var now = DateTimeOffset.UtcNow;
        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = rid,
            ThreadId = tid,
            MessageId = null,
            OfferId = null,
            MessagePreview = preview,
            AuthorStoreName = "Entrega",
            AuthorTrustScore = 0,
            SenderUserId = rid,
            CreatedAtUtc = now,
            ReadAtUtc = null,
            Kind = "rl_ownership_granted",
            MetaJson = meta,
        });
        await db.SaveChangesAsync(cancellationToken);
        await hub.Clients.Group(ChatHubGroupNames.ForUser(rid)).SendAsync(
            "notificationCreated",
            new
            {
                kind = "rl_ownership_granted",
                threadId = tid,
                routeSheetId = rsid,
                agreementId = aid,
                routeStopId = sid,
            },
            cancellationToken);
    }

    public async Task NotifyRouteLegProximityAsync(
        RouteLegProximityNotificationArgs request,
        CancellationToken cancellationToken = default)
    {
        var rid = (request.RecipientUserId ?? "").Trim();
        var tid = (request.ThreadId ?? "").Trim();
        var rsid = (request.RouteSheetId ?? "").Trim();
        var aid = (request.AgreementId ?? "").Trim();
        var sid = (request.RouteStopId ?? "").Trim();
        if (rid.Length < 2 || tid.Length < 4 || rsid.Length < 1 || aid.Length < 8 || sid.Length < 1)
            return;

        var preview = request.MessagePreview.Length > 500
            ? request.MessagePreview[..500] + "…"
            : request.MessagePreview;
        var meta = System.Text.Json.JsonSerializer.Serialize(new
        {
            routeSheetId = rsid,
            agreementId = aid,
            routeStopId = sid,
            threadId = tid,
        });
        var now = DateTimeOffset.UtcNow;
        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = rid,
            ThreadId = tid,
            MessageId = null,
            OfferId = null,
            MessagePreview = preview,
            AuthorStoreName = "Entrega",
            AuthorTrustScore = 0,
            SenderUserId = rid,
            CreatedAtUtc = now,
            ReadAtUtc = null,
            Kind = "rl_proximity",
            MetaJson = meta,
        });
        await db.SaveChangesAsync(cancellationToken);
        await hub.Clients.Group(ChatHubGroupNames.ForUser(rid)).SendAsync(
            "notificationCreated",
            new
            {
                kind = "rl_proximity",
                threadId = tid,
                routeSheetId = rsid,
                agreementId = aid,
                routeStopId = sid,
            },
            cancellationToken);
    }

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
            },
            cancellationToken);
    }

    public async Task NotifySellerStoreTrustPenaltyAsync(
        SellerStoreTrustPenaltyNotificationArgs request,
        CancellationToken cancellationToken = default)
    {
        var sellerId = (request.SellerUserId ?? "").Trim();
        if (sellerId.Length < 2)
            return;

        var tid = (request.ThreadId ?? "").Trim();
        var oid = (request.OfferId ?? "").Trim();
        var preview = request.MessagePreview.Length > 500
            ? request.MessagePreview[..500] + "…"
            : request.MessagePreview;
        var meta = JsonSerializer.Serialize(new
        {
            delta = request.Delta,
            balanceAfter = request.BalanceAfter,
        });
        var now = DateTimeOffset.UtcNow;
        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = sellerId,
            ThreadId = tid.Length >= 4 ? tid : null,
            MessageId = null,
            OfferId = oid.Length >= 2 ? oid : null,
            MessagePreview = preview,
            AuthorStoreName = "Confianza de la tienda",
            AuthorTrustScore = request.BalanceAfter,
            SenderUserId = sellerId,
            CreatedAtUtc = now,
            ReadAtUtc = null,
            Kind = "store_trust_penalty",
            MetaJson = meta,
        });
        await db.SaveChangesAsync(cancellationToken);
        await hub.Clients.Group(ChatHubGroupNames.ForUser(sellerId)).SendAsync(
            "notificationCreated",
            new
            {
                kind = "store_trust_penalty",
                threadId = tid.Length >= 4 ? tid : null,
                offerId = oid.Length >= 2 ? oid : null,
                delta = request.Delta,
                balanceAfter = request.BalanceAfter,
            },
            cancellationToken);
    }

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
            emergentOfferId = eid.Length >= 4 && RecommendationBatchOfferLoader.IsEmergentPublicationId(eid) ? eid : null,
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
        return ChatMessageDtoFactory.FromThread(t, f.DisplayName, f.AvatarUrl);
    }

    public async Task<ChatThreadDto?> CreateOrGetThreadForBuyerAsync(
        string buyerUserId,
        string offerId,
        bool purchaseIntent = true,
        bool forceNewThread = false,
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

        // Sin unique en DB: el más reciente gana reutilizar si el cliente no pide hilo forzado.
        var existing = await db.ChatThreads
            .Where(
                x => x.OfferId == oid
                    && x.BuyerUserId == buyerUserId
                    && x.DeletedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (!forceNewThread && existing is not null)
        {
            var reused = await TryReuseOrArchiveExistingThreadForBuyerAsync(
                existing, purchaseIntent, cancellationToken);
            if (reused.Handled)
                return reused.Thread;
        }

        if (!await OfferIsListedForBuyerChatAsync(oid, cancellationToken))
            return null;

        return await CreateNewThreadForBuyerAndNotifyAsync(
            buyerUserId, oid, storeId, sellerUserId, purchaseIntent, cancellationToken);
    }

    private async Task<(bool Handled, ChatThreadDto? Thread)> TryReuseOrArchiveExistingThreadForBuyerAsync(
        ChatThreadRow existing,
        bool purchaseIntent,
        CancellationToken cancellationToken)
    {
        if (existing.BuyerExpelledAtUtc is not null)
        {
            // Comprador expulsado de este hilo: archivamos y se crea uno nuevo (misma oferta, nuevo id).
            existing.DeletedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return (Handled: false, Thread: null);
        }

        if (purchaseIntent && !existing.PurchaseMode)
        {
            existing.PurchaseMode = true;
            await db.SaveChangesAsync(cancellationToken);
        }

        return (true, await MapThreadWithBuyerLabelAsync(existing, cancellationToken));
    }

    private async Task<ChatThreadDto?> CreateNewThreadForBuyerAndNotifyAsync(
        string buyerUserId,
        string offerId,
        string storeId,
        string sellerUserId,
        bool purchaseIntent,
        CancellationToken cancellationToken)
    {
        var id = "cth_" + Guid.NewGuid().ToString("N")[..16];
        var now = DateTimeOffset.UtcNow;
        var row = new ChatThreadRow
        {
            Id = id,
            OfferId = offerId,
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
        await hub.Clients.Group(ChatHubGroupNames.ForUser(dto.BuyerUserId)).SendAsync(
            "threadCreated",
            new { thread = dto },
            cancellationToken);
    }

    public async Task<ChatThreadDto?> GetThreadIfVisibleAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var tid = ChatThreadIds.NormalizePersistedId(threadId);
        if (tid.Length < 4)
            return null;

        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (t is null)
            return null;
        if (!await UserCanAccessThreadRowAsync(userId, t, cancellationToken))
            return null;
        return await MapThreadWithBuyerLabelAsync(t, cancellationToken);
    }

    public async Task<ChatThreadDto?> GetThreadByOfferIfVisibleAsync(
        string userId,
        string offerId,
        CancellationToken cancellationToken = default)
    {
        var uid = (userId ?? "").Trim();
        var oid = (offerId ?? "").Trim();
        ChatThreadRow? t;
        var (_, sellerUserId) = await ResolveOfferStoreAsync(oid, cancellationToken);
        if (sellerUserId is null)
            return null;

        if (uid == sellerUserId)
        {
            t = await db.ChatThreads.AsNoTracking()
                .Where(x => x.OfferId == oid && x.SellerUserId == sellerUserId && x.DeletedAtUtc == null)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            t = await db.ChatThreads.AsNoTracking()
                .Where(x => x.OfferId == oid && x.BuyerUserId == uid && x.DeletedAtUtc == null)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (t is null)
            return null;
        if (!await UserCanAccessThreadRowAsync(uid, t, cancellationToken))
            return null;
        return await MapThreadWithBuyerLabelAsync(t, cancellationToken);
    }

    private sealed record ListThreadsForUserMaterialized(
        IReadOnlyList<ChatThreadRow> Threads,
        IReadOnlyDictionary<string, ChatMessageRow> LastByThread,
        IReadOnlyDictionary<string, BuyerPublicFields> BuyerById);

    public async Task<IReadOnlyList<ChatThreadSummaryDto>> ListThreadsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var uid = (userId ?? "").Trim();
        if (uid.Length < 2)
            return Array.Empty<ChatThreadSummaryDto>();

        var data = await LoadListThreadsForUserDataOrNullAsync(uid, cancellationToken);
        if (data is null)
            return Array.Empty<ChatThreadSummaryDto>();

        return data.Threads.Select(t =>
        {
            data.LastByThread.TryGetValue(t.Id, out var lastMsg);
            data.BuyerById.TryGetValue(t.BuyerUserId, out var b);
            return ChatThreadSummaryMapper.ToDto(
                t,
                lastMsg,
                b?.DisplayName,
                b?.AvatarUrl);
        }).ToList();
    }

    private async Task<ListThreadsForUserMaterialized?> LoadListThreadsForUserDataOrNullAsync(
        string uid,
        CancellationToken cancellationToken)
    {
        var unionIds = await ListThreadsUnionIdListForUserOrNullAsync(uid, cancellationToken);
        if (unionIds is null)
            return null;
        return await BuildListThreadsForUserMaterializedAsync(unionIds, cancellationToken);
    }

    private async Task<IReadOnlyList<string>?> ListThreadsUnionIdListForUserOrNullAsync(
        string uid,
        CancellationToken cancellationToken)
    {
        // Comprador/vendedor (como antes) ∪ hilos donde el usuario es transportista con tramo pendiente o confirmado.
        var partyQuery = db.ChatThreads.AsNoTracking()
            .Where(t =>
                t.DeletedAtUtc == null
                && !(t.BuyerUserId == uid && t.BuyerExpelledAtUtc != null)
                && !(t.SellerUserId == uid && t.SellerExpelledAtUtc != null)
                && (t.InitiatorUserId == uid
                    || (t.IsSocialGroup && (t.BuyerUserId == uid || t.SellerUserId == uid))
                    || (t.FirstMessageSentAtUtc != null
                        && (t.BuyerUserId == uid || t.SellerUserId == uid))));

        var carrierQuery =
            from s in db.RouteTramoSubscriptions.AsNoTracking()
            join t in db.ChatThreads.AsNoTracking() on s.ThreadId equals t.Id
            where s.CarrierUserId == uid
                && s.Status != "rejected"
                && s.Status != "withdrawn"
                && t.DeletedAtUtc == null
                && !(t.BuyerUserId == uid && t.BuyerExpelledAtUtc != null)
                && !(t.SellerUserId == uid && t.SellerExpelledAtUtc != null)
            select t;

        var partyIds = await partyQuery.Select(t => t.Id).ToListAsync(cancellationToken);
        var carrierIds = await carrierQuery.Select(t => t.Id).Distinct().ToListAsync(cancellationToken);
        var socialExtraIds = await db.ChatSocialGroupMembers.AsNoTracking()
            .Where(m => m.UserId == uid)
            .Join(db.ChatThreads.AsNoTracking(), m => m.ThreadId, t => t.Id, (_, t) => t)
            .Where(t =>
                t.DeletedAtUtc == null
                && t.IsSocialGroup
                && !(t.BuyerUserId == uid && t.BuyerExpelledAtUtc != null)
                && !(t.SellerUserId == uid && t.SellerExpelledAtUtc != null))
            .Select(t => t.Id)
            .Distinct()
            .ToListAsync(cancellationToken);
        var unionIds = partyIds.Union(carrierIds).Union(socialExtraIds).ToList();
        return unionIds.Count == 0 ? null : unionIds;
    }

    private async Task<ListThreadsForUserMaterialized?> BuildListThreadsForUserMaterializedAsync(
        IReadOnlyList<string> unionIds,
        CancellationToken cancellationToken)
    {
        var threads = await db.ChatThreads.AsNoTracking()
            .Where(t => unionIds.Contains(t.Id))
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var ids = threads.Select(t => t.Id).ToList();
        if (ids.Count == 0)
            return null;

        var allMsgs = await db.ChatMessages.AsNoTracking()
            .Where(m => ids.Contains(m.ThreadId) && m.DeletedAtUtc == null)
            .ToListAsync(cancellationToken);
        var lastByThread = allMsgs
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
        var buyerById = buyerRows.ToDictionary(
            x => x.Id,
            x => new BuyerPublicFields(
                string.IsNullOrWhiteSpace(x.DisplayName) ? null : x.DisplayName.Trim(),
                string.IsNullOrWhiteSpace(x.AvatarUrl) ? null : x.AvatarUrl.Trim()));

        return new ListThreadsForUserMaterialized(threads, lastByThread, buyerById);
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

        if (!await UserCanAccessThreadRowAsync(uid, t, cancellationToken))
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
        var others = await GetThreadParticipantUserIdsAsync(t, cancellationToken);
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

        var set = await GetThreadParticipantUserIdsAsync(t, cancellationToken).ConfigureAwait(false);
        return set.ToList();
    }

    /// <inheritdoc />
    public async Task<ChatThreadDto?> CreateSocialGroupThreadAsync(
        string creatorUserId,
        IReadOnlyList<string> otherUserIds,
        CancellationToken cancellationToken = default)
    {
        var creator = (creatorUserId ?? "").Trim();
        if (creator.Length < 2)
            return null;

        var others = otherUserIds
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (others.Count == 0)
            return null;
        if (others.Any(u => string.Equals(u, creator, StringComparison.Ordinal)))
            return null;

        var allIds = others.Append(creator).ToList();
        var existingCount = await db.UserAccounts.AsNoTracking()
            .Where(u => allIds.Contains(u.Id))
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existingCount != allIds.Count)
            return null;

        var store = await db.Stores.AsNoTracking()
            .Where(s => s.OwnerUserId == creator)
            .OrderBy(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (store is null)
            return null;

        var buyerId = creator;
        var sellerId = others[0];
        var rest = others.Skip(1).ToList();

        var id = "cth_" + Guid.NewGuid().ToString("N")[..16];
        var now = DateTimeOffset.UtcNow;
        var row = new ChatThreadRow
        {
            Id = id,
            OfferId = SocialThreadOfferId,
            StoreId = store.Id,
            BuyerUserId = buyerId,
            SellerUserId = sellerId,
            InitiatorUserId = creator,
            FirstMessageSentAtUtc = null,
            CreatedAtUtc = now,
            PurchaseMode = false,
            IsSocialGroup = true,
        };
        db.ChatThreads.Add(row);

        foreach (var uid in rest)
        {
            db.ChatSocialGroupMembers.Add(new ChatSocialGroupMemberRow
            {
                Id = "csgm_" + Guid.NewGuid().ToString("N")[..12],
                ThreadId = id,
                UserId = uid,
                JoinedAtUtc = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var dto = await MapThreadWithBuyerLabelAsync(row, cancellationToken).ConfigureAwait(false);

        var notifyIds = new HashSet<string>(StringComparer.Ordinal) { buyerId, sellerId };
        foreach (var u in rest)
            notifyIds.Add(u);
        foreach (var n in notifyIds)
        {
            await hub.Clients.Group(ChatHubGroupNames.ForUser(n)).SendAsync(
                    "threadCreated",
                    new { thread = dto },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return dto;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatThreadMemberDto>?> ListSocialThreadMembersAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var tid = ChatThreadIds.NormalizePersistedId(threadId);
        if (tid.Length < 4)
            return null;

        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (t is null || t.DeletedAtUtc is not null)
            return null;
        if (!await UserCanAccessThreadRowAsync(userId, t, cancellationToken).ConfigureAwait(false))
            return null;
        if (!t.IsSocialGroup)
            return null;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(t.BuyerUserId) && t.BuyerExpelledAtUtc is null)
            ids.Add(t.BuyerUserId.Trim());
        if (!string.IsNullOrWhiteSpace(t.SellerUserId) && t.SellerExpelledAtUtc is null)
            ids.Add(t.SellerUserId.Trim());
        var extra = await db.ChatSocialGroupMembers.AsNoTracking()
            .Where(m => m.ThreadId == tid)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var u in extra)
        {
            var x = (u ?? "").Trim();
            if (x.Length >= 2)
                ids.Add(x);
        }

        var idList = ids.ToList();
        if (idList.Count == 0)
            return Array.Empty<ChatThreadMemberDto>();

        var userRows = await db.UserAccounts.AsNoTracking()
            .Where(u => idList.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.AvatarUrl })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        string SortKey(string uid)
        {
            var row = userRows.FirstOrDefault(u => u.Id == uid);
            var dn = row?.DisplayName;
            if (!string.IsNullOrWhiteSpace(dn))
                return dn.Trim();
            return uid;
        }

        var ordered = idList.OrderBy(SortKey, StringComparer.OrdinalIgnoreCase).ToList();

        return ordered.Select(uid =>
        {
            var row = userRows.FirstOrDefault(u => u.Id == uid);
            var dn = row?.DisplayName is { Length: > 0 } s ? s.Trim() : null;
            var av = row?.AvatarUrl is { Length: > 0 } a ? a.Trim() : null;
            return new ChatThreadMemberDto(uid, dn, av);
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<ChatThreadDto?> PatchSocialGroupTitleAsync(
        string userId,
        string threadId,
        string? title,
        CancellationToken cancellationToken = default)
    {
        var tid = ChatThreadIds.NormalizePersistedId(threadId);
        if (tid.Length < 4)
            return null;

        var t = await db.ChatThreads
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (t is null || t.DeletedAtUtc is not null)
            return null;
        if (!t.IsSocialGroup)
            return null;
        if (!string.Equals(t.InitiatorUserId, userId, StringComparison.Ordinal))
            return null;

        string? normalized = null;
        if (!string.IsNullOrWhiteSpace(title))
        {
            var tr = title.Trim();
            if (tr.Length > 120)
                tr = tr[..120];
            normalized = tr;
        }

        t.SocialGroupTitle = normalized;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await MapThreadWithBuyerLabelAsync(t, cancellationToken).ConfigureAwait(false);
    }
}

