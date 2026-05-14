using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Infrastructure.Email;
using VibeTrade.Backend.Infrastructure.Email.Interfaces;
using VibeTrade.Backend.Utils;

namespace VibeTrade.Backend.Features.Payments;

public sealed class PaymentFeeReceiptEmailDispatcher(
    AppDbContext db,
    IChatService chat,
    IEmailSender emailSender,
    IOptionsSnapshot<EmailSmtpOptions> smtpOptions,
    ILogger<PaymentFeeReceiptEmailDispatcher> logger) : IPaymentFeeReceiptEmailDispatcher
{
    public async Task TryDispatchToThreadParticipantsAsync(
        string threadId,
        ChatPaymentFeeReceiptData payload,
        CancellationToken cancellationToken = default)
    {
        var o = smtpOptions.Value;
        if (!o.Enabled || string.IsNullOrWhiteSpace(o.Host) || string.IsNullOrWhiteSpace(o.EffectiveFromAddress))
        {
            logger.LogWarning(
                "Payment receipt email skipped: EmailSmtp is not configured (Enabled={Enabled}, Host set={HasHost}, From set={HasFrom}). "
                + "Set EmailSmtp:FromAddress or User (mailbox email), plus Host; User/Password if required.",
                o.Enabled,
                !string.IsNullOrWhiteSpace(o.Host),
                !string.IsNullOrWhiteSpace(o.EffectiveFromAddress));
            return;
        }

        try
        {
            var tid = ChatThreadIds.NormalizePersistedId(threadId);
            if (tid.Length < 4)
                return;

            var participantIds = await chat.GetThreadParticipantUserIdsAsync(tid, cancellationToken)
                .ConfigureAwait(false);
            if (participantIds.Count == 0)
                return;

            var ids = participantIds.Select(x => x.Trim()).Where(x => x.Length > 1).Distinct().ToList();
            var accounts = await db.UserAccounts.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.Email })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            byte[] pdf;
            try
            {
                pdf = PaymentFeeReceiptPdfBuilder.Build(payload);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo generar el PDF del informe de pago para el hilo {ThreadId}", tid);
                return;
            }

            var safePayment = string.Join("_",
                (payload.PaymentId ?? "pago").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            if (safePayment.Length == 0)
                safePayment = "pago";
            var fileName = $"InformePago_{safePayment}.pdf";

            var title = (payload.AgreementTitle ?? "").Trim();
            if (title.Length == 0)
                title = "Acuerdo";

            var subject = $"VibeTrade — Informe de pago: {title}";
            var textBody =
                "Hola,\n\n"
                + "Se registró un pago en un chat de VibeTrade. Adjuntamos el informe de pago en PDF "
                + "(desglose, tarifa Stripe según liquidación y enlace a precios Stripe).\n\n"
                + $"Acuerdo: {title}\n"
                + $"Moneda: {(payload.CurrencyLower ?? "").Trim().ToUpperInvariant()}\n\n"
                + "Este mensaje se envía a los participantes del chat que tienen un correo en su cuenta. "
                + "Si no configuraste email, solo verás el recibo en la app.\n\n"
                + "— VibeTrade\n";

            foreach (var a in accounts)
            {
                var email = (a.Email ?? "").Trim();
                if (email.Length < 5 || !email.Contains('@', StringComparison.Ordinal))
                {
                    logger.LogInformation(
                        "Informe de pago por correo: usuario {UserId} sin email; no se envía.",
                        a.Id);
                    continue;
                }

                var ok = await emailSender.TrySendAsync(
                    new EmailSendRequest
                    {
                        To = email,
                        Subject = subject,
                        TextBody = textBody,
                        Attachments =
                        [
                            new EmailAttachment(fileName, "application/pdf", pdf),
                        ],
                    },
                    cancellationToken).ConfigureAwait(false);

                if (ok)
                {
                    logger.LogInformation(
                        "Informe de pago enviado por correo a {Email} (usuario {UserId}, hilo {ThreadId}).",
                        email,
                        a.Id,
                        tid);
                }
                else
                {
                    logger.LogWarning(
                        "Payment receipt email failed for {Email} (user {UserId}, thread {ThreadId}); see earlier SMTP log for the error.",
                        email,
                        a.Id,
                        tid);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Fallo al intentar enviar informes de pago por correo para el hilo {ThreadId}; el pago no se revierte.",
                threadId);
        }
    }
}
