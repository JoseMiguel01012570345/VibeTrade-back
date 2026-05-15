using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Notifications.NotificationDtos;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;

namespace VibeTrade.Backend.Features.Notifications;

/// <summary>In-app notifications, listado/marcado y filas <see cref="ChatNotificationRow"/> (SignalR <c>notificationCreated</c>).</summary>
public sealed class NotificationService(AppDbContext db, IHubContext<ChatHub> hub) : INotificationService
{
    public async Task NotifyOfferCommentAsync(
        OfferCommentNotificationArgs request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RecipientUserId))
            return;

        var preview = NotificationUtils.TruncatePreview(request.TextPreview);
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
        var preview = NotificationUtils.TruncatePreview(request.MessagePreview);
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

        var preview = NotificationUtils.TruncatePreview(request.MessagePreview);
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
            MessagePreview = NotificationUtils.TruncatePreview(spv),
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
        var preview = NotificationUtils.TruncatePreview(request.MessagePreview);
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

        var preview = NotificationUtils.TruncatePreview(request.MessagePreview);
        var r = (request.Reason ?? "").Trim();
        var meta = JsonSerializer.Serialize(new { reason = r });
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

        var preview = NotificationUtils.TruncatePreview(request.MessagePreview);
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

        var preview = NotificationUtils.TruncatePreview(request.MessagePreview);
        var meta = JsonSerializer.Serialize(new { routeSheetId = rsid, carrierUserId = cid });
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

        var preview = NotificationUtils.TruncatePreview(request.MessagePreview);
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

        var preview = NotificationUtils.TruncatePreview(request.MessagePreview);
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

        var preview = NotificationUtils.TruncatePreview(request.MessagePreview);
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

    public async Task NotifySellerStoreTrustPenaltyAsync(
        SellerStoreTrustPenaltyNotificationArgs request,
        CancellationToken cancellationToken = default)
    {
        var sellerId = (request.SellerUserId ?? "").Trim();
        if (sellerId.Length < 2)
            return;

        var tid = (request.ThreadId ?? "").Trim();
        var oid = (request.OfferId ?? "").Trim();
        var preview = NotificationUtils.TruncatePreview(request.MessagePreview);
        var meta = NotificationUtils.SerializeMeta(new
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

    /// <inheritdoc />
    public async Task StageInAppNotificationForMessageRecipientAsync(
        string recipientUserId,
        ChatThreadRow thread,
        ChatMessageRow message,
        string textPreview,
        string senderUserId,
        CancellationToken cancellationToken = default)
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

        var preview = NotificationUtils.TruncatePreview(textPreview);
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

    /// <inheritdoc />
    public async Task StageInAppNotificationsForMessageRecipientsAsync(
        IReadOnlyList<string> recipientUserIds,
        ChatThreadRow thread,
        ChatMessageRow message,
        string textPreview,
        string senderUserId,
        CancellationToken cancellationToken = default)
    {
        foreach (var rid in recipientUserIds)
            await StageInAppNotificationForMessageRecipientAsync(
                rid, thread, message, textPreview, senderUserId, cancellationToken);
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
    public async Task NotifyCounterpartyOfPartySoftLeaveAsync(
        ChatThreadRow t,
        string leaverUserId,
        bool leaverIsSeller,
        string reasonTrim,
        CancellationToken cancellationToken = default)
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
        body = NotificationUtils.TruncatePreview(body, maxLength: 497);
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

    /// <inheritdoc />
    public async Task<bool> TryPostPartySoftLeaveSystemThreadNoticeAsync(
        IChatThreadSystemMessageService threadSystemMessages,
        string userId,
        string threadId,
        bool isSeller,
        string reasonTrim,
        CancellationToken cancellationToken = default)
    {
        var notice = isSeller
            ? $"El vendedor salió del chat con un acuerdo ya aceptado. Motivo declarado: {reasonTrim}"
            : $"El comprador salió del chat con un acuerdo ya aceptado. Motivo declarado: {reasonTrim}";
        return await threadSystemMessages.PostSystemThreadNoticeAsync(userId, threadId, notice, cancellationToken) is not null;
    }
}
