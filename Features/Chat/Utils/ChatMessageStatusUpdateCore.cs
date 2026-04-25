using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Chat.Utils;

public static class ChatMessageStatusUpdateCore
{
    public static bool InExpectedList(string userId, IReadOnlyList<string> expected) =>
        expected.Any(
            e => string.Equals((e ?? "").Trim(), userId, StringComparison.Ordinal)
                || ChatThreadAccess.UserIdsMatchLoose(userId, (e ?? "").Trim()));

    public static string CanonicalRecipientId(string userId, IReadOnlyList<string> expected) =>
        expected.First(
            e => string.Equals((e ?? "").Trim(), userId, StringComparison.Ordinal)
                || ChatThreadAccess.UserIdsMatchLoose(userId, (e ?? "").Trim()));

    public static IReadOnlyList<string> MergedExpectedIds(ChatMessageGroupReceipts parsed, IReadOnlyList<string> fromRecipients)
    {
        var fromJson = parsed.ExpectedRecipientIds
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (fromJson.Count == 0)
            return fromRecipients;
        return fromJson;
    }

    public enum PairedApplyOutcome
    {
        RejectNull,
        ReturnDtoWithoutSave,
        MutatedSave,
    }

    public static PairedApplyOutcome TryApplyPaired(
        ChatMessageRow m,
        ChatMessageStatus request,
        DateTimeOffset now)
    {
        if (request == ChatMessageStatus.Delivered)
        {
            if (m.Status is ChatMessageStatus.Delivered or ChatMessageStatus.Read)
                return PairedApplyOutcome.ReturnDtoWithoutSave;
            if (m.Status != ChatMessageStatus.Sent)
                return PairedApplyOutcome.RejectNull;
            m.Status = ChatMessageStatus.Delivered;
            m.UpdatedAtUtc = now;
            return PairedApplyOutcome.MutatedSave;
        }
        if (m.Status == ChatMessageStatus.Read)
            return PairedApplyOutcome.ReturnDtoWithoutSave;
        if (m.Status is not (ChatMessageStatus.Sent or ChatMessageStatus.Delivered))
            return PairedApplyOutcome.RejectNull;
        m.Status = ChatMessageStatus.Read;
        m.UpdatedAtUtc = now;
        return PairedApplyOutcome.MutatedSave;
    }

    public static void ApplyGroup(
        ChatMessageGroupReceipts receipts,
        IReadOnlyList<string> expected,
        string canonical,
        ChatMessageStatus request,
        ChatMessageRow m,
        DateTimeOffset now)
    {
        if (receipts.ExpectedRecipientIds.Count == 0)
            receipts.ExpectedRecipientIds = new List<string>(expected);
        if (request == ChatMessageStatus.Delivered)
        {
            if (!receipts.DeliveredUserIds.Any(d => string.Equals((d ?? "").Trim(), canonical, StringComparison.Ordinal)))
                receipts.DeliveredUserIds.Add(canonical);
        }
        else
        {
            if (!receipts.ReadUserIds.Any(d => string.Equals((d ?? "").Trim(), canonical, StringComparison.Ordinal)))
                receipts.ReadUserIds.Add(canonical);
            if (!receipts.DeliveredUserIds.Any(d => string.Equals((d ?? "").Trim(), canonical, StringComparison.Ordinal)))
                receipts.DeliveredUserIds.Add(canonical);
        }
        m.GroupReceiptsJson = ChatGroupReceiptsJsonUtil.Serialize(receipts);
        var allD = expected.All(
            e => receipts.DeliveredUserIds.Any(
                d => string.Equals((d ?? "").Trim(), (e ?? "").Trim(), StringComparison.Ordinal)));
        var allR = expected.All(
            e => receipts.ReadUserIds.Any(
                d => string.Equals((d ?? "").Trim(), (e ?? "").Trim(), StringComparison.Ordinal)));
        if (allR)
        {
            m.Status = ChatMessageStatus.Read;
            m.UpdatedAtUtc = now;
        }
        else if (allD)
        {
            if (m.Status == ChatMessageStatus.Sent)
            {
                m.Status = ChatMessageStatus.Delivered;
                m.UpdatedAtUtc = now;
            }
        }
        else
        {
            m.UpdatedAtUtc = now;
        }
    }
}
