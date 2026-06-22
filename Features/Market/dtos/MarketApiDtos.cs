namespace VibeTrade.Backend.Features.Market.Dtos;

public sealed record CatalogCategoriesResponse(IReadOnlyList<string> Categories);

public sealed record CurrenciesResponse(IReadOnlyList<string> Currencies);

public sealed record StoreDetailBody(string? ViewerUserId, string? ViewerRole);

public sealed record PostInquiryAskedBy(string Id, string Name, int TrustScore);

public sealed record ToggleEngagementBody(string? GuestId);

/// <summary>Cuerpo para <c>POST /inquiries</c> (la API usa la sesión para <c>askedBy</c>).</summary>
/// <param name="OfferId">Id del producto o servicio (oferta).</param>
/// <param name="Question">Legado; preferí <paramref name="Text"/>.</param>
/// <param name="Text">Texto de la pregunta o comentario público.</param>
/// <param name="ParentId">Opcional: id del comentario padre (hilo tipo reels).</param>
/// <param name="AskedBy">En el DTO de cliente; el servidor puede sobreescribir con la sesión.</param>
/// <param name="CreatedAt">Epoch ms opcional (cliente).</param>
public sealed record PostInquiryBody(
    string OfferId,
    string? Question,
    string? Text,
    string? ParentId,
    PostInquiryAskedBy AskedBy,
    long? CreatedAt);
