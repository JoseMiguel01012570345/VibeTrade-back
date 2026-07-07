using MediatR;

namespace VibeTrade.Backend.Features.Shared.Contracts.Events;

public sealed record UserNotificationRequestedEvent(
    string UserId,
    string Title,
    string Body,
    string? ThreadId = null,
    string? DeepLink = null) : INotification;

public sealed record AgreementSignedEvent(
    string AgreementId,
    string BuyerUserId,
    string SellerUserId,
    string ThreadId) : INotification;

public sealed record PaymentConfirmedEvent(
    string PaymentId,
    string AgreementId,
    string ThreadId,
    string PayerUserId,
    string? RouteSheetId = null,
    IReadOnlyList<string>? PaidRouteStopIds = null) : INotification;

public sealed record DeliveryCompletedEvent(
    string AgreementId,
    string ThreadId,
    string CarrierUserId) : INotification;

public sealed record RouteSheetEditedEvent(
    string ThreadId,
    string SheetId,
    string EditorUserId) : INotification;
