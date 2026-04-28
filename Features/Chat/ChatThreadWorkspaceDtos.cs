using System.Text.Json.Serialization;
using VibeTrade.Backend.Data.RouteSheets;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Features.Chat;

/// <summary>Mensaje de hilo al shape del cliente (market store) sin nodos JSON.</summary>
public sealed class ChatThreadMessageView
{
    public string Id { get; set; } = "";
    public string From { get; set; } = "";
    public string Type { get; set; } = "";
    public long At { get; set; }
    public bool Read { get; set; }
    [JsonPropertyName("chatStatus")]
    public string? ChatStatus { get; set; }
    public string? Text { get; set; }
    public string? Url { get; set; }
    public int? Seconds { get; set; }
    [JsonPropertyName("offerQaId")]
    public string? OfferQaId { get; set; }
    public IReadOnlyList<ChatMessageImageView>? Images { get; set; }
    public string? Caption { get; set; }
    [JsonPropertyName("embeddedAudio")]
    public ChatMessageEmbeddedAudioView? EmbeddedAudio { get; set; }
    public string? Name { get; set; }
    public string? Size { get; set; }
    public string? Kind { get; set; }
    public IReadOnlyList<ChatMessageDocView>? Documents { get; set; }
    [JsonPropertyName("agreementId")]
    public string? AgreementId { get; set; }
    public string? Title { get; set; }
    [JsonPropertyName("replyQuotes")]
    public IReadOnlyList<ChatReplyQuoteView>? ReplyQuotes { get; set; }
}

public sealed class ChatMessageImageView
{
    public string Url { get; set; } = "";
}

public sealed class ChatMessageEmbeddedAudioView
{
    public string Url { get; set; } = "";
    public int Seconds { get; set; }
}

public sealed class ChatMessageDocView
{
    public string Name { get; set; } = "";
    public string Size { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Url { get; set; }
}

public sealed class ChatReplyQuoteView
{
    public string Id { get; set; } = "";
    public string Author { get; set; } = "";
    public string Preview { get; set; } = "";
}

/// <summary>Place-holder para <c>contracts[]</c> en hilo; actualmente no se pueblan entradas.</summary>
public sealed class ChatThreadContractView
{
}

/// <summary>Hilo de chat en <c>market.threads[threadId]</c>.</summary>
public sealed class ChatThreadWorkspaceDto
{
    public string Id { get; set; } = "";
    [JsonPropertyName("offerId")]
    public string OfferId { get; set; } = "";
    [JsonPropertyName("storeId")]
    public string StoreId { get; set; } = "";
    [JsonPropertyName("buyerUserId")]
    public string BuyerUserId { get; set; } = "";
    [JsonPropertyName("sellerUserId")]
    public string SellerUserId { get; set; } = "";
    public StoreProfileWorkspaceData? Store { get; set; }
    [JsonPropertyName("purchaseMode")]
    public bool PurchaseMode { get; set; }
    public IReadOnlyList<ChatThreadMessageView> Messages { get; set; } = Array.Empty<ChatThreadMessageView>();
    /// <summary>Reservado; el cliente envía hoy <c>[]</c> en hilos creados desde acá.</summary>
    public IReadOnlyList<ChatThreadContractView> Contracts { get; set; } = Array.Empty<ChatThreadContractView>();
    [JsonPropertyName("routeSheets")]
    public IReadOnlyList<RouteSheetPayload> RouteSheets { get; set; } = Array.Empty<RouteSheetPayload>();
    [JsonPropertyName("buyerDisplayName")]
    public string? BuyerDisplayName { get; set; }
    [JsonPropertyName("buyerAvatarUrl")]
    public string? BuyerAvatarUrl { get; set; }
    [JsonPropertyName("partyExitedUserId")]
    public string? PartyExitedUserId { get; set; }
    [JsonPropertyName("partyExitedReason")]
    public string? PartyExitedReason { get; set; }
    [JsonPropertyName("partyExitedAtUtc")]
    public DateTimeOffset? PartyExitedAtUtc { get; set; }
}
