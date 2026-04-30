using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace VibeTrade.Backend.Infrastructure.Email;

public sealed class SmtpEmailSender(
    IOptionsSnapshot<EmailSmtpOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<bool> TrySendAsync(EmailSendRequest request, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        var fromAddress = o.EffectiveFromAddress;
        if (!o.Enabled || string.IsNullOrWhiteSpace(o.Host) || string.IsNullOrWhiteSpace(fromAddress))
            return false;

        var to = (request.To ?? "").Trim();
        if (to.Length < 3 || !to.Contains('@', StringComparison.Ordinal))
            return false;

        try
        {
            var message = new MimeMessage();
            var fromName = string.IsNullOrWhiteSpace(o.FromDisplayName) ? "VibeTrade" : o.FromDisplayName.Trim();
            message.From.Add(new MailboxAddress(fromName, fromAddress));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = request.Subject;

            var builder = new BodyBuilder();
            if (!string.IsNullOrEmpty(request.HtmlBody))
                builder.HtmlBody = request.HtmlBody;
            builder.TextBody = request.TextBody ?? "";

            foreach (var a in request.Attachments)
            {
                if (a.Content.Length == 0)
                    continue;
                var name = string.IsNullOrWhiteSpace(a.FileName) ? "adjunto.bin" : a.FileName.Trim();
                var ct = string.IsNullOrWhiteSpace(a.ContentType) ? "application/octet-stream" : a.ContentType.Trim();
                builder.Attachments.Add(name, new MemoryStream(a.Content), ContentType.Parse(ct));
            }

            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            var secure =
                o.UseSslOnConnect ? SecureSocketOptions.SslOnConnect :
                o.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
            await client.ConnectAsync(o.Host.Trim(), o.Port, secure, cancellationToken).ConfigureAwait(false);

            var user = o.User?.Trim();
            if (!string.IsNullOrEmpty(user))
                await client.AuthenticateAsync(user, o.Password ?? "", cancellationToken).ConfigureAwait(false);

            await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SMTP: no se pudo enviar correo a {To}", to);

            // Print each value of options
            Console.WriteLine("SMTP options:");
            Console.WriteLine($"Enabled: {options.Value.Enabled}");
            Console.WriteLine($"Host: {options.Value.Host}");
            Console.WriteLine($"Port: {options.Value.Port}");
            Console.WriteLine($"User: {options.Value.User}");
            Console.WriteLine($"Password: {options.Value.Password}");
            Console.WriteLine($"FromDisplayName: {options.Value.FromDisplayName}");
            Console.WriteLine($"EffectiveFromAddress: {options.Value.EffectiveFromAddress}");
            Console.WriteLine($"UseSslOnConnect: {options.Value.UseSslOnConnect}");
            Console.WriteLine($"UseStartTls: {options.Value.UseStartTls}");

            return false;
    
        }
    }
}
