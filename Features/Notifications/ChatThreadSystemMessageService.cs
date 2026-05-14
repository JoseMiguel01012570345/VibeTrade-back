using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Chat.Core;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;

namespace VibeTrade.Backend.Features.Notifications;

/// <inheritdoc />
public sealed class ChatThreadSystemMessageService(
    AppDbContext db,
    IThreadAccessControlService threadAccess,
    Lazy<IChatMessageInserter> messageInserter) : IChatThreadSystemMessageService
{
    /// <inheritdoc />
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

        var payload = new ChatUnifiedMessagePayload
        {
            Agreement = new ChatUnifiedPlatformAgreementBlock
            {
                AgreementId = request.AgreementId.Trim(),
                Title = request.Title.Trim(),
                Body = "",
                Status = st,
            },
            IssuedByVibeTradePlatform = true,
        };
        return await messageInserter.Value.InsertChatMessageAsync(t, sellerUserId, payload, cancellationToken);
    }

    /// <inheritdoc />
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
        if (!await threadAccess.UserCanAccessThreadRowAsync(aid, t, cancellationToken))
            return null;
        var tx = (text ?? "").Trim();
        if (tx.Length == 0 || tx.Length > 12_000)
            return null;

        var payload = new ChatUnifiedMessagePayload
        {
            SystemText = tx,
            IssuedByVibeTradePlatform = false,
        };
        return await messageInserter.Value.InsertChatMessageAsync(t, aid, payload, cancellationToken);
    }

    /// <inheritdoc />
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

        var payload = new ChatUnifiedMessagePayload
        {
            SystemText = tx,
            IssuedByVibeTradePlatform = true,
        };
        return await messageInserter.Value.InsertChatMessageAsync(t, actorUserId, payload, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ChatMessageDto?> PostAutomatedPaymentFeeReceiptAsync(
        string threadId,
        ChatPaymentFeeReceiptData payload,
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

        var block = ChatUnifiedPlatformReceiptMapper.FromPayload(payload);
        var unified = new ChatUnifiedMessagePayload
        {
            PaymentFeeReceipt = block,
            IssuedByVibeTradePlatform = true,
        };
        return await messageInserter.Value.InsertChatMessageAsync(t, actorUserId, unified, cancellationToken);
    }
}
