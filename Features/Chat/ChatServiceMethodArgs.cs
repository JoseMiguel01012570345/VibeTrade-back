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

/// <summary>Notificación de tramo aceptado; <c>MetaJson</c> opcional: <c>routeSheetId</c>, <c>carrierUserId</c>, <c>stops</c> [{ <c>stopId</c>, <c>storeServiceId</c> }].</summary>
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
    int SellerInboxSubjectTrust = 0,
    string? MetaJson = null);

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

/// <summary>Transportista del tramo siguiente: el tramo anterior está listo para entrega / handoff.</summary>
public sealed record RouteLegHandoffReadyNotificationArgs(
    string RecipientCarrierUserId,
    string ThreadId,
    string RouteSheetId,
    string AgreementId,
    string RouteStopId,
    string MessagePreview);

/// <summary>Participante del hilo: el transportista está cerca del fin del tramo (handoff próximo).</summary>
public sealed record RouteLegProximityNotificationArgs(
    string RecipientUserId,
    string ThreadId,
    string RouteSheetId,
    string AgreementId,
    string RouteStopId,
    string MessagePreview);

/// <summary>Quien editó la hoja indica un teléfono de transportista registrado: aviso in-app (no requiere estar en el hilo aún).</summary>
public sealed record RouteSheetPreselectedTransportistaNotificationArgs(
    string RecipientUserId,
    string ThreadId,
    string OfferId,
    string RouteSheetId,
    string MessagePreview,
    string AuthorLabel,
    int AuthorTrust,
    string SenderUserId,
    IReadOnlyList<string>? StopIds = null);

/// <summary>Vendedor del hilo: el transportista rechazó la invitación por contacto preseleccionado en la hoja.</summary>
public sealed record RouteSheetPreselDeclinedByCarrierNotificationArgs(
    string SellerUserId,
    string ThreadId,
    string OfferId,
    string RouteSheetId,
    string CarrierDisplayName,
    int CarrierTrustScore,
    string CarrierUserId,
    string MessagePreview);

/// <summary>Vendedor (dueño de la tienda): la confianza de la tienda bajó por reglas de hoja de ruta u operación (demo).</summary>
public sealed record SellerStoreTrustPenaltyNotificationArgs(
    string SellerUserId,
    string? ThreadId,
    string? OfferId,
    int Delta,
    int BalanceAfter,
    string MessagePreview);

public sealed record PostChatMessageArgs(
    string SenderUserId,
    string ThreadId,
    PostChatMessageBody Message);

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

/// <summary>Resultado de <see cref="IChatService.SoftLeaveThreadAsPartyAsync"/>.</summary>
public sealed record PartySoftLeaveResult(
    bool Success,
    string? ErrorCode,
    bool SkipClientTrustPenalty);
