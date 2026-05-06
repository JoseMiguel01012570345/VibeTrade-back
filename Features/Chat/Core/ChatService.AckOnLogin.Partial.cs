using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Chat.Utils;

namespace VibeTrade.Backend.Features.Chat.Core;

public sealed partial class ChatService
{
    private const int MaxMessagesPerThreadForLoginDeliveryAck = 500;

    public async Task<int> AckAllPendingIncomingDeliveredAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var uid = (userId ?? "").Trim();
        if (uid.Length < 2)
            return 0;
        var threadIds = await ListThreadsUnionIdListForUserOrNullAsync(uid, cancellationToken);
        if (threadIds is null || threadIds.Count == 0)
            return 0;
        var n = 0;
        foreach (var tid in threadIds)
        {
            if (string.IsNullOrWhiteSpace(tid))
                continue;
            var t = await db.ChatThreads.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
            if (t is null)
                continue;
            if (!await UserCanAccessThreadRowAsync(uid, t, cancellationToken))
                continue;
            var messages = await db.ChatMessages.AsNoTracking()
                .Where(
                    m => m.ThreadId == tid
                        && m.DeletedAtUtc == null
                        && m.SenderUserId != uid)
                .OrderByDescending(m => m.CreatedAtUtc)
                .Take(MaxMessagesPerThreadForLoginDeliveryAck)
                .ToListAsync(cancellationToken);
            foreach (var m in messages)
            {
                if (string.Equals(m.SenderUserId, uid, StringComparison.Ordinal))
                    continue;
                var fromRecipients = (await GetMessageRecipientUserIdsAsync(t, m.SenderUserId, cancellationToken))
                    .ToList();
                if (fromRecipients.Count == 0
                    || !ChatMessageStatusUpdateCore.InExpectedList(uid, fromRecipients))
                    continue;
                if (string.IsNullOrEmpty(m.GroupReceiptsJson))
                {
                    if (m.Status is ChatMessageStatus.Delivered or ChatMessageStatus.Read)
                        continue;
                }
                else
                {
                    var gr = ChatGroupReceiptsJsonUtil.Parse(m.GroupReceiptsJson);
                    IReadOnlyList<string> exp = ChatMessageStatusUpdateCore.MergedExpectedIds(
                        gr,
                        fromRecipients);
                    if (exp.Count > 1)
                    {
                        var c = ChatMessageStatusUpdateCore.CanonicalRecipientId(uid, exp);
                        if (gr.DeliveredUserIds.Any(
                                d => string.Equals(
                                    (d ?? "").Trim(),
                                    c,
                                    StringComparison.Ordinal)))
                            continue;
                    }
                    else if (m.Status is ChatMessageStatus.Delivered or ChatMessageStatus.Read)
                    {
                        continue;
                    }
                }
                var dto = await UpdateMessageStatusAsync(
                    new UpdateChatMessageStatusArgs(
                        uid,
                        tid,
                        m.Id,
                        ChatMessageStatus.Delivered),
                    cancellationToken);
                if (dto is not null)
                    n++;
            }
        }
        return n;
    }
}
