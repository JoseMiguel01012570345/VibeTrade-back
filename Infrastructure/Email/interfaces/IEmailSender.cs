namespace VibeTrade.Backend.Infrastructure.Email.Interfaces;

public interface IEmailSender
{
    /// <summary>Envía el mensaje por SMTP si la configuración lo permite; devuelve false si está desactivado o falla de forma controlada.</summary>
    Task<bool> TrySendAsync(EmailSendRequest request, CancellationToken cancellationToken = default);
}
