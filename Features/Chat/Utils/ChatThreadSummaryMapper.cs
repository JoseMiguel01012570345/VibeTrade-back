using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using ChatThreadSummaryDto = VibeTrade.Backend.Features.Chat.ChatThreadSummaryDto;

namespace VibeTrade.Backend.Features.Chat.Utils;

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
            t.PartyExitedAtUtc);
    }
}
