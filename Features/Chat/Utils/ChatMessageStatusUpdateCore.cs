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

        // JSON snapshot may omit or use different string forms than current thread recipients (e.g. carrier id
        // variants). Union with DB-derived recipients so CanonicalRecipientId / InExpectedList stay consistent.
        var merged = new List<string>(fromJson);
        foreach (var r in fromRecipients)
        {
            var rt = (r ?? "").Trim();
            if (rt.Length == 0)
                continue;
            if (merged.Any(e => RecipientIdsSamePerson(e, rt)))
                continue;
            merged.Add(rt);
        }
        return merged;
    }

    private static bool RecipientIdsSamePerson(string a, string b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal)
        || ChatThreadAccess.UserIdsMatchLoose((a ?? "").Trim(), b);

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

    /// <summary>
    /// Ticks que ve el <b>emisor</b> en grupo: <b>todos</b> con recibo de entrega (o lectura) → Entregado;
    /// todos leídos → Leído; si falta algún destinatario, Enviado.
    /// Misma condición <c>allD</c>/<c>allR</c> que <see cref="ApplyGroup"/>.
    /// </summary>
    public static ChatMessageStatus OutgoingGroupDisplayStatus(
        IReadOnlyList<string> expected,
        ChatMessageGroupReceipts receipts)
    {
        if (expected is not { Count: > 1 })
            return ChatMessageStatus.Sent;
        if (AllRecipientsRead(expected, receipts))
            return ChatMessageStatus.Read;
        if (AllRecipientsHaveDeliveryReceipt(expected, receipts))
            return ChatMessageStatus.Delivered;
        return ChatMessageStatus.Sent;
    }

    /// <summary>Igual que <c>allD</c> en <see cref="ApplyGroup"/>.</summary>
    public static bool AllRecipientsHaveDeliveryReceipt(
        IReadOnlyList<string> expected,
        ChatMessageGroupReceipts r) =>
        expected.All(
            e => r.DeliveredUserIds.Any(
                d => string.Equals((d ?? "").Trim(), (e ?? "").Trim(), StringComparison.Ordinal)));

    private static bool AllRecipientsRead(
        IReadOnlyList<string> expected,
        ChatMessageGroupReceipts r) =>
        expected.All(
            e => r.ReadUserIds.Any(
                d => string.Equals((d ?? "").Trim(), (e ?? "").Trim(), StringComparison.Ordinal)));

    /// <summary>
    /// Al publicar, si <b>todas</b> las cuentas destinatario tienen sesión Bearer activa, equivale a
    /// &quot;entregado en dispositivo en línea&quot; y pasa a <see cref="ChatMessageStatus.Delivered"/>
    /// (1:1 o grupo con <c>allD</c> en JSON).
    /// </summary>
    public static void ApplyAllRecipientsSessionActiveAsDelivered(
        ChatMessageRow m,
        IReadOnlyList<string> expected,
        DateTimeOffset now)
    {
        if (expected is not { Count: > 0 })
            return;
        if (m.Status != ChatMessageStatus.Sent)
            return;

        if (expected.Count == 1)
        {
            m.Status = ChatMessageStatus.Delivered;
            m.UpdatedAtUtc = now;
            return;
        }

        var receipts = ChatGroupReceiptsJsonUtil.Parse(m.GroupReceiptsJson);
        if (receipts.ExpectedRecipientIds.Count == 0)
            receipts.ExpectedRecipientIds = new List<string>(expected);
        foreach (var e in expected)
        {
            if (string.IsNullOrWhiteSpace(e))
                continue;
            var canonical = CanonicalRecipientId((e ?? "").Trim(), expected);
            if (receipts.DeliveredUserIds.Any(
                    d => string.Equals((d ?? "").Trim(), canonical, StringComparison.Ordinal)))
                continue;
            receipts.DeliveredUserIds.Add(canonical);
        }

        m.GroupReceiptsJson = ChatGroupReceiptsJsonUtil.Serialize(receipts);
        var allD = expected.All(
            e2 => receipts.DeliveredUserIds.Any(
                d => string.Equals((d ?? "").Trim(), (e2 ?? "").Trim(), StringComparison.Ordinal)));
        var allR = expected.All(
            e2 => receipts.ReadUserIds.Any(
                d => string.Equals((d ?? "").Trim(), (e2 ?? "").Trim(), StringComparison.Ordinal)));
        if (allR)
        {
            m.Status = ChatMessageStatus.Read;
            m.UpdatedAtUtc = now;
        }
        else if (allD)
        {
            m.Status = ChatMessageStatus.Delivered;
            m.UpdatedAtUtc = now;
        }
    }
}
