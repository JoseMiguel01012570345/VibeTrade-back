using System.Text.Json;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Chat;

/// <summary>Parámetros para <see cref="IChatService"/> cuando el método supera 3 argumentos lógicos (sin contar <see cref="CancellationToken"/>).</summary>
public sealed record OfferCommentNotificationArgs(
    string RecipientUserId,
    string OfferId,
    string TextPreview,
    string AuthorLabel,
    int AuthorTrust,
    string SenderUserId);

public sealed record OfferLikeNotificationArgs(
    string SellerUserId,
    string OfferId,
    string LikerLabel,
    int LikerTrust,
    string LikerSenderUserId);

public sealed record QaCommentLikeNotificationArgs(
    string CommentAuthorUserId,
    string OfferId,
    string LikerLabel,
    int LikerTrust,
    string LikerSenderUserId);

public sealed record RouteTramoSubscriptionRequestNotificationArgs(
    IReadOnlyCollection<string> RecipientUserIds,
    string ThreadId,
    string MessagePreview,
    string AuthorLabel,
    int AuthorTrust,
    string CarrierUserId,
    string? MetaJson = null);

public sealed record RouteTramoSubscriptionAcceptedNotificationArgs(
    string CarrierUserId,
    string ThreadId,
    string MessagePreview,
    string DeciderLabel,
    int DeciderTrust,
    string DeciderUserId,
    string? SellerInboxUserId = null,
    string? SellerInboxPreview = null,
    string? SellerInboxSubjectLabel = null,
    int SellerInboxSubjectTrust = 0);

public sealed record RouteTramoSubscriptionRejectedNotificationArgs(
    string CarrierUserId,
    string ThreadId,
    string MessagePreview,
    string SellerLabel,
    int SellerTrust,
    string SellerUserId,
    string? RouteOfferId);

public sealed record RouteTramoSellerExpelledNotificationArgs(
    string CarrierUserId,
    string ThreadId,
    string MessagePreview,
    string SellerLabel,
    int SellerTrust,
    string SellerUserId,
    string? RouteOfferId,
    string Reason);

public sealed record RouteTramoSubscriptionsBroadcastArgs(
    string ThreadId,
    string RouteSheetId,
    string Change,
    string ActorUserId,
    string? EmergentPublicationOfferId = null);

/// <summary>Quien editó la hoja indica un teléfono de transportista registrado: aviso in-app (no requiere estar en el hilo aún).</summary>
public sealed record RouteSheetPreselectedTransportistaNotificationArgs(
    string RecipientUserId,
    string ThreadId,
    string OfferId,
    string RouteSheetId,
    string MessagePreview,
    string AuthorLabel,
    int AuthorTrust,
    string SenderUserId);

public sealed record PostChatMessageArgs(
    string SenderUserId,
    string ThreadId,
    JsonElement Payload);

public sealed record PostAgreementAnnouncementArgs(
    string SellerUserId,
    string ThreadId,
    string AgreementId,
    string Title,
    string Status);

public sealed record UpdateChatMessageStatusArgs(
    string UserId,
    string ThreadId,
    string MessageId,
    ChatMessageStatus Status);

public sealed record PartySoftLeaveArgs(
    string UserId,
    string ThreadId,
    string Reason);
