using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Chat.Core;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Features.Notifications.NotificationInterfaces;

/// <summary>Mensajes de sistema / acuerdos / recibos publicados en hilos (validación + inserción).</summary>
public interface IChatThreadSystemMessageService
{
    Task<ChatMessageDto?> PostAgreementAnnouncementAsync(
        PostAgreementAnnouncementArgs request,
        CancellationToken cancellationToken = default);

    Task<ChatMessageDto?> PostSystemThreadNoticeAsync(
        string actorUserId,
        string threadId,
        string text,
        CancellationToken cancellationToken = default);

    Task<ChatMessageDto?> PostAutomatedSystemThreadNoticeAsync(
        string threadId,
        string text,
        CancellationToken cancellationToken = default);

    Task<ChatMessageDto?> PostAutomatedPaymentFeeReceiptAsync(
        string threadId,
        ChatPaymentFeeReceiptData payload,
        CancellationToken cancellationToken = default);
}
