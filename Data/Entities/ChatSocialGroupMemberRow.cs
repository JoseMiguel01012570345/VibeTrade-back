namespace VibeTrade.Backend.Data.Entities;

/// <summary>Participante adicional en un hilo <see cref="ChatThreadRow.IsSocialGroup"/> cuando hay más de dos personas (además de comprador y vendedor lógicos en la fila del hilo).</summary>
public sealed class ChatSocialGroupMemberRow
{
    public string Id { get; set; } = "";

    public string ThreadId { get; set; } = "";

    public string UserId { get; set; } = "";

    public DateTimeOffset JoinedAtUtc { get; set; }
}
