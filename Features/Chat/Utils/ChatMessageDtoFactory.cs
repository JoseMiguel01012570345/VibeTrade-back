using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using ChatMessageDto = VibeTrade.Backend.Features.Chat.ChatMessageDto;
using ChatThreadDto = VibeTrade.Backend.Features.Chat.ChatThreadDto;

namespace VibeTrade.Backend.Features.Chat.Utils;

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
            t.PartyExitedAtUtc);
}
