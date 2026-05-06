using System.Text.Json.Serialization;

namespace VibeTrade.Backend.Features.Market.Catalog;

/// <summary>Adjunto en <see cref="StoreCustomFieldBody"/> (mismo contrato que en cliente).</summary>
public sealed record StoreCustomAttachmentBody
{
    public string Id { get; init; } = "";
    public string Url { get; init; } = "";
    public string FileName { get; init; } = "";
    /// <summary><c>image</c> | <c>pdf</c> | <c>other</c></summary>
    public string Kind { get; init; } = "";
}

/// <summary>Campo personalizado con texto y adjuntos (ficha de producto o servicio).</summary>
public sealed record StoreCustomFieldBody
{
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public string? AttachmentNote { get; init; }
    public IReadOnlyList<StoreCustomAttachmentBody>? Attachments { get; init; }
}

/// <summary><c>riesgos</c> en ficha de servicio.</summary>
public sealed record ServiceRiesgosBody
{
    public bool Enabled { get; init; }
    public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();
}

/// <summary><c>dependencias</c> en ficha de servicio.</summary>
public sealed record ServiceDependenciasBody
{
    public bool Enabled { get; init; }
    public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();
}

/// <summary><c>garantias</c> en ficha de servicio.</summary>
public sealed record ServiceGarantiasBody
{
    public bool Enabled { get; init; }

    [JsonPropertyName("texto")]
    public string Texto { get; init; } = "";
}
