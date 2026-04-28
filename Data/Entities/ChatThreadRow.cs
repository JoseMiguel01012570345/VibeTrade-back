namespace VibeTrade.Backend.Data.Entities;

/// <summary>Hilo de chat entre comprador y dueño de tienda para una oferta (producto/servicio).</summary>
public sealed class ChatThreadRow
{
    public string Id { get; set; } = "";

    /// <summary>Id de oferta = id de producto o servicio en catálogo.</summary>
    public string OfferId { get; set; } = "";

    public string StoreId { get; set; } = "";

    /// <summary>Usuario comprador (quien consulta por la oferta).</summary>
    public string BuyerUserId { get; set; } = "";

    /// <summary>Dueño de la tienda (vendedor).</summary>
    public string SellerUserId { get; set; } = "";

    /// <summary>Quien abrió el hilo primero; el otro participante no lo ve en listados hasta <see cref="FirstMessageSentAtUtc"/>.</summary>
    public string InitiatorUserId { get; set; } = "";

    /// <summary>UTC: primer mensaje enviado en el hilo (cualquier participante). Null hasta entonces.</summary>
    public DateTimeOffset? FirstMessageSentAtUtc { get; set; }

    /// <summary>
    /// True si el comprador abrió el flujo &quot;Comprar (chat)&quot; o envió mensajes de compra;
    /// false si el hilo solo nació de consultas (inquiry) desde la ficha.
    /// </summary>
    public bool PurchaseMode { get; set; } = true;

    /// <summary>UTC: creación del registro.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>UTC: borrado lógico; null = activo. No se retorna en listados ni mensajes.</summary>
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <summary>UTC: el comprador fue expulsado de este hilo; ya no puede acceder. Nuevo interés → nuevo hilo.</summary>
    public DateTimeOffset? BuyerExpelledAtUtc { get; set; }

    /// <summary>UTC: el vendedor fue expulsado de este hilo; ya no puede acceder.</summary>
    public DateTimeOffset? SellerExpelledAtUtc { get; set; }

    /// <summary>Usuario (comprador o vendedor) que salió notificando motivo con acuerdo aceptado.</summary>
    public string? PartyExitedUserId { get; set; }

    public string? PartyExitedReason { get; set; }

    public DateTimeOffset? PartyExitedAtUtc { get; set; }

    public ICollection<ChatMessageRow> Messages { get; set; } = new List<ChatMessageRow>();

    public ICollection<TradeAgreementRow> TradeAgreements { get; set; } = new List<TradeAgreementRow>();
}
