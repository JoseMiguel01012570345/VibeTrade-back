namespace VibeTrade.Backend.Infrastructure.Email;

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

public sealed class EmailSendRequest
{
    public required string To { get; init; }

    public required string Subject { get; init; }

    public string? TextBody { get; init; }

    public string? HtmlBody { get; init; }

    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];
}
