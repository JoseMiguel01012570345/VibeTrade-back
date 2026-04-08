namespace VibeTrade.Backend.Features.Market;

/// <summary>Ya existe otra tienda con el mismo nombre normalizado (plataforma).</summary>
public sealed class DuplicateStoreNameException(string? normalizedName) : InvalidOperationException(
    "Ya existe una tienda con ese nombre en la plataforma.")
{
    public string? NormalizedName { get; } = normalizedName;
}
