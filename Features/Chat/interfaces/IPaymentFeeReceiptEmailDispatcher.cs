namespace VibeTrade.Backend.Features.Chat.Interfaces;

/// <summary>Envía por correo el PDF del informe de pago a participantes del hilo con email configurado.</summary>
public interface IPaymentFeeReceiptEmailDispatcher
{
    /// <summary>No lanza si SMTP falla; registra advertencias. Omite usuarios sin email.</summary>
    Task TryDispatchToThreadParticipantsAsync(
        string threadId,
        ChatPaymentFeeReceiptData payload,
        CancellationToken cancellationToken = default);
}
