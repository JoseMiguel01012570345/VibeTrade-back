using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Chat.Dtos;
using VibeTrade.Backend.Features.Market.Interfaces;
using VibeTrade.Backend.Utils;
using VibeTrade.Backend.Features.Chat;

namespace VibeTrade.Backend.Features.Chat.Core;

public sealed partial class ChatService(
    AppDbContext db,
    IBroadcastingService broadcasting,
    INotificationService notifications,
    IThreadAccessControlService threadAccess,
    IChatThreadSystemMessageService threadSystemMessages)
    : IChatService,
        IThreadManagementService,
        IMessageHandlingService,
        IParticipantManagementService,
        IOfferRelationService,
        IChatMessageInserter
{
    /// <summary>Oferta ficticia para hilos solo mensajería (lista/chat sin catálogo).</summary>
    private const string SocialThreadOfferId = "__vt_social__";

    /// <inheritdoc />
    public Task<bool> UserCanAccessThreadRowAsync(
        string userId,
        ChatThreadRow thread,
        CancellationToken cancellationToken = default)
        => threadAccess.UserCanAccessThreadRowAsync(userId, thread, cancellationToken);

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

    private async Task<ChatQueryHelpers.BuyerPublicFields> GetBuyerPublicFieldsAsync(
        string buyerUserId,
        CancellationToken cancellationToken)
    {
        var dict = await ChatQueryHelpers.GetBuyerPublicFieldsByIdsAsync(db, new[] { buyerUserId }, cancellationToken);
        return dict.TryGetValue(buyerUserId, out var fields) ? fields : new ChatQueryHelpers.BuyerPublicFields(null, null);
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
        await broadcasting.NotifyThreadCreatedToBuyerAsync(created, cancellationToken);
        return created;
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
        IReadOnlyDictionary<string, ChatQueryHelpers.BuyerPublicFields> BuyerById);

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

        var allMsgs = await ChatQueryHelpers.GetNonDeletedMessagesForThreadIdsAsync(db, ids, cancellationToken);
        var lastByThread = allMsgs
            .GroupBy(m => m.ThreadId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAtUtc).First());

        var buyerIds = threads
            .Select(x => x.BuyerUserId)
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();
        var buyerById = await ChatQueryHelpers.GetBuyerPublicFieldsByIdsAsync(db, buyerIds, cancellationToken);

        return new ListThreadsForUserMaterialized(threads, lastByThread, buyerById);
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
        await broadcasting.BroadcastThreadCreatedToUsersAsync(notifyIds, dto, cancellationToken).ConfigureAwait(false);

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

        var ids = await broadcasting.GetThreadParticipantUserIdsForThreadRowAsync(t, cancellationToken).ConfigureAwait(false);
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

        var myExpected = (await broadcasting.GetMessageRecipientUserIdsAsync(t, userId, cancellationToken))
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

    private static IReadOnlyList<string>? ReplyIdsFromQuotes(IReadOnlyList<ReplyQuoteDto>? quotes) =>
        quotes is null or { Count: 0 }
            ? null
            : quotes.Select(static q => q.MessageId).Where(static id => !string.IsNullOrWhiteSpace(id)).ToList();

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
        if (thread.IsSocialGroup)
        {
            var acc = await db.UserAccounts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == senderUserId, cancellationToken);
            if (acc == null)
                return "Usuario";
            if (!string.IsNullOrWhiteSpace(acc.DisplayName))
                return acc.DisplayName.Trim();
            if (!string.IsNullOrWhiteSpace(acc.PhoneDisplay))
                return acc.PhoneDisplay.Trim();
            if (!string.IsNullOrWhiteSpace(acc.PhoneDigits))
                return acc.PhoneDigits.Trim();
            return senderUserId.Length >= 6 ? senderUserId[..6] : "Usuario";
        }

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

        if (await ChatQueryHelpers.IsUserActiveCarrierOnThreadAsync(db, senderUserId, thread.Id, cancellationToken))
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
        var recipients = (await broadcasting.GetMessageRecipientUserIdsAsync(thread, senderUserId, cancellationToken))
            .ToList();
        AttachGroupReceiptsJsonForMultiRecipientMessage(row, recipients);
        if (recipients.Count > 0
            && await AllRecipientAccountsHaveValidSessionAsync(recipients, cancellationToken))
        {
            ChatMessageStatusUpdateCore.ApplyAllRecipientsSessionActiveAsDelivered(
                row, recipients, now);
        }

        await notifications.StageInAppNotificationsForMessageRecipientsAsync(
            recipients, thread, row, preview, senderUserId, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        if (sellerHadNotSeenThreadYet)
        {
            var threadDto = await MapThreadWithBuyerLabelAsync(thread, cancellationToken);
            await broadcasting.NotifyThreadCreatedToUserAsync(thread.SellerUserId, threadDto, cancellationToken);
        }

        var senderLabel = await GetParticipantAuthorLabelAsync(thread, senderUserId, cancellationToken);
        var dto = ChatMessageDtoFactory.FromRow(row, senderLabel);
        await broadcasting.BroadcastChatMessageCreatedAsync(thread, dto, cancellationToken);
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

        var sid = (senderUserId ?? "").Trim();
        if (sid.Length < 2)
            return null;
        var buyerId = (t.BuyerUserId ?? "").Trim();
        var sellerId = (t.SellerUserId ?? "").Trim();
        var isBuyerOrSeller =
            string.Equals(sid, buyerId, StringComparison.Ordinal)
            || string.Equals(sid, sellerId, StringComparison.Ordinal);
        var isSocialExtraMember =
            t.IsSocialGroup
            && await db.ChatSocialGroupMembers.AsNoTracking()
                .AnyAsync(m => m.ThreadId == t.Id && m.UserId == sid, cancellationToken);
        if (!isBuyerOrSeller
            && !isSocialExtraMember
            && !await ChatQueryHelpers.IsUserActiveCarrierOnThreadAsync(db, sid, t.Id, cancellationToken))
            return null;

        return await TryPostUnifiedUserChatMessageAsync(sid, t, body, cancellationToken);
    }

    private async Task<ChatMessageDto?> TryPostUnifiedUserChatMessageAsync(
        string senderUserId,
        ChatThreadRow t,
        PostChatMessageBody body,
        CancellationToken cancellationToken)
    {
        var payload = await BuildUnifiedUserMessagePayloadAsync(t, body, cancellationToken);
        return await InsertChatMessageAsync(t, senderUserId, payload, cancellationToken);
    }

    private async Task<ChatUnifiedMessagePayload> BuildUnifiedUserMessagePayloadAsync(
        ChatThreadRow t,
        PostChatMessageBody body,
        CancellationToken cancellationToken)
    {
        var replies = await BuildReplyQuotesAsync(t, body, cancellationToken);
        var replyIds = ReplyIdsFromQuotes(replies);
        return ComposeUnifiedUserMessagePayloadFromBody(body, replies, replyIds);
    }

    /// <summary>
    /// Arma un <see cref="ChatUnifiedMessagePayload"/> desde el cuerpo del POST sin ramificar por <c>type</c>:
    /// cada slot se rellena si los datos son válidos y si no queda vacío o null.
    /// </summary>
    private static ChatUnifiedMessagePayload ComposeUnifiedUserMessagePayloadFromBody(
        PostChatMessageBody body,
        IReadOnlyList<ReplyQuoteDto>? replies,
        IReadOnlyList<string>? replyIds)
    {
        var text = (body.Text ?? "").Trim();
        if (text.Length > 12_000)
            text = text[..12_000];

        string? offerQaId = body.OfferQaId is { } oqs && !string.IsNullOrWhiteSpace(oqs)
            ? oqs.Trim()
            : null;

        ChatImagePayload? imgParsed = null;
        if (body.Images is { Count: > 0 })
            ChatPostPayloadValidation.TryParseAndValidateImagePayload(body, out imgParsed);

        ChatDocsBundlePayload? docsParsed = null;
        IReadOnlyList<ChatDocumentDto>? documents = null;
        if (body.Documents is { Count: > 0 })
        {
            if (ChatPostPayloadValidation.TryParseAndValidateDocsBundlePayload(body, out docsParsed) && docsParsed is not null)
                documents = docsParsed.Documents;
        }
        else if (!string.IsNullOrWhiteSpace(body.Name)
            && body.Name.Length <= 500
            && (body.Url is null || ChatMediaUrlRules.IsAllowedPersisted(body.Url))
            && body.Kind is ("pdf" or "doc" or "other")
            && !string.IsNullOrWhiteSpace(body.Size)
            && body.Caption is null or { Length: <= 4000 })
        {
            documents =
            [
                new ChatDocumentDto
                {
                    Name = body.Name.Trim(),
                    Size = body.Size.Trim(),
                    Kind = body.Kind!,
                    Url = body.Url,
                },
            ];
        }

        string? voiceUrl = null;
        int? voiceSec = null;
        if (body.Seconds is { } secVoice
            && secVoice is >= 1 and <= 3600
            && !string.IsNullOrWhiteSpace(body.Url)
            && ChatMediaUrlRules.IsAllowedPersisted(body.Url))
        {
            voiceUrl = body.Url.Trim();
            voiceSec = secVoice;
        }

        ChatEmbeddedAudioDto? embedded = imgParsed?.EmbeddedAudio ?? docsParsed?.EmbeddedAudio;
        string? caption = imgParsed?.Caption ?? docsParsed?.Caption;
        if (documents is { Count: > 0 } && body.Documents is null && body.Caption is { Length: > 0 } c0)
            caption = string.IsNullOrWhiteSpace(caption) ? c0.Trim() : caption;

        return new ChatUnifiedMessagePayload
        {
            Text = text.Length > 0 ? text : null,
            OfferQaId = offerQaId,
            Images = imgParsed?.Images,
            Documents = documents,
            Caption = caption,
            EmbeddedAudio = embedded,
            VoiceUrl = voiceUrl,
            VoiceSeconds = voiceSec,
            RepliesTo = replies,
            ReplyToMessageIds = replyIds,
        };
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

        var fromRecipients = (await broadcasting.GetMessageRecipientUserIdsAsync(t, m.SenderUserId, cancellationToken))
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
        await broadcasting.TryBroadcastChatMessageStatusChangedAsync(
            t, tid, m, before, groupReceiptsJsonBefore, cancellationToken);
        return dto;
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
            await notifications.StageInAppNotificationForMessageRecipientAsync(
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

        await broadcasting.TryNotifySellersThreadCreatedAfterQaMessageInsertSyncAsync(
            hubRows,
            threads,
            MapThreadWithBuyerLabelAsync,
            cancellationToken);

        var byId = threads.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var hubItems = new List<(ChatThreadRow Thread, ChatMessageDto Message)>();
        foreach (var row in hubRows)
        {
            if (!byId.TryGetValue(row.ThreadId, out var thread))
                continue;
            var senderLabel = await GetParticipantAuthorLabelAsync(thread, row.SenderUserId, cancellationToken);
            hubItems.Add((thread, ChatMessageDtoFactory.FromRow(row, senderLabel)));
        }

        if (hubItems.Count > 0)
            await broadcasting.BroadcastChatMessagesCreatedAsync(hubItems, cancellationToken);
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
        sellerMsgs.Any(m => m.Payload is ChatUnifiedMessagePayload u && u.OfferQaId == qaId)
        || sellerMsgs.Any(m => m.Payload is ChatTextPayload text && text.OfferQaId == qaId);

    private ChatMessageRow CreateAndStageOfferQaAnswerMessageRow(
        ChatThreadRow thread,
        string qaId,
        string answer,
        DateTimeOffset now,
        List<ChatMessageRow> sellerMsgs,
        List<ChatMessageRow> hubRows)
    {
        var msgId = "cmg_" + Guid.NewGuid().ToString("N")[..16];
        var payloadObj = new ChatUnifiedMessagePayload
        {
            Text = answer,
            OfferQaId = qaId,
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

    public Task<ChatMessageDto?> PostAgreementAnnouncementAsync(
        PostAgreementAnnouncementArgs request,
        CancellationToken cancellationToken = default) =>
        threadSystemMessages.PostAgreementAnnouncementAsync(request, cancellationToken);

    public Task<ChatMessageDto?> PostSystemThreadNoticeAsync(
        string actorUserId,
        string threadId,
        string text,
        CancellationToken cancellationToken = default) =>
        threadSystemMessages.PostSystemThreadNoticeAsync(actorUserId, threadId, text, cancellationToken);

    public Task<ChatMessageDto?> PostAutomatedSystemThreadNoticeAsync(
        string threadId,
        string text,
        CancellationToken cancellationToken = default) =>
        threadSystemMessages.PostAutomatedSystemThreadNoticeAsync(threadId, text, cancellationToken);

    public Task<ChatMessageDto?> PostAutomatedPaymentFeeReceiptAsync(
        string threadId,
        ChatPaymentFeeReceiptData payload,
        CancellationToken cancellationToken = default) =>
        threadSystemMessages.PostAutomatedPaymentFeeReceiptAsync(threadId, payload, cancellationToken);
}

