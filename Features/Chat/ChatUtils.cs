using System.Text.Json;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;
using VibeTrade.Backend.Utils;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Chat.Core;
using RouteTramoItemDto = global::VibeTrade.Backend.Features.Chat.Interfaces.RouteTramoSubscriptionItemDto;

namespace VibeTrade.Backend.Features.Chat;

public static class ChatGroupReceiptsJsonUtil
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static ChatMessageGroupReceipts Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ChatMessageGroupReceipts();
        try
        {
            return JsonSerializer.Deserialize<ChatMessageGroupReceipts>(json, Options)
                ?? new ChatMessageGroupReceipts();
        }
        catch
        {
            return new ChatMessageGroupReceipts();
        }
    }

    public static string Serialize(ChatMessageGroupReceipts r) =>
        JsonSerializer.Serialize(r, Options);
}

public static class ChatHubGroupNames
{
    public static string ForUser(string userId) => $"user:{(userId ?? "").Trim()}";
    public static string ForOffer(string offerId) => $"offer:{(offerId ?? "").Trim()}";
    public static string ForThread(string threadId) =>
        $"thread:{ChatThreadIds.NormalizePersistedId(threadId)}";
}

public static class ChatMediaUrlRules
{
    public static bool IsAllowedPersisted(string url)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0 || !url.StartsWith("/", StringComparison.Ordinal))
            return false;
        if (url.Contains("..", StringComparison.Ordinal))
            return false;
        return url.StartsWith("/api/v1/media/", StringComparison.Ordinal);
    }
}

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

    public static ChatMessageDto FromRowWithStatus(
        ChatMessageRow m,
        ChatMessageStatus displayStatus,
        string? senderDisplayLabel = null) =>
        new(
            m.Id,
            m.ThreadId,
            m.SenderUserId,
            m.Payload,
            displayStatus,
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
            t.PartyExitedAtUtc,
            t.IsSocialGroup,
            string.IsNullOrWhiteSpace(t.SocialGroupTitle) ? null : t.SocialGroupTitle.Trim());
}

public static class ChatMessagePreviewText
{
    public static string FromPayload(ChatMessagePayload payload) =>
        payload switch
        {
            ChatTextPayload p => PreviewText(p.Text),
            ChatAudioPayload => "Nota de voz",
            ChatImagePayload p => string.IsNullOrWhiteSpace(p.Caption) ? "Foto" : p.Caption!.Trim(),
            ChatDocPayload p => string.IsNullOrWhiteSpace(p.Name) ? "Documento" : p.Name.Trim(),
            ChatDocsBundlePayload p => p.Documents.Count switch
            {
                0 => "Documento",
                1 => string.IsNullOrWhiteSpace(p.Documents[0].Name) ? "Documento" : p.Documents[0].Name.Trim(),
                var n => $"{n} documentos",
            },
            ChatAgreementPayload p => string.IsNullOrWhiteSpace(p.Title)
                ? "Acuerdo"
                : $"Acuerdo: {p.Title.Trim()}",
            ChatSystemTextPayload p => PreviewText(p.Text),
            ChatPaymentFeeReceiptPayload p =>
                string.IsNullOrWhiteSpace(p.AgreementTitle)
                    ? "Recibo de pago (PDF)"
                    : $"Recibo de pago: {p.AgreementTitle.Trim()}",
            ChatCertificatePayload p => string.IsNullOrWhiteSpace(p.Title)
                ? "Certificado"
                : p.Title.Trim(),
            _ => "Mensaje",
        };

    private static string PreviewText(string tx)
    {
        tx = tx.Trim();
        return tx.Length == 0 ? "Mensaje" : tx;
    }
}

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

internal static class ChatPostPayloadValidation
{
    public static bool TryParseAndValidateImagePayload(PostChatMessageBody b, out ChatImagePayload? parsed)
    {
        parsed = null;
        if (b.Images is not { Count: > 0 })
            return false;

        foreach (var img in b.Images)
        {
            if (!ChatMediaUrlRules.IsAllowedPersisted(img.Url))
                return false;
        }

        if (b.EmbeddedAudio is not null)
        {
            if (!ChatMediaUrlRules.IsAllowedPersisted(b.EmbeddedAudio.Url))
                return false;
            if (b.EmbeddedAudio.Seconds is < 1 or > 3600)
                return false;
        }

        if (b.Caption is { Length: > 4000 })
            return false;

        parsed = new ChatImagePayload
        {
            Images = b.Images,
            Caption = b.Caption,
            EmbeddedAudio = b.EmbeddedAudio,
        };
        return true;
    }

    public static bool TryParseAndValidateDocsBundlePayload(PostChatMessageBody b, out ChatDocsBundlePayload? parsed)
    {
        parsed = null;
        if (b.Documents is not { Count: > 0 })
            return false;

        foreach (var d in b.Documents)
        {
            if (string.IsNullOrWhiteSpace(d.Name))
                return false;
            if (d.Url is not null && !ChatMediaUrlRules.IsAllowedPersisted(d.Url))
                return false;
        }

        if (b.EmbeddedAudio is not null)
        {
            if (!ChatMediaUrlRules.IsAllowedPersisted(b.EmbeddedAudio.Url))
                return false;
            if (b.EmbeddedAudio.Seconds is < 1 or > 3600)
                return false;
        }

        if (b.Caption is { Length: > 4000 })
            return false;

        parsed = new ChatDocsBundlePayload
        {
            Documents = b.Documents,
            Caption = b.Caption,
            EmbeddedAudio = b.EmbeddedAudio,
        };
        return true;
    }
}

public static class ChatReplyToIdsFromPayload
{
    public static IReadOnlyList<string>? ReadList(PostChatMessageBody body) =>
        body.ReplyToIds is { Count: > 0 } l ? l : null;
}

public static class ChatThreadAccess
{
    private static string NormId(string? s) => (s ?? "").Trim();

    public static bool UserCanSeeThread(string userId, ChatThreadRow t) =>
        t.DeletedAtUtc is null
        && (NormId(t.InitiatorUserId) == NormId(userId)
            || (t.IsSocialGroup
                && (NormId(t.BuyerUserId) == NormId(userId)
                    || NormId(t.SellerUserId) == NormId(userId)))
            || (t.FirstMessageSentAtUtc is not null
                && (NormId(t.BuyerUserId) == NormId(userId)
                    || NormId(t.SellerUserId) == NormId(userId))));

    public static bool UserIdsMatchLoose(string viewerId, string? storedCarrierId)
    {
        var a = (viewerId ?? "").Trim();
        var b = (storedCarrierId ?? "").Trim();
        if (a.Length == 0 || b.Length == 0)
            return false;
        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;
        static string Digits(string s) => string.Concat(s.Where(char.IsDigit));
        var da = Digits(a);
        var db = Digits(b);
        return da.Length >= 6 && db.Length >= 6 && string.Equals(da, db, StringComparison.Ordinal);
    }
}

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
            t.PartyExitedAtUtc,
            t.IsSocialGroup,
            string.IsNullOrWhiteSpace(t.SocialGroupTitle) ? null : t.SocialGroupTitle.Trim());
    }
}

internal static class RouteSheetPayloadPersistence
{
    public static RouteSheetPayload ClonePayloadForEfUpdate(RouteSheetPayload payload) =>
        JsonSerializer.Deserialize<RouteSheetPayload>(
            JsonSerializer.Serialize(payload, RouteSheetJson.Options),
            RouteSheetJson.Options) ?? payload;

    public static void ApplyPayloadAndTouch(ChatRouteSheetRow row, RouteSheetPayload mutatingPayload, DateTimeOffset now)
    {
        row.Payload = ClonePayloadForEfUpdate(mutatingPayload);
        row.UpdatedAtUtc = now;
    }
}

internal static class RouteTramoParadaResolver
{
    public static RouteStopPayload? FindByStopIdOrOrden(
        IReadOnlyList<RouteStopPayload> paradas,
        string? stopId,
        int stopOrden)
    {
        var sid = (stopId ?? "").Trim();
        if (sid.Length > 0)
        {
            var byId = paradas.FirstOrDefault(p =>
                string.Equals((p.Id ?? "").Trim(), sid, StringComparison.Ordinal));
            if (byId is not null)
                return byId;
        }
        if (stopOrden > 0)
            return paradas.FirstOrDefault(p => p.Orden == stopOrden);
        return null;
    }
}

internal static class RouteTramoSellerPresentation
{
    public static (string Label, int Trust) LabelAndTrust(StoreRow? store, UserAccount? actorAcc)
    {
        var label = !string.IsNullOrWhiteSpace(store?.Name) ? store!.Name.Trim()
            : string.IsNullOrWhiteSpace(actorAcc?.DisplayName) ? "Vendedor"
            : actorAcc!.DisplayName.Trim();
        var trust = store?.TrustScore ?? actorAcc?.TrustScore ?? 0;
        return (label, trust);
    }
}

internal static class RouteTramoSubscriptionDtoFilter
{
    public static List<RouteTramoItemDto> NarrowForCarrierViewer(
        string viewerUserId,
        List<RouteTramoItemDto> dtos)
    {
        var v = (viewerUserId ?? "").Trim();
        if (v.Length < 2)
            return [];
        return dtos
            .Where(dto =>
                string.Equals((dto.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase)
                || ChatThreadAccess.UserIdsMatchLoose(v, dto.CarrierUserId))
            .ToList();
    }
}

internal static class RouteTramoSubscriptionInputNormalize
{
    public static (string Tid, string Rsid, string Sid, string Uid) TrimTramoRequestKeys(
        string threadId,
        string routeSheetId,
        string stopId,
        string carrierUserId) => (
        (threadId ?? "").Trim(),
        (routeSheetId ?? "").Trim(),
        (stopId ?? "").Trim(),
        (carrierUserId ?? "").Trim());

    public static (string? Svc, string Label, string? PhoneSnap) NormalizeOptionalFields(
        string? storeServiceId,
        string transportServiceLabel,
        string? carrierContactPhone)
    {
        var label = (transportServiceLabel ?? "").Trim();
        if (label.Length > 512)
            label = label[..512];

        var svcTrim = string.IsNullOrWhiteSpace(storeServiceId) ? null : storeServiceId.Trim();
        if (svcTrim is { Length: > 64 })
            svcTrim = svcTrim[..64];

        var snap = (carrierContactPhone ?? "").Trim();
        if (snap.Length > 40)
            snap = snap[..40];
        if (snap.Length == 0)
            snap = null;

        return (svcTrim, label, snap);
    }
}

internal static class RouteTramoSubscriptionItemMapper
{
    public static RouteTramoItemDto MapRow(
        RouteTramoSubscriptionRow r,
        RouteSheetPayload? payload,
        IReadOnlyDictionary<string, UserAccount> accounts,
        IReadOnlyDictionary<string, string> serviceIdToStoreId)
    {
        var parada = (payload?.Paradas ?? []).FirstOrDefault(p =>
            string.Equals((p.Id ?? "").Trim(), r.StopId, StringComparison.Ordinal));
        var orden = parada?.Orden > 0 ? parada.Orden : r.StopOrden;
        var origen = (parada?.Origen ?? "").Trim();
        var destino = (parada?.Destino ?? "").Trim();
        if (origen.Length == 0 && destino.Length == 0)
        {
            origen = "—";
            destino = "—";
        }

        accounts.TryGetValue(r.CarrierUserId, out var acc);
        var display = RouteTramoUserContactUtil.CarrierDisplayOrDefault(acc?.DisplayName);
        var phone = RouteTramoUserContactUtil.BestPhoneForCarrier(acc, r.CarrierPhoneSnapshot, parada);
        var trust = acc?.TrustScore ?? 0;
        var status = RouteTramoSubscriptionStatusUtil.Normalized(r.Status);
        var createdMs = r.CreatedAtUtc.ToUnixTimeMilliseconds();
        string? svcStore = null;
        if (!string.IsNullOrWhiteSpace(r.StoreServiceId)
            && serviceIdToStoreId.TryGetValue(r.StoreServiceId.Trim(), out var st))
            svcStore = st;

        var avatarUrl = string.IsNullOrWhiteSpace(acc?.AvatarUrl) ? null : acc.AvatarUrl.Trim();

        return new RouteTramoItemDto(
            r.RouteSheetId,
            r.StopId,
            orden,
            r.CarrierUserId,
            display,
            phone,
            trust,
            r.StoreServiceId,
            r.TransportServiceLabel,
            status,
            origen,
            destino,
            createdMs,
            svcStore,
            avatarUrl);
    }
}

internal static class RouteTramoSubscriptionStatusUtil
{
    public static string Normalized(string? status) => (status ?? "pending").Trim().ToLowerInvariant();

    public static bool IsPendingForSellerDecision(RouteTramoSubscriptionRow r) =>
        Normalized(r.Status) is not ("confirmed" or "rejected" or "withdrawn");

    public static bool AllowsCarrierThreadParticipation(string? status) =>
        Normalized(status) is not ("rejected" or "withdrawn");
}

internal static class RouteTramoUserContactUtil
{
    public static string BestPhoneForCarrier(UserAccount? acc, string? subSnapshot, RouteStopPayload? parada)
    {
        var phone = (acc?.PhoneDisplay ?? "").Trim();
        if (phone.Length == 0 && !string.IsNullOrWhiteSpace(acc?.PhoneDigits))
            phone = acc!.PhoneDigits!.Trim();
        if (phone.Length == 0)
            phone = (subSnapshot ?? "").Trim();
        if (phone.Length == 0 && parada is not null)
            phone = (parada.TelefonoTransportista ?? "").Trim();
        return phone;
    }

    public static string CarrierDisplayOrDefault(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? "Transportista" : displayName.Trim();

    public static string ParticipanteOrDisplay(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? "Participante" : displayName!.Trim();
}

internal static class RouteTramoWithdrawSystemText
{
    public static string BuildAutomatedNotice(
        string whoDisplay,
        int nTramos,
        int nSheets,
        bool applyTrustPenalty,
        bool noPenaltyBecauseSheetsDelivered = false)
    {
        var who = (whoDisplay ?? "").Trim();
        if (who.Length == 0)
            who = "Un transportista";
        if (who.Length > 120)
            who = who[..120] + "…";
        var sys = nSheets <= 1
            ? $"{who} dejó de participar como transportista en este hilo (se retiró de {nTramos} tramo(s))."
            : $"{who} dejó de participar como transportista en este hilo (se retiró de {nTramos} tramo(s) en {nSheets} hojas de ruta).";
        if (applyTrustPenalty)
            sys += " Se aplicó un ajuste de confianza por retirarse como transportista con tramos confirmados (demo).";
        else if (noPenaltyBecauseSheetsDelivered)
            sys += " No aplica ajuste de confianza: los tramos confirmados estaban cerrados (hoja entregada o logística liquidada/vencida).";
        return sys;
    }
}
