namespace VibeTrade.Backend.Infrastructure.Email;

/// <summary>Opciones SMTP para notificaciones transaccionales (p. ej. informe de pago adjunto).</summary>
public sealed class EmailSmtpOptions
{
    public const string SectionName = "EmailSmtp";

    /// <summary>Si es false o falta <see cref="Host"/>, no se envía correo (no-op silencioso salvo logs de depuración).</summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    public string? User { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = "";

    public string? FromDisplayName { get; set; }

    /// <summary><see cref="FromAddress"/> si está definido; si no, <see cref="User"/> cuando parece un correo (p. ej. Gmail SMTP).</summary>
    public string EffectiveFromAddress
    {
        get
        {
            var from = (FromAddress ?? "").Trim();
            if (from.Length > 0)
                return from;
            var u = (User ?? "").Trim();
            return u.Contains('@', StringComparison.Ordinal) ? u : "";
        }
    }

    /// <summary>Puerto 587 típico con STARTTLS; 465 suele requerir SSL explícito (ajustar <see cref="UseSslOnConnect"/>).</summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>True para puerto 465 u otros servidores que exigen SSL al conectar.</summary>
    public bool UseSslOnConnect { get; set; }
}
