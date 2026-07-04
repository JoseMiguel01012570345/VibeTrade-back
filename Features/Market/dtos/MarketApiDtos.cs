namespace VibeTrade.Backend.Features.Market.Dtos;

public sealed record CatalogCategoriesResponse(IReadOnlyList<string> Categories);

public sealed record CurrenciesResponse(IReadOnlyList<string> Currencies);

public sealed record StoreDetailBody(string? ViewerUserId, string? ViewerRole);

public sealed record ToggleEngagementBody(string? GuestId);

/// <summary>Cuerpo para <c>POST /stores/{storeId}/comments</c> (el autor se toma de la sesión).</summary>
/// <param name="Text">Texto del comentario o pregunta pública de la tienda.</param>
/// <param name="ParentId">Opcional: id del comentario padre (hilo de respuestas).</param>
/// <param name="CreatedAt">Epoch ms opcional (cliente).</param>
public sealed record StoreCommentPostBody(
    string? Text,
    string? ParentId,
    long? CreatedAt);
